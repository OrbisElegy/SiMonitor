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
    internal ComboBox LimbPlacementInput { get; } = new() { ItemsSource = new[] { "标准接线", "RA ↔ LA（右臂／左臂）", "RA ↔ LL（右臂／左腿）", "LA ↔ LL（左臂／左腿）" }, SelectedIndex = 0 };
    internal ComboBox QtMethod { get; } = new() { ItemsSource = new[] { "原始示意时序", "Bazett", "Fridericia" }, SelectedIndex = 0 };
    internal ComboBox MechanicalEveryCyclesInput { get; } = new() { ItemsSource = new[] { "每个室性周期", "每 2 个室性周期", "每 3 个室性周期", "每 4 个室性周期" }, SelectedIndex = 0 };
    internal TextBox MechanicalDurationCyclesInput { get; } = new() { Text = "", Width = 65 };
    internal TextBox MechanicalAfterCyclesInput { get; } = new() { Text = "", Width = 65 };
    internal CheckBox VentricularMechanicalInput { get; } = new() { Content = "生成室性机械事件", IsChecked = true };
    internal CheckBox VascularReservoirInput { get; } = new() { Content = "血管储压模型（教学）", IsChecked = true };
    internal TextBlock VascularPressureModeStatus { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal ComboBox CardiacActivityInput { get; } = new() { ItemsSource = new[] { "心房与心室事件", "仅心房事件", "无心脏事件", "仅心室事件" }, SelectedIndex = 0 };
    internal TextBox IndependentVentricularOffsetInput { get; } = new() { Width = 75 };
    internal TextBox IndependentVentricularPeriodInput { get; } = new() { Width = 75 };
    internal ComboBox ConductionInput { get; } = new() { ItemsSource = new[] { "1:1", "2:1", "3:1", "4:1" }, SelectedIndex = 0 };
    internal TextBox HeartRateInput { get; } = new() { Text = "75", Width = 70, IsEnabled = false };
    internal TextBox PDurationInput { get; } = new() { Text = "100", Width = 65, IsEnabled = false };
    internal TextBox PrIntervalInput { get; } = new() { Text = "160", Width = 65, IsEnabled = false };
    internal TextBox QrsDurationInput { get; } = new() { Text = "80", Width = 65, IsEnabled = false };
    internal TextBox TDurationInput { get; } = new() { Text = "180", Width = 65, IsEnabled = false };
    internal TextBox UDelayInput { get; } = new() { Text = "30", Width = 65 };
    internal TextBox UDurationInput { get; } = new() { Text = "120", Width = 65 };
    internal TextBox ChestJInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox ChestStEndInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox TPeakInput { get; } = new() { Width = 65 };
    internal TextBox[] TScaleInputs { get; } = Enumerable.Range(0, 6).Select(_ => new TextBox { Text = "1", Width = 65 }).ToArray();
    internal TextBox[] UAmplitudeInputs { get; } = Enumerable.Range(0, 6)
        .Select(_ => new TextBox { Text = "0", Width = 65 }).ToArray();
    internal TextBox QtcInput { get; } = new() { Text = "400", Width = 70, IsEnabled = false };
    internal Button ApplyEcgButton { get; } = new() { Content = "应用并重新开始" };
    internal TextBlock EcgConfigurationStatus { get; } = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _activeEcgConfiguration = new() { TextWrapping = TextWrapping.Wrap };
    private PhysiologyWaveformGroup? _physiologySource;
    internal PhysiologyDemoConfiguration BreathConfiguration { get; private set; } = PhysiologyDemoConfiguration.Default;
    internal TextBox ActivityDurationBreathsInput { get; } = new() { Text = "", Width = 75 };
    internal TextBox ActivityAfterBreathsInput { get; } = new() { Text = "", Width = 75 };
    internal ComboBox RespiratoryActivityInput { get; } = new()
    { ItemsSource = new[] { "正常呼吸", "仅胸廓努力", "无呼吸分量" }, SelectedIndex = 0 };
    internal TextBox BreathPeriodInput { get; } = new() { Text = "3750", Width = 75 };
    internal TextBox InspirationInput { get; } = new() { Text = "1875", Width = 75 };
    internal TextBox ExpiratoryPauseInput { get; } = new() { Text = "0", Width = 75 };
    internal TextBox InspiratoryPauseInput { get; } = new() { Text = "0", Width = 75 };
    internal TextBox RespCardiacArtifactInput { get; } = new() { Text = "0", Width = 75 };
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
            panel.Children.Add(new TextBlock { Text = "第六行 PA（紫红）：125 Hz / 80 ms；固定尺度 0–40 mmHg。使用独立肺动脉压参数，未计算 PAP SYS/DIA/MEAN。" });
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
        if (projected || physiology)
        {
            WrapPanel conduction = new();
            conduction.Children.Add(new TextBlock { Text = "基础周期:心室周期比例" });
            conduction.Children.Add(ConductionInput);
            conduction.Children.Add(new TextBlock { Text = "心脏源活动" });
            conduction.Children.Add(CardiacActivityInput);
            ToolTip.SetTip(CardiacActivityInput, "仅心室模式关闭房性电／机械事件；心室沿原周期与偏移运行。基础周期率不是测得的心房率；不代表已验证的逸搏或房颤预设。");
            conduction.Children.Add(new TextBlock { Text = "独立心室周期（ms，可空）" });
            conduction.Children.Add(IndependentVentricularPeriodInput);
            conduction.Children.Add(new TextBlock { Text = "首次 QRS 起始偏移（ms，可空）" });
            conduction.Children.Add(IndependentVentricularOffsetInput);
            ToolTip.SetTip(IndependentVentricularOffsetInput, "仅在独立心室周期下可用，从源时间零计；≥0，偏移＋80 ms 须短于心室周期。留空沿用 PR 偏移。房性事件不移动，室性机械事件比 QRS 晚 80 ms；QT/U 时限不变。");
            ToolTip.SetTip(IndependentVentricularPeriodInput, "800～3200 ms，不短于基础心房周期，需选择比例 1:1；留空沿用比例传导。独立模式且起始偏移留空时 PR 字段控制首次 QRS 偏移，后续 P 与 QRS 不保持固定间隔；QTc 使用心室 RR。不会自动生成炮击样 a 波或逸搏形态。");
            if (physiology)
            {
                conduction.Children.Add(VentricularMechanicalInput);
                conduction.Children.Add(new TextBlock { Text = "机械搏动一次／" });
                conduction.Children.Add(MechanicalEveryCyclesInput);
                conduction.Children.Add(new TextBlock { Text = "先完成室性周期数（可空）" });
                conduction.Children.Add(MechanicalAfterCyclesInput);
                conduction.Children.Add(new TextBlock { Text = "机械停止持续周期数（可空）" });
                conduction.Children.Add(MechanicalDurationCyclesInput);
                conduction.Children.Add(VascularReservoirInput);
                ToolTip.SetTip(VascularReservoirInput, "ABP／PA 保留各自的上升支、切迹与下降支，并加入血管储压：无射血时压力衰减，恢复后从残余压力逐搏充盈。取消后使用固定基线形态模板。应用后从源时间零重新开始。");
            }
            panel.Children.Add(conduction);
            if (physiology)
            {
                panel.Children.Add(new TextBlock { Text = "关闭室性机械事件可保留 ECG 电活动，同时停止新的 Pleth／ABP／PA 射血输入及室性 CVP 分量。可在关闭时填写先完成周期数 1～100（需心房与心室事件），先生成这些周期再停止新机械事件，已有波尾继续；留空从零关闭。填写停止持续周期数 1～100 可在原时序上恢复，需先设置完成周期数；持续数留空则不恢复。可选每 N 个室性周期触发一次机械搏动，从第一个周期计数；恢复沿用原周期位置，不重新计数。" });
                panel.Children.Add(VascularPressureModeStatus);
            }
            panel.Children.Add(new TextBlock { Text = "仅心房事件保留 P 波与房性机械分量；无心脏事件停止全部心脏源事件。呼吸独立继续；所示时限与频率是源配置，未模拟灌注或测量有效性。" });
        }
        if (projected)
        {
            WrapPanel settings = new();
            settings.Children.Add(new TextBlock { Text = "肢体电极接线" });
            settings.Children.Add(LimbPlacementInput);
            ToolTip.SetTip(LimbPlacementInput, "仅交换肢体电极连接；潜在心脏事件、采样时钟与胸前电极保持不变。应用后从零重新生成，不表示导联脱落。");
            settings.Children.Add(QtMethod);
            settings.Children.Add(new TextBlock { Text = "基础周期率（次/分）" });
            settings.Children.Add(HeartRateInput);
            settings.Children.Add(new TextBlock { Text = "QTc（ms）" });
            settings.Children.Add(QtcInput);
            foreach (var (label, input) in new[] { ("P 时限（ms）", PDurationInput), ("PR／首次 QRS 偏移（ms）", PrIntervalInput),
                ("QRS 时限（ms）", QrsDurationInput), ("T 时限（ms）", TDurationInput) })
            {
                settings.Children.Add(new TextBlock { Text = label });
                settings.Children.Add(input);
            }
            settings.Children.Add(new TextBlock { Text = "u 波：T 后延迟（ms）" });
            settings.Children.Add(UDelayInput);
            settings.Children.Add(new TextBlock { Text = "u 时限（ms）" });
            settings.Children.Add(UDurationInput);
            for (int index = 0; index < UAmplitudeInputs.Length; index++)
            {
                settings.Children.Add(new TextBlock { Text = $"V{index + 1} u（μV）" });
                settings.Children.Add(UAmplitudeInputs[index]);
            }
            for (int index = 0; index < TScaleInputs.Length; index++)
            {
                settings.Children.Add(new TextBlock { Text = $"C{index + 1} T 倍率" });
                settings.Children.Add(TScaleInputs[index]);
            }
            settings.Children.Add(new TextBlock { Text = "T 峰位置（%，可空）" });
            settings.Children.Add(TPeakInput);
            settings.Children.Add(new TextBlock { Text = "胸前电极 J 偏移（μV）" });
            settings.Children.Add(ChestJInput);
            settings.Children.Add(new TextBlock { Text = "ST 末端偏移（μV）" });
            settings.Children.Add(ChestStEndInput);
            settings.Children.Add(ApplyEcgButton);
            foreach (Control item in settings.Children) { item.Margin = new Thickness(0, 0, 8, 8); }
            panel.Children.Add(settings);
            panel.Children.Add(new TextBlock { Text = "应用后暂停扫屏并清空当前及固定画面，从零生成。源心房率不是检测心率；QTc 使用传导后的室性 RR。固定示意模式保留原 QT。输入范围 30–200 次/分，QTc 与各时限 1–1000 ms；P ≤ PR、QRS＋T ≤ QT、PR＋QT ≤ 室性 RR。高心率可按需要缩短各波段，时限不再固定为参考值。" });
            panel.Children.Add(new TextBlock { Text = "u 波幅度 −1000～1000 μV，全部为 0 时关闭；T 后延迟 0～1000 ms，时限 1～1000 ms。启用时须满足 PR＋QT＋u 延迟＋u 时限 ≤ 室性 RR；不计入 QT。此处仅添加胸前 u 波，肢体导联不变，不自动按心率调整幅度。" });
            panel.Children.Add(new TextBlock { Text = "胸前电极 T 波手动倍率：−4～4，精度 0.001；1 保留参考，0 去除该电极 T 分量，负值反转极性。投影前调整，肢体电极不变；不改变 P/QRS/QT/u，不代表正常或病理预设。T 峰位置为 T 自身时限的 10～90%，精度 0.1%；所有电极共用，留空使用参考 62.5%，不改变 T 时限或 QT。" });
            panel.Children.Add(new TextBlock { Text = "手动 J/ST 偏移 −1000～1000 μV，共同作用于 C1～C6，肢体电极不变。附加电位从 QRS 最后四分之一平滑升起，在 T 起点达到 ST 末端值，再随 T 时限回到零；会改变 QRS 末端及 T 上的基线，不改变 QT/u。非病理预设；两值为 0 关闭，启用时需有非零 ST 时限。" });
            panel.Children.Add(_activeEcgConfiguration);
            panel.Children.Add(EcgConfigurationStatus);
        }
        if (_physiology)
        {
            WrapPanel settings = new();
            settings.Children.Add(new TextBlock { Text = "呼吸周期（ms）" });
            settings.Children.Add(BreathPeriodInput);
            settings.Children.Add(new TextBlock { Text = "吸气总时长（ms，含停顿）" });
            settings.Children.Add(InspirationInput);
            settings.Children.Add(new TextBlock { Text = "呼吸活动" });
            settings.Children.Add(RespiratoryActivityInput);
            settings.Children.Add(new TextBlock { Text = "先完成呼吸次数（留空立即）" });
            settings.Children.Add(ActivityAfterBreathsInput);
            settings.Children.Add(new TextBlock { Text = "状态持续周期数（留空不恢复）" });
            settings.Children.Add(ActivityDurationBreathsInput);
            settings.Children.Add(new TextBlock { Text = "吸气末停顿（ms）" });
            settings.Children.Add(InspiratoryPauseInput);
            settings.Children.Add(new TextBlock { Text = "呼气末停顿（ms）" });
            settings.Children.Add(ExpiratoryPauseInput);
            settings.Children.Add(new TextBlock { Text = "Resp 相对幅度（可负）" });
            settings.Children.Add(RespAmplitudeInput);
            settings.Children.Add(new TextBlock { Text = "Resp 心源伪差幅度（可负）" });
            settings.Children.Add(RespCardiacArtifactInput);
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
            panel.Children.Add(new TextBlock { Text = "应用后暂停并从零生成，清空实时及固定画面。仅胸廓努力：保留 Resp/CVP 呼吸分量，CO₂ 保持配置基线；无呼吸分量：停止胸廓周期，心源伪差和心搏仍可继续。先完成次数可填 1～100（仅用于后两种状态），届时连续改变源状态并保留 CO₂ 尾部；留空则从零采用所选状态。设置先完成次数后，还可填状态持续周期数 1～100，届时恢复正常呼吸；留空不恢复。此为信号源状态，不是测得的呼吸暂停。周期 1000–10000 ms；吸气末停顿包含在吸气总时长内，须 ≥0 且短于吸气总时长，0 关闭。呼气末停顿包含在呼气总时长内，须 ≥0 且短于呼气总时长，保持 Resp 和 CVP 呼吸分量的基线。两种停顿均不改变 CO₂ 时程。下降时长不超过吸气，死腔与上升时长之和须短于呼气；幅度 −1000～1000，仅改变 Resp，零幅度不代表气流停止。Resp 心源伪差幅度 −200～200，0 关闭；随心搏叠加，不能据此计为有效呼吸。CO₂ 基线和呼气末目标为 0～80 mmHg，平台起始位于两者之间（精确到 0.01）。各时长须为正整数。CO₂ 管路滞后可填 0～5000 ms，独立于 2 秒采集处理延迟。展宽步长 0～500 ms，0 关闭；三路加权示意会额外产生一个步长的平均滞后。" });
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
        QtMethod.SelectionChanged += (_, _) => { HeartRateInput.IsEnabled = QtcInput.IsEnabled = PDurationInput.IsEnabled = PrIntervalInput.IsEnabled = QrsDurationInput.IsEnabled = TDurationInput.IsEnabled = !_closed && QtMethod.SelectedIndex != 0; };
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
        long delta = DemoFrameTiming.ResolveElapsed(Stopwatch.GetElapsedTime(_lastTick, now));
        _lastTick = now;
        Pulse(sender, delta);
    }

    internal void Pulse(object? timer, long deltaNs = 16_000_000)
    {
        if (_closed || _timer is null || !ReferenceEquals(timer, _timer)) { return; }
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaNs);
            while (deltaNs > 0)
            {
                long chunk = Math.Min(deltaNs, DemoFrameTiming.MaximumChunkNs);
                Advance(chunk, progressive: true);
                deltaNs -= chunk;
            }
        }
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
        PhysiologyWaveformGroup? eventTrial = _physiology ? _physiologySource!.Fork() : null;
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
            if (QtMethod.SelectedIndex == 0) { configuration = ProjectedEcgDemoConfiguration.Default with { VentricularConductionRatio = ConductionInput.SelectedIndex + 1 }; }
            else
            {
                if (!int.TryParse(HeartRateInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int hr) ||
                    !int.TryParse(QtcInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int qtc) ||
                    !int.TryParse(PDurationInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int p) ||
                    !int.TryParse(PrIntervalInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int pr) ||
                    !int.TryParse(QrsDurationInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int qrs) ||
                    !int.TryParse(TDurationInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int t))
                { throw new ArgumentException("Integer input required."); }
                string method = QtMethod.SelectedIndex switch
                {
                    1 => EcgQtCorrection.Bazett,
                    2 => EcgQtCorrection.Fridericia,
                    _ => throw new ArgumentException("Unknown method."),
                };
                configuration = new(hr, qtc, method, ConductionInput.SelectedIndex + 1, p, pr, qrs, t);
            }
            if (!int.TryParse(UDelayInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int uDelay) ||
                !int.TryParse(UDurationInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int uDuration))
            { throw new ArgumentException("Integer U timing required."); }
            int[] amplitudes = new int[6];
            for (int index = 0; index < amplitudes.Length; index++)
            {
                if (!int.TryParse(UAmplitudeInputs[index].Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amplitudes[index]))
                { throw new ArgumentException("Signed integer U amplitude required."); }
            }
            ProjectedEcgUConfiguration u = new(uDelay, uDuration, amplitudes[0], amplitudes[1], amplitudes[2], amplitudes[3], amplitudes[4], amplitudes[5]);
            int? independentPeriod = null;
            if (!string.IsNullOrWhiteSpace(IndependentVentricularPeriodInput.Text))
            {
                if (!int.TryParse(IndependentVentricularPeriodInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPeriod))
                { throw new ArgumentException("Invalid independent ventricular period."); }
                independentPeriod = parsedPeriod;
            }
            int[] tScales = new int[6];
            for (int index = 0; index < tScales.Length; index++)
            {
                if (!decimal.TryParse(TScaleInputs[index].Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal scale) || scale is < -4 or > 4 || scale * 1000 != decimal.Truncate(scale * 1000))
                { throw new ArgumentException("Invalid T wave scale."); }
                tScales[index] = (int)(scale * 1000);
            }
            int? peakPermille = null;
            if (!string.IsNullOrWhiteSpace(TPeakInput.Text))
            {
                if (!decimal.TryParse(TPeakInput.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal peak) ||
                    peak is < 10 or > 90 || peak * 10 != decimal.Truncate(peak * 10))
                { throw new ArgumentException("Invalid T peak position."); }
                peakPermille = (int)(peak * 10);
            }
            ProjectedEcgTConfiguration tWave = new(tScales[0], tScales[1], tScales[2], tScales[3], tScales[4], tScales[5], peakPermille);
            if (!int.TryParse(ChestJInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int chestJ) ||
                !int.TryParse(ChestStEndInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int chestEnd))
            { throw new ArgumentException("Invalid ST offset."); }
            int? independentOffset = ParseIndependentVentricularOffset();
            configuration = configuration with { ChestJMicrovolts = chestJ, ChestStEndMicrovolts = chestEnd, TWave = tWave == ProjectedEcgTConfiguration.Default ? null : tWave, IndependentVentricularOffsetMilliseconds = independentOffset, IndependentVentricularPeriodMilliseconds = independentPeriod, UWave = u == ProjectedEcgUConfiguration.Default ? null : u, CardiacActivity = (CardiacActivity)CardiacActivityInput.SelectedIndex, Placement = (EcgLimbPlacement)LimbPlacementInput.SelectedIndex };
            Reset(UsesPulse, configuration);
        }
        catch (ArgumentException)
        {
            EcgConfigurationStatus.Text = "未应用：J/ST 偏移须为 −1000～1000 μV 整数，启用时 ST 时限须大于零；T 峰位置须为 10～90%，精度 0.1% 或留空；T 倍率须为 −4～4 且精度不超过 0.001；起始偏移需独立心室周期，且 ≥0、偏移＋80 ms < 心室周期；独立心室周期须为 800～3200 ms 且不短于基础心房周期，需比例 1:1，或留空；起始偏移留空时沿用 PR。请输入范围内的整数，传导比例须为 1:1～4:1，各时限为 1～1000 ms，并满足 P ≤ PR、QRS＋T ≤ QT、PR＋QT ≤ 室性 RR；u 波参数须在所示范围内，启用时 PR＋QT＋u 延迟＋u 时限 ≤ 室性 RR。当前数据与扫屏状态保持。";
        }
    }

    private int? ParseIndependentVentricularOffset()
    {
        if (string.IsNullOrWhiteSpace(IndependentVentricularOffsetInput.Text)) { return null; }
        if (!int.TryParse(IndependentVentricularOffsetInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        { throw new ArgumentException("Invalid ventricular offset."); }
        return value;
    }

    private void ApplyBreathConfiguration()
    {
        if (_closed || !_physiology) { return; }
        try
        {
            if (!int.TryParse(BreathPeriodInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int period) ||
                !int.TryParse(InspirationInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inspiration) ||
                !int.TryParse(InspiratoryPauseInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pause) ||
                !int.TryParse(ExpiratoryPauseInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expiratoryPause) ||
                !int.TryParse(RespAmplitudeInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amplitude) ||
                !int.TryParse(RespCardiacArtifactInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int artifact) ||
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
            int? afterBreaths = null;
            if (!string.IsNullOrWhiteSpace(ActivityAfterBreathsInput.Text))
            {
                if (!int.TryParse(ActivityAfterBreathsInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                { throw new ArgumentException("Integer breath count required."); }
                afterBreaths = count;
            }
            int? durationBreaths = null;
            if (!string.IsNullOrWhiteSpace(ActivityDurationBreathsInput.Text))
            {
                if (!int.TryParse(ActivityDurationBreathsInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int durationCount))
                { throw new ArgumentException("Integer duration count required."); }
                durationBreaths = durationCount;
            }
            if (VentricularMechanicalInput.IsChecked is not { } mechanicalEnabled)
            { throw new ArgumentException("Explicit ventricular mechanical selection required."); }
            if (VascularReservoirInput.IsChecked is not { } vascularReservoir)
            { throw new ArgumentException("Explicit vascular pressure model selection required."); }
            int? mechanicalAfter = null;
            if (!string.IsNullOrWhiteSpace(MechanicalAfterCyclesInput.Text))
            {
                if (!int.TryParse(MechanicalAfterCyclesInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int cycles))
                { throw new ArgumentException("Integer mechanical cycle count required."); }
                mechanicalAfter = cycles;
            }
            int? mechanicalDuration = null;
            if (!string.IsNullOrWhiteSpace(MechanicalDurationCyclesInput.Text))
            {
                if (!int.TryParse(MechanicalDurationCyclesInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int durationCycles))
                { throw new ArgumentException("Integer mechanical duration required."); }
                mechanicalDuration = durationCycles;
            }
            int? independentPeriod = null;
            if (!string.IsNullOrWhiteSpace(IndependentVentricularPeriodInput.Text))
            {
                if (!int.TryParse(IndependentVentricularPeriodInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPeriod))
                { throw new ArgumentException("Invalid independent ventricular period."); }
                independentPeriod = parsedPeriod;
            }
            Reset(UsesPulse, breathConfiguration: new(period, inspiration, amplitude, plateau, baseline, end, deadSpace, rise, fall, transport, dispersion, pause, expiratoryPause, artifact,
                (RespiratoryActivity)RespiratoryActivityInput.SelectedIndex, afterBreaths, durationBreaths, ConductionInput.SelectedIndex + 1, (CardiacActivity)CardiacActivityInput.SelectedIndex, mechanicalEnabled, mechanicalAfter, mechanicalDuration, MechanicalEveryCyclesInput.SelectedIndex + 1, vascularReservoir, independentPeriod, ParseIndependentVentricularOffset()));
        }
        catch (ArgumentException)
        {
            BreathConfigurationStatus.Text = "未应用：起始偏移需独立心室周期，且 ≥0、偏移＋80 ms < 心室周期；请检查参数范围；独立心室周期须为 800～3200 ms 或留空，设置时比例须为 1:1；机械搏动比例须为每 1～4 个室性周期一次；机械停止持续周期须为 1～100 或留空，且需先设置机械完成周期；机械先完成周期须为 1～100 或留空，需关闭室性机械事件并选择含心室事件的模式；先完成次数须为 1～100 或留空，且不能用于正常呼吸；恢复所需周期数须为 1～100 或留空，并先设置完成次数；Resp 心源伪差幅度为 −200～200；吸气／呼气末停顿须 ≥0 且短于各自总时长；基线 ≤ 平台起始 ≤ 呼气末目标，各时长须为正，下降不超过吸气，死腔＋上升须短于呼气。平台精确到 0.01 mmHg 或留空，管路滞后为 0～5000 ms，展宽步长为 0～500 ms。当前状态保持。";
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
            ConductionInput.SelectedIndex = breathConfiguration.VentricularConductionRatio - 1;
            IndependentVentricularOffsetInput.Text = breathConfiguration.IndependentVentricularOffsetMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            IndependentVentricularPeriodInput.Text = breathConfiguration.IndependentVentricularPeriodMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            CardiacActivityInput.SelectedIndex = (int)breathConfiguration.CardiacActivity;
            VentricularMechanicalInput.IsChecked = breathConfiguration.VentricularMechanicalEnabled;
            VascularReservoirInput.IsChecked = breathConfiguration.UseVascularReservoir;
            VascularPressureModeStatus.Text = breathConfiguration.UseVascularReservoir
                ? "已应用血管储压模型：ABP／PA 保留各自的上升支、切迹与下降支；停搏波尾结束后分别衰减至 10／5 mmHg，恢复后从残余压力逐搏充盈。形态与储压组合为教学近似，CVP 仍使用原有分量模型。"
                : "已应用固定基线形态模板：无新机械事件时 ABP／PA 波尾结束后保持 80／10 mmHg；启用血管储压模型可观察压力衰减与恢复。";
            MechanicalEveryCyclesInput.SelectedIndex = breathConfiguration.MechanicalEveryCycles - 1;
            MechanicalAfterCyclesInput.Text = breathConfiguration.MechanicalAfterCycles?.ToString(CultureInfo.InvariantCulture) ?? "";
            MechanicalDurationCyclesInput.Text = breathConfiguration.MechanicalDurationCycles?.ToString(CultureInfo.InvariantCulture) ?? "";
            ActivityDurationBreathsInput.Text = breathConfiguration.ActivityDurationBreaths?.ToString(CultureInfo.InvariantCulture) ?? "";
            ActivityAfterBreathsInput.Text = breathConfiguration.ActivityAfterBreaths?.ToString(CultureInfo.InvariantCulture) ?? "";
            RespiratoryActivityInput.SelectedIndex = (int)breathConfiguration.RespiratoryActivity;
            BreathPeriodInput.Text = breathConfiguration.BreathPeriodMilliseconds.ToString(CultureInfo.InvariantCulture);
            InspirationInput.Text = breathConfiguration.InspirationMilliseconds.ToString(CultureInfo.InvariantCulture);
            ExpiratoryPauseInput.Text = breathConfiguration.ExpiratoryPauseMilliseconds.ToString(CultureInfo.InvariantCulture);
            InspiratoryPauseInput.Text = breathConfiguration.InspiratoryPauseMilliseconds.ToString(CultureInfo.InvariantCulture);
            RespCardiacArtifactInput.Text = breathConfiguration.RespCardiacArtifactCounts.ToString(CultureInfo.InvariantCulture);
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
                $"已应用：{CardiacActivityInput.SelectedItem}；首次 QRS 偏移 {breathConfiguration.IndependentVentricularOffsetMilliseconds ?? 160} ms；独立心室周期 {breathConfiguration.IndependentVentricularPeriodMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "未启用"} ms；室性机械事件{(breathConfiguration.VentricularMechanicalEnabled ? "启用" : "关闭")}、每 {breathConfiguration.MechanicalEveryCycles} 个室性周期一次（先完成周期数 {MechanicalAfterCyclesInput.Text}，空为立即；停止持续周期数 {MechanicalDurationCyclesInput.Text}，空为不恢复）；传导 {breathConfiguration.VentricularConductionRatio}:1；目标呼吸活动 {RespiratoryActivityInput.SelectedItem}（先完成次数 {ActivityAfterBreathsInput.Text}，留空立即；状态持续周期 {ActivityDurationBreathsInput.Text}，留空不恢复）；周期 {breathConfiguration.BreathPeriodMilliseconds} ms；吸气/呼气 {breathConfiguration.InspirationMilliseconds}/{breathConfiguration.BreathPeriodMilliseconds - breathConfiguration.InspirationMilliseconds} ms（吸气／呼气末停顿 {breathConfiguration.InspiratoryPauseMilliseconds}/{breathConfiguration.ExpiratoryPauseMilliseconds} ms）；Resp 幅度 {breathConfiguration.RespAmplitudeCounts}，心源伪差幅度 {breathConfiguration.RespCardiacArtifactCounts}。Resp、CO₂、CVP 共用呼吸时序；不是测得的 RR。CO₂ 平台起始 {(breathConfiguration.Co2PlateauStartCentiMmHg is null ? "参考比例" : Co2PlateauInput.Text + " mmHg")}，基线/呼气末目标 {breathConfiguration.Co2BaselineMmHg}/{breathConfiguration.Co2EndExpiratoryMmHg} mmHg；死腔/上升/下降 {breathConfiguration.Co2DeadSpaceMilliseconds}/{breathConfiguration.Co2RiseMilliseconds}/{breathConfiguration.Co2FallMilliseconds} ms；CO₂ 管路滞后 {breathConfiguration.Co2TransportDelayMilliseconds} ms；展宽步长 {breathConfiguration.Co2DispersionStepMilliseconds} ms。");
            BreathConfigurationStatus.Text = "";
        }
        if (_projected)
        {
            var timing = configuration.ResolveTiming();
            LimbPlacementInput.SelectedIndex = (int)configuration.Placement;
            QtMethod.SelectedIndex = configuration.MethodId switch { EcgQtCorrection.Bazett => 1, EcgQtCorrection.Fridericia => 2, _ => 0 };
            ConductionInput.SelectedIndex = configuration.VentricularConductionRatio - 1;
            IndependentVentricularOffsetInput.Text = configuration.IndependentVentricularOffsetMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            IndependentVentricularPeriodInput.Text = configuration.IndependentVentricularPeriodMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            CardiacActivityInput.SelectedIndex = (int)configuration.CardiacActivity;
            HeartRateInput.Text = configuration.HeartRateBpm.ToString(CultureInfo.InvariantCulture);
            PDurationInput.Text = configuration.PDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
            PrIntervalInput.Text = configuration.PrIntervalMilliseconds.ToString(CultureInfo.InvariantCulture);
            QrsDurationInput.Text = configuration.QrsDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
            TDurationInput.Text = configuration.TDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
            QtcInput.Text = configuration.QtcMilliseconds.ToString(CultureInfo.InvariantCulture);
            var u = configuration.UWave ?? ProjectedEcgUConfiguration.Default;
            UDelayInput.Text = u.DelayMilliseconds.ToString(CultureInfo.InvariantCulture);
            UDurationInput.Text = u.DurationMilliseconds.ToString(CultureInfo.InvariantCulture);
            for (int index = 0; index < UAmplitudeInputs.Length; index++)
            { UAmplitudeInputs[index].Text = u.ChestAmplitudes[index].ToString(CultureInfo.InvariantCulture); }
            var tWave = configuration.TWave ?? ProjectedEcgTConfiguration.Default;
            ChestJInput.Text = configuration.ChestJMicrovolts.ToString(CultureInfo.InvariantCulture);
            ChestStEndInput.Text = configuration.ChestStEndMicrovolts.ToString(CultureInfo.InvariantCulture);
            TPeakInput.Text = tWave.PeakPositionPermille is { } peak ? (peak / 10m).ToString("0.#", CultureInfo.InvariantCulture) : "";
            for (int index = 0; index < TScaleInputs.Length; index++)
            { TScaleInputs[index].Text = (tWave.ChestScales[index] / 1000m).ToString("0.###", CultureInfo.InvariantCulture); }
            _activeEcgConfiguration.Text = string.Create(CultureInfo.InvariantCulture,
                $"已应用接线：{LimbPlacementInput.SelectedItem}；{CardiacActivityInput.SelectedItem}；基础周期率 {configuration.HeartRateBpm} 次/分；传导 {configuration.VentricularConductionRatio}:1；{configuration.MethodId ?? "固定示意（不使用 QTc）"}；RR {timing.RrIntervalNs / 1_000_000m:0.###} ms；P/PR/QRS/T {configuration.PDurationMilliseconds}/{configuration.PrIntervalMilliseconds}/{configuration.QrsDurationMilliseconds}/{configuration.TDurationMilliseconds} ms；QT {timing.QtIntervalNs / 1_000_000m:0.###} ms") +
                (configuration.IndependentVentricularPeriodMilliseconds is { } independent ? $"；独立心室周期 {independent} ms、首次 QRS 偏移 {configuration.IndependentVentricularOffsetMilliseconds ?? configuration.PrIntervalMilliseconds} ms（后续 P-QRS 间隔不固定）" : "") +
                (configuration.TWave is null ? "；T 参考倍率" : "；手动 C1–C6 T 倍率 " + string.Join("/", TScaleInputs.Select(input => input.Text))) +
                (configuration.ChestJMicrovolts == 0 && configuration.ChestStEndMicrovolts == 0 ? "；J/ST 附加电位关闭" : $"；手动胸前电极 J/ST 末端 {configuration.ChestJMicrovolts}/{configuration.ChestStEndMicrovolts} μV") +
                (tWave.PeakPositionPermille is null ? "；T 峰参考 62.5%" : $"；手动 T 峰 {TPeakInput.Text}%") +
                (configuration.MethodId is null ? "" : $"；QTc {configuration.QtcMilliseconds} ms") +
                (u.ChestAmplitudes.All(value => value == 0) ? "；u 波关闭" :
                    $"；u 延迟/时限 {u.DelayMilliseconds}/{u.DurationMilliseconds} ms，V1–V6 幅度 {string.Join("/", u.ChestAmplitudes)} μV");
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
                                // Terminal display conversion uses each plane's affine
                                // metadata for both template excursions and absolute RC pressure.
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
