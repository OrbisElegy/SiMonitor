// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Local preview with sample-derived numerics; alarm/audio scheduling remains separate.
internal sealed class DesignPreviewWindow : Window
{
    internal static FontFamily PreviewFont { get; } = new("Segoe UI, Microsoft YaHei UI, WenQuanYi Zen Hei, sans-serif");
    private readonly ContentControl _workspace = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _title = Text("监护波形", 27, true);
    private readonly TextBlock _subtitle = Text("", 13);
    private readonly TextBlock _state = Text("", 12);
    private readonly Button[] _navigation = new Button[3];
    private WaveformEnvelope[] _ecg;
    private LiveMonitorTrace _monitor;
    internal LiveMonitorView MonitorView { get; private set; }
    private LocalMonitorPreviewSession _session;
    private DispatcherTimer? _timer;
    private long _lastTick;
    private bool _closed;
    internal DesignPreviewSettings Settings { get; }
    internal int Page { get; private set; }
    internal LocalMonitorPreviewSession Session => _session;
    internal DispatcherTimer? ActiveTimer => _timer;
    internal DesignPreviewTrace? CurrentPaper => (_workspace.Content as Viewbox)?.Child as DesignPreviewTrace;
    internal LiveMonitorTrace MonitorTrace => _monitor;
    internal DesignPreviewWindow()
    {
        Title = "心电监护 · V0.5 Standalone（开发版）";
        Width = 1440; Height = 940; MinWidth = 960; MinHeight = 640;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = Brush.Parse("#F5F6F8"); Foreground = Brush.Parse("#202C39");
        FontSize = 14; FontFamily = PreviewFont; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _session = new(PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default(), enableMeasurements: true);
        _monitor = new(_session);
        MonitorView = new(_monitor);
        _ecg = CapturePaper(ProjectedEcgDemoConfiguration.Default);
        Settings = new(StylePreviewCatalog.Get, StylePreviewCatalog.Respiration, ApplySettings, () => { if (_timer is null) { Start(); } else { Pause(); } },
            () => new WaveformDemoWindow(projected: true).Show(this));
        MonitorView.AdditionalNotices = CurrentNotices;
        MonitorView.BeatSourceText = () => Settings.Sound.BeatSourceLabel;
        Settings.Sound.BeatSourceChanged += () => MonitorView.Refresh();
        Settings.Sound.AudioPauseChanged += () => MonitorView.AudioPauseStatus.Text = Settings.Sound.AudioPauseText;
        Settings.Sound.OutputNoticeChanged += () =>
        {
            if (!_closed && _session.Measurements is { } snapshot) { MonitorView.RefreshReadings(snapshot); }
        };
        MonitorView.NoticeColorEnabled = () => Settings.Alerts.NoticeColorEnabled.IsChecked == true;
        Settings.Apply.Background = Brush.Parse("#2464BA"); Settings.Apply.Foreground = Brushes.White;
        var root = new Grid { ColumnDefinitions = new("184,*"), Background = Background };
        var sidebar = new DockPanel { Margin = new Thickness(16, 24) };
        var brand = new StackPanel { Spacing = 5, Margin = new Thickness(12, 0, 0, 32) };
        brand.Children.Add(Text("心电监护", 21, true)); brand.Children.Add(Text("V0.5 · Standalone", 12));
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(12, 20) };
        footer.Children.Add(_state); footer.Children.Add(Text("离线教学模拟\n不得用于临床决策", 12));
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        var nav = new StackPanel { Spacing = 8 };
        string[] labels = ["监护波形", "十二导联", "设置"];
        for (int i = 0; i < labels.Length; i++)
        {
            int page = i;
            var button = new Button
            {
                Content = labels[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 12),
                CornerRadius = new CornerRadius(8)
            };
            button.Click += (_, _) => SelectPage(page); _navigation[i] = button; nav.Children.Add(button);
        }
        sidebar.Children.Add(nav);
        root.Children.Add(new Border { Background = Brush.Parse("#EBEEF2"), Child = sidebar });
        var main = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(24, 20) };
        Grid.SetColumn(main, 1); root.Children.Add(main);
        var heading = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(_title); heading.Children.Add(_subtitle); main.Children.Add(heading);
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#DCE1E7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = _workspace
        };
        Grid.SetRow(card, 1); main.Children.Add(card); Content = root;
        Opened += (_, _) => Start(); Closed += (_, _) => { _closed = true; Pause(); Settings.Sound.Close(); };
        SelectPage(0); UpdateState();
    }
    internal void SelectPage(int page)
    {
        if (page is < 0 or > 2) { throw new ArgumentOutOfRangeException(nameof(page)); }
        Page = page;
        for (int i = 0; i < _navigation.Length; i++)
        {
            _navigation[i].Background = Brush.Parse(i == page ? "#D3E3FA" : "#EBEEF2");
            _navigation[i].FontWeight = i == page ? FontWeight.SemiBold : FontWeight.Normal;
            AutomationProperties.SetName(_navigation[i], $"{_navigation[i].Content}{(i == page ? "，当前页面" : "")}");
        }
        _title.Text = page switch { 0 => "监护波形", 1 => "十二导联", _ => "设置" };
        _subtitle.Text = page switch
        {
            0 => $"{_session.Display.Slots.Count}个固定槽位 · 独立扫速",
            1 => "监护采样快照",
            _ => "按分类与参数组浏览设置"
        };
        _workspace.Content = page switch
        {
            0 => MonitorView,
            1 => new Viewbox { Stretch = Stretch.Uniform, Child = new DesignPreviewTrace(_ecg, Settings.PaperLayout.SelectedIndex == 1) },
            _ => Settings
        };
    }
    internal void ApplySettings()
    {
        try
        {
            var (config, ecgConfig) = ResolveStyle(Settings.EcgSelection, Settings.RespirationSelection, Settings.EjectionSelection);
            var (breathPeriod, inspiration) = Settings.ReadBreathingTiming();
            config = config with
            {
                BreathPeriodMilliseconds = breathPeriod,
                InspirationMilliseconds = inspiration,
                AbpPulsePermille = checked((int)((Settings.AbpPulseGain.Value ?? throw new ArgumentException("ABP pulse gain required")) * 1000)),
                PaPulsePermille = checked((int)((Settings.PaPulseGain.Value ?? throw new ArgumentException("PA pulse gain required")) * 1000)),
                CvpBaselineCentiMmHg = checked((int)((Settings.CvpBaseline.Value ?? throw new ArgumentException("CVP baseline required")) * 100)),
                Co2EndExpiratoryMmHg = checked((int)(Settings.EtCo2Target.Value ?? throw new ArgumentException("EtCO2 required")))
            };
            if (Settings.CardiacRateEnabled.IsChecked == true)
            {
                if (Settings.EcgSelection != 0 || Settings.EjectionSelection == 2)
                { throw new ArgumentException("Heart rate controls require reference sinus and1:1 conduction."); }
                var rate = new SeededCardiacRate(checked((int)(Settings.HeartRate.Value ?? throw new ArgumentException("HR required"))),
                    Settings.RateSeed.Text ?? "", checked((int)((Settings.RateVariation.Value ?? throw new ArgumentException("variation required")) * 10)));
                config = config with { SeededRate = rate }; ecgConfig = ecgConfig with { SeededRate = rate };
            }
            decimal co2Amplitude = Settings.EtCo2Variation.Value ?? throw new ArgumentException("CO2 variation required");
            if (co2Amplitude > 0)
            { config = config with { SeededCo2 = new(config.Co2EndExpiratoryMmHg, checked((int)(co2Amplitude * 100)), Settings.RateSeed.Text ?? "") }; }
            int? opticalTarget = Settings.ReadOpticalTarget();
            SeededOpticalSaturation? opticalVariation = null;
            if (opticalTarget is { } target)
            {
                decimal amplitude = Settings.OpticalVariation.Value ?? throw new ArgumentException("SpO2 variation required");
                if (amplitude > 0) { opticalVariation = new(target, checked((int)(amplitude * 1000)), Settings.RateSeed.Text ?? ""); }
            }
            var next = new LocalMonitorPreviewSession(config, Settings.ReadDisplay(), enableMeasurements: true,
                opticalSaturationMilliPercent: opticalTarget, opticalModulationPermille: checked((int)((Settings.OpticalModulation.Value ?? 1) * 1000)), opticalVariation: opticalVariation);
            var ecg = CapturePaper(ecgConfig);
            Pause(); _session = next; _monitor = new(next); _ecg = ecg;
            MonitorView = new(_monitor);
            MonitorView.AudioPauseStatus.Text = Settings.Sound.AudioPauseText;
            MonitorView.AdditionalNotices = CurrentNotices;
            MonitorView.BeatSourceText = () => Settings.Sound.BeatSourceLabel;
            MonitorView.NoticeColorEnabled = () => Settings.Alerts.NoticeColorEnabled.IsChecked == true;
            Settings.Sound.ResetBeatSource(); Settings.Sound.ResetPitchState();
            SelectPage(Page); Settings.Status.Text = "已应用；监护从头开始，十二导联快照已更新。"; Start();
        }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidBreathingTiming")
        { Settings.Status.Text = "未应用：吸气须至少200 ms，呼气须大于375 ms；请调整呼吸频率或吸气占比。原运行与画面保持不变。"; }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { Settings.Status.Text = "未应用：检查样式、生命体征、种子或量程。心率调整需窦性参考及1:1下传，CO₂波动需规则呼吸。原运行与画面保持不变。"; }
    }
    private IEnumerable<MonitorNotice> CurrentNotices(Monitor.Application.Measurements.LiveMeasurementSnapshot snapshot)
    {
        foreach (var notice in Settings.Alerts.Notices(snapshot)) { yield return notice; }
        if (Settings.Sound.OutputNotice is { } fault) { yield return fault; }
        if (Settings.Sound.PitchNotice is { } pitch) { yield return pitch; }
    }
    internal void Start()
    {
        if (_closed || _timer is not null) { return; }
        _lastTick = Stopwatch.GetTimestamp();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick; _timer.Start(); UpdateState();
    }
    internal void Pause()
    {
        var old = _timer; _timer = null;
        if (old is not null) { old.Stop(); old.Tick -= OnTick; }
        Settings?.Sound.PauseMonitor();
        UpdateState();
    }
    private void OnTick(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _timer) || _timer is null || _closed) { return; }
        long now = Stopwatch.GetTimestamp();
        long elapsed = DemoFrameTiming.ResolveElapsed(Stopwatch.GetElapsedTime(_lastTick, now));
        _lastTick = now; Pulse(sender, elapsed);
    }
    internal void Pulse(object? timer, long deltaNs)
    {
        if (_closed || _timer is null || !ReferenceEquals(timer, _timer)) { return; }
        try
        {
            _session.Advance(deltaNs); _monitor.InvalidateVisual();
            MonitorView.Refresh();
            Settings.Sound.UpdateAlarm(MonitorView.HighestNotice, Settings.Alerts.Timing, _session.DetectedBeats, _session.DetectedPulses, _session.Measurements);
            UpdateState();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { Pause(); Settings.Status.Text = "生成失败，已暂停。可检查设置后重新开始。"; }
    }
    private void UpdateState()
    {
        _state.Text = $"{(_timer is null ? "已暂停" : "运行中")} · {_session.SimulationTimeNs / 1_000_000_000}s";
        if (Settings is not null) { Settings.Run.Content = _timer is null ? "继续生成" : "暂停生成"; }
    }
    internal static (PhysiologyDemoConfiguration Physiology, ProjectedEcgDemoConfiguration Ecg) ResolveStyle(int ecg, int resp, int ejection)
    {
        var config = ecg switch { 1 => PhysiologyDemoConfiguration.SinusArrestPreset, 2 => PhysiologyDemoConfiguration.PrematureVentricular, 3 => PhysiologyDemoConfiguration.SinusArrhythmiaPreset, 4 => PhysiologyDemoConfiguration.PrematureAtrial, 5 => PhysiologyDemoConfiguration.PrematureJunctional, _ => PhysiologyDemoConfiguration.Default };
        var ecgConfig = ecg switch { 1 => ProjectedEcgDemoConfiguration.SinusArrestPreset, 2 => ProjectedEcgDemoConfiguration.PrematureVentricular, 3 => ProjectedEcgDemoConfiguration.SinusArrhythmiaPreset, 4 => ProjectedEcgDemoConfiguration.PrematureAtrial, 5 => ProjectedEcgDemoConfiguration.PrematureJunctional, _ => ProjectedEcgDemoConfiguration.Default };
        config = config with
        {
            RespiratoryPattern = resp switch { 1 => RespiratoryPattern.CheyneStokesIllustration, 2 => RespiratoryPattern.IntermittentIllustration, _ => RespiratoryPattern.Regular },
            RespiratoryActivity = resp == 3 ? RespiratoryActivity.Absent : RespiratoryActivity.Breathing
        };
        if (ejection == 1 && ecg != 2) { throw new ArgumentException("早搏弱射血需选择单形室早。"); }
        if (ejection == 2)
        {
            if (ecg != 0) { throw new ArgumentException("2:1漏搏需选择窦性参考。"); }
            config = config with { VentricularConductionRatio = 2 };
            ecgConfig = ecgConfig with { VentricularConductionRatio = 2 };
        }
        if (ejection == 3) { config = config with { VentricularMechanicalEnabled = false }; }
        return (config, ecgConfig);
    }
    internal static LocalMonitorPreviewSession CreateStylePreview(int ecg, int resp, int ejection) =>
        CreateThumbnailSource(ResolveStyle(ecg, resp, ejection).Physiology);
    private static LocalMonitorPreviewSession CreateThumbnailSource(PhysiologyDemoConfiguration configuration)
    {
        var preview = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default());
        for (int i = 0; i < 108; i++) { preview.Advance(50_000_000); }
        return preview;
    }
    internal static (long TimeNs, double Value)[] CreateRespirationPreview(int resp)
    {
        // One cached 11-breath overview includes crescendo, decrescendo and
        // pauses; use the exact same respiratory configuration as Apply.
        var source = PhysiologyDemoSource.Create(ResolveStyle(0, resp, 0).Physiology);
        List<(long, double)> samples = [];
        for (int step = 1; step <= 217; step++)
            foreach (var wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            {
                var block = WaveformEnvelopeCodec.Decode(wire);
                var plane = block.Planes.Single(p => p.ChannelId == PhysiologyDemoSource.ChannelId(1));
                long interval = 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
                for (int i = 0; i < plane.Samples.Count; i++)
                {
                    long time = block.StartSimTimeNs + i * interval;
                    if (time < DesignPreviewSettings.RespirationPreviewDurationNs)
                    { samples.Add((time, (double)plane.Samples[i] * plane.ScaleNumerator / plane.ScaleDenominator + (double)plane.OffsetNumerator / plane.OffsetDenominator)); }
                }
            }
        return samples.ToArray();
    }
    private static WaveformEnvelope[] CapturePaper(ProjectedEcgDemoConfiguration configuration)
    {
        var source = ProjectedEcgDemoSource.Create(configuration); List<WaveformEnvelope> output = [];
        for (int step = 1; step <= 56; step++)
        { foreach (var bytes in source.AdvanceTo(step * 200_000_000L, 50, 1, 100)) { output.Add(WaveformEnvelopeCodec.Decode(bytes)); } }
        return output.Take(55).ToArray();
    }
    private static TextBlock Text(string value, double size, bool strong = false) => new()
    { Text = value, FontSize = size, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
}
