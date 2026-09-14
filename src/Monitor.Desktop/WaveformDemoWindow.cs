// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Domain.Presentation;
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
    internal ProjectedEcgDemoConfiguration EcgConfiguration { get; private set; } = ProjectedEcgDemoConfiguration.Default;
    internal ComboBox QtMethod { get; } = new() { ItemsSource = new[] { "原始示意时序", "Bazett", "Fridericia" }, SelectedIndex = 0 };
    internal TextBox HeartRateInput { get; } = new() { Text = "75", Width = 70, IsEnabled = false };
    internal TextBox QtcInput { get; } = new() { Text = "400", Width = 70, IsEnabled = false };
    internal Button ApplyEcgButton { get; } = new() { Content = "应用并重新开始" };
    internal TextBlock EcgConfigurationStatus { get; } = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _activeEcgConfiguration = new() { TextWrapping = TextWrapping.Wrap };
    private PhysiologyWaveformGroup? _physiologySource;
    internal PhysiologyDemoConfiguration BreathConfiguration { get; private set; } = PhysiologyDemoConfiguration.Default;
    internal TextBox BreathPeriodInput { get; } = new() { Text = "3750", Width = 75 };
    internal TextBox InspirationInput { get; } = new() { Text = "1875", Width = 75 };
    internal TextBox RespAmplitudeInput { get; } = new() { Text = "1000", Width = 75 };
    internal TextBox Co2PlateauInput { get; } = new() { Text = "", Width = 75 };
    internal TextBox Co2BaselineInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox Co2EndInput { get; } = new() { Text = "40", Width = 65 };
    internal TextBox Co2DeadSpaceInput { get; } = new() { Text = "125", Width = 65 };
    internal TextBox Co2RiseInput { get; } = new() { Text = "250", Width = 65 };
    internal TextBox Co2FallInput { get; } = new() { Text = "200", Width = 65 };
    internal TextBox Co2TransportInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox Co2DispersionInput { get; } = new() { Text = "0", Width = 65 };
    internal Button ApplyBreathButton { get; } = new() { Content = "应用波形参数并重新开始" };
    internal TextBlock BreathConfigurationStatus { get; } = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _activeBreathConfiguration = new() { TextWrapping = TextWrapping.Wrap };
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
    private long PresentationLatencyNs => _projected ? 240_000_000 : 2_200_000_000;
    private readonly TextBlock _runStatus = new();
    internal Button RunButton { get; } = new() { Content = "连续扫屏" };
    internal DispatcherTimer? ActiveTimer => _timer;
    internal Button StepButton { get; } = new() { Content = "步进 200 ms" };
    internal Button ResetButton { get; } = new() { Content = "重置" };
    internal Button HoldButton { get; } = new() { Content = "固定当前画面", IsEnabled = false };
    internal bool IsHeld => _pinned is not null;
    internal long DisplayDurationNs => ((RawTrace)Trace).DurationNs;
    internal long? DisplayStartNs => ((RawTrace)Trace).SourceStartNs;
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
        Title = projected ? "12 导联电极投影演示 — 教学模拟" : physiology ? "事件驱动 ECG / Resp / Pleth / ABP / CO₂ / PA / CVP 演示 — 教学模拟" : "合成波形开发演示 — 教学模拟";
        ShapeButton.IsVisible = !physiology && !projected;
        Width = 1040 + (projected ? 85 : 0);
        Height = projected ? 900 : physiology ? 700 : 520;
        StackPanel panel = new() { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = projected ? "电极模型示意：12 导联均由电极电位投影；非已验证正常成人预设，无屏幕毫米标定。" : physiology ? "教材约束的单导联参考：ECG 每计数 1 μV；Resp、Pleth 为相对量。非验证预设，无屏幕毫米标定。" : "合成周期信号，非生理模型；纵轴为原始计数 ±1000，无物理标定。" });
        panel.Children.Add(new TextBlock { Text = projected ? "同步监护采样 250 Hz／40 ms 延迟；每计数 1 μV。此视图为连续监护扫屏，不是诊断型 10 s 记录。" : physiology ? "第一行 ECG（绿）250 Hz / 40 ms；第二行 Resp（黄）125 Hz / 80 ms；第三行 Pleth（青）125 Hz / 2 s。延迟为采集处理延迟。" : "上：ECG 采集档 250 Hz / 40 ms 延迟；下：Pleth 采集档 125 Hz / 2 s 延迟。" });
        if (_physiology)
        {
            panel.Children.Add(new TextBlock { Text = "第四行 ABP（红）：125 Hz / 80 ms；固定压力尺度 0–160 mmHg。按实际样本的比例与偏移换算压力，未计算 SYS/DIA/MAP。" });
            panel.Children.Add(new TextBlock { Text = "第五行 CO₂（白）：100 Hz / 2 s；固定尺度 0–80 mmHg。由呼气事件驱动，下一吸气触发下降，管路滞后另计；未计算 EtCO₂ 或 RR-CO₂。" });
            panel.Children.Add(new TextBlock { Text = "第六行 PA（紫红）：125 Hz / 80 ms；固定尺度 0–40 mmHg。独立肺动脉压形态种子，未计算 PAP SYS/DIA/MEAN。" });
            panel.Children.Add(new TextBlock { Text = "第七行 CVP（橙）：125 Hz / 80 ms；固定尺度 −5–15 mmHg。a/c/v 波与 x/y 下降，基线 6 mmHg，吸气压力变化 −1 mmHg；未计算平均 CVP。" });
            panel.Children.Add(new TextBlock { Text = "Pleth、ABP、PA 由机械搏动触发；示意电机械延迟 80 ms，传播延迟 Pleth/ABP 80 ms、PA 40 ms，处理延迟另计。七通道共用源时间；不显示估计的 SpO₂ 或脉率。" });
        }
        panel.Children.Add(new TextBlock { Text = "共享块等待全部通道齐备；横向速度固定为 125 逻辑像素/秒，1000 像素对应 8 秒。拉宽窗口显示更多时间，波形不拉伸。" });
        panel.Children.Add(new TextBlock { Text = "演示擦除间隙 200 ms；可见时间随绘图区宽度变化，最多 60 秒。冻结画面保留原时间范围。" });
        if (projected) { panel.Children.Add(new TextBlock { Text = "每行左侧标定方波：1 mV × 200 ms；与波形使用相同尺度，不代表屏幕毫米已校准。" }); }
        WrapPanel actions = new();
        actions.Children.Add(StepButton);
        actions.Children.Add(ResetButton);
        actions.Children.Add(HoldButton);
        actions.Children.Add(RunButton);
        actions.Children.Add(ShapeButton);
        foreach (Control action in actions.Children) { action.Margin = new Thickness(0, 0, 12, 8); }
        panel.Children.Add(actions);
        if (projected)
        {
            WrapPanel settings = new();
            settings.Children.Add(QtMethod);
            settings.Children.Add(new TextBlock { Text = "源心率（次/分）" });
            settings.Children.Add(HeartRateInput);
            settings.Children.Add(new TextBlock { Text = "QTc（ms）" });
            settings.Children.Add(QtcInput);
            settings.Children.Add(ApplyEcgButton);
            foreach (Control item in settings.Children) { item.Margin = new Thickness(0, 0, 8, 8); }
            panel.Children.Add(settings);
            panel.Children.Add(new TextBlock { Text = "应用后暂停扫屏并清空当前及固定画面，从零生成。源心率不是检测心率；输入范围 30–200 次/分，QTc 1–1000 ms，仍须满足波段时序。" });
            panel.Children.Add(_activeEcgConfiguration);
            panel.Children.Add(EcgConfigurationStatus);
        }
        if (_physiology)
        {
            WrapPanel settings = new();
            settings.Children.Add(new TextBlock { Text = "呼吸周期（ms）" });
            settings.Children.Add(BreathPeriodInput);
            settings.Children.Add(new TextBlock { Text = "吸气时长（ms）" });
            settings.Children.Add(InspirationInput);
            settings.Children.Add(new TextBlock { Text = "Resp 相对幅度（可负）" });
            settings.Children.Add(RespAmplitudeInput);
            settings.Children.Add(new TextBlock { Text = "CO₂ 平台起始（mmHg，留空参考）" });
            settings.Children.Add(Co2PlateauInput);
            foreach (var (label, input) in new[] { ("CO₂ 基线（整数 mmHg）", Co2BaselineInput),
                ("呼气末目标（整数 mmHg）", Co2EndInput), ("死腔时长（ms）", Co2DeadSpaceInput),
                ("上升时长（ms）", Co2RiseInput), ("下降时长（ms）", Co2FallInput), ("CO₂ 管路滞后（ms）", Co2TransportInput), ("展宽步长（ms）", Co2DispersionInput) })
            {
                settings.Children.Add(new TextBlock { Text = label });
                settings.Children.Add(input);
            }
            settings.Children.Add(ApplyBreathButton);
            foreach (Control item in settings.Children) { item.Margin = new Thickness(0, 0, 8, 8); }
            panel.Children.Add(settings);
            panel.Children.Add(new TextBlock { Text = "应用后暂停并从零生成，清空实时及固定画面。周期 1000–10000 ms，下降时长不超过吸气，死腔与上升时长之和须短于呼气；幅度 −1000～1000，仅改变 Resp，零幅度不代表气流停止。CO₂ 基线和呼气末目标为 0～80 mmHg，平台起始位于两者之间（精确到 0.01）。各时长须为正整数。CO₂ 管路滞后可填 0～5000 ms，独立于 2 秒采集处理延迟。展宽步长 0～500 ms，0 关闭；三路加权示意会额外产生一个步长的平均滞后。" });
            panel.Children.Add(_activeBreathConfiguration);
            panel.Children.Add(BreathConfigurationStatus);
        }
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
        QtMethod.SelectionChanged += (_, _) => { HeartRateInput.IsEnabled = QtcInput.IsEnabled = !_closed && QtMethod.SelectedIndex != 0; };
        ApplyEcgButton.Click += (_, _) => ApplyEcgConfiguration();
        ApplyBreathButton.Click += (_, _) => ApplyBreathConfiguration();
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
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes))).TakeLast(DemoSweepLayout.RetainedBlockCount).ToArray();
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
        if (control is not null) { Display(control); }
        UpdateStatus();
    }

    private void ToggleHold()
    {
        if (_blocks.Length == 0) { return; }
        if (_pinned is null)
        {
            // Decoded blocks are owned, never mutated and independent of retention.
            ((RawTrace)Trace).Hold();
            _pinned = _blocks;
        }
        else
        {
            RawTrace control = new(_blocks, _physiology, LiveFrontierNs, _projected);
            _pinned = null;
            Display(control);
        }
        UpdateStatus();
    }

    private void Reset() => Reset(UsesPulse);

    private void ApplyEcgConfiguration()
    {
        if (_closed || !_projected) { return; }
        try
        {
            ProjectedEcgDemoConfiguration configuration;
            if (QtMethod.SelectedIndex == 0) { configuration = ProjectedEcgDemoConfiguration.Default; }
            else
            {
                if (!int.TryParse(HeartRateInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int hr) ||
                    !int.TryParse(QtcInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int qtc))
                { throw new ArgumentException("Integer input required."); }
                string method = QtMethod.SelectedIndex switch
                {
                    1 => EcgQtCorrection.Bazett,
                    2 => EcgQtCorrection.Fridericia,
                    _ => throw new ArgumentException("Unknown method."),
                };
                configuration = new(hr, qtc, method);
            }
            Reset(UsesPulse, configuration);
        }
        catch (ArgumentException)
        {
            EcgConfigurationStatus.Text = "未应用：请输入范围内的整数，并确保 QT 容纳 QRS、T 且不进入下一 P 波。当前数据与扫屏状态保持。";
        }
    }

    private void ApplyBreathConfiguration()
    {
        if (_closed || !_physiology) { return; }
        try
        {
            if (!int.TryParse(BreathPeriodInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int period) ||
                !int.TryParse(InspirationInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inspiration) ||
                !int.TryParse(RespAmplitudeInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amplitude) ||
                !int.TryParse(Co2BaselineInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int baseline) ||
                !int.TryParse(Co2EndInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int end) ||
                !int.TryParse(Co2DeadSpaceInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int deadSpace) ||
                !int.TryParse(Co2RiseInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rise) ||
                !int.TryParse(Co2FallInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fall) ||
                !int.TryParse(Co2TransportInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int transport) ||
                !int.TryParse(Co2DispersionInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dispersion))
            { throw new ArgumentException("Integer input required."); }
            int? plateau = null;
            if (!string.IsNullOrWhiteSpace(Co2PlateauInput.Text))
            {
                if (!decimal.TryParse(Co2PlateauInput.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal mmHg) ||
                    mmHg < 0 || mmHg > 80 || decimal.Truncate(mmHg * 100) != mmHg * 100)
                { throw new ArgumentException("CO2 plateau must be representable in centi-mmHg."); }
                plateau = (int)(mmHg * 100);
            }
            Reset(UsesPulse, breathConfiguration: new(period, inspiration, amplitude, plateau, baseline, end, deadSpace, rise, fall, transport, dispersion));
        }
        catch (ArgumentException)
        {
            BreathConfigurationStatus.Text = "未应用：请检查参数范围；基线 ≤ 平台起始 ≤ 呼气末目标，各时长须为正，下降不超过吸气，死腔＋上升须短于呼气。平台精确到 0.01 mmHg 或留空，管路滞后为 0～5000 ms，展宽步长为 0～500 ms。当前状态保持。";
        }
    }

    private void Reset(bool pulse, ProjectedEcgDemoConfiguration? configuration = null, PhysiologyDemoConfiguration? breathConfiguration = null)
    {
        configuration ??= EcgConfiguration;
        breathConfiguration ??= BreathConfiguration;
        PeriodicWaveformGroup source = CreateSource(pulse);
        PhysiologyWaveformGroup? eventSource = _physiology ? PhysiologyDemoSource.Create(breathConfiguration) : null;
        ElectrodeWaveformGroup? electrodeSource = _projected ? ProjectedEcgDemoSource.Create(configuration) : null;
        RawTrace empty = new([], _physiology, projected: _projected);
        Pause();
        _source = source;
        _physiologySource = eventSource;
        _electrodeSource = electrodeSource;
        EcgConfiguration = configuration;
        BreathConfiguration = breathConfiguration;
        if (_physiology)
        {
            BreathPeriodInput.Text = breathConfiguration.BreathPeriodMilliseconds.ToString(CultureInfo.InvariantCulture);
            InspirationInput.Text = breathConfiguration.InspirationMilliseconds.ToString(CultureInfo.InvariantCulture);
            RespAmplitudeInput.Text = breathConfiguration.RespAmplitudeCounts.ToString(CultureInfo.InvariantCulture);
            Co2BaselineInput.Text = breathConfiguration.Co2BaselineMmHg.ToString(CultureInfo.InvariantCulture);
            Co2EndInput.Text = breathConfiguration.Co2EndExpiratoryMmHg.ToString(CultureInfo.InvariantCulture);
            Co2DeadSpaceInput.Text = breathConfiguration.Co2DeadSpaceMilliseconds.ToString(CultureInfo.InvariantCulture);
            Co2RiseInput.Text = breathConfiguration.Co2RiseMilliseconds.ToString(CultureInfo.InvariantCulture);
            Co2DispersionInput.Text = breathConfiguration.Co2DispersionStepMilliseconds.ToString(CultureInfo.InvariantCulture);
            Co2TransportInput.Text = breathConfiguration.Co2TransportDelayMilliseconds.ToString(CultureInfo.InvariantCulture);
            Co2FallInput.Text = breathConfiguration.Co2FallMilliseconds.ToString(CultureInfo.InvariantCulture);
            Co2PlateauInput.Text = breathConfiguration.Co2PlateauStartCentiMmHg is { } plateau
                ? (plateau / 100m).ToString("0.##", CultureInfo.InvariantCulture) : "";
            _activeBreathConfiguration.Text = string.Create(CultureInfo.InvariantCulture,
                $"已应用：周期 {breathConfiguration.BreathPeriodMilliseconds} ms；吸气/呼气 {breathConfiguration.InspirationMilliseconds}/{breathConfiguration.BreathPeriodMilliseconds - breathConfiguration.InspirationMilliseconds} ms；Resp 幅度 {breathConfiguration.RespAmplitudeCounts}。Resp、CO₂、CVP 共用呼吸时序；不是测得的 RR。CO₂ 平台起始 {(breathConfiguration.Co2PlateauStartCentiMmHg is null ? "参考比例" : Co2PlateauInput.Text + " mmHg")}，基线/呼气末目标 {breathConfiguration.Co2BaselineMmHg}/{breathConfiguration.Co2EndExpiratoryMmHg} mmHg；死腔/上升/下降 {breathConfiguration.Co2DeadSpaceMilliseconds}/{breathConfiguration.Co2RiseMilliseconds}/{breathConfiguration.Co2FallMilliseconds} ms；CO₂ 管路滞后 {breathConfiguration.Co2TransportDelayMilliseconds} ms；展宽步长 {breathConfiguration.Co2DispersionStepMilliseconds} ms。");
            BreathConfigurationStatus.Text = "";
        }
        if (_projected)
        {
            var timing = configuration.ResolveTiming();
            QtMethod.SelectedIndex = configuration.MethodId switch { EcgQtCorrection.Bazett => 1, EcgQtCorrection.Fridericia => 2, _ => 0 };
            HeartRateInput.Text = configuration.HeartRateBpm.ToString(CultureInfo.InvariantCulture);
            QtcInput.Text = configuration.QtcMilliseconds.ToString(CultureInfo.InvariantCulture);
            _activeEcgConfiguration.Text = string.Create(CultureInfo.InvariantCulture,
                $"已应用：源心率 {configuration.HeartRateBpm} 次/分；{configuration.MethodId ?? "固定示意（不使用 QTc）"}；RR {timing.RrIntervalNs / 1_000_000m:0.###} ms；QT {timing.QtIntervalNs / 1_000_000m:0.###} ms") +
                (configuration.MethodId is null ? "" : $"；QTc {configuration.QtcMilliseconds} ms");
            EcgConfigurationStatus.Text = "";
        }
        UsesPulse = pulse;
        ShapeButton.Content = pulse ? "形状：双相脉冲（点击切换并重置）" : "形状：三角波（点击切换并重置）";
        _blocks = [];
        _pinned = null;
        SimulationTimeNs = 0;
        LiveFrontierNs = 0;
        Display(empty);
        UpdateStatus();
    }

    private void Display(RawTrace control)
    {
        control.SizeChanged += (_, _) => UpdateStatus();
        _trace.Content = control;
    }

    private void UpdateStatus()
    {
        HoldButton.IsEnabled = _blocks.Length > 0;
        HoldButton.Content = IsHeld ? "返回最新数据" : "固定当前画面";
        _status.Text = DisplayBlocks.Length == 0
            ? $"数据模拟时间 {SimulationTimeNs / 1_000_000} ms；等待完整通道块"
            : $"数据模拟时间 {SimulationTimeNs / 1_000_000} ms；{(IsHeld ? "固定画面（步进仍生成后台数据）" : "最新数据")}；显示时窗 {DisplayDurationNs / 1_000_000} ms；源区间 [{DisplayStartNs / 1_000_000}, {((RawTrace)Trace).SnapshotFrontierNs / 1_000_000}) ms";
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

    private sealed class RawTrace : Control
    {
        private (Point Start, Point End, int Channel)[] _segments = [];
        private double? _gapStart;
        private readonly bool _resp;
        private readonly bool _projected;
        private readonly WaveformEnvelope[] _blocks;
        internal long SnapshotFrontierNs { get; }
        private long? _heldDurationNs;
        private long _builtDurationNs;
        private int PlotWidth => _projected ? ProjectedEcgPlotLayout.Resolve(Bounds.Width)?.PlotWidth ?? 0
            : DemoSweepLayout.PlotWidth(Bounds.Width);
        internal long DurationNs => _heldDurationNs ?? (PlotWidth > 0
            ? PlotWidth * DemoSweepLayout.NanosecondsPerPixel : DemoSweepLayout.ReferenceDurationNs);
        internal long? SourceStartNs => _blocks.Length == 0 ? null : Math.Max(_blocks[0].StartSimTimeNs, SnapshotFrontierNs - DurationNs);
        internal void Hold() => _heldDurationNs = DurationNs;

        public RawTrace(WaveformEnvelope[] blocks, bool resp, long frontier = 0, bool projected = false)
        {
            _resp = resp;
            _projected = projected;
            _blocks = blocks;
            SnapshotFrontierNs = frontier;
            Height = projected ? 12 * ProjectedEcgPlotLayout.RowHeight : resp ? 840 : 240;
        }

        private void Rebuild()
        {
            if (_builtDurationNs == DurationNs) { return; }
            WaveformEnvelope[] blocks = _blocks.Where(block => block.StartSimTimeNs + 200_000_000 >= SnapshotFrontierNs - DurationNs &&
                block.StartSimTimeNs <= SnapshotFrontierNs).ToArray();
            long frontier = SnapshotFrontierNs;
            bool projected = _projected, resp = _resp;
            List<(Point, Point, int)> segments = [];
            if (blocks.Length > 0)
            {
                _gapStart = (frontier % DurationNs) / (double)DurationNs;
                for (int channel = 0; channel < (projected ? 12 : resp ? 7 : 2); channel++)
                {
                    Point? previous = null;
                    long previousTime = 0;
                    foreach (WaveformEnvelope block in blocks)
                    {
                        WaveformPlane plane = projected ? block.Planes.Single(item => item.ChannelId == ProjectedEcgDemoSource.ChannelId((EcgLead)channel)) : resp
                            ? block.Planes.Single(item => item.ChannelId == PhysiologyDemoSource.ChannelId(channel)) : block.Planes[channel];
                        for (int index = 0; index < plane.Samples.Count; index++)
                        {
                            // Floating point is confined to terminal screen coordinates.
                            // Fixture blocks are 200ms aligned from epoch zero.
                            // Reduce integer time before terminal pixel conversion.
                            long sourceTime = block.StartSimTimeNs + index * 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
                            double time = sourceTime % DurationNs;
                            double y;
                            if (projected)
                            {
                                var position = EcgVerticalGeometry.MapMicrovolts(ProjectedEcgPlotLayout.VerticalScale(channel), plane.Samples[index], 1);
                                y = (double)position.PixelNumerator / (double)position.PixelDenominator;
                            }
                            else if (resp && channel >= 3)
                            {
                                // Terminal display conversion; raw counts are hundredths
                                // above the wire baseline, not absolute pressure in mmHg.
                                double pressureMmHg = (double)plane.Samples[index] * plane.ScaleNumerator / plane.ScaleDenominator +
                                    (double)plane.OffsetNumerator / plane.OffsetDenominator;
                                double minimum = channel == 6 ? -5 : 0;
                                double maximum = channel == 3 ? 160 : channel == 4 ? 80 : channel == 5 ? 40 : 15;
                                y = channel * 120 + 110 - (pressureMmHg - minimum) * (100 / (maximum - minimum));
                            }
                            else { y = channel * 120 + 60 - plane.Samples[index] * 0.05; }
                            Point point = new(time / DurationNs, y);
                            if (previous is { } start && point.X > start.X &&
                                sourceTime > frontier - DurationNs && previousTime < frontier)
                            {
                                // Clip only a segment between two acquired samples; never extrapolate.
                                long from = Math.Max(previousTime, frontier - DurationNs);
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
            _builtDurationNs = DurationNs;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(Brushes.Black, Bounds.WithX(0).WithY(0));
            using (context.PushClip(new Rect(Bounds.Size)))
            {
                var layout = _projected ? ProjectedEcgPlotLayout.Resolve(Bounds.Width) : null;
                if (_projected && layout is null) { return; }
                double left = layout?.PlotLeft ?? 0;
                if (PlotWidth == 0) { return; }
                Rebuild();
                double width = DurationNs / (double)DemoSweepLayout.NanosecondsPerPixel;
                Pen first = new(Brushes.Lime, 1);
                Pen second = new(_resp ? Brushes.Yellow : Brushes.Cyan, 1);
                Pen third = new(Brushes.Cyan, 1);
                Pen fourth = new(Brushes.Red, 1);
                Pen fifth = new(Brushes.White, 1);
                Pen sixth = new(Brushes.Magenta, 1);
                Pen seventh = new(Brushes.Orange, 1);
                using (context.PushClip(new Rect(left, 0, Math.Min(PlotWidth, width), Bounds.Height)))
                {
                    foreach (var segment in _segments)
                    {
                        context.DrawLine(_projected || segment.Channel == 0 ? first : segment.Channel == 1 ? second : segment.Channel == 2 ? third : segment.Channel == 3 ? fourth : segment.Channel == 4 ? fifth : segment.Channel == 5 ? sixth : seventh,
                            new(left + segment.Start.X * width, segment.Start.Y),
                            new(left + segment.End.X * width, segment.End.Y));
                    }
                }
                if (_projected)
                {
                    for (int lead = 0; lead < 12; lead++)
                    {
                        var label = new FormattedText(ProjectedEcgDemoSource.LeadNames[lead], CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, new Typeface("sans-serif"), 12, Brushes.Lime);
                        context.DrawText(label, new Point(3, lead * ProjectedEcgPlotLayout.RowHeight + ProjectedEcgPlotLayout.RowHeight / 2 - 10));
                        var points = layout!.Calibration(lead).Points;
                        Point Pixel(EcgCalibrationPoint point) => new(point.X.WholePixels +
                            (double)point.X.FractionNumerator / point.X.FractionDenominator,
                            (double)point.Y.PixelNumerator / (double)point.Y.PixelDenominator);
                        for (int index = 1; index < points.Count; index++)
                        { context.DrawLine(first, Pixel(points[index - 1]), Pixel(points[index])); }
                    }
                }
                if (_gapStart is { } start)
                {
                    double end = start + 200_000_000.0 / DurationNs;
                    context.FillRectangle(Brushes.Black, new Rect(left + start * width, 0,
                        (Math.Min(1, end) - start) * width, Bounds.Height));
                    if (end > 1)
                    { context.FillRectangle(Brushes.Black, new Rect(left, 0, (end - 1) * width, Bounds.Height)); }
                }
            }
        }
    }
}
