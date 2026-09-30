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
using Monitor.Infrastructure.Preferences;
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
    private readonly ListBox _navigation = new();
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
    private readonly DisplayPreferenceStore? _preferences;
    internal TextBlock PreferenceNotice { get; } = Text("", 12);
    internal DesignPreviewWindow(string? preferencesPath = null)
    {
        Title = "心电监护 · V0.5 Standalone（开发版）";
        Width = 1440; Height = 940; MinWidth = 960; MinHeight = 640;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = DesktopFluentStyle.Canvas; Foreground = DesktopFluentStyle.Text;
        FontSize = 14; FontFamily = PreviewFont; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _preferences = preferencesPath is null ? null : new(preferencesPath);
        bool rejected = false;
        var preferences = _preferences?.Load(out rejected) ?? new DisplayPreferences(MonitorDisplayConfiguration.Default(), 0);
        _session = new(PhysiologyDemoConfiguration.Default, preferences.Display, enableMeasurements: true);
        _monitor = new(_session);
        MonitorView = new(_monitor);
        _ecg = CapturePaper(ProjectedEcgDemoConfiguration.Default);
        Settings = new(StylePreviewCatalog.Get, StylePreviewCatalog.Respiration, ApplySettings, () => { if (_timer is null) { Start(); } else { Pause(); } },
            () => new WaveformDemoWindow(projected: true).Show(this));
        Settings.RestoreDisplay(preferences.Display, preferences.PaperLayout);
        if (preferences.Alarms is { } alarms) { Settings.Alerts.RestorePreferences(alarms); }
        if (preferences.Sound is { } sound) { Settings.Sound.RestorePreferences(sound, Settings.Alerts); }
        PreferenceNotice.IsVisible = rejected;
        if (rejected)
        {
            PreferenceNotice.Text = "显示／报警／声音配置无法读取，已使用默认值。";
            Settings.Status.Text = "显示／报警／声音配置无法读取，已使用默认值；应用有效设置后将重新保存。";
        }
        MonitorView.AdditionalNotices = CurrentNotices;
        MonitorView.BeatSourceText = () => Settings.Sound.BeatSourceLabel;
        Settings.Sound.BeatSourceChanged += () => MonitorView.Refresh();
        Settings.Sound.AudioPauseChanged += () => MonitorView.AudioPauseStatus.Text = Settings.Sound.AudioPauseText;
        Settings.Sound.OutputNoticeChanged += () =>
        {
            if (!_closed && _session.Measurements is { } snapshot) { MonitorView.RefreshReadings(snapshot); }
        };
        MonitorView.NoticeColorEnabled = () => Settings.Alerts.NoticeColorEnabled.IsChecked == true;
        Settings.Apply.Classes.Add("accent");
        var root = new Grid { ColumnDefinitions = new("184,*"), Background = Background };
        var sidebar = new DockPanel { Margin = new Thickness(16, 24) };
        var brand = new StackPanel { Spacing = 5, Margin = new Thickness(12, 0, 0, 32) };
        brand.Children.Add(Text("心电监护", 21, true)); brand.Children.Add(Text("V0.5 · Standalone", 12));
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(12, 20) };
        footer.Children.Add(PreferenceNotice);
        footer.Children.Add(_state); footer.Children.Add(Text("离线教学模拟\n不得用于临床决策", 12));
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        string[] labels = ["监护波形", "十二导联", "设置", "帮助", "关于"];
        SettingsSections.StyleNavigation(_navigation);
        _navigation.ItemsSource = labels.Select(title => SettingsSections.Item(title, showChevron: false)).ToArray();
        AutomationProperties.SetName(_navigation, "主导航");
        _navigation.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, _navigation) && _navigation.SelectedIndex >= 0 && _navigation.SelectedIndex != Page)
            { SelectPage(_navigation.SelectedIndex); }
        };
        sidebar.Children.Add(_navigation);
        root.Children.Add(new Border { Background = DesktopFluentStyle.Canvas, Child = sidebar });
        var main = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(24, 20) };
        Grid.SetColumn(main, 1); root.Children.Add(main);
        var heading = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(_title); heading.Children.Add(_subtitle); main.Children.Add(heading);
        var card = new Border
        {
            Background = DesktopFluentStyle.Surface,
            BorderBrush = DesktopFluentStyle.Stroke,
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
        if (page is < 0 or > 4) { throw new ArgumentOutOfRangeException(nameof(page)); }
        Page = page;
        _navigation.SelectedIndex = page;
        _title.Text = page switch { 0 => "监护波形", 1 => "十二导联", 2 => "设置", 3 => "帮助", _ => "关于" };
        _subtitle.Text = page switch
        {
            0 => $"{_session.Display.Slots.Count}个固定槽位 · 独立扫速",
            1 => "监护采样快照",
            2 => "按分类与参数组浏览设置",
            3 => "操作说明与参数说明",
            _ => "版本与开源许可"
        };
        _workspace.Content = page switch
        {
            0 => MonitorView,
            1 => new Viewbox { Stretch = Stretch.Uniform, Child = new DesignPreviewTrace(_ecg, Settings.PaperLayout.SelectedIndex == 1) },
            2 => Settings,
            3 => DesktopInformationPages.CreateHelp(),
            _ => DesktopInformationPages.CreateAbout()
        };
    }
    internal void ApplySettings()
    {
        try
        {
            var alarms = _preferences is null ? null : Settings.Alerts.CapturePreferences();
            var sound = _preferences is null ? null : Settings.Sound.CapturePreferences(Settings.Alerts);
            int paperLayout = Settings.PaperLayout.SelectedIndex;
            if (paperLayout is < 0 or > 1) { throw new ArgumentException("Preview.InvalidPaperLayout"); }
            var (config, ecgConfig) = ResolveStyle(Settings.EcgSelection, Settings.RespirationSelection, Settings.EjectionSelection);
            var zones = Settings.InfarctionParameters.ReadZones(ecgConfig.Infarction);
            var infarction = zones is null ? Settings.InfarctionParameters.Read(ecgConfig.Infarction) : null;
            config = config with { Infarction = infarction, Zones = zones }; ecgConfig = ecgConfig with { Infarction = infarction, Zones = zones };
            var contour = Settings.TContourParameters.Read(ecgConfig.TContour);
            config = config with { TContour = contour }; ecgConfig = ecgConfig with { TContour = contour };
            var (breathPeriod, inspiration) = Settings.ReadBreathingTiming();
            var (respAmplitude, respArtifact) = Settings.ReadRespirationSignal();
            var (co2Delay, co2Dispersion) = Settings.ReadCo2Response();
            var (co2DeadSpace, co2Rise, co2Fall) = Settings.ReadCo2Timing();
            var (co2Baseline, co2Target, co2Plateau) = Settings.ReadCo2Levels();
            config = config with
            {
                Co2BaselineMmHg = co2Baseline,
                Co2PlateauStartCentiMmHg = co2Plateau,
                Co2DeadSpaceMilliseconds = co2DeadSpace,
                Co2RiseMilliseconds = co2Rise,
                Co2FallMilliseconds = co2Fall,
                Co2TransportDelayMilliseconds = co2Delay,
                Co2DispersionStepMilliseconds = co2Dispersion,
                RespAmplitudeCounts = respAmplitude,
                RespCardiacArtifactCounts = respArtifact,
                BreathPeriodMilliseconds = breathPeriod,
                InspirationMilliseconds = inspiration,
                AbpPulsePermille = DesignPreviewSettings.ReadVitalValue(Settings.AbpPulseGain, 1000, "ABP 脉搏分量倍率"),
                PaPulsePermille = DesignPreviewSettings.ReadVitalValue(Settings.PaPulseGain, 1000, "PA 脉搏分量倍率"),
                CvpBaselineCentiMmHg = DesignPreviewSettings.ReadVitalValue(Settings.CvpBaseline, 100, "CVP 基线"),
                Co2EndExpiratoryMmHg = co2Target
            };
            if (Settings.CardiacRateEnabled.IsChecked == true)
            {
                if (Settings.EcgSelection != 0 || Settings.EjectionSelection == 2)
                { throw new ArgumentException("Preview.CardiacRateRequiresSinus"); }
                var rate = new SeededCardiacRate(DesignPreviewSettings.ReadVitalValue(Settings.HeartRate, 1, "心率目标"),
                    Settings.RateSeed.Text ?? "", DesignPreviewSettings.ReadVitalValue(Settings.RateVariation, 10, "心搏周期波动"));
                config = config with { SeededRate = rate }; ecgConfig = ecgConfig with { SeededRate = rate };
            }
            int co2Amplitude = DesignPreviewSettings.ReadVitalValue(Settings.EtCo2Variation, 100, "CO₂ 逐呼吸波动");
            if (co2Amplitude > 0 && co2Baseline != 0) { throw new ArgumentException("Preview.Co2BaselineVariationConflict"); }
            if (co2Amplitude > 0)
            { config = config with { SeededCo2 = new(config.Co2EndExpiratoryMmHg, co2Amplitude, Settings.RateSeed.Text ?? "") }; }
            int? opticalTarget = Settings.ReadOpticalTarget();
            SeededOpticalSaturation? opticalVariation = null;
            if (opticalTarget is { } target)
            {
                int amplitude = DesignPreviewSettings.ReadVitalValue(Settings.OpticalVariation, 1000, "SpO₂ 波动幅度");
                if (amplitude > 0) { opticalVariation = new(target, amplitude, Settings.RateSeed.Text ?? ""); }
            }
            var next = new LocalMonitorPreviewSession(config, Settings.ReadDisplay(), enableMeasurements: true,
                opticalSaturationMilliPercent: opticalTarget, opticalModulationPermille: opticalTarget is null ? 1000 : DesignPreviewSettings.ReadVitalValue(Settings.OpticalModulation, 1000, "光学脉动幅度"), opticalVariation: opticalVariation);
            var ecg = CapturePaper(ecgConfig);
            Pause(); _session = next; _monitor = new(next); _ecg = ecg;
            Settings.MarkShapeApplied(ecgConfig);
            MonitorView = new(_monitor);
            MonitorView.AudioPauseStatus.Text = Settings.Sound.AudioPauseText;
            MonitorView.AdditionalNotices = CurrentNotices;
            MonitorView.BeatSourceText = () => Settings.Sound.BeatSourceLabel;
            MonitorView.NoticeColorEnabled = () => Settings.Alerts.NoticeColorEnabled.IsChecked == true;
            Settings.Sound.ResetBeatSource(); Settings.Sound.ResetPitchState();
            SelectPage(Page); Settings.Status.Text = "已应用；监护从头开始，十二导联快照已更新。"; Start();
            if (_preferences is not null)
            {
                bool saved = _preferences.Save(new(next.Display, paperLayout, alarms, sound));
                PreferenceNotice.IsVisible = !saved;
                PreferenceNotice.Text = saved ? "" : "显示／报警／声音配置保存失败；本次运行已生效。";
                if (!saved) { Settings.Status.Text += "显示／报警／声音配置保存失败，重启后不会保留本次显示／报警／声音更改。"; }
            }
        }
        catch (ArgumentException exception) when (exception.Message is "SoundPreferences.Invalid" or "AlarmSound.InvalidTiming")
        { Settings.Status.Text = "未应用或保存：请检查声音页的音量、来源、暂停时长及报警声音间隔。原波形会话保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "AlarmPreferences.Invalid")
        { Settings.Status.Text = "未应用或保存：启用的报警阈值须完整且按 Critical 下限 < Warning 下限 < Warning 上限 < Critical 上限排列；SpO₂ 须 Critical 下限 < Warning 下限。CO₂ 未检出呼吸时限须为 5–120 秒。请检查报警页。原波形会话保持不变。"; }
        catch (ArgumentException exception) when (exception.ParamName == "rootSeedHex")
        { Settings.Status.Text = "未应用：共用种子须为 64 个小写十六进制字符（0–9、a–f）；可在生命体征 → 共用随机种子中点击生成随机种子。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.CardiacRateRequiresSinus")
        { Settings.Status.Text = "未应用：心率调整仅支持窦性参考及 1:1 下传；请关闭生命体征 → 心率中的调整开关，或改用兼容模板。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "SeededCo2.InvalidRange")
        { Settings.Status.Text = "未应用：CO₂ 呼气末目标减去／加上波动幅度后，须仍在 5–80 mmHg 内；请在生命体征 → 呼吸与 CO₂ 中调整目标或幅度。原运行与画面保持不变。"; }
        catch (EventWaveformException exception) when (exception.ReasonCode == "Capnogram.SeededPressureRequiresRegularBreathing")
        { Settings.Status.Text = "未应用：CO₂ 逐呼吸随机波动目前仅支持规则呼吸；请在生命体征 → 呼吸与 CO₂ 中将波动幅度设为 0，或选择规则呼吸。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message.StartsWith("Preview.InvalidVitalValue", StringComparison.Ordinal))
        { Settings.Status.Text = $"未应用：请填写有效的 {exception.ParamName}。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidCo2Levels")
        { Settings.Status.Text = "未应用：CO₂ 基线须为 0–80、呼气末目标须为 5–80 mmHg 的整数；平台起始高度最多两位小数，须位于基线与呼气末目标之间。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.Co2BaselineVariationConflict")
        { Settings.Status.Text = "未应用：当前模型的 CO₂ 逐呼吸随机波动要求基线为 0；请将基线归零或关闭波动。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidCo2Timing")
        { Settings.Status.Text = "未应用：CO₂ 死腔、上升和下降时长须为 1–10000 ms 的整数，并满足当前吸呼时程。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidCo2Response")
        { Settings.Status.Text = "未应用：CO₂ 管路延迟须为 0–5000 ms 的整数，展宽步长须为 0–500 ms 的整数。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidRespirationSignal")
        { Settings.Status.Text = "未应用：RESP 信号幅度须为 −1000–1000 的整数，心源性干扰须为 −200–200 的整数。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidInfarctionComponents")
        { Settings.Status.Text = "未应用：请检查 QRS 形态、混合比例及 ST／T 分量的整数值。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.InfarctionChestRequired")
        { Settings.Status.Text = "未应用：梗死快照须至少选择一个胸导联。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidInfarctionDelay")
        { Settings.Status.Text = "未应用：局部复极延长须为 0–500 ms 的整数，并满足当前心搏周期约束。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.TContourChestRequired")
        { Settings.Status.Text = "未应用：T 波目标须至少选择一个胸导联。原运行与画面保持不变。"; }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidBreathingTiming")
        { Settings.Status.Text = "未应用：" + Settings.BreathingConstraintDescription() + "原运行与画面保持不变。"; }
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
        if (ecg < 0 || ecg >= DesignPreviewSettings.EcgChoiceCount || resp is < 0 or > 3 || ejection is < 0 or > 3)
        { throw new ArgumentOutOfRangeException(nameof(ecg), "Preview.InvalidStyle"); }
        var config = ecg switch { 1 => PhysiologyDemoConfiguration.SinusArrestPreset, 2 => PhysiologyDemoConfiguration.PrematureVentricular, 3 => PhysiologyDemoConfiguration.SinusArrhythmiaPreset, 4 => PhysiologyDemoConfiguration.PrematureAtrial, 5 => PhysiologyDemoConfiguration.PrematureJunctional, 6 => PhysiologyDemoConfiguration.Fibrillation(), 7 => PhysiologyDemoConfiguration.Fibrillation(fine: true), 8 => PhysiologyDemoConfiguration.Flutter(2), 9 => PhysiologyDemoConfiguration.Flutter(4), >= 10 and <= 17 => SecondDegreeBlockPreset.Physiology(ecg - 10), 18 => PhysiologyDemoConfiguration.JunctionalEscape, 19 => PhysiologyDemoConfiguration.VentricularEscape, 20 => PhysiologyDemoConfiguration.Disorganized(AvConductionPattern.VentricularFlutterIllustration), 21 => PhysiologyDemoConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration), 22 => PhysiologyDemoConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationFineIllustration), 23 => PhysiologyDemoConfiguration.SvtPreset, 24 => PhysiologyDemoConfiguration.SvtPreset with { SvtRbbb = true }, 25 => PhysiologyDemoConfiguration.SvtPreset with { SvtLbbb = true }, 26 => PhysiologyDemoConfiguration.VtPreset, 27 => PhysiologyDemoConfiguration.VtPreset with { VtFusion = true }, 28 => PhysiologyDemoConfiguration.VtPreset with { VtCapture = true }, 29 => PhysiologyDemoConfiguration.VtPreset with { VtBidirectional = true }, 30 => PhysiologyDemoConfiguration.VtPreset with { VtTwisting = true }, 31 => PhysiologyDemoConfiguration.AarPreset, 32 => PhysiologyDemoConfiguration.AjrPreset, 33 => PhysiologyDemoConfiguration.AivrPreset, 34 => PhysiologyDemoConfiguration.AivrPreset with { AivrFusion = true }, 35 => PhysiologyDemoConfiguration.AivrPreset with { AivrCapture = true }, 36 => PhysiologyDemoConfiguration.AtrialEscapePreset, 37 => PhysiologyDemoConfiguration.Flutter(1), 38 => PhysiologyDemoConfiguration.Flutter(3), 39 => PhysiologyDemoConfiguration.VariableFlutter, 40 => PhysiologyDemoConfiguration.BlockedPrematureAtrial, 41 => PhysiologyDemoConfiguration.AberrantPrematureAtrial, 42 => PhysiologyDemoConfiguration.PrematureJunctional with { ConductionPattern = AvConductionPattern.PrematureJunctionalAfterQrsIllustration }, 43 => PhysiologyDemoConfiguration.PrematureJunctional with { ConductionPattern = AvConductionPattern.PrematureJunctionalOverlappingIllustration }, 44 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.VentricularBigeminyIllustration }, 45 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.VentricularTrigeminyIllustration }, 46 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.VentricularCoupletIllustration }, 47 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.InterpolatedPvcIllustration }, 48 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.PolymorphicPvcIllustration }, 49 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.MultifocalPvcIllustration }, 50 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.PolymorphicVentricularCoupletIllustration }, 51 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.RonTLongQtPvcIllustration }, 52 => PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = AvConductionPattern.ShortCoupledRonTPvcIllustration }, >= 53 and <= 58 => BundleBlockPreset.Physiology((EcgBundleBlockIllustration)(ecg - 52)), 59 => PhysiologyDemoConfiguration.WpwPreset with { WpwNegativeV1 = false, WpwSmallerDelta = false }, 60 => PhysiologyDemoConfiguration.WpwPreset with { WpwNegativeV1 = true, WpwSmallerDelta = false }, 61 => PhysiologyDemoConfiguration.WpwPreset with { WpwNegativeV1 = false, WpwSmallerDelta = true }, 62 => PhysiologyDemoConfiguration.WpwPreset with { WpwNegativeV1 = true, WpwSmallerDelta = true }, 63 => PhysiologyDemoConfiguration.ShortPrPreset, 64 => PhysiologyDemoConfiguration.NormalPrDeltaPreset, 65 => PhysiologyDemoConfiguration.NormalPrDeltaPreset with { ProlongedPrDelta = true }, >= 66 and <= 71 => PhysiologyDemoConfiguration.Fibrillation(fine: ecg % 2 == 1) with { IllustrateAfAberrancy = ecg is 66 or 67 or 70 or 71, IllustrateAfSystemicPulseDeficit = ecg >= 68 }, 72 => PhysiologyDemoConfiguration.Default with { CardiacActivity = CardiacActivity.Absent }, 73 => PhysiologyDemoConfiguration.Default with { VentricularMechanicalEnabled = false }, 74 => PhysiologyDemoConfiguration.Default with { CardiacActivity = CardiacActivity.AtrialOnly }, 75 => PhysiologyDemoConfiguration.Default with { HyperkalemiaRepolarization = true }, 76 or 77 => PhysiologyDemoConfiguration.Default with { HyperkalemiaRepolarization = true, HyperkalemiaConduction = true, HyperkalemiaAbsentP = ecg == 77, CardiacActivity = ecg == 77 ? CardiacActivity.VentricularOnly : CardiacActivity.AtrialAndVentricular }, >= 78 and <= 81 => PhysiologyDemoConfiguration.Default with { HypokalemiaRepolarization = true, HypokalemiaInvertedT = ecg == 79, HypokalemiaTuFusion = ecg == 80, HypokalemiaConduction = ecg == 81 }, >= 82 and <= 86 => PhysiologyDemoConfiguration.Default with { Calcium = (CalciumIllustration)(ecg - 81) }, >= 87 and <= 89 => PhysiologyDemoConfiguration.Default with { DigitalisEffect = true, DigitalisShape = (DigitalisTShape)(ecg - 87) }, >= 90 and <= 97 => PhysiologyDemoConfiguration.Default with { Quinidine = (QuinidineIllustration)((ecg - 90) % 4 + 1), QuinidineNotchedP = ecg >= 94 }, 98 => PhysiologyDemoConfiguration.Default with { HyperkalemiaRepolarization = true, HyperkalemiaConduction = true, HyperkalemiaAbsentP = true, HyperkalemiaFusion = true, CardiacActivity = CardiacActivity.VentricularOnly }, >= 99 and <= 101 => PhysiologyDemoConfiguration.Default with { AtrialShape = (EcgAtrialIllustration)(ecg - 98) }, >= 102 and <= 106 => PhysiologyDemoConfiguration.Default with { VentricularShape = (EcgVentricularIllustration)(ecg - 101) }, >= 107 and <= 114 => PhysiologyDemoConfiguration.Default with { TContour = TContourProductPreset.Create(ecg - 107) }, >= 115 and <= 124 => PhysiologyDemoConfiguration.Default with { Infarction = InfarctionProductPreset.Create(ecg - 115) }, >= 125 and <= 134 => PhysiologyDemoConfiguration.Default with { Infarction = InfarctionProductPreset.Create(ecg - 125, InfarctionTerritory.Lateral) }, >= 135 and <= 164 => PhysiologyDemoConfiguration.Default with { Infarction = InfarctionProductPreset.Create((ecg - 135) % 10, (InfarctionTerritory)((ecg - 135) / 10 + 3)) }, _ => PhysiologyDemoConfiguration.Default };
        var ecgConfig = ecg switch { 1 => ProjectedEcgDemoConfiguration.SinusArrestPreset, 2 => ProjectedEcgDemoConfiguration.PrematureVentricular, 3 => ProjectedEcgDemoConfiguration.SinusArrhythmiaPreset, 4 => ProjectedEcgDemoConfiguration.PrematureAtrial, 5 => ProjectedEcgDemoConfiguration.PrematureJunctional, 6 => ProjectedEcgDemoConfiguration.Fibrillation(), 7 => ProjectedEcgDemoConfiguration.Fibrillation(fine: true), 8 => ProjectedEcgDemoConfiguration.Flutter(2), 9 => ProjectedEcgDemoConfiguration.Flutter(4), >= 10 and <= 17 => SecondDegreeBlockPreset.Ecg(ecg - 10), 18 => ProjectedEcgDemoConfiguration.JunctionalEscape, 19 => ProjectedEcgDemoConfiguration.VentricularEscape, 20 => ProjectedEcgDemoConfiguration.Disorganized(AvConductionPattern.VentricularFlutterIllustration), 21 => ProjectedEcgDemoConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration), 22 => ProjectedEcgDemoConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationFineIllustration), 23 => ProjectedEcgDemoConfiguration.SvtPreset, 24 => ProjectedEcgDemoConfiguration.SvtRightBundlePreset, 25 => ProjectedEcgDemoConfiguration.SvtLeftBundlePreset, 26 => ProjectedEcgDemoConfiguration.VtPreset, 27 => ProjectedEcgDemoConfiguration.VtPreset with { VtFusion = true }, 28 => ProjectedEcgDemoConfiguration.VtPreset with { VtCapture = true }, 29 => ProjectedEcgDemoConfiguration.VtPreset with { VtBidirectional = true }, 30 => ProjectedEcgDemoConfiguration.VtPreset with { VtTwisting = true }, 31 => ProjectedEcgDemoConfiguration.AarPreset, 32 => ProjectedEcgDemoConfiguration.AjrPreset, 33 => ProjectedEcgDemoConfiguration.AivrPreset, 34 => ProjectedEcgDemoConfiguration.AivrPreset with { AivrFusion = true }, 35 => ProjectedEcgDemoConfiguration.AivrPreset with { AivrCapture = true }, 36 => ProjectedEcgDemoConfiguration.AtrialEscapePreset, 37 => ProjectedEcgDemoConfiguration.Flutter(1), 38 => ProjectedEcgDemoConfiguration.Flutter(3), 39 => ProjectedEcgDemoConfiguration.VariableFlutter, 40 => ProjectedEcgDemoConfiguration.BlockedPrematureAtrial, 41 => ProjectedEcgDemoConfiguration.AberrantPrematureAtrial, 42 => ProjectedEcgDemoConfiguration.PrematureJunctional with { ConductionPattern = AvConductionPattern.PrematureJunctionalAfterQrsIllustration }, 43 => ProjectedEcgDemoConfiguration.PrematureJunctional with { ConductionPattern = AvConductionPattern.PrematureJunctionalOverlappingIllustration }, 44 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.VentricularBigeminyIllustration), 45 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.VentricularTrigeminyIllustration), 46 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.VentricularCoupletIllustration), 47 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.InterpolatedPvcIllustration), 48 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.PolymorphicPvcIllustration), 49 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.MultifocalPvcIllustration), 50 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.PolymorphicVentricularCoupletIllustration), 51 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.RonTLongQtPvcIllustration), 52 => ProjectedEcgDemoConfiguration.Pvc(AvConductionPattern.ShortCoupledRonTPvcIllustration), >= 53 and <= 58 => BundleBlockPreset.Ecg((EcgBundleBlockIllustration)(ecg - 52)), 59 => ProjectedEcgDemoConfiguration.WpwPreset with { WpwNegativeV1 = false, WpwSmallerDelta = false, QrsDurationMilliseconds = 140 }, 60 => ProjectedEcgDemoConfiguration.WpwPreset with { WpwNegativeV1 = true, WpwSmallerDelta = false, QrsDurationMilliseconds = 140 }, 61 => ProjectedEcgDemoConfiguration.WpwPreset with { WpwNegativeV1 = false, WpwSmallerDelta = true, QrsDurationMilliseconds = 110 }, 62 => ProjectedEcgDemoConfiguration.WpwPreset with { WpwNegativeV1 = true, WpwSmallerDelta = true, QrsDurationMilliseconds = 110 }, 63 => ProjectedEcgDemoConfiguration.ShortPrPreset, 64 => ProjectedEcgDemoConfiguration.NormalPrDeltaPreset, 65 => ProjectedEcgDemoConfiguration.NormalPrDeltaPreset with { ProlongedPrDelta = true, PrIntervalMilliseconds = 240 }, >= 66 and <= 71 => ProjectedEcgDemoConfiguration.Fibrillation(fine: ecg % 2 == 1) with { IllustrateAfAberrancy = ecg is 66 or 67 or 70 or 71 }, 72 => ProjectedEcgDemoConfiguration.Default with { CardiacActivity = CardiacActivity.Absent }, 73 => ProjectedEcgDemoConfiguration.Default, 74 => ProjectedEcgDemoConfiguration.Default with { CardiacActivity = CardiacActivity.AtrialOnly }, 75 => ProjectedEcgDemoConfiguration.Hyperkalemia, 76 => ProjectedEcgDemoConfiguration.HyperkalemiaWithConduction, 77 => ProjectedEcgDemoConfiguration.HyperkalemiaWithoutP, 78 => ProjectedEcgDemoConfiguration.Hypokalemia, 79 => ProjectedEcgDemoConfiguration.Hypokalemia with { HypokalemiaInvertedT = true }, 80 => ProjectedEcgDemoConfiguration.Hypokalemia with { HypokalemiaTuFusion = true }, 81 => ProjectedEcgDemoConfiguration.HypokalemiaWithConduction, >= 82 and <= 86 => ProjectedEcgDemoConfiguration.CalciumPreset((CalciumIllustration)(ecg - 81)), >= 87 and <= 89 => ProjectedEcgDemoConfiguration.Digitalis with { DigitalisShape = (DigitalisTShape)(ecg - 87) }, >= 90 and <= 97 => ProjectedEcgDemoConfiguration.QuinidinePreset((QuinidineIllustration)((ecg - 90) % 4 + 1), ecg >= 94), 98 => ProjectedEcgDemoConfiguration.HyperkalemiaWithFusion, >= 99 and <= 101 => ProjectedEcgDemoConfiguration.Default with { Atrial = (EcgAtrialIllustration)(ecg - 98) }, >= 102 and <= 106 => ProjectedEcgDemoConfiguration.Default with { Ventricular = (EcgVentricularIllustration)(ecg - 101) }, >= 107 and <= 114 => ProjectedEcgDemoConfiguration.Default with { TContour = TContourProductPreset.Create(ecg - 107) }, >= 115 and <= 124 => ProjectedEcgDemoConfiguration.Default with { Infarction = InfarctionProductPreset.Create(ecg - 115) }, >= 125 and <= 134 => ProjectedEcgDemoConfiguration.Default with { Infarction = InfarctionProductPreset.Create(ecg - 125, InfarctionTerritory.Lateral) }, >= 135 and <= 164 => ProjectedEcgDemoConfiguration.Default with { Infarction = InfarctionProductPreset.Create((ecg - 135) % 10, (InfarctionTerritory)((ecg - 135) / 10 + 3)) }, _ => ProjectedEcgDemoConfiguration.Default };
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
    internal static LocalMonitorPreviewSession CreateStylePreview(int ecg, int resp, int ejection)
    {
        var preview = CreateThumbnailSource(ResolveStyle(ecg, resp, ejection).Physiology, StylePreviewCatalog.DurationNs(ecg));
        // The first authored AF long-short beat occurs at 12.966 s (electrical).
        // Keep twelve seconds of context while including that beat and its pulse tail.
        if (ecg is >= 66 and <= 71)
        {
            for (int i = 0; i < 40; i++) { preview.Advance(50_000_000); }
        }
        return preview;
    }
    private static LocalMonitorPreviewSession CreateThumbnailSource(PhysiologyDemoConfiguration configuration, long durationNs)
    {
        var preview = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default());
        // Include presentation latency before taking the requested complete interval.
        long end = Math.Max(5_400_000_000, durationNs + LocalMonitorPreviewSession.PresentationLatencyNs + 200_000_000);
        while (preview.SimulationTimeNs < end) { preview.Advance(50_000_000); }
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
