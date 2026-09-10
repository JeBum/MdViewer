using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdTable = Markdig.Extensions.Tables.Table;
using MdRow = Markdig.Extensions.Tables.TableRow;
using MdCell = Markdig.Extensions.Tables.TableCell;
using WDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using WBody = DocumentFormat.OpenXml.Wordprocessing.Body;
using WParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WText = DocumentFormat.OpenXml.Wordprocessing.Text;
using WBreak = DocumentFormat.OpenXml.Wordprocessing.Break;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using WTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using WTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using WTableGrid = DocumentFormat.OpenXml.Wordprocessing.TableGrid;
using WGridColumn = DocumentFormat.OpenXml.Wordprocessing.GridColumn;
using WTableProperties = DocumentFormat.OpenXml.Wordprocessing.TableProperties;
using WTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;
using WTopBorder = DocumentFormat.OpenXml.Wordprocessing.TopBorder;
using WBottomBorder = DocumentFormat.OpenXml.Wordprocessing.BottomBorder;
using WLeftBorder = DocumentFormat.OpenXml.Wordprocessing.LeftBorder;
using WRightBorder = DocumentFormat.OpenXml.Wordprocessing.RightBorder;
using WInsideH = DocumentFormat.OpenXml.Wordprocessing.InsideHorizontalBorder;
using WInsideV = DocumentFormat.OpenXml.Wordprocessing.InsideVerticalBorder;
using WBorderValues = DocumentFormat.OpenXml.Wordprocessing.BorderValues;
using WParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using WParagraphStyleId = DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId;
using WRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using WBold = DocumentFormat.OpenXml.Wordprocessing.Bold;
using WItalic = DocumentFormat.OpenXml.Wordprocessing.Italic;
using WRunFonts = DocumentFormat.OpenXml.Wordprocessing.RunFonts;
using WSpace = DocumentFormat.OpenXml.SpaceProcessingModeValues;

namespace MDviewer;

static class WordExport
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static void Save(string markdown, string destPath)
    {
        var md = Markdown.Parse(markdown ?? "", Pipeline);
        using var doc = WordprocessingDocument.Create(destPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new WDocument(new WBody());
        var body = main.Document.Body!;

        foreach (var block in md)
            WriteBlock(body, block);

        main.Document.Save();
    }

    static void WriteBlock(WBody body, Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
                body.Append(Para(Inlines(h.Inline), HeadingStyle(h.Level)));
                break;
            case ParagraphBlock p:
                body.Append(Para(Inlines(p.Inline), "Normal"));
                break;
            case QuoteBlock q:
                foreach (var child in q)
                {
                    if (child is ParagraphBlock qp)
                        body.Append(Para(Inlines(qp.Inline), "Quote"));
                    else
                        WriteBlock(body, child);
                }
                break;
            case FencedCodeBlock code:
            case CodeBlock:
                var text = (block as LeafBlock)?.Lines.ToString() ?? "";
                body.Append(Para(new[] { RunText(text, mono: true) }, "Normal"));
                break;
            case ListBlock list:
                var num = 0;
                foreach (var item in list)
                {
                    num++;
                    if (item is not ListItemBlock li) continue;
                    foreach (var child in li)
                    {
                        if (child is ParagraphBlock ip)
                        {
                            var prefix = list.IsOrdered ? num + ". " : "• ";
                            var runs = new List<WRun> { RunText(prefix) };
                            runs.AddRange(Inlines(ip.Inline));
                            body.Append(Para(runs, "Normal"));
                        }
                        else WriteBlock(body, child);
                    }
                }
                break;
            case MdTable table:
                WriteTable(body, table);
                break;
            case ThematicBreakBlock:
                body.Append(Para(new[] { RunText("────────────────") }, "Normal"));
                break;
        }
    }

    static void WriteTable(WBody body, MdTable table)
    {
        var rows = table.Count;
        if (rows == 0) return;
        var cols = table.Max(r => r is MdRow tr ? tr.Count : 0);
        var wt = new WTable();
        var grid = new WTableGrid();
        for (int c = 0; c < cols; c++)
            grid.Append(new WGridColumn());
        wt.Append(new WTableProperties(
            new WTableBorders(
                new WTopBorder { Val = WBorderValues.Single, Size = 4 },
                new WBottomBorder { Val = WBorderValues.Single, Size = 4 },
                new WLeftBorder { Val = WBorderValues.Single, Size = 4 },
                new WRightBorder { Val = WBorderValues.Single, Size = 4 },
                new WInsideH { Val = WBorderValues.Single, Size = 4 },
                new WInsideV { Val = WBorderValues.Single, Size = 4 })));
        wt.Append(grid);

        foreach (var block in table)
        {
            if (block is not MdRow row) continue;
            var wr = new WTableRow();
            foreach (var cell in row)
            {
                var tc = new WTableCell();
                if (cell is MdCell mdCell)
                {
                    var inner = new List<WRun>();
                    foreach (var cb in mdCell)
                    {
                        if (cb is ParagraphBlock p)
                            inner.AddRange(Inlines(p.Inline));
                    }
                    tc.Append(Para(inner, "Normal"));
                }
                else tc.Append(new WParagraph());
                wr.Append(tc);
            }
            wt.Append(wr);
        }
        body.Append(wt);
    }

    static WParagraph Para(IEnumerable<WRun> runs, string style)
    {
        var p = new WParagraph(new WParagraphProperties(new WParagraphStyleId { Val = style }));
        foreach (var r in runs) p.Append(r);
        if (!p.Elements<WRun>().Any())
            p.Append(new WRun(new WText("")));
        return p;
    }

    static string HeadingStyle(int level) => level switch
    {
        1 => "Heading1",
        2 => "Heading2",
        3 => "Heading3",
        _ => "Heading4"
    };

    static List<WRun> Inlines(ContainerInline? inline)
    {
        var runs = new List<WRun>();
        if (inline == null) return runs;
        Collect(inline, runs, bold: false, italic: false);
        return runs;
    }

    static void Collect(ContainerInline container, List<WRun> runs, bool bold, bool italic)
    {
        foreach (var child in container)
        {
            switch (child)
            {
                case LiteralInline lit:
                    runs.Add(RunText(lit.Content.ToString(), bold, italic));
                    break;
                case EmphasisInline em:
                    var b = bold || em.DelimiterCount >= 2;
                    var i = italic || em.DelimiterCount == 1;
                    Collect(em, runs, b, i);
                    break;
                case CodeInline code:
                    runs.Add(RunText(code.Content, mono: true));
                    break;
                case LinkInline link:
                    if (link.IsImage)
                        runs.Add(RunText(link.Title ?? link.Url ?? ""));
                    else
                        Collect(link, runs, bold, italic);
                    break;
                case LineBreakInline:
                    runs.Add(new WRun(new WBreak()));
                    break;
                case ContainerInline nested:
                    Collect(nested, runs, bold, italic);
                    break;
            }
        }
    }

    static WRun RunText(string text, bool bold = false, bool italic = false, bool mono = false)
    {
        var run = new WRun();
        var rp = new WRunProperties();
        if (bold) rp.Append(new WBold());
        if (italic) rp.Append(new WItalic());
        if (mono) rp.Append(new WRunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
        run.Append(rp);
        var t = new WText(text ?? "") { Space = WSpace.Preserve };
        run.Append(t);
        return run;
    }
}
