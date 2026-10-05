// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;

namespace Monitor.Desktop;

internal sealed class SoundSettingsPanel : StackPanel
{
    private readonly DesktopLocalization _localization;
    private readonly Func<int, CancellationToken, Task<SoundPreviewResult>> _play;
    private SoundPreviewResult? _outputResult;
    private CancellationTokenSource? _cancellation;
    private bool _closed;
    private readonly MonitorAlarmPlayback _alarms = new(() => new NativeAudioOutputFactory(NativeAudioOutputFactory.DefaultLibraryPath));
    private CancellationTokenSource? _alarmCancellation;
    private MonitorNoticeLevel? _alarmLevel;
    private readonly AlarmNotificationSoundRouter _notificationRouter = new();
    private IReadOnlyList<AlarmLifecycleJournal>? _notificationSources;
    private IReadOnlyList<MonitorNotice> _activeNotices = [];
    private MonitorSoundTiming _timing = new();
    private bool _monitorRunning;
    private readonly MonitorBeatSource _source = new();
    private LiveMeasurementSnapshot? _sourceMeasurement;
    private readonly TextBlock _sourceStatus = Text();
    private readonly TextBlock _sourceHistory = Text();
    internal event Action? BeatSourceChanged;
    internal string BeatSourceLabel => _localization.Format("sound.beatSourceLabel", OriginName(_source.Current), AutoSuffix);
    private string AutoSuffix => BeatSource.SelectedIndex == 2 ? _localization.Get("sound.autoSuffix") : "";
    private string OriginName(MonitorBeatOrigin source) => source switch
    {
        MonitorBeatOrigin.Ecg => "ECG",
        MonitorBeatOrigin.Pleth => "PLETH",
        _ => _localization.Get("sound.originWaiting"),
    };
    private readonly MonitorBeatPitch _pitch = new();
    internal int BeatPitchPercent => PitchSource.SelectedIndex == 1 ? _pitch.SaturationPercent : 97;
    internal MonitorNotice? PitchNotice => AlarmEnabled.IsChecked == true && HeartbeatEnabled.IsChecked == true && PitchSource.SelectedIndex == 1 && _pitch.Unavailable
        ? new("beat-pitch-unavailable", MonitorNoticeLevel.Info, _localization.Get("sound.pitchUnavailable")) { Audible = false } : null;
    internal ComboBox PitchSource { get; } = new() { SelectedIndex = 1, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly MonitorAudioPause _audioPause = new();
    private readonly Func<long> _authorityNow;
    private readonly DispatcherTimer _pauseTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    internal event Action? AudioPauseChanged;
    internal string AudioPauseText { get; private set; } = "";
    internal MonitorAlarmSoundRequest? PublishedAlarm { get; private set; }
    internal NumericUpDown PauseSeconds { get; } = new() { Minimum = 1, Maximum = 3600, Value = 120, Increment = 1, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal Button PauseAlarmAudio { get; } = Button();
    internal Button ResumeAlarmAudio { get; } = Button();
    internal TextBlock PauseStatus { get; } = Text();
    internal MonitorNotice? OutputNotice { get; private set; }
    internal event Action? OutputNoticeChanged;
    internal CheckBox AlarmEnabled { get; } = new() { IsChecked = false };
    internal CheckBox HeartbeatEnabled { get; } = new() { IsChecked = true };
    internal ComboBox BeatSource { get; } = new() { SelectedIndex = 0, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
    internal Slider Volume { get; } = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
    internal Slider HeartbeatVolume { get; } = new() { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
    internal int EffectiveHeartbeatVolume => (int)(Volume.Value * HeartbeatVolume.Value / 100);
    internal Button Audition { get; } = Button();
    internal Button Stop { get; } = Button();
    internal TextBlock Status { get; } = Text();

    internal SoundSettingsPanel(Func<int, CancellationToken, Task<SoundPreviewResult>>? play = null, Func<long>? authorityNow = null,
        DesktopLocalization? localization = null)
    {
        _localization = localization ?? new DesktopLocalization();
        Show(Status, "sound.statusIdle");
        _localization.Bind(Audition, ContentControl.ContentProperty, "sound.audition");
        _localization.Bind(Stop, ContentControl.ContentProperty, "sound.stopAudition");
        _localization.Bind(AlarmEnabled, ContentControl.ContentProperty, "sound.alarmEnabled");
        _localization.Bind(HeartbeatEnabled, ContentControl.ContentProperty, "sound.heartbeatEnabled");
        _localization.Bind(PauseAlarmAudio, ContentControl.ContentProperty, "sound.pause");
        _localization.Bind(ResumeAlarmAudio, ContentControl.ContentProperty, "sound.resume");
        _localization.SetChoices(BeatSource, "sound.beatSourceEcg", "sound.beatSourcePleth", "sound.beatSourceAuto");
        _localization.SetChoices(PitchSource, "sound.pitchFixed", "sound.pitchSpO2");
        // Text computed outside bindings (source labels, notices) follows the selected language.
        _localization.LocaleChanged += () =>
        {
            RefreshSourceText();
            if (_outputResult is { } result) { RecordOutputResult(result); }
            Publish();
        };
        long origin = Stopwatch.GetTimestamp();
        _authorityNow = authorityNow ?? (() => checked(Stopwatch.GetElapsedTime(origin).Ticks * 100));
        _pauseTimer.Tick += (_, _) => RefreshAudioPause();
        var playback = new SoundPreviewPlayback(() => new NativeAudioOutputFactory(NativeAudioOutputFactory.DefaultLibraryPath));
        _play = play ?? playback.PlayAsync;
        Margin = new Thickness(20); Spacing = 16;
        Children.Add(Label("sound.output"));
        Children.Add(DesktopInformationPages.Help("settings-detail-7"));
        var volumeLabel = Label("sound.volume", 50); Children.Add(volumeLabel); Children.Add(Volume);
        _localization.Bind(Volume, AutomationProperties.NameProperty, "sound.volumeName");
        Volume.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
            {
                Show(volumeLabel, "sound.volume", Volume.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                if (_notificationSources is not null) { Publish(); }
            }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(Audition); buttons.Children.Add(Stop); Children.Add(buttons);
        Stop.IsEnabled = false; Children.Add(Status);
        Children.Add(AlarmEnabled);
        Children.Add(HeartbeatEnabled);
        var beatVolumeLabel = Label("sound.heartbeatVolume", 100); Children.Add(beatVolumeLabel); Children.Add(HeartbeatVolume);
        _localization.Bind(HeartbeatVolume, AutomationProperties.NameProperty, "sound.heartbeatVolumeName");
        HeartbeatVolume.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
            { Show(beatVolumeLabel, "sound.heartbeatVolume", HeartbeatVolume.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)); }
        };
        Children.Add(DesktopInformationPages.Help("settings-detail-8"));
        Children.Add(Label("sound.beatSource")); Children.Add(BeatSource);
        _localization.Bind(BeatSource, AutomationProperties.NameProperty, "sound.beatSourceName");
        BeatSource.SelectionChanged += (_, _) => { _alarms.SetHeartbeatEnabled(false); UpdateBeatSource(); Publish(); };
        Children.Add(_sourceStatus);
        var history = new Expander { Content = _sourceHistory };
        _localization.Bind(history, Expander.HeaderProperty, "sound.history");
        Children.Add(history);
        Children.Add(DesktopInformationPages.Help("settings-detail-9"));
        Children.Add(Label("sound.pitchSource")); Children.Add(PitchSource);
        _localization.Bind(PitchSource, AutomationProperties.NameProperty, "sound.pitchSourceName");
        PitchSource.SelectionChanged += (_, _) => { ResetPitchState(); OutputNoticeChanged?.Invoke(); };
        Children.Add(Label("sound.pauseDuration")); Children.Add(PauseSeconds);
        _localization.Bind(PauseSeconds, AutomationProperties.NameProperty, "sound.pauseDurationName");
        var pauseButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pauseButtons.Children.Add(PauseAlarmAudio); pauseButtons.Children.Add(ResumeAlarmAudio); Children.Add(pauseButtons);
        ResumeAlarmAudio.IsEnabled = false; Children.Add(PauseStatus);
        Children.Add(DesktopInformationPages.Help("topic-9"));
        PauseAlarmAudio.Click += (_, _) =>
        {
            if (PauseSeconds.Value is not { } seconds || seconds != decimal.Truncate(seconds))
            { Show(PauseStatus, "sound.pauseInvalid"); return; }
            StartAudioPause(checked((int)seconds));
        };
        ResumeAlarmAudio.Click += (_, _) => { _audioPause.Resume(_authorityNow()); RefreshAudioPause(); };
        Children.Add(DesktopInformationPages.Help("topic-10"));
        HeartbeatEnabled.IsCheckedChanged += (_, _) => Publish();
        AlarmEnabled.IsCheckedChanged += async (_, _) =>
        {
            if (AlarmEnabled.IsChecked == true) { await RunAlarmsAsync(); }
            else { _alarms.SetHeartbeatEnabled(false); _alarmCancellation?.Cancel(); }
        };
        Audition.Click += async (_, _) => await PreviewAsync();
        Stop.Click += (_, _) => StopPreview();
        UpdateBeatSource();
    }

    internal MonitorSoundPreferences CapturePreferences(MonitorAlertSettings alerts)
    {
        static int Percent(double value)
        {
            if (!double.IsFinite(value) || value != Math.Truncate(value) || value is < 0 or > 100)
            { throw new ArgumentException("SoundPreferences.Invalid"); }
            return (int)value;
        }
        var timing = new MonitorSoundTiming(alerts.InfoTone.IsChecked == true,
            DesignPreviewSettings.ReadVitalValue(alerts.InfoInterval, 1000, "sound.infoIntervalField"),
            DesignPreviewSettings.ReadVitalValue(alerts.NoticeInterval, 1000, "sound.noticeIntervalField"),
            DesignPreviewSettings.ReadVitalValue(alerts.WarningInterval, 1000, "sound.warningIntervalField"),
            DesignPreviewSettings.ReadVitalValue(alerts.CriticalInterval, 1000, "sound.criticalIntervalField"));
        var result = new MonitorSoundPreferences(Percent(Volume.Value), Percent(HeartbeatVolume.Value),
            HeartbeatEnabled.IsChecked == true, BeatSource.SelectedIndex, PitchSource.SelectedIndex,
            DesignPreviewSettings.ReadVitalValue(PauseSeconds, 1, "sound.pauseField"), timing);
        result.Validate(); return result;
    }
    internal void RestorePreferences(MonitorSoundPreferences preferences, MonitorAlertSettings alerts)
    {
        preferences.Validate();
        Volume.Value = preferences.Volume; HeartbeatVolume.Value = preferences.HeartbeatVolume;
        HeartbeatEnabled.IsChecked = preferences.HeartbeatEnabled;
        BeatSource.SelectedIndex = preferences.BeatSource; PitchSource.SelectedIndex = preferences.PitchSource;
        PauseSeconds.Value = preferences.PauseSeconds;
        alerts.InfoTone.IsChecked = preferences.Timing.InfoTone;
        alerts.InfoInterval.Value = preferences.Timing.InfoMilliseconds / 1000m;
        alerts.NoticeInterval.Value = preferences.Timing.NoticeMilliseconds / 1000m;
        alerts.WarningInterval.Value = preferences.Timing.WarningMilliseconds / 1000m;
        alerts.CriticalInterval.Value = preferences.Timing.CriticalMilliseconds / 1000m;
    }

    internal async Task PreviewAsync()
    {
        if (_closed || _cancellation is not null || !Audition.IsEnabled) { return; }
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        Audition.IsEnabled = false; Volume.IsEnabled = false; AlarmEnabled.IsEnabled = false; Stop.IsEnabled = true;
        Show(Status, "sound.previewing");
        var result = await _play((int)Volume.Value, cancellation.Token);
        _cancellation = null;
        if (_closed) { return; }
        RecordOutputResult(result);
        Volume.IsEnabled = true; Stop.IsEnabled = false;
        Audition.IsEnabled = result != SoundPreviewResult.StopFailed;
        AlarmEnabled.IsEnabled = result != SoundPreviewResult.StopFailed;
        Show(Status, result switch
        {
            SoundPreviewResult.Completed => "sound.previewDone",
            SoundPreviewResult.Stopped => "sound.previewStopped",
            SoundPreviewResult.Interrupted => "sound.previewInterrupted",
            SoundPreviewResult.StopFailed => "sound.stopFailed",
            _ => "sound.unavailable",
        });
    }

    internal void StopPreview()
    {
        _cancellation?.Cancel(); Stop.IsEnabled = false;
        if (_cancellation is not null) { Show(Status, "sound.stopping"); }
    }
    internal void UseNotificationPlayback(IReadOnlyList<AlarmLifecycleJournal>? journals)
    {
        if (journals is not null && _notificationSources is not null && journals.SequenceEqual(_notificationSources)) { return; }
        if (journals is not null)
        { _notificationRouter.Update(journals, (int)Volume.Value, _timing, enabled: false); }
        else if (_notificationSources is not null)
        { _notificationRouter.Update(_notificationSources, (int)Volume.Value, _timing, enabled: false); }
        _notificationSources = journals is null ? null : Array.AsReadOnly(journals.ToArray());
        Publish();
    }

    internal void UpdateAlarm(MonitorNoticeLevel? level, MonitorSoundTiming timing, IReadOnlyList<DetectedEcgBeat>? beats = null,
        IReadOnlyList<DetectedPlethPulse>? pulses = null, LiveMeasurementSnapshot? measurement = null, IReadOnlyList<MonitorNotice>? notices = null)
    {
        if (measurement is not null) { _sourceMeasurement = measurement; }
        UpdateBeatSource();
        if (PitchSource.SelectedIndex == 1) { _pitch.Update(measurement?.SpO2, measurement?.SampleTimeNs ?? 0); }
        if (_alarms.OutputActive) { SetOutputNotice(null); }
        _monitorRunning = true; _alarmLevel = level; _timing = timing;
        _activeNotices = notices is null ? [] : Array.AsReadOnly(notices.ToArray());
        Publish();
        // Transfer only fresh events from the one selected source.
        long? confirmed = _source.Current switch
        {
            MonitorBeatOrigin.Ecg when beats is { Count: > 0 } => beats[^1].ConfirmedAtNs,
            MonitorBeatOrigin.Pleth when pulses is { Count: > 0 } => pulses[^1].ConfirmedAtNs,
            _ => null
        };
        if (AlarmEnabled.IsChecked == true && HeartbeatEnabled.IsChecked == true && confirmed is { } at && _source.Accept(_source.Current, at))
        { _alarms.SubmitHeartbeat(EffectiveHeartbeatVolume, BeatPitchPercent); }
    }
    internal void RefreshAlarmNotices(IReadOnlyList<MonitorNotice> notices)
    {
        _activeNotices = Array.AsReadOnly(notices.ToArray());
        _alarmLevel = notices.Where(n => n.Audible).Select(n => (MonitorNoticeLevel?)n.Level).Max();
        Publish();
    }
    private void UpdateBeatSource()
    {
        if (BeatSource.SelectedIndex is < 0 or > 2) { return; }
        if (!_source.Update((MonitorBeatMode)BeatSource.SelectedIndex,
            _sourceMeasurement?.HeartRate?.Status ?? WaveformMeasurementStatus.NoData,
            _sourceMeasurement?.PulseRate?.Status ?? WaveformMeasurementStatus.NoData, _sourceMeasurement?.SampleTimeNs ?? 0)) { return; }
        _alarms.SetHeartbeatEnabled(false);
        RefreshSourceText();
        BeatSourceChanged?.Invoke();
    }
    private void RefreshSourceText()
    {
        _sourceStatus.Text = _localization.Format("sound.currentBeatSource", OriginName(_source.Current), AutoSuffix);
        _sourceHistory.Text = string.Join("\n", _source.Changes.Select(c => _localization.Format("sound.historyEntry", c.TimeNs / 1_000_000_000,
            _localization.Get(c.Mode == MonitorBeatMode.Auto ? "sound.modeAuto" : "sound.modeManual"), OriginName(c.From), OriginName(c.To))));
    }
    internal void ResetBeatSource() { _source.Reset(); _sourceMeasurement = null; UpdateBeatSource(); }
    internal void ResetPitchState() { _pitch.Reset(); _alarms.SetHeartbeatEnabled(false); Publish(); }
    internal void PauseMonitor()
    {
        _monitorRunning = false; Publish();
    }
    internal void StartAudioPause(int seconds)
    {
        if (_closed) { return; }
        _audioPause.Start(_authorityNow(), seconds); _pauseTimer.Start(); RefreshAudioPause();
    }
    internal void RefreshAudioPause() => Publish();
    private void Publish()
    {
        int remaining = _audioPause.RemainingSeconds(_authorityNow());
        if (remaining == 0) { _pauseTimer.Stop(); }
        string text = remaining == 0 ? "" : _localization.Format("sound.pauseRemaining", remaining);
        PauseStatus.Text = remaining == 0 ? _localization.Get("sound.pauseIdle") : text;
        ResumeAlarmAudio.IsEnabled = remaining > 0;
        if (text != AudioPauseText) { AudioPauseText = text; AudioPauseChanged?.Invoke(); }
        bool enabled = _monitorRunning && remaining == 0;
        PublishedAlarm = _notificationSources is null
            ? enabled && _alarmLevel is { } active ? new(active, (int)Volume.Value, _timing) : null
            : _notificationRouter.Update(_notificationSources, (int)Volume.Value, _timing, enabled, _activeNotices, _alarms.RetiredNotificationSequence);
        _alarms.SetRequest(PublishedAlarm);
        _alarms.SetHeartbeatEnabled(_monitorRunning && AlarmEnabled.IsChecked == true && HeartbeatEnabled.IsChecked == true);
    }
    private async Task RunAlarmsAsync()
    {
        if (_closed || _alarmCancellation is not null || _cancellation is not null) { return; }
        using var cancellation = new CancellationTokenSource(); _alarmCancellation = cancellation;
        Audition.IsEnabled = false; Publish(); Show(Status, "sound.alarmsOn");
        var result = await _alarms.RunAsync(cancellation.Token);
        _alarmCancellation = null;
        if (_closed) { return; }
        RecordOutputResult(result);
        AlarmEnabled.IsChecked = false;
        AlarmEnabled.IsEnabled = Audition.IsEnabled = result != SoundPreviewResult.StopFailed;
        Show(Status, result switch
        {
            SoundPreviewResult.Stopped => "sound.alarmsOff",
            SoundPreviewResult.StopFailed => "sound.stopFailed",
            _ => "sound.alarmsUnavailable",
        });
    }
    private void RecordOutputResult(SoundPreviewResult result)
    {
        if (result == SoundPreviewResult.Completed)
        {
            _outputResult = null;
            SetOutputNotice(null);
        }
        else if (result is SoundPreviewResult.Unavailable or SoundPreviewResult.Interrupted or SoundPreviewResult.StopFailed)
        {
            _outputResult = result;
            string text = _localization.Get(result switch
            {
                SoundPreviewResult.Interrupted => "sound.noticeInterrupted",
                SoundPreviewResult.StopFailed => "sound.noticeStopFailed",
                _ => "sound.noticeUnavailable",
            });
            SetOutputNotice(new("audio-output", MonitorNoticeLevel.Notice, text) { Audible = false });
        }
        // A cancelled attempt does not prove that output has recovered.
    }
    private void SetOutputNotice(MonitorNotice? notice)
    {
        if (OutputNotice == notice) { return; }
        OutputNotice = notice; OutputNoticeChanged?.Invoke();
    }
    internal void Close() { _closed = true; _pauseTimer.Stop(); StopPreview(); _alarmCancellation?.Cancel(); }
    private static Button Button() => new()
    {
        MinHeight = 44,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private static TextBlock Text() => new() { TextWrapping = TextWrapping.Wrap };
    private TextBlock Label(string key, params object?[] arguments)
    {
        var text = Text();
        Show(text, key, arguments);
        return text;
    }
    private void Show(TextBlock target, string key, params object?[] arguments) =>
        _localization.Bind(target, TextBlock.TextProperty, key, arguments);
}
