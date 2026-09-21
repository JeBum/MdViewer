// 의무사항: 소스 변경 시 요구사항을 소스 주석에 기록하고, 테스트 케이스를 추가하며, 작업 완료 후 release.bat을 실행한다.
// ==============================================================================================
// [MDviewer 개발 및 유지보수 필수 작업 규칙 (Development Workflow Rules)]
// 1. 사용자 요구사항(기능 추가/수정) 발생 시 소스 코드(Program.cs 등)에 요건 및 주석 명확히 반영
// 2. 테스트 케이스는 verification/TEST-CASES.md 및 verification/Checks.cs.txt에 기록. test-sample.md는 사용자의 MD 형식 샘플이므로 수정하지 않음
// 3. 작업 완료 후 항상 build.bat을 실행하여 단일 실행 파일(publish\MDviewer.exe) 빌드 및 검증 완료
// 4. 변경 이력은 History.md에 일자별로 상세 기록 유지
// ==============================================================================================

using System.Diagnostics;
using System.Drawing.Text;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MDviewer;

enum SplitMode { None, Vertical, Horizontal }
enum AppTheme { Sogang, Albatross, BlueSky, ForestGreen }
enum ColumnMode { Single = 1, Two = 2, Three = 3, Auto = 0 }

static class Program
{
    const string MutexName = "MDviewer_SingleInstance_Mutex_2026";
    const string PipeName = "MDviewer_SingleInstance_Pipe_2026";

    [STAThread]
    static void Main(string[] args)
    {
        bool isNew;
        using var mutex = new Mutex(true, MutexName, out isNew);

        if (!isNew)
        {
            SendArgsToRunningInstance(args);
            return;
        }

        ApplicationConfiguration.Initialize();
        Brand.Init();

        using var cts = new CancellationTokenSource();
        MainForm? form = null;

        StartPipeServer(cts.Token, () => form);

        try
        {
            form = new MainForm(args);
            Application.Run(form);
        }
        finally
        {
            cts.Cancel();
        }
    }

    static void SendArgsToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            foreach (var a in args)
            {
                if (!string.IsNullOrWhiteSpace(a))
                    writer.WriteLine(Path.GetFullPath(a));
            }
        }
        catch { }
    }

    static void StartPipeServer(CancellationToken token, Func<MainForm?> getForm)
    {
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var files = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            files.Add(line);
                    }

                    var form = getForm();
                    if (form != null)
                    {
                        if (form.IsHandleCreated)
                        {
                            form.BeginInvoke(() =>
                            {
                                foreach (var f in files)
                                {
                                    if (File.Exists(f) && MainForm.IsSupportedDocument(f))
                                        form.OpenFile(f);
                                }
                                form.BringToForeground();
                            });
                        }
                        else
                        {
                            form.HandleCreated += (_, _) =>
                            {
                                foreach (var f in files)
                                {
                                    if (File.Exists(f) && MainForm.IsSupportedDocument(f))
                                        form.OpenFile(f);
                                }
                                form.BringToForeground();
                            };
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
            }
        }, token);
    }
}

sealed class MainForm : Form, IMessageFilter
{
    readonly Panel _banner = new() { Dock = DockStyle.Top, Height = 53 };
    readonly Panel _footBand = new() { Dock = DockStyle.Bottom, Height = 53 };
    readonly Panel _home = new() { Dock = DockStyle.Fill, BackColor = Brand.Wash };
    readonly Panel _homeThemeLine = new() { Dock = DockStyle.Top, Height = 2, BackColor = Brand.FrameAccent };
    readonly PictureBox _campus = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
    readonly PictureBox _bird = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    readonly PictureBox _logoFooter = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    readonly Label _intro = new();
    readonly Label _sampleLink = new();
    readonly Label _githubLink = new();
    readonly Label _associationLink = new();
    readonly Label _help = new();
    readonly Label _helpTitle = new();
    readonly Label _helpMouse = new();
    readonly Label _helpMouseTitle = new();
    readonly ContextMenuStrip _menu = new();
    ToolStripMenuItem? _miSave;
    ToolStripMenuItem? _miDocx;
    ToolStripMenuItem? _miClose;
    ToolStripMenuItem? _miCloseAll;
    ToolStripMenuItem? _miCloseOthers;
    ToolStripMenuItem? _miFind;
    ToolStripMenuItem? _miHighlight;
    ToolStripMenuItem? _miEdit;
    ToolStripMenuItem? _miVert;
    ToolStripMenuItem? _miHorz;
    ToolStripMenuItem? _miSogang;
    ToolStripMenuItem? _miAlba;
    ToolStripMenuItem? _miBlueSky;
    ToolStripMenuItem? _miForestGreen;
    ToolStripMenuItem? _miCol1;
    ToolStripMenuItem? _miCol2;
    ToolStripMenuItem? _miCol3;
    ToolStripMenuItem? _miColAuto;
    bool _menuBusy;
    readonly TabControl _tabs = new()
    {
        Dock = DockStyle.Fill,
        Alignment = TabAlignment.Top,
        Padding = new Point(18, 8),
        DrawMode = TabDrawMode.OwnerDrawFixed,
        ItemSize = new Size(148, 30),
        SizeMode = TabSizeMode.Normal,
        BackColor = Brand.Wash
    };

    public MainForm(string[] args)
    {
        Text = "MDviewer";
        Width = 1100;
        Height = 780;
        MinimumSize = new Size(520, 340);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        BackColor = Brand.Wash;
        KeyPreview = true;
        var ic = Brand.AppIcon();
        if (ic != null) Icon = ic;
        _tabs.AllowDrop = true;
        _tabs.DrawItem += DrawTab;
        BuildHome();
        BuildMenu();
        ApplyTheme();
        ContextMenuStrip = _menu;
        _tabs.ContextMenuStrip = _menu;
        _home.ContextMenuStrip = _menu;
        Controls.Add(_tabs);
        Controls.Add(_home);
        Controls.Add(_banner);
        Controls.Add(_footBand);
        Controls.Add(_homeThemeLine);
        BindDrop(this);
        BindDrop(_tabs);
        BindDrop(_home);

        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.N) NewFile();
            if (e.Control && e.KeyCode == Keys.O) OpenDialog();
            if (e.Control && e.KeyCode == Keys.W) CloseCurrent();
            if (e.Control && e.KeyCode == Keys.S && !e.Shift) Current?.Save();
            if (e.Control && e.Shift && e.KeyCode == Keys.S) Current?.SaveDocx();
            if (e.KeyCode == Keys.F5) Current?.Reload();
            if (e.Control && e.KeyCode == Keys.F && !e.Shift && !e.Alt)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Current?.ToggleFindBar();
                return;
            }
            if (e.KeyCode == Keys.F3 && !e.Control && !e.Alt)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Current?.FindNext(!e.Shift);
                return;
            }
            if (!e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.F8)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ToggleEditMode();
                return;
            }
            if ((e.Control || e.Shift) && e.KeyCode == Keys.F8)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Current?.ToggleSelectedHighlight();
                return;
            }
        };

        _tabs.SelectedIndexChanged += (_, _) =>
        {
            _tabs.Invalidate();
            SyncTitle();
        };
        _tabs.MouseDown += OnTabMouseDown;
        _tabs.MouseUp += OnTabMouseUp;
        ShowHome();

        Load += (_, _) =>
        {
            ApplyHomeFonts();
            LayoutHome();
            Application.AddMessageFilter(this);
            Native.DragAcceptFiles(Handle, true);
            Native.AcceptTree(Handle);
            foreach (var a in args)
                if (File.Exists(a) && IsSupportedDocument(a)) OpenFile(a);
            SyncTitle();
        };
        Shown += (_, _) =>
        {
            ApplyHomeFonts();
            LayoutHome();
        };
        FormClosed += (_, _) => Application.RemoveMessageFilter(this);
    }

    void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _tabs.TabCount) return;
        var page = _tabs.TabPages[e.Index];
        bool on = e.Index == _tabs.SelectedIndex;
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var r = e.Bounds;
        using (var bg = new SolidBrush(on ? Brand.Paper : Brand.Wash))
            g.FillRectangle(bg, r);
        // 탭 아래 공통 테마선과 겹치지 않도록 선택 탭은 글꼴과 색상으로 구분한다.
        var font = Brand.Ui(9f, on ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(
            g, page.Text, font,
            new Rectangle(r.X + 6, r.Y, r.Width - 12, r.Height - 2),
            on ? Brand.Wine : Brand.Mute,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        font.Dispose();
    }

    public bool PreFilterMessage(ref Message m)
    {
        const int WmMouseWheel = 0x020A;
        const int WmMouseHWheel = 0x020E;
        const int WmContextMenu = 0x007B;
        const int WmLButtonDown = 0x0201;
        const int WmNCLButtonDown = 0x00A1;
        const int WmRButtonDown = 0x0204;
        const int WmNCRButtonDown = 0x00A4;
        const int WmMButtonDown = 0x0207;
        const int WmNCMButtonDown = 0x00A7;

        if (_menu.Visible)
        {
            if (m.Msg == WmLButtonDown || m.Msg == WmNCLButtonDown ||
                m.Msg == WmRButtonDown || m.Msg == WmNCRButtonDown ||
                m.Msg == WmMButtonDown || m.Msg == WmNCMButtonDown)
            {
                CloseMenuIfOutside(Cursor.Position);
            }
        }

        if (m.Msg == WmContextMenu && Current != null)
        {
            ShowDocMenu(this);
            return true;
        }
        if (m.Msg != WmMouseWheel && m.Msg != WmMouseHWheel) return false;
        long wp = m.WParam.ToInt64();
        int keys = (int)(wp & 0xFFFF);
        bool zoomCtrl = (keys & 0x0008) != 0 || (ModifierKeys & Keys.Control) != 0;
        if (!zoomCtrl) return false;
        bool shift = (ModifierKeys & Keys.Shift) != 0 || (keys & 0x0004) != 0;
        int delta = unchecked((short)((wp >> 16) & 0xFFFF));
        Current?.HandleMouseWheelZoom(delta > 0 ? 0.08 : -0.08, shift, Cursor.Position);
        return true;
    }

    // 하위 메뉴는 상위 메뉴 바깥에 별도 창으로 열린다. 열린 메뉴 전체를 검사한다.
    void CloseMenuIfOutside(Point screenPoint)
    {
        if (_menu.Visible && !ContainsMenuPoint(_menu, screenPoint)) _menu.Close();
    }

    static bool ContainsMenuPoint(ToolStripDropDown menu, Point screenPoint)
    {
        if (!menu.Visible) return false;
        if (menu.Bounds.Contains(screenPoint)) return true;
        foreach (ToolStripMenuItem item in menu.Items.OfType<ToolStripMenuItem>())
            if (item.HasDropDownItems && ContainsMenuPoint(item.DropDown, screenPoint)) return true;
        return false;
    }

    void BindDrop(Control c)
    {
        c.AllowDrop = true;
        c.DragEnter -= OnDrag;
        c.DragOver -= OnDrag;
        c.DragDrop -= OnDrop;
        c.DragEnter += OnDrag;
        c.DragOver += OnDrag;
        c.DragDrop += OnDrop;
    }

    void OnDrag(object? sender, DragEventArgs e) =>
        e.Effect = FirstMd(e) != null ? DragDropEffects.Copy : DragDropEffects.None;

    void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            foreach (var f in files.Where(IsSupportedDocument)) OpenFile(f);
    }

    FileTab? Current => _tabs.SelectedTab?.Tag as FileTab;

    public List<FileTab> GetAllTabs() =>
        _tabs.TabPages.Cast<TabPage>().Select(p => p.Tag as FileTab).Where(t => t != null).ToList()!;

    public void HideAllFindBars()
    {
        foreach (var tab in GetAllTabs())
        {
            tab.HideFindBar(false);
        }
    }

    public int GetTabIndex(FileTab tab)
    {
        for (int i = 0; i < _tabs.TabCount; i++)
            if (_tabs.TabPages[i].Tag == tab) return i;
        return -1;
    }

    public void SelectTabByIndex(int index)
    {
        if (index >= 0 && index < _tabs.TabCount)
            _tabs.SelectedIndex = index;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000010;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0233)
        {
            var n = Native.DropCount(m.WParam);
            for (uint i = 0; i < n; i++)
            {
                var path = Native.DroppedFile(m.WParam, i);
                if (path != null && IsSupportedDocument(path)) OpenFile(path);
            }
            Native.DragFinish(m.WParam);
            return;
        }
        base.WndProc(ref m);
    }

    void BuildHome()
    {
        var footer = Brand.Logo();
        if (footer != null)
        {
            _logoFooter.Image = footer;
            _logoFooter.Size = new Size(footer.Width, footer.Height);
        }
        var buildDate = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "BuildDate").Value;
        _intro.Text = "26.09.10 : 데이터 엔지니어링 프로그래밍 수업 md file viewer\n" +
            $"last build : {buildDate}";
        _intro.AutoSize = true;
        _intro.Location = new Point(28, 10);
        _intro.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _intro.BackColor = Color.Transparent;
        _sampleLink.Text = "test-sample.md 열기";
        _sampleLink.AutoSize = true;
        _sampleLink.Cursor = Cursors.Hand;
        _sampleLink.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _sampleLink.BackColor = Color.Transparent;
        _sampleLink.UseCompatibleTextRendering = true;
        _sampleLink.Click += (_, _) => OpenTestSample();
        _githubLink.Text = "GitHub 저장소";
        _githubLink.AutoSize = true;
        _githubLink.Cursor = Cursors.Hand;
        _githubLink.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _githubLink.BackColor = Color.Transparent;
        _githubLink.UseCompatibleTextRendering = true;
        _githubLink.Tag = "https://github.com/JeBum/MdViewer";
        _githubLink.Click += (_, _) => FileTab.OpenExternalUrl(_githubLink.Tag as string);
        _associationLink.Text = "md 파일 연결프로그램 등록";
        _associationLink.AutoSize = true;
        _associationLink.Cursor = Cursors.Hand;
        _associationLink.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _associationLink.BackColor = Color.Transparent;
        _associationLink.UseCompatibleTextRendering = true;
        _associationLink.Click += (_, _) => RegisterMdAssociation();
        _helpTitle.Text = "단축키";
        _helpTitle.AutoSize = true;
        _helpTitle.BackColor = Color.Transparent;
        _helpTitle.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _helpTitle.UseCompatibleTextRendering = true;
        _helpTitle.Font = Brand.Sogang(10f, FontStyle.Bold);
        _helpTitle.ForeColor = Brand.Ink;
        _help.Text =
            "- Ctrl + N  새 문서\n" +
            "- Ctrl + O  열기\n" +
            "- Ctrl + S  저장\n" +
            "- Ctrl + Shift + S  Word로 저장\n" +
            "- Ctrl + W  탭 닫기\n" +
            "- Ctrl + F  단어 검색\n" +
            "- Ctrl + F8  단어 강조 추가/삭제\n" +
            "- F8  편집 모드 On/Off\n" +
            "- F5  다시 읽기\n" +
            "- Ctrl + 휠  확대/축소";
        _help.AutoSize = true;
        _help.BackColor = Color.Transparent;
        _help.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _help.UseCompatibleTextRendering = true;
        _help.Font = Brand.Sogang(8f);
        _help.ForeColor = Brand.Ink;
        _helpMouseTitle.Text = "마우스";
        _helpMouseTitle.AutoSize = true;
        _helpMouseTitle.BackColor = Color.Transparent;
        _helpMouseTitle.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _helpMouseTitle.UseCompatibleTextRendering = true;
        _helpMouseTitle.Font = Brand.Sogang(10f, FontStyle.Bold);
        _helpMouseTitle.ForeColor = Brand.Ink;
        _helpMouse.Text =
            "- .md 끌어다 놓기  새 탭\n" +
            "- 오른쪽 클릭  메뉴\n" +
            "- 편집 / 분할 / 테마\n" +
            "- 탭 제목 클릭  문서 전환\n" +
            "- 탭 휠클릭  탭 닫기";
        _helpMouse.AutoSize = true;
        _helpMouse.BackColor = Color.Transparent;
        _helpMouse.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _helpMouse.UseCompatibleTextRendering = true;
        _helpMouse.Font = Brand.Sogang(8f);
        _helpMouse.ForeColor = Brand.Ink;
        _home.Resize += (_, _) => LayoutHome();
        _home.Controls.Add(_intro);
        _home.Controls.Add(_sampleLink);
        _home.Controls.Add(_githubLink);
        _home.Controls.Add(_associationLink);
        _home.Controls.Add(_helpTitle);
        _home.Controls.Add(_help);
        _home.Controls.Add(_helpMouseTitle);
        _home.Controls.Add(_helpMouse);
        _home.Controls.Add(_logoFooter);
        _home.Controls.Add(_bird);
        _home.Controls.Add(_campus);
        _campus.SendToBack();
        _home.ContextMenuStrip = _menu;
        _campus.ContextMenuStrip = _menu;
        _bird.ContextMenuStrip = _menu;
        _help.ContextMenuStrip = _menu;
        _helpMouse.ContextMenuStrip = _menu;
        ApplyHomeFonts();
    }

    void ApplyHomeFonts()
    {
        _intro.UseCompatibleTextRendering = true;
        _sampleLink.UseCompatibleTextRendering = true;
        _githubLink.UseCompatibleTextRendering = true;
        _associationLink.UseCompatibleTextRendering = true;
        _helpTitle.UseCompatibleTextRendering = true;
        _help.UseCompatibleTextRendering = true;
        _helpMouseTitle.UseCompatibleTextRendering = true;
        _helpMouse.UseCompatibleTextRendering = true;
        bool sg = Brand.UsesSogangFont;
        Font F(float em, FontStyle st = FontStyle.Regular) =>
            sg ? Brand.Sogang(em, st) : new Font("Segoe UI", em, st, GraphicsUnit.Point);
        _intro.Font = F(10f);
        _sampleLink.Font = F(10f, FontStyle.Underline);
        _githubLink.Font = F(10f, FontStyle.Underline);
        _associationLink.Font = F(10f, FontStyle.Underline);
        _helpTitle.Font = F(10f, FontStyle.Bold);
        _help.Font = F(8f);
        _helpMouseTitle.Font = F(10f, FontStyle.Bold);
        _helpMouse.Font = F(8f);
        _intro.ForeColor = Brand.Ink;
        _sampleLink.ForeColor = Brand.Cardinal;
        _githubLink.ForeColor = Brand.Cardinal;
        _associationLink.ForeColor = Brand.Cardinal;
        _helpTitle.ForeColor = Brand.Ink;
        _help.ForeColor = Brand.Ink;
        _helpMouseTitle.ForeColor = Brand.Ink;
        _helpMouse.ForeColor = Brand.Ink;
    }

    void LayoutHome()
    {
        int w = Math.Max(80, _home.ClientSize.Width);
        int h = Math.Max(80, _home.ClientSize.Height);
        int campW = (int)(w * 0.90);
        int campH = (int)(h * 0.58);
        _campus.Size = new Size(campW, campH);
        _campus.Location = new Point((w - campW) / 2, h - campH - 124);
        int birdW = Math.Min(120, (int)(w * 0.11));
        int birdH = Math.Max(28, (int)(birdW * 0.32));
        _bird.Size = new Size(birdW, birdH);
        _bird.Location = new Point(
            w / 2,
            _campus.Top + (int)(campH * 0.28));
        _logoFooter.Location = new Point(w - _logoFooter.Width - 24, h - _logoFooter.Height - 20);
        _sampleLink.Location = new Point(_intro.Left, _intro.Bottom + _intro.Font.Height);
        _githubLink.Location = new Point(_sampleLink.Left, _sampleLink.Bottom + 10);
        _associationLink.Location = new Point(_sampleLink.Left, _githubLink.Bottom + 10);
        int blockH = _helpTitle.Height + 2 + Math.Max(_help.Height, _helpMouse.Height);
        int helpY = Math.Max(80, h - blockH - 8);
        _helpTitle.Location = new Point(28, helpY);
        _help.Location = new Point(28, helpY + _helpTitle.Height + 2);
        int mouseX = Math.Max(_helpTitle.Right, _help.Right) + 48;
        _helpMouseTitle.Location = new Point(mouseX, helpY);
        _helpMouse.Location = new Point(mouseX, helpY + _helpMouseTitle.Height + 2);
        _bird.BringToFront();
        _intro.BringToFront();
        _sampleLink.BringToFront();
        _githubLink.BringToFront();
        _associationLink.BringToFront();
        _helpTitle.BringToFront();
        _help.BringToFront();
        _helpMouseTitle.BringToFront();
        _helpMouse.BringToFront();
        _logoFooter.BringToFront();
    }

    void OpenTestSample()
    {
        try
        {
            // 단일 파일 배포에서도 추출 임시 폴더가 아닌 실행 파일의 폴더를 사용한다.
            OpenFile(EnsureTestSampleFile(AppContext.BaseDirectory));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "샘플 문서를 열 수 없습니다.\n" + ex.Message,
                "test-sample.md 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static string EnsureTestSampleFile(string directory)
    {
        var path = System.IO.Path.Combine(directory, "test-sample.md");
        if (File.Exists(path)) return path;
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("MDviewer.test-sample.md")
            ?? throw new InvalidOperationException("내장 샘플 문서를 찾을 수 없습니다.");
        var temporary = System.IO.Path.Combine(directory, ".test-sample-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                source.CopyTo(output);
            // 클릭 도중 다른 프로세스가 파일을 만들었어도 기존 파일을 덮어쓰지 않는다.
            try { File.Move(temporary, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { }
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    void RegisterMdAssociation()
    {
        try
        {
            var exe = Environment.ProcessPath ?? Application.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                throw new InvalidOperationException("실행 파일 경로를 찾지 못했습니다.\npublish\\MDviewer.exe로 실행하세요.");
            exe = System.IO.Path.GetFullPath(exe);
            const string progId = "MDviewer.markdown";
            string cmd = "\"" + exe + "\" \"%1\"";
            string icon = "\"" + exe + "\",0";

            void Put(string path, string name, object value)
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path);
                k.SetValue(name, value);
            }

            Put(@"Software\Classes\.md", "", progId);
            Put(@"Software\Classes\.md\OpenWithProgids", progId, "");
            Put(@"Software\Classes\.markdown", "", progId);
            Put(@"Software\Classes\.markdown\OpenWithProgids", progId, "");
            Put(@"Software\Classes\" + progId, "", "Markdown Document");
            Put(@"Software\Classes\" + progId + @"\DefaultIcon", "", icon);
            Put(@"Software\Classes\" + progId + @"\shell", "", "open");
            Put(@"Software\Classes\" + progId + @"\shell\open", "", "MDviewer로 열기");
            Put(@"Software\Classes\" + progId + @"\shell\open\command", "", cmd);
            Put(@"Software\Classes\Applications\MDviewer.exe\shell\open\command", "", cmd);
            Put(@"Software\MDviewer\Capabilities", "ApplicationName", "MDviewer");
            Put(@"Software\MDviewer\Capabilities", "ApplicationDescription", "Markdown viewer");
            Put(@"Software\MDviewer\Capabilities\FileAssociations", ".md", progId);
            Put(@"Software\MDviewer\Capabilities\FileAssociations", ".markdown", progId);
            Put(@"Software\RegisteredApplications", "MDviewer", @"Software\MDviewer\Capabilities");
            Put(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\OpenWithProgids", progId, "");
            Put(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.markdown\OpenWithProgids", progId, "");

            Native.NotifyAssocChanged();
            using var dlg = new AssocGuideDialog();
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "연결 등록 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    void BuildMenu()
    {
        _menu.Renderer = new SogangMenuRenderer();
        _menu.Padding = new Padding(2, 2, 2, 2);
        _menu.ImageScalingSize = new Size(12, 12);
        _menu.Font = Brand.MenuFont;
        _menu.FontChanged += (_, _) => ApplyMenuFont();
        _menu.Items.Add("New", null, (_, _) => NewFile());
        _menu.Items.Add("Open", null, (_, _) => OpenDialog());
        _miSave = new ToolStripMenuItem("Save", null, (_, _) => Current?.Save());
        _miDocx = new ToolStripMenuItem("Word로 저장", null, (_, _) => Current?.SaveDocx());
        _menu.Items.Add(_miSave);
        _menu.Items.Add(_miDocx);
        _menu.Items.Add(new ToolStripSeparator());
        _miClose = new ToolStripMenuItem("닫기", null, (_, _) => CloseCurrent());
        _miCloseAll = new ToolStripMenuItem("전체 닫기", null, (_, _) => CloseAll());
        _miCloseOthers = new ToolStripMenuItem("다른 문서 닫기", null, (_, _) =>
        {
            if (_tabs.SelectedTab != null) CloseOthers(_tabs.SelectedTab);
        });
        _menu.Items.Add(_miClose);
        _menu.Items.Add(_miCloseAll);
        _menu.Items.Add(_miCloseOthers);
        _menu.Items.Add(new ToolStripSeparator());
        _miFind = new ToolStripMenuItem("검색 (Ctrl+F)", null, (_, _) => Current?.ToggleFindBar());
        _miHighlight = new ToolStripMenuItem("강조 키워드 (Ctrl+F8)") { CheckOnClick = true };
        _miHighlight.CheckedChanged += (_, _) =>
        {
            if (_menuBusy || Current == null) return;
            Current.HighlightBarVisible = _miHighlight.Checked;
        };
        _menu.Items.Add(_miFind);
        _menu.Items.Add(_miHighlight);
        _menu.Items.Add(new ToolStripSeparator());
        _miEdit = new ToolStripMenuItem("편집 모드 (F8)") { CheckOnClick = true };
        _miVert = new ToolStripMenuItem("세로 분할") { CheckOnClick = true };
        _miHorz = new ToolStripMenuItem("가로 분할") { CheckOnClick = true };
        _miEdit.CheckedChanged += (_, _) =>
        {
            if (_menuBusy || Current == null) return;
            Current.EditMode = _miEdit.Checked;
            if (_miEdit.Checked && Current.Split == SplitMode.None)
                Current.Split = SplitMode.Vertical;
            _menuBusy = true;
            try
            {
                _miVert.Checked = Current.Split == SplitMode.Vertical;
                _miHorz.Checked = Current.Split == SplitMode.Horizontal;
            }
            finally { _menuBusy = false; }
            SyncTitle();
        };
        _miVert.CheckedChanged += (_, _) =>
        {
            if (_menuBusy || Current == null) return;
            Current.Split = _miVert.Checked ? SplitMode.Vertical : SplitMode.None;
        };
        _miHorz.CheckedChanged += (_, _) =>
        {
            if (_menuBusy || Current == null) return;
            Current.Split = _miHorz.Checked ? SplitMode.Horizontal : SplitMode.None;
        };
        _menu.Items.Add(_miEdit);
        _menu.Items.Add(_miVert);
        _menu.Items.Add(_miHorz);
        _menu.Items.Add(new ToolStripSeparator());
        _miSogang = new ToolStripMenuItem("서강") { CheckOnClick = true };
        _miAlba = new ToolStripMenuItem("알바트로스") { CheckOnClick = true };
        _miBlueSky = new ToolStripMenuItem("블루스카이") { CheckOnClick = true };
        _miForestGreen = new ToolStripMenuItem("포레스트 그린") { CheckOnClick = true };
        _miSogang.CheckedChanged += (_, _) =>
        {
            if (_menuBusy) return;
            SetTheme(AppTheme.Sogang);
        };
        _miAlba.CheckedChanged += (_, _) =>
        {
            if (_menuBusy) return;
            SetTheme(AppTheme.Albatross);
        };
        _miBlueSky.CheckedChanged += (_, _) =>
        {
            if (_menuBusy) return;
            SetTheme(AppTheme.BlueSky);
        };
        _miForestGreen.CheckedChanged += (_, _) =>
        {
            if (_menuBusy) return;
            SetTheme(AppTheme.ForestGreen);
        };
        var themeMenu = new ToolStripMenuItem("테마");
        themeMenu.DropDownItems.AddRange(new ToolStripItem[] { _miSogang, _miAlba, _miBlueSky, _miForestGreen });
        _menu.Items.Add(themeMenu);

        _miCol1 = new ToolStripMenuItem("1단 (기본)") { CheckOnClick = true };
        _miCol2 = new ToolStripMenuItem("2단") { CheckOnClick = true };
        _miCol3 = new ToolStripMenuItem("3단") { CheckOnClick = true };
        _miColAuto = new ToolStripMenuItem("자동 (너비 맞춤)") { CheckOnClick = true };
        _miCol1.CheckedChanged += (_, _) =>
        {
            if (_menuBusy) return;
            SetColumnMode(ColumnMode.Single);
        };
        _miCol2.CheckedChanged += (_, _) =>
        {
            if (_menuBusy) return;
            SetColumnMode(ColumnMode.Two);
        };
        _miCol3.CheckedChanged += (_, _) =>
        {
            if (_menuBusy) return;
            SetColumnMode(ColumnMode.Three);
        };
        _miColAuto.CheckedChanged += (_, _) =>
        {
            if (_menuBusy) return;
            SetColumnMode(ColumnMode.Auto);
        };
        var columnMenu = new ToolStripMenuItem("다단 보기");
        columnMenu.DropDownItems.AddRange(new ToolStripItem[] { _miCol1, _miCol2, _miCol3, _miColAuto });
        _menu.Items.Add(columnMenu);

        SyncThemeMenu();
        SyncColumnMenu();
        ApplyMenuFont();
        SizeMenu();
        _menu.Opening += (_, _) =>
        {
            ApplyMenuFont();
            SizeMenu();
            _menuBusy = true;
            var has = Current != null;
            if (_miSave != null) _miSave.Enabled = has;
            if (_miDocx != null) _miDocx.Enabled = has;
            if (_miClose != null) _miClose.Enabled = has;
            if (_miCloseAll != null) _miCloseAll.Enabled = _tabs.TabCount > 0;
            if (_miCloseOthers != null) _miCloseOthers.Enabled = has && _tabs.TabCount > 1;
            if (_miFind != null) _miFind.Enabled = has;
            if (_miHighlight != null)
            {
                _miHighlight.Enabled = has;
                if (has) _miHighlight.Checked = Current!.HighlightBarVisible;
            }
            if (_miEdit != null) _miEdit.Enabled = has;
            if (_miVert != null) _miVert.Enabled = has;
            if (_miHorz != null) _miHorz.Enabled = has;
            if (has)
            {
                if (_miEdit != null) _miEdit.Checked = Current!.EditMode;
                if (_miVert != null) _miVert.Checked = Current!.Split == SplitMode.Vertical;
                if (_miHorz != null) _miHorz.Checked = Current!.Split == SplitMode.Horizontal;
            }
            SyncThemeMenu();
            SyncColumnMenu();
            _menuBusy = false;
        };
    }

    public void ToggleEditMode()
    {
        if (Current == null) return;
        _menuBusy = true;
        try
        {
            if (!Current.EditMode)
            {
                Current.EditMode = true;
                Current.Split = SplitMode.Vertical;
            }
            else
            {
                Current.EditMode = false;
                Current.Split = SplitMode.None;
            }
            if (_miEdit != null) _miEdit.Checked = Current.EditMode;
            if (_miVert != null) _miVert.Checked = Current.Split == SplitMode.Vertical;
            if (_miHorz != null) _miHorz.Checked = Current.Split == SplitMode.Horizontal;
            SyncTitle();
        }
        finally
        {
            _menuBusy = false;
        }
    }

    public void EnterViewMode()
    {
        if (Current == null) return;
        _menuBusy = true;
        try
        {
            Current.EditMode = false;
            if (_miEdit != null) _miEdit.Checked = false;
            if (_miVert != null) _miVert.Checked = false;
            if (_miHorz != null) _miHorz.Checked = false;
            SyncTitle();
        }
        finally
        {
            _menuBusy = false;
        }
    }

    void SetTheme(AppTheme theme)
    {
        if (Brand.Theme != theme)
        {
            Brand.Theme = theme;
            Brand.SaveTheme();
            ApplyTheme();
        }
        SyncThemeMenu();
    }

    void SyncThemeMenu()
    {
        var busy = _menuBusy;
        _menuBusy = true;
        try
        {
            if (_miSogang != null) _miSogang.Checked = Brand.Theme == AppTheme.Sogang;
            if (_miAlba != null) _miAlba.Checked = Brand.Theme == AppTheme.Albatross;
            if (_miBlueSky != null) _miBlueSky.Checked = Brand.Theme == AppTheme.BlueSky;
            if (_miForestGreen != null) _miForestGreen.Checked = Brand.Theme == AppTheme.ForestGreen;
        }
        finally { _menuBusy = busy; }
    }

    void SyncColumnMenu()
    {
        if (_miCol1 == null || _miCol2 == null || _miCol3 == null || _miColAuto == null) return;
        var busy = _menuBusy;
        _menuBusy = true;
        try
        {
            _miCol1.Checked = Brand.ColumnViewMode == ColumnMode.Single;
            _miCol2.Checked = Brand.ColumnViewMode == ColumnMode.Two;
            _miCol3.Checked = Brand.ColumnViewMode == ColumnMode.Three;
            _miColAuto.Checked = Brand.ColumnViewMode == ColumnMode.Auto;
        }
        finally { _menuBusy = busy; }
    }

    void SetColumnMode(ColumnMode mode)
    {
        if (Brand.ColumnViewMode != mode)
        {
            Brand.ColumnViewMode = mode;
            foreach (var tab in GetAllTabs())
                tab.ApplyColumnMode();
        }
        SyncColumnMenu();
    }

    void ApplyTheme()
    {
        BackColor = Brand.Wash;
        _home.BackColor = Brand.Wash;
        _homeThemeLine.BackColor = Brand.FrameAccent;
        _tabs.BackColor = Brand.Wash;
        bool sg = Brand.Theme == AppTheme.Sogang;
        _banner.Visible = false;
        _footBand.Visible = true;
        _footBand.BackColor = Brand.FrameAccent;
        _footBand.Height = _tabs.TabPages.Count == 0 ? 53 : 5;
        ApplyHomeFonts();
        _campus.Image = Brand.ResImage("campus_light.png");
        _bird.Image = Brand.ResImage("albatross.png");
        if (_logoFooter.Image == null)
        {
            var footer = Brand.Logo();
            if (footer != null)
            {
                _logoFooter.Image = footer;
                _logoFooter.Size = new Size(footer.Width, footer.Height);
            }
        }
        _logoFooter.Visible = true;
        LayoutHome();
        _tabs.Invalidate();
        foreach (TabPage p in _tabs.TabPages)
            if (p.Tag is FileTab tab) tab.ApplyChrome();
        ApplyMenuFont();
        SizeMenu();
    }

    bool _menuFontLock;

    void ApplyMenuFont()
    {
        if (_menuFontLock) return;
        _menuFontLock = true;
        try
        {
            var f = Brand.MenuFont;
            _menu.Font = f;
            void ApplyItems(ToolStripItemCollection items)
            {
                foreach (ToolStripItem item in items)
                {
                    item.Font = f;
                    if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
                    {
                        menuItem.DropDown.Font = f;
                        menuItem.DropDown.Renderer = _menu.Renderer;
                        ApplyItems(menuItem.DropDownItems);
                    }
                }
            }
            ApplyItems(_menu.Items);
        }
        finally { _menuFontLock = false; }
    }

    void SizeMenu()
    {
        var f = Brand.MenuFont;
        int max = 80;
        foreach (ToolStripItem it in _menu.Items)
        {
            if (string.IsNullOrEmpty(it.Text)) continue;
            var sz = TextRenderer.MeasureText(it.Text, f, new Size(1000, 40),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (sz.Width > max) max = sz.Width;
        }
        int inner = max + 88;
        foreach (ToolStripItem it in _menu.Items)
        {
            if (it is ToolStripSeparator)
            {
                it.AutoSize = true;
                continue;
            }
            it.AutoSize = false;
            it.Width = inner;
            it.Height = 26;
            it.Padding = new Padding(4, 0, 16, 0);
        }
        _menu.ShowCheckMargin = true;
        _menu.AutoSize = false;
        _menu.Width = inner + 28;
    }

    public bool IsMenuVisible => _menu.Visible;
    public void CloseMenu() => _menu.Close();

    public void ShowDocMenu(Control c)
    {
        _menu.Show(c, c.PointToClient(Cursor.Position));
    }

    void ShowHome()
    {
        bool empty = _tabs.TabPages.Count == 0;
        _home.Visible = empty;
        _homeThemeLine.Visible = empty;
        _home.BringToFront();
        if (!empty) _tabs.BringToFront();
        _footBand.Height = empty ? 53 : 5;
    }

    void NewFile()
    {
        AddTab(new FileTab(null));
    }

    void OnTabMouseDown(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            if (_tabs.GetTabRect(i).Contains(e.Location))
            {
                _tabs.SelectedTab = _tabs.TabPages[i];
                SyncTitle();
                break;
            }
        }
    }

    void OnTabMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Middle) return;
        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            if (_tabs.GetTabRect(i).Contains(e.Location))
            {
                CloseTab(_tabs.TabPages[i]);
                return;
            }
        }
    }

    void OpenDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Markdown (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|JSON (*.json;*.jsonc)|*.json;*.jsonc|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in dlg.FileNames) OpenFile(f);
    }

    public void OpenFile(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        foreach (TabPage page in _tabs.TabPages)
        {
            if (page.Tag is FileTab t && string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                _tabs.SelectedTab = page;
                t.Reload();
                SyncTitle();
                return;
            }
        }

        AddTab(new FileTab(path));
    }

    void AddTab(FileTab tab)
    {
        tab.DirtyChanged += SyncTitle;
        tab.OpenRequested += OpenFile;
        tab.AttachMenu(_menu);
        var pageNew = new TabPage(tab.Title) { Tag = tab, AllowDrop = true };
        tab.HostPage = pageNew;
        BindDrop(pageNew);
        BindDrop(tab);
        pageNew.Controls.Add(tab);
        _tabs.TabPages.Add(pageNew);
        _tabs.SelectedTab = pageNew;
        _tabs.Visible = true;
        ShowHome();
        SyncTitle();
    }

    void CloseCurrent()
    {
        if (_tabs.SelectedTab != null) CloseTab(_tabs.SelectedTab);
    }

    void CloseOthers(TabPage keep)
    {
        var others = _tabs.TabPages.Cast<TabPage>().Where(p => p != keep).ToList();
        foreach (var p in others) CloseTab(p);
    }

    void CloseAll()
    {
        foreach (var p in _tabs.TabPages.Cast<TabPage>().ToList()) CloseTab(p);
    }

    void CloseTab(TabPage page)
    {
        if (page.Tag is FileTab t)
        {
            if (!t.ConfirmClose()) return;
            t.DisposeWatch();
        }
        var i = _tabs.TabPages.IndexOf(page);
        _tabs.TabPages.Remove(page);
        page.Dispose();
        if (_tabs.TabPages.Count > 0)
            _tabs.SelectedIndex = Math.Min(i, _tabs.TabPages.Count - 1);
        ShowHome();
        SyncTitle();
    }

    void SyncTitle()
    {
        if (Current == null)
        {
            Text = "MDviewer";
            return;
        }
        var name = Current.Title + (Current.Dirty ? "*" : "");
        Text = "MDviewer - [" + name + "]";
        if (Current.HostPage != null)
            Current.HostPage.Text = name;
    }

    public void BringToForeground()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Native.ShowWindow(Handle, 9);
        Native.SetForegroundWindow(Handle);
        Activate();
        BringToFront();
    }

    public static bool IsMd(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    public static bool IsJson(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedDocument(string path) => IsMd(path) || IsJson(path);

    public static bool IsMdPublic(string path) => IsMd(path);

    public static string? FirstMd(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return null;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return null;
        return files.FirstOrDefault(IsSupportedDocument);
    }
}

sealed class AssocGuideDialog : Form
{
    public AssocGuideDialog()
    {
        Text = "MDviewer - .md 연결 프로그램 등록 안내";
        Width = 590;
        Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Brand.Paper;
        Font = Brand.Ui(9f);

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 95, Padding = new Padding(20, 16, 20, 8), BackColor = Brand.Wash };
        var lblTitle = new Label
        {
            Text = "MDviewer를 .md 연결 프로그램으로 등록했습니다.",
            Dock = DockStyle.Top,
            Height = 26,
            Font = Brand.Ui(11f, FontStyle.Bold),
            ForeColor = Brand.Theme == AppTheme.Sogang ? Brand.Cardinal : Color.FromArgb(20, 20, 20)
        };
        var lblDesc = new Label
        {
            Text = "Windows 11은 기본 앱 설정을 사용자가 한 번 직접 지정해야 합니다.\n아래 가이드 화면처럼 기본 앱 설정에서 .md 검색 후 MDviewer를 선택하세요.",
            Dock = DockStyle.Fill,
            Font = Brand.Ui(9f),
            ForeColor = Brand.Ink
        };
        topPanel.Controls.Add(lblDesc);
        topPanel.Controls.Add(lblTitle);

        var pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Image = Brand.ResImage("assoc_guide.png")
        };
        var centerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 10, 20, 10) };
        centerPanel.Controls.Add(pic);

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(20, 10, 20, 12), BackColor = Brand.Wash };
        var btnOpen = new Button
        {
            Text = "기본 앱 설정 열기",
            AutoSize = true,
            Width = 180,
            Height = 34,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Brand.Cardinal,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = Brand.Ui(9.5f, FontStyle.Bold)
        };
        btnOpen.FlatAppearance.BorderSize = 0;
        btnOpen.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
            catch { Process.Start(new ProcessStartInfo("control", "/name Microsoft.DefaultPrograms") { UseShellExecute = true }); }
        };

        var btnClose = new Button
        {
            Text = "닫기", Width = 80, Height = 34, Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(230, 230, 230),
            ForeColor = Color.FromArgb(40, 40, 40), Cursor = Cursors.Hand, Font = Brand.Ui(9f)
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Click += (_, _) => Close();
        bottomPanel.Controls.Add(btnOpen);
        bottomPanel.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 10 });
        bottomPanel.Controls.Add(btnClose);
        Controls.Add(centerPanel);
        Controls.Add(topPanel);
        Controls.Add(bottomPanel);
        AcceptButton = btnOpen;
        CancelButton = btnClose;
    }
}
sealed class FileTab : Panel
{
    public static string LastFindQuery = "";
    public static bool LastFindWholeWord = false;
    public static bool LastFindAllTabs = false;

    readonly Panel _themeLine = new() { Dock = DockStyle.Top, Height = 2, BackColor = Brand.FrameAccent };
    readonly SplitContainer _split = new();
    double _verticalSplitRatio = 0.5, _horizontalSplitRatio = 0.5;
    bool _applyingSplitLayout;
    int _lastSplitExtent;
    readonly Panel _findBar = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 7, 8, 7), Visible = false };
    readonly FlowLayoutPanel _findFlow = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = false, Margin = new Padding(0), Padding = new Padding(0) };
    readonly Label _lblFindTitle = new() { Text = "찾기:", AutoSize = true, Margin = new Padding(2, 5, 4, 0) };
    readonly TextBox _txtFind = new() { Width = 160, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2, 2, 6, 0) };
    readonly Label _lblFindCount = new() { Text = "0/0", AutoSize = true, ForeColor = Color.FromArgb(100, 100, 100), Margin = new Padding(2, 5, 8, 0) };
    readonly Button _btnFindPrev = new() { Text = "◀ 이전", AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 25, Margin = new Padding(2, 1, 2, 0), Cursor = Cursors.Hand };
    readonly Button _btnFindNext = new() { Text = "다음 ▶", AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 25, Margin = new Padding(2, 1, 8, 0), Cursor = Cursors.Hand };
    readonly CheckBox _chkWholeWord = new() { Text = "단어단위", AutoSize = true, Margin = new Padding(4, 4, 6, 0), Cursor = Cursors.Hand };
    readonly CheckBox _chkAllTabs = new() { Text = "모든 탭", AutoSize = true, Margin = new Padding(4, 4, 6, 0), Cursor = Cursors.Hand };
    readonly Button _btnCloseFind = new() { Text = "✕", Size = new Size(26, 26), FlatStyle = FlatStyle.Flat, Dock = DockStyle.Right, Margin = new Padding(0), Cursor = Cursors.Hand };

    int _findCurrentIndex = -1;
    int _findTotalMatches = 0;

    readonly Panel _hlBar = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(10, 8, 10, 8), Visible = false };
    readonly Label _lblHl = new() { Text = "강조키워드:", AutoSize = true, Dock = DockStyle.Left, Padding = new Padding(0, 3, 8, 0) };
    readonly TextBox _txtHl = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly Panel _editPane = new() { Dock = DockStyle.Fill };
    readonly Panel _gutter = new();
    readonly RichTextBox _text = new();
    readonly Panel _viewPane = new() { Dock = DockStyle.Fill };
    readonly Panel _viewPane2 = new() { Dock = DockStyle.Fill };
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, AllowExternalDrop = true };
    readonly WebView2 _web2 = new() { Dock = DockStyle.Fill, AllowExternalDrop = true };
    readonly FileSystemWatcher _watch = new();
    readonly System.Windows.Forms.Timer _previewTick = new() { Interval = 120 };
    readonly MarkdownPipeline _md = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseGridTables()
        .Build();

    bool _editMode;
    bool _suppressPreviewRefresh;
    double _zoomEdit = 1;
    double _zoomWeb = 1;
    double _zoomWeb2 = 1;
    readonly ZoomOsdLabel _osdEdit = new();
    readonly System.Windows.Forms.Timer _osdEditTimer = new() { Interval = 1100 };
    bool _syncing;
    SplitMode _splitMode = SplitMode.None;
    bool _ready;
    bool _dirty;
    bool _suspendWatch;
    string _loaded = "";

    public string Path { get; private set; }
    public string Title => string.IsNullOrEmpty(Path) ? "제목 없음" : System.IO.Path.GetFileName(Path);
    public TabPage? HostPage { get; set; }
    public bool Dirty
    {
        get => _dirty;
        private set
        {
            if (_dirty == value) return;
            _dirty = value;
            DirtyChanged?.Invoke();
        }
    }
    public event Action? DirtyChanged;
    public event Action<string>? OpenRequested;

    public bool HighlightBarVisible
    {
        get => _hlBar.Visible;
        set
        {
            _hlBar.Visible = value;
            if (value)
            {
                _txtHl.Focus();
                _txtHl.SelectAll();
            }
            else
            {
                if (_editMode) _text.Focus();
                else _web.Focus();
            }
        }
    }

    public bool EditMode
    {
        get => _editMode;
        set
        {
            _editMode = value;
            if (!value) _splitMode = SplitMode.None;
            _text.ReadOnly = !value;
            ApplyLayout();
            RefreshPreview(forceReload: true);
            if (value)
            {
                BeginInvoke(() =>
                {
                    _text.Focus();
                    SyncCursorToWeb();
                });
            }
            else
            {
                if (_web.CoreWebView2 != null)
                    _ = _web.ExecuteScriptAsync("if(typeof clearCursorHighlight==='function')clearCursorHighlight();");
                if (_web2.CoreWebView2 != null)
                    _ = _web2.ExecuteScriptAsync("if(typeof clearCursorHighlight==='function')clearCursorHighlight();");
            }
        }
    }

    public SplitMode Split
    {
        get => _splitMode;
        set
        {
            _splitMode = value;
            ApplyLayout();
            RefreshPreview();
        }
    }

    public FileTab(string? path)
    {
        Path = path ?? "";
        Dock = DockStyle.Fill;
        BackColor = Brand.Wash;

        _lblFindTitle.Font = Brand.Ui(8.5f, FontStyle.Bold);
        _lblFindTitle.ForeColor = Color.FromArgb(70, 70, 70);
        _txtFind.Font = Brand.Ui(8.5f);
        _lblFindCount.Font = Brand.Ui(8.5f);
        _btnFindPrev.Font = Brand.Ui(8f);
        _btnFindNext.Font = Brand.Ui(8f);
        _chkWholeWord.Font = Brand.Ui(8.5f);
        _chkAllTabs.Font = Brand.Ui(8.5f);
        _btnCloseFind.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        _btnFindPrev.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 215);
        _btnFindNext.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 215);
        _btnCloseFind.FlatAppearance.BorderSize = 0;

        _btnFindPrev.Click += (_, _) => FindNext(false);
        _btnFindNext.Click += (_, _) => FindNext(true);
        _btnCloseFind.Click += (_, _) => HideFindBar(true);
        _chkWholeWord.CheckedChanged += (_, _) =>
        {
            LastFindWholeWord = _chkWholeWord.Checked;
            FindNext(true, selectCurrent: true);
        };
        _chkAllTabs.CheckedChanged += (_, _) =>
        {
            LastFindAllTabs = _chkAllTabs.Checked;
        };
        _txtFind.TextChanged += (_, _) =>
        {
            LastFindQuery = _txtFind.Text;
            UpdateFindCount();
        };
        _txtFind.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F && !e.Shift && !e.Alt)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ToggleFindBar();
                return;
            }
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                FindNext(!e.Shift);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                HideFindBar(true);
            }
        };

        _findFlow.Controls.Add(_lblFindTitle);
        _findFlow.Controls.Add(_txtFind);
        _findFlow.Controls.Add(_lblFindCount);
        _findFlow.Controls.Add(_btnFindPrev);
        _findFlow.Controls.Add(_btnFindNext);
        _findFlow.Controls.Add(_chkWholeWord);
        _findFlow.Controls.Add(_chkAllTabs);

        _findBar.BackColor = Color.FromArgb(246, 246, 248);
        _findBar.Paint += (_, e) =>
        {
            using var p = new Pen(Color.FromArgb(220, 220, 225));
            e.Graphics.DrawLine(p, 0, _findBar.Height - 1, _findBar.Width, _findBar.Height - 1);
        };
        _findBar.Controls.Add(_findFlow);
        _findBar.Controls.Add(_btnCloseFind);

        _lblHl.Font = Brand.Ui(8.5f, FontStyle.Bold);
        _lblHl.ForeColor = Color.FromArgb(80, 80, 80);
        _txtHl.Font = Brand.Ui(8.5f);
        _txtHl.BackColor = Color.White;
        _txtHl.ForeColor = Color.FromArgb(40, 40, 40);
        _txtHl.TextChanged += (_, _) => ApplyHighlightKeywords();
        _txtHl.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ApplyHighlightKeywords();
            }
        };

        _hlBar.BackColor = Color.FromArgb(248, 248, 249);
        _hlBar.Paint += (_, e) =>
        {
            using var p = new Pen(Color.FromArgb(222, 222, 225));
            e.Graphics.DrawLine(p, 0, _hlBar.Height - 1, _hlBar.Width, _hlBar.Height - 1);
        };

        _hlBar.Controls.Add(_txtHl);
        _hlBar.Controls.Add(_lblHl);

        _gutter.Dock = DockStyle.Left;
        _gutter.Width = 52;
        _gutter.BackColor = Color.FromArgb(240, 240, 241);
        _gutter.Paint += (_, e) => PaintGutter(e, _text, _gutter);

        _text.Dock = DockStyle.Fill;
        _text.ReadOnly = true;
        _text.BorderStyle = BorderStyle.None;
        _text.WordWrap = true;
        _text.HideSelection = false;
        _text.Font = Brand.Editor(10.5f);
        _text.HandleCreated += (_, _) => Native.ApplyLineSpacing(_text, 1.4f);
        _text.ForeColor = Brand.Ink;
        _text.BackColor = Brand.Paper;
        _text.DetectUrls = false;
        _text.ScrollBars = RichTextBoxScrollBars.Both;
        _text.AcceptsTab = true;
        _text.EnableAutoDragDrop = false;
        _text.AllowDrop = true;
        _text.DragEnter += OnEditorDrag;
        _text.DragOver += OnEditorDrag;
        _text.DragDrop += OnEditorDrop;
        _text.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F && !e.Shift && !e.Alt)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ToggleFindBar();
                return;
            }
            if (e.KeyCode == Keys.F3 && !e.Control && !e.Alt)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                FindNext(!e.Shift);
                return;
            }
            if (!e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.F8)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                (FindForm() as MainForm)?.ToggleEditMode();
                return;
            }
            if ((e.Control || e.Shift) && e.KeyCode == Keys.F8)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ToggleSelectedHighlight();
                return;
            }
            if (!_editMode || _text.ReadOnly) return;
            if (e.Control && e.KeyCode == Keys.B)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                WrapSelection("**", "굵은 텍스트");
                return;
            }
            if (e.Control && e.KeyCode == Keys.I)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                WrapSelection("*", "기울임 텍스트");
                return;
            }
            if (e.Control && e.KeyCode == Keys.K)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                InsertMarkdownLink();
                return;
            }
            if (e.KeyCode == Keys.Enter && !e.Control && !e.Alt && !e.Shift && TryContinueList())
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Tab && !e.Control && !e.Alt && TryIndentList(e.Shift))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        _text.SelectionChanged += (_, _) =>
        {
            if (_editMode && !_syncing) SyncCursorToWeb();
        };
        _text.KeyUp += (_, _) =>
        {
            if (_editMode && !_syncing) SyncCursorToWeb();
        };
        _text.MouseUp += (_, _) =>
        {
            if (_editMode && !_syncing) SyncCursorToWeb();
        };
        _text.VScroll += (_, _) =>
        {
            _gutter.Invalidate();
            if (_editMode) SyncEditorToWeb();
        };
        _text.HScroll += (_, _) => _gutter.Invalidate();
        _text.ContentsResized += (_, _) => _gutter.Invalidate();
        _text.Resize += (_, _) => _gutter.Invalidate();
        _text.MouseWheel += (_, _) => _gutter.Invalidate();
        _text.TextChanged += (_, _) =>
        {
            Dirty = _text.Text != _loaded;
            _gutter.Width = GutterWidth(_text);
            _gutter.Invalidate();
            if (!_suppressPreviewRefresh)
            {
                _previewTick.Stop();
                _previewTick.Start();
            }
        };

        _editPane.Controls.Add(_text);
        _editPane.Controls.Add(_gutter);
        _text.Controls.Add(_osdEdit);
        _osdEdit.BringToFront();
        _text.Resize += (_, _) =>
        {
            _osdEdit.Location = new Point(Math.Max(10, _text.ClientSize.Width - _osdEdit.Width - 22), 12);
        };
        _text.VScroll += (_, _) => _osdEdit.BringToFront();
        _text.HScroll += (_, _) => _osdEdit.BringToFront();
        _osdEditTimer.Tick += (_, _) =>
        {
            _osdEditTimer.Stop();
            _osdEdit.Visible = false;
        };
        _viewPane.Controls.Add(_web);
        _viewPane2.Controls.Add(_web2);

        _split.Dock = DockStyle.Fill;
        _split.SplitterWidth = 9;
        _split.IsSplitterFixed = false;
        _split.Panel1MinSize = 40;
        _split.Panel2MinSize = 40;
        _split.BackColor = Color.FromArgb(222, 226, 232);
        _split.SplitterMoved += (_, _) => RememberSplitRatio();
        _split.SizeChanged += (_, _) => ApplySplitterDistance();
        _split.Paint += (_, e) =>
        {
            if (_split.Panel1Collapsed || _split.Panel2Collapsed) return;
            var r = _split.SplitterRectangle;
            using var brush = new SolidBrush(Color.FromArgb(125, 133, 144));
            for (int i = -1; i <= 1; i++)
            {
                var x = r.Left + r.Width / 2 - 1;
                var y = r.Top + r.Height / 2 - 1;
                if (_split.Orientation == Orientation.Vertical) y += i * 5;
                else x += i * 5;
                e.Graphics.FillEllipse(brush, x, y, 3, 3);
            }
        };
        _split.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !_split.SplitterRectangle.Contains(e.Location)) return;
            if (_split.Orientation == Orientation.Vertical) _verticalSplitRatio = 0.5;
            else _horizontalSplitRatio = 0.5;
            ApplySplitterDistance();
        };
        Controls.Add(_split);
        Controls.Add(_hlBar);
        Controls.Add(_findBar);
        _findBar.BringToFront();
        _hlBar.BringToFront();
        // 먼저 도킹해 검색/편집/분할 영역보다 위, 탭 바로 아래에 얇은 테마선을 유지한다.
        Controls.Add(_themeLine);
        _themeLine.SendToBack();

        _previewTick.Tick += (_, _) =>
        {
            _previewTick.Stop();
            RefreshPreview(forceReload: false);
        };

        try
        {
            if (string.IsNullOrEmpty(Path)) throw new InvalidOperationException();
            _watch.Path = System.IO.Path.GetDirectoryName(Path)!;
            _watch.Filter = System.IO.Path.GetFileName(Path);
            _watch.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
            _watch.Changed += (_, _) =>
            {
                if (_suspendWatch) return;
                BeginInvoke(() =>
                {
                    if (!Dirty) Reload();
                });
            };
            _watch.EnableRaisingEvents = true;
        }
        catch { }

        HandleCreated += async (_, _) => await InitWeb();
        HandleCreated += (_, _) => ApplySplitterDistance();
        Reload();
        ApplyLayout();
    }

    public void AttachMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        _themeLine.ContextMenuStrip = menu;
        _text.ContextMenuStrip = menu;
        _gutter.ContextMenuStrip = menu;
        _editPane.ContextMenuStrip = menu;
        _viewPane.ContextMenuStrip = menu;
        _viewPane2.ContextMenuStrip = menu;
        _split.ContextMenuStrip = menu;
        _hlBar.ContextMenuStrip = menu;
        _lblHl.ContextMenuStrip = menu;
        _txtHl.ContextMenuStrip = menu;
        _findBar.ContextMenuStrip = menu;
        _findFlow.ContextMenuStrip = menu;
        _lblFindTitle.ContextMenuStrip = menu;
        _txtFind.ContextMenuStrip = menu;
        _lblFindCount.ContextMenuStrip = menu;
        _chkWholeWord.ContextMenuStrip = menu;
        _chkAllTabs.ContextMenuStrip = menu;
    }

    public void ApplyChrome()
    {
        BackColor = Brand.Wash;
        _themeLine.BackColor = Brand.FrameAccent;
        _hlBar.BackColor = Brand.Wash;
        _lblFindTitle.Font = Brand.Ui(8.5f, FontStyle.Bold);
        _txtFind.Font = Brand.Ui(8.5f);
        _lblFindCount.Font = Brand.Ui(8.5f);
        _btnFindPrev.Font = Brand.Ui(8f);
        _btnFindNext.Font = Brand.Ui(8f);
        _chkWholeWord.Font = Brand.Ui(8.5f);
        _chkAllTabs.Font = Brand.Ui(8.5f);
        _lblHl.Font = Brand.Ui(8.5f, FontStyle.Bold);
        _txtHl.Font = Brand.Ui(8.5f);
        _editPane.BackColor = Brand.Wash;
        _gutter.BackColor = Color.FromArgb(240, 240, 241);
        _text.ForeColor = Brand.Ink;
        _text.BackColor = Brand.Paper;
        _text.Font = Brand.Editor((float)(10.5 * _zoomEdit));
        Native.ApplyLineSpacing(_text, 1.4f);
        ApplyWebZoom(_web, _zoomWeb);
        ApplyWebZoom(_web2, _zoomWeb2);
        ApplyColumnMode();
        RefreshPreview(forceReload: true);
        _gutter.Invalidate();
    }

    public void ApplyColumnMode()
    {
        if (_ready && _web.CoreWebView2 != null)
        {
            var js = $"if(typeof setColumnMode==='function')setColumnMode({(int)Brand.ColumnViewMode});";
            _ = _web.CoreWebView2.ExecuteScriptAsync(js);
        }
        if (_ready && _splitMode != SplitMode.None && _web2.CoreWebView2 != null)
        {
            var js = $"if(typeof setColumnMode==='function')setColumnMode({(int)Brand.ColumnViewMode});";
            _ = _web2.CoreWebView2.ExecuteScriptAsync(js);
        }
    }

    void OnEditorDrag(object? sender, DragEventArgs e)
    {
        e.Effect = MainForm.FirstMd(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnEditorDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files.Where(MainForm.IsSupportedDocument))
            OpenRequested?.Invoke(f);
    }

    async Task InitWeb()
    {
        try
        {
            var dataDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MdView", "wv2");
            Directory.CreateDirectory(dataDir);
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await InitOneWeb(_web, env);
            await InitOneWeb(_web2, env);
            HookWebDrop(_web);
            HookWebDrop(_web2);
            _ready = true;
            RefreshPreview(forceReload: true);
        }
        catch
        {
            _ready = false;
        }
    }

    static async Task InitOneWeb(WebView2 web, CoreWebView2Environment env)
    {
        await web.EnsureCoreWebView2Async(env);
        if (!string.IsNullOrEmpty(Brand.AssetDir))
        {
            web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "mdviewer.local", Brand.AssetDir,
                CoreWebView2HostResourceAccessKind.Allow);
        }
        web.AllowExternalDrop = true;
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        web.CoreWebView2.ContextMenuRequested += (_, e) =>
        {
            e.Handled = true;
            var form = web.FindForm() as MainForm;
            form?.ShowDocMenu(web);
        };
        web.CoreWebView2.Settings.IsZoomControlEnabled = false;
        web.CoreWebView2.Settings.IsPinchZoomEnabled = false;
        web.ZoomFactor = 1;
    }

    void HookWebDrop(WebView2 web)
    {
        web.AllowExternalDrop = true;
        web.DragEnter += (_, e) =>
        {
            e.Effect = MainForm.FirstMd(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        };
        web.DragOver += (_, e) =>
        {
            e.Effect = MainForm.FirstMd(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        };
        web.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var f in files.Where(MainForm.IsSupportedDocument))
                    OpenRequested?.Invoke(f);
        };
        var core = web.CoreWebView2;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            TryOpenUri(e.Uri);
        };
        core.NavigationCompleted += (_, _) =>
        {
            var targetZoom = web == _web2 ? _zoomWeb2 : _zoomWeb;
            ApplyWebZoom(web, targetZoom);
            if (_editMode)
            {
                BeginInvoke(() =>
                {
                    SyncCursorToWeb();
                    SyncEditorToWeb();
                });
            }
            BeginInvoke(() => ApplyHighlightKeywords());
        };
        core.NavigationStarting += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Uri)) return;
            if (e.Uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("about:blank#", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("https://mdviewer.local/", StringComparison.OrdinalIgnoreCase))
                return;

            e.Cancel = true;
            TryOpenUri(e.Uri);
        };
        core.DownloadStarting += (_, e) =>
        {
            if (TryParseDropped(e.DownloadOperation.Uri, out var md))
            {
                e.Cancel = true;
                e.Handled = true;
                OpenRequested?.Invoke(md);
            }
        };
        core.WebMessageReceived += (_, e) =>
        {
            var raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                using var message = System.Text.Json.JsonDocument.Parse(raw);
                var root = message.RootElement;
                var type = root.TryGetProperty("t", out var typeValue)
                    ? typeValue.GetString()
                    : null;
                if (type == "task" &&
                    root.TryGetProperty("line", out var lineValue) &&
                    root.TryGetProperty("checked", out var checkedValue) &&
                    lineValue.TryGetInt32(out var taskLine) &&
                    checkedValue.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                {
                    var isChecked = checkedValue.GetBoolean();
                    BeginInvoke(() => ToggleTaskAtLine(taskLine, isChecked));
                    return;
                }
                if (type == "f8")
                {
                    BeginInvoke(() => (FindForm() as MainForm)?.ToggleEditMode());
                    return;
                }
                if (type == "toggleHighlight" || type == "shiftF8")
                {
                    var sel = root.TryGetProperty("sel", out var selectionValue)
                        ? selectionValue.GetString()?.Trim() ?? ""
                        : "";
                    BeginInvoke(() =>
                    {
                        if (string.IsNullOrEmpty(sel)) ToggleSelectedHighlight();
                        else ToggleHighlightKeyword(sel);
                    });
                    return;
                }
                if (type == "ctrlF")
                {
                    var sel = root.TryGetProperty("sel", out var selectionValue)
                        ? selectionValue.GetString()?.Trim()
                        : null;
                    BeginInvoke(() => ToggleFindBar(sel));
                    return;
                }
                if (type == "f3")
                {
                    var shift = root.TryGetProperty("shift", out var shiftVal) && shiftVal.GetBoolean();
                    BeginInvoke(() => FindNext(!shift));
                    return;
                }
                if (raw.Contains("\"t\":\"esc\"", StringComparison.Ordinal))
                {
                    BeginInvoke(() =>
                    {
                        if (_findBar.Visible) HideFindBar(true);
                        else if (FindForm() is MainForm f && f.IsMenuVisible) f.CloseMenu();
                    });
                    return;
                }
                if (raw.Contains("\"t\":\"pointerdown\"", StringComparison.Ordinal))
                {
                    BeginInvoke(() =>
                    {
                        if (FindForm() is MainForm f && f.IsMenuVisible)
                            f.CloseMenu();
                    });
                    return;
                }
                if (raw.Contains("\"t\":\"ctx\"", StringComparison.Ordinal))
                {
                    BeginInvoke(() =>
                    {
                        if (FindForm() is MainForm f) f.ShowDocMenu(this);
                    });
                    return;
                }
                if (type == "clickLine" && _editMode &&
                    root.TryGetProperty("userInitiated", out var userInitiated) && userInitiated.ValueKind == System.Text.Json.JsonValueKind.True &&
                    root.TryGetProperty("ln", out var clickedLine) && clickedLine.TryGetInt32(out var normLine) && normLine > 0)
                {
                    MoveEditorToPreviewLine(normLine);
                    return;
                }
                if (raw.Contains("\"t\":\"zoom\"", StringComparison.Ordinal))
                {
                    var up = raw.Contains("\"d\":1");
                    var shift = raw.Contains("\"s\":true") || (ModifierKeys & Keys.Shift) != 0;
                    var delta = up ? 0.08 : -0.08;
                    BeginInvoke(() =>
                    {
                        if (shift)
                            AdjustWebZoom(web, delta);
                        else
                            AdjustAllZoom(delta);
                    });
                    return;
                }
                // 자동 스크롤 및 사용자 클릭 표식이 없는 구형 위치 메시지는 편집 위치에 반영하지 않는다.
            }
            catch { }
        };
    }

    DateTime _zoomAt;

    public void AdjustZoom(double delta) => AdjustAllZoom(delta);

    public void HandleMouseWheelZoom(double delta, bool shift, Point screenCursor)
    {
        if (!shift)
        {
            AdjustAllZoom(delta);
            return;
        }

        var pt = PointToClient(screenCursor);
        if (_editMode)
        {
            if (_split.Panel1.Bounds.Contains(pt))
                AdjustEditZoom(delta);
            else
                AdjustWebZoom(_web, delta);
        }
        else if (_splitMode != SplitMode.None)
        {
            if (_split.Panel1.Bounds.Contains(pt))
                AdjustWebZoom(_web, delta);
            else
                AdjustWebZoom(_web2, delta);
        }
        else
        {
            AdjustWebZoom(_web, delta);
        }
    }

    public void AdjustAllZoom(double delta)
    {
        if ((DateTime.UtcNow - _zoomAt).TotalMilliseconds < 35) return;
        _zoomAt = DateTime.UtcNow;
        _zoomEdit = Math.Clamp(_zoomEdit + delta, 0.6, 2.4);
        _zoomWeb = Math.Clamp(_zoomWeb + delta, 0.6, 2.4);
        _zoomWeb2 = Math.Clamp(_zoomWeb2 + delta, 0.6, 2.4);
        ApplyEditZoom();
        ApplyWebZoom(_web, _zoomWeb);
        if (_splitMode != SplitMode.None)
            ApplyWebZoom(_web2, _zoomWeb2);

        if (_editMode)
        {
            ShowEditOsd((int)Math.Round(_zoomEdit * 100));
            ShowWebOsd(_web, (int)Math.Round(_zoomWeb * 100));
        }
        else if (_splitMode != SplitMode.None)
        {
            ShowWebOsd(_web, (int)Math.Round(_zoomWeb * 100));
            ShowWebOsd(_web2, (int)Math.Round(_zoomWeb2 * 100));
        }
        else
        {
            ShowWebOsd(_web, (int)Math.Round(_zoomWeb * 100));
        }
    }

    public void AdjustEditZoom(double delta)
    {
        if ((DateTime.UtcNow - _zoomAt).TotalMilliseconds < 35) return;
        _zoomAt = DateTime.UtcNow;
        _zoomEdit = Math.Clamp(_zoomEdit + delta, 0.6, 2.4);
        ApplyEditZoom();
        ShowEditOsd((int)Math.Round(_zoomEdit * 100));
    }

    public void AdjustWebZoom(WebView2 targetWeb, double delta)
    {
        if ((DateTime.UtcNow - _zoomAt).TotalMilliseconds < 35) return;
        _zoomAt = DateTime.UtcNow;
        if (targetWeb == _web2)
        {
            _zoomWeb2 = Math.Clamp(_zoomWeb2 + delta, 0.6, 2.4);
            ApplyWebZoom(_web2, _zoomWeb2);
            ShowWebOsd(_web2, (int)Math.Round(_zoomWeb2 * 100));
        }
        else
        {
            _zoomWeb = Math.Clamp(_zoomWeb + delta, 0.6, 2.4);
            ApplyWebZoom(_web, _zoomWeb);
            ShowWebOsd(_web, (int)Math.Round(_zoomWeb * 100));
        }
    }

    void ApplyEditZoom()
    {
        _text.Font = Brand.Editor((float)(10.5 * _zoomEdit));
        Native.ApplyLineSpacing(_text, 1.4f);
        _gutter.Invalidate();
    }

    void ApplyWebZoom(WebView2 web, double zoomVal)
    {
        var z = zoomVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var js = "document.documentElement.style.zoom='" + z + "';";
        try
        {
            if (web.CoreWebView2 != null) _ = web.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch { }
    }

    void ShowEditOsd(int pct)
    {
        _osdEdit.Text = pct + "%";
        _osdEdit.Location = new Point(Math.Max(10, _text.ClientSize.Width - _osdEdit.Width - 22), 12);
        _osdEdit.Visible = true;
        _osdEdit.BringToFront();
        _osdEditTimer.Stop();
        _osdEditTimer.Start();
    }

    void ShowWebOsd(WebView2 web, int pct)
    {
        var js = "if(window.showZoomOsd){window.showZoomOsd(" + pct + ");}";
        try
        {
            if (web.CoreWebView2 != null) _ = web.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch { }
    }

    void MoveEditorToPreviewLine(int normLine)
    {
        if (!_editMode || _syncing) return;
        _syncing = true;
        try
        {
            var logicalLine = FromNormLine(_text.Text, normLine - 1);
            // RichTextBox의 화면 줄 번호 대신 원문의 논리 줄 번호를 사용한다.
            var position = GetCharIndexOfLogicalLine(_text.Text, logicalLine);
            _text.Select(position, 0);
            _text.ScrollToCaret();
            _text.Focus();
            _gutter.Invalidate();
            // 사용자가 클릭한 VIEW는 다시 스크롤하지 않고 위치 마크만 갱신한다.
            if (_web.CoreWebView2 != null)
                _ = _web.ExecuteScriptAsync("highlightCursorLine(" + normLine + ",0);");
        }
        finally { _syncing = false; }
    }

    void SyncCursorToWeb()
    {
        if (_syncing || !_ready || !_editMode || _web.CoreWebView2 == null) return;
        try
        {
            int charIndex = _text.SelectionStart;
            int logicalLine = GetLogicalLine(_text.Text, charIndex);
            int totalLines = CountLogicalLines(_text.Text);
            int normLine = ToNormLine(_text.Text, logicalLine) + 1;
            int normTotal = ToNormLine(_text.Text, totalLines - 1) + 1;
            var js = "scrollToCursorLine(" + normLine + ", " + normTotal + ");";
            _ = _web.ExecuteScriptAsync(js);
            if (_splitMode != SplitMode.None && _web2.CoreWebView2 != null)
            {
                _ = _web2.ExecuteScriptAsync(js);
            }
        }
        catch { }
    }

    void SyncEditorToWeb()
    {
        if (_syncing || !_ready || !_editMode || _web.CoreWebView2 == null) return;
        _syncing = true;
        try
        {
            int firstChar = _text.GetCharIndexFromPosition(Point.Empty);
            int logicalFirst = GetLogicalLine(_text.Text, firstChar);
            int totalLines = CountLogicalLines(_text.Text);
            int norm = ToNormLine(_text.Text, logicalFirst) + 1;
            int normTotal = ToNormLine(_text.Text, totalLines - 1) + 1;
            var js = "scrollToSrcLine(" + norm + ", " + normTotal + ")";
            _ = _web.ExecuteScriptAsync(js);
            if (_splitMode != SplitMode.None && _web2.CoreWebView2 != null)
            {
                _ = _web2.ExecuteScriptAsync(js);
            }
        }
        catch { }
        finally
        {
            _syncing = false;
        }
    }

    static string JsStr(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ") + "\"";

    static bool TryParseDropped(string? uri, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var raw = Uri.UnescapeDataString(uri.Trim().Trim('"'));
        if (MainForm.IsMdPublic(raw) && File.Exists(raw))
        {
            path = raw;
            return true;
        }
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || !u.IsFile) return false;
        path = u.LocalPath;
        return MainForm.IsMdPublic(path) && File.Exists(path);
    }

    bool TryResolveRelativeMd(string uri, out string resolved)
    {
        resolved = "";
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrEmpty(Path)) return false;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (string.IsNullOrEmpty(dir)) return false;
            var raw = Uri.UnescapeDataString(uri.Trim().Trim('"'));
            if (raw.StartsWith("about:blank/", StringComparison.OrdinalIgnoreCase))
                raw = raw["about:blank/".Length..];
            if (Uri.TryCreate(raw, UriKind.Absolute, out var u) && u.IsFile)
                raw = u.LocalPath;

            var comb = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, raw));
            if (File.Exists(comb) && MainForm.IsMdPublic(comb))
            {
                resolved = comb;
                return true;
            }
        }
        catch { }
        return false;
    }

    public static void OpenExternalUrl(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch { }
    }

    void TryOpenUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        if (TryParseDropped(uri, out var md))
        {
            OpenRequested?.Invoke(md);
            return;
        }
        if (TryResolveRelativeMd(uri, out var relMd))
        {
            OpenRequested?.Invoke(relMd);
            return;
        }
        OpenExternalUrl(uri);
    }

    void ToggleTaskAtLine(int oneBasedLine, bool isChecked)
    {
        if (!MainForm.IsMd(Path)) return;
        var source = _text.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = source.Split('\n');
        var index = oneBasedLine - 1;
        if (index < 0 || index >= lines.Length) return;

        var updated = new Regex(
            @"^(\s*(?:[-+*]|\d+[.)])\s+)\[\s*([xX ])\s*\]")
            .Replace(
                lines[index],
                m => m.Groups[1].Value + "[" + (isChecked ? "x" : " ") + "]",
                1);
        if (updated == lines[index]) return;

        var position = _text.SelectionStart;
        lines[index] = updated;
        _suppressPreviewRefresh = true;
        try
        {
            _text.Text = string.Join("\n", lines);
            _text.SelectionStart = Math.Min(position, _text.TextLength);
            if (_editMode)
            {
                _text.ScrollToCaret();
            }
        }
        finally
        {
            _suppressPreviewRefresh = false;
        }

        if (_web2.CoreWebView2 != null)
        {
            var boolStr = isChecked ? "true" : "false";
            var js = "document.querySelectorAll('li[data-src-line=\"" + oneBasedLine + "\"] input[type=checkbox]').forEach(cb => cb.checked = " + boolStr + ");";
            _ = _web2.CoreWebView2.ExecuteScriptAsync(js);
        }
    }

    void WrapSelection(string marker, string placeholder)
    {
        var start = _text.SelectionStart;
        var selected = _text.SelectedText;
        var value = selected.Length > 0 ? selected : placeholder;
        _text.SelectedText = marker + value + marker;
        _text.Select(start + marker.Length, value.Length);
    }

    void InsertMarkdownLink()
    {
        var start = _text.SelectionStart;
        var selected = _text.SelectedText;
        var label = selected.Length > 0 ? selected : "링크 텍스트";
        var url = "https://";
        _text.SelectedText = "[" + label + "](" + url + ")";
        _text.Select(start + label.Length + 3, url.Length);
    }

    bool TryContinueList()
    {
        var caret = _text.SelectionStart;
        var lineIndex = _text.GetLineFromCharIndex(caret);
        var lineStart = _text.GetFirstCharIndexFromLine(lineIndex);
        if (lineStart < 0) return false;
        var lineEnd = lineIndex + 1 < _text.Lines.Length
            ? _text.GetFirstCharIndexFromLine(lineIndex + 1)
            : _text.TextLength;
        var line = _text.Text.Substring(lineStart, Math.Max(0, lineEnd - lineStart)).TrimEnd('\r', '\n');
        if (caret < lineStart + line.Length) return false;

        var match = Regex.Match(line, @"^(\s*)([-+*]|\d+[.)])([ \t]+)(.*)$");
        if (!match.Success) return false;

        var indent = match.Groups[1].Value;
        var marker = match.Groups[2].Value;
        var spacing = match.Groups[3].Value;
        var content = match.Groups[4].Value.Trim();
        if (content.Length == 0)
        {
            _text.Select(lineStart, line.Length);
            _text.SelectedText = indent;
            _text.SelectionStart = lineStart + indent.Length;
            _text.SelectedText = Environment.NewLine;
            return true;
        }

        var nextMarker = marker;
        var number = Regex.Match(marker, @"\d+");
        if (number.Success && int.TryParse(number.Value, out var n))
            nextMarker = (n + 1) + (marker.EndsWith(")", StringComparison.Ordinal) ? ")" : ".");

        _text.SelectionStart = caret;
        _text.SelectedText = Environment.NewLine + indent + nextMarker + spacing;
        return true;
    }

    bool TryIndentList(bool outdent)
    {
        if (_text.SelectionLength != 0) return false;
        var caret = _text.SelectionStart;
        var lineIndex = _text.GetLineFromCharIndex(caret);
        var lineStart = _text.GetFirstCharIndexFromLine(lineIndex);
        if (lineStart < 0) return false;
        var lineEnd = lineIndex + 1 < _text.Lines.Length
            ? _text.GetFirstCharIndexFromLine(lineIndex + 1)
            : _text.TextLength;
        var line = _text.Text.Substring(lineStart, Math.Max(0, lineEnd - lineStart)).TrimEnd('\r', '\n');
        if (!Regex.IsMatch(line, @"^\s*(?:[-+*]|\d+[.)])\s+")) return false;

        if (outdent)
        {
            var remove = line.StartsWith("\t", StringComparison.Ordinal) ? 1 :
                line.StartsWith("    ", StringComparison.Ordinal) ? 4 :
                line.StartsWith("  ", StringComparison.Ordinal) ? 2 : 0;
            if (remove == 0) return false;
            _text.Select(lineStart, remove);
            _text.SelectedText = "";
            _text.SelectionStart = Math.Max(lineStart, caret - remove);
        }
        else
        {
            _text.Select(lineStart, 0);
            _text.SelectedText = "  ";
            _text.SelectionStart = caret + 2;
        }
        return true;
    }

    async Task<string> ReadWebSelection(WebView2 web)
    {
        if (web.CoreWebView2 == null) return "";
        try
        {
            var res = await web.CoreWebView2.ExecuteScriptAsync(
                "window.getSelection ? window.getSelection().toString() : ''");
            return UnquoteJs(res).Trim();
        }
        catch { return ""; }
    }

    static string UnquoteJs(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(s) ?? "";
        }
        catch { }
        if (s.StartsWith('"') && s.EndsWith('"') && s.Length >= 2)
            s = s[1..^1];
        return Regex.Unescape(s);
    }

    public async void ToggleSelectedHighlight()
    {
        if (_editMode && _text.Focused && !string.IsNullOrWhiteSpace(_text.SelectedText))
        {
            ToggleHighlightKeyword(_text.SelectedText.Trim());
            return;
        }

        var sel = await ReadWebSelection(_web);
        if (string.IsNullOrEmpty(sel)) sel = await ReadWebSelection(_web2);
        if (!string.IsNullOrEmpty(sel))
        {
            ToggleHighlightKeyword(sel);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_text.SelectedText))
            ToggleHighlightKeyword(_text.SelectedText.Trim());
    }

    public void AddSelectedToHighlight() => ToggleSelectedHighlight();

    public void ToggleHighlightKeyword(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        word = word.Trim();
        var current = _txtHl.Text.Trim();
        var existing = current.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        int idx = existing.FindIndex(x => string.Equals(x, word, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            existing.RemoveAt(idx);
            _txtHl.Text = string.Join(" ", existing);
            if (existing.Count == 0)
            {
                _hlBar.Visible = false;
            }
        }
        else
        {
            existing.Add(word);
            _txtHl.Text = string.Join(" ", existing);
            _hlBar.Visible = true;
            _txtHl.Focus();
            _txtHl.SelectionStart = _txtHl.TextLength;
            _txtHl.SelectionLength = 0;
        }
        ApplyHighlightKeywords();
    }

    public void AddHighlightKeyword(string word) => ToggleHighlightKeyword(word);

    MainForm? Main => FindForm() as MainForm;

    public void ToggleFindBar(string? initialText = null)
    {
        if (_findBar.Visible)
        {
            var sel = initialText ?? (!string.IsNullOrWhiteSpace(_text.SelectedText) ? _text.SelectedText.Trim() : null);
            if (!string.IsNullOrEmpty(sel) && !sel.Equals(_txtFind.Text, StringComparison.OrdinalIgnoreCase))
            {
                ShowFindBar(sel);
            }
            else
            {
                HideFindBar(true);
            }
        }
        else
        {
            ShowFindBar(initialText);
        }
    }

    public void ShowFindBar(string? initialText = null, bool? wholeWord = null, bool? allTabs = null)
    {
        _findBar.Visible = true;
        _findBar.BringToFront();

        if (wholeWord.HasValue) _chkWholeWord.Checked = wholeWord.Value;
        else _chkWholeWord.Checked = LastFindWholeWord;

        if (allTabs.HasValue) _chkAllTabs.Checked = allTabs.Value;
        else _chkAllTabs.Checked = LastFindAllTabs;

        string query = initialText ?? (!string.IsNullOrWhiteSpace(_text.SelectedText) ? _text.SelectedText.Trim() : LastFindQuery);
        if (!string.IsNullOrEmpty(query))
        {
            _txtFind.Text = query;
        }

        _txtFind.Focus();
        _txtFind.SelectAll();
        UpdateFindCount();
    }

    public void HideFindBar(bool hideAllTabs = true)
    {
        _findBar.Visible = false;
        ClearFindInWeb();
        if (hideAllTabs && Main != null)
        {
            foreach (var tab in Main.GetAllTabs())
            {
                if (tab != this)
                {
                    tab.HideFindBar(false);
                }
            }
        }
        if (_editMode) _text.Focus();
        else _web.Focus();
    }

    public MatchCollection GetMatches(string query, bool wholeWord)
    {
        if (string.IsNullOrEmpty(query))
            return Regex.Matches("", "$");
        try
        {
            var pattern = wholeWord ? $@"\b{Regex.Escape(query)}\b" : Regex.Escape(query);
            return Regex.Matches(_text.Text, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return Regex.Matches("", "$");
        }
    }

    public void UpdateFindCount()
    {
        var query = _txtFind.Text;
        if (string.IsNullOrEmpty(query))
        {
            _lblFindCount.Text = "0/0";
            _findTotalMatches = 0;
            _findCurrentIndex = -1;
            ClearFindInWeb();
            return;
        }
        var matches = GetMatches(query, _chkWholeWord.Checked);
        _findTotalMatches = matches.Count;
        if (_findTotalMatches == 0)
        {
            _findCurrentIndex = -1;
            _lblFindCount.Text = "0/0 (결과 없음)";
            ClearFindInWeb();
        }
        else
        {
            int curPos = _text.SelectionStart;
            int foundIdx = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Index >= curPos)
                {
                    foundIdx = i;
                    break;
                }
            }
            _findCurrentIndex = foundIdx;
            _lblFindCount.Text = $"{foundIdx + 1}/{_findTotalMatches}";
            HighlightFindInWeb(query, _chkWholeWord.Checked, foundIdx);
        }
    }

    public void FindNext(bool forward, bool selectCurrent = false)
    {
        var query = _txtFind.Text;
        if (string.IsNullOrEmpty(query)) return;
        LastFindQuery = query;
        LastFindWholeWord = _chkWholeWord.Checked;
        LastFindAllTabs = _chkAllTabs.Checked;

        var matches = GetMatches(query, _chkWholeWord.Checked);
        if (matches.Count > 0)
        {
            if (selectCurrent)
            {
                int cur = Math.Clamp(_findCurrentIndex, 0, matches.Count - 1);
                SelectMatch(matches, cur);
                return;
            }

            if (forward)
            {
                int curPos = _text.SelectionStart + Math.Max(1, _text.SelectionLength);
                int nextIdx = -1;
                for (int i = 0; i < matches.Count; i++)
                {
                    if (matches[i].Index >= curPos)
                    {
                        nextIdx = i;
                        break;
                    }
                }

                if (nextIdx >= 0)
                {
                    SelectMatch(matches, nextIdx);
                    return;
                }
            }
            else
            {
                int curPos = _text.SelectionStart;
                int prevIdx = -1;
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (matches[i].Index < curPos)
                    {
                        prevIdx = i;
                        break;
                    }
                }

                if (prevIdx >= 0)
                {
                    SelectMatch(matches, prevIdx);
                    return;
                }
            }
        }

        // Wrap across tabs if All Tabs is checked
        if (_chkAllTabs.Checked && Main != null)
        {
            var all = Main.GetAllTabs();
            int curTabIdx = Main.GetTabIndex(this);
            if (all.Count > 1 && curTabIdx >= 0)
            {
                for (int step = 1; step < all.Count; step++)
                {
                    int targetTabIdx = forward
                        ? (curTabIdx + step) % all.Count
                        : (curTabIdx - step + all.Count) % all.Count;

                    var targetTab = all[targetTabIdx];
                    var targetMatches = targetTab.GetMatches(query, _chkWholeWord.Checked);
                    if (targetMatches.Count > 0)
                    {
                        Main.SelectTabByIndex(targetTabIdx);
                        targetTab.ShowFindBar(query, _chkWholeWord.Checked, true);
                        int targetMatchIdx = forward ? 0 : targetMatches.Count - 1;
                        var dirText = forward ? "다음 탭" : "이전 탭";
                        targetTab.SelectMatch(targetMatches, targetMatchIdx,
                            $"{targetMatchIdx + 1}/{targetMatches.Count} [{dirText}: {targetTab.Title}]");
                        return;
                    }
                }
            }
        }

        // Fallback wrap in current tab
        if (matches.Count > 0)
        {
            int wrapIdx = forward ? 0 : matches.Count - 1;
            SelectMatch(matches, wrapIdx);
        }
        else
        {
            _findCurrentIndex = -1;
            _findTotalMatches = 0;
            _lblFindCount.Text = "0/0 (결과 없음)";
        }
    }

    public void SelectMatch(MatchCollection matches, int idx, string? customMsg = null)
    {
        if (idx < 0 || idx >= matches.Count) return;
        _findCurrentIndex = idx;
        _findTotalMatches = matches.Count;
        var m = matches[idx];

        _text.SelectionStart = m.Index;
        _text.SelectionLength = m.Length;
        _text.ScrollToCaret();

        HighlightFindInWeb(_txtFind.Text, _chkWholeWord.Checked, idx);

        if (_editMode)
        {
            SyncCursorToWeb();
        }

        _lblFindCount.Text = customMsg ?? $"{idx + 1}/{matches.Count}";
    }

    void HighlightFindInWeb(string query, bool wholeWord, int activeIndex)
    {
        if (!_ready || string.IsNullOrEmpty(query)) return;
        var qJson = System.Text.Json.JsonSerializer.Serialize(query);
        var wwJson = wholeWord ? "true" : "false";
        var js = $"if(window.findInPage){{window.findInPage({qJson}, {wwJson}, {activeIndex});}}";
        try
        {
            if (_web.CoreWebView2 != null) _ = _web.CoreWebView2.ExecuteScriptAsync(js);
            if (_web2.CoreWebView2 != null) _ = _web2.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch { }
    }

    void ClearFindInWeb()
    {
        var js = "if(window.clearFindHighlights){window.clearFindHighlights();}";
        try
        {
            if (_web.CoreWebView2 != null) _ = _web.CoreWebView2.ExecuteScriptAsync(js);
            if (_web2.CoreWebView2 != null) _ = _web2.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch { }
    }

    public void TriggerFind() => ToggleFindBar();

    public void ApplyHighlightKeywords()
    {
        if (!_ready) return;
        var kw = _txtHl.Text.Trim();
        var list = kw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var json = System.Text.Json.JsonSerializer.Serialize(list);
        var js = "applyHighlightKeywords(" + json + ");";
        try
        {
            if (_web.CoreWebView2 != null) _ = _web.CoreWebView2.ExecuteScriptAsync(js);
            if (_web2.CoreWebView2 != null) _ = _web2.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch { }
    }

    static void Host(Control parent, Control child)
    {
        if (child.Parent == parent) return;
        child.Parent?.Controls.Remove(child);
        parent.Controls.Add(child);
        child.Dock = DockStyle.Fill;
        child.BringToFront();
    }

    void ApplyLayout()
    {
        _applyingSplitLayout = true;
        try
        {
            _split.Panel1Collapsed = false;
            if (_editMode)
            {
                _split.Panel2Collapsed = false;
                Host(_split.Panel1, _editPane);
                Host(_split.Panel2, _viewPane);
            }
            else if (_splitMode != SplitMode.None)
            {
                _split.Panel2Collapsed = false;
                Host(_split.Panel1, _viewPane);
                Host(_split.Panel2, _viewPane2);
            }
            else
            {
                _split.Panel2Collapsed = true;
                Host(_split.Panel1, _viewPane);
            }
            _split.Orientation = _splitMode == SplitMode.Horizontal
                ? Orientation.Horizontal : Orientation.Vertical;
            ApplySplitterDistance();
        }
        finally { _applyingSplitLayout = false; }
    }

    int SplitExtent => _split.Orientation == Orientation.Vertical
        ? _split.ClientSize.Width : _split.ClientSize.Height;

    void RememberSplitRatio()
    {
        // Resize/layout also raises SplitterMoved; only remember an actual divider move.
        if (_applyingSplitLayout || _split.Panel1Collapsed || _split.Panel2Collapsed || SplitExtent != _lastSplitExtent) return;
        int available = SplitExtent - _split.SplitterWidth;
        if (available <= 0) return;
        var ratio = (double)_split.SplitterDistance / available;
        if (_split.Orientation == Orientation.Vertical) _verticalSplitRatio = ratio;
        else _horizontalSplitRatio = ratio;
    }

    void ApplySplitterDistance()
    {
        if (IsDisposed || !IsHandleCreated || _split.Panel1Collapsed || _split.Panel2Collapsed) return;
        int available = SplitExtent - _split.SplitterWidth;
        int min = _split.Panel1MinSize, max = available - _split.Panel2MinSize;
        if (max < min) return;
        bool wasApplying = _applyingSplitLayout;
        _applyingSplitLayout = true;
        try
        {
            _lastSplitExtent = SplitExtent;
            var ratio = _split.Orientation == Orientation.Vertical ? _verticalSplitRatio : _horizontalSplitRatio;
            _split.SplitterDistance = Math.Clamp((int)Math.Round(available * ratio), min, max);
            _split.Invalidate();
        }
        finally { _applyingSplitLayout = wasApplying; }
    }

    public void DisposeWatch()
    {
        _previewTick.Stop();
        _previewTick.Dispose();
        _watch.EnableRaisingEvents = false;
        _watch.Dispose();
    }

    public bool ConfirmClose()
    {
        if (!Dirty) return true;
        var r = MessageBox.Show(this, Title + " 저장할까요?", "MDviewer", MessageBoxButtons.YesNoCancel);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) Save();
        return true;
    }

    bool _savingDocx;

    public async void SaveDocx()
    {
        if (_savingDocx) return;
        _savingDocx = true;
        try
        {
            if (FindForm() is MainForm main) main.EnterViewMode();
            else EditMode = false;

            using var dlg = new SaveFileDialog
            {
                Filter = "Word 문서 (*.docx)|*.docx",
                FileName = (string.IsNullOrEmpty(Path) ? "untitled" : System.IO.Path.GetFileNameWithoutExtension(Path)) + ".docx",
                OverwritePrompt = true
            };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            await ExportViewDocxAsync(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Word 저장 실패");
        }
        finally { _savingDocx = false; }
    }

    // Word는 원문을 별도 파싱하지 않고 최신 VIEW의 렌더링된 본문과 서식을 변환한다.
    internal async Task ExportViewDocxAsync(string destPath)
    {
        if (!_ready || _web.CoreWebView2 == null)
            throw new InvalidOperationException("VIEW 화면을 불러온 뒤 다시 저장해 주세요.");
        _previewTick.Stop();
        var token = Guid.NewGuid().ToString("N");
        var html = WrapHtml(BuildPreviewHtml()).Replace("<head>",
            "<head><meta name='word-export' content='" + token + "'>");
        _web.CoreWebView2.NavigateToString(html);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (true)
            {
                if (IsDisposed) throw new OperationCanceledException("문서가 닫혔습니다.");
                var ready = await _web.ExecuteScriptAsync(
                    "document.querySelector('meta[name=word-export]')?.content==='" + token +
                    "' && document.readyState==='complete' && document.fonts.status==='loaded' && " +
                    "Array.from(document.images).every(i=>i.complete)");
                if (ready == "true") break;
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("VIEW 렌더링을 완료하지 못했습니다. 잠시 후 다시 저장해 주세요.");
                await Task.Delay(100);
            }
            await _web.ExecuteScriptAsync("document.documentElement.style.zoom='1';if(typeof renderMath==='function')renderMath();");
            await WordExport.SaveViewAsync(_web.CoreWebView2, destPath);
        }
        finally
        {
            if (!IsDisposed) ApplyWebZoom(_web, _zoomWeb);
        }
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(Path))
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
                FileName = "untitled.md"
            };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            Path = dlg.FileName;
            DirtyChanged?.Invoke();
        }
        try
        {
            _suspendWatch = true;
            File.WriteAllText(Path, _text.Text, new UTF8Encoding(false));
            _loaded = _text.Text;
            Dirty = false;
            RefreshPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed");
        }
        finally
        {
            _suspendWatch = false;
        }
    }

    public void Reload()
    {
        if (string.IsNullOrEmpty(Path))
        {
            _loaded = _text.Text;
            RefreshPreview(forceReload: true);
            return;
        }
        try
        {
            using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8, true);
            var content = sr.ReadToEnd();
            var pos = _text.SelectionStart;
            _text.Text = content;
            // RichTextBox는 CRLF/CR을 LF로 정규화한다. 수정 비교도 같은 표현을 사용한다.
            _loaded = _text.Text;
            Native.ApplyLineSpacing(_text, 1.4f);
            Dirty = false;
            _text.SelectionStart = Math.Min(pos, _text.TextLength);
            _gutter.Width = GutterWidth(_text);
            _gutter.Invalidate();
            RefreshPreview(forceReload: true);
        }
        catch
        {
            _text.Text = "(cannot read file)";
        }
    }

    public static int GetLogicalLine(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text) || charIndex <= 0) return 0;
        int line = 0;
        int max = Math.Min(charIndex, text.Length);
        for (int i = 0; i < max; i++)
        {
            if (text[i] == '\n') line++;
        }
        return line;
    }

    public static int GetCharIndexOfLogicalLine(string text, int logicalLine)
    {
        if (string.IsNullOrEmpty(text) || logicalLine <= 0) return 0;
        int curLine = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (curLine == logicalLine) return i;
            if (text[i] == '\n') curLine++;
        }
        return text.Length;
    }

    public static int CountLogicalLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') count++;
        }
        return count;
    }

    string BuildPreviewHtml()
    {
        var html = MainForm.IsJson(Path) ? RenderJsonHtml(_text.Text) : RenderHtml(_text.Text);
        var dir = string.IsNullOrEmpty(Path) ? Environment.CurrentDirectory : System.IO.Path.GetDirectoryName(Path)!;
        return RewriteLocalImages(html, dir);
    }

    public void RefreshPreview(bool forceReload = false)
    {
        if (!_ready) return;
        try
        {
            var html = BuildPreviewHtml();

            int charIndex = _text.SelectionStart;
            int cursorLine = GetLogicalLine(_text.Text, charIndex);
            int totalLines = CountLogicalLines(_text.Text);
            int normCursor = ToNormLine(_text.Text, cursorLine) + 1;
            int normTotal = ToNormLine(_text.Text, totalLines - 1) + 1;

            if (!forceReload && _web.CoreWebView2 != null)
            {
                var jsonHtml = System.Text.Json.JsonSerializer.Serialize(html);
                int targetLine = _editMode ? normCursor : 0;
                var js = "updatePreviewContent(" + jsonHtml + ", " + targetLine + ", " + normTotal + ");";
                _ = _web.CoreWebView2.ExecuteScriptAsync(js);
            }
            else
            {
                var doc = WrapHtml(html);
                if (_web.CoreWebView2 != null) _web.CoreWebView2.NavigateToString(doc);
            }

            if (_web2.CoreWebView2 != null)
            {
                if (!forceReload)
                {
                    var jsonHtml = System.Text.Json.JsonSerializer.Serialize(html);
                    var js = "updatePreviewContent(" + jsonHtml + ", 0, " + normTotal + ");";
                    _ = _web2.CoreWebView2.ExecuteScriptAsync(js);
                }
                else
                {
                    var doc = WrapHtml(html);
                    _web2.CoreWebView2.NavigateToString(doc);
                }
            }
            BeginInvoke(() =>
            {
                ApplyWebZoom(_web, _zoomWeb);
                ApplyWebZoom(_web2, _zoomWeb2);
            });
        }
        catch { }
    }

    int GutterWidth(RichTextBox box)
    {
        var lines = Math.Max(1, CountLogicalLines(box.Text));
        var digits = lines.ToString().Length;
        using var g = CreateGraphics();
        var w = TextRenderer.MeasureText(g, new string('0', digits), box.Font).Width;
        return Math.Max(44, w + 16);
    }

    static void PaintGutter(PaintEventArgs e, RichTextBox box, Panel gutter)
    {
        e.Graphics.Clear(gutter.BackColor);
        if (box.TextLength == 0)
        {
            TextRenderer.DrawText(e.Graphics, "1", box.Font, new Point(8, 4), Color.Gray);
            return;
        }
        int firstChar = box.GetCharIndexFromPosition(Point.Empty);
        int firstLine = box.GetLineFromCharIndex(firstChar);
        int lastChar = box.GetCharIndexFromPosition(new Point(0, box.ClientSize.Height));
        int lastLine = box.GetLineFromCharIndex(lastChar);
        int lastLogical = -1;
        for (int line = firstLine; line <= lastLine + 1; line++)
        {
            int index = box.GetFirstCharIndexFromLine(line);
            if (index < 0) break;
            var pt = box.GetPositionFromCharIndex(index);
            if (pt.Y > box.ClientSize.Height) break;
            int logical = GetLogicalLine(box.Text, index);
            if (logical != lastLogical)
            {
                lastLogical = logical;
                var label = (logical + 1).ToString();
                var size = TextRenderer.MeasureText(label, box.Font);
                TextRenderer.DrawText(
                    e.Graphics, label, box.Font,
                    new Point(gutter.Width - size.Width - 6, pt.Y),
                    Color.FromArgb(150, 150, 150), TextFormatFlags.NoPadding);
            }
        }
        e.Graphics.DrawLine(Pens.Gainsboro, gutter.Width - 1, 0, gutter.Width - 1, gutter.Height);
    }

    static string RewriteLocalImages(string html, string dir)
    {
        html = Regex.Replace(
            html,
            """(?i)(<img\b[^>]*?\bsrc\s*=\s*["'])(?!https?:|data:|file:)([^"']+)(["'])""",
            m =>
            {
                var rel = m.Groups[2].Value.Replace('/', System.IO.Path.DirectorySeparatorChar);
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, rel));
                return m.Groups[1].Value + new Uri(full).AbsoluteUri + m.Groups[3].Value;
            });

        html = Regex.Replace(
            html,
            """(?i)<p(\s+[^>]*?)?>\s*(<img\b([^>]*?)\balt\s*=\s*["']([^"']+)["']([^>]*?)/?>)\s*</p>""",
            m =>
            {
                var pAttrs = m.Groups[1].Value;
                var imgTag = m.Groups[2].Value;
                var altText = System.Net.WebUtility.HtmlEncode(m.Groups[4].Value);
                if (string.IsNullOrWhiteSpace(altText)) return m.Value;
                return $"""<figure class="md-img-figure"{pAttrs}>{imgTag}<figcaption class="md-img-caption">{altText}</figcaption></figure>""";
            });

        return html;
    }

    string RenderHtml(string src)
    {
        var norm = NormalizeMd(src);
        var document = Markdown.Parse(norm, _md);
        AttachSrcLines(document);
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _md.Setup(renderer);
        renderer.Render(document);
        var html = writer.ToString();
        html = RenderCallouts(html);
        return RenderJsonCodeBlocks(html);
    }

    static string RenderCallouts(string html)
    {
        return Regex.Replace(
            html,
            @"<blockquote(?<battr>[^>]*)>\s*<p(?<pattr>[^>]*)>\[!(?<kind>NOTE|TIP|IMPORTANT|WARNING|CAUTION)\](?:[ \t]+(?<title>[^\r\n<]*))?(?<body>[\r\n][\s\S]*?)?</p>\s*</blockquote>",
            match =>
            {
                var kind = match.Groups["kind"].Value.ToLowerInvariant();
                var title = match.Groups["title"].Value.Trim();
                var body = match.Groups["body"].Value.Trim();
                var battr = match.Groups["battr"].Value;
                var titleHtml = string.IsNullOrEmpty(title)
                    ? ""
                    : "<p class='markdown-alert-title'>" + title + "</p>";
                var bodyHtml = string.IsNullOrEmpty(body)
                    ? ""
                    : "<div class='markdown-alert-body'>" + body + "</div>";
                return "<div class='markdown-alert markdown-alert-" + kind + "'" + battr + ">" +
                    titleHtml + bodyHtml + "</div>";
            },
            RegexOptions.IgnoreCase);
    }

    static string RenderJsonCodeBlocks(string html)
    {
        html = Regex.Replace(
            html,
            @"<pre(?<preattr>[^>]*)><code class=""language-json[c]?""(?<attr>[^>]*)>(?<content>[\s\S]*?)</code></pre>",
            match =>
            {
                var rawHtml = match.Groups["content"].Value;
                var decodedJson = System.Net.WebUtility.HtmlDecode(rawHtml);
                var attr = match.Groups["attr"].Value;
                var preattr = match.Groups["preattr"].Value;
                return FormatJsonBox(decodedJson, attr, preattr: preattr);
            },
            RegexOptions.IgnoreCase);

        html = Regex.Replace(
            html,
            @"<pre(?<preattr>[^>]*)><code class=""language-(?<lang>[a-zA-Z0-9_-]+)""(?<attr>[^>]*)>(?<content>[\s\S]*?)</code></pre>",
            match =>
            {
                var lang = match.Groups["lang"].Value;
                var attr = match.Groups["attr"].Value;
                var preattr = match.Groups["preattr"].Value;
                var content = match.Groups["content"].Value;
                return "<div class='code-block-wrapper'" + preattr + ">" +
                    "<div class='code-header'><span class='code-lang'>" + System.Net.WebUtility.HtmlEncode(lang) + "</span>" +
                    "<button class='copy-code-btn' onclick='copyCode(this)'>Copy</button></div>" +
                    "<pre class='json-view'><code class='language-" + lang + "'" + attr + ">" + AddLineNumbers(content) + "</code></pre></div>";
            },
            RegexOptions.IgnoreCase);

        html = Regex.Replace(
            html,
            @"<pre(?<preattr>[^>]*)><code(?<attr>(?:(?!class=""language-|class='language-)[^>])*)>(?<content>[\s\S]*?)</code></pre>",
            match =>
            {
                var attr = match.Groups["attr"].Value;
                var preattr = match.Groups["preattr"].Value;
                var content = match.Groups["content"].Value;
                return "<div class='code-block-wrapper'" + preattr + ">" +
                    "<div class='code-header'><span class='code-lang'>CODE</span>" +
                    "<button class='copy-code-btn' onclick='copyCode(this)'>Copy</button></div>" +
                    "<pre class='json-view'><code" + attr + ">" + AddLineNumbers(content) + "</code></pre></div>";
            },
            RegexOptions.IgnoreCase);

        return html;
    }

    static string RenderJsonHtml(string src)
    {
        return FormatJsonBox(src, isStandalone: true);
    }

    static string FormatJsonBox(string src, string attr = "", bool isStandalone = false, string preattr = "")
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                src,
                new System.Text.Json.JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip
                });
            var body = RenderJsonValue(document.RootElement, 0);
            return "<div class='code-block-wrapper json-wrapper'" + preattr + ">" +
                "<div class='code-header'><span class='code-lang'>JSON</span>" +
                "<button class='copy-code-btn' onclick='copyCode(this)'>Copy JSON</button></div>" +
                "<pre class='json-view'><code" + attr + ">" + AddLineNumbers(body) + "</code></pre></div>";
        }
        catch (System.Text.Json.JsonException ex)
        {
            var line = ex.LineNumber.HasValue ? " (line " + (ex.LineNumber.Value + 1) + ")" : "";
            return "<div class='code-block-wrapper json-wrapper json-has-error'" + preattr + ">" +
                "<div class='code-header'><span class='code-lang'>JSON</span>" +
                "<span class='json-error-badge'>Syntax Error</span>" +
                "<button class='copy-code-btn' onclick='copyCode(this)'>Copy</button></div>" +
                "<div class='json-error-msg'>JSON 구문 오류" + line + ": " +
                System.Net.WebUtility.HtmlEncode(ex.Message) + "</div>" +
                "<pre class='json-view json-invalid'><code" + attr + ">" +
                AddLineNumbers(System.Net.WebUtility.HtmlEncode(src)) + "</code></pre></div>";
        }
    }

    static string AddLineNumbers(string htmlCode)
    {
        if (string.IsNullOrEmpty(htmlCode))
            return "<span class='code-line'><span class='line-num'> 1</span><span class='line-content'></span></span>";

        var normalized = htmlCode.Replace("\r\n", "\n").Replace('\r', '\n');
        // Markdig may retain the fence's first line break; do not render it as a duplicate line 1.
        if (normalized.StartsWith("\n", StringComparison.Ordinal))
            normalized = normalized[1..];
        var lines = normalized.Split('\n');
        if (lines.Length > 1 && string.IsNullOrEmpty(lines[^1]))
        {
            Array.Resize(ref lines, lines.Length - 1);
        }
        int maxDigits = Math.Max(2, lines.Length.ToString().Length);
        var sb = new StringBuilder(htmlCode.Length + lines.Length * 64);
        for (int i = 0; i < lines.Length; i++)
        {
            var lineNum = (i + 1).ToString().PadLeft(maxDigits);
            sb.Append("<span class='code-line'><span class='line-num'>")
              .Append(lineNum)
              .Append("</span><span class='line-content'>")
              .Append(lines[i])
              .Append("</span></span>");
        }
        return sb.ToString();
    }

    static string RenderJsonValue(System.Text.Json.JsonElement value, int level)
    {
        var indent = new string(' ', level * 2);
        var childIndent = new string(' ', (level + 1) * 2);
        var sb = new StringBuilder();
        switch (value.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var properties = value.EnumerateObject().ToList();
                sb.Append("<span class='json-punct'>{</span>");
                if (properties.Count > 0)
                {
                    sb.Append('\n');
                    for (var i = 0; i < properties.Count; i++)
                    {
                        var property = properties[i];
                        sb.Append(childIndent)
                            .Append("<span class='json-key'>")
                            .Append(System.Net.WebUtility.HtmlEncode(
                                System.Text.Json.JsonSerializer.Serialize(property.Name)))
                            .Append("</span><span class='json-punct'>: </span>")
                            .Append(RenderJsonValue(property.Value, level + 1));
                        if (i < properties.Count - 1) sb.Append("<span class='json-punct'>,</span>");
                        sb.Append('\n');
                    }
                    sb.Append(indent);
                }
                sb.Append("<span class='json-punct'>}</span>");
                return sb.ToString();

            case System.Text.Json.JsonValueKind.Array:
                var items = value.EnumerateArray().ToList();
                sb.Append("<span class='json-punct'>[</span>");
                if (items.Count > 0)
                {
                    sb.Append('\n');
                    for (var i = 0; i < items.Count; i++)
                    {
                        sb.Append(childIndent).Append(RenderJsonValue(items[i], level + 1));
                        if (i < items.Count - 1) sb.Append("<span class='json-punct'>,</span>");
                        sb.Append('\n');
                    }
                    sb.Append(indent);
                }
                sb.Append("<span class='json-punct'>]</span>");
                return sb.ToString();

            case System.Text.Json.JsonValueKind.String:
                return "<span class='json-string'>" +
                    System.Net.WebUtility.HtmlEncode(value.GetRawText()) + "</span>";
            case System.Text.Json.JsonValueKind.Number:
                return "<span class='json-number'>" +
                    System.Net.WebUtility.HtmlEncode(value.GetRawText()) + "</span>";
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                return "<span class='json-bool'>" + value.GetRawText() + "</span>";
            case System.Text.Json.JsonValueKind.Null:
                return "<span class='json-null'>null</span>";
            default:
                return System.Net.WebUtility.HtmlEncode(value.GetRawText());
        }
    }

    static void AttachSrcLines(MarkdownDocument doc)
    {
        foreach (var block in doc.Descendants<Block>())
        {
            var a = block.GetAttributes();
            a.AddPropertyIfNotExist("data-src-line", (block.Line + 1).ToString());
        }
    }

    static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    static int CountInsertedBlanks(string src, int origLine)
    {
        src = src.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = src.Split('\n');
        int extra = 0;
        int last = Math.Min(origLine, lines.Length);
        for (int i = 0; i < last; i++)
        {
            if (IsTableLine(lines[i]) && i > 0 && lines[i - 1].Trim().Length > 0 && !IsTableLine(lines[i - 1]))
                extra++;
        }
        return extra;
    }

    static int ToNormLine(string src, int origLine) => origLine + CountInsertedBlanks(src, origLine);

    static int FromNormLine(string src, int normLine)
    {
        src = src.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = src.Split('\n');
        int extra = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (IsTableLine(lines[i]) && i > 0 && lines[i - 1].Trim().Length > 0 && !IsTableLine(lines[i - 1]))
                extra++;
            if (i + extra >= normLine) return i;
        }
        return Math.Max(0, lines.Length - 1);
    }

    static string NormalizeMd(string src)
    {
        if (string.IsNullOrEmpty(src)) return src;
        src = src.Replace("\r\n", "\n").Replace('\r', '\n');
        src = Regex.Replace(src, @"(?<!\\)\\\(([\s\S]*?)(?<!\\)\\\)", @"\\($1\\)");
        src = Regex.Replace(src, @"(?<!\\)\\\[([\s\S]*?)(?<!\\)\\\]", @"\\[$1\\]");
        var lines = src.Split('\n');
        var sb = new System.Text.StringBuilder(src.Length + 64);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool table = line.TrimStart().StartsWith('|');
            if (table && i > 0)
            {
                var prev = lines[i - 1];
                if (prev.Trim().Length > 0 && !prev.TrimStart().StartsWith('|'))
                    sb.Append('\n');
            }
            sb.Append(line);
            if (i < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    static string WrapHtml(string body)
    {
        string colClass = Brand.ColumnViewMode switch
        {
            ColumnMode.Two => "multi-col col-2",
            ColumnMode.Three => "multi-col col-3",
            ColumnMode.Auto => "multi-col col-auto",
            _ => "col-1"
        };
        return "<!doctype html><html><head><meta charset='utf-8'/>" +
        "<meta name='viewport' content='width=device-width, initial-scale=1'/>" +
        "<link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css'/>" +
        "<script src='https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.js'></script>" +
        "<script src='https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/contrib/auto-render.min.js'></script>" +
        "<style>@font-face{font-family:'SogangUni';src:url('https://mdviewer.local/sogang.ttf') format('truetype');}" +
         Brand.PreviewCss +
         "mark.md-kw-hl{background-color:#ffff00 !important;color:#000000 !important;border-radius:2px;padding:0 2px;}" +
         "mark:not(.md-kw-hl){background:#fff3a3;color:inherit;padding:0 2px;border-radius:2px;}" +
         "mark.md-find-hl{background-color:#ffe066 !important;color:#000000 !important;border-radius:2px;padding:0 2px;}" +
         "mark.md-find-active{background-color:#ff9900 !important;color:#000000 !important;outline:2px solid #ff6600;box-shadow:0 0 6px rgba(255,102,0,0.7);}" +
         "figure.md-img-figure{margin:0.8em 0;padding:0;text-align:center;display:block;}" +
         "figure.md-img-figure img{max-width:100%;height:auto;display:block;margin:0 auto;border-radius:4px;}" +
         "figcaption.md-img-caption{margin-top:6px;font-size:.88em;color:#6a6c6e;text-align:center;line-height:1.4;}" +
         // 본문과 구분된 편집 위치 레일: 문서의 기호로 오해되지 않도록 전용 여백에 삼각형을 둔다.
         "body.md-edit-position{padding-left:var(--md-position-width,24px);box-sizing:border-box}" +
         "#md-position-rail{position:fixed;left:0;top:0;bottom:0;width:24px;background:#edf0f4;border-right:1px solid #d4dae2;box-sizing:border-box;pointer-events:none;user-select:none;z-index:1000;}" +
         "#md-cursor-indicator{position:absolute;left:8px;top:0;width:8px;height:12px;clip-path:polygon(0 0,100% 50%,0 100%);background:" + Brand.CursorActiveAccentHex + ";transform:translateY(-50%);pointer-events:none;user-select:none;}" +
         ".task-list-item input[type=checkbox]{width:15px;height:15px;margin:0 .45em 0 0;vertical-align:-2px;accent-color:" + Brand.CheckboxAccentHex + ";cursor:pointer;}" +
         ".markdown-alert{margin:1em 0;padding:.7em 1em;border:1px solid #d8d8dc;border-left:4px solid #888;border-radius:8px;background:#fff;}" +
         ".markdown-alert-note{border-left-color:#268bd2}.markdown-alert-tip{border-left-color:#2e9d59}.markdown-alert-warning{border-left-color:#d08a00}.markdown-alert-important{border-left-color:#8250df}.markdown-alert-caution{border-left-color:#d1242f}" +
         ".markdown-alert-title{margin:0 0 .25em;font-weight:700;color:#444;}" +
         ".footnotes{margin-top:2em;padding-top:1em;border-top:1px solid #d8d8dc;font-size:.9em;color:#555}.footnote-ref{font-weight:600}.footnote-backref{margin-left:.35em;}" +
         ".code-block-wrapper{margin:0.9em 0;background:#282c34;border-radius:8px;border:1px solid #3e4451;overflow:hidden;box-shadow:0 4px 12px rgba(0,0,0,0.12);}" +
         ".code-header{display:flex;align-items:center;justify-content:space-between;padding:5px 12px;background:#21252b;border-bottom:1px solid #181a1f;font-family:Consolas,'Cascadia Code','Fira Code',monospace;font-size:12px;}" +
         ".code-lang{color:#abb2bf;font-weight:600;text-transform:uppercase;letter-spacing:0.5px;}" +
         ".copy-code-btn{background:#3a3f4b;color:#abb2bf;border:1px solid #4b5263;border-radius:4px;padding:3px 10px;font-size:11px;font-weight:500;cursor:pointer;transition:all .15s ease-in-out;outline:none;}" +
         ".copy-code-btn:hover{background:#4b5263;color:#fff;}" +
         ".copy-code-btn.copied{background:#98c379;color:#282c34;border-color:#98c379;font-weight:700;}" +
         ".json-view{margin:0 !important;padding:10px 14px;background:#282c34 !important;color:#abb2bf;white-space:pre;overflow-x:auto;tab-size:2;font:13px/1.4 Consolas,'Cascadia Code','Fira Code',monospace !important;border:none !important;border-radius:0 0 8px 8px !important;}" +
         ".code-line{display:block;line-height:1.4;white-space:pre;}" +
         ".line-num{display:inline-block;min-width:2.2em;padding-right:10px;margin-right:10px;text-align:right;color:#5c6370;border-right:1px solid #3e4451;user-select:none;-webkit-user-select:none;-moz-user-select:none;-ms-user-select:none;pointer-events:none;font-size:12px;}" +
         ".line-content{display:inline;}" +
         ".json-key{color:#61afef;font-weight:600;}" +
         ".json-string{color:#98c379;}" +
         ".json-number{color:#d19a66;}" +
         ".json-bool{color:#e06c75;font-weight:600;}" +
         ".json-null{color:#e5c07b;font-style:italic;font-weight:500;}" +
         ".json-punct{color:#abb2bf;}" +
         ".json-has-error{border-color:#e06c75;}" +
         ".json-error-badge{background:#e06c75;color:#fff;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:bold;margin-left:8px;margin-right:auto;}" +
         ".json-error-msg{padding:8px 14px;background:rgba(224,108,117,0.15);border-bottom:1px solid rgba(224,108,117,0.3);color:#ff7b86;font-size:12px;font-family:Consolas,'Cascadia Code',monospace;}" +
         ".json-invalid{opacity:0.9;}" +
         "html:has(meta[name='word-export']) body.multi-col{overflow-x:hidden !important;overflow-y:auto !important;height:auto !important;width:100% !important;}" +
         "html:has(meta[name='word-export']) body.multi-col .md{max-width:46rem !important;width:100% !important;height:auto !important;margin:0 auto !important;column-count:1 !important;column-width:auto !important;overflow:visible !important;}" +
         "body.multi-col:not(.md-edit-position){margin:0 !important;padding:0 !important;width:100vw !important;height:100vh !important;overflow:hidden !important;}" +
         "body.multi-col:not(.md-edit-position) .md{max-width:none !important;width:calc(100vw - 64px) !important;height:calc(100vh - 48px) !important;margin:24px 32px !important;padding:0 !important;box-sizing:border-box !important;overflow-x:auto !important;overflow-y:hidden !important;column-fill:auto !important;column-gap:36px !important;column-rule:1px solid rgba(128,128,128,0.22) !important;}" +
         "body.col-2:not(.md-edit-position) .md{column-count:2 !important;}" +
         "body.col-3:not(.md-edit-position) .md{column-count:3 !important;}" +
         "body.col-auto:not(.md-edit-position) .md{column-width:400px !important;}" +
         "body.multi-col:not(.md-edit-position) pre,body.multi-col:not(.md-edit-position) table,body.multi-col:not(.md-edit-position) figure,body.multi-col:not(.md-edit-position) img,body.multi-col:not(.md-edit-position) blockquote,body.multi-col:not(.md-edit-position) .code-block-wrapper,body.multi-col:not(.md-edit-position) .markdown-alert,body.multi-col:not(.md-edit-position) .katex-display{break-inside:avoid !important;page-break-inside:avoid !important;}" +
         "body.multi-col:not(.md-edit-position) img{max-height:calc(100vh - 120px) !important;max-width:100% !important;object-fit:contain !important;}" +
         "body.multi-col:not(.md-edit-position) h1,body.multi-col:not(.md-edit-position) h2,body.multi-col:not(.md-edit-position) h3{break-after:avoid !important;}" +
         "body.multi-col:not(.md-edit-position) .md::-webkit-scrollbar{height:8px !important;}" +
         "body.multi-col:not(.md-edit-position) .md::-webkit-scrollbar-track{background:transparent !important;}" +
         "body.multi-col:not(.md-edit-position) .md::-webkit-scrollbar-thumb{background:rgba(128,128,128,0.35) !important;border-radius:4px !important;}" +
         "body.multi-col:not(.md-edit-position) .md::-webkit-scrollbar-thumb:hover{background:rgba(128,128,128,0.6) !important;}" +
         $"</style></head><body class='{colClass}'><article class='md'>" +
        body + "</article>" + ScrollScript + "</body></html>";
    }

    const string ScrollScript =
        "<script>let __lastKeywords=[];let __cursorTarget=null;" +
        "function setCopied(btn){" +
        "const orig=btn.innerText;" +
        "btn.innerText='Copied!';" +
        "btn.classList.add('copied');" +
        "setTimeout(()=>{btn.innerText=orig;btn.classList.remove('copied');},1500);" +
        "}" +
        "function fallbackCopy(btn,text){" +
        "try{" +
        "const ta=document.createElement('textarea');" +
        "ta.value=text;" +
        "ta.style.position='fixed';" +
        "ta.style.top='0';" +
        "ta.style.left='0';" +
        "ta.style.opacity='0';" +
        "document.body.appendChild(ta);" +
        "ta.focus();" +
        "ta.select();" +
        "document.execCommand('copy');" +
        "document.body.removeChild(ta);" +
        "setCopied(btn);" +
        "}catch(e){}" +
        "}" +
        "function copyCode(btn){" +
        "const wrapper=btn.closest('.code-block-wrapper');" +
        "if(!wrapper)return;" +
        "const lineContents=wrapper.querySelectorAll('.line-content');" +
        "let text='';" +
        "if(lineContents.length>0){" +
        "text=Array.from(lineContents).map(el=>el.innerText||el.textContent||'').join('\\n');" +
        "}else{" +
        "const pre=wrapper.querySelector('pre');" +
        "if(!pre)return;" +
        "const clone=pre.cloneNode(true);" +
        "clone.querySelectorAll('.line-num,.json-error-badge,.json-error-msg').forEach(el=>el.remove());" +
        "text=clone.innerText||clone.textContent||'';" +
        "}" +
        "if(navigator.clipboard&&navigator.clipboard.writeText){" +
        "navigator.clipboard.writeText(text).then(()=>{setCopied(btn);}).catch(()=>{fallbackCopy(btn,text);});" +
        "}else{" +
        "fallbackCopy(btn,text);" +
        "}" +
        "}" +
        "function nodes(){return [...document.querySelectorAll('[data-src-line]')];}" +
        "function getLineTargetY(n,totalLines){" +
        "n=parseInt(n,10)||1;totalLines=parseInt(totalLines,10)||n;" +
        "const all=nodes();const docH=document.documentElement.scrollHeight;" +
        "const winH=window.innerHeight;const maxScroll=Math.max(0,docH-winH);" +
        "if(all.length===0){const ratio=totalLines>1?(n-1)/(totalLines-1):0;return ratio*maxScroll;}" +
        "let prev=null,next=null;" +
        "for(const el of all){" +
        "const ln=parseInt(el.getAttribute('data-src-line'),10);" +
        "if(ln<=n)prev=el;else{next=el;break;}" +
        "}" +
        "if(!prev){" +
        "const firstLn=parseInt(all[0].getAttribute('data-src-line'),10);" +
        "const firstY=all[0].getBoundingClientRect().top+window.scrollY;" +
        "if(firstLn<=1)return 0;" +
        "const ratio=Math.max(0,n-1)/Math.max(1,firstLn-1);" +
        "return ratio*firstY;" +
        "}" +
        "const prevLn=parseInt(prev.getAttribute('data-src-line'),10);" +
        "const prevY=prev.getBoundingClientRect().top+window.scrollY;" +
        "if(!next){" +
        "if(totalLines<=prevLn)return Math.min(prevY,maxScroll);" +
        "const ratio=Math.min(1,Math.max(0,n-prevLn)/Math.max(1,totalLines-prevLn));" +
        "const prevH=prev.offsetHeight||30;" +
        "const startY=prevY+prevH;" +
        "return Math.min(maxScroll,startY+ratio*Math.max(0,maxScroll-startY));" +
        "}" +
        "const nextLn=parseInt(next.getAttribute('data-src-line'),10);" +
        "const nextY=next.getBoundingClientRect().top+window.scrollY;" +
        "if(nextLn===prevLn)return prevY;" +
        "const ratio=Math.max(0,Math.min(1,(n-prevLn)/(nextLn-prevLn)));" +
        "return prevY+ratio*(nextY-prevY);" +
        "}" +
        "function clearCursorHighlight(){" +
        "  __cursorTarget=null;" +
        "  const marker=document.getElementById('md-cursor-indicator');" +
        "  if(marker)marker.hidden=true;" +
        "  const rail=document.getElementById('md-position-rail');if(rail)rail.hidden=true;" +
        "  document.body.classList.remove('md-edit-position');" +
        "}" +
        "function updateCursorIndicator(){" +
        "  const marker=document.getElementById('md-cursor-indicator');" +
        "  if(!marker)return;" +
        "  if(!__cursorTarget||!__cursorTarget.isConnected){marker.hidden=true;return;}" +
        "  const rect=__cursorTarget.getBoundingClientRect();" +
        "  const zoom=parseFloat(getComputedStyle(document.documentElement).zoom)||1;" +
        "  const lineHeight=(parseFloat(getComputedStyle(__cursorTarget).lineHeight)||20)*zoom;" +
        "  const y=rect.top+Math.min(rect.height,lineHeight)/2;" +
        "  marker.hidden=rect.height<=0||y<0||y>window.innerHeight;" +
        "  marker.style.top=(y/zoom)+'px';" +
        "  const rail=document.getElementById('md-position-rail');" +
        "  rail.style.width=(24/zoom)+'px';" +
        "  document.body.style.setProperty('--md-position-width',(24/zoom)+'px');" +
        "  marker.style.left=(8/zoom)+'px';" +
        "  marker.style.width=(8/zoom)+'px';" +
        "  marker.style.height=(12/zoom)+'px';" +
        "}" +
        "function highlightCursorLine(n,totalLines){" +
        "  n=parseInt(n,10)||1;" +
        "  const all=nodes();" +
        "  let target=null;" +
        "  for(const el of all){" +
        "    const ln=parseInt(el.getAttribute('data-src-line'),10);" +
        "    if(ln<=n)target=el;else break;" +
        "  }" +
        "  __cursorTarget=target||all[0]||null;" +
        "  if(!__cursorTarget){clearCursorHighlight();return;}" +
        "  let rail=document.getElementById('md-position-rail');" +
        "  if(!rail){rail=document.createElement('div');rail.id='md-position-rail';rail.setAttribute('aria-hidden','true');" +
        "    document.body.appendChild(rail);}" +
        "  rail.hidden=false;document.body.classList.add('md-edit-position');" +
        "  let marker=document.getElementById('md-cursor-indicator');" +
        "  if(!marker){" +
        "    marker=document.createElement('div');" +
        "    marker.id='md-cursor-indicator';" +
        "    marker.setAttribute('aria-hidden','true');" +
        "    rail.appendChild(marker);" +
        "  }" +
        "  updateCursorIndicator();" +
        "}" +
        "window.addEventListener('resize',updateCursorIndicator);" +
        "window.addEventListener('load',updateCursorIndicator,true);" +
        "new ResizeObserver(updateCursorIndicator).observe(document.querySelector('.md'));" +
        "function scrollToCursorLine(n,totalLines){" +
        "highlightCursorLine(n,totalLines);" +
        "const targetY=getLineTargetY(n,totalLines);" +
        "const winH=window.innerHeight;" +
        "const desiredY=Math.max(0,targetY-(winH*0.35));" +
        "window.scrollTo(0,desiredY);updateCursorIndicator();" +
        "}" +
        "function scrollToSrcLine(n,totalLines){" +
        "const targetY=getLineTargetY(n,totalLines);" +
        "window.scrollTo(0,Math.max(0,targetY-10));" +
        "}" +
        "function renderMath(){" +
        "if(typeof renderMathInElement==='function'){" +
        "const art=document.querySelector('.md')||document.body;" +
        "renderMathInElement(art,{" +
        "delimiters:[" +
        "{left:'$$',right:'$$',display:true}," +
        "{left:'\\\\[',right:'\\\\]',display:true}," +
        "{left:'$',right:'$',display:false}," +
        "{left:'\\\\(',right:'\\\\)',display:false}" +
        "]," +
        "throwOnError:false" +
        "});" +
        "}}" +
        "function updatePreviewContent(html,cursorLine,totalLines){" +
        "const art=document.querySelector('.md');" +
        "if(art){" +
        "const prevY=window.scrollY;" +
        "art.innerHTML=html;" +
        "wireTaskCheckboxes();" +
        "renderMath();" +
        "if(Array.isArray(__lastKeywords)&&__lastKeywords.length>0){applyHighlightKeywords(__lastKeywords);}" +
        "if(cursorLine&&cursorLine>0){scrollToCursorLine(cursorLine,totalLines);}" +
        "else{clearCursorHighlight();window.scrollTo(0,prevY);}" +
        "}}" +
        // 실제 사용자의 일반 왼쪽 클릭만 편집 위치 이동으로 해석한다.
        // 링크/버튼/체크박스, 드래그 선택, 스크립트 click()은 각자의 동작을 유지한다.
        "window.addEventListener('click',e=>{" +
        "if(!e.isTrusted||e.button!==0||e.ctrlKey||e.shiftKey||e.altKey||e.metaKey)return;" +
        "const target=e.target instanceof Element?e.target:null;" +
        "if(!target||target.closest('a,button,input,select,textarea,summary,[contenteditable]'))return;" +
        "const selection=window.getSelection();if(selection&&!selection.isCollapsed)return;" +
        "const block=target.closest('[data-src-line]');" +
        "if(!block||!block.closest('article.md'))return;" +
        "const ln=parseInt(block.getAttribute('data-src-line'),10);" +
        "if(ln>0)chrome.webview.postMessage(JSON.stringify({t:'clickLine',ln:ln,userInitiated:true}));" +
        "});" +
        // 이미지 로드나 레이아웃 갱신으로 발생한 scroll 이벤트도 미리보기 안에서만 처리한다.
        "window.addEventListener('scroll',updateCursorIndicator);" +
        "window.addEventListener('pointerdown',e=>{chrome.webview.postMessage(JSON.stringify({t:'pointerdown'}));},true);" +
        "window.addEventListener('contextmenu',e=>{e.preventDefault();chrome.webview.postMessage(JSON.stringify({t:'ctx'}));});" +
        "function showZoomOsd(pct){" +
        "let el=document.getElementById('__zoom_osd');" +
        "if(!el){" +
        "el=document.createElement('div');" +
        "el.id='__zoom_osd';" +
        "el.style.cssText='position:fixed;top:12px;right:16px;z-index:999999;background:rgba(30,34,42,0.88);color:#fff;padding:4px 12px;border-radius:12px;font-family:Consolas,-apple-system,sans-serif;font-size:12px;font-weight:700;letter-spacing:0.5px;pointer-events:none;box-shadow:0 3px 10px rgba(0,0,0,0.3);border:1px solid rgba(255,255,255,0.18);backdrop-filter:blur(4px);transition:opacity 0.25s ease;';" +
        "document.body.appendChild(el);" +
        "}" +
        "el.textContent=pct+'%';" +
        "el.style.opacity='1';" +
        "if(window.__zoomTimer)clearTimeout(window.__zoomTimer);" +
        "window.__zoomTimer=setTimeout(()=>{el.style.opacity='0';},1100);" +
        "}" +
        "window.addEventListener('wheel',e=>{if(e.ctrlKey){e.preventDefault();e.stopPropagation();chrome.webview.postMessage(JSON.stringify({t:'zoom',d:e.deltaY<0?1:-1,s:e.shiftKey}));}},{passive:false});" +
        "document.addEventListener('keydown',e=>{" +
        "if(e.key==='Escape'){chrome.webview.postMessage(JSON.stringify({t:'esc'}));}" +
        "else if(e.ctrlKey&&!e.shiftKey&&!e.altKey&&(e.key==='f'||e.key==='F'||e.code==='KeyF')){" +
        "e.preventDefault();" +
        "const sel=window.getSelection()?window.getSelection().toString().trim():'';" +
        "chrome.webview.postMessage(JSON.stringify({t:'ctrlF',sel:sel}));}" +
        "else if((e.key==='F3'||e.code==='F3')&&!e.ctrlKey&&!e.altKey){" +
        "e.preventDefault();chrome.webview.postMessage(JSON.stringify({t:'f3',shift:e.shiftKey}));}" +
        "else if(!e.ctrlKey&&!e.shiftKey&&!e.altKey&&(e.key==='F8'||e.code==='F8')){" +
        "e.preventDefault();chrome.webview.postMessage(JSON.stringify({t:'f8'}));}" +
        "else if((e.ctrlKey||e.shiftKey)&&(e.key==='F8'||e.code==='F8')){" +
        "e.preventDefault();" +
        "const sel=window.getSelection()?window.getSelection().toString().trim():'';" +
        "chrome.webview.postMessage(JSON.stringify({t:'toggleHighlight',sel:sel}));}" +
        "});" +
        "function findInPage(query,wholeWord,index){" +
        "clearFindHighlights();" +
        "if(!query)return 0;" +
        "const art=document.querySelector('.md')||document.body;" +
        "const pattern=wholeWord?'\\\\b'+escapeRegExp(query)+'\\\\b':escapeRegExp(query);" +
        "const rx=new RegExp(pattern,'gi');" +
        "highlightFindNodes(art,rx);" +
        "const matches=Array.from(document.querySelectorAll('.md-find-hl'));" +
        "if(matches.length===0)return 0;" +
        "const targetIndex=Math.max(0,Math.min(index,matches.length-1));" +
        "matches[targetIndex].classList.add('md-find-active');" +
        "matches[targetIndex].scrollIntoView({block:'center',behavior:'smooth'});" +
        "return matches.length;}" +
        "function clearFindHighlights(){" +
        "document.querySelectorAll('.md-find-hl').forEach(m=>{" +
        "const p=m.parentNode;if(p){p.replaceChild(document.createTextNode(m.textContent),m);p.normalize();}" +
        "});}" +
        "function highlightFindNodes(root,rx){" +
        "const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT,{" +
        "acceptNode:n=>{" +
        "rx.lastIndex=0;" +
        "if(!n.nodeValue||!rx.test(n.nodeValue))return NodeFilter.FILTER_REJECT;" +
        "const p=n.parentElement;" +
        "if(p&&(p.tagName==='SCRIPT'||p.tagName==='STYLE'||p.classList.contains('line-num')||p.classList.contains('md-find-hl')))" +
        "return NodeFilter.FILTER_REJECT;" +
        "return NodeFilter.FILTER_ACCEPT;}" +
        "});" +
        "const nds=[];let cur;while((cur=walker.nextNode()))nds.push(cur);" +
        "for(const node of nds){" +
        "const text=node.nodeValue;rx.lastIndex=0;let match;let last=0;" +
        "const frag=document.createDocumentFragment();let matched=false;" +
        "while((match=rx.exec(text))!==null){" +
        "matched=true;" +
        "if(match.index>last)frag.appendChild(document.createTextNode(text.substring(last,match.index)));" +
        "const mark=document.createElement('mark');" +
        "mark.className='md-find-hl';" +
        "mark.textContent=match[0];" +
        "frag.appendChild(mark);" +
        "last=rx.lastIndex;" +
        "if(!rx.global)break;}" +
        "if(matched){if(last<text.length)frag.appendChild(document.createTextNode(text.substring(last)));" +
        "if(node.parentNode)node.parentNode.replaceChild(frag,node);}" +
        "}}" +
        "function wireTaskCheckboxes(){document.querySelectorAll('.task-list-item input[type=checkbox]').forEach(cb=>{" +
        "cb.disabled=false;cb.title='완료 상태 전환';" +
        "cb.addEventListener('change',()=>{const li=cb.closest('li[data-src-line]');" +
        "const line=li?parseInt(li.getAttribute('data-src-line'),10):0;" +
        "if(line>0)chrome.webview.postMessage(JSON.stringify({t:'task',line:line,checked:cb.checked}));});" +
        "});}" +
        "function clearKeywordHighlights(root){" +
        "root.querySelectorAll('mark.md-kw-hl').forEach(m=>{" +
        "const p=m.parentNode;if(p){p.replaceChild(document.createTextNode(m.textContent),m);p.normalize();}" +
        "});}" +
        "function escapeRegExp(s){return s.replace(/[-[\\]{}()*+?.,\\\\^$|#\\s]/g,'\\\\$&');}" +
        "function applyHighlightKeywords(keywords){" +
        "__lastKeywords=Array.isArray(keywords)?keywords:[];" +
        "const art=document.querySelector('.md')||document.body;" +
        "clearKeywordHighlights(art);" +
        "if(!keywords||!Array.isArray(keywords)||keywords.length===0)return;" +
        "const valid=keywords.map(k=>(k||'').trim()).filter(k=>k.length>0);" +
        "if(valid.length===0)return;" +
        "const pattern='('+valid.map(escapeRegExp).join('|')+')';" +
        "const rx=new RegExp(pattern,'gi');" +
        "const walker=document.createTreeWalker(art,NodeFilter.SHOW_TEXT,{" +
        "acceptNode:n=>{" +
        "rx.lastIndex=0;" +
        "if(!n.nodeValue||!rx.test(n.nodeValue))return NodeFilter.FILTER_REJECT;" +
        "const p=n.parentElement;" +
        "if(p&&(p.tagName==='SCRIPT'||p.tagName==='STYLE'||p.classList.contains('md-kw-hl')||p.closest('.katex')))return NodeFilter.FILTER_REJECT;" +
        "return NodeFilter.FILTER_ACCEPT;}" +
        "});" +
        "const nds=[];let cur;" +
        "while(cur=walker.nextNode())nds.push(cur);" +
        "for(const node of nds){" +
        "const text=node.nodeValue;" +
        "rx.lastIndex=0;let last=0;let match;" +
        "const frag=document.createDocumentFragment();let matched=false;" +
        "while((match=rx.exec(text))!==null){" +
        "matched=true;" +
        "if(match.index>last)frag.appendChild(document.createTextNode(text.substring(last,match.index)));" +
        "const mark=document.createElement('mark');" +
        "mark.className='md-kw-hl';" +
        "mark.textContent=match[0];" +
        "frag.appendChild(mark);" +
        "last=match.index+match[0].length;}" +
        "if(matched){if(last<text.length)frag.appendChild(document.createTextNode(text.substring(last)));" +
        "if(node.parentNode)node.parentNode.replaceChild(frag,node);}" +
        "}}" +
        "wireTaskCheckboxes();" +
        "document.addEventListener('DOMContentLoaded',renderMath);" +
        "window.addEventListener('load',renderMath);" +
        "setTimeout(renderMath,50);" +
        "setTimeout(renderMath,250);" +
        "function setColumnMode(mode){" +
        "const wasMulti=document.body.classList.contains('multi-col');" +
        "document.body.classList.remove('multi-col','col-1','col-2','col-3','col-auto');" +
        "const isMulti=mode===2||mode===3||mode===0;" +
        "if(mode===2){document.body.classList.add('multi-col','col-2');}" +
        "else if(mode===3){document.body.classList.add('multi-col','col-3');}" +
        "else if(mode===0){document.body.classList.add('multi-col','col-auto');}" +
        "else{document.body.classList.add('col-1');}" +
        "window.__targetColumnIndex=undefined;" +
        "const md=document.querySelector('.md');" +
        "if(isMulti&&!wasMulti){if(md)md.scrollLeft=0;}" +
        "else if(!isMulti&&wasMulti){window.scrollTo({top:0,left:0});}" +
        "}" +
        "function isMultiCol(){return document.body.classList.contains('multi-col')&&!document.body.classList.contains('md-edit-position');}" +
        "function getColStep(){" +
        "const md=document.querySelector('.md');" +
        "if(!md)return 400;" +
        "const gap=36;const clientW=md.clientWidth;" +
        "let count=2;" +
        "if(document.body.classList.contains('col-3'))count=3;" +
        "else if(document.body.classList.contains('col-auto')){const colW=400;count=Math.max(1,Math.floor((clientW+gap)/(colW+gap)));}" +
        "return (clientW+gap)/count;" +
        "}" +
        "window.addEventListener('wheel',(e)=>{" +
        "if(!isMultiCol())return;" +
        "if(e.ctrlKey||e.shiftKey||e.altKey)return;" +
        "const md=document.querySelector('.md');" +
        "if(!md)return;" +
        "let target=e.target;" +
        "while(target&&target!==md&&target!==document.body){" +
        "if(target.classList&&(target.classList.contains('json-view')||target.tagName==='PRE')){" +
        "if(target.scrollHeight>target.clientHeight||target.scrollWidth>target.clientWidth)return;" +
        "}" +
        "target=target.parentElement;" +
        "}" +
        "const dir=e.deltaY>0?1:(e.deltaY<0?-1:0);" +
        "if(dir===0)return;" +
        "e.preventDefault();" +
        "const step=getColStep();" +
        "if(typeof window.__targetColumnIndex!=='number'||Math.abs(md.scrollLeft-window.__targetColumnIndex*step)>step*1.5){" +
        "window.__targetColumnIndex=Math.round(md.scrollLeft/step);" +
        "}" +
        "window.__targetColumnIndex=Math.max(0,window.__targetColumnIndex+dir);" +
        "const maxScroll=Math.max(0,md.scrollWidth-md.clientWidth);" +
        "const targetLeft=Math.min(maxScroll,window.__targetColumnIndex*step);" +
        "md.scrollTo({left:targetLeft,behavior:'smooth'});" +
        "},{passive:false});" +
        "window.addEventListener('keydown',(e)=>{" +
        "if(!isMultiCol())return;" +
        "if(e.ctrlKey||e.altKey)return;" +
        "const md=document.querySelector('.md');" +
        "if(!md)return;" +
        "const step=getColStep();let count=2;" +
        "if(document.body.classList.contains('col-3'))count=3;" +
        "else if(document.body.classList.contains('col-auto')){count=Math.max(1,Math.round((md.clientWidth+36)/step));}" +
        "if(e.key==='ArrowRight'||e.key==='Right'){" +
        "e.preventDefault();" +
        "window.__targetColumnIndex=Math.max(0,(window.__targetColumnIndex??Math.round(md.scrollLeft/step))+1);" +
        "const maxScroll=Math.max(0,md.scrollWidth-md.clientWidth);" +
        "md.scrollTo({left:Math.min(maxScroll,window.__targetColumnIndex*step),behavior:'smooth'});" +
        "}else if(e.key==='ArrowLeft'||e.key==='Left'){" +
        "e.preventDefault();" +
        "window.__targetColumnIndex=Math.max(0,(window.__targetColumnIndex??Math.round(md.scrollLeft/step))-1);" +
        "md.scrollTo({left:window.__targetColumnIndex*step,behavior:'smooth'});" +
        "}else if(e.key==='PageDown'){" +
        "e.preventDefault();" +
        "window.__targetColumnIndex=Math.max(0,(window.__targetColumnIndex??Math.round(md.scrollLeft/step))+count);" +
        "const maxScroll=Math.max(0,md.scrollWidth-md.clientWidth);" +
        "md.scrollTo({left:Math.min(maxScroll,window.__targetColumnIndex*step),behavior:'smooth'});" +
        "}else if(e.key==='PageUp'){" +
        "e.preventDefault();" +
        "window.__targetColumnIndex=Math.max(0,(window.__targetColumnIndex??Math.round(md.scrollLeft/step))-count);" +
        "md.scrollTo({left:window.__targetColumnIndex*step,behavior:'smooth'});" +
        "}else if(e.key==='Home'){" +
        "e.preventDefault();window.__targetColumnIndex=0;md.scrollTo({left:0,behavior:'smooth'});" +
        "}else if(e.key==='End'){" +
        "e.preventDefault();const maxScroll=Math.max(0,md.scrollWidth-md.clientWidth);" +
        "window.__targetColumnIndex=Math.ceil(maxScroll/step);" +
        "md.scrollTo({left:maxScroll,behavior:'smooth'});" +
        "}" +
        "});" +
        "window.addEventListener('resize',()=>{" +
        "if(!isMultiCol())return;" +
        "window.__targetColumnIndex=undefined;" +
        "});" +
        "</script>";

}

sealed class SogangMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // 기본 렌더러의 하늘색 체크 배경/테두리를 생략하고 글꼴과 무관한 체크만 그린다.
        if (e.Item is not ToolStripMenuItem item || item.CheckState == CheckState.Unchecked) return;
        var r = e.ImageRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;
        float size = Math.Min(r.Width, r.Height);
        float x = r.Left + (r.Width - size) / 2f;
        float y = r.Top + (r.Height - size) / 2f;
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(item.Enabled ? Brand.Cardinal : SystemColors.GrayText, Math.Max(1.6f, size / 8f))
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round
            };
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(x + size * .20f, y + size * .50f),
                new PointF(x + size * .42f, y + size * .72f),
                new PointF(x + size * .82f, y + size * .28f)
            });
        }
        finally { e.Graphics.Restore(state); }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextFont = Brand.MenuFont;
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var color = e.Item.Enabled ? Color.FromArgb(32, 32, 32) : Color.FromArgb(140, 140, 140);
        TextRenderer.DrawText(
            g, e.Text ?? "", Brand.MenuFont, e.TextRectangle, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

static class Brand
{
    static readonly PrivateFontCollection Fonts = new();
    static IntPtr _fontMem;
    public static FontFamily? Family { get; private set; }
    public static string AssetDir { get; private set; } = "";
    public static AppTheme Theme { get; set; } = AppTheme.Sogang;
    public static Color Cardinal => Theme switch
    {
        AppTheme.Sogang => Color.FromArgb(0xB3, 0x29, 0x2E),
        AppTheme.BlueSky => Color.FromArgb(0x6D, 0xA7, 0xF2),
        AppTheme.ForestGreen => Color.FromArgb(0x2F, 0x6B, 0x4F),
        AppTheme.Albatross => Color.FromArgb(0x43, 0x46, 0x4B),
        _ => Color.FromArgb(30, 30, 30)
    };
    public static Color Wine => Theme switch
    {
        AppTheme.Sogang => Color.FromArgb(0x9E, 0x2A, 0x2F),
        AppTheme.BlueSky => Color.FromArgb(0x03, 0x5A, 0xA6),
        AppTheme.ForestGreen => Color.FromArgb(0x2F, 0x6B, 0x4F),
        AppTheme.Albatross => Color.FromArgb(0x0F, 0x10, 0x12),
        _ => Color.FromArgb(20, 20, 20)
    };
    public static Color Gray5 => Theme switch
    {
        AppTheme.Sogang => Color.FromArgb(0xB1, 0xB3, 0xB6),
        AppTheme.BlueSky => Color.FromArgb(0xA0, 0xC4, 0xF2),
        AppTheme.ForestGreen => Color.FromArgb(0xBD, 0xD5, 0xC5),
        AppTheme.Albatross => Color.FromArgb(0xB0, 0xB3, 0xB8),
        _ => Color.FromArgb(180, 180, 180)
    };
    public static Color Wash => Theme switch
    {
        AppTheme.BlueSky => Color.White,
        AppTheme.ForestGreen => Color.White,
        AppTheme.Albatross => Color.FromArgb(0xFA, 0xFA, 0xF8),
        _ => Color.FromArgb(0xFA, 0xFA, 0xFA)
    };
    public static Color FrameAccent => Theme == AppTheme.Albatross ? Color.FromArgb(0x0F, 0x10, 0x12) : Cardinal;
    public static Color Paper => Color.White;
    public static Color Ink => Theme switch
    {
        AppTheme.BlueSky => Color.FromArgb(0x02, 0x33, 0x73),
        AppTheme.Albatross => Color.FromArgb(0x0F, 0x10, 0x12),
        AppTheme.ForestGreen => Color.FromArgb(0x25, 0x33, 0x2B),
        _ => Color.FromArgb(0x2A, 0x2A, 0x2A)
    };
    public static Color Mute => Theme switch
    {
        AppTheme.Sogang => Color.FromArgb(0x6A, 0x6C, 0x6E),
        AppTheme.BlueSky => Color.FromArgb(0x03, 0x5A, 0xA6),
        AppTheme.Albatross => Color.FromArgb(0x43, 0x46, 0x4B),
        AppTheme.ForestGreen => Color.FromArgb(0x5C, 0x73, 0x65),
        _ => Color.FromArgb(90, 90, 90)
    };
    public static string CheckboxAccentHex => Theme switch
    {
        AppTheme.Sogang => "#b3292e",
        AppTheme.BlueSky => "#6da7f2",
        AppTheme.ForestGreen => "#2f6b4f",
        AppTheme.Albatross => "#43464b",
        _ => "#222222"
    };

    public static string CursorActiveAccentHex => Theme switch
    {
        AppTheme.Sogang => "#b3292e",
        AppTheme.BlueSky => "#6da7f2",
        AppTheme.ForestGreen => "#2f6b4f",
        AppTheme.Albatross => "#43464b",
        _ => "#333333"
    };


    public static string PreviewCss => Theme switch
    {
        AppTheme.Sogang =>
            "html,body{margin:0;background:#fafafa;color:#2a2a2a;width:100%}" +
            "body{font:15.5px/1.6 'SogangUni','Malgun Gothic',sans-serif;overflow-x:hidden}" +
            ".md{max-width:46rem;width:100%;margin:0 auto;padding:24px 20px 56px;box-sizing:border-box}" +
            "h1,h2,h3{line-height:1.25;color:#9e2a2f} h1{font-size:1.85rem} h2{font-size:1.3rem;border-bottom:1px solid #b1b3b6;padding-bottom:.2em}" +
            "p{margin:0.8em 0;line-height:1.6}" +
            "ul,ol{margin:0.6em 0;padding-left:1.5em;line-height:1.6}" +
            "li{margin:0.25em 0}" +
            "a{color:#b3292e} code{font-family:Consolas,monospace;font-size:.88em;background:#f0f0f1;padding:.1em .35em;border-radius:6px}" +
            "pre{background:#2a2a2a;color:#f3f3f3;border-radius:12px;padding:10px 14px;white-space:pre-wrap;word-break:break-word}" +
            "pre code{background:none;padding:0;color:inherit}" +
            "blockquote{margin:0.8em 0;padding:.2em 0 .2em 1em;border-left:3px solid #b3292e;color:#6a6c6e}" +
            "table{border-collapse:collapse;width:100%;margin:0.8em 0;background:#fff}" +
            "th,td{border:1px solid #b1b3b6;padding:.4em .6em} th{background:#f3e8e9;color:#9e2a2f;text-align:left}" +
            "img{max-width:100%}" +
            ".katex-display{margin:0.8em 0;overflow-x:auto;overflow-y:hidden;text-align:center}",

        AppTheme.BlueSky =>
            "html,body{margin:0;background:#ffffff;color:#023373;width:100%}" +
            "body{font:15.5px/1.6 'SogangUni','Malgun Gothic',sans-serif;overflow-x:hidden}" +
            ".md{max-width:46rem;width:100%;margin:0 auto;padding:24px 20px 56px;box-sizing:border-box}" +
            "h1,h2,h3{line-height:1.25;color:#035aa6} h1{font-size:1.85rem} h2{font-size:1.3rem;border-bottom:1px solid #a0c4f2;padding-bottom:.2em}" +
            "p{margin:0.8em 0;line-height:1.6}" +
            "ul,ol{margin:0.6em 0;padding-left:1.5em;line-height:1.6}" +
            "li{margin:0.25em 0}" +
            "a{color:#035aa6;text-decoration:none} a:hover{text-decoration:underline}" +
            "code{font-family:Consolas,monospace;font-size:.88em;background:#cedef2;color:#035aa6;padding:.1em .35em;border-radius:6px}" +
            "pre{background:#023373;color:#f8fafc;border-radius:12px;padding:10px 14px;white-space:pre-wrap;word-break:break-word}" +
            "pre code{background:none;padding:0;color:inherit}" +
            "blockquote{margin:0.8em 0;padding:.2em 0 .2em 1em;border-left:3px solid #6da7f2;color:#035aa6;background:#cedef2;border-radius:0 6px 6px 0;}" +
            "table{border-collapse:collapse;width:100%;margin:0.8em 0;background:#ffffff}" +
            "th,td{border:1px solid #a0c4f2;padding:.4em .6em} th{background:#cedef2;color:#035aa6;text-align:left}" +
            "img{max-width:100%}" +
            ".katex-display{margin:0.8em 0;overflow-x:auto;overflow-y:hidden;text-align:center}",

        // 포레스트 그린: 흰 배경, 서강 글꼴, 짙은 초록 포인트.
        AppTheme.ForestGreen =>
            "html,body{margin:0;background:#ffffff;color:#25332b;width:100%}" +
            "body{font:15.5px/1.6 'SogangUni','Malgun Gothic',sans-serif;overflow-x:hidden}" +
            ".md{max-width:46rem;width:100%;margin:0 auto;padding:24px 20px 56px;box-sizing:border-box}" +
            "h1,h2,h3{line-height:1.25;color:#2f6b4f} h1{font-size:1.85rem} h2{font-size:1.3rem;border-bottom:1px solid #bdd5c5;padding-bottom:.2em}" +
            "p{margin:0.8em 0;line-height:1.6}" +
            "ul,ol{margin:0.6em 0;padding-left:1.5em;line-height:1.6}" +
            "li{margin:0.25em 0}" +
            "a{color:#2f6b4f;text-decoration:none} a:hover{text-decoration:underline}" +
            "code{font-family:Consolas,monospace;font-size:.88em;background:#eaf4ee;color:#2f6b4f;padding:.1em .35em;border-radius:6px}" +
            "pre{background:#25332b;color:#f5faf7;border-radius:12px;padding:10px 14px;white-space:pre-wrap;word-break:break-word}" +
            "pre code{background:none;padding:0;color:inherit}" +
            "blockquote{margin:0.8em 0;padding:.2em 0 .2em 1em;border-left:3px solid #2f6b4f;color:#5c7365;background:#eaf4ee;border-radius:0 6px 6px 0;}" +
            "table{border-collapse:collapse;width:100%;margin:0.8em 0;background:#fff}" +
            "th,td{border:1px solid #bdd5c5;padding:.4em .6em} th{background:#eaf4ee;color:#2f6b4f;text-align:left}" +
            "img{max-width:100%}" +
            ".katex-display{margin:0.8em 0;overflow-x:auto;overflow-y:hidden;text-align:center}",

        AppTheme.Albatross =>
            "html,body{margin:0;background:#fafaf8;color:#0f1012;width:100%}" +
            "body{font:15.5px/1.6 'Segoe UI','Malgun Gothic',sans-serif;overflow-x:hidden}" +
            ".md{max-width:46rem;width:100%;margin:0 auto;padding:24px 20px 56px;box-sizing:border-box}" +
            "h1,h2,h3{line-height:1.25;color:#43464b} h1{font-size:1.85rem} h2{font-size:1.3rem;border-bottom:1px solid #b0b3b8;padding-bottom:.2em}" +
            "p{margin:0.8em 0;line-height:1.6}" +
            "ul,ol{margin:0.6em 0;padding-left:1.5em;line-height:1.6}" +
            "li{margin:0.25em 0}" +
            "a{color:#43464b} code{font-family:Consolas,monospace;font-size:.88em;background:#dadada;color:#43464b;padding:.1em .35em;border-radius:6px}" +
            "pre{background:#0f1012;color:#fafaf8;border-radius:12px;padding:10px 14px;white-space:pre-wrap;word-break:break-word}" +
            "pre code{background:none;padding:0;color:inherit}" +
            "blockquote{margin:0.8em 0;padding:.2em 0 .2em 1em;border-left:3px solid #43464b;color:#43464b;background:#dadada;border-radius:0 6px 6px 0;}" +
            "table{border-collapse:collapse;width:100%;margin:0.8em 0;background:#fafaf8}" +
            "th,td{border:1px solid #b0b3b8;padding:.4em .6em} th{background:#dadada;color:#43464b;text-align:left}" +
            "img{max-width:100%}" +
            ".katex-display{margin:0.8em 0;overflow-x:auto;overflow-y:hidden;text-align:center}",

        _ =>
            "html,body{margin:0;background:#fafafa;color:#1a1a1a;width:100%}" +
            "body{font:15.5px/1.6 'Segoe UI','Malgun Gothic',sans-serif;overflow-x:hidden}" +
            ".md{max-width:46rem;width:100%;margin:0 auto;padding:24px 20px 56px;box-sizing:border-box}" +
            "h1,h2,h3{line-height:1.25;color:#111} h1{font-size:1.85rem} h2{font-size:1.3rem;border-bottom:1px solid #ccc;padding-bottom:.2em}" +
            "p{margin:0.8em 0;line-height:1.6}" +
            "ul,ol{margin:0.6em 0;padding-left:1.5em;line-height:1.6}" +
            "li{margin:0.25em 0}" +
            "a{color:#222} code{font-family:Consolas,monospace;font-size:.88em;background:#f0f0f0;padding:.1em .35em;border-radius:6px}" +
            "pre{background:#f3f3f3;color:#111;border-radius:12px;padding:10px 14px;white-space:pre-wrap;word-break:break-word}" +
            "pre code{background:none;padding:0;color:inherit}" +
            "blockquote{margin:0.8em 0;padding:.2em 0 .2em 1em;border-left:3px solid #888;color:#555}" +
            "table{border-collapse:collapse;width:100%;margin:0.8em 0;background:#fff}" +
            "th,td{border:1px solid #ccc;padding:.4em .6em} th{background:#f0f0f0;color:#111;text-align:left}" +
            "img{max-width:100%}" +
            ".katex-display{margin:0.8em 0;overflow-x:auto;overflow-y:hidden;text-align:center}"    };

    static string ThemePath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDviewer", "theme.txt");

    public static void Init()
    {
        try
        {
            AssetDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MDviewer");
            Directory.CreateDirectory(AssetDir);
            using var s = OpenRes("SOGANG_UNIVERSITY_for_windows.ttf");
            if (s != null)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var bytes = ms.ToArray();
                var ttf = System.IO.Path.Combine(AssetDir, "sogang.ttf");
                File.WriteAllBytes(ttf, bytes);
                Native.AddFontResourceEx(ttf, 0x10, IntPtr.Zero);
                Fonts.AddFontFile(ttf);
                if (Fonts.Families.Length > 0) Family = Fonts.Families[0];
                _fontMem = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, _fontMem, bytes.Length);
                try { Fonts.AddMemoryFont(_fontMem, bytes.Length); } catch { }
            }
        }
        catch { }
        LoadTheme();
    }

    public static void LoadTheme()
    {
        try
        {
            var p = ThemePath;
            if (File.Exists(p))
            {
                var txt = File.ReadAllText(p).Trim();
                if (txt.Equals("Albatross", StringComparison.OrdinalIgnoreCase))
                    Theme = AppTheme.Albatross;
                else if (txt.Equals("BlueSky", StringComparison.OrdinalIgnoreCase))
                    Theme = AppTheme.BlueSky;
                else if (txt.Equals("ForestGreen", StringComparison.OrdinalIgnoreCase))
                    Theme = AppTheme.ForestGreen;
                else
                    Theme = AppTheme.Sogang;
            }
        }
        catch { }
    }

    public static void SaveTheme()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ThemePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ThemePath, Theme.ToString());
        }
        catch { }
    }

    public static ColumnMode ColumnViewMode { get; set; } = ColumnMode.Single;
    static Font? _menuSogang;
    static Font? _menuAlba;
    public static bool UsesSogangFont => Theme is AppTheme.Sogang or AppTheme.BlueSky or AppTheme.ForestGreen;
    public static Font MenuFont => UsesSogangFont
        ? (_menuSogang ??= Sogang(9f))
        : (_menuAlba ??= new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point));

    public static Font Sogang(float em, FontStyle style = FontStyle.Regular)
    {
        try
        {
            if (Family != null) return new Font(Family, em, style, GraphicsUnit.Point);
        }
        catch { }
        return new Font("Malgun Gothic", em, style, GraphicsUnit.Point);
    }

    public static Font Ui(float em, FontStyle style = FontStyle.Regular)
    {
        try
        {
            if (UsesSogangFont && Family != null)
                return new Font(Family, em, style);
        }
        catch { }
        return new Font("Segoe UI", em, style);
    }

    public static Font Editor(float em) =>
        new Font("Consolas", em, FontStyle.Regular, GraphicsUnit.Point);

    public static Image? ResImage(string file)
    {
        try
        {
            using var s = OpenRes(file);
            if (s != null) return Image.FromStream(s);
        }
        catch { }
        try
        {
            if (File.Exists(file)) return Image.FromFile(file);
            var p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
            if (File.Exists(p)) return Image.FromFile(p);
            var pAssets = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", file);
            if (File.Exists(pAssets)) return Image.FromFile(pAssets);
        }
        catch { }
        return null;
    }

    public static Icon? AppIcon()
    {
        try
        {
            using var s = OpenRes("app.ico");
            if (s != null) return new Icon(s);
        }
        catch { }
        return null;
    }

    public static Image? Logo()
    {
        try
        {
            using var s = OpenRes("footer.png") ?? OpenRes("logo.png");
            if (s != null) return Image.FromStream(s);
        }
        catch { }
        try
        {
            var pic = new PictureBox { WaitOnLoad = false };
            pic.LoadAsync("https://sgmot.sogang.ac.kr/front_dept_r/module_sample/header/003/img/logo.png");
        }
        catch { }
        return null;
    }

    static Stream? OpenRes(string file)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        return name == null ? null : asm.GetManifestResourceStream(name);
    }
}

static class Native
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("shell32.dll")] public static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);
    [DllImport("shell32.dll")] public static extern void DragFinish(IntPtr hDrop);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWndParent, EnumProc lpEnumFunc, IntPtr lParam);
    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    public static void NotifyAssocChanged() => SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);
    public static void AcceptTree(IntPtr hwnd)
    {
        DragAcceptFiles(hwnd, true);
        EnumChildWindows(hwnd, (child, _) =>
        {
            DragAcceptFiles(child, true);
            return true;
        }, IntPtr.Zero);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);
    public static uint DropCount(IntPtr hDrop) => DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
    public static string? DroppedFile(IntPtr hDrop, uint index)
    {
        var sb = new StringBuilder(1024);
        _ = DragQueryFile(hDrop, index, sb, (uint)sb.Capacity);
        var path = sb.ToString();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    const int EmGetFirstVisibleLine = 0x00CE;
    const int EmLineScroll = 0x00B6;

    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    public static int EditorFirstLine(RichTextBox box)
    {
        if (!box.IsHandleCreated || box.TextLength == 0) return 0;
        return Math.Max(0, SendMessage(box.Handle, EmGetFirstVisibleLine, 0, 0));
    }

    public static void SetEditorFirstLine(RichTextBox box, int line)
    {
        if (!box.IsHandleCreated) return;
        int current = SendMessage(box.Handle, EmGetFirstVisibleLine, 0, 0);
        int delta = line - current;
        if (delta != 0)
        {
            SendMessage(box.Handle, EmLineScroll, 0, delta);
        }
    }

    public static double EditorScrollRatio(RichTextBox box)
    {
        if (!box.IsHandleCreated || box.Lines.Length <= 1) return 0;
        int first = EditorFirstLine(box);
        int visible = Math.Max(1, box.ClientSize.Height / Math.Max(1, box.Font.Height));
        int max = Math.Max(1, box.Lines.Length - visible);
        return Math.Clamp(first / (double)max, 0, 1);
    }

    public static void SetEditorScrollRatio(RichTextBox box, double ratio)
    {
        if (!box.IsHandleCreated || box.Lines.Length <= 1) return;
        int visible = Math.Max(1, box.ClientSize.Height / Math.Max(1, box.Font.Height));
        int max = Math.Max(1, box.Lines.Length - visible);
        int target = (int)Math.Round(Math.Clamp(ratio, 0, 1) * max);
        SetEditorFirstLine(box, target);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PARAFORMAT2
    {
        public int cbSize;
        public uint dwMask;
        public short wNumbering;
        public short wEffects;
        public int dxStartIndent;
        public int dxRightIndent;
        public int dxOffset;
        public short wAlignment;
        public short cTabCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] rgxTabs;
        public int dySpaceBefore;
        public int dySpaceAfter;
        public int dyLineSpacing;
        public byte bLineSpacingRule;
        public byte bOutlineLevel;
        public short wShadingWeight;
        public short wShadingStyle;
        public short wNumberingStart;
        public short wNumberingStyle;
        public short wNumberingTab;
        public short wBorderSpace;
        public short wBorderWidth;
        public short wBorders;
    }

    const int EM_SETPARAFORMAT = 0x0447;
    const uint PFM_LINESPACING = 0x00000100;
    const int SCF_ALL = 0x0004;

    [DllImport("user32.dll", EntryPoint = "SendMessage")]
    static extern IntPtr SendMessagePara(IntPtr hWnd, int msg, IntPtr wParam, ref PARAFORMAT2 lParam);

    public static void ApplyLineSpacing(RichTextBox box, float multiple = 1.4f)
    {
        if (!box.IsHandleCreated) return;
        var fmt = new PARAFORMAT2();
        fmt.cbSize = Marshal.SizeOf(fmt);
        fmt.dwMask = PFM_LINESPACING;
        fmt.bLineSpacingRule = 5;
        fmt.dyLineSpacing = (int)Math.Round(multiple * 20);
        SendMessagePara(box.Handle, EM_SETPARAFORMAT, (IntPtr)SCF_ALL, ref fmt);
    }
}

sealed class ZoomOsdLabel : Control
{
    public ZoomOsdLabel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(58, 26);
        Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        ForeColor = Color.White;
        BackColor = Color.Transparent;
        Visible = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(225, 30, 34, 42));
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 6);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
