using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Web.WebView2.Core;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MDviewer;

// VIEW의 실제 DOM/계산된 스타일을 사용한다. 편집 원문이나 앱 UI는 내보내지 않는다.
static class WordExport
{
    internal const string CaptureScript = """
    (() => {
      const root=document.querySelector('article.md');
      if(!root) throw new Error('VIEW document unavailable');
      function style(el) {
        const s=getComputedStyle(el);
        let bg=s.backgroundColor, p=el;
        while((bg==='rgba(0, 0, 0, 0)' || bg==='transparent') && p.parentElement){p=p.parentElement;bg=getComputedStyle(p).backgroundColor;}
        return {font:s.fontFamily,size:parseFloat(s.fontSize),color:s.color,background:bg,
          bold:parseInt(s.fontWeight)>=600,italic:s.fontStyle==='italic',decoration:s.textDecorationLine,
          align:s.textAlign,vertical:s.verticalAlign,whiteSpace:s.whiteSpace,
          lineHeight:parseFloat(s.lineHeight)||parseFloat(s.fontSize)*1.6,borderColor:s.borderTopColor,
          marginTop:parseFloat(s.marginTop)||0,marginBottom:parseFloat(s.marginBottom)||0};
      }
      function visit(n) {
        if(n.nodeType===3) return {tag:'#text',text:n.textContent,style:style(n.parentElement)};
        if(n.nodeType!==1) return null;
        if(n.matches('script,style,button,.code-header,.line-num,.footnote-backref,.katex-mathml'))return null;
        const s=getComputedStyle(n);
        if(s.display==='none'||s.visibility==='hidden')return null;
        let tag=n.tagName.toLowerCase(),st=style(n);
        if(tag==='img' || n.classList.contains('katex')) {
          const r=n.getBoundingClientRect();
          if(tag==='img' && !n.naturalWidth)return {tag:'#text',text:n.alt||'',style:st};
          return {tag:'image',text:n.alt||n.textContent,style:st,x:r.x+scrollX,y:r.y+scrollY,width:r.width,height:r.height};
        }
        if(tag==='input')return n.type==='checkbox'?{tag:'#text',text:n.checked?'☑ ':'☐ ',style:st}:null;
        if(tag==='mark' && n.classList.contains('md-find-hl'))st=style(n.parentElement);
        const children=Array.from(n.childNodes).map(visit).filter(Boolean);
        if(n.classList.contains('code-line'))children.push({tag:'br',style:st});
        let prefix='';
        if(tag==='li') {
          const list=n.parentElement;
          const index=Array.from(list.children).filter(c=>c.tagName==='LI').indexOf(n);
          prefix=list.tagName==='OL'?String((parseInt(list.getAttribute('start'))||1)+index)+'. ':'• ';
          if(n.querySelector(':scope > input[type=checkbox],:scope > p > input[type=checkbox]'))prefix='';
        }
        return {tag,style:st,children,prefix,href:tag==='a'?n.getAttribute('href')||'':'',colSpan:n.colSpan||1};
      }
      return visit(root);
    })()
    """;

    internal sealed class ViewStyle
    {
        public string Font { get; set; } = "Malgun Gothic";
        public double Size { get; set; } = 15.5;
        public string Color { get; set; } = "rgb(42,42,42)";
        public string Background { get; set; } = "";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string Decoration { get; set; } = "";
        public string Align { get; set; } = "left";
        public string Vertical { get; set; } = "";
        public string WhiteSpace { get; set; } = "normal";
        public double LineHeight { get; set; } = 24.8;
        public string BorderColor { get; set; } = "";
        public double MarginTop { get; set; }
        public double MarginBottom { get; set; }
    }
    internal sealed class ViewNode
    {
        public string Tag { get; set; } = "";
        public string Text { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string Href { get; set; } = "";
        public ViewStyle Style { get; set; } = new();
        public List<ViewNode> Children { get; set; } = new();
        public int ColSpan { get; set; } = 1;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public byte[]? Image { get; set; }
    }

    public static async Task SaveViewAsync(CoreWebView2 web, string destPath)
    {
        var json = await web.ExecuteScriptAsync(CaptureScript);
        var root = JsonSerializer.Deserialize<ViewNode>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("VIEW 문서를 읽을 수 없습니다.");
        if(root.Tag != "article") throw new InvalidOperationException("VIEW 본문이 준비되지 않았습니다.");
        foreach(var node in Descendants(root).Where(n=>n.Tag=="image" && n.Width>0 && n.Height>0))
        {
            // 이미지/수식은 브라우저가 표시한 모습으로 내장해 외부 파일 의존성을 없앤다.
            var parameters = JsonSerializer.Serialize(new { format="png", captureBeyondViewport=true,
                clip=new { x=Math.Max(0,node.X), y=Math.Max(0,node.Y), width=node.Width, height=node.Height, scale=1 } });
            using var capture = JsonDocument.Parse(await web.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", parameters));
            node.Image = Convert.FromBase64String(capture.RootElement.GetProperty("data").GetString()!);
        }
        // 변환 실패 시 사용자의 기존 파일은 보존한다.
        var temporary = destPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            SaveDocument(root, temporary);
            File.Move(temporary, destPath, true);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }

    static IEnumerable<ViewNode> Descendants(ViewNode node)
    {
        yield return node;
        foreach(var child in node.Children)
            foreach(var descendant in Descendants(child)) yield return descendant;
    }

    internal static void SaveDocument(ViewNode root, string destPath)
    {
        using var doc = WordprocessingDocument.Create(destPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body());
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new W.Styles(new W.DocDefaults(
            new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                new W.RunFonts { Ascii=FontName(root.Style.Font), HighAnsi=FontName(root.Style.Font), EastAsia=FontName(root.Style.Font) },
                new W.FontSize { Val="23" })),
            new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(new W.SpacingBetweenLines { After="120", Line="320", LineRule=W.LineSpacingRuleValues.Auto }))));
        for(int level=1;level<=6;level++)
            styles.Styles.Append(new W.Style(new W.StyleName { Val="heading "+level },
                new W.StyleParagraphProperties(new W.KeepNext(),new W.OutlineLevel { Val=level-1 }))
                { Type=W.StyleValues.Paragraph,StyleId="Heading"+level });
        var writer = new Writer(main);
        writer.Blocks(main.Document.Body!, root.Children);
        main.Document.Body!.Append(new W.SectionProperties(
            new W.PageSize { Width=11906, Height=16838 },
            new W.PageMargin { Top=850,Bottom=850,Left=850,Right=850,Header=400,Footer=400,Gutter=0 }));
        styles.Styles.Save();
        main.Document.Save();
    }

    static string FontName(string value)
    {
        var name = value.Split(',')[0].Trim(' ', '\'', '"');
        return name == "SogangUni" ? Brand.Family?.Name ?? "Malgun Gothic" : name;
    }
    static string? Hex(string color)
    {
        var values=Regex.Matches(color,@"[\d.]+");
        if(values.Count<3 || (values.Count==4 && values[3].Value=="0"))return null;
        return string.Concat(values.Take(3).Select(v=>Math.Clamp((int)double.Parse(v.Value,CultureInfo.InvariantCulture),0,255).ToString("X2")));
    }
    static bool IsBlock(ViewNode n) => n.Tag is "p" or "div" or "blockquote" or "pre" or "ul" or "ol" or "li" or "table" or "figure" or "figcaption" or "hr" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "dl" or "dt" or "dd";

    sealed class Writer(MainDocumentPart main)
    {
        uint imageId;
        public void Blocks(OpenXmlCompositeElement parent, IEnumerable<ViewNode> nodes, int depth=0, string prefix="")
        {
            var inline=new List<ViewNode>();
            void Flush()
            {
                if(inline.Count==0)return;
                if(inline.Any(n=>n.Tag!="#text"||!string.IsNullOrWhiteSpace(n.Text)))
                {
                    Paragraph(parent,inline,inline[0].Style,depth,prefix);
                    prefix="";
                }
                inline.Clear();
            }
            foreach(var n in nodes)
            {
                if(!IsBlock(n)){inline.Add(n);continue;}
                Flush();
                switch(n.Tag)
                {
                    case "table": Table(parent,n,depth); break;
                    case "ul": case "ol":
                        foreach(var li in n.Children.Where(c=>c.Tag=="li")) Blocks(parent,li.Children,depth+1,li.Prefix);
                        break;
                    case "p": case "pre": case "figcaption": case "dt": case "dd":
                    case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                        Paragraph(parent,n.Children,n.Style,depth,prefix,n.Tag); prefix=""; break;
                    case "hr":
                        parent.Append(new W.Paragraph(new W.ParagraphProperties(new W.ParagraphBorders(new W.BottomBorder { Val=W.BorderValues.Single, Color=Hex(n.Style.Color)??"AAAAAA", Size=6 })))); break;
                    default: Blocks(parent,n.Children,depth+(n.Tag=="blockquote"?1:0),prefix);prefix="";break;
                }
            }
            Flush();
        }
        void Paragraph(OpenXmlCompositeElement parent,IEnumerable<ViewNode> nodes,ViewStyle s,int depth,string prefix,string tag="p")
        {
            var props=new W.ParagraphProperties();
            if(tag.Length==2 && tag[0]=='h' && char.IsDigit(tag[1])) props.ParagraphStyleId=new W.ParagraphStyleId { Val="Heading"+tag[1] };
            props.SpacingBetweenLines=new W.SpacingBetweenLines { Before=Math.Round(s.MarginTop*15).ToString(CultureInfo.InvariantCulture), After=Math.Round(s.MarginBottom*15).ToString(CultureInfo.InvariantCulture), Line=Math.Max(1,(int)Math.Round(s.LineHeight/Math.Max(1,s.Size)*240)).ToString(), LineRule=W.LineSpacingRuleValues.Auto };
            if(depth>0)props.Indentation=new W.Indentation { Left=(depth*360).ToString(),Hanging=prefix.Length>0?"240":"0" };
            props.Justification=new W.Justification { Val=s.Align=="center"?W.JustificationValues.Center:s.Align=="right"?W.JustificationValues.Right:W.JustificationValues.Left };
            if(Hex(s.Background) is string background)props.Shading=new W.Shading { Val=W.ShadingPatternValues.Clear,Fill=background };
            var p=new W.Paragraph(props);
            if(prefix.Length>0)p.Append(TextRun(prefix,s));
            foreach(var n in nodes) Inline(p,n,tag=="pre");
            // A code-line ends with a break; paragraphs already have their own final newline.
            if(p.LastChild is W.Run last && last.LastChild is W.Break)last.LastChild.Remove();
            parent.Append(p);
        }
        W.Run TextRun(string text,ViewStyle s)
        {
            var props=new W.RunProperties();
            props.RunFonts=new W.RunFonts { Ascii=FontName(s.Font),HighAnsi=FontName(s.Font),EastAsia=FontName(s.Font),ComplexScript=FontName(s.Font) };
            if(s.Bold)props.Bold=new W.Bold();
            if(s.Italic)props.Italic=new W.Italic();
            if(s.Decoration.Contains("line-through"))props.Strike=new W.Strike();
            if(Hex(s.Color) is string color)props.Color=new W.Color { Val=color };
            props.FontSize=new W.FontSize { Val=Math.Max(2,(int)Math.Round(s.Size*1.5)).ToString() };
            if(s.Decoration.Contains("underline"))props.Underline=new W.Underline { Val=W.UnderlineValues.Single };
            if(Hex(s.Background) is string bg)props.Shading=new W.Shading { Val=W.ShadingPatternValues.Clear,Fill=bg };
            if(s.Vertical is "super" or "sub")props.VerticalTextAlignment=new W.VerticalTextAlignment { Val=s.Vertical=="super"?W.VerticalPositionValues.Superscript:W.VerticalPositionValues.Subscript };
            var run=new W.Run(props);
            var lines=text.Replace("\r","").Split('\n');
            for(int i=0;i<lines.Length;i++)
            {
                if(i>0)run.Append(new W.Break());
                run.Append(new W.Text(lines[i]) { Space=SpaceProcessingModeValues.Preserve });
            }
            return run;
        }
        void Inline(OpenXmlCompositeElement parent,ViewNode n,bool pre)
        {
            if(n.Tag=="#text") { parent.Append(TextRun(pre||n.Style.WhiteSpace.StartsWith("pre")?n.Text:Regex.Replace(n.Text,@"\s+"," "),n.Style));return; }
            if(n.Tag=="br"){parent.Append(new W.Run(new W.Break()));return;}
            if(n.Tag=="image") { Image(parent,n); return; }
            if(n.Tag=="a" && Uri.TryCreate(n.Href,UriKind.Absolute,out var url) && url.Scheme is "https" or "http" or "mailto")
            {
                var link=new W.Hyperlink { Id=main.AddHyperlinkRelationship(url,true).Id };
                foreach(var child in n.Children)Inline(link,child,pre);
                parent.Append(link);return;
            }
            foreach(var child in n.Children)Inline(parent,child,pre);
        }
        void Image(OpenXmlCompositeElement parent,ViewNode n)
        {
            if(n.Image==null){parent.Append(TextRun(n.Text,n.Style));return;}
            var part=main.AddImagePart(ImagePartType.Png);
            using(var stream=new MemoryStream(n.Image))part.FeedData(stream);
            double scale=Math.Min(1,Math.Min(680/Math.Max(1,n.Width),990/Math.Max(1,n.Height)));
            long cx=(long)(n.Width*9525*scale),cy=(long)(n.Height*9525*scale);
            uint id=++imageId;
            var pic=new PIC.Picture(
                new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id=id,Name="VIEW "+id,Description=n.Text },new PIC.NonVisualPictureDrawingProperties()),
                new PIC.BlipFill(new A.Blip { Embed=main.GetIdOfPart(part) },new A.Stretch(new A.FillRectangle())),
                new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X=0,Y=0 },new A.Extents { Cx=cx,Cy=cy }),new A.PresetGeometry(new A.AdjustValueList()) { Preset=A.ShapeTypeValues.Rectangle }));
            parent.Append(new W.Run(new W.Drawing(new DW.Inline(new DW.Extent { Cx=cx,Cy=cy },new DW.DocProperties { Id=id,Name="VIEW "+id },
                new A.Graphic(new A.GraphicData(pic) { Uri="http://schemas.openxmlformats.org/drawingml/2006/picture" })))));
        }
        void Table(OpenXmlCompositeElement parent,ViewNode node,int depth)
        {
            var rows=node.Children.SelectMany(c=>c.Tag=="tr"?new[]{c}:c.Children.Where(r=>r.Tag=="tr")).ToList();
            if(rows.Count==0)return;
            int cols=rows.Max(r=>r.Children.Where(c=>c.Tag is "th" or "td").Sum(c=>c.ColSpan));
            string border=Hex(rows[0].Children.FirstOrDefault(c=>c.Tag is "th" or "td")?.Style.BorderColor??"")??"B1B3B6";
            var table=new W.Table(new W.TableProperties(new W.TableWidth { Type=W.TableWidthUnitValues.Pct,Width="5000" },
                new W.TableBorders(new W.TopBorder { Val=W.BorderValues.Single,Size=4,Color=border },new W.LeftBorder { Val=W.BorderValues.Single,Size=4,Color=border },
                    new W.BottomBorder { Val=W.BorderValues.Single,Size=4,Color=border },new W.RightBorder { Val=W.BorderValues.Single,Size=4,Color=border },
                    new W.InsideHorizontalBorder { Val=W.BorderValues.Single,Size=4,Color=border },new W.InsideVerticalBorder { Val=W.BorderValues.Single,Size=4,Color=border })));
            table.Append(new W.TableGrid(Enumerable.Range(0,cols).Select(_=>new W.GridColumn { Width=(10200/Math.Max(1,cols)).ToString() })));
            foreach(var row in rows)
            {
                var tr=new W.TableRow();
                if(row.Children.Any(c=>c.Tag=="th"))tr.Append(new W.TableRowProperties(new W.TableHeader()));
                foreach(var cell in row.Children.Where(c=>c.Tag is "th" or "td"))
                {
                    var cp=new W.TableCellProperties();
                    if(cell.ColSpan>1)cp.GridSpan=new W.GridSpan { Val=cell.ColSpan };
                    if(Hex(cell.Style.Background) is string bg)cp.Shading=new W.Shading { Val=W.ShadingPatternValues.Clear,Fill=bg };
                    var tc=new W.TableCell(cp);
                    Blocks(tc,cell.Children);
                    if(tc.LastChild is not W.Paragraph)tc.Append(new W.Paragraph());
                    tr.Append(tc);
                }
                table.Append(tr);
            }
            parent.Append(table);
        }
    }
}
