// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;

namespace Monitor.Desktop;

internal sealed class SoundSettingsPanel : StackPanel
{
    private readonly Func<int, CancellationToken, Task<SoundPreviewResult>> _play;
    private CancellationTokenSource? _cancellation;
    private bool _closed;
    private readonly MonitorAlarmPlayback _alarms = new(() => new NativeAudioOutputFactory(Path.Combine(AppContext.BaseDirectory, "sim_audio_native.dll")));
    private CancellationTokenSource? _alarmCancellation;
    private MonitorNoticeLevel? _alarmLevel;
    private MonitorSoundTiming _timing = new();
    private bool _monitorRunning;
    internal MonitorNotice? OutputNotice { get; private set; }
    internal event Action? OutputNoticeChanged;
    internal CheckBox AlarmEnabled { get; } = new() { Content = "启用监护提示声音", IsChecked = false };
    internal CheckBox HeartbeatEnabled { get; } = new() { Content = "ECG 心搏提示音（与报警声独立重叠）", IsChecked = true };
    internal Slider Volume { get; } = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
    internal Button Audition { get; } = Button("试听三声");
    internal Button Stop { get; } = Button("停止试听");
    internal TextBlock Status { get; } = Text("未播放");

    internal SoundSettingsPanel(Func<int, CancellationToken, Task<SoundPreviewResult>>? play = null)
    {
        var playback = new SoundPreviewPlayback(() => new NativeAudioOutputFactory(Path.Combine(AppContext.BaseDirectory, "sim_audio_native.dll")));
        _play = play ?? playback.PlayAsync;
        Margin = new Thickness(20); Spacing = 16;
        Children.Add(Text("声音输出"));
        Children.Add(Text("输出至系统默认音频设备。点击试听检查音量；监护提示声音须另行启用。"));
        var volumeLabel = Text("声音音量：50%"); Children.Add(volumeLabel); Children.Add(Volume);
        AutomationProperties.SetName(Volume, "声音音量，百分比");
        Volume.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty) { volumeLabel.Text = $"声音音量：{Volume.Value:0}%"; }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(Audition); buttons.Children.Add(Stop); Children.Add(buttons);
        Stop.IsEnabled = false; Children.Add(Status);
        Children.Add(AlarmEnabled);
        Children.Add(HeartbeatEnabled);
        Children.Add(Text("报警音高于日常心搏音，音量滑块同时调整两者。报警按最高活动级别发声；心搏音由 ECG 检测到的搏动触发，可与报警起音和尾音重叠。暂停模拟时静音。试听三声仅用于检查输出。"));
        HeartbeatEnabled.IsCheckedChanged += (_, _) => Publish();
        AlarmEnabled.IsCheckedChanged += async (_, _) =>
        {
            if (AlarmEnabled.IsChecked == true) { await RunAlarmsAsync(); }
            else { _alarms.SetHeartbeatEnabled(false); _alarmCancellation?.Cancel(); }
        };
        Audition.Click += async (_, _) => await PreviewAsync();
        Stop.Click += (_, _) => StopPreview();
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
    internal void UpdateAlarm(MonitorNoticeLevel? level, MonitorSoundTiming timing, IReadOnlyList<DetectedEcgBeat>? beats = null)
    {
        if (_alarms.OutputActive) { SetOutputNotice(null); }
        _monitorRunning = true; _alarmLevel = level; _timing = timing;
        Publish();
        // Advance delivers new measurement events once, never extrapolated HR.
        if (AlarmEnabled.IsChecked == true && HeartbeatEnabled.IsChecked == true && beats is { Count: > 0 })
        { _alarms.SubmitHeartbeat((int)Volume.Value); }
    }
    internal void PauseMonitor()
    {
        _monitorRunning = false; Publish();
    }
    private void Publish()
    {
        _alarms.SetRequest(_monitorRunning && _alarmLevel is { } active ? new(active, (int)Volume.Value, _timing) : null);
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
    internal void Close() { _closed = true; StopPreview(); _alarmCancellation?.Cancel(); }
    private static Button Button(string content) => new()
    {
        Content = content,
        MinHeight = 44,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}
