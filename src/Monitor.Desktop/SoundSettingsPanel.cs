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
    private readonly Func<int, CancellationToken, Task<SoundPreviewResult>> _play;
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
    private readonly TextBlock _sourceStatus = Text("当前心搏音源：ECG");
    private readonly TextBlock _sourceHistory = Text("");
    internal event Action? BeatSourceChanged;
    internal string BeatSourceLabel => $"心搏音源：{OriginName(_source.Current)}{(BeatSource.SelectedIndex == 2 ? " · 自动" : "")}";
    private static string OriginName(MonitorBeatOrigin source) => source switch { MonitorBeatOrigin.Ecg => "ECG", MonitorBeatOrigin.Pleth => "PLETH", _ => "等待有效信号" };
    private readonly MonitorBeatPitch _pitch = new();
    internal int BeatPitchPercent => PitchSource.SelectedIndex == 1 ? _pitch.SaturationPercent : 97;
    internal MonitorNotice? PitchNotice => AlarmEnabled.IsChecked == true && HeartbeatEnabled.IsChecked == true && PitchSource.SelectedIndex == 1 && _pitch.Unavailable
        ? new("beat-pitch-unavailable", MonitorNoticeLevel.Info, "SpO₂ 音高不可用 · 固定音高") { Audible = false } : null;
    internal ComboBox PitchSource { get; } = new() { ItemsSource = new[] { "固定音高", "SpO₂ · A 曲线" }, SelectedIndex = 1, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly MonitorAudioPause _audioPause = new();
    private readonly Func<long> _authorityNow;
    private readonly DispatcherTimer _pauseTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    internal event Action? AudioPauseChanged;
    internal string AudioPauseText { get; private set; } = "";
    internal MonitorAlarmSoundRequest? PublishedAlarm { get; private set; }
    internal NumericUpDown PauseSeconds { get; } = new() { Minimum = 1, Maximum = 3600, Value = 120, Increment = 1, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal Button PauseAlarmAudio { get; } = Button("暂停报警声音");
    internal Button ResumeAlarmAudio { get; } = Button("立即恢复报警声音");
    internal TextBlock PauseStatus { get; } = Text("报警声音未定时暂停");
    internal MonitorNotice? OutputNotice { get; private set; }
    internal event Action? OutputNoticeChanged;
    internal CheckBox AlarmEnabled { get; } = new() { Content = "启用监护提示声音", IsChecked = false };
    internal CheckBox HeartbeatEnabled { get; } = new() { Content = "心搏提示音（与报警声独立重叠）", IsChecked = true };
    internal ComboBox BeatSource { get; } = new() { ItemsSource = new[] { "ECG · 已检测 QRS", "PLETH · 已检测脉搏", "自动 · ECG 优先" }, SelectedIndex = 0, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
    internal Slider Volume { get; } = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
    internal Slider HeartbeatVolume { get; } = new() { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
    internal int EffectiveHeartbeatVolume => (int)(Volume.Value * HeartbeatVolume.Value / 100);
    internal Button Audition { get; } = Button("试听三声");
    internal Button Stop { get; } = Button("停止试听");
    internal TextBlock Status { get; } = Text("未播放");

    internal SoundSettingsPanel(Func<int, CancellationToken, Task<SoundPreviewResult>>? play = null, Func<long>? authorityNow = null)
    {
        long origin = Stopwatch.GetTimestamp();
        _authorityNow = authorityNow ?? (() => checked(Stopwatch.GetElapsedTime(origin).Ticks * 100));
        _pauseTimer.Tick += (_, _) => RefreshAudioPause();
        var playback = new SoundPreviewPlayback(() => new NativeAudioOutputFactory(NativeAudioOutputFactory.DefaultLibraryPath));
        _play = play ?? playback.PlayAsync;
        Margin = new Thickness(20); Spacing = 16;
        Children.Add(Text("声音输出"));
        Children.Add(DesktopInformationPages.Help("settings-detail-7"));
        var volumeLabel = Text("声音音量：50%"); Children.Add(volumeLabel); Children.Add(Volume);
        AutomationProperties.SetName(Volume, "声音音量，百分比");
        Volume.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
            {
                volumeLabel.Text = $"声音音量：{Volume.Value:0}%";
                if (_notificationSources is not null) { Publish(); }
            }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(Audition); buttons.Children.Add(Stop); Children.Add(buttons);
        Stop.IsEnabled = false; Children.Add(Status);
        Children.Add(AlarmEnabled);
        Children.Add(HeartbeatEnabled);
        var beatVolumeLabel = Text("心搏相对音量：100%"); Children.Add(beatVolumeLabel); Children.Add(HeartbeatVolume);
        AutomationProperties.SetName(HeartbeatVolume, "心搏相对音量，百分比");
        HeartbeatVolume.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty) { beatVolumeLabel.Text = $"心搏相对音量：{HeartbeatVolume.Value:0}%"; }
        };
        Children.Add(DesktopInformationPages.Help("settings-detail-8"));
        Children.Add(Text("心搏提示音来源")); Children.Add(BeatSource);
        AutomationProperties.SetName(BeatSource, "心搏提示音来源，ECG、PLETH 或自动");
        BeatSource.SelectionChanged += (_, _) => { _alarms.SetHeartbeatEnabled(false); UpdateBeatSource(); Publish(); };
        Children.Add(_sourceStatus);
        Children.Add(new Expander { Header = "本次模拟音源切换记录（最近 64 条）", Content = _sourceHistory });
        Children.Add(DesktopInformationPages.Help("settings-detail-9"));
        Children.Add(Text("心搏音高来源")); Children.Add(PitchSource);
        AutomationProperties.SetName(PitchSource, "心搏音高来源，固定或 SpO₂ A 曲线");
        PitchSource.SelectionChanged += (_, _) => { ResetPitchState(); OutputNoticeChanged?.Invoke(); };
        Children.Add(Text("报警声音暂停时长（秒，1–3600）")); Children.Add(PauseSeconds);
        AutomationProperties.SetName(PauseSeconds, "报警声音暂停时长，秒");
        var pauseButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pauseButtons.Children.Add(PauseAlarmAudio); pauseButtons.Children.Add(ResumeAlarmAudio); Children.Add(pauseButtons);
        ResumeAlarmAudio.IsEnabled = false; Children.Add(PauseStatus);
        Children.Add(DesktopInformationPages.Help("topic-9"));
        PauseAlarmAudio.Click += (_, _) =>
        {
            if (PauseSeconds.Value is not { } seconds || seconds != decimal.Truncate(seconds))
            { PauseStatus.Text = "请输入 1–3600 的整数秒数；原声音状态保持不变。"; return; }
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
            DesignPreviewSettings.ReadVitalValue(alerts.InfoInterval, 1000, "Info 声音间隔"),
            DesignPreviewSettings.ReadVitalValue(alerts.NoticeInterval, 1000, "Notice 声音间隔"),
            DesignPreviewSettings.ReadVitalValue(alerts.WarningInterval, 1000, "Warning 声音间隔"),
            DesignPreviewSettings.ReadVitalValue(alerts.CriticalInterval, 1000, "Critical 声音间隔"));
        var result = new MonitorSoundPreferences(Percent(Volume.Value), Percent(HeartbeatVolume.Value),
            HeartbeatEnabled.IsChecked == true, BeatSource.SelectedIndex, PitchSource.SelectedIndex,
            DesignPreviewSettings.ReadVitalValue(PauseSeconds, 1, "声音暂停时长"), timing);
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
        Status.Text = "正在试听…";
        var result = await _play((int)Volume.Value, cancellation.Token);
        _cancellation = null;
        if (_closed) { return; }
        RecordOutputResult(result);
        Volume.IsEnabled = true; Stop.IsEnabled = false;
        Audition.IsEnabled = result != SoundPreviewResult.StopFailed;
        AlarmEnabled.IsEnabled = result != SoundPreviewResult.StopFailed;
        Status.Text = result switch
        {
            SoundPreviewResult.Completed => "试听已结束",
            SoundPreviewResult.Stopped => "试听已停止",
            SoundPreviewResult.Interrupted => "音频输出中断。请检查设备后重新试听。",
            SoundPreviewResult.StopFailed => "音频设备未能释放，请关闭并重新启动客户端。",
            _ => "声音不可用。请确认已安装 Windows 音频组件且默认输出设备可用。"
        };
    }

    internal void StopPreview()
    {
        _cancellation?.Cancel(); Stop.IsEnabled = false;
        if (_cancellation is not null) { Status.Text = "正在停止…"; }
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
        _sourceStatus.Text = "当前" + BeatSourceLabel;
        _sourceHistory.Text = string.Join("\n", _source.Changes.Select(c => $"{c.TimeNs / 1_000_000_000}s · {(c.Mode == MonitorBeatMode.Auto ? "自动" : "手动")} · {OriginName(c.From)} → {OriginName(c.To)}"));
        BeatSourceChanged?.Invoke();
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
        string text = remaining == 0 ? "" : $"报警声音暂停 · {remaining}s";
        PauseStatus.Text = remaining == 0 ? "报警声音未定时暂停" : text;
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
        Audition.IsEnabled = false; Publish(); Status.Text = "监护提示声音已启用";
        var result = await _alarms.RunAsync(cancellation.Token);
        _alarmCancellation = null;
        if (_closed) { return; }
        RecordOutputResult(result);
        AlarmEnabled.IsChecked = false;
        AlarmEnabled.IsEnabled = Audition.IsEnabled = result != SoundPreviewResult.StopFailed;
        Status.Text = result switch
        {
            SoundPreviewResult.Stopped => "监护提示声音已关闭",
            SoundPreviewResult.StopFailed => "音频设备未能释放，请关闭并重新启动客户端。",
            _ => "监护声音输出不可用或中断，请检查设备后重新启用。"
        };
    }
    private void RecordOutputResult(SoundPreviewResult result)
    {
        if (result == SoundPreviewResult.Completed) { SetOutputNotice(null); }
        else if (result is SoundPreviewResult.Unavailable or SoundPreviewResult.Interrupted or SoundPreviewResult.StopFailed)
        {
            string text = result switch
            {
                SoundPreviewResult.Interrupted => "监护声音输出已中断，请检查设备并重新启用",
                SoundPreviewResult.StopFailed => "音频设备未能释放，请重新启动客户端",
                _ => "声音输出不可用，请检查音频组件与输出设备"
            };
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
    private static Button Button(string content) => new()
    {
        Content = content,
        MinHeight = 44,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}
