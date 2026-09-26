// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Measurements;

namespace Monitor.Desktop;

// One monitor surface: the fixed waveform rows and their sample-derived values
// share separators and colors. Settings and paper snapshots remain separate.
internal sealed class LiveMonitorView : UserControl
{
    private readonly LiveMonitorTrace _trace;
    private long _lastMeasurementBucket = -1;
    private readonly List<(int Channel, TextBlock Primary, TextBlock Secondary)> _rows = [];
    internal TextBlock Clock { get; } = new() { Foreground = Brushes.White, FontSize = 13 };
    internal TextBlock Notice { get; } = new() { Foreground = Brushes.White, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    internal IReadOnlyList<string> NumericTexts => _rows.Select(r => r.Primary.Text ?? "").ToArray();
    internal LiveMonitorView(LiveMonitorTrace trace)
    {
        _trace = trace;
        var root = new Grid { RowDefinitions = new("Auto,*"), Background = Brush.Parse("#101B25") };
        var header = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new Thickness(12, 10) };
        Clock.Margin = new Thickness(0, 0, 20, 0);
        header.Children.Add(Clock); Grid.SetColumn(Notice, 1); header.Children.Add(Notice); root.Children.Add(header);
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
            var secondary = new TextBlock { FontSize = 17, Foreground = color, IsVisible = slot.Channel is 2 or 4 };
            AutomationProperties.SetName(primary, label);
            AutomationProperties.SetName(secondary, slot.Channel == 2 ? "PR · PLETH，bpm" : "RR · CO₂，次/分");
            var content = new StackPanel { Width = 166, Spacing = 2 };
            content.Children.Add(new TextBlock { Text = label, Foreground = color, FontSize = 13 });
            content.Children.Add(primary); content.Children.Add(secondary);
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
        // Numeric updates follow acquisition cadence, not the 60Hz sweep.
        long bucket = _trace.Session.SimulationTimeNs / 200_000_000;
        if (bucket == _lastMeasurementBucket) { return; }
        _lastMeasurementBucket = bucket;
        var snapshot = _trace.Session.Measurements;
        if (snapshot is null) { return; }
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
                    primary.Text = Value(MeasurementSource.Pressure, pressure.Status, pressure.MeanCentiMmHg, 100, LiveMonitorTrace.Names[channel]); break;
            }
        }
        Notice.Text = string.Join(" · ", notices.Distinct());
        ToolTip.SetTip(Notice, Notice.Text);
        AutomationProperties.SetName(Notice, Notice.Text);
    }
}
