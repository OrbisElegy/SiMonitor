// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Application.Localization;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class MonitorAlertSettings : StackPanel
{
    private readonly DesktopLocalization _localization;
    private string? _noExpirationErrorKey;
    internal MonitorChannels MonitoredChannels { get; private set; } = MonitorChannels.All;
    internal bool Monitors(MonitorNumeric numeric) => (MonitoredChannels & MonitorChannelMapping.ForNumeric(numeric)) != 0;
    internal void SetMonitoredChannels(MonitorChannels channels)
    {
        if (channels == MonitoredChannels) { return; }
        MonitoredChannels = channels;
        if (!Monitors(MonitorNumeric.HeartRate))
        {
            _ecgNotices.Reset(AlarmTransitionReason.Disabled);
            _heartRateNotice.Reset(AlarmTransitionReason.Disabled);
        }
        if (!Monitors(MonitorNumeric.SpO2)) { _spO2Notice.Reset(AlarmTransitionReason.Disabled); }
        if (!Monitors(MonitorNumeric.EtCo2)) { _noExpirationNotice.Reset(AlarmTransitionReason.Disabled); }
        AdditionalLimits.SetMonitoredChannels(channels);
        HeartRateEnabled.IsEnabled = EcgMonitoringEnabled.IsEnabled = Monitors(MonitorNumeric.HeartRate);
        SpO2Enabled.IsEnabled = Monitors(MonitorNumeric.SpO2);
        NoExpirationEnabled.IsEnabled = Monitors(MonitorNumeric.EtCo2);
    }
    private readonly ConfirmedLimitNotice _heartRateNotice = new(MonitorNumeric.HeartRate);
    private readonly ConfirmedLimitNotice _spO2Notice = new(MonitorNumeric.SpO2);
    private readonly EcgAlarmNotices _ecgNotices = new();
    internal CheckBox EcgMonitoringEnabled { get; } = new() { IsChecked = true };
    internal TabItem EcgMonitoringPage { get; private set; } = null!;
    private readonly ConfirmedNoExpirationNotice _noExpirationNotice = new();
    private readonly TextBlock _noExpirationError = new() { Foreground = Avalonia.Media.Brushes.OrangeRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
    internal AlarmConfirmationEditor HeartRateConfirmation { get; }
    internal AlarmConfirmationEditor SpO2Confirmation { get; }
    internal CheckBox HeartRateEnabled { get; } = new() { IsChecked = false };
    internal NumericUpDown WarningLowHeartRate { get; } = Number(50, 1, 299);
    internal NumericUpDown CriticalLowHeartRate { get; } = Number(40, 1, 298);
    internal NumericUpDown WarningHeartRate { get; } = Number(120, 20, 300);
    internal NumericUpDown CriticalHeartRate { get; } = Number(180, 21, 350);
    internal CheckBox SpO2Enabled { get; } = new() { IsChecked = false };
    internal NumericUpDown WarningSpO2 { get; } = Number(92, 1, 100);
    internal NumericUpDown CriticalSpO2 { get; } = Number(85, 1, 99);
    internal CheckBox NoExpirationEnabled { get; } = new() { IsChecked = false };
    internal NumericUpDown NoExpirationSeconds { get; } = new() { Minimum = 5, Maximum = 120, Value = 20, Increment = 1, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown NoExpirationTriggerSeconds { get; } = AlarmConfirmationEditor.CreateTimingField();
    internal NumericUpDown NoExpirationRecoverySeconds { get; } = AlarmConfirmationEditor.CreateTimingField();
    internal AdditionalMeasurementLimits AdditionalLimits { get; }
    internal ComboBox TestLevel { get; } = new() { SelectedIndex = 0, MinWidth = 220 };
    internal CheckBox InfoTone { get; } = new();
    internal CheckBox NoticeColorEnabled { get; } = new() { IsChecked = true };
    internal ComboBox TestNumeric { get; } = new() { SelectedIndex = 0, MinWidth = 220 };
    internal NumericUpDown InfoInterval { get; } = Number(30, 5, 120);
    internal NumericUpDown NoticeInterval { get; } = Number(10, 1.5m, 60);
    internal NumericUpDown WarningInterval { get; } = Number(5, 3.5m, 60);
    internal NumericUpDown CriticalInterval { get; } = Number(1.5m, .25m, 2);
    internal IReadOnlyList<AlarmLifecycleJournal> AlarmLifecycles =>
        new[] { _heartRateNotice.Lifecycle, _spO2Notice.Lifecycle, _noExpirationNotice.Lifecycle, _ecgNotices.Lifecycle }.Concat(AdditionalLimits.Lifecycles).ToArray();
    internal AlarmNotificationSettingsPanel NotificationSettings { get; }
    // Titles are catalog keys.
    internal IReadOnlyList<(MonitorNumeric Numeric, string Title)> Parameters { get; } =
        MeasuredLimitNotice.Descriptors.Select(d => (d.Numeric, d.LabelKey))
            .Prepend((MonitorNumeric.SpO2, MeasuredLimitNotice.SpO2Descriptor.LabelKey))
            .Prepend((MonitorNumeric.HeartRate, "alarm.parameterHr")).ToArray();
    internal IReadOnlyDictionary<MonitorNumeric, TabItem> SoundPages => _soundPages;
    private readonly Dictionary<MonitorNumeric, TabItem> _soundPages = [];

    internal MonitorAlertSettings(DesktopLocalization? localization = null)
    {
        _localization = localization ?? new DesktopLocalization();
        HeartRateConfirmation = new(MeasuredLimitNotice.HeartRateDescriptor, _localization);
        SpO2Confirmation = new(MeasuredLimitNotice.SpO2Descriptor, _localization);
        AdditionalLimits = new(_localization);
        _localization.Bind(EcgMonitoringEnabled, ContentControl.ContentProperty, "alarm.ecgEnabled");
        EcgMonitoringEnabled.IsCheckedChanged += (_, _) => _ecgNotices.Reset(EcgMonitoringEnabled.IsChecked == true
            ? AlarmTransitionReason.ConfigurationChanged : AlarmTransitionReason.Disabled);
        _localization.Bind(HeartRateEnabled, ContentControl.ContentProperty, "alarm.hrEnabled");
        _localization.Bind(SpO2Enabled, ContentControl.ContentProperty, "alarm.spo2Enabled");
        _localization.Bind(NoExpirationEnabled, ContentControl.ContentProperty, "alarm.noExpirationEnabled");
        _localization.Bind(InfoTone, ContentControl.ContentProperty, "alarm.infoTone");
        _localization.Bind(NoticeColorEnabled, ContentControl.ContentProperty, "alarm.noticeColor");
        _localization.SetChoices(TestLevel, "alarm.testOff", "alarm.testInfo", "alarm.testNotice", "alarm.testWarning", "alarm.testCritical");
        _localization.SetChoices(TestNumeric, new Func<ITextLocalizer, string>[]
        {
            text => text.GetString("alarm.testBannerOnly"), _ => "HR", _ => "RR · RESP", _ => "SpO₂", _ => "PR", _ => "EtCO₂", _ => "RR · CO₂",
            text => text.GetString("numeric.abpMean"), text => text.GetString("numeric.paMean"), text => text.GetString("numeric.cvpMean"),
        });
        _localization.LocaleChanged += () =>
        {
            if (_noExpirationErrorKey is { } key) { _noExpirationError.Text = _localization.Get(key); }
        };
        HeartRateEnabled.IsCheckedChanged += (_, _) => _heartRateNotice.Reset(HeartRateEnabled.IsChecked == true
            ? AlarmTransitionReason.ConfigurationChanged : AlarmTransitionReason.Disabled);
        SpO2Enabled.IsCheckedChanged += (_, _) => _spO2Notice.Reset(SpO2Enabled.IsChecked == true
            ? AlarmTransitionReason.ConfigurationChanged : AlarmTransitionReason.Disabled);
        foreach (var field in new[] { CriticalLowHeartRate, WarningLowHeartRate, WarningHeartRate, CriticalHeartRate }.Concat(HeartRateConfirmation.Fields))
        { field.ValueChanged += (_, _) => _heartRateNotice.Reset(AlarmTransitionReason.ConfigurationChanged); }
        foreach (var field in new[] { CriticalSpO2, WarningSpO2 }.Concat(SpO2Confirmation.Fields))
        { field.ValueChanged += (_, _) => _spO2Notice.Reset(AlarmTransitionReason.ConfigurationChanged); }
        NoExpirationEnabled.IsCheckedChanged += (_, _) => NoExpirationEdited();
        foreach (var field in new[] { NoExpirationSeconds, NoExpirationTriggerSeconds, NoExpirationRecoverySeconds })
        { field.ValueChanged += (_, _) => NoExpirationEdited(); }
        Margin = new Thickness(20); Spacing = 12;
        Children.Add(HeartRateEnabled);
        var heartRateThresholds = AlarmConfirmationEditor.CreateFieldsPanel();
        Row("alarm.criticalLowHr", CriticalLowHeartRate, heartRateThresholds);
        Row("alarm.warningLowHr", WarningLowHeartRate, heartRateThresholds);
        Row("alarm.warningHighHr", WarningHeartRate, heartRateThresholds);
        Row("alarm.criticalHighHr", CriticalHeartRate, heartRateThresholds);
        HeartRateConfirmation.SetThresholdContent(heartRateThresholds);
        Children.Add(HeartRateConfirmation);
        Children.Add(DesktopInformationPages.Help("topic-12"));
        Children.Add(SpO2Enabled);
        var saturationThresholds = AlarmConfirmationEditor.CreateFieldsPanel();
        Row("alarm.criticalLowSpo2", CriticalSpO2, saturationThresholds);
        Row("alarm.warningLowSpo2", WarningSpO2, saturationThresholds);
        SpO2Confirmation.SetThresholdContent(saturationThresholds);
        Children.Add(SpO2Confirmation);
        Children.Add(DesktopInformationPages.Help("topic-13"));
        foreach (var descriptor in MeasuredLimitNotice.Descriptors)
        {
            Children.Add(AdditionalLimits.Editors[descriptor.Numeric]);
            if (descriptor.Numeric == MonitorNumeric.EtCo2)
            {
                var absence = new StackPanel { Spacing = 6 };
                absence.Children.Add(NoExpirationEnabled);
                var absenceFields = AlarmConfirmationEditor.CreateFieldsPanel();
                absenceFields.MaxWidth = 660;
                Row("alarm.noExpirationWait", NoExpirationSeconds, absenceFields);
                Row("alarm.noExpirationTrigger", NoExpirationTriggerSeconds, absenceFields);
                Row("alarm.noExpirationRecovery", NoExpirationRecoverySeconds, absenceFields);
                foreach (var row in absenceFields.Children.Cast<StackPanel>())
                {
                    row.Width = 200;
                    row.Margin = new Thickness(0, 0, 16, 8);
                    ((NumericUpDown)row.Children[1]).Width = 200;
                }
                absence.Children.Add(absenceFields);
                var resetAbsence = new Button();
                _localization.Bind(resetAbsence, ContentControl.ContentProperty, "alarm.resetConfirmation");
                resetAbsence.Click += (_, _) =>
                {
                    NoExpirationTriggerSeconds.Value = 0;
                    NoExpirationRecoverySeconds.Value = 0;
                };
                var absenceActions = new WrapPanel { Orientation = Orientation.Horizontal };
                absenceActions.Children.Add(resetAbsence);
                var effect = Text("alarm.changeEffect");
                effect.Margin = new Thickness(12, 4, 0, 4);
                effect.VerticalAlignment = VerticalAlignment.Center;
                absenceActions.Children.Add(effect);
                absenceActions.Children.Add(DesktopInformationPages.Help("topic-14"));
                absence.Children.Add(absenceActions);
                absence.Children.Add(_noExpirationError);
                AdditionalLimits.Editors[descriptor.Numeric].Confirmation.AddPage("alarm.pageNoExpiration", absence);
            }
        }
        if (ProductIdentity.DevelopmentFeatures)
        {
            Row("alarm.testLevel", TestLevel);
            Row("alarm.testNumeric", TestNumeric);
        }
        Children.Add(NoticeColorEnabled);
        Children.Add(DesktopInformationPages.Help("topic-11"));
        Children.Add(DesktopInformationPages.Help("settings-detail-10"));
        if (ProductIdentity.DevelopmentFeatures) { Children.Add(DesktopInformationPages.Help("settings-detail-11")); }
        Children.Add(InfoTone);
        Row("alarm.infoInterval", InfoInterval); Row("alarm.noticeInterval", NoticeInterval);
        Row("alarm.warningInterval", WarningInterval); Row("alarm.criticalInterval", CriticalInterval);
        Children.Add(Text("alarm.intervalNote"));
        NotificationSettings = new(AlarmLifecycles, _localization);
        Children.Add(NotificationSettings);
        foreach (var (numeric, title) in Parameters)
        {
            var descriptor = MeasuredLimitNotice.Describe(numeric);
            string[] ids = numeric switch
            {
                MonitorNumeric.SpO2 => [descriptor.Id + "-low"],
                MonitorNumeric.EtCo2 => [descriptor.Id + "-low", descriptor.Id + "-high", "co2-no-expiration"],
                _ => [descriptor.Id + "-low", descriptor.Id + "-high"]
            };
            _soundPages[numeric] = ConfirmationFor(numeric).AddPage("alarm.pageSound", NotificationSettings.CreateEventPage(numeric, title, ids));
        }
        var ecgPage = new StackPanel { Spacing = 12 };
        ecgPage.Children.Add(EcgMonitoringEnabled);
        ecgPage.Children.Add(NotificationSettings.CreateEcgPage());
        ecgPage.Children.Add(DesktopInformationPages.Help("ecg-monitoring-alarms"));
        EcgMonitoringPage = HeartRateConfirmation.AddPage("alarm.ecgPage", ecgPage);
    }
    internal AlarmConfirmationEditor ConfirmationFor(MonitorNumeric numeric) => numeric switch
    {
        MonitorNumeric.HeartRate => HeartRateConfirmation,
        MonitorNumeric.SpO2 => SpO2Confirmation,
        _ => AdditionalLimits.Editors[numeric].Confirmation
    };
    internal IReadOnlyList<CheckBox> SwitchesFor(MonitorNumeric numeric) => numeric switch
    {
        MonitorNumeric.HeartRate => [HeartRateEnabled, EcgMonitoringEnabled],
        MonitorNumeric.SpO2 => [SpO2Enabled],
        MonitorNumeric.EtCo2 => [AdditionalLimits.Editors[numeric].Enabled, NoExpirationEnabled],
        _ => [AdditionalLimits.Editors[numeric].Enabled]
    };
    internal void ShowSoundPage(MonitorNumeric numeric) => ConfirmationFor(numeric).Groups.SelectedItem = _soundPages[numeric];
    internal MonitorAlarmPreferences CapturePreferences()
    {
        var notifications = NotificationSettings.Read();
        static int? Read(NumericUpDown field, int scale)
        {
            if (field.Value is null) { return null; }
            return DesignPreviewSettings.ReadVitalValue(field, scale, "alarm.thresholdField");
        }
        var result = new MonitorAlarmPreferences(
            new(HeartRateEnabled.IsChecked == true, Read(CriticalLowHeartRate, 1000), Read(WarningLowHeartRate, 1000), Read(WarningHeartRate, 1000), Read(CriticalHeartRate, 1000)),
            SpO2Enabled.IsChecked == true, Read(WarningSpO2, 1000), Read(CriticalSpO2, 1000),
            NoExpirationEnabled.IsChecked == true, Read(NoExpirationSeconds, 1),
            MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d =>
            {
                var editor = AdditionalLimits.Editors[d.Numeric];
                return new MeasurementLimits(editor.Enabled.IsChecked == true, Read(editor.CriticalLow, d.Divisor),
                    Read(editor.WarningLow, d.Divisor), Read(editor.WarningHigh, d.Divisor), Read(editor.CriticalHigh, d.Divisor));
            }), NoticeColorEnabled.IsChecked == true)
        {
            EcgMonitoringEnabled = EcgMonitoringEnabled.IsChecked == true,
            PlaybackMode = notifications.Mode,
            Notifications = notifications.Overrides,
            NoExpirationConfirmation = ReadNoExpirationTiming(),
            ConfirmationTimings = MeasuredLimitNotice.Descriptors
                .Select(d => (d.Numeric, Timing: AdditionalLimits.Editors[d.Numeric].Confirmation.Read()))
                .Concat(new[]
                {
                    (Numeric: MonitorNumeric.HeartRate, Timing: HeartRateConfirmation.Read()),
                    (Numeric: MonitorNumeric.SpO2, Timing: SpO2Confirmation.Read())
                })
                .Where(entry => entry.Timing != MeasurementConfirmationTiming.DefaultFor(entry.Numeric))
                .ToDictionary(entry => entry.Numeric, entry => entry.Timing)
        };
        result.Validate(); return result;
    }
    internal void RestorePreferences(MonitorAlarmPreferences preferences)
    {
        preferences.Validate();
        Reset(AlarmTransitionReason.ConfigurationChanged);
        HeartRateConfirmation.Restore(preferences.ConfirmationFor(MonitorNumeric.HeartRate));
        SpO2Confirmation.Restore(preferences.ConfirmationFor(MonitorNumeric.SpO2));
        HeartRateEnabled.IsChecked = preferences.HeartRate.Enabled;
        EcgMonitoringEnabled.IsChecked = preferences.EcgMonitoringEnabled;
        CriticalLowHeartRate.Value = preferences.HeartRate.CriticalLow / 1000m;
        WarningLowHeartRate.Value = preferences.HeartRate.WarningLow / 1000m;
        WarningHeartRate.Value = preferences.HeartRate.WarningHigh / 1000m;
        CriticalHeartRate.Value = preferences.HeartRate.CriticalHigh / 1000m;
        SpO2Enabled.IsChecked = preferences.SpO2Enabled;
        WarningSpO2.Value = preferences.SpO2Warning / 1000m; CriticalSpO2.Value = preferences.SpO2Critical / 1000m;
        NoExpirationEnabled.IsChecked = preferences.NoExpirationEnabled; NoExpirationSeconds.Value = preferences.NoExpirationSeconds;
        NoExpirationTriggerSeconds.Value = preferences.NoExpirationConfirmation.TriggerMilliseconds / 1000m;
        NoExpirationRecoverySeconds.Value = preferences.NoExpirationConfirmation.RecoveryMilliseconds / 1000m;
        NoticeColorEnabled.IsChecked = preferences.NoticeColorEnabled;
        foreach (var d in MeasuredLimitNotice.Descriptors)
        {
            var saved = preferences.Additional[d.Numeric]; var editor = AdditionalLimits.Editors[d.Numeric];
            editor.Confirmation.Restore(preferences.ConfirmationFor(d.Numeric));
            editor.Enabled.IsChecked = saved.Enabled;
            editor.CriticalLow.Value = saved.CriticalLow / (decimal)d.Divisor; editor.WarningLow.Value = saved.WarningLow / (decimal)d.Divisor;
            editor.WarningHigh.Value = saved.WarningHigh / (decimal)d.Divisor; editor.CriticalHigh.Value = saved.CriticalHigh / (decimal)d.Divisor;
        }
        NotificationSettings.Restore(preferences, AlarmLifecycles);
    }
    internal MonitorSoundTiming Timing => new(InfoTone.IsChecked == true, Milliseconds(InfoInterval), Milliseconds(NoticeInterval), Milliseconds(WarningInterval), Milliseconds(CriticalInterval));
    internal const string RetainedNoticePrefix = "retained:";
    internal IEnumerable<MonitorNotice> ProjectAttention(IEnumerable<MonitorNotice> notices)
    {
        var attention = AlarmLifecycles.SelectMany(j => j.Attention.Conditions).ToDictionary(c => c.ConditionId, StringComparer.Ordinal);
        foreach (var notice in notices)
        {
            if (attention.TryGetValue(notice.Id, out var state))
            {
                if (state.State is AlarmAttentionState.None or AlarmAttentionState.RecoveredUnacknowledged || state.Level != notice.Level) { continue; }
                yield return state.State == AlarmAttentionState.ActiveAcknowledged
                    ? notice with { Text = notice.Text + " · 已确认", Audible = false, Message = new("alarm.acknowledged", Message(notice)) } : notice;
            }
            else { yield return notice; }
        }
        foreach (var state in attention.Values.Where(c => c.State == AlarmAttentionState.RecoveredUnacknowledged))
        {
            if (EcgAlarmNotices.Descriptors.SingleOrDefault(d => d.Id == state.ConditionId) is { } ecg)
            {
                yield return new(RetainedNoticePrefix + state.ConditionId, state.Level!.Value, ecg.Text + " · 已恢复，待确认")
                { Audible = false, Message = new("alarm.recoveredUnacknowledged", ecg.Message) };
                continue;
            }
            bool low = state.ConditionId.EndsWith("-low", StringComparison.Ordinal);
            var descriptor = state.ConditionId == "co2-no-expiration" ? null :
                MeasuredLimitNotice.Descriptors.Prepend(MeasuredLimitNotice.HeartRateDescriptor).Prepend(MeasuredLimitNotice.SpO2Descriptor)
                    .Single(d => state.ConditionId == d.Id + "-low" || state.ConditionId == d.Id + "-high");
            string label = descriptor is null ? "CO₂ · 未检出呼吸" : descriptor.Label + (low ? " · 低限" : " · 高限");
            var labelMessage = descriptor is null ? new TextMessage("alarm.noExpirationLabel") : AlarmText.ConditionLabel(descriptor, low);
            yield return new(RetainedNoticePrefix + state.ConditionId, state.Level!.Value, label + " · 已恢复，待确认")
            { Audible = false, Message = new("alarm.recoveredUnacknowledged", labelMessage) };
        }
    }

    internal IEnumerable<MonitorNotice> Notices(LiveMeasurementSnapshot snapshot) => Notices(snapshot, null, null);

    internal IEnumerable<MonitorNotice> Notices(LiveMeasurementSnapshot snapshot,
        IReadOnlyList<DetectedEcgMonitoringEvent>? monitoringEvents = null, IReadOnlyList<DetectedEcgRhythmEvent>? rhythmEvents = null)
    {
        foreach (var notice in _ecgNotices.Evaluate(Monitors(MonitorNumeric.HeartRate) && EcgMonitoringEnabled.IsChecked == true, snapshot, monitoringEvents, rhythmEvents))
        { yield return notice; }
        if (EvaluatePrimary(MonitorNumeric.HeartRate, snapshot) is { } heartRate) { yield return heartRate; }
        if (EvaluatePrimary(MonitorNumeric.SpO2, snapshot) is { } saturation) { yield return saturation; }
        foreach (var notice in AdditionalLimits.Notices(snapshot)) { yield return notice; }
        if (EvaluateNoExpiration(snapshot) is { } absence)
        { yield return absence; }
        if (ProductIdentity.DevelopmentFeatures && TestLevel.SelectedIndex > 0)
        {
            var level = (MonitorNoticeLevel)(TestLevel.SelectedIndex - 1);
            yield return new("explicit-test", level, "测试提示 · " + level)
            { Numeric = TestNumeric.SelectedIndex > 0 ? (MonitorNumeric)(TestNumeric.SelectedIndex - 1) : null, Message = new("alarm.testNoticeText", level.ToString()) };
        }
    }
    internal void Reset(AlarmTransitionReason reason = AlarmTransitionReason.SessionReset)
    {
        _ecgNotices.Reset(reason);
        _heartRateNotice.Reset(reason);
        _spO2Notice.Reset(reason);
        _noExpirationNotice.Reset(reason);
        AdditionalLimits.Reset(reason);
    }

    private BoundaryConfirmationTiming ReadNoExpirationTiming() => new(
        AlarmConfirmationEditor.ReadMilliseconds(NoExpirationTriggerSeconds),
        AlarmConfirmationEditor.ReadMilliseconds(NoExpirationRecoverySeconds));

    private int? ReadNoExpirationDelay() => NoExpirationSeconds.Value is { } seconds &&
        seconds >= 5 && seconds <= 120 && seconds == decimal.Truncate(seconds) ? (int)seconds : null;

    private void NoExpirationEdited()
    {
        try
        {
            _ = ReadNoExpirationTiming();
            if (NoExpirationEnabled.IsChecked == true && ReadNoExpirationDelay() is null)
            { throw new ArgumentException("AlarmNoExpiration.InvalidDelay"); }
            _noExpirationNotice.Reset(NoExpirationEnabled.IsChecked == true
                ? AlarmTransitionReason.ConfigurationChanged : AlarmTransitionReason.Disabled);
            _noExpirationErrorKey = null;
            _noExpirationError.Text = null;
            _noExpirationError.IsVisible = false;
        }
        catch (ArgumentException error)
        {
            _noExpirationNotice.Reset(AlarmTransitionReason.InvalidConfiguration);
            _noExpirationErrorKey = error.Message == "AlarmNoExpiration.InvalidDelay" ? "alarm.noExpirationWaitInvalid" : "alarm.confirmationTimingInvalid";
            _noExpirationError.Text = _localization.Get(_noExpirationErrorKey);
            _noExpirationError.IsVisible = true;
        }
    }

    private MonitorNotice? EvaluateNoExpiration(LiveMeasurementSnapshot snapshot)
    {
        if (!Monitors(MonitorNumeric.EtCo2)) { _noExpirationNotice.Reset(AlarmTransitionReason.Disabled); return null; }
        try
        {
            return _noExpirationNotice.Evaluate(NoExpirationEnabled.IsChecked == true, ReadNoExpirationDelay(),
                snapshot.SampleTimeNs, snapshot.Capnography.Activity, ReadNoExpirationTiming());
        }
        catch (ArgumentException)
        {
            _noExpirationNotice.Reset(AlarmTransitionReason.InvalidConfiguration);
            return new("co2-absence-settings", MonitorNoticeLevel.Info, "CO₂ 未检出呼吸设置无效：确认时间须为 0–600 秒，最多三位小数")
            { Audible = false, Message = new("alarm.noExpirationTimingInvalid") };
        }
    }

    private MonitorNotice? EvaluatePrimary(MonitorNumeric numeric, LiveMeasurementSnapshot snapshot)
    {
        bool heartRate = numeric == MonitorNumeric.HeartRate;
        var filter = heartRate ? _heartRateNotice : _spO2Notice;
        var editor = heartRate ? HeartRateConfirmation : SpO2Confirmation;
        if (!Monitors(numeric)) { filter.Reset(AlarmTransitionReason.Disabled); return null; }
        MeasurementConfirmationTiming timing;
        MeasurementLimits limits;
        try
        {
            timing = editor.Read();
            limits = heartRate
                ? new(HeartRateEnabled.IsChecked == true, MilliUnits(CriticalLowHeartRate), MilliUnits(WarningLowHeartRate),
                    MilliUnits(WarningHeartRate), MilliUnits(CriticalHeartRate))
                : new(SpO2Enabled.IsChecked == true, MilliUnits(CriticalSpO2), MilliUnits(WarningSpO2), null, null);
        }
        catch (ArgumentException)
        {
            filter.Reset(AlarmTransitionReason.InvalidConfiguration);
            var descriptor = MeasuredLimitNotice.Describe(numeric);
            return new(descriptor.Id + "-settings", MonitorNoticeLevel.Info, descriptor.Label + " 提示设置无效：请检查阈值范围和精度，以及确认时间（0–600 秒，最多三位小数）")
            { Message = new("alarm.primarySettingsInvalid", descriptor.LabelMessage) };
        }
        return filter.Evaluate(limits, snapshot, timing);
    }

    private static int? MilliUnits(NumericUpDown field) => field.Value is null
        ? null : DesignPreviewSettings.ReadVitalValue(field, 1000, "alarm.thresholdField");
    private static int Milliseconds(NumericUpDown number) => checked((int)((number.Value ?? number.Minimum) * 1000));
    // A message for notices whose producer did not attach one keeps its default text.
    private static TextMessage Message(MonitorNotice notice) => notice.Message ?? new("common.verbatim", notice.Text);
    private void Row(string key, Control control, Panel? owner = null)
    {
        var row = new StackPanel { Spacing = 4 };
        if (owner is WrapPanel) { row.Width = 220; row.Margin = new Thickness(0, 0, 24, 12); }
        row.Children.Add(Text(key)); row.Children.Add(control); (owner ?? this).Children.Add(row);
        _localization.Bind(control, AutomationProperties.NameProperty, key);
    }
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum) => new()
    { Value = value, Minimum = minimum, Maximum = maximum, Increment = .25m, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private TextBlock Text(string key)
    {
        var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _localization.Bind(text, TextBlock.TextProperty, key);
        return text;
    }
}
