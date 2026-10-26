using System.Drawing.Printing;
using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MDviewer;

// PRINT-05: 화면과 분리된 용지 설정을 기억하고 같은 설정으로 미리보기/PDF/프린터를 출력한다.
sealed class PrintLayoutOptions
{
    public string Paper { get; set; } = "A4";
    public bool Landscape { get; set; }
    public string MarginPreset { get; set; } = "Narrow";
    public double TopMm { get; set; } = 8;
    public double BottomMm { get; set; } = 8;
    public double LeftMm { get; set; } = 8;
    public double RightMm { get; set; } = 8;
    public bool FitWidth { get; set; } = true;
    public int ScalePercent { get; set; } = 100;
    public string Printer { get; set; } = "";
    internal static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDviewer", "print-layout.json");

    public static PrintLayoutOptions Load(string? path = null)
    {
        try { return (JsonSerializer.Deserialize<PrintLayoutOptions>(File.ReadAllText(path ?? SettingsPath)) ?? new()).Normalize(); }
        catch { return new(); }
    }

    public void Save(string? path = null)
    {
        var target = path ?? SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, JsonSerializer.Serialize(Normalize()));
    }

    public PrintLayoutOptions Normalize()
    {
        if (Paper is not ("A4" or "A3" or "Letter")) Paper = "A4";
        if (MarginPreset is not ("Narrow" or "Normal" or "Custom")) MarginPreset = "Narrow";
        double Margin(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 50) : 8;
        TopMm = Margin(TopMm); BottomMm = Margin(BottomMm); LeftMm = Margin(LeftMm); RightMm = Margin(RightMm);
        ScalePercent = Math.Clamp(ScalePercent, 10, 200);
        if (MarginPreset != "Custom") TopMm = BottomMm = LeftMm = RightMm = MarginPreset == "Normal" ? 15 : 8;
        return this;
    }

    internal (double Width, double Height) PaperMm => Paper switch { "A3" => (297, 420), "Letter" => (215.9, 279.4), _ => (210, 297) };
    internal double PrintableWidthPx => ((Landscape ? PaperMm.Height : PaperMm.Width) - LeftMm - RightMm) / 25.4 * 96;

    internal CoreWebView2PrintSettings CreateSettings(CoreWebView2Environment environment, double fitScale = 1)
    {
        Normalize();
        var settings = environment.CreatePrintSettings();
        settings.MediaSize = CoreWebView2PrintMediaSize.Custom;
        settings.PageWidth = PaperMm.Width / 25.4;
        settings.PageHeight = PaperMm.Height / 25.4;
        settings.Orientation = Landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
        settings.MarginTop = TopMm / 25.4; settings.MarginBottom = BottomMm / 25.4;
        settings.MarginLeft = LeftMm / 25.4; settings.MarginRight = RightMm / 25.4;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ScaleFactor = FitWidth ? Math.Clamp(fitScale, .1, 1) : ScalePercent / 100d;
        settings.PrinterName = Printer;
        return settings;
    }
}

sealed class PrintLayoutDialog : Form
{
    readonly string _html;
    readonly string? _settingsPath;
    readonly string _directory = Path.Combine(Path.GetTempPath(), "MdView", "print-" + Guid.NewGuid().ToString("N"));
    readonly WebView2 _source = new() { Size = new Size(800, 600), Visible = false };
    readonly WebView2 _preview = new() { Dock = DockStyle.Fill };
    readonly ComboBox _paper = Choice("A4", "A3", "Letter");
    readonly ComboBox _direction = Choice("세로", "가로");
    readonly ComboBox _margins = Choice("좁게 (8mm)", "보통 (15mm)", "직접 지정");
    readonly ComboBox _scale = Choice("용지 너비에 맞춤", "직접 지정 (%)");
    readonly ComboBox _printer = Choice();
    readonly NumericUpDown _percent = Number(10, 200, 100);
    readonly NumericUpDown[] _edges = Enumerable.Range(0, 4).Select(_ => Number(0, 50, 8)).ToArray();
    readonly FlowLayoutPanel _customMargins = new() { Width = 210, Height = 132, WrapContents = true, Margin = new Padding(0) };
    readonly Button _print = new() { Text = "인쇄", AutoSize = true, Enabled = false };
    readonly Button _savePdf = new() { Text = "PDF 저장", AutoSize = true, Enabled = false };
    readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(215, 0), Margin = new Padding(0, 12, 0, 0) };
    readonly System.Windows.Forms.Timer _delay = new() { Interval = 300 };
    bool _initialized, _busy, _closing;
    int _revision;
    double _fitScale = 1;
    internal bool PreviewReady { get; private set; }
    internal string PreviewFile { get; private set; } = "";
    internal PrintLayoutOptions Options { get; private set; }

    static ComboBox Choice(params string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Margin = new Padding(0, 0, 0, 8) };
        combo.Items.AddRange(items); if (items.Length > 0) combo.SelectedIndex = 0;
        return combo;
    }
    static NumericUpDown Number(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 80, Margin = new Padding(0, 0, 0, 6) };

    public PrintLayoutDialog(string html, PrintLayoutOptions? options = null, string? settingsPath = null)
    {
        _html = html; _settingsPath = settingsPath; Options = (options ?? PrintLayoutOptions.Load(settingsPath)).Normalize();
        Text = "인쇄 미리보기"; Size = new Size(1120, 820); MinimumSize = new Size(780, 580);
        StartPosition = FormStartPosition.CenterParent; Font = Brand.Ui(9); BackColor = Brand.Wash;
        var sidebar = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        void Add(string title, Control control) { sidebar.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 4, 0, 4) }); sidebar.Controls.Add(control); }
        Add("용지", _paper); Add("방향", _direction); Add("여백", _margins);
        for (int i = 0; i < 4; i++)
        {
            _customMargins.Controls.Add(new Label { Text = new[] { "위 (mm)", "아래 (mm)", "왼쪽 (mm)", "오른쪽 (mm)" }[i], Width = 100, Height = 26, Margin = new Padding(0) });
            _customMargins.Controls.Add(_edges[i]);
        }
        sidebar.Controls.Add(_customMargins);
        Add("배율", _scale); sidebar.Controls.Add(_percent); Add("프린터", _printer);
        sidebar.Controls.Add(_print); sidebar.Controls.Add(_savePdf); sidebar.Controls.Add(_status);
        Controls.Add(_preview); Controls.Add(sidebar); Controls.Add(_source);
        _paper.SelectedItem = Options.Paper; _direction.SelectedIndex = Options.Landscape ? 1 : 0;
        _margins.SelectedIndex = Options.MarginPreset == "Custom" ? 2 : Options.MarginPreset == "Normal" ? 1 : 0;
        _scale.SelectedIndex = Options.FitWidth ? 0 : 1; _percent.Value = Options.ScalePercent;
        double[] edges = { Options.TopMm, Options.BottomMm, Options.LeftMm, Options.RightMm };
        for (int i = 0; i < 4; i++) _edges[i].Value = (decimal)edges[i];
        try
        {
            foreach (string printer in PrinterSettings.InstalledPrinters) _printer.Items.Add(printer);
            string preferred = _printer.Items.Contains(Options.Printer) ? Options.Printer : new PrinterSettings().PrinterName;
            if (_printer.Items.Contains(preferred)) _printer.SelectedItem = preferred;
        }
        catch { }
        SetEnabledInputs();
        foreach (var combo in new[] { _paper, _direction, _margins, _scale, _printer }) combo.SelectedIndexChanged += (_, _) => QueuePreview();
        foreach (var number in _edges.Append(_percent)) number.ValueChanged += (_, _) => QueuePreview();
        _delay.Tick += async (_, _) => { _delay.Stop(); await RenderPreviewAsync(); };
        Shown += async (_, _) =>
        {
            try { await InitializeAsync(); await RenderPreviewAsync(); }
            catch (Exception ex) { if (!_closing) _status.Text = "미리보기 실패: " + ex.Message; }
        };
        _print.Click += async (_, _) => await PrintAsync();
        _savePdf.Click += (_, _) => SavePdf();
        FormClosed += (_, _) =>
        {
            _closing = true; _delay.Stop();
            _source.Dispose(); _preview.Dispose();
            try { Directory.Delete(_directory, true); } catch { }
        };
    }

    void SetEnabledInputs()
    {
        foreach (var edge in _edges) edge.Enabled = _margins.SelectedIndex == 2;
        _customMargins.Visible = _margins.SelectedIndex == 2;
        _percent.Enabled = _scale.SelectedIndex == 1;
    }
    void QueuePreview()
    {
        SetEnabledInputs(); _revision++; PreviewReady = false; _print.Enabled = _savePdf.Enabled = false;
        _status.Text = "미리보기를 갱신하고 있습니다…";
        _delay.Stop(); _delay.Start();
    }
    PrintLayoutOptions ReadOptions() => new PrintLayoutOptions
    {
        Paper = _paper.Text, Landscape = _direction.SelectedIndex == 1,
        MarginPreset = _margins.SelectedIndex == 2 ? "Custom" : _margins.SelectedIndex == 1 ? "Normal" : "Narrow",
        TopMm = (double)_edges[0].Value, BottomMm = (double)_edges[1].Value, LeftMm = (double)_edges[2].Value, RightMm = (double)_edges[3].Value,
        FitWidth = _scale.SelectedIndex == 0, ScalePercent = (int)_percent.Value, Printer = _printer.Text
    }.Normalize();

    async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "MdView", "wv2"));
        await _source.EnsureCoreWebView2Async(env); await _preview.EnsureCoreWebView2Async(env);
        _source.CoreWebView2.SetVirtualHostNameToFolderMapping("mdviewer.local", Brand.AssetDir, CoreWebView2HostResourceAccessKind.Allow);
        _source.CoreWebView2.SetVirtualHostNameToFolderMapping("mdviewer-print.local", _directory, CoreWebView2HostResourceAccessKind.Allow);
        var completion = new TaskCompletionSource<bool>();
        void Loaded(object? sender, CoreWebView2NavigationCompletedEventArgs e) { _source.CoreWebView2.NavigationCompleted -= Loaded; completion.TrySetResult(e.IsSuccess); }
        _source.CoreWebView2.NavigationCompleted += Loaded;
        File.WriteAllText(Path.Combine(_directory, "document.html"), _html);
        _source.CoreWebView2.Navigate("https://mdviewer-print.local/document.html");
        if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(25))) throw new IOException("인쇄 문서를 불러올 수 없습니다.");
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (await _source.ExecuteScriptAsync("typeof renderMermaid==='function' && !document.querySelector('[data-mermaid-pending]') && [...document.images].every(i=>i.complete) && document.fonts.status==='loaded'") != "true")
        {
            if (_closing || DateTime.UtcNow >= deadline) throw new TimeoutException("도표·이미지 준비 시간이 초과되었습니다.");
            await Task.Delay(100);
        }
        _initialized = true;
    }

    internal async Task RenderPreviewAsync()
    {
        if (!_initialized || _busy || _closing) return;
        _busy = true; PreviewReady = false; _print.Enabled = _savePdf.Enabled = false;
        int revision = _revision;
        try
        {
            _status.Text = "인쇄 미리보기를 만들고 있습니다…";
            var options = ReadOptions();
            int width = (int)Math.Floor(options.PrintableWidthPx);
            await _source.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", JsonSerializer.Serialize(new { width, height = 1000, deviceScaleFactor = 1, mobile = false }));
            await _source.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"media\":\"print\"}");
            var measured = await _source.ExecuteScriptAsync("Math.max(document.documentElement.scrollWidth,...[...document.querySelectorAll('.md,.md-table-wrap,pre,svg')].map(e=>e.getBoundingClientRect().left+e.scrollWidth))");
            double contentWidth = double.Parse(measured, CultureInfo.InvariantCulture);
            _fitScale = Math.Min(1, width / Math.Max(width, contentWidth));
            var output = Path.Combine(_directory, "preview-" + Guid.NewGuid().ToString("N") + ".pdf");
            if (!await _source.CoreWebView2.PrintToPdfAsync(output, options.CreateSettings(_source.CoreWebView2.Environment, _fitScale))) throw new IOException("PDF 미리보기를 만들 수 없습니다.");
            if (_closing || revision != _revision) return;
            Options = options; PreviewFile = output;
            _preview.CoreWebView2.Navigate(new Uri(output).AbsoluteUri);
            PreviewReady = true; _savePdf.Enabled = true; _print.Enabled = _printer.SelectedIndex >= 0;
            _status.Text = options.Landscape ? "가로 방향 · 설정이 자동 저장됩니다." : "세로 방향 · 설정이 자동 저장됩니다.";
            var overflow = await _source.ExecuteScriptAsync("document.querySelector('.md')?.scrollWidth > document.querySelector('.md')?.clientWidth");
            if (!options.Landscape && overflow == "true") _status.Text += " 넓은 표는 가로 방향을 권장합니다.";
            try { options.Save(_settingsPath); } catch { _status.Text += " 설정을 저장할 수 없습니다."; }
        }
        catch (Exception ex) { if (!_closing) _status.Text = "미리보기 실패: " + ex.Message; }
        finally { _busy = false; if (!_closing && revision != _revision) { _delay.Stop(); _delay.Start(); } }
    }

    async Task PrintAsync()
    {
        if (!PreviewReady || _busy) return;
        _busy = true; _print.Enabled = _savePdf.Enabled = false;
        try
        {
            var result = await _source.CoreWebView2.PrintAsync(Options.CreateSettings(_source.CoreWebView2.Environment, _fitScale));
            if (!_closing) _status.Text = result == CoreWebView2PrintStatus.Succeeded ? "인쇄 작업을 보냈습니다." : "인쇄하지 못했습니다: " + result;
        }
        catch (Exception ex) { if (!_closing) _status.Text = "인쇄 실패: " + ex.Message; }
        finally { _busy = false; if (!_closing) { _print.Enabled = PreviewReady && _printer.SelectedIndex >= 0; _savePdf.Enabled = PreviewReady; if (!PreviewReady) { _delay.Stop(); _delay.Start(); } } }
    }
    void SavePdf()
    {
        if (!PreviewReady) return;
        using var dialog = new SaveFileDialog { Filter = "PDF 파일 (*.pdf)|*.pdf", FileName = "문서.pdf" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.Copy(PreviewFile, dialog.FileName, true); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "PDF 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    protected override void Dispose(bool disposing) { if (disposing) _delay.Dispose(); base.Dispose(disposing); }
}
