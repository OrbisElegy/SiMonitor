// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
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
    private readonly bool _projected;
    private ElectrodeWaveformGroup? _electrodeSource;
    private PhysiologyWaveformGroup? _physiologySource;
    internal bool UsesPulse { get; private set; }
    internal Button ShapeButton { get; } = new() { Content = "形状：三角波（点击切换并重置）" };
    private WaveformEnvelope[] _blocks = [];
    private WaveformEnvelope[]? _pinned;
    private readonly TextBlock _status = new();
    private readonly ContentControl _trace = new();
    private DispatcherTimer? _timer;
    private bool _closed;
    private long _lastTick;
    internal long LiveFrontierNs { get; private set; }
    private long PresentationLatencyNs => _projected ? 240_000_000 : _physiology ? 280_000_000 : 2_200_000_000;
    private readonly TextBlock _runStatus = new();
    internal Button RunButton { get; } = new() { Content = "连续扫屏" };
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

    public WaveformDemoWindow(bool physiology = false, bool projected = false)
    {
        _projected = projected;
        _physiology = physiology && !projected;
        Title = projected ? "12 导联电极投影演示 — 教学模拟" : physiology ? "事件驱动 ECG / Resp 开发演示 — 教学模拟" : "合成波形开发演示 — 教学模拟";
        ShapeButton.IsVisible = !physiology && !projected;
        Width = 1040;
        Height = projected ? 900 : 520;
        StackPanel panel = new() { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = projected ? "电极模型示意：12 导联均由电极电位投影；非已验证正常成人预设，无屏幕毫米标定。" : physiology ? "教材约束的单导联参考：ECG 每计数 1 μV；Resp 为相对量。非验证预设，无屏幕毫米标定。" : "合成周期信号，非生理模型；纵轴为原始计数 ±1000，无物理标定。" });
        panel.Children.Add(new TextBlock { Text = projected ? "同步监护采样 250 Hz／40 ms 延迟；每计数 1 μV。此视图为连续监护扫屏，不是诊断型 10 s 记录。" : physiology ? "上：ECG 250 Hz / 40 ms；下：Resp 125 Hz / 80 ms；显式测试形态，未核验生理预设。" : "上：ECG 采集档 250 Hz / 40 ms 延迟；下：Pleth 采集档 125 Hz / 2 s 延迟。" });
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
            if (_closed || _physiology || _projected) { return; }
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
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _lastTick = Stopwatch.GetTimestamp();
        _timer.Start();
        RunButton.Content = "暂停扫屏";
        StepButton.IsEnabled = false;
        _runStatus.Text = "连续扫屏：约 60 帧／秒；缓冲完整数据块后逐步显示，长时间卡顿不追赶。";
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (_closed || _timer is null || !ReferenceEquals(sender, _timer)) { return; }
        long now = Stopwatch.GetTimestamp();
        long delta = Math.Clamp(Stopwatch.GetElapsedTime(_lastTick, now).Ticks * 100, 1, 50_000_000);
        _lastTick = now;
        Pulse(sender, delta);
    }

    internal void Pulse(object? timer, long deltaNs = 16_000_000)
    {
        if (_closed || _timer is null || !ReferenceEquals(timer, _timer)) { return; }
        try { Advance(deltaNs, progressive: true); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            Pause();
            _runStatus.Text = "生成失败，扫屏已暂停；可重置后重新开始。";
        }
    }

    private void Pause()
    {
        DispatcherTimer? old = _timer;
        _timer = null;
        if (old is not null) { old.Stop(); old.Tick -= OnTick; }
        RunButton.Content = "连续扫屏";
        RunButton.IsEnabled = !_closed;
        StepButton.IsEnabled = !_closed;
        ResetButton.IsEnabled = !_closed;
        ShapeButton.IsEnabled = !_closed;
        HoldButton.IsEnabled = !_closed && _blocks.Length > 0;
        _runStatus.Text = "扫屏已暂停；可手动步进。";
    }

    internal void Advance(long deltaNs, bool progressive = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaNs);
        long next = checked(SimulationTimeNs + deltaNs);
        PeriodicWaveformGroup? trial = _physiology || _projected ? null : PeriodicWaveformGroup.Restore(_source.CaptureState());
        PhysiologyWaveformGroup? eventTrial = _physiology ? PhysiologyWaveformGroup.Restore(_physiologySource!.CaptureState()) : null;
        ElectrodeWaveformGroup? electrodeTrial = _projected ? ElectrodeWaveformGroup.Restore(_electrodeSource!.CaptureState()) : null;
        IReadOnlyList<byte[]> wires = electrodeTrial is not null ? electrodeTrial.AdvanceTo(next, 50, 1, 100) : eventTrial is not null ? eventTrial.AdvanceTo(next, 50, 1, 100) : trial!.AdvanceTo(next, 50, 1);
        WaveformEnvelope[] blocks = _blocks.Concat(wires
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes))).TakeLast(11).ToArray();
        long frontier = blocks.Length == 0 ? 0 : progressive
            ? Math.Max(LiveFrontierNs, Math.Min(blocks[^1].StartSimTimeNs + 200_000_000,
                Math.Max(0, next - PresentationLatencyNs)))
            : blocks[^1].StartSimTimeNs + 200_000_000;
        RawTrace? control = IsHeld ? null : new(blocks, _physiology, frontier, _projected);
        if (trial is not null) { _source = trial; }
        if (eventTrial is not null) { _physiologySource = eventTrial; }
        if (electrodeTrial is not null) { _electrodeSource = electrodeTrial; }
        _blocks = blocks;
        SimulationTimeNs = next;
        LiveFrontierNs = frontier;
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
            RawTrace control = new(_blocks, _physiology, LiveFrontierNs, _projected);
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
        ElectrodeWaveformGroup? electrodeSource = _projected ? ProjectedEcgDemoSource.Create() : null;
        RawTrace empty = new([], _physiology, projected: _projected);
        Pause();
        _source = source;
        _physiologySource = eventSource;
        _electrodeSource = electrodeSource;
        UsesPulse = pulse;
        ShapeButton.Content = pulse ? "形状：双相脉冲（点击切换并重置）" : "形状：三角波（点击切换并重置）";
        _blocks = [];
        _pinned = null;
        SimulationTimeNs = 0;
        LiveFrontierNs = 0;
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
        EcgCycleTiming timing = TextbookEcgReference.Timing;
        RegularPhysiologyPlan plan = new(0, timing.RrIntervalNs, timing.PrIntervalNs, 80_000_000,
            timing.PrIntervalNs + 80_000_000, 3_750_000_000, 1_875_000_000);
        return PhysiologyWaveformGroup.Start(Ecg, Pleth, 1, 1, 1, 0, 16,
            [new(plan, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1),
                TextbookEcgReference.CreateBands(), 10, 0),
             new(plan, new(Pleth, "AcqResp125@1", 1, 1, 0, 1),
                [new(PhysiologyCycleEventKind.InspirationStart, 0, 3_750_000_000, PhysiologyDemoTables.Resp)], 10, 0)]);
    }

    private sealed class RawTrace : Control
    {
        private readonly (Point Start, Point End, int Channel)[] _segments;
        private readonly double? _gapStart;
        private readonly bool _resp;
        private readonly bool _projected;
        public RawTrace(WaveformEnvelope[] blocks, bool resp, long frontier = 0, bool projected = false)
        {
            _resp = resp;
            _projected = projected;
            Height = projected ? 720 : 240;
            List<(Point, Point, int)> segments = [];
            if (blocks.Length > 0)
            {
                _gapStart = (frontier % 2_000_000_000) / 2_000_000_000.0;
                for (int channel = 0; channel < (projected ? 12 : 2); channel++)
                {
                    Point? previous = null;
                    long previousTime = 0;
                    foreach (WaveformEnvelope block in blocks)
                    {
                        WaveformPlane plane = projected ? block.Planes.Single(item => item.ChannelId == ProjectedEcgDemoSource.ChannelId((EcgLead)channel)) : block.Planes[channel];
                        for (int index = 0; index < plane.Samples.Count; index++)
                        {
                            // Floating point is confined to terminal screen coordinates.
                            // Fixture blocks are 200ms aligned from epoch zero.
                            // Reduce integer time before terminal pixel conversion.
                            long sourceTime = block.StartSimTimeNs + index * 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
                            double time = sourceTime % 2_000_000_000;
                            Point point = new(time / 2_000_000_000, projected ? channel * 60 + 30 - plane.Samples[index] * 0.015 : channel * 120 + 60 - plane.Samples[index] * 0.05);
                            if (previous is { } start && point.X > start.X &&
                                sourceTime > frontier - 2_000_000_000 && previousTime < frontier)
                            {
                                // Clip only a segment between two acquired samples; never extrapolate.
                                long from = Math.Max(previousTime, frontier - 2_000_000_000);
                                long to = Math.Min(sourceTime, frontier);
                                Point At(long value) => start + (point - start) *
                                    ((double)(value - previousTime) / (sourceTime - previousTime));
                                segments.Add((At(from), At(to), channel));
                            }
                            previous = point;
                            previousTime = sourceTime;
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
                double left = _projected ? 44 : 0;
                double width = Math.Max(0, Bounds.Width - left);
                Pen first = new(Brushes.Lime, 1);
                Pen second = new(_resp ? Brushes.Yellow : Brushes.Cyan, 1);
                foreach (var segment in _segments)
                {
                    context.DrawLine(_projected || segment.Channel == 0 ? first : second,
                        new(left + segment.Start.X * width, segment.Start.Y),
                        new(left + segment.End.X * width, segment.End.Y));
                }
                if (_projected)
                {
                    for (int lead = 0; lead < 12; lead++)
                    {
                        var label = new FormattedText(ProjectedEcgDemoSource.LeadNames[lead], CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, new Typeface("sans-serif"), 12, Brushes.Lime);
                        context.DrawText(label, new Point(3, lead * 60 + 20));
                    }
                }
                if (_gapStart is { } start)
                {
                    double end = start + 0.1;
                    context.FillRectangle(Brushes.Black, new Rect(left + start * width, 0,
                        (Math.Min(1, end) - start) * width, Bounds.Height));
                    if (end > 1)
                    { context.FillRectangle(Brushes.Black, new Rect(left, 0, (end - 1) * width, Bounds.Height)); }
                }
            }
        }
    }
}
