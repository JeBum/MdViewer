using System.Drawing.Drawing2D;

namespace MDviewer;

// TOOLBAR-01~03: 승인된 선형 아이콘/배치, 3단계 폭 전환과 현재 문서 상태를 전역 툴바에 표시한다.
sealed partial class MainForm
{
    readonly ToolStrip _toolbar = new()
    {
        Name = "documentToolbar", Dock = DockStyle.Top, AutoSize = false, Height = 44,
        Padding = new Padding(8, 4, 8, 4), GripStyle = ToolStripGripStyle.Hidden,
        ImageScalingSize = new Size(24, 24), ShowItemToolTips = true,
        AccessibleName = "문서 도구 모음"
    };
    readonly ToolStripSplitButton _widthTool = new() { Name = "width", DisplayStyle = ToolStripItemDisplayStyle.Image, AutoSize = false, Size = new Size(48, 36) };
    readonly Dictionary<ToolStripItem, string> _toolIconKeys = new();
    readonly ToolbarActionNotice _actionNotice = new();

    void BuildToolbar()
    {
        _toolbar.Renderer = new DocumentToolbarRenderer();
        ToolStripButton Add(string name, string text, string icon, Action action)
        {
            var button = new ToolStripButton
            {
                Name = name, Text = text, ToolTipText = text, AccessibleName = text,
                DisplayStyle = ToolStripItemDisplayStyle.Image, AutoSize = false,
                Size = new Size(36, 36), Margin = Padding.Empty
            };
            SetToolIcon(button, icon, Brand.Ink);
            button.Click += (_, _) =>
            {
                // 대화상자를 여는 기능도 선택 즉시 기능명을 먼저 보여 준다.
                bool dialogAction = name is "open" or "save" or "print";
                if (dialogAction) ShowToolbarNotice(ToolbarNoticeName(name));
                action();
                if (IsDisposed) return;
                SyncToolbar();
                if (!dialogAction) ShowToolbarNotice(ToolbarNoticeName(name));
            };
            _toolbar.Items.Add(button);
            return button;
        }
        void Separator() => _toolbar.Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(10, 30), Margin = new Padding(3, 2, 3, 2) });
        Add("new", "새 문서 (Ctrl+N)", "new", NewFile);
        Add("open", "열기 (Ctrl+O)", "open", OpenDialog);
        Add("save", "저장 (Ctrl+S)", "save", () => Current?.Save());
        Add("print", "인쇄 (Ctrl+P)", "print", () => Current?.Print());
        Separator();
        Add("undo", "실행 취소 (Ctrl+Z)", "undo", () => Current?.UndoEdit());
        Add("redo", "다시 실행 (Ctrl+Y)", "redo", () => Current?.RedoEdit());
        Separator();
        Add("mode", "VIEW 모드 (F8)", "view", ToggleEditMode);
        _widthTool.ButtonClick += (_, _) =>
        {
            SetDocWidthMode(Brand.DocWidthMode switch
            {
                DocumentWidthMode.Standard => DocumentWidthMode.Wide,
                DocumentWidthMode.Wide => DocumentWidthMode.Full,
                _ => DocumentWidthMode.Standard
            });
            ShowToolbarNotice(ToolbarNoticeName("width"));
        };
        foreach (var (mode, label) in new[]
        {
            (DocumentWidthMode.Standard, "기본 (46rem)"),
            (DocumentWidthMode.Wide, "넓게 (70rem)"),
            (DocumentWidthMode.Full, "창 맞춤")
        })
        {
            var item = new ToolStripMenuItem(label) { Tag = mode, AccessibleName = label };
            item.Click += (_, _) => { SetDocWidthMode(mode); ShowToolbarNotice(ToolbarNoticeName("width")); };
            _widthTool.DropDownItems.Add(item);
        }
        _widthTool.DropDown.Renderer = new SogangMenuRenderer();
        _widthTool.DropDown.ImageScalingSize = new Size(20, 20);
        if (_widthTool.DropDown is ToolStripDropDownMenu widthMenu) widthMenu.ShowCheckMargin = true;
        _widthTool.DropDownOpening += (_, _) => { _actionNotice.HideNotice(); SyncToolbar(); };
        _toolbar.Items.Add(_widthTool);
        Separator();
        Add("find", "검색 (Ctrl+F)", "search", () => Current?.ToggleFindBar());
        Add("highlight", "키워드 강조 패널 열기/닫기", "highlight", () =>
        {
            if (Current is { } tab) tab.HighlightBarVisible = !tab.HighlightBarVisible;
        });
        var close = Add("close", "현재 문서 닫기 (Ctrl+W)", "close", CloseCurrent);
        close.Alignment = ToolStripItemAlignment.Right;
        close.Overflow = ToolStripItemOverflow.Never;
        DpiChanged += (_, _) => SyncToolbar();
        LocationChanged += (_, _) => PositionToolbarNotice();
        SizeChanged += (_, _) => PositionToolbarNotice();
        VisibleChanged += (_, _) => { if (!Visible) _actionNotice.HideNotice(); };
        Disposed += (_, _) =>
        {
            _actionNotice.Dispose();
            foreach (var item in _toolIconKeys.Keys) item.Image?.Dispose();
            _toolIconKeys.Clear();
        };
    }

    string ToolbarNoticeName(string name) => name switch
    {
        "new" => "새 문서",
        "open" => "열기",
        "save" => "저장",
        "print" => "인쇄",
        "undo" => "실행 취소",
        "redo" => "다시 실행",
        "mode" => Current?.EditMode == true ? "편집 모드" : "VIEW 모드",
        "width" => Brand.DocWidthMode switch { DocumentWidthMode.Standard => "기본 폭 (46rem)", DocumentWidthMode.Wide => "넓게 (70rem)", _ => "창 맞춤" },
        "find" => Current?.FindBarVisible == true ? "검색" : "검색 닫기",
        "highlight" => Current?.HighlightBarVisible == true ? "키워드 강조" : "키워드 강조 닫기",
        "close" => "현재 문서 닫기",
        _ => ""
    };

    void ShowToolbarNotice(string message)
    {
        if (!Visible || IsDisposed || WindowState == FormWindowState.Minimized || string.IsNullOrEmpty(message)) return;
        _actionNotice.Prepare(message, ClientSize.Width, DeviceDpi);
        PositionToolbarNotice(force: true);
        _actionNotice.ShowNotice(this);
    }

    void PositionToolbarNotice(bool force = false)
    {
        if (_actionNotice.IsDisposed || (!force && !_actionNotice.Visible)) return;
        if (WindowState == FormWindowState.Minimized) { _actionNotice.HideNotice(); return; }
        // TOOLBAR-10: 툴바 아래에 나타나는 마우스오버 설명과 분리해 문서 하단에 고정한다.
        var gap = (int)Math.Round(24 * DeviceDpi / 96d);
        var point = PointToScreen(new Point((ClientSize.Width - _actionNotice.Width) / 2,
            Math.Max(_toolbar.Bottom + gap, ClientSize.Height - _footBand.Height - _actionNotice.Height - gap)));
        var area = Screen.FromControl(this).WorkingArea;
        _actionNotice.Location = new Point(Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - _actionNotice.Width)),
            Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - _actionNotice.Height)));
    }

    void SyncToolbar()
    {
        if (_toolbar.IsDisposed || _toolbar.Items.Count == 0) return;
        var tab = Current;
        bool document = tab != null;
        bool text = document && !IsBrowserDocument(tab!.Path);
        _toolbar.BackColor = Brand.Paper;
        _toolbar.ForeColor = Brand.Ink;
        _toolbar.Font = Brand.MenuFont;
        _widthTool.DropDown.Font = Brand.MenuFont;
        void State(string name, bool enabled, bool active = false, string? icon = null, string? tooltip = null)
        {
            var item = _toolbar.Items[name]!;
            item.Enabled = enabled;
            if (item is ToolStripButton button) button.Checked = active;
            if (tooltip != null) item.Text = item.ToolTipText = item.AccessibleName = tooltip;
            if (icon != null) SetToolIcon(item, icon, active ? Brand.FrameAccent : Brand.Ink);
        }
        State("new", true, icon: "new"); State("open", true, icon: "open");
        State("save", tab?.CanSaveDocument == true, icon: "save");
        State("print", tab?.CanPrint == true, icon: "print");
        State("undo", tab?.CanUndoEdit == true, icon: "undo");
        State("redo", tab?.CanRedoEdit == true, icon: "redo");
        bool editing = tab?.EditMode == true;
        State("mode", text, document, editing ? "edit" : "view",
            editing ? "편집 모드 · 클릭하면 VIEW 모드 (F8)" : "VIEW 모드 · 클릭하면 편집 모드 (F8)");
        var width = Brand.DocWidthMode;
        string widthName = width switch { DocumentWidthMode.Standard => "기본 (46rem)", DocumentWidthMode.Wide => "넓게 (70rem)", _ => "창 맞춤" };
        _widthTool.Enabled = text;
        _widthTool.Text = _widthTool.AccessibleName = "문서 폭: " + widthName;
        _widthTool.ToolTipText = $"문서 폭: {widthName}\n클릭: 기본 → 넓게 → 창 맞춤\n화살표: 폭 직접 선택";
        SetToolIcon(_widthTool, width switch { DocumentWidthMode.Standard => "fit", DocumentWidthMode.Wide => "wide", _ => "window" }, Brand.FrameAccent);
        foreach (ToolStripMenuItem item in _widthTool.DropDownItems)
        {
            var mode = (DocumentWidthMode)item.Tag!;
            item.Checked = width == mode;
            item.Font = Brand.MenuFont;
            SetToolIcon(item, mode switch { DocumentWidthMode.Standard => "fit", DocumentWidthMode.Wide => "wide", _ => "window" }, item.Checked ? Brand.FrameAccent : Brand.Ink);
        }
        State("find", text, tab?.FindBarVisible == true, "search");
        State("highlight", text, tab?.HighlightBarVisible == true, "highlight");
        State("close", document, icon: "close");
        _toolbar.Invalidate();
    }

    void SetToolIcon(ToolStripItem item, string kind, Color color)
    {
        int size = _toolbar.ImageScalingSize.Width;
        string key = $"{kind}:{color.ToArgb()}:{size}";
        if (_toolIconKeys.TryGetValue(item, out var previous) && previous == key) return;
        var old = item.Image;
        item.Image = DocumentToolbarIcons.Create(kind, color, size);
        _toolIconKeys[item] = key;
        if (item is not ToolStripMenuItem) item.Tag = key;
        old?.Dispose();
    }
}

// TOOLBAR-07: WebView 위에서도 보이는 클릭 통과/비활성 팝업으로 0.9초 유지 후 0.9초 동안 페이드 아웃한다.
sealed class ToolbarActionNotice : Form
{
    internal const int HoldMilliseconds = 900;
    internal const int FadeMilliseconds = 900;
    readonly System.Windows.Forms.Timer _fade = new() { Interval = 16 };
    long _started;
    Font? _messageFont;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; // NOACTIVATE | TOOLWINDOW | TRANSPARENT
            return parameters;
        }
    }

    public ToolbarActionNotice()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        AccessibleName = "선택한 도구 안내";
        _fade.Tick += (_, _) =>
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            var progress = Math.Clamp((elapsed - HoldMilliseconds) / FadeMilliseconds, 0, 1);
            if (progress >= 1) { HideNotice(); return; }
            Opacity = .96 * (1 - progress);
        };
    }

    internal void Prepare(string message, int ownerWidth, int dpi)
    {
        Text = message;
        _messageFont?.Dispose();
        _messageFont = Brand.Ui(24f, FontStyle.Bold);
        BackColor = Brand.FrameAccent;
        ForeColor = Color.White;
        var scale = dpi / 96f;
        var size = TextRenderer.MeasureText(message, _messageFont);
        Size = new Size(Math.Min(Math.Max(160, ownerWidth - 32), size.Width + (int)(48 * scale)), size.Height + (int)(32 * scale));
        var radius = Math.Min(12 * scale, Height / 2f);
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(0, 0, d, d, 180, 90); path.AddArc(Width - d, 0, d, d, 270, 90);
        path.AddArc(Width - d, Height - d, d, d, 0, 90); path.AddArc(0, Height - d, d, d, 90, 90);
        path.CloseFigure();
        var oldRegion = Region; Region = new Region(path); oldRegion?.Dispose();
        Invalidate();
    }

    internal void ShowNotice(Form owner)
    {
        _fade.Stop();
        _started = System.Diagnostics.Stopwatch.GetTimestamp();
        Opacity = .96;
        if (!Visible) Show(owner);
        Refresh();
        _fade.Start();
    }

    internal void HideNotice() { if (IsDisposed) return; _fade.Stop(); Hide(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TextRenderer.DrawText(e.Graphics, Text, _messageFont, Rectangle.Inflate(ClientRectangle, -16, -8), ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = (IntPtr)(-1); return; } // HTTRANSPARENT
        if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _fade.Dispose(); _messageFont?.Dispose(); }
        base.Dispose(disposing);
    }
}

sealed class DocumentToolbarRenderer : ToolStripProfessionalRenderer
{
    public DocumentToolbarRenderer() { RoundedEdges = false; }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
    void Background(ToolStripItemRenderEventArgs e)
    {
        bool active = e.Item is ToolStripButton { Checked: true } || e.Item.Name == "width" && e.Item.Enabled;
        if (!e.Item.Enabled || !(e.Item.Selected || e.Item.Pressed || active)) return;
        using var brush = new SolidBrush(Color.FromArgb(e.Item.Pressed ? 55 : e.Item.Selected ? 38 : 22, Brand.FrameAccent));
        e.Graphics.FillRectangle(brush, new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2));
    }
    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => Background(e);
    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
        Background(e);
        var split = (ToolStripSplitButton)e.Item;
        OnRenderArrow(new ToolStripArrowRenderEventArgs(e.Graphics, e.Item, split.DropDownButtonBounds,
            e.Item.Enabled ? Brand.Ink : SystemColors.GrayText, ArrowDirection.Down));
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(220, 224, 229));
        e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
    }
}

// 승인 시안과 동일한 24px 벡터 좌표를 사용하고 DPI에 맞춰 다시 그린다.
static class DocumentToolbarIcons
{
    public static Bitmap Create(string kind, Color color, int size = 24)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 24f, size / 24f);
        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        void L(float a, float b, float c, float d) => g.DrawLine(pen, a, b, c, d);
        void Box(float x, float y, float w, float h) => g.DrawRectangle(pen, x, y, w, h);
        void Page() { L(5,3,14,3); L(14,3,19,8); L(19,8,19,21); L(19,21,5,21); L(5,21,5,3); L(14,3,14,8); L(14,8,19,8); }
        switch (kind)
        {
            case "new": Page(); L(8,14,16,14); L(12,10,12,18); break;
            case "open": L(3,8,3,5); L(3,5,9,5); L(9,5,11,8); L(11,8,20,8); L(20,8,20,10); L(3,10,22,10); L(22,10,19,20); L(19,20,3,20); L(3,20,1,10); L(1,10,3,10); break;
            case "save": L(4,3,17,3); L(17,3,21,7); L(21,7,21,21); L(21,21,3,21); L(3,21,3,3); L(3,3,4,3); Box(7,3,9,6); Box(7,14,10,7); L(14,5,14,7); break;
            case "print":
                Box(6,2,12,6); Box(3,8,18,9);
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.Transparent)) g.FillRectangle(clear,6,13,12,8);
                g.CompositingMode = CompositingMode.SourceOver;
                Box(6,13,12,8); L(8,16,16,16); L(8,18,14,18); g.DrawEllipse(pen,17,10,1,1); break;
            case "undo": g.DrawArc(pen,6,7,14,13,215,235); L(4,9,10,9); L(4,9,4,3); break;
            case "redo": g.DrawArc(pen,4,7,14,13,90,235); L(20,9,14,9); L(20,9,20,3); break;
            case "view": g.DrawBezier(pen,2,12,7,3,17,3,22,12); g.DrawBezier(pen,2,12,7,21,17,21,22,12); g.DrawEllipse(pen,9,9,6,6); break;
            case "edit": L(4,5,13,5); L(4,10,10,10); L(4,15,8,15); L(11,20,12,15); L(12,15,20,7); L(20,7,23,10); L(23,10,15,18); L(15,18,11,20); L(18,9,21,12); break;
            case "fit": Box(2,3,20,18); L(10,8,10,16); L(14,8,14,16); L(3,12,8,12); L(6,10,8,12); L(6,14,8,12); L(21,12,16,12); L(18,10,16,12); L(18,14,16,12); break;
            case "wide": Box(2,3,20,18); L(6,7,6,17); L(18,7,18,17); L(8,12,16,12); L(8,12,10,10); L(8,12,10,14); L(16,12,14,10); L(16,12,14,14); break;
            case "window": Box(2,3,20,18); L(4,7,20,7); L(5,14,19,14); L(5,14,8,11); L(5,14,8,17); L(19,14,16,11); L(19,14,16,17); break;
            case "search": g.DrawEllipse(pen,3,3,12,12); L(13,13,21,21); break;
            case "highlight":
                L(6,15,16,5); L(16,5,21,10); L(21,10,11,20); L(11,20,6,15); L(8,17,5,20); L(5,20,10,20); L(13,8,18,13);
                using (var yellow = new Pen(Color.FromArgb(215,169,0),2.8f)) g.DrawLine(yellow,3,23,21,23); break;
            case "close": Page(); L(9,12,15,18); L(15,12,9,18); break;
            default: throw new ArgumentException("Unknown toolbar icon", nameof(kind));
        }
        return bitmap;
    }
}
