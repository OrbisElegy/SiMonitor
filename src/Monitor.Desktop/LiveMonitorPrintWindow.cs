// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed class LiveMonitorPrintWindow : Window
{
    internal MonitorPrintSnapshot Snapshot { get; }
    internal LiveMonitorPrintDocument Document { get; private set; } = null!;
    internal ComboBox RecordType { get; } = new();
    internal ComboBox Lead { get; } = new() { ItemsSource = ProjectedEcgDemoSource.LeadNames };
    internal ComboBox Duration { get; } = new() { ItemsSource = new[] { "10 s", "20 s", "30 s" }, SelectedIndex = 0 };
    internal ComboBox Speed { get; } = new() { ItemsSource = new[] { "25 mm/s", "50 mm/s" }, SelectedIndex = 0 };
    internal ComboBox Gain { get; } = new() { ItemsSource = new[] { "5 mm/mV", "10 mm/mV" }, SelectedIndex = 1 };
    internal ComboBox Color { get; } = new();
    internal NumericUpDown Start { get; } = new() { Increment = .1m, FormatString = "0.000" };
    internal Button SavePdf { get; } = new();
    internal Button OpenPdf { get; } = new();
    internal NumericUpDown Page { get; } = new() { Minimum = 1, Value = 1, Increment = 1, FormatString = "0", Width = 100 };
    private readonly DesktopLocalization _localization;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _interval = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private Bitmap? _bitmap;
    private bool _closed, _refreshing;

    internal LiveMonitorPrintWindow(MonitorPrintSnapshot snapshot, bool twelveLead, DesktopLocalization localization)
    {
        Snapshot = snapshot;
        _localization = localization;
        Width = 1180;
        Height = 800;
        MinWidth = 780;
        MinHeight = 560;
        FontFamily = DesignPreviewWindow.PreviewFont;
        FontSize = 14;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = DesktopFluentStyle.Canvas;
        Foreground = DesktopFluentStyle.Text;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localization.Bind(this, TitleProperty, "livePrint.window");
        localization.SetChoices(RecordType, "livePrint.rhythm", "livePrint.twelveLead");
        localization.SetChoices(Color, "print.color", "print.monochrome");
        RecordType.SelectedIndex = twelveLead ? 1 : 0;
        Color.SelectedIndex = 0;
        Lead.SelectedIndex = (int)snapshot.DisplayLead;
        Start.Minimum = snapshot.AvailableFromNs / 1_000_000_000m;
        Start.Maximum = snapshot.CapturedAtNs / 1_000_000_000m;
        SelectRecentRange();
        var form = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        form.Children.Add(new TextBlock { Text = "A4 · 297 × 210 mm", FontSize = 18, FontWeight = FontWeight.SemiBold });
        void Field(Control control, string key)
        {
            var label = new TextBlock();
            localization.Bind(label, TextBlock.TextProperty, key);
            localization.Bind(control, AutomationProperties.NameProperty, key);
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            form.Children.Add(label);
            form.Children.Add(control);
        }
        Field(RecordType, "livePrint.recordType");
        Field(Lead, "livePrint.lead");
        Field(Duration, "livePrint.duration");
        Field(Start, "print.start");
        Field(Speed, "print.speed");
        Field(Gain, "print.gain");
        Field(Color, "print.template");
        form.Children.Add(_interval);
        Field(Page, "livePrint.page");
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*") };
        body.Children.Add(new ScrollViewer { Content = form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var preview = new Border { Background = Brush.Parse("#DDE1E5"), Padding = new Thickness(16), Child = _preview };
        Grid.SetColumn(preview, 1);
        body.Children.Add(preview);
        localization.Bind(_preview, AutomationProperties.NameProperty, "print.preview");
        localization.Bind(SavePdf, ContentControl.ContentProperty, "print.save");
        localization.Bind(OpenPdf, ContentControl.ContentProperty, "print.open");
        var close = new Button();
        localization.Bind(close, ContentControl.ContentProperty, "print.close");
        close.Click += (_, _) => Close();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { close, SavePdf, OpenPdf })
        {
            button.MinHeight = 40;
            actions.Children.Add(button);
        }
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(18, 10) };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap };
        localization.Bind(hint, TextBlock.TextProperty, "livePrint.scaleHint");
        footer.Children.Add(hint);
        footer.Children.Add(_status);
        footer.Children.Add(actions);
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Background = DesktopFluentStyle.Canvas };
        root.Children.Add(body);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        ScreenWatermark.Attach(root);
        Content = root;
        foreach (var combo in new[] { RecordType, Lead, Speed, Gain, Color })
        { combo.SelectionChanged += (_, _) => ScheduleRefresh(); }
        Duration.SelectionChanged += (_, _) => { SelectRecentRange(); ScheduleRefresh(); };
        Start.ValueChanged += (_, _) => ScheduleRefresh();
        Page.ValueChanged += (_, _) => { if (!_refreshing && !_closed) { ShowPage(); } };
        _refreshTimer.Tick += (_, _) => RefreshPreview();
        localization.LocaleChanged += ScheduleRefresh;
        SavePdf.Click += async (_, _) => await ExportAsync(false);
        OpenPdf.Click += async (_, _) => await ExportAsync(true);
        Closed += (_, _) =>
        {
            _closed = true;
            _refreshTimer.Stop();
            localization.LocaleChanged -= ScheduleRefresh;
            _preview.Source = null;
            _bitmap?.Dispose();
            Document.Dispose();
        };
        RefreshPreview();
    }

    private void SelectRecentRange() => Start.Value = Math.Max(Start.Minimum,
        Snapshot.CapturedAtNs / 1_000_000_000m - (Duration.SelectedIndex + 1) * 10);

    private void ScheduleRefresh()
    {
        if (_closed || _refreshing) { return; }
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    internal void RefreshPreview()
    {
        _refreshTimer.Stop();
        _refreshing = true;
        try
        {
            Start.Value = Math.Clamp(Start.Value ?? Start.Minimum, Start.Minimum, Start.Maximum);
            Lead.IsEnabled = RecordType.SelectedIndex == 0;
            var options = new LiveMonitorPrintOptions(RecordType.SelectedIndex == 1, (EcgLead)Lead.SelectedIndex,
                (long)(Start.Value.Value * 1_000_000_000m), (Duration.SelectedIndex + 1) * 10,
                Speed.SelectedIndex == 1 ? 50 : 25, Gain.SelectedIndex == 0 ? 5 : 10, Color.SelectedIndex == 0);
            var document = new LiveMonitorPrintDocument(Snapshot, options, _localization);
            Document?.Dispose();
            Document = document;
            Page.Maximum = document.PageCount;
            Page.Value = Math.Clamp(Page.Value ?? 1, 1, Page.Maximum);
            _interval.Text = _localization.Format("livePrint.interval", options.StartTimeNs / 1e9, document.EndTimeNs / 1e9,
                document.PageCount, document.Events.Count);
            if (document.EndTimeNs - options.StartTimeNs < options.DurationSeconds * 1_000_000_000L)
            { _interval.Text += "\n" + _localization.Format("livePrint.shortHistory", (document.EndTimeNs - options.StartTimeNs) / 1e9, options.DurationSeconds); }
            ShowPage();
        }
        finally { _refreshing = false; }
    }

    private void ShowPage()
    {
        using var stream = new MemoryStream(Document.PreviewPng(Math.Clamp((int)(Page.Value ?? 1) - 1, 0, Document.PageCount - 1)));
        var bitmap = new Bitmap(stream);
        _preview.Source = bitmap;
        _bitmap?.Dispose();
        _bitmap = bitmap;
    }

    private async Task ExportAsync(bool open)
    {
        SavePdf.IsEnabled = OpenPdf.IsEnabled = false;
        _status.Text = "";
        try
        {
            RefreshPreview();
            using var pdf = new MemoryStream();
            Document.WritePdf(pdf);
            if (open)
            {
                string path = Path.Combine(Path.GetTempPath(), "SiMonitor-Live-" + Guid.NewGuid().ToString("N") + ".pdf");
                await File.WriteAllBytesAsync(path, pdf.ToArray());
                using var file = await StorageProvider.TryGetFileFromPathAsync(path);
                bool opened = file is not null && await Launcher.LaunchFileAsync(file);
                if (!_closed) { _status.Text = _localization.Get(opened ? "print.opened" : "print.openFailed"); }
            }
            else
            {
                using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = _localization.Get("print.save"),
                    SuggestedFileName = "Live-ECG.pdf",
                    DefaultExtension = "pdf",
                    FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] }],
                    ShowOverwritePrompt = true
                });
                if (file is null || _closed) { return; }
                await using var stream = await file.OpenWriteAsync();
                if (stream.CanSeek) { stream.SetLength(0); }
                pdf.Position = 0;
                await pdf.CopyToAsync(stream);
                if (!_closed) { _status.Text = _localization.Get("print.saved"); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        { if (!_closed) { _status.Text = _localization.Get("print.failed"); } }
        finally { if (!_closed) { SavePdf.IsEnabled = OpenPdf.IsEnabled = true; } }
    }
}
