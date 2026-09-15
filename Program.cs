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
enum AppTheme { Sogang, Albatross }

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
                                    if (File.Exists(f) && MainForm.IsMd(f))
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
                                    if (File.Exists(f) && MainForm.IsMd(f))
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
    readonly PictureBox _campus = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
    readonly PictureBox _bird = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    readonly PictureBox _logoFooter = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    readonly Label _intro = new();
    readonly Label _link = new();
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
            if (e.Shift && e.KeyCode == Keys.F8)
            {
                e.Handled = true;
                Current?.AddSelectedToHighlight();
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
                if (File.Exists(a) && IsMd(a)) OpenFile(a);
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
        if (on)
        {
            using var bar = new SolidBrush(Brand.Cardinal);
            g.FillRectangle(bar, r.X + 8, r.Bottom - 3, r.Width - 16, 3);
        }
        var font = Brand.Ui(8f, on ? FontStyle.Bold : FontStyle.Regular);
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
                if (!_menu.Bounds.Contains(Cursor.Position))
                {
                    _menu.Close();
                }
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
        int delta = unchecked((short)((wp >> 16) & 0xFFFF));
        Current?.AdjustZoom(delta > 0 ? 0.08 : -0.08);
        return true;
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
            foreach (var f in files.Where(IsMd)) OpenFile(f);
    }

    FileTab? Current => _tabs.SelectedTab?.Tag as FileTab;

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
                if (path != null && IsMd(path)) OpenFile(path);
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
        _link.Text = "md 파일 연결프로그램 등록";
        _link.AutoSize = true;
        _link.Cursor = Cursors.Hand;
        _link.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _link.BackColor = Color.Transparent;
        _link.UseCompatibleTextRendering = true;
        _link.Click += (_, _) => RegisterMdAssociation();
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
            "- Shift + F8  단어 강조\n" +
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
        _home.Controls.Add(_link);
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
        _link.UseCompatibleTextRendering = true;
        _helpTitle.UseCompatibleTextRendering = true;
        _help.UseCompatibleTextRendering = true;
        _helpMouseTitle.UseCompatibleTextRendering = true;
        _helpMouse.UseCompatibleTextRendering = true;
        bool sg = Brand.Theme == AppTheme.Sogang;
        Font F(float em, FontStyle st = FontStyle.Regular) =>
            sg ? Brand.Sogang(em, st) : new Font("Segoe UI", em, st, GraphicsUnit.Point);
        _intro.Font = F(10f);
        _link.Font = F(10f, FontStyle.Underline);
        _helpTitle.Font = F(10f, FontStyle.Bold);
        _help.Font = F(8f);
        _helpMouseTitle.Font = F(10f, FontStyle.Bold);
        _helpMouse.Font = F(8f);
        _intro.ForeColor = Brand.Ink;
        _link.ForeColor = Brand.Cardinal;
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
        _link.Location = new Point(_intro.Left, _intro.Bottom + _intro.Font.Height);
        int blockH = _helpTitle.Height + 2 + Math.Max(_help.Height, _helpMouse.Height);
        int helpY = Math.Max(80, h - blockH - 8);
        _helpTitle.Location = new Point(28, helpY);
        _help.Location = new Point(28, helpY + _helpTitle.Height + 2);
        int mouseX = Math.Max(_helpTitle.Right, _help.Right) + 48;
        _helpMouseTitle.Location = new Point(mouseX, helpY);
        _helpMouse.Location = new Point(mouseX, helpY + _helpMouseTitle.Height + 2);
        _bird.BringToFront();
        _intro.BringToFront();
        _link.BringToFront();
        _helpTitle.BringToFront();
        _help.BringToFront();
        _helpMouseTitle.BringToFront();
        _helpMouse.BringToFront();
        _logoFooter.BringToFront();
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
        _miFind = new ToolStripMenuItem("검색 (Ctrl+F)", null, (_, _) => Current?.TriggerFind());
        _miHighlight = new ToolStripMenuItem("강조 키워드 (Shift+F8)") { CheckOnClick = true };
        _miHighlight.CheckedChanged += (_, _) =>
        {
            if (_menuBusy || Current == null) return;
            Current.HighlightBarVisible = _miHighlight.Checked;
        };
        _menu.Items.Add(_miFind);
        _menu.Items.Add(_miHighlight);
        _menu.Items.Add(new ToolStripSeparator());
        _miEdit = new ToolStripMenuItem("편집 모드") { CheckOnClick = true };
        _miVert = new ToolStripMenuItem("세로 분할") { CheckOnClick = true };
        _miHorz = new ToolStripMenuItem("가로 분할") { CheckOnClick = true };
        _miEdit.CheckedChanged += (_, _) =>
        {
            if (_menuBusy || Current == null) return;
            Current.EditMode = _miEdit.Checked;
            if (_miEdit.Checked && Current.Split == SplitMode.None)
                Current.Split = SplitMode.Vertical;
            if (!_miEdit.Checked && Current.Split != SplitMode.None)
                Current.RefreshPreview();
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
        _miSogang = new ToolStripMenuItem("서강 테마") { CheckOnClick = true };
        _miAlba = new ToolStripMenuItem("알바트로스 테마") { CheckOnClick = true };
        _miSogang.CheckedChanged += (_, _) =>
        {
            if (_menuBusy || !_miSogang.Checked) return;
            SetTheme(AppTheme.Sogang);
        };
        _miAlba.CheckedChanged += (_, _) =>
        {
            if (_menuBusy || !_miAlba.Checked) return;
            SetTheme(AppTheme.Albatross);
        };
        _menu.Items.Add(_miSogang);
        _menu.Items.Add(_miAlba);
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
            if (_miSogang != null) _miSogang.Checked = Brand.Theme == AppTheme.Sogang;
            if (_miAlba != null) _miAlba.Checked = Brand.Theme == AppTheme.Albatross;
            _menuBusy = false;
        };
    }

    void SetTheme(AppTheme theme)
    {
        if (Brand.Theme == theme) return;
        Brand.Theme = theme;
        Brand.SaveTheme();
        ApplyTheme();
    }

    void ApplyTheme()
    {
        BackColor = Brand.Wash;
        _home.BackColor = Brand.Wash;
        _tabs.BackColor = Brand.Wash;
        bool sg = Brand.Theme == AppTheme.Sogang;
        _banner.Visible = false;
        _footBand.Visible = true;
        _footBand.BackColor = sg ? Brand.Cardinal : Color.FromArgb(12, 12, 12);
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
            foreach (ToolStripItem it in _menu.Items)
                it.Font = f;
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
            Filter = "Markdown (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
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

    public static bool IsMdPublic(string path) => IsMd(path);

    public static string? FirstMd(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return null;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return null;
        return files.FirstOrDefault(IsMd);
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
            BackColor = Color.White
        };
        var img = Brand.ResImage("스크린샷_기본앱등록.png") ?? Brand.ResImage("assoc_guide.png");
        if (img != null) pic.Image = img;

        var centerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 10, 20, 10) };
        centerPanel.Controls.Add(pic);

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(20, 10, 20, 12), BackColor = Brand.Wash };
        var btnOpen = new Button
        {
            Text = "기본 앱 설정 열기",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Width = 180,
            Height = 34,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Brand.Theme == AppTheme.Sogang ? Brand.Cardinal : Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = Brand.Ui(9.5f, FontStyle.Bold)
        };
        btnOpen.FlatAppearance.BorderSize = 0;
        btnOpen.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            }
            catch
            {
                Process.Start(new ProcessStartInfo("control", "/name Microsoft.DefaultPrograms") { UseShellExecute = true });
            }
        };

        var btnClose = new Button
        {
            Text = "닫기",
            Width = 80,
            Height = 34,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(230, 230, 230),
            ForeColor = Color.FromArgb(40, 40, 40),
            Cursor = Cursors.Hand,
            Font = Brand.Ui(9f)
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Click += (_, _) => Close();

        var btnSpace = new Panel { Dock = DockStyle.Right, Width = 10 };

        bottomPanel.Controls.Add(btnOpen);
        bottomPanel.Controls.Add(btnSpace);
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
    readonly SplitContainer _split = new();
    readonly Panel _hlBar = new() { Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 3, 8, 3), Visible = false };
    readonly Label _lblHl = new() { Text = "강조키워드:", AutoSize = true, Dock = DockStyle.Left, Padding = new Padding(0, 3, 6, 0) };
    readonly TextBox _txtHl = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly Panel _editPane = new() { Dock = DockStyle.Fill };
    readonly Panel _gutter = new();
    readonly RichTextBox _text = new();
    readonly Panel _viewPane = new() { Dock = DockStyle.Fill };
    readonly Panel _viewPane2 = new() { Dock = DockStyle.Fill };
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, AllowExternalDrop = true };
    readonly WebView2 _web2 = new() { Dock = DockStyle.Fill, AllowExternalDrop = true };
    readonly FileSystemWatcher _watch = new();
    readonly System.Windows.Forms.Timer _previewTick = new() { Interval = 280 };
    readonly MarkdownPipeline _md = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UsePipeTables()
        .UseGridTables()
        .Build();

    bool _editMode;
    double _zoom = 1;
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
            _text.ReadOnly = !value;
            ApplyLayout();
            RefreshPreview();
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
        _text.Font = Brand.Editor(11f);
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
            if (e.Shift && e.KeyCode == Keys.F8)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                AddSelectedToHighlight();
            }
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
            _previewTick.Stop();
            _previewTick.Start();
        };

        _editPane.Controls.Add(_text);
        _editPane.Controls.Add(_gutter);
        _viewPane.Controls.Add(_web);
        _viewPane2.Controls.Add(_web2);

        _split.Dock = DockStyle.Fill;
        _split.SplitterWidth = 6;
        Controls.Add(_split);
        Controls.Add(_hlBar);
        _hlBar.BringToFront();

        _previewTick.Tick += (_, _) =>
        {
            _previewTick.Stop();
            RefreshPreview();
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
        Reload();
        ApplyLayout();
    }

    public void AttachMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        _text.ContextMenuStrip = menu;
        _gutter.ContextMenuStrip = menu;
        _editPane.ContextMenuStrip = menu;
        _viewPane.ContextMenuStrip = menu;
        _viewPane2.ContextMenuStrip = menu;
        _split.ContextMenuStrip = menu;
        _hlBar.ContextMenuStrip = menu;
        _lblHl.ContextMenuStrip = menu;
        _txtHl.ContextMenuStrip = menu;
    }

    public void ApplyChrome()
    {
        BackColor = Brand.Wash;
        _hlBar.BackColor = Brand.Wash;
        _editPane.BackColor = Brand.Wash;
        _gutter.BackColor = Color.FromArgb(240, 240, 241);
        _text.ForeColor = Brand.Ink;
        _text.BackColor = Brand.Paper;
        _text.Font = Brand.Editor((float)(11.0 * _zoom));
        RefreshPreview();
        _gutter.Invalidate();
    }

    void OnEditorDrag(object? sender, DragEventArgs e)
    {
        e.Effect = MainForm.FirstMd(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnEditorDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files.Where(MainForm.IsMdPublic))
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
            RefreshPreview();
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
                foreach (var f in files.Where(MainForm.IsMdPublic))
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
            if (_editMode) BeginInvoke(SyncEditorToWeb);
            BeginInvoke(ApplyHighlightKeywords);
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
                if (raw.Contains("\"t\":\"esc\"", StringComparison.Ordinal))
                {
                    BeginInvoke(() =>
                    {
                        if (FindForm() is MainForm f && f.IsMenuVisible) f.CloseMenu();
                    });
                    return;
                }
                if (raw.Contains("\"t\":\"shiftF8\"", StringComparison.Ordinal))
                {
                    var sel = "";
                    var sIdx = raw.IndexOf("\"sel\":", StringComparison.Ordinal);
                    if (sIdx >= 0)
                    {
                        var restSel = raw[(sIdx + 6)..].Trim();
                        if (restSel.StartsWith('"'))
                        {
                            var end = restSel.IndexOf('"', 1);
                            if (end > 1) sel = restSel[1..end];
                        }
                    }
                    BeginInvoke(() =>
                    {
                        if (string.IsNullOrEmpty(sel)) AddSelectedToHighlight();
                        else AddHighlightKeyword(Regex.Unescape(sel));
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
                if (raw.Contains("\"t\":\"zoom\"", StringComparison.Ordinal))
                {
                    var up = raw.Contains("\"d\":1");
                    BeginInvoke(() => AdjustZoom(up ? 0.08 : -0.08));
                    return;
                }
                var key = "\"ln\":";
                var i = raw.IndexOf(key, StringComparison.Ordinal);
                if (i >= 0)
                {
                    var num = raw[(i + key.Length)..].Trim().TrimEnd('}');
                    if (int.TryParse(num, out var ln))
                        SyncWebToEditorLine(ln);
                    return;
                }
                key = "\"h\":";
                i = raw.IndexOf(key, StringComparison.Ordinal);
                if (i < 0) return;
                var rest = raw[(i + key.Length)..].Trim();
                if (rest.StartsWith('"'))
                {
                    var end = rest.IndexOf('"', 1);
                    if (end > 1) SyncWebToEditorHeading(rest[1..end]);
                }
            }
            catch { }
        };
    }

    DateTime _zoomAt;

    public void AdjustZoom(double delta)
    {
        if ((DateTime.UtcNow - _zoomAt).TotalMilliseconds < 40) return;
        _zoomAt = DateTime.UtcNow;
        _zoom = Math.Clamp(_zoom + delta, 0.6, 2.4);
        ApplyWebZoom();
        _text.Font = Brand.Editor((float)(11.0 * _zoom));
        _gutter.Invalidate();
    }

    void ApplyWebZoom()
    {
        var z = _zoom.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var js = "document.documentElement.style.zoom='" + z + "';";
        try
        {
            if (_web.CoreWebView2 != null) _ = _web.CoreWebView2.ExecuteScriptAsync(js);
            if (_web2.CoreWebView2 != null) _ = _web2.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch { }
    }

    void SyncEditorToWeb()
    {
        if (_syncing || !_ready || !_editMode || _web.CoreWebView2 == null) return;
        _syncing = true;
        var orig = Native.EditorFirstLine(_text);
        var norm = ToNormLine(_text.Text, orig) + 1;
        _ = _web.ExecuteScriptAsync("scrollToSrcLine(" + norm + ")");
        _syncing = false;
    }

    void SyncWebToEditorLine(int normLine)
    {
        if (_syncing || !_editMode) return;
        _syncing = true;
        var orig = FromNormLine(_text.Text, Math.Max(1, normLine) - 1);
        Native.SetEditorFirstLine(_text, orig);
        _gutter.Invalidate();
        _syncing = false;
    }

    void SyncWebToEditorHeading(string heading)
    {
        if (_syncing || !_editMode || string.IsNullOrWhiteSpace(heading)) return;
        _syncing = true;
        var line = FindHeadingLine(_text, heading);
        if (line >= 0) Native.SetEditorFirstLine(_text, line);
        _gutter.Invalidate();
        _syncing = false;
    }

    static string? HeadingAtOrAbove(RichTextBox box, int line)
    {
        var lines = box.Lines;
        if (lines.Length == 0) return null;
        line = Math.Clamp(line, 0, lines.Length - 1);
        for (int i = line; i >= 0; i--)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith('#'))
                return t.TrimStart('#').Trim();
        }
        return null;
    }

    static int FindHeadingLine(RichTextBox box, string heading)
    {
        heading = heading.Trim();
        var lines = box.Lines;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (!t.StartsWith('#')) continue;
            var title = t.TrimStart('#').Trim();
            if (HeadingKey(title) == HeadingKey(heading) ||
                title == heading || title.StartsWith(heading) || heading.StartsWith(title))
                return i;
        }
        return -1;
    }

    static string HeadingKey(string s)
    {
        s = (s ?? "").Trim();
        var i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return i > 0 ? s[..i] : s;
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

    public async void AddSelectedToHighlight()
    {
        if (_editMode && _text.Focused && !string.IsNullOrWhiteSpace(_text.SelectedText))
        {
            AddHighlightKeyword(_text.SelectedText.Trim());
            return;
        }
        if (_web.CoreWebView2 != null)
        {
            try
            {
                var res = await _web.CoreWebView2.ExecuteScriptAsync("window.getSelection() ? window.getSelection().toString() : ''");
                var sel = UnquoteJs(res).Trim();
                if (!string.IsNullOrEmpty(sel))
                {
                    AddHighlightKeyword(sel);
                    return;
                }
            }
            catch { }
        }
        if (!string.IsNullOrWhiteSpace(_text.SelectedText))
        {
            AddHighlightKeyword(_text.SelectedText.Trim());
        }
    }

    static string UnquoteJs(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        if (s.StartsWith('"') && s.EndsWith('"') && s.Length >= 2)
            s = s[1..^1];
        return Regex.Unescape(s);
    }

    public void AddHighlightKeyword(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        word = word.Trim();
        _hlBar.Visible = true;
        var current = _txtHl.Text.Trim();
        var existing = current.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!existing.Any(x => string.Equals(x, word, StringComparison.OrdinalIgnoreCase)))
        {
            _txtHl.Text = string.IsNullOrEmpty(current) ? word : current + " " + word;
        }
        _txtHl.Focus();
        _txtHl.SelectionStart = _txtHl.TextLength;
        _txtHl.SelectionLength = 0;
        ApplyHighlightKeywords();
    }

    public void TriggerFind()
    {
        if (_editMode) _text.Focus();
        else _web.Focus();
        SendKeys.Send("^f");
    }

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
            ? Orientation.Horizontal
            : Orientation.Vertical;
        BeginInvoke(() =>
        {
            if (_split.Width < 20 || _split.Height < 20) return;
            if (_split.Orientation == Orientation.Vertical)
                _split.SplitterDistance = Math.Max(80, _split.Width / 2);
            else
                _split.SplitterDistance = Math.Max(80, _split.Height / 2);
        });
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

    public void SaveDocx()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Word 문서 (*.docx)|*.docx",
            FileName = (string.IsNullOrEmpty(Path) ? "untitled" : System.IO.Path.GetFileNameWithoutExtension(Path)) + ".docx",
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            WordExport.Save(_text.Text, dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Word 저장 실패");
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
            RefreshPreview();
            return;
        }
        try
        {
            using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8, true);
            _loaded = sr.ReadToEnd();
            var pos = _text.SelectionStart;
            _text.Text = _loaded;
            Dirty = false;
            _text.SelectionStart = Math.Min(pos, _text.TextLength);
            _gutter.Width = GutterWidth(_text);
            _gutter.Invalidate();
            RefreshPreview();
        }
        catch
        {
            _text.Text = "(cannot read file)";
        }
    }

    public void RefreshPreview()
    {
        if (!_ready) return;
        try
        {
            var html = RenderHtml(_text.Text);
            var dir = string.IsNullOrEmpty(Path) ? Environment.CurrentDirectory : System.IO.Path.GetDirectoryName(Path)!;
            html = RewriteLocalImages(html, dir);
            var doc = WrapHtml(html);
            if (_web.CoreWebView2 != null) _web.CoreWebView2.NavigateToString(doc);
            if (_web2.CoreWebView2 != null) _web2.CoreWebView2.NavigateToString(doc);
            BeginInvoke(ApplyWebZoom);
        }
        catch { }
    }

    int GutterWidth(RichTextBox box)
    {
        var lines = Math.Max(1, box.Lines.Length);
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
        for (int line = firstLine; line <= lastLine + 1; line++)
        {
            int index = box.GetFirstCharIndexFromLine(line);
            if (index < 0) break;
            var pt = box.GetPositionFromCharIndex(index);
            if (pt.Y > box.ClientSize.Height) break;
            var label = (line + 1).ToString();
            var size = TextRenderer.MeasureText(label, box.Font);
            TextRenderer.DrawText(
                e.Graphics, label, box.Font,
                new Point(gutter.Width - size.Width - 6, pt.Y),
                Color.FromArgb(150, 150, 150), TextFormatFlags.NoPadding);
        }
        e.Graphics.DrawLine(Pens.Gainsboro, gutter.Width - 1, 0, gutter.Width - 1, gutter.Height);
    }

    static string RewriteLocalImages(string html, string dir)
    {
        return Regex.Replace(
            html,
            """(?i)(<img\b[^>]*?\bsrc\s*=\s*["'])(?!https?:|data:|file:)([^"']+)(["'])""",
            m =>
            {
                var rel = m.Groups[2].Value.Replace('/', System.IO.Path.DirectorySeparatorChar);
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, rel));
                return m.Groups[1].Value + new Uri(full).AbsoluteUri + m.Groups[3].Value;
            });
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
        return writer.ToString();
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

    static string WrapHtml(string body) =>
        "<!doctype html><html><head><meta charset='utf-8'/>" +
        "<meta name='viewport' content='width=device-width, initial-scale=1'/>" +
        "<style>@font-face{font-family:'SogangUni';src:url('https://mdviewer.local/sogang.ttf') format('truetype');}" +
        Brand.PreviewCss +
        "mark.md-kw-hl{background-color:#ffff00 !important;color:#000000 !important;border-radius:2px;padding:0 2px;}" +
        "</style></head><body><article class='md'>" +
        body + "</article>" + ScrollScript + "</body></html>";

    const string ScrollScript =
        "<script>let __lock=false;" +
        "function nodes(){return [...document.querySelectorAll('[data-src-line]')];}" +
        "function scrollToSrcLine(n){__lock=true;n=parseInt(n,10);let best=null;" +
        "for(const el of nodes()){const ln=parseInt(el.getAttribute('data-src-line'),10);if(ln<=n)best=el;else break;}" +
        "if(best){const y=best.getBoundingClientRect().top+window.scrollY-8;window.scrollTo(0,Math.max(0,y));}" +
        "setTimeout(()=>__lock=false,80);}" +
        "function srcLineAtTop(){let best=null;for(const el of nodes()){if(el.getBoundingClientRect().top<=90)best=el;}" +
        "return best?parseInt(best.getAttribute('data-src-line'),10):1;}" +
        "window.addEventListener('scroll',()=>{if(__lock)return;" +
        "chrome.webview.postMessage(JSON.stringify({t:'line',ln:srcLineAtTop()}));});" +
        "window.addEventListener('pointerdown',e=>{chrome.webview.postMessage(JSON.stringify({t:'pointerdown'}));},true);" +
        "window.addEventListener('contextmenu',e=>{e.preventDefault();chrome.webview.postMessage(JSON.stringify({t:'ctx'}));});" +
        "window.addEventListener('wheel',e=>{if(e.ctrlKey){e.preventDefault();e.stopPropagation();chrome.webview.postMessage(JSON.stringify({t:'zoom',d:e.deltaY<0?1:-1}));}},{passive:false});" +
        "window.addEventListener('keydown',e=>{" +
        "if(e.key==='Escape'){chrome.webview.postMessage(JSON.stringify({t:'esc'}));}" +
        "else if(e.shiftKey&&e.key==='F8'){" +
        "e.preventDefault();" +
        "const sel=window.getSelection()?window.getSelection().toString().trim():'';" +
        "chrome.webview.postMessage(JSON.stringify({t:'shiftF8',sel:sel}));}" +
        "});" +
        "function clearKeywordHighlights(root){" +
        "root.querySelectorAll('mark.md-kw-hl').forEach(m=>{" +
        "const p=m.parentNode;if(p){p.replaceChild(document.createTextNode(m.textContent),m);p.normalize();}" +
        "});}" +
        "function escapeRegExp(s){return s.replace(/[-[\\]{}()*+?.,\\\\^$|#\\s]/g,'\\\\$&');}" +
        "function applyHighlightKeywords(keywords){" +
        "const art=document.querySelector('.md')||document.body;" +
        "clearKeywordHighlights(art);" +
        "if(!keywords||!Array.isArray(keywords)||keywords.length===0)return;" +
        "const valid=keywords.map(k=>(k||'').trim()).filter(k=>k.length>0);" +
        "if(valid.length===0)return;" +
        "const pattern='('+valid.map(escapeRegExp).join('|')+')';" +
        "const rx=new RegExp(pattern,'gi');" +
        "const walker=document.createTreeWalker(art,NodeFilter.SHOW_TEXT,{" +
        "acceptNode:n=>{" +
        "if(!n.nodeValue||!rx.test(n.nodeValue))return NodeFilter.FILTER_REJECT;" +
        "const p=n.parentElement;" +
        "if(p&&(p.tagName==='SCRIPT'||p.tagName==='STYLE'||p.classList.contains('md-kw-hl')))return NodeFilter.FILTER_REJECT;" +
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
        "</script>";

}

sealed class SogangMenuRenderer : ToolStripProfessionalRenderer
{
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
    public static Color Cardinal => Theme == AppTheme.Sogang ? Color.FromArgb(0xB3, 0x29, 0x2E) : Color.FromArgb(30, 30, 30);
    public static Color Wine => Theme == AppTheme.Sogang ? Color.FromArgb(0x9E, 0x2A, 0x2F) : Color.FromArgb(20, 20, 20);
    public static Color Gray5 => Theme == AppTheme.Sogang ? Color.FromArgb(0xB1, 0xB3, 0xB6) : Color.FromArgb(180, 180, 180);
    public static Color Wash => Color.FromArgb(0xFA, 0xFA, 0xFA);
    public static Color Paper => Color.White;
    public static Color Ink => Color.FromArgb(0x2A, 0x2A, 0x2A);
    public static Color Mute => Theme == AppTheme.Sogang ? Color.FromArgb(0x6A, 0x6C, 0x6E) : Color.FromArgb(90, 90, 90);

    public static string PreviewCss => Theme == AppTheme.Sogang
        ? "html,body{margin:0;background:#fafafa;color:#2a2a2a;width:100%}" +
          "body{font:16px/1.75 'SogangUni','Malgun Gothic',sans-serif;overflow-x:hidden}" +
          ".md{max-width:46rem;width:100%;margin:0 auto;padding:28px 22px 64px;box-sizing:border-box}" +
          "h1,h2,h3{line-height:1.25;color:#9e2a2f} h1{font-size:2rem} h2{font-size:1.35rem;border-bottom:1px solid #b1b3b6;padding-bottom:.2em}" +
          "a{color:#b3292e} code{font-family:Consolas,monospace;font-size:.88em;background:#f0f0f1;padding:.1em .35em;border-radius:6px}" +
          "pre{background:#2a2a2a;color:#f3f3f3;border-radius:12px;padding:12px 14px;white-space:pre-wrap;word-break:break-word}" +
          "pre code{background:none;padding:0;color:inherit}" +
          "blockquote{margin:1em 0;padding:.2em 0 .2em 1em;border-left:3px solid #b3292e;color:#6a6c6e}" +
          "table{border-collapse:collapse;width:100%;margin:1em 0;background:#fff}" +
          "th,td{border:1px solid #b1b3b6;padding:.45em .65em} th{background:#f3e8e9;color:#9e2a2f;text-align:left}" +
          "img{max-width:100%}"
        : "html,body{margin:0;background:#fafafa;color:#1a1a1a;width:100%}" +
          "body{font:16px/1.75 'Segoe UI','Malgun Gothic',sans-serif;overflow-x:hidden}" +
          ".md{max-width:46rem;width:100%;margin:0 auto;padding:28px 22px 64px;box-sizing:border-box}" +
          "h1,h2,h3{line-height:1.25;color:#111} h1{font-size:2rem} h2{font-size:1.35rem;border-bottom:1px solid #ccc;padding-bottom:.2em}" +
          "a{color:#222} code{font-family:Consolas,monospace;font-size:.88em;background:#f0f0f0;padding:.1em .35em;border-radius:6px}" +
          "pre{background:#f3f3f3;color:#111;border-radius:12px;padding:12px 14px;white-space:pre-wrap;word-break:break-word}" +
          "pre code{background:none;padding:0;color:inherit}" +
          "blockquote{margin:1em 0;padding:.2em 0 .2em 1em;border-left:3px solid #888;color:#555}" +
          "table{border-collapse:collapse;width:100%;margin:1em 0;background:#fff}" +
          "th,td{border:1px solid #ccc;padding:.45em .65em} th{background:#f0f0f0;color:#111;text-align:left}" +
          "img{max-width:100%}";

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
            if (File.Exists(p) && File.ReadAllText(p).Trim().Equals("Albatross", StringComparison.OrdinalIgnoreCase))
                Theme = AppTheme.Albatross;
        }
        catch { }
    }

    public static void SaveTheme()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ThemePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ThemePath, Theme == AppTheme.Albatross ? "Albatross" : "Sogang");
        }
        catch { }
    }

    static Font? _menuSogang;
    static Font? _menuAlba;
    public static Font MenuFont => Theme == AppTheme.Sogang
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
            if (Theme == AppTheme.Sogang && Family != null)
                return new Font(Family, em, style);
        }
        catch { }
        return new Font("Segoe UI", em, style);
    }

    public static Font Editor(float em) =>
        Theme == AppTheme.Sogang ? Ui(em) : new Font("Consolas", em);

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
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);
    [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    public static void NotifyAssocChanged() => SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);

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
        int idx = box.GetCharIndexFromPosition(Point.Empty);
        idx = Math.Clamp(idx, 0, box.TextLength);
        int line = 0;
        var t = box.Text;
        for (int i = 0; i < idx && i < t.Length; i++)
            if (t[i] == '\n') line++;
        return line;
    }

    public static void SetEditorFirstLine(RichTextBox box, int line)
    {
        if (!box.IsHandleCreated) return;
        var t = box.Text;
        int pos = 0, seen = 0;
        for (int i = 0; i < t.Length; i++)
        {
            if (seen == line) { pos = i; break; }
            if (t[i] == '\n') seen++;
            if (i == t.Length - 1) pos = t.Length;
        }
        box.SelectionStart = pos;
        box.SelectionLength = 0;
        box.ScrollToCaret();
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
}
