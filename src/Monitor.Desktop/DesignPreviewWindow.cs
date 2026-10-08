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
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Localization;
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
    private readonly TextBlock _title = Text("", 27, true);
    private readonly TextBlock _subtitle = Text("", 13);
    private readonly TextBlock _state = Text("", 12);
    private readonly ListBox _navigation = new();
    private WaveformEnvelope[] _ecg;
    private (ProjectedEcgDemoConfiguration Ecg, PhysiologyDemoConfiguration Physiology,
        WaveformEnvelope[] Paper, long EffectiveNs, int EcgSelection, int RespirationSelection, int EjectionSelection)? _pendingPresentation;
    private LiveMonitorTrace _monitor;
    internal LiveMonitorView MonitorView { get; private set; }
    private LocalMonitorPreviewSession _session;
    private DispatcherTimer? _timer;
    private long _lastTick;
    private bool _closed;
    private bool _applying;
    internal bool IsApplying => _applying;
    internal DesignPreviewSettings Settings { get; private set; }
    internal int Page { get; private set; }
    internal LocalMonitorPreviewSession Session => _session;
    internal DispatcherTimer? ActiveTimer => _timer;
    internal Ecg12PaperPage? EcgPage => _workspace.Content as Ecg12PaperPage;
    internal DesignPreviewTrace? CurrentPaper => EcgPage?.Paper;
    // Calipers survive page switches while the same paper and layout are shown.
    private Ecg12PaperMeasurement? _paperMeasurement;
    private WaveformEnvelope[]? _paperMeasurementBlocks;
    private bool _paperMeasuring;
    internal SystemViewCommandAssessmentPolicy MeasurementPolicy { get; private set; } = SystemViewCommandAssessmentPolicy.Enabled;
    internal LiveMonitorTrace MonitorTrace => _monitor;
    private readonly DisplayPreferenceStore? _preferences;
    private readonly LanguagePreferenceStore? _languagePreferences;
    internal DesktopLocalization Localization { get; }
    internal TextBlock LanguageNotice { get; } = Text("", 12);
    private static readonly string[] PageKeys = ["shell.monitor", "shell.ecg", "navigation.settings", "navigation.help", "navigation.about"];
    internal TextBlock PreferenceNotice { get; } = Text("", 12);
    internal DesignPreviewWindow(string? preferencesPath = null)
    {
        Title = ProductIdentity.WindowTitle;
        Width = 1440; Height = 940; MinWidth = 960; MinHeight = 640;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = DesktopFluentStyle.Canvas; Foreground = DesktopFluentStyle.Text;
        FontSize = 14; FontFamily = PreviewFont; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _preferences = preferencesPath is null ? null : new(preferencesPath);
        _languagePreferences = preferencesPath is null ? null : new(Path.ChangeExtension(preferencesPath, ".language.json"));
        bool languageRejected = false;
        Localization = new(_languagePreferences?.Load(out languageRejected));
        if (ProductIdentity.DevelopmentFeatures)
        { Localization.Bind(this, TitleProperty, "shell.developmentTitle", ProductIdentity.Name, ProductIdentity.Version); }
        LanguageNotice.IsVisible = languageRejected;
        Localization.Bind(LanguageNotice, TextBlock.TextProperty, "settings.languageRejected");
        bool rejected = false;
        var preferences = _preferences?.Load(out rejected) ?? new DisplayPreferences(MonitorDisplayConfiguration.Default(), 0);
        Settings = CreateSettings();
        Settings.RestoreDisplay(preferences.Display, preferences.PaperLayout, preferences.MeasurementMillimeters);
        var defaults = BuildConfiguredSources();
        _session = defaults.Session;
        _session.DiscardStartup();
        _ecg = defaults.Paper;
        Settings.MarkParametersApplied(defaults.Configuration, defaults.Physiology);
        if (preferences.Generator is { } generator)
        {
            try
            {
                Settings.RestoreGenerator(generator);
                var restored = BuildConfiguredSources();
                restored.Session.DiscardStartup();
                Settings.MarkParametersApplied(restored.Configuration, restored.Physiology);
                _session = restored.Session; _ecg = restored.Paper;
            }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            {
                Settings.Sound.Close(); Settings = CreateSettings();
                Settings.RestoreDisplay(preferences.Display, preferences.PaperLayout, preferences.MeasurementMillimeters);
                Settings.MarkParametersApplied(defaults.Configuration, defaults.Physiology);
                rejected = true;
            }
        }
        _monitor = new(_session, Localization); MonitorView = new(_monitor, Localization);
        if (preferences.Alarms is { } alarms) { Settings.Alerts.RestorePreferences(alarms); }
        if (preferences.Sound is { } sound) { Settings.Sound.RestorePreferences(sound, Settings.Alerts); }
        PreferenceNotice.IsVisible = rejected;
        if (rejected)
        {
            Localization.Bind(PreferenceNotice, TextBlock.TextProperty, "settings.recoveryNotice");
            SetStatus("settings.recoveryStatus");
        }
        ConnectSettings();
        var root = new Grid { ColumnDefinitions = new("184,*"), Background = Background };
        var sidebar = new DockPanel { Margin = new Thickness(16, 24) };
        var brand = new StackPanel { Spacing = 5, Margin = new Thickness(12, 0, 0, 32) };
        brand.Children.Add(Text(ProductIdentity.Name, 16, true)); brand.Children.Add(Text("V0.5 · Standalone", 12));
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(12, 20) };
        footer.Children.Add(PreferenceNotice);
        footer.Children.Add(LanguageNotice);
        footer.Children.Add(_state);
        var disclaimer = Text("", 12);
        Localization.Bind(disclaimer, TextBlock.TextProperty, "shell.disclaimer");
        footer.Children.Add(disclaimer);
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        SettingsSections.StyleNavigation(_navigation);
        _navigation.ItemsSource = PageKeys.Select(key => SettingsSections.Item(key, showChevron: false, localization: Localization)).ToArray();
        Localization.Bind(_navigation, AutomationProperties.NameProperty, "shell.navigation");
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
        Opened += (_, _) => { Settings.Sound.StartOutput(); Start(); }; Closed += (_, _) => { _closed = true; Pause(); Settings.Sound.Close(); };
        SelectPage(0); UpdateState();
    }
    private DesignPreviewSettings CreateSettings() => new(StylePreviewCatalog.Get, StylePreviewCatalog.Respiration,
            async () => await ApplySettingsAsync(), () => { if (_timer is null) { Start(); } else { Pause(); } },
            () => new WaveformDemoWindow(projected: true).Show(this), Localization);
    private void ConnectSettings()
    {
        if (IsVisible) { Settings.Sound.StartOutput(); }
        Settings.LanguageChanged += SelectLanguage;
        Settings.MeasurementUnits.SelectionChanged += (_, _) => EcgPage?.SetMeasurementUnits(Settings.MeasurementUnits.SelectedIndex == 1);
        void UpdateNotificationMode()
        {
            Settings.Sound.UseNotificationPlayback(Settings.Alerts.AlarmLifecycles);
            MonitorView.RefreshAttention();
            Settings.Sound.RefreshAlarmNotices(MonitorView.ActiveNotices);
        }
        BindAlarmAttention();
        Settings.Alerts.NotificationSettings.ModeChanged += UpdateNotificationMode;
        Settings.Alerts.EcgMonitoringEnabled.IsCheckedChanged += (_, _) => UpdateNotificationMode();
        UpdateNotificationMode();
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
        Settings.Restart.Click += async (_, _) => await ApplySettingsAsync(restart: true);
        Settings.ResetAll.Click += async (_, _) => await ConfirmResetAllSettings();
        Settings.Oxygenation.UpdateVentilation.Click += (_, _) => UpdateOxygenationVentilation();
    }
    private void SelectLanguage(string locale)
    {
        Localization.Select(locale);
        UpdateState();
        LanguageNotice.IsVisible = _languagePreferences is not null && !_languagePreferences.Save(Localization.Locale);
        Localization.Bind(LanguageNotice, TextBlock.TextProperty, "settings.languageSaveFailed");
    }

    private void SetStatus(string key, params object?[] arguments) =>
        Localization.Bind(Settings.Status, TextBlock.TextProperty, key, arguments);
    private void BindAlarmAttention()
    {
        var journals = Settings.Alerts.AlarmLifecycles;
        MonitorView.NoticeProjection = Settings.Alerts.ProjectAttention;
        MonitorView.AttentionFor = id =>
        {
            string conditionId = id.StartsWith(MonitorAlertSettings.RetainedNoticePrefix, StringComparison.Ordinal)
                ? id[MonitorAlertSettings.RetainedNoticePrefix.Length..] : id;
            foreach (var journal in journals)
            {
                var state = journal.Attention.Conditions.SingleOrDefault(c => c.ConditionId == conditionId);
                if (state is not null) { return state; }
            }
            return null;
        };
    }

    internal void UpdateOxygenationVentilation()
    {
        try
        {
            decimal multiplier = Settings.Oxygenation.ReadDemandMultiplier();
            long effectiveNs = _session.UpdateOxygenationVentilation(Settings.Oxygenation.ReadVentilation(), multiplier);
            decimal baseline = _session.OxygenationParameters!.OxygenDemandMlStpdPerMinute;
            SetStatus("settings.ventilationScheduled", effectiveNs / 1_000_000_000m, baseline, multiplier, baseline * multiplier);
        }
        catch (InvalidOperationException)
        { SetStatus("validation.oxygenDisabled"); }
        catch (ArgumentException error) when (error.Message == "Oxygenation.VentilationFlowTooHigh")
        { SetStatus("validation.ventilationFlow"); }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { SetStatus("validation.ventilationParameters"); }
    }
    internal Window? ResetConfirmation { get; private set; }
    private async Task ConfirmResetAllSettings()
    {
        if (ResetConfirmation is not null) { return; }
        var cancel = new Button { IsCancel = true, MinWidth = 88, MinHeight = 44 };
        var confirm = new Button
        { MinHeight = 44, Foreground = Brush.Parse("#B42318") };
        Localization.Bind(cancel, ContentControl.ContentProperty, "common.cancel");
        Localization.Bind(confirm, ContentControl.ContentProperty, "settings.resetTitle");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        var question = Text("", 20, true);
        Localization.Bind(question, TextBlock.TextProperty, "settings.resetQuestion");
        content.Children.Add(question);
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Localization.Bind(warning, TextBlock.TextProperty, "settings.resetWarning");
        content.Children.Add(warning);
        content.Children.Add(buttons);
        var dialog = new Window
        {
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = DesktopFluentStyle.Surface,
            FontFamily = PreviewFont,
            FontSize = 14,
            Content = content
        };
        Localization.Bind(dialog, TitleProperty, "settings.resetTitle");
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        ResetConfirmation = dialog;
        try
        {
            if (await dialog.ShowDialog<bool>(this) && !_closed) { ResetAllSettings(); }
        }
        finally { ResetConfirmation = null; }
    }

    internal void ResetAllSettings()
    {
        Pause();
        Settings.Sound.Close();
        SelectLanguage(BuiltInLocalizations.DefaultLocale);
        Settings = CreateSettings();
        ConnectSettings();
        RestartSettings();
        if (!PreferenceNotice.IsVisible) { SetStatus("settings.resetDone"); }
    }
    internal void SelectPage(int page)
    {
        if (page is < 0 or > 4) { throw new ArgumentOutOfRangeException(nameof(page)); }
        Page = page;
        _navigation.SelectedIndex = page;
        Localization.Bind(_title, TextBlock.TextProperty, PageKeys[page]);
        if (page == 0) { Localization.Bind(_subtitle, TextBlock.TextProperty, "shell.monitorSubtitle", _session.Display.Slots.Count); }
        else
        {
            string key = page switch { 1 => "shell.ecgSubtitle", 2 => "shell.settingsSubtitle", 3 => "shell.helpSubtitle", _ => "shell.aboutSubtitle" };
            Localization.Bind(_subtitle, TextBlock.TextProperty, key);
        }
        _workspace.Content = page switch
        {
            0 => MonitorView,
            1 => CreateEcgPage(),
            2 => Settings,
            3 => DesktopInformationPages.CreateHelp(Localization),
            _ => DesktopInformationPages.CreateAbout(Localization)
        };
    }
    private Ecg12PaperPage CreateEcgPage()
    {
        var paper = new DesignPreviewTrace(_ecg, Settings.PaperLayout.SelectedIndex == 1, Localization);
        if (_paperMeasurement is null || !ReferenceEquals(_paperMeasurementBlocks, _ecg) || _paperMeasurement.Layout.SixRows != paper.SixRows)
        {
            _paperMeasurement = new Ecg12PaperMeasurement(paper.Layout, _ecg, lead => ProjectedEcgDemoSource.ChannelId((EcgLead)lead), MeasurementPolicy);
            _paperMeasurementBlocks = _ecg;
        }
        var page = new Ecg12PaperPage(paper, _paperMeasurement, Localization, _paperMeasuring, Settings.MeasurementUnits.SelectedIndex == 1);
        page.MeasuringChanged += measuring => _paperMeasuring = measuring;
        return page;
    }

    // Teaching and assessment sessions may disable or lock manual measurement.
    internal void SetMeasurementPolicy(SystemViewCommandAssessmentPolicy policy)
    {
        if (!Enum.IsDefined(policy)) { throw new ArgumentOutOfRangeException(nameof(policy)); }
        MeasurementPolicy = policy;
        _paperMeasurement?.UpdatePolicy(policy);
        if (Page == 1) { SelectPage(1); }
    }

    private (LocalMonitorPreviewSession Session, ProjectedEcgDemoConfiguration Configuration, WaveformEnvelope[] Paper, PhysiologyDemoConfiguration Physiology) BuildConfiguredSources() => CaptureSourceBuilder()();

    // Capture controls on the dispatcher; the returned computation owns no UI objects.
    private Func<(LocalMonitorPreviewSession Session, ProjectedEcgDemoConfiguration Configuration, WaveformEnvelope[] Paper, PhysiologyDemoConfiguration Physiology)> CaptureSourceBuilder()
    {
        if (Settings.EcgSelection < 0 || Settings.EcgSelection >= DesignPreviewSettings.EcgChoiceCount ||
            Settings.RespirationSelection is < 0 or > 3 || Settings.EjectionSelection is < 0 or > 3)
        { throw new ArgumentException("GeneratorPreferences.InvalidSelection"); }
        var (config, ecgConfig) = ResolveStyle(Settings.EcgSelection, Settings.RespirationSelection, Settings.EjectionSelection);
        if (SeededRhythmSchedule.Supports(config.ConductionPattern))
        {
            var rhythm = Settings.ReadRhythm(config.ConductionPattern);
            config = config with { RhythmSchedule = rhythm };
            ecgConfig = ecgConfig with { RhythmSchedule = rhythm };
        }
        var zones = Settings.InfarctionParameters.ReadZones(ecgConfig.Infarction);
        var infarction = zones is null ? Settings.InfarctionParameters.Read(ecgConfig.Infarction) : null;
        config = config with { Infarction = infarction, Zones = zones }; ecgConfig = ecgConfig with { Infarction = infarction, Zones = zones };
        var contour = Settings.TContourParameters.Read(ecgConfig.TContour);
        config = config with { TContour = contour }; ecgConfig = ecgConfig with { TContour = contour };
        var (breathPeriod, inspiration) = Settings.ReadBreathingTiming();
        var (respAmplitude, respArtifact) = Settings.ReadRespirationSignal();
        var (co2DelayMilliseconds, co2DispersionMilliseconds) = Settings.ReadCo2Response();
        var (co2DeadSpaceMilliseconds, co2RiseMilliseconds, co2FallMilliseconds) = Settings.ReadCo2Timing();
        var (co2BaselineMmHg, co2TargetMmHg, co2PlateauCentiMmHg) = Settings.ReadCo2Levels();
        config = config with
        {
            Co2BaselineMmHg = co2BaselineMmHg,
            Co2PlateauStartCentiMmHg = co2PlateauCentiMmHg,
            Co2DeadSpaceMilliseconds = co2DeadSpaceMilliseconds,
            Co2RiseMilliseconds = co2RiseMilliseconds,
            Co2FallMilliseconds = co2FallMilliseconds,
            Co2TransportDelayMilliseconds = co2DelayMilliseconds,
            Co2DispersionStepMilliseconds = co2DispersionMilliseconds,
            RespAmplitudeCounts = respAmplitude,
            RespCardiacArtifactCounts = respArtifact,
            BreathPeriodMilliseconds = breathPeriod,
            InspirationMilliseconds = inspiration,
            AbpPulsePermille = Settings.AbpTargetEnabled.IsChecked == true ? 1000 : DesignPreviewSettings.ReadVitalValue(Settings.AbpPulseGain, 1000, "vitals.abpPulseGainField"),
            PaPulsePermille = Settings.PaTargetEnabled.IsChecked == true ? 1000 : DesignPreviewSettings.ReadVitalValue(Settings.PaPulseGain, 1000, "vitals.paPulseGainField"),
            AbpTarget = Settings.ReadPressureTarget(arterial: true),
            PaTarget = Settings.ReadPressureTarget(arterial: false),
            PressureVariation = Settings.ReadPressureVariation(),
            CvpBaselineCentiMmHg = DesignPreviewSettings.ReadVitalValue(Settings.CvpBaseline, 100, "vitals.cvpBaselineField"),
            Co2EndExpiratoryMmHg = co2TargetMmHg
        };
        if (Settings.CardiacRateEnabled.IsChecked == true)
        {
            var adjustment = Settings.ReadCardiacRate(validateSources: false);
            if (adjustment is not null)
            {
                if (Settings.EcgSelection == 0 && Settings.EjectionSelection != 2 && adjustment.Rate.HeartRateBpm is >= 30 and <= 180)
                {
                    var rate = new SeededCardiacRate(adjustment.Rate.HeartRateBpm, adjustment.Rate.SeedHex, adjustment.Rate.VariationPermille);
                    config = config with { SeededRate = rate };
                    ecgConfig = ecgConfig with { SeededRate = rate };
                }
                else
                {
                    config = config with { RateAdjustment = adjustment };
                    ecgConfig = ecgConfig with { RateAdjustment = adjustment };
                }
            }
        }
        int co2AmplitudeCentiMmHg = DesignPreviewSettings.ReadVitalValue(Settings.EtCo2Variation, 100, "vitals.etco2VariationField");
        if (co2AmplitudeCentiMmHg > 0 && co2BaselineMmHg != 0) { throw new ArgumentException("Preview.Co2BaselineVariationConflict"); }
        if (co2AmplitudeCentiMmHg > 0)
        { config = config with { SeededCo2 = new(config.Co2EndExpiratoryMmHg, co2AmplitudeCentiMmHg, Settings.RateSeed.Text ?? "") }; }
        int? opticalTarget = Settings.ReadOpticalTarget();
        SeededOpticalSaturation? opticalVariation = null;
        if (opticalTarget is { } target)
        {
            int amplitude = DesignPreviewSettings.ReadVitalValue(Settings.OpticalVariation, 1000, "vitals.opticalVariationField");
            if (amplitude > 0) { opticalVariation = new(target, amplitude, Settings.RateSeed.Text ?? ""); }
        }
        var manualVitals = Settings.ManualVitals.Read();
        var display = Settings.ReadDisplay();
        int modulation = Settings.OpticalEnabled.IsChecked != true ? 1000 : DesignPreviewSettings.ReadVitalValue(Settings.OpticalModulation, 1000, "vitals.opticalModulationField");
        var oxygenation = Settings.OpticalEnabled.IsChecked == true && Settings.Oxygenation.Realtime.IsChecked == true
            ? Settings.Oxygenation.ReadConfiguration() : null;
        return () =>
        {
            var next = new LocalMonitorPreviewSession(config, display, enableMeasurements: true,
                opticalSaturationMilliPercent: opticalTarget, opticalModulationPermille: modulation,
                opticalVariation: opticalVariation, realtimeOxygenation: oxygenation, manualVitals: manualVitals);
            return (next, ecgConfig, CapturePaper(ecgConfig), config);
        };
    }
    internal void ApplySettings() => ApplySettingsCore(restart: false, background: false).GetAwaiter().GetResult();
    internal void RestartSettings() => ApplySettingsCore(restart: true, background: false).GetAwaiter().GetResult();
    internal Task ApplySettingsAsync(bool restart = false) => ApplySettingsCore(restart, background: true);

    private async Task ApplySettingsCore(bool restart, bool background)
    {
        if (_applying || _closed) { return; }
        _applying = true;
        var editor = Settings;
        if (background)
        {
            editor.Apply.IsEnabled = false;
            editor.Restart.IsEnabled = false;
            editor.ResetAll.IsEnabled = false;
            SetStatus("settings.preparing");
        }
        try
        {
            _ = Settings.Alerts.NotificationSettings.Read();
            var alarms = _preferences is null ? null : Settings.Alerts.CapturePreferences();
            var sound = _preferences is null ? null : Settings.Sound.CapturePreferences(Settings.Alerts);
            int paperLayout = Settings.PaperLayout.SelectedIndex;
            bool measurementMillimeters = Settings.MeasurementUnits.SelectedIndex == 1;
            if (paperLayout is < 0 or > 1) { throw new ArgumentException("Preview.InvalidPaperLayout"); }
            var build = CaptureSourceBuilder();
            var generator = _preferences is null ? null : Settings.CaptureGenerator();
            long delayNs = Settings.ReadApplyDelayNs();
            var selection = (Settings.EcgSelection, Settings.RespirationSelection, Settings.EjectionSelection);
            var prepared = background ? await Task.Run(() =>
            {
                var result = build();
                if (restart) { result.Session.DiscardStartup(); }
                return result;
            }) : build();
            if (_closed || !ReferenceEquals(editor, Settings)) { return; }
            var (next, ecgConfig, ecg, physiology) = prepared;
            long? effective = null;
            if (restart)
            {
                if (!background) { next.DiscardStartup(); }
                Pause();
                _session = next;
                _pendingPresentation = null;
                _ecg = ecg;
                Settings.MarkParametersApplied(ecgConfig, physiology, selection.EcgSelection,
                    selection.RespirationSelection, selection.EjectionSelection);
                Settings.Alerts.Reset();
                Settings.Sound.ResetBeatSource();
                Settings.Sound.ResetPitchState();
            }
            else
            {
                effective = _session.ScheduleSource(next, delayNs);
                _pendingPresentation = (ecgConfig, physiology, ecg, effective.Value,
                    selection.EcgSelection, selection.RespirationSelection, selection.EjectionSelection);
                _session.UpdateDisplay(next.Display);
            }
            _monitor = new(_session, Localization);
            MonitorView = new(_monitor, Localization);
            BindAlarmAttention();
            MonitorView.AudioPauseStatus.Text = Settings.Sound.AudioPauseText;
            MonitorView.AdditionalNotices = CurrentNotices;
            MonitorView.BeatSourceText = () => Settings.Sound.BeatSourceLabel;
            MonitorView.NoticeColorEnabled = () => Settings.Alerts.NoticeColorEnabled.IsChecked == true;
            MonitorView.Refresh();
            if (Page is 0 or 1 || (Page == 2 && !ReferenceEquals(_workspace.Content, Settings)))
            { SelectPage(Page); }
            if (restart) { SetStatus("settings.restarted"); }
            else { SetStatus("settings.scheduled", effective / 1_000_000_000m); }
            if (restart) { Start(); }
            if (_preferences is not null)
            {
                bool saved = _preferences.Save(new(next.Display, paperLayout, alarms, sound, generator, measurementMillimeters));
                PreferenceNotice.IsVisible = !saved;
                Localization.Bind(PreferenceNotice, TextBlock.TextProperty, "settings.preferenceSaveFailed");
                if (!saved) { SetStatus("settings.preferenceSaveWarning"); }
            }
        }
        catch (ArgumentException exception) when (exception.Message == "Preview.OxygenationBaselineRequiresRestart")
        { SetStatus("validation.baselineRestart"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidApplyDelay")
        { SetStatus("validation.applyDelay"); }
        catch (ArgumentException exception) when (exception.Message is "SoundPreferences.Invalid" or "AlarmSound.InvalidTiming")
        { SetStatus("validation.sound"); }
        catch (ArgumentException exception) when (exception.Message == "AlarmNotification.InvalidDraft")
        { SetStatus("validation.notification"); }
        catch (ArgumentException exception) when (exception.Message == "AlarmPreferences.Invalid")
        { SetStatus("validation.alarmLimits"); }
        catch (ArgumentException exception) when (exception.ParamName == "rootSeedHex")
        { SetStatus("validation.seed"); }
        catch (ArgumentException exception) when (exception.Message.StartsWith("CardiacRate.", StringComparison.Ordinal))
        { SetStatus("validation." + exception.Message); }
        catch (ArgumentException exception) when (exception.Message == "SeededCo2.InvalidRange")
        { SetStatus("validation.co2Range"); }
        catch (EventWaveformException exception) when (exception.ReasonCode == "Capnogram.SeededPressureRequiresRegularBreathing")
        { SetStatus("validation.co2Regular"); }
        catch (ArgumentException exception) when (exception.Message.StartsWith("ManualVitals.", StringComparison.Ordinal))
        { SetStatus(exception.Message); }
        catch (VitalValueException exception)
        {
            Localization.Bind(Settings.Status, TextBlock.TextProperty, text => text.Format("validation.vitalValue",
                text.Format("validation.vitalField", DesktopLocalization.Label(text, exception.FieldName), exception.Minimum, exception.Maximum, exception.Step)));
        }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidCo2Levels")
        { SetStatus("validation.co2Levels"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.Co2BaselineVariationConflict")
        { SetStatus("validation.co2Baseline"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidCo2Timing")
        { SetStatus("validation.co2Timing"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidCo2Response")
        { SetStatus("validation.co2Response"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidRespirationSignal")
        { SetStatus("validation.respirationSignal"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidInfarctionComponents")
        { SetStatus("validation.qrs"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.InfarctionChestRequired")
        { SetStatus("validation.infarctionLead"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidInfarctionDelay")
        { SetStatus("validation.repolarization"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.TContourChestRequired")
        { SetStatus("validation.waveLead"); }
        catch (ArgumentException exception) when (exception.Message == "Preview.InvalidBreathingTiming")
        { SetStatus("validation.breathingDetail", Settings.BreathingConstraintDescription()); }
        catch (ArgumentException exception) when (exception.Message == "Oxygenation.VentilationFlowTooHigh")
        { SetStatus("validation.oxygenFlow"); }
        catch (ArgumentException exception) when (exception.Message is "Preview.PressureTargetPulseTooSmall" or "Physiology.PressureTargetPulseTooSmall")
        { SetStatus("validation.pressureTargetPulse"); }
        catch (ArgumentException exception) when (exception.Message == "Physiology.PressureTargetUnreachable")
        { SetStatus("validation.pressureTargetUnreachable"); }
        catch (ArgumentException exception) when (exception.Message == "Physiology.PressureTargetRequiresReservoirMorphology")
        { SetStatus("validation.pressureTargetUnsupported"); }
        catch (ArgumentException exception) when (exception.Message is "Physiology.PressureVariationTooLarge" or "Physiology.PressureVariationRequiresReservoir")
        { SetStatus("validation.pressureVariation"); }
        catch (ArgumentException exception) when (exception.Message.StartsWith("OxygenationDefaults.", StringComparison.Ordinal) ||
            exception.Message.StartsWith("OxygenReservoir.", StringComparison.Ordinal))
        {
            string reason = OxygenationPatientPanel.Explain(exception);
            Localization.Bind(Settings.Status, TextBlock.TextProperty, text => text.Format("validation.patientDetail", text.GetString(reason)));
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { if (!_closed && ReferenceEquals(editor, Settings)) { SetStatus("validation.configuration"); } }
        finally
        {
            _applying = false;
            editor.Apply.IsEnabled = true;
            editor.Restart.IsEnabled = true;
            editor.ResetAll.IsEnabled = true;
        }
    }
    private IEnumerable<MonitorNotice> CurrentNotices(Monitor.Application.Measurements.LiveMeasurementSnapshot snapshot)
    {
        foreach (var notice in Settings.Alerts.Notices(snapshot,
            _session.Measurements?.SampleTimeNs == snapshot.SampleTimeNs ? _session.DetectedMonitoringEvents : null,
            _session.Measurements?.SampleTimeNs == snapshot.SampleTimeNs ? _session.DetectedRhythmEvents : null)) { yield return notice; }
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
            _session.Advance(deltaNs);
            if (_pendingPresentation is { } applied && _session.PendingSourceTimeNs is null)
            {
                _ecg = applied.Paper;
                Settings.MarkParametersApplied(applied.Ecg, applied.Physiology, applied.EcgSelection,
                    applied.RespirationSelection, applied.EjectionSelection);
                _pendingPresentation = null;
                SetStatus("settings.applied", applied.EffectiveNs / 1_000_000_000m);
                if (Page == 1) { SelectPage(Page); }
            }
            _monitor.InvalidateVisual();
            MonitorView.Refresh();
            Settings.Sound.UpdateAlarm(MonitorView.HighestNotice, Settings.Alerts.Timing, _session.DetectedBeats,
                _session.DetectedPulses, _session.Measurements, MonitorView.ActiveNotices);
            UpdateState();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { Pause(); SetStatus("settings.generationFailed"); }
    }
    private void UpdateState()
    {
        _state.Text = Localization.Format("shell.state", Localization.Get(_timer is null ? "shell.paused" : "shell.running"), _session.SimulationTimeNs / 1_000_000_000);
        if (Settings is not null) { Settings.Run.Content = Localization.Get(_timer is null ? "settings.resume" : "settings.pause"); }
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
        if (ejection == 1 && ecg != 2) { throw new ArgumentException("Preview.EjectionRequiresPvc"); }
        if (ejection == 2)
        {
            if (ecg != 0) { throw new ArgumentException("Preview.EjectionRequiresSinus"); }
            config = config with { VentricularConductionRatio = 2 };
            ecgConfig = ecgConfig with { VentricularConductionRatio = 2 };
        }
        if (ejection == 3) { config = config with { VentricularMechanicalEnabled = false }; }
        if (SeededRhythmSchedule.Supports(config.ConductionPattern))
        {
            config = config with { RhythmSchedule = SeededRhythmSchedule.Default };
            ecgConfig = ecgConfig with { RhythmSchedule = SeededRhythmSchedule.Default };
        }
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
            foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
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
        { foreach (byte[] bytes in source.AdvanceTo(step * 200_000_000L, 50, 1, 100)) { output.Add(WaveformEnvelopeCodec.Decode(bytes)); } }
        return output.Take(55).ToArray();
    }
    private static TextBlock Text(string value, double size, bool strong = false) => new()
    { Text = value, FontSize = size, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
}
