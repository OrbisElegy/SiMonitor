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
using Monitor.Simulation.Acquisition;

namespace Monitor.Desktop;

internal sealed class Ecg12PrintWindow : Window
{
    private readonly IReadOnlyList<WaveformEnvelope> _blocks;
    private readonly DesktopLocalization _localization;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _interval = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private Bitmap? _bitmap;
    private bool _refreshing, _closed;
    internal Ecg12PrintDocument Document { get; private set; } = null!;
    internal TextBox Heading { get; } = new() { MaxLength = 80 };
    internal TextBox Subtitle { get; } = new() { MaxLength = 100 };
    internal CheckBox ShowCandidate { get; } = new();
    internal TextBox Candidate { get; } = new() { MaxLength = 180, AcceptsReturn = true, MinHeight = 62, TextWrapping = TextWrapping.Wrap };
    internal ComboBox PrintTemplate { get; } = new();
    internal ComboBox Speed { get; } = new() { ItemsSource = new[] { "25 mm/s", "50 mm/s" } };
    internal ComboBox Gain { get; } = new() { ItemsSource = new[] { "5 mm/mV", "10 mm/mV" } };
    internal NumericUpDown Start { get; } = new() { Minimum = 0, Increment = .1m, FormatString = "0.00" };
    internal CheckBox ShowMeasurements { get; } = new();
    internal Dictionary<Ecg12PrintAnswer, CheckBox> AnswerChoices { get; } = [];
    internal TextBox Diagnosis { get; } = new() { MaxLength = 240, AcceptsReturn = true, MinHeight = 84, TextWrapping = TextWrapping.Wrap };
    internal Button SavePdf { get; } = new();
    internal Button OpenPdf { get; } = new();

    internal Ecg12PrintWindow(IReadOnlyList<WaveformEnvelope> blocks, DesktopLocalization localization, Ecg12PrintOptions? options = null)
    {
        _blocks = blocks;
        _localization = localization;
        Width = 1120;
        Height = 880;
        MinWidth = 760;
        MinHeight = 560;
        FontFamily = DesignPreviewWindow.PreviewFont;
        FontSize = 14;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = DesktopFluentStyle.Canvas;
        Foreground = DesktopFluentStyle.Text;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localization.Bind(this, TitleProperty, "print.window");
        options ??= new(localization.Get("print.defaultTitle"), "", false, localization.Get("print.defaultCandidate"), true, false, "");
        Heading.Text = options.Title;
        Subtitle.Text = options.Subtitle;
        ShowCandidate.IsChecked = options.ShowCandidate;
        Candidate.Text = options.Candidate;
        localization.SetChoices(PrintTemplate, "print.color", "print.monochrome");
        PrintTemplate.SelectedIndex = options.Color ? 0 : 1;
        Speed.SelectedIndex = options.SpeedMmPerSecond == 50 ? 1 : 0;
        Gain.SelectedIndex = options.GainMmPerMillivolt == 5 ? 0 : 1;
        Start.Maximum = (decimal)Math.Max(0, (LastTimeNs - Ecg12PrintDocument.DurationNs(options.SpeedMmPerSecond)) / 1e9);
        Start.Value = Math.Min(Start.Maximum, options.StartTimeNs / 1_000_000_000m);
        ShowMeasurements.IsChecked = options.ShowMeasurements;
        Diagnosis.Text = options.Diagnosis;
        var form = PrintPreviewLayout.CreateForm("A4 · 210 × 297 mm");
        void Field(Control control, string key) => PrintPreviewLayout.AddField(form, control, localization, key);
        Field(Heading, "print.title");
        Field(Subtitle, "print.subtitle");
        localization.Bind(ShowCandidate, ContentControl.ContentProperty, "print.showCandidate");
        ShowCandidate.MinHeight = 44;
        form.Children.Add(ShowCandidate);
        localization.Bind(Candidate, AutomationProperties.NameProperty, "print.candidate");
        form.Children.Add(Candidate);
        Field(PrintTemplate, "print.template");
        Field(Speed, "print.speed");
        Field(Gain, "print.gain");
        Field(Start, "print.start");
        form.Children.Add(_interval);
        localization.Bind(ShowMeasurements, ContentControl.ContentProperty, "print.showMeasurements");
        ShowMeasurements.MinHeight = 44;
        form.Children.Add(ShowMeasurements);
        var answers = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var field in Enum.GetValues<Ecg12PrintAnswer>())
        {
            var choice = new CheckBox { MinWidth = 130, MinHeight = 44, IsChecked = options.AnswerFields.Contains(field) };
            localization.Bind(choice, ContentControl.ContentProperty, Ecg12PrintDocument.AnswerKey(field));
            choice.IsCheckedChanged += (_, _) => ScheduleRefresh();
            AnswerChoices.Add(field, choice);
            answers.Children.Add(choice);
        }
        form.Children.Add(answers);
        Field(Diagnosis, "print.diagnosis");
        Content = PrintPreviewLayout.Compose(this, form, _preview, _status, SavePdf, OpenPdf, localization, "print.scaleHint");
        foreach (var text in new[] { Heading, Subtitle, Candidate, Diagnosis }) { text.TextChanged += (_, _) => ScheduleRefresh(); }
        foreach (var combo in new[] { PrintTemplate, Speed, Gain }) { combo.SelectionChanged += (_, _) => ScheduleRefresh(); }
        ShowCandidate.IsCheckedChanged += (_, _) => ScheduleRefresh();
        ShowMeasurements.IsCheckedChanged += (_, _) => ScheduleRefresh();
        Start.ValueChanged += (_, _) => ScheduleRefresh();
        _refreshTimer.Tick += (_, _) => RefreshPreview();
        SavePdf.Click += async (_, _) => await ExportAsync(open: false);
        OpenPdf.Click += async (_, _) => await ExportAsync(open: true);
        localization.LocaleChanged += ScheduleRefresh;
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

    private long LastTimeNs => _blocks.Count == 0 ? 0 : _blocks.Max(block => block.StartSimTimeNs + block.DurationNs);
    internal Ecg12PrintOptions ReadOptions() => new(Heading.Text ?? "", Subtitle.Text ?? "", ShowCandidate.IsChecked == true,
        Candidate.Text ?? "", PrintTemplate.SelectedIndex == 0, ShowMeasurements.IsChecked == true, Diagnosis.Text ?? "",
        Speed.SelectedIndex == 1 ? 50 : 25, Gain.SelectedIndex == 0 ? 5 : 10,
        (long)((Start.Value ?? 0) * 1_000_000_000m))
    { AnswerFields = AnswerChoices.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray() };

    private void ScheduleRefresh()
    {
        if (_refreshing || _closed) { return; }
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    internal void RefreshPreview()
    {
        _refreshTimer.Stop();
        _refreshing = true;
        try
        {
            int speed = Speed.SelectedIndex == 1 ? 50 : 25;
            Start.Maximum = (decimal)Math.Max(0, (LastTimeNs - Ecg12PrintDocument.DurationNs(speed)) / 1e9);
            Start.Value = Math.Clamp(Start.Value ?? 0, 0, Start.Maximum);
            Candidate.IsEnabled = ShowCandidate.IsChecked == true;
            foreach (var choice in AnswerChoices.Values) { choice.IsEnabled = ShowMeasurements.IsChecked == true; }
            var document = new Ecg12PrintDocument(_blocks, ReadOptions(), _localization);
            using var stream = new MemoryStream(document.PreviewPng());
            var bitmap = new Bitmap(stream);
            _preview.Source = bitmap;
            _bitmap?.Dispose();
            _bitmap = bitmap;
            Document?.Dispose();
            Document = document;
            _interval.Text = _localization.Format("print.interval", document.Options.StartTimeNs / 1e9, document.EndTimeNs / 1e9);
        }
        finally { _refreshing = false; }
    }

    private async Task ExportAsync(bool open)
    {
        SavePdf.IsEnabled = OpenPdf.IsEnabled = false;
        _status.Text = "";
        try
        {
            RefreshPreview();
            // Finish the current snapshot before awaiting a picker; later edits cannot alter this output.
            using var pdf = new MemoryStream();
            Document.WritePdf(pdf);
            if (open)
            {
                string path = Path.Combine(Path.GetTempPath(), "SiMonitor-ECG-" + Guid.NewGuid().ToString("N") + ".pdf");
                await File.WriteAllBytesAsync(path, pdf.ToArray());
                using var file = await StorageProvider.TryGetFileFromPathAsync(path);
                bool opened = file is not null && await Launcher.LaunchFileAsync(file);
                _status.Text = _localization.Get(opened ? "print.opened" : "print.openFailed");
            }
            else
            {
                using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = _localization.Get("print.save"),
                    SuggestedFileName = "ECG-A4.pdf",
                    DefaultExtension = "pdf",
                    FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] }],
                    ShowOverwritePrompt = true
                });
                if (file is null || _closed) { return; }
                await using var stream = await file.OpenWriteAsync();
                if (stream.CanSeek) { stream.SetLength(0); }
                pdf.Position = 0;
                await pdf.CopyToAsync(stream);
                _status.Text = _localization.Get("print.saved");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        { _status.Text = _localization.Get("print.failed"); }
        finally { if (!_closed) { SavePdf.IsEnabled = OpenPdf.IsEnabled = true; } }
    }
}
