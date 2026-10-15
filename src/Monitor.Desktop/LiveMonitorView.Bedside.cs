// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class LiveMonitorView
{
    private WaveformAlarmBanners? _waveformAlarms;
    private readonly Dictionary<string, MonitorNumeric> _measurementNoticeSources = [];
    internal WaveformAlarmBanners? WaveformAlarms => _waveformAlarms;
    internal Func<MonitorNumeric, (string High, string Low)>? WarningLimits { get; set; }
    private readonly List<(MonitorNumeric Numeric, TextBlock Text)> _bedsideLimits = [];
    internal IReadOnlyList<string> BedsideLimitTexts => _bedsideLimits.Select(item => item.Text.Text ?? "").ToArray();
    internal static FontFamily BedsideFont { get; } = new("Arial, Liberation Sans, sans-serif");

    private MonitorNotice WaveformNotice(MonitorNotice notice) =>
        notice.Numeric is null && _measurementNoticeSources.TryGetValue(notice.Id, out var numeric)
            ? notice with { Numeric = numeric } : notice;

    private Grid BuildBedsideHeader()
    {
        var header = new Grid { ColumnDefinitions = new("210,*,150"), Margin = new Thickness(12, 2) };
        var description = new TextBlock
        {
            FontSize = 16,
            Foreground = Brush.Parse("#D3E5EE"),
            VerticalAlignment = VerticalAlignment.Center
        };
        _localization.Bind(description, TextBlock.TextProperty, "bedside.simulation");
        header.Children.Add(description);
        _noticeBackground.MinHeight = 24;
        _noticeBackground.MaxWidth = 380;
        _noticeBackground.Padding = new Thickness(8, 2);
        _noticeBackground.CornerRadius = new CornerRadius(2);
        _noticeBackground.HorizontalAlignment = HorizontalAlignment.Right;
        _noticeBackground.VerticalAlignment = VerticalAlignment.Center;
        Notice.FontSize = 14;
        Notice.FontWeight = FontWeight.Normal;
        Notice.TextWrapping = TextWrapping.NoWrap;
        Notice.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(_noticeBackground, 1);
        header.Children.Add(_noticeBackground);
        var status = new Grid { RowDefinitions = new("18,*") };
        Clock.FontFamily = BedsideFont;
        Clock.FontSize = 20;
        Clock.Margin = new Thickness(0);
        Clock.HorizontalAlignment = HorizontalAlignment.Right;
        Clock.VerticalAlignment = VerticalAlignment.Center;
        Clock.TextWrapping = TextWrapping.NoWrap;
        Grid.SetRow(Clock, 1);
        status.Children.Add(Clock);
        AudioPauseStatus.FontSize = 11;
        AudioPauseStatus.TextTrimming = TextTrimming.CharacterEllipsis;
        AudioPauseStatus.TextWrapping = TextWrapping.NoWrap;
        AudioPauseStatus.Margin = new Thickness(0);
        Grid.SetColumn(AudioPauseStatus, 0);
        status.Children.Add(AudioPauseStatus);
        Grid.SetColumn(status, 2);
        header.Children.Add(status);
        return header;
    }

    private Grid BuildBedsideNumeric(int channel, TextBlock primary, TextBlock secondary, IBrush color)
    {
        var row = new Grid { ColumnDefinitions = new("52,*,14"), Margin = new Thickness(2, 0, 8, 0), ClipToBounds = true };
        var rail = new Grid { RowDefinitions = new("Auto,*,Auto") };
        var label = new TextBlock
        {
            Text = channel switch
            {
                0 => "ECG\nbpm",
                1 => "Resp\nrpm",
                2 => "SpO₂\n%",
                3 => "ABP\nmmHg",
                4 => "CO₂\nmmHg",
                5 => "PA\nmmHg",
                _ => "CVP\nmmHg"
            },
            Foreground = color,
            FontSize = 13,
            LineHeight = 15
        };
        rail.Children.Add(label);
        var limits = new TextBlock { Foreground = color, FontSize = 11, LineHeight = 13, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(limits, 2);
        rail.Children.Add(limits);
        row.Children.Add(rail);
        _bedsideLimits.Add((NumericForChannel(channel), limits));
        primary.FontFamily = BedsideFont;
        primary.FontSize = 164;
        // Let the Viewbox fit the complete font metrics, including ink below the baseline.
        primary.FontWeight = FontWeight.Normal;
        var host = HighlightHost(primary);
        host.CornerRadius = new CornerRadius(0);
        var condensed = new LayoutTransformControl { Child = host, LayoutTransform = new ScaleTransform(.66, 1) };
        var digits = new Viewbox
        {
            Child = condensed,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 2)
        };
        Grid.SetColumn(digits, 1);
        row.Children.Add(digits);
        // Additional measurements remain available without changing the device's main numeric layout.
        var details = new StackPanel { Spacing = 4 };
        secondary.Foreground = color;
        details.Children.Add(HighlightHost(secondary));
        if (channel == 2)
        {
            var bar = new PulseIndicator(color) { Height = 30, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);
            _localization.Bind(bar, AutomationProperties.NameProperty, "monitor.pulseIndicatorName");
            var pi = new TextBlock { Foreground = color, FontSize = 13 };
            _localization.Bind(pi, AutomationProperties.NameProperty, "monitor.piName");
            details.Children.Add(pi);
            _opticalRows.Add((pi, bar));
        }
        ToolTip.SetTip(row, details);
        return row;
    }

    private void RefreshBedsideLimits()
    {
        foreach (var (numeric, text) in _bedsideLimits)
        {
            var (high, low) = WarningLimits?.Invoke(numeric) ?? ("--", "--");
            text.Text = high + "\n" + low;
            AutomationProperties.SetName(text, _localization.Format("bedside.limits", high, low));
        }
    }

    private static MonitorNumeric NumericForChannel(int channel) => channel switch
    {
        0 => MonitorNumeric.HeartRate,
        1 => MonitorNumeric.RespirationRate,
        2 => MonitorNumeric.SpO2,
        3 => MonitorNumeric.AbpMean,
        4 => MonitorNumeric.EtCo2,
        5 => MonitorNumeric.PaMean,
        _ => MonitorNumeric.CvpMean
    };
}
