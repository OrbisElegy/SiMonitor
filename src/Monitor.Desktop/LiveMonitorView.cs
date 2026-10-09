// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Localization;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// One monitor surface: the fixed waveform rows and their sample-derived values
// share separators and colors. Settings and paper snapshots remain separate.
internal sealed class LiveMonitorView : UserControl
{
    private readonly LiveMonitorTrace _trace;
    private readonly IMonitorSkin? _skin;
    private readonly MonitorManualTile _temperature = new(), _custom1 = new(), _custom2 = new(), _nibp = new();
    internal IReadOnlyList<MonitorManualTile> ManualTiles => [_temperature, _custom1, _custom2, _nibp];
    private readonly WrapPanel _manualPanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 4) };
    internal IReadOnlyList<string> ManualNumericTexts => _skin is null
        ? _manualPanel.Children.OfType<TextBlock>().Select(text => text.Text ?? "").ToArray()
        : ManualTiles.Select(tile => tile.Reading).ToArray();
    private ManualVitalSigns? _shownManualVitals;
    private string? _shownManualCaption;
    private readonly DesktopLocalization _localization;
    private long _lastMeasurementBucket = -1;
    private readonly List<(int Channel, TextBlock Primary, TextBlock Secondary)> _rows = [];
    private readonly List<(TextBlock Pi, PulseIndicator Bar)> _opticalRows = [];
    private readonly MonitorNoticeRotation _rotation = new();
    private MonitorNotice[] _notices = [];
    private MonitorNotice[] _rawNotices = [];
    internal Func<IEnumerable<MonitorNotice>, IEnumerable<MonitorNotice>>? NoticeProjection { get; set; }
    private AlarmAttentionSnapshot? _displayedAttention;
    internal Func<string, AlarmAttentionSnapshot?>? AttentionFor { get; set; }
    private MonitorNotice[]? _highlightNotices;
    private bool? _highlightOn, _highlightNoticeColor;
    private double _pulseMinimum, _pulseMaximum;
    internal Func<LiveMeasurementSnapshot, IEnumerable<MonitorNotice>>? AdditionalNotices { get; set; }
    internal Func<bool>? NoticeColorEnabled { get; set; }
    internal IReadOnlyList<TextBlock> NumericBlocks => _rows.SelectMany(r => new[] { r.Primary, r.Secondary }).ToArray();
    internal MonitorNoticeLevel? HighestNotice => _notices.Where(n => n.Audible).Select(n => (MonitorNoticeLevel?)n.Level).Max();
    internal IReadOnlyList<MonitorNotice> ActiveNotices => _notices;
    internal IReadOnlyList<double> PulseLevels => _opticalRows.Select(r => r.Bar.Level).ToArray();
    private readonly Border _noticeBackground = new() { Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(0) };
    internal Border NoticeRegion => _noticeBackground;
    internal TextBlock AudioPauseStatus { get; } = new() { Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = Avalonia.Media.TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
    internal Func<string>? BeatSourceText { get; set; }
    internal TextBlock Clock { get; } = new() { Foreground = Brushes.White, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    internal Border NoticeSurface => _noticeBackground;
    internal TextBlock Notice { get; } = new() { Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = Avalonia.Media.TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    internal IReadOnlyList<string> NumericTexts => _rows.Select(r => r.Primary.Text ?? "").ToArray();
    protected override Size MeasureOverride(Size availableSize)
    {
        // Scale against the monitor surface, independently of notice text length.
        double scale = Math.Min(availableSize.Width / 1180, availableSize.Height / 800);
        if (!double.IsFinite(scale)) { scale = 1; }
        scale = Math.Max(.5, scale);
        _noticeBackground.MinHeight = 44 * scale;
        _noticeBackground.Padding = new Thickness(12 * scale, 8 * scale);
        Notice.FontSize = 20 * scale;
        return base.MeasureOverride(availableSize);
    }
    internal LiveMonitorView(LiveMonitorTrace trace, DesktopLocalization? localization = null, IMonitorSkin? skin = null)
    {
        _trace = trace;
        _skin = skin;
        trace.Skin = skin;
        _localization = localization ?? new DesktopLocalization();
        AttachedToVisualTree += (_, _) =>
        {
            _localization.LocaleChanged += RefreshLanguage;
            RefreshLanguage();
        };
        DetachedFromVisualTree += (_, _) => _localization.LocaleChanged -= RefreshLanguage;
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Background = Brush.Parse("#101B25") };
        var header = new Grid { ColumnDefinitions = new("*,2*,*"), Margin = new Thickness(12, 10) };
        Clock.Margin = new Thickness(0, 0, 20, 0);
        header.Children.Add(Clock);
        Grid.SetColumn(_noticeBackground, 1);
        _noticeBackground.Child = Notice;
        header.Children.Add(_noticeBackground);
        Grid.SetColumn(AudioPauseStatus, 2);
        header.Children.Add(AudioPauseStatus);
        if (skin is null) { root.Children.Add(header); }
        var body = new Grid { ColumnDefinitions = new("*,190") };
        Grid.SetRow(body, 1);
        if (skin is null) { root.Children.Add(body); body.Children.Add(trace); }
        var numbers = new Grid();
        Grid.SetColumn(numbers, 1);
        if (skin is null) { body.Children.Add(numbers); }
        foreach (var slot in trace.Session.Display.Slots)
        {
            int row = numbers.RowDefinitions.Count;
            numbers.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            if ((trace.MonitoredChannels & MonitorChannelMapping.ForChannel(slot.Channel)) == 0) { continue; }
            var color = trace.ChannelBrush(slot.Channel);
            string label = slot.Channel switch
            {
                0 => "monitor.labelHr",
                1 => "monitor.labelResp",
                2 => "monitor.labelSpo2",
                3 => "monitor.labelAbp",
                4 => "monitor.labelEtco2",
                5 => "monitor.labelPa",
                _ => "monitor.labelCvp"
            };
            var primary = new TextBlock { Text = "---", FontSize = skin is null ? 52 : 64, FontWeight = FontWeight.SemiBold, Foreground = color };
            var secondary = new TextBlock { FontSize = slot.Channel == 0 ? 13 : 17, Foreground = color, IsVisible = slot.Channel is 0 or 2 or 3 or 4 or 5 };
            _localization.Bind(primary, AutomationProperties.NameProperty, label);
            _localization.Bind(secondary, AutomationProperties.NameProperty, slot.Channel switch
            {
                0 => "monitor.ecgRepolarizationName",
                2 => "monitor.secondaryPr",
                3 => "monitor.secondaryAbp",
                5 => "monitor.secondaryPa",
                _ => "monitor.secondaryCo2Rate"
            });
            var caption = new TextBlock { Foreground = color, FontSize = 13 };
            _localization.Bind(caption, TextBlock.TextProperty, label);
            Control primaryContent = HighlightHost(primary);
            TextBlock? pi = null;
            if (slot.Channel == 2)
            {
                var pair = new Grid { ColumnDefinitions = new("*,22"), Width = 166 };
                var bar = new PulseIndicator(color);
                Grid.SetColumn(bar, 1);
                pair.Children.Add(primaryContent);
                pair.Children.Add(bar);
                primaryContent = pair;
                _localization.Bind(bar, AutomationProperties.NameProperty, "monitor.pulseIndicatorName");
                pi = new TextBlock { Text = "PI --- %", FontSize = 14, Foreground = color };
                _localization.Bind(pi, AutomationProperties.NameProperty, "monitor.piName");
                _opticalRows.Add((pi, bar));
            }
            Control content;
            if (skin is null)
            {
                var stack = new StackPanel { Width = 166, Spacing = 2 };
                stack.Children.Add(caption);
                stack.Children.Add(primaryContent);
                if (pi is not null) { stack.Children.Add(pi); }
                stack.Children.Add(HighlightHost(secondary));
                content = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = stack
                };
            }
            else
            {
                // Keep labels at readable sizes; only digits scale into the remaining height.
                var grid = new Grid { RowDefinitions = new("Auto,*,Auto") };
                grid.Children.Add(caption);
                var digits = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = primaryContent
                };
                Grid.SetRow(digits, 1);
                grid.Children.Add(digits);
                secondary.FontSize = 11;
                Control footer = HighlightHost(secondary);
                if (pi is not null)
                {
                    pi.FontSize = 11;
                    var pair = new Grid { ColumnDefinitions = new("*,*") };
                    pair.Children.Add(footer);
                    Grid.SetColumn(pi, 1);
                    pair.Children.Add(pi);
                    footer = pair;
                }
                Grid.SetRow(footer, 2);
                grid.Children.Add(footer);
                content = grid;
            }
            var border = new Border
            {
                Padding = new Thickness(8, 6),
                BorderBrush = Brush.Parse("#607080"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                ClipToBounds = true,
                Child = content
            };
            Grid.SetRow(border, row); numbers.Children.Add(border); _rows.Add((slot.Channel, primary, secondary));
        }
        Grid.SetRow(_manualPanel, 2);
        if (skin is null) { root.Children.Add(_manualPanel); }
        Content = skin?.Compose(new(header, trace, numbers, _temperature, _custom1, _custom2, _nibp)) ?? root;
        Refresh();
    }
    private static Border HighlightHost(TextBlock text) => new() { CornerRadius = new CornerRadius(0), Child = text };
    internal static IBrush? NumericBackground(TextBlock text) => ((Border)text.Parent!).Background;

    private void RefreshLanguage()
    {
        Refresh();
        RefreshNotice();
    }

    private void RefreshManualVitals()
    {
        var values = _trace.Session.ManualVitals;
        string caption = _localization.Get("manual.caption");
        if (values == _shownManualVitals && caption == _shownManualCaption) { return; }
        _shownManualVitals = values;
        _shownManualCaption = caption;
        if (_skin is not null)
        {
            _nibp.Update("NIBP", values.Nibp is { } pressure ? $"{pressure.SystolicMmHg}/{pressure.DiastolicMmHg}" : "---/---",
                values.Nibp is { } mean ? $"({mean.MeanMmHg}) mmHg" : "(---) mmHg");
            _temperature.Update(_localization.Get("manual.temperatureLabel"),
                values.TemperatureDeciCelsius is { } deciCelsius ? (deciCelsius / 10m).ToString("0.0", CultureInfo.InvariantCulture) : "---", "°C");
            if (values.Nibp is null) { _nibp.Clear(); }
            if (values.TemperatureDeciCelsius is null) { _temperature.Clear(); }
            UpdateCustom(_custom1, values.Custom1);
            UpdateCustom(_custom2, values.Custom2);
            return;
        }
        _manualPanel.Children.Clear();
        if (values == ManualVitalSigns.Empty) { _manualPanel.IsVisible = false; return; }
        _manualPanel.IsVisible = true;
        Add(caption);
        if (values.Nibp is { } nibp) { Add($"NIBP {nibp.SystolicMmHg}/{nibp.DiastolicMmHg} ({nibp.MeanMmHg}) mmHg"); }
        if (values.TemperatureDeciCelsius is { } temperature)
        { Add(_localization.Get("manual.temperatureLabel") + " " + (temperature / 10m).ToString("0.0", CultureInfo.InvariantCulture) + " °C"); }
        foreach (var custom in new[] { values.Custom1, values.Custom2 })
        {
            if (custom is not null) { Add(custom.Name + " " + custom.Value.ToString("0.##", CultureInfo.InvariantCulture) + (custom.Unit.Length == 0 ? "" : " " + custom.Unit)); }
        }
        void Add(string value)
        {
            var text = new TextBlock
            {
                Text = value,
                Foreground = Brushes.White,
                FontSize = 18,
                Margin = new Thickness(0, 4, 24, 4),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            };
            AutomationProperties.SetName(text, value);
            _manualPanel.Children.Add(text);
        }
    }

    private static void UpdateCustom(MonitorManualTile tile, ManualCustomVital? value)
    {
        if (value is null) { tile.Clear(); return; }
        tile.Update(value.Name, value.Value.ToString("0.##", CultureInfo.InvariantCulture), value.Unit);
    }

    internal void Refresh()
    {
        RefreshManualVitals();
        long seconds = _trace.Session.SimulationTimeNs / 1_000_000_000;
        string clock = string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
        Clock.Text = _localization.Format("monitor.clock", clock) + (BeatSourceText is null ? "" : "\n" + BeatSourceText());
        if (_opticalRows.Count > 0)
        {
            var pulse = _trace.Session.Samples(2, Math.Max(0, _trace.Session.FrontierNs - 32_000_000), _trace.Session.FrontierNs).LastOrDefault();
            double level = _pulseMaximum > _pulseMinimum ? Math.Clamp((pulse.Value - _pulseMinimum) / (_pulseMaximum - _pulseMinimum), 0, 1) : 0;
            foreach (var (_, bar) in _opticalRows) { bar.Level = level; bar.InvalidateVisual(); }
        }
        // Numeric updates follow acquisition cadence, not the 60Hz sweep.
        long bucket = _trace.Session.SimulationTimeNs / 200_000_000;
        if (bucket == _lastMeasurementBucket && _trace.Session.DetectedMonitoringEvents.Count == 0 &&
            _trace.Session.DetectedRhythmEvents.Count == 0) { RefreshNotice(); return; }
        _lastMeasurementBucket = bucket;
        var snapshot = _trace.Session.Measurements;
        if (snapshot is null) { return; }
        var samples = _trace.Session.Samples(2, Math.Max(0, _trace.Session.FrontierNs - 4_000_000_000), _trace.Session.FrontierNs).ToArray();
        _pulseMinimum = samples.Length > 0 ? samples.Min(s => s.Value) : 0;
        _pulseMaximum = samples.Length > 0 ? samples.Max(s => s.Value) : 0;
        if (snapshot.PulseRate.Status is WaveformMeasurementStatus.NoData or WaveformMeasurementStatus.PoorSignal or WaveformMeasurementStatus.Stale)
        { _pulseMaximum = _pulseMinimum; }
        RefreshReadings(snapshot);
    }
    internal void RefreshAttention()
    {
        _notices = (NoticeProjection?.Invoke(_rawNotices) ?? _rawNotices).ToArray();
        RefreshNotice();
    }
    internal void RefreshReadings(LiveMeasurementSnapshot snapshot)
    {
        List<MonitorNotice> notices = [];
        string Value(MeasurementSource source, WaveformMeasurementStatus status, int? value, int divisor, string label)
        {
            string? number = value is { } v ? ((decimal)v / divisor).ToString("0", CultureInfo.InvariantCulture) : null;
            var display = MeasurementDisplay.Resolve(source, status, number);
            if (display is { TopNotice: { } notice, TopNoticeMessage: { } message })
            {
                // Prefix the row label unless the source label already names it (SpO₂).
                bool named = notice.StartsWith(label, StringComparison.Ordinal);
                notices.Add(new MonitorNotice("measurement:" + label + ":" + status, MonitorNoticeLevel.Info, named ? notice : label + "：" + notice)
                { Message = named ? message : new TextMessage("measurement.labelled", label, message) });
            }
            return display.NumericText;
        }
        foreach (var (channel, primary, secondary) in _rows)
        {
            secondary.Text = "";
            switch (channel)
            {
                case 0:
                    primary.Text = Value(MeasurementSource.Ecg, snapshot.HeartRate.Status, snapshot.HeartRate.MilliBeatsPerMinute, 1000, "HR");
                    secondary.Text = RepolarizationText(snapshot.EcgMonitoring);
                    break;
                case 1:
                    primary.Text = Value(MeasurementSource.ImpedanceRespiration, snapshot.ImpedanceRespiration.Status, snapshot.ImpedanceRespiration.MilliBreathsPerMinute, 1000, "RR"); break;
                case 2:
                    primary.Text = Value(MeasurementSource.SpO2, snapshot.SpO2.Status, snapshot.SpO2.SaturationMilliPercent, 1000, "SpO₂") + (snapshot.SpO2.IsQuestionable ? "?" : "");
                    secondary.Text = "PR  " + Value(MeasurementSource.Pleth, snapshot.PulseRate.Status, snapshot.PulseRate.MilliBeatsPerMinute, 1000, "PR") + " bpm"; break;
                case 4:
                    primary.Text = Value(MeasurementSource.Co2, snapshot.Capnography.EndTidalCentiMmHg.Status, snapshot.Capnography.EndTidalCentiMmHg.Value, 100, "EtCO₂");
                    // Keep the measured value while bindings translate its unit, even when paused.
                    _localization.Bind(secondary, TextBlock.TextProperty, "monitor.co2RateValue",
                        Value(MeasurementSource.Co2, snapshot.Capnography.RespirationsMilliPerMinute.Status, snapshot.Capnography.RespirationsMilliPerMinute.Value, 1000, "RR-CO₂")); break;
                default:
                    var pressure = channel == 3 ? snapshot.AbpMean : channel == 5 ? snapshot.PaMean : snapshot.CvpMean;
                    primary.Text = Value(MeasurementSource.Pressure, pressure.Status, pressure.MeanCentiMmHg, 100, LiveMonitorTrace.Names[channel]);
                    if (channel is 3 or 5 && pressure.Pulse is { } pulse)
                    {
                        string sys = Value(MeasurementSource.Pressure, pulse.Status, pulse.SystolicCentiMmHg, 100, LiveMonitorTrace.Names[channel]);
                        string dia = Value(MeasurementSource.Pressure, pulse.Status, pulse.DiastolicCentiMmHg, 100, LiveMonitorTrace.Names[channel]);
                        secondary.Text = "SYS/DIA " + sys + "/" + dia;
                    }
                    break;
            }
        }
        foreach (var (pi, _) in _opticalRows)
        { pi.Text = "PI " + (snapshot.SpO2.PerfusionMilliPercent is { } value ? ((decimal)value / 1000).ToString("0.00", CultureInfo.InvariantCulture) : "---") + " %"; }
        _rawNotices = notices.DistinctBy(notice => notice.Id).Concat(AdditionalNotices?.Invoke(snapshot) ?? []).ToArray();
        RefreshAttention();
    }
    private static string RepolarizationText(EcgMonitoringReading reading)
    {
        string Value(WaveformMeasurementStatus status, int? value, bool voltage = false)
        {
            if (reading.Learning || reading.Status != WaveformMeasurementStatus.Valid) { return "---"; }
            if (status == WaveformMeasurementStatus.Uncountable) { return "-?-"; }
            if (status != WaveformMeasurementStatus.Valid || value is null) { return "---"; }
            return voltage ? ((decimal)value.Value / 1000).ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture)
                : value.Value.ToString(CultureInfo.InvariantCulture);
        }
        var repolarization = reading.Repolarization;
        return "ST " + Value(repolarization.StStatus, repolarization.StMicrovolts, true) + " mV\nQT " +
            Value(repolarization.QtStatus, repolarization.QtMilliseconds) + " / QTc " +
            Value(repolarization.QtStatus, repolarization.QtcMilliseconds) + " ms";
    }

    private void RefreshNotice()
    {
        _rotation.Update(_notices, _trace.Session.SimulationTimeNs);
        var notice = _rotation.Current;
        Notice.Text = notice is null ? "" : notice.Message?.Render(_localization.Current) ?? notice.Text;
        _displayedAttention = notice is null ? null : AttentionFor?.Invoke(notice.Id);
        if (_displayedAttention?.State != AlarmAttentionState.RecoveredUnacknowledged && notice is not null && _rotation.CriticalElapsedNs(notice.Id) is { } elapsed)
        {
            long seconds = elapsed / 1_000_000_000;
            Notice.Text = _localization.Format("monitor.criticalElapsed", Notice.Text, string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}"));
        }
        ToolTip.SetTip(Notice, Notice.Text); AutomationProperties.SetName(Notice, Notice.Text);
        RefreshNumericHighlights(_trace.Session.SimulationTimeNs);
    }
    internal void RefreshNumericHighlights(long timeNs)
    {
        // 1Hz cycle, half a second on/off; simulation clock freezes on pause.
        bool on = timeNs % 1_000_000_000 < 500_000_000;
        bool noticeColor = NoticeColorEnabled?.Invoke() != false;
        var level = _rotation.Current?.Level;
        bool bannerOn = on && _displayedAttention?.State != AlarmAttentionState.ActiveAcknowledged &&
            _displayedAttention?.State != AlarmAttentionState.RecoveredUnacknowledged && level is not null && level != MonitorNoticeLevel.Info && (level != MonitorNoticeLevel.Notice || noticeColor);
        _noticeBackground.Background = !bannerOn ? Brushes.Transparent : level switch
        {
            MonitorNoticeLevel.Critical => Brush.Parse("#B51F2C"),
            MonitorNoticeLevel.Warning => Brush.Parse("#F2C94C"),
            _ => Brush.Parse("#145AA3")
        };
        Notice.Foreground = bannerOn && level == MonitorNoticeLevel.Warning ? Brushes.Black : Brushes.White;
        if (ReferenceEquals(_highlightNotices, _notices) && _highlightOn == on && _highlightNoticeColor == noticeColor) { return; }
        _highlightNotices = _notices; _highlightOn = on; _highlightNoticeColor = noticeColor;
        void Paint(TextBlock text, MonitorNumeric? numeric, IBrush normal)
        {
            var level = _notices.Where(n => n.Numeric == numeric && numeric is not null && n.Level != MonitorNoticeLevel.Info &&
                AttentionFor?.Invoke(n.Id)?.State != AlarmAttentionState.ActiveAcknowledged)
                .Select(n => (MonitorNoticeLevel?)n.Level).Max();
            bool highlight = on && level is not null && (level != MonitorNoticeLevel.Notice || noticeColor);
            ((Border)text.Parent!).Background = !highlight ? Brushes.Transparent : level switch
            {
                MonitorNoticeLevel.Critical => Brush.Parse("#B51F2C"),
                MonitorNoticeLevel.Warning => Brush.Parse("#F2C94C"),
                _ => Brush.Parse("#145AA3")
            };
            text.Foreground = !highlight ? normal : level == MonitorNoticeLevel.Warning ? Brushes.Black : Brushes.White;
        }
        foreach (var (channel, primary, secondary) in _rows)
        {
            var normal = _trace.ChannelBrush(channel);
            Paint(primary, channel switch
            {
                0 => MonitorNumeric.HeartRate,
                1 => MonitorNumeric.RespirationRate,
                2 => MonitorNumeric.SpO2,
                3 => MonitorNumeric.AbpMean,
                4 => MonitorNumeric.EtCo2,
                5 => MonitorNumeric.PaMean,
                _ => MonitorNumeric.CvpMean
            }, normal);
            Paint(secondary, channel switch { 2 => MonitorNumeric.PulseRate, 4 => MonitorNumeric.Co2RespirationRate, _ => (MonitorNumeric?)null }, normal);
        }
    }
    private sealed class PulseIndicator(IBrush color) : Control
    {
        internal double Level;
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            for (int i = 0; i < 12; i++)
            {
                var brush = i < Level * 12 ? color : Brush.Parse("#263C48");
                context.FillRectangle(brush, new Rect(3, Bounds.Height - (i + 1) * Bounds.Height / 12 + 1, Math.Max(0, Bounds.Width - 6), Math.Max(0, Bounds.Height / 12 - 2)));
            }
        }
    }
}
