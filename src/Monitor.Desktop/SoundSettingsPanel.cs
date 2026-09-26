// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Infrastructure.Audio;

namespace Monitor.Desktop;

internal sealed class SoundSettingsPanel : StackPanel
{
    private readonly Func<int, CancellationToken, Task<SoundPreviewResult>> _play;
    private CancellationTokenSource? _cancellation;
    private bool _closed;
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
        Children.Add(Text("输出至系统默认音频设备。点击试听检查音量；不会随页面切换或波形重启自动发声。"));
        var volumeLabel = Text("试听音量：50%"); Children.Add(volumeLabel); Children.Add(Volume);
        AutomationProperties.SetName(Volume, "试听音量，百分比");
        Volume.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty) { volumeLabel.Text = $"试听音量：{Volume.Value:0}%"; }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(Audition); buttons.Children.Add(Stop); Children.Add(buttons);
        Stop.IsEnabled = false; Children.Add(Status);
        Children.Add(Text("心搏提示音与报警声音尚未启用。试听为固定三声，与患者心率、脉率和报警状态无关。"));
        Audition.Click += async (_, _) => await PreviewAsync();
        Stop.Click += (_, _) => StopPreview();
    }

    internal async Task PreviewAsync()
    {
        if (_closed || _cancellation is not null || !Audition.IsEnabled) { return; }
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        Audition.IsEnabled = false; Volume.IsEnabled = false; Stop.IsEnabled = true;
        Status.Text = "正在试听…";
        var result = await _play((int)Volume.Value, cancellation.Token);
        _cancellation = null;
        if (_closed) { return; }
        Volume.IsEnabled = true; Stop.IsEnabled = false;
        Audition.IsEnabled = result != SoundPreviewResult.StopFailed;
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
    internal void Close() { _closed = true; StopPreview(); }
    private static Button Button(string content) => new()
    {
        Content = content,
        MinHeight = 44,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}
