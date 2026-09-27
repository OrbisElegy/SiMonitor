// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// One monitor surface: the fixed waveform rows and their sample-derived values
// share separators and colors. Settings and paper snapshots remain separate.
internal sealed class LiveMonitorView : UserControl
{
    private readonly LiveMonitorTrace _trace;
    private long _lastMeasurementBucket = -1;
    private readonly List<(int Channel, TextBlock Primary, TextBlock Secondary)> _rows = [];
    private readonly List<(TextBlock Pi, PulseIndicator Bar)> _opticalRows = [];
    private readonly MonitorNoticeRotation _rotation = new();
    private MonitorNotice[] _notices = [];
    private MonitorNotice[]? _highlightNotices;
    private bool? _highlightOn, _highlightNoticeColor;
    private double _pulseMinimum, _pulseMaximum;
    internal Func<LiveMeasurementSnapshot, IEnumerable<MonitorNotice>>? AdditionalNotices { get; set; }
    internal Func<bool>? NoticeColorEnabled { get; set; }
    internal IReadOnlyList<TextBlock> NumericBlocks => _rows.SelectMany(r => new[] { r.Primary, r.Secondary }).ToArray();
    internal MonitorNoticeLevel? HighestNotice => _notices.Where(n => n.Audible).Select(n => (MonitorNoticeLevel?)n.Level).Max();
    internal IReadOnlyList<MonitorNotice> ActiveNotices => _notices;
    internal IReadOnlyList<double> PulseLevels => _opticalRows.Select(r => r.Bar.Level).ToArray();
    private readonly Border _noticeBackground = new() { Padding = new Thickness(8, 3), CornerRadius = new CornerRadius(3) };
    internal TextBlock Clock { get; } = new() { Foreground = Brushes.White, FontSize = 13 };
    internal TextBlock Notice { get; } = new() { Foreground = Brushes.White, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    internal IReadOnlyList<string> NumericTexts => _rows.Select(r => r.Primary.Text ?? "").ToArray();
    internal LiveMonitorView(LiveMonitorTrace trace)
    {
        _trace = trace;
        var root = new Grid { RowDefinitions = new("Auto,*"), Background = Brush.Parse("#101B25") };
        var header = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new Thickness(12, 10) };
        Clock.Margin = new Thickness(0, 0, 20, 0);
        header.Children.Add(Clock); Grid.SetColumn(_noticeBackground, 1); _noticeBackground.Child = Notice; header.Children.Add(_noticeBackground); root.Children.Add(header);
        var body = new Grid { ColumnDefinitions = new("*,190") };
        Grid.SetRow(body, 1); root.Children.Add(body); body.Children.Add(trace);
        var numbers = new Grid();
        Grid.SetColumn(numbers, 1); body.Children.Add(numbers);
        foreach (var slot in trace.Session.Display.Slots)
        {
            int row = _rows.Count;
            numbers.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            var color = Brush.Parse(LiveMonitorTrace.Colors[slot.Channel]);
            string label = slot.Channel switch
            {
                0 => "HR · ECG   bpm",
                1 => "RR · RESP   次/分",
                2 => "SpO₂   %",
                3 => "ABP 平均压   mmHg",
                4 => "EtCO₂   mmHg",
                5 => "PA 平均压   mmHg",
                _ => "CVP 平均压   mmHg"
            };
            var primary = new TextBlock { Text = "---", FontSize = 52, FontWeight = FontWeight.SemiBold, Foreground = color };
            var secondary = new TextBlock { FontSize = 17, Foreground = color, IsVisible = slot.Channel is 2 or 3 or 4 or 5 };
            AutomationProperties.SetName(primary, label);
            AutomationProperties.SetName(secondary, slot.Channel switch { 2 => "PR · PLETH，bpm", 3 => "ABP收缩压/舒张压，mmHg", 5 => "PA收缩压/舒张压，mmHg", _ => "RR · CO₂，次/分" });
            var content = new StackPanel { Width = 166, Spacing = 2 };
            content.Children.Add(new TextBlock { Text = label, Foreground = color, FontSize = 13 });
            if (slot.Channel == 2)
            {
                var pair = new Grid { ColumnDefinitions = new("*,22") };
                var bar = new PulseIndicator(); Grid.SetColumn(bar, 1); pair.Children.Add(primary); pair.Children.Add(bar); content.Children.Add(pair);
                var pi = new TextBlock { Text = "PI --- %", FontSize = 14, Foreground = color };
                AutomationProperties.SetName(pi, "灌注指数 PI，百分比");
                content.Children.Add(pi); _opticalRows.Add((pi, bar));
            }
            else { content.Children.Add(primary); }
            content.Children.Add(secondary);
            var border = new Border
            {
                Padding = new Thickness(8, 6),
                BorderBrush = Brush.Parse("#607080"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = content
                }
            };
            Grid.SetRow(border, row); numbers.Children.Add(border); _rows.Add((slot.Channel, primary, secondary));
        }
        Content = root; Refresh();
    }
    internal void Refresh()
    {
        long seconds = _trace.Session.SimulationTimeNs / 1_000_000_000;
        Clock.Text = $"模拟 {seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
        if (_opticalRows.Count > 0)
        {
            var pulse = _trace.Session.Samples(2, Math.Max(0, _trace.Session.FrontierNs - 32_000_000), _trace.Session.FrontierNs).LastOrDefault();
            double level = _pulseMaximum > _pulseMinimum ? Math.Clamp((pulse.Value - _pulseMinimum) / (_pulseMaximum - _pulseMinimum), 0, 1) : 0;
            foreach (var (_, bar) in _opticalRows) { bar.Level = level; bar.InvalidateVisual(); }
        }
        // Numeric updates follow acquisition cadence, not the 60Hz sweep.
        long bucket = _trace.Session.SimulationTimeNs / 200_000_000;
        if (bucket == _lastMeasurementBucket) { RefreshNotice(); return; }
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
    internal void RefreshReadings(LiveMeasurementSnapshot snapshot)
    {
        List<string> notices = [];
        string Value(MeasurementSource source, WaveformMeasurementStatus status, int? value, int divisor, string label)
        {
            string? number = value is { } v ? ((decimal)v / divisor).ToString("0", CultureInfo.InvariantCulture) : null;
            var display = MeasurementDisplay.Resolve(source, status, number);
            if (display.TopNotice is { } notice) { notices.Add(notice.StartsWith(label, StringComparison.Ordinal) ? notice : label + "：" + notice); }
            return display.NumericText;
        }
        foreach (var (channel, primary, secondary) in _rows)
        {
            secondary.Text = "";
            switch (channel)
            {
                case 0:
                    primary.Text = Value(MeasurementSource.Ecg, snapshot.HeartRate.Status, snapshot.HeartRate.MilliBeatsPerMinute, 1000, "HR"); break;
                case 1:
                    primary.Text = Value(MeasurementSource.ImpedanceRespiration, snapshot.ImpedanceRespiration.Status, snapshot.ImpedanceRespiration.MilliBreathsPerMinute, 1000, "RR"); break;
                case 2:
                    primary.Text = Value(MeasurementSource.SpO2, snapshot.SpO2.Status, snapshot.SpO2.SaturationMilliPercent, 1000, "SpO₂");
                    secondary.Text = "PR  " + Value(MeasurementSource.Pleth, snapshot.PulseRate.Status, snapshot.PulseRate.MilliBeatsPerMinute, 1000, "PR") + " bpm"; break;
                case 4:
                    primary.Text = Value(MeasurementSource.Co2, snapshot.Capnography.EndTidalCentiMmHg.Status, snapshot.Capnography.EndTidalCentiMmHg.Value, 100, "EtCO₂");
                    secondary.Text = "RR  " + Value(MeasurementSource.Co2, snapshot.Capnography.RespirationsMilliPerMinute.Status, snapshot.Capnography.RespirationsMilliPerMinute.Value, 1000, "RR-CO₂") + " 次/分"; break;
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
        _notices = notices.Distinct().Select(n => new MonitorNotice(n, MonitorNoticeLevel.Info, n))
            .Concat(AdditionalNotices?.Invoke(snapshot) ?? []).ToArray();
        RefreshNotice();
    }
    private void RefreshNotice()
    {
        _rotation.Update(_notices, _trace.Session.SimulationTimeNs);
        var notice = _rotation.Current;
        Notice.Text = notice is null ? "" : $"{notice.Level} · {notice.Text}";
        _noticeBackground.Background = notice?.Level switch
        {
            MonitorNoticeLevel.Notice when NoticeColorEnabled?.Invoke() != false => Brush.Parse("#145AA3"),
            MonitorNoticeLevel.Warning => Brush.Parse("#F2C94C"),
            MonitorNoticeLevel.Critical => Brush.Parse("#B51F2C"),
            _ => Brushes.Transparent
        };
        Notice.Foreground = notice?.Level == MonitorNoticeLevel.Warning ? Brushes.Black : Brushes.White;
        ToolTip.SetTip(Notice, Notice.Text); AutomationProperties.SetName(Notice, Notice.Text);
        RefreshNumericHighlights(_trace.Session.SimulationTimeNs);
    }
    internal void RefreshNumericHighlights(long timeNs)
    {
        // 1Hz cycle, half a second on/off; simulation clock freezes on pause.
        bool on = timeNs % 1_000_000_000 < 500_000_000;
        bool noticeColor = NoticeColorEnabled?.Invoke() != false;
        if (ReferenceEquals(_highlightNotices, _notices) && _highlightOn == on && _highlightNoticeColor == noticeColor) { return; }
        _highlightNotices = _notices; _highlightOn = on; _highlightNoticeColor = noticeColor;
        void Paint(TextBlock text, MonitorNumeric? numeric, IBrush normal)
        {
            var level = _notices.Where(n => n.Numeric == numeric && numeric is not null && n.Level != MonitorNoticeLevel.Info)
                .Select(n => (MonitorNoticeLevel?)n.Level).Max();
            bool highlight = on && level is not null && (level != MonitorNoticeLevel.Notice || noticeColor);
            text.Background = !highlight ? Brushes.Transparent : level switch
            {
                MonitorNoticeLevel.Critical => Brush.Parse("#B51F2C"),
                MonitorNoticeLevel.Warning => Brush.Parse("#F2C94C"),
                _ => Brush.Parse("#145AA3")
            };
            text.Foreground = !highlight ? normal : level == MonitorNoticeLevel.Warning ? Brushes.Black : Brushes.White;
        }
        foreach (var (channel, primary, secondary) in _rows)
        {
            var normal = Brush.Parse(LiveMonitorTrace.Colors[channel]);
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
    private sealed class PulseIndicator : Control
    {
        internal double Level;
        public PulseIndicator() { AutomationProperties.SetName(this, "脉搏强度指示，随 PLETH 变化"); }
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            for (int i = 0; i < 12; i++)
            {
                var brush = i < Level * 12 ? Brush.Parse(LiveMonitorTrace.Colors[2]) : Brush.Parse("#263C48");
                context.FillRectangle(brush, new Rect(3, Bounds.Height - (i + 1) * Bounds.Height / 12 + 1, Math.Max(0, Bounds.Width - 6), Math.Max(0, Bounds.Height / 12 - 2)));
            }
        }
    }
}
