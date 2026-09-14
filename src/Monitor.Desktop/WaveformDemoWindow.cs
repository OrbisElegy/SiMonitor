// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Desktop;

// Explicit development fixture: raw periodic signals, not a physiological model.
internal sealed class WaveformDemoWindow : Window
{
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private PeriodicWaveformGroup _source = CreateSource();
    private WaveformEnvelope[] _blocks = [];
    private WaveformEnvelope[]? _pinned;
    private readonly TextBlock _status = new();
    private readonly ContentControl _trace = new();
    internal Button StepButton { get; } = new() { Content = "步进 200 ms" };
    internal Button ResetButton { get; } = new() { Content = "重置" };
    internal Button HoldButton { get; } = new() { Content = "固定当前画面", IsEnabled = false };
    internal bool IsHeld => _pinned is not null;
    internal long? DisplayStartNs => DisplayBlocks.Length == 0 ? null : DisplayBlocks[0].StartSimTimeNs;
    private WaveformEnvelope[] DisplayBlocks => _pinned ?? _blocks;
    internal long SimulationTimeNs { get; private set; }
    internal int BlockCount => _blocks.Length;
    internal Control Trace => (Control)_trace.Content!;

    public WaveformDemoWindow()
    {
        Title = "合成波形开发演示 — 教学模拟";
        Width = 1040;
        Height = 520;
        StackPanel panel = new() { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "教学模拟／不得用于患者监护或临床决策" });
        panel.Children.Add(new TextBlock { Text = "合成周期信号，非生理模型；纵轴为原始计数 ±1000，无物理标定。" });
        panel.Children.Add(new TextBlock { Text = "上：ECG 采集档 250 Hz / 40 ms 延迟；下：Pleth 采集档 125 Hz / 2 s 延迟。" });
        panel.Children.Add(new TextBlock { Text = "共享块等待全部通道齐备；显示最近 2 s 已完成数据，横轴为源数据时间。" });
        StackPanel actions = new() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(StepButton);
        actions.Children.Add(ResetButton);
        actions.Children.Add(HoldButton);
        panel.Children.Add(actions);
        panel.Children.Add(_status);
        panel.Children.Add(_trace);
        Content = panel;
        StepButton.Click += (_, _) => Advance(200_000_000);
        ResetButton.Click += (_, _) => Reset();
        HoldButton.Click += (_, _) => ToggleHold();
        Reset();
    }

    internal void Advance(long deltaNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaNs);
        long next = checked(SimulationTimeNs + deltaNs);
        PeriodicWaveformGroup trial = PeriodicWaveformGroup.Restore(_source.CaptureState());
        WaveformEnvelope[] blocks = _blocks.Concat(trial.AdvanceTo(next, 50, 1)
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes))).TakeLast(10).ToArray();
        RawTrace? control = IsHeld ? null : new(blocks);
        _source = trial;
        _blocks = blocks;
        SimulationTimeNs = next;
        if (control is not null) { _trace.Content = control; }
        UpdateStatus();
    }

    private void ToggleHold()
    {
        if (_blocks.Length == 0) { return; }
        if (_pinned is null)
        {
            // Decoded blocks are owned, never mutated and independent of retention.
            _pinned = _blocks;
        }
        else
        {
            RawTrace control = new(_blocks);
            _pinned = null;
            _trace.Content = control;
        }
        UpdateStatus();
    }

    private void Reset()
    {
        _source = CreateSource();
        _blocks = [];
        _pinned = null;
        SimulationTimeNs = 0;
        _trace.Content = new RawTrace(_blocks);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        HoldButton.IsEnabled = _blocks.Length > 0;
        HoldButton.Content = IsHeld ? "返回最新数据" : "固定当前画面";
        _status.Text = DisplayBlocks.Length == 0
            ? $"数据模拟时间 {SimulationTimeNs / 1_000_000} ms；等待完整通道块"
            : $"数据模拟时间 {SimulationTimeNs / 1_000_000} ms；{(IsHeld ? "固定画面（步进仍生成后台数据）" : "最新数据")}；源区间 [{DisplayBlocks[0].StartSimTimeNs / 1_000_000}, {(DisplayBlocks[^1].StartSimTimeNs + 200_000_000) / 1_000_000}) ms";
    }

    private static PeriodicWaveformGroup CreateSource()
    {
        PeriodicWaveformChannelPlan Channel(Guid id, string profile, int capacity, ulong increment) => new(
            new(profile, 1, 0, 0, increment,
                [0, 1000 * FixedPointMath.Q32One, 0, -1000 * FixedPointMath.Q32One]),
            new(id, profile, 1, 1, 0, 1), capacity, 0);
        return PeriodicWaveformGroup.Start(Ecg, Pleth, 1, 1, 0, 16,
            [Channel(Ecg, "AcqECGMonitor250@1", 10, 0x0100000000000000),
             Channel(Pleth, "AcqPleth125@1", 250, 0x0200000000000000)]);
    }

    private sealed class RawTrace : Control
    {
        private readonly (Point Start, Point End, int Channel)[] _segments;
        public RawTrace(WaveformEnvelope[] blocks)
        {
            Height = 240;
            List<(Point, Point, int)> segments = [];
            if (blocks.Length > 0)
            {
                long origin = blocks[0].StartSimTimeNs;
                for (int channel = 0; channel < 2; channel++)
                {
                    Point? previous = null;
                    foreach (WaveformEnvelope block in blocks)
                    {
                        WaveformPlane plane = block.Planes[channel];
                        for (int index = 0; index < plane.Samples.Count; index++)
                        {
                            // Floating point is confined to terminal screen coordinates.
                            double time = block.StartSimTimeNs - origin + index * 1_000_000_000.0 * plane.SampleRateDenominator / plane.SampleRateNumerator;
                            Point point = new(time / 2_000_000_000, channel * 120 + 60 - plane.Samples[index] * 0.05);
                            if (previous is { } start) { segments.Add((start, point, channel)); }
                            previous = point;
                        }
                    }
                }
            }
            _segments = segments.ToArray();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(Brushes.Black, Bounds.WithX(0).WithY(0));
            using (context.PushClip(new Rect(Bounds.Size)))
            {
                Pen first = new(Brushes.Lime, 1);
                Pen second = new(Brushes.Cyan, 1);
                foreach (var segment in _segments)
                {
                    context.DrawLine(segment.Channel == 0 ? first : second,
                        new(segment.Start.X * Bounds.Width, segment.Start.Y),
                        new(segment.End.X * Bounds.Width, segment.End.Y));
                }
            }
        }
    }
}
