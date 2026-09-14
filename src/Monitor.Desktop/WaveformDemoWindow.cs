// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Explicit development fixture: raw periodic signals, not a physiological model.
internal sealed class WaveformDemoWindow : Window
{
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private PeriodicWaveformGroup _source = CreateSource(false);
    private readonly bool _physiology;
    private PhysiologyWaveformGroup? _physiologySource;
    internal bool UsesPulse { get; private set; }
    internal Button ShapeButton { get; } = new() { Content = "形状：三角波（点击切换并重置）" };
    private WaveformEnvelope[] _blocks = [];
    private WaveformEnvelope[]? _pinned;
    private readonly TextBlock _status = new();
    private readonly ContentControl _trace = new();
    private DispatcherTimer? _timer;
    private bool _closed;
    private readonly TextBlock _runStatus = new();
    internal Button RunButton { get; } = new() { Content = "自动步进" };
    internal DispatcherTimer? ActiveTimer => _timer;
    internal Button StepButton { get; } = new() { Content = "步进 200 ms" };
    internal Button ResetButton { get; } = new() { Content = "重置" };
    internal Button HoldButton { get; } = new() { Content = "固定当前画面", IsEnabled = false };
    internal bool IsHeld => _pinned is not null;
    internal long? DisplayStartNs => DisplayBlocks.Length == 0 ? null : DisplayBlocks[Math.Max(0, DisplayBlocks.Length - 10)].StartSimTimeNs;
    private WaveformEnvelope[] DisplayBlocks => _pinned ?? _blocks;
    internal long SimulationTimeNs { get; private set; }
    internal int BlockCount => _blocks.Length;
    internal Control Trace => (Control)_trace.Content!;
    internal Grid LayoutRoot { get; } = new() { RowDefinitions = new RowDefinitions("Auto,*") };
    internal ScrollViewer ContentScroll { get; } = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    };
    internal TextBlock TeachingNotice { get; } = new()
    {
        Text = "教学模拟／不得用于患者监护或临床决策",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16, 12),
    };

    public WaveformDemoWindow(bool physiology = false)
    {
        _physiology = physiology;
        Title = physiology ? "事件驱动 ECG / Resp 开发演示 — 教学模拟" : "合成波形开发演示 — 教学模拟";
        ShapeButton.IsVisible = !physiology;
        Width = 1040;
        Height = 520;
        StackPanel panel = new() { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "合成周期信号，非生理模型；纵轴为原始计数 ±1000，无物理标定。" });
        panel.Children.Add(new TextBlock { Text = physiology ? "上：ECG 250 Hz / 40 ms；下：Resp 125 Hz / 80 ms；显式测试形态，未核验生理预设。" : "上：ECG 采集档 250 Hz / 40 ms 延迟；下：Pleth 采集档 125 Hz / 2 s 延迟。" });
        panel.Children.Add(new TextBlock { Text = "共享块等待全部通道齐备；2 s 固定窗从左到右回绕，横轴为源时间在周期内的位置。" });
        panel.Children.Add(new TextBlock { Text = "演示擦除间隙 200 ms，仅遮盖绘图；保留 2.2 s 源历史。" });
        WrapPanel actions = new();
        actions.Children.Add(StepButton);
        actions.Children.Add(ResetButton);
        actions.Children.Add(HoldButton);
        actions.Children.Add(RunButton);
        actions.Children.Add(ShapeButton);
        foreach (Control action in actions.Children) { action.Margin = new Thickness(0, 0, 12, 8); }
        panel.Children.Add(actions);
        panel.Children.Add(_runStatus);
        panel.Children.Add(_status);
        panel.Children.Add(_trace);
        foreach (TextBlock text in panel.Children.OfType<TextBlock>()) { text.TextWrapping = TextWrapping.Wrap; }
        ContentScroll.Content = panel;
        Grid.SetRow(ContentScroll, 1);
        LayoutRoot.Children.Add(TeachingNotice);
        LayoutRoot.Children.Add(ContentScroll);
        Content = LayoutRoot;
        StepButton.Click += (_, _) => { if (!_closed && _timer is null) { Advance(200_000_000); } };
        ResetButton.Click += (_, _) => { if (!_closed) { Reset(); } };
        HoldButton.Click += (_, _) => { if (!_closed) { ToggleHold(); } };
        RunButton.Click += (_, _) => ToggleRun();
        ShapeButton.Click += (_, _) =>
        {
            if (_closed || _physiology) { return; }
            Reset(!UsesPulse);
        };
        Closed += (_, _) => { _closed = true; Pause(); };
        Reset();
    }

    private void ToggleRun()
    {
        if (_closed) { return; }
        if (_timer is not null) { Pause(); return; }
        // A fresh timer identity fences callbacks from previous runs.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTick;
        _timer.Start();
        RunButton.Content = "暂停自动步进";
        StepButton.IsEnabled = false;
        _runStatus.Text = "自动步进：每次回调推进 200 ms；界面繁忙时不追赶，不保证实时速率。";
    }

    private void OnTick(object? sender, EventArgs args) => Pulse(sender);

    internal void Pulse(object? timer)
    {
        if (_closed || _timer is null || !ReferenceEquals(timer, _timer)) { return; }
        try { Advance(200_000_000); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            Pause();
            _runStatus.Text = "生成失败，自动步进已暂停；可重置后重新开始。";
        }
    }

    private void Pause()
    {
        DispatcherTimer? old = _timer;
        _timer = null;
        if (old is not null) { old.Stop(); old.Tick -= OnTick; }
        RunButton.Content = "自动步进";
        RunButton.IsEnabled = !_closed;
        StepButton.IsEnabled = !_closed;
        ResetButton.IsEnabled = !_closed;
        ShapeButton.IsEnabled = !_closed;
        HoldButton.IsEnabled = !_closed && _blocks.Length > 0;
        _runStatus.Text = "自动步进已暂停；可手动步进。";
    }

    internal void Advance(long deltaNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaNs);
        long next = checked(SimulationTimeNs + deltaNs);
        PeriodicWaveformGroup? trial = _physiology ? null : PeriodicWaveformGroup.Restore(_source.CaptureState());
        PhysiologyWaveformGroup? eventTrial = _physiology ? PhysiologyWaveformGroup.Restore(_physiologySource!.CaptureState()) : null;
        IReadOnlyList<byte[]> wires = eventTrial is not null ? eventTrial.AdvanceTo(next, 50, 1, 100) : trial!.AdvanceTo(next, 50, 1);
        WaveformEnvelope[] blocks = _blocks.Concat(wires
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes))).TakeLast(11).ToArray();
        RawTrace? control = IsHeld ? null : new(blocks, _physiology);
        if (trial is not null) { _source = trial; }
        if (eventTrial is not null) { _physiologySource = eventTrial; }
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
            RawTrace control = new(_blocks, _physiology);
            _pinned = null;
            _trace.Content = control;
        }
        UpdateStatus();
    }

    private void Reset() => Reset(UsesPulse);

    private void Reset(bool pulse)
    {
        PeriodicWaveformGroup source = CreateSource(pulse);
        PhysiologyWaveformGroup? eventSource = _physiology ? CreatePhysiologySource() : null;
        RawTrace empty = new([], _physiology);
        Pause();
        _source = source;
        _physiologySource = eventSource;
        UsesPulse = pulse;
        ShapeButton.Content = pulse ? "形状：双相脉冲（点击切换并重置）" : "形状：三角波（点击切换并重置）";
        _blocks = [];
        _pinned = null;
        SimulationTimeNs = 0;
        _trace.Content = empty;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        HoldButton.IsEnabled = _blocks.Length > 0;
        HoldButton.Content = IsHeld ? "返回最新数据" : "固定当前画面";
        _status.Text = DisplayBlocks.Length == 0
            ? $"数据模拟时间 {SimulationTimeNs / 1_000_000} ms；等待完整通道块"
            : $"数据模拟时间 {SimulationTimeNs / 1_000_000} ms；{(IsHeld ? "固定画面（步进仍生成后台数据）" : "最新数据")}；源区间 [{DisplayStartNs / 1_000_000}, {(DisplayBlocks[^1].StartSimTimeNs + 200_000_000) / 1_000_000}) ms";
    }

    private static PeriodicWaveformGroup CreateSource(bool pulse)
    {
        // Explicit synthetic fixtures, not registered ECG/pleth morphology.
        long[] table = pulse
            ? [0, 0, 0, 1000 * FixedPointMath.Q32One, -500 * FixedPointMath.Q32One, 0, 0, 0]
            : [0, 1000 * FixedPointMath.Q32One, 0, -1000 * FixedPointMath.Q32One];
        PeriodicWaveformChannelPlan Channel(Guid id, string profile, int capacity, ulong increment) => new(
            new(profile, 1, 0, 0, increment, table),
            new(id, profile, 1, 1, 0, 1), capacity, 0);
        return PeriodicWaveformGroup.Start(Ecg, Pleth, 1, 1, 0, 16,
            [Channel(Ecg, "AcqECGMonitor250@1", 10, 0x0100000000000000),
             Channel(Pleth, "AcqPleth125@1", 250, 0x0200000000000000)]);
    }

    private static PhysiologyWaveformGroup CreatePhysiologySource()
    {
        const long q = FixedPointMath.Q32One;
        RegularPhysiologyPlan plan = new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
        return PhysiologyWaveformGroup.Start(Ecg, Pleth, 1, 1, 1, 0, 16,
            [new(plan, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1),
                [new(PhysiologyCycleEventKind.AtrialElectrical, 0, 80_000_000, [0, 50*q, 100*q, 50*q]),
                 new(PhysiologyCycleEventKind.VentricularElectrical, 0, 80_000_000, [0, -200*q, 1000*q, -100*q]),
                 new(PhysiologyCycleEventKind.VentricularElectrical, 160_000_000, 160_000_000, [0, 150*q, 300*q, 150*q])], 10, 0),
             new(plan, new(Pleth, "AcqResp125@1", 1, 1, 0, 1),
                [new(PhysiologyCycleEventKind.InspirationStart, 0, 3_750_000_000, [0, 500*q, 1000*q, 500*q])], 10, 0)]);
    }

    private sealed class RawTrace : Control
    {
        private readonly (Point Start, Point End, int Channel)[] _segments;
        private readonly double? _gapStart;
        private readonly bool _resp;
        public RawTrace(WaveformEnvelope[] blocks, bool resp)
        {
            _resp = resp;
            Height = 240;
            List<(Point, Point, int)> segments = [];
            if (blocks.Length > 0)
            {
                _gapStart = ((blocks[^1].StartSimTimeNs + 200_000_000) % 2_000_000_000) / 2_000_000_000.0;
                for (int channel = 0; channel < 2; channel++)
                {
                    Point? previous = null;
                    foreach (WaveformEnvelope block in blocks.TakeLast(10))
                    {
                        WaveformPlane plane = block.Planes[channel];
                        for (int index = 0; index < plane.Samples.Count; index++)
                        {
                            // Floating point is confined to terminal screen coordinates.
                            // Fixture blocks are 200ms aligned from epoch zero.
                            // Reduce integer time before terminal pixel conversion.
                            double time = block.StartSimTimeNs % 2_000_000_000 + index * 1_000_000_000.0 * plane.SampleRateDenominator / plane.SampleRateNumerator;
                            Point point = new(time / 2_000_000_000, channel * 120 + 60 - plane.Samples[index] * 0.05);
                            if (previous is { } start && point.X > start.X) { segments.Add((start, point, channel)); }
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
                Pen second = new(_resp ? Brushes.Yellow : Brushes.Cyan, 1);
                foreach (var segment in _segments)
                {
                    context.DrawLine(segment.Channel == 0 ? first : second,
                        new(segment.Start.X * Bounds.Width, segment.Start.Y),
                        new(segment.End.X * Bounds.Width, segment.End.Y));
                }
                if (_gapStart is { } start)
                {
                    double end = start + 0.1;
                    context.FillRectangle(Brushes.Black, new Rect(start * Bounds.Width, 0,
                        (Math.Min(1, end) - start) * Bounds.Width, Bounds.Height));
                    if (end > 1)
                    { context.FillRectangle(Brushes.Black, new Rect(0, 0, (end - 1) * Bounds.Width, Bounds.Height)); }
                }
            }
        }
    }
}
