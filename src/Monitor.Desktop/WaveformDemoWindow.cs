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
    internal CheckBox AfAberrancyInput { get; } = new() { Content = "房颤Ashman型差异传导示例", IsChecked = false };
    internal CheckBox AfPulseDeficitInput { get; } = new() { Content = "房颤脉搏短绌示例（体循环）", IsChecked = false };
    internal CheckBox VascularReservoirInput { get; } = new() { Content = "血管储压模型（教学）", IsChecked = true };
    internal TextBlock VascularPressureModeStatus { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal ComboBox CardiacActivityInput { get; } = new() { ItemsSource = new[] { "心房与心室事件", "仅心房事件", "无心脏事件", "仅心室事件" }, SelectedIndex = 0 };
    internal TextBox IndependentVentricularOffsetInput { get; } = new() { Width = 75 };
    internal TextBox IndependentVentricularPeriodInput { get; } = new() { Width = 75 };
    internal ComboBox ConductionInput { get; } = new() { ItemsSource = new[] { "1:1", "2:1", "3:1", "4:1", "3:2（固定PR）", "4:3（固定PR）", "4:3（文氏示意）", "三度AVB：交界性逸搏示意", "三度AVB：室性逸搏示意", "房扑2:1示意", "房扑4:1示意", "房颤粗颤示意", "房颤细颤示意", "室扑示意", "室颤粗颤示意", "室颤细颤示意", "3:2（文氏示意）", "5:4（文氏示意）", "二度Ⅱ型3:2示意", "二度Ⅱ型4:3示意", "二度Ⅱ型4:3＋完全RBBB", "二度Ⅱ型4:3＋完全LBBB", "房性期前收缩（正常下传）", "房性期前收缩（未下传）", "房早伴RBBB差异传导", "交界性早搏（逆行P′在前）", "交界性早搏（逆行P′在后）", "交界性早搏（P′与QRS重叠）", "室性期前收缩（单形）", "室早二联律", "室早三联律", "多形室早（固定联律示意）", "多源室早（不同联律示意）", "插入性室早（无代偿间歇）", "成对室早（单形示意）", "成对室早（双形态示意）", "R-on-T室早（长QT示意）", "R-on-T室早（短联律、无有效外周射血示意）", "房扑3:1示意", "房扑可变下传2:1→3:1→4:1示意" }, SelectedIndex = 0 };
    internal TextBox HeartRateInput { get; } = new() { Text = "75", Width = 70, IsEnabled = false };
    internal TextBox PDurationInput { get; } = new() { Text = "100", Width = 65, IsEnabled = false };
    internal TextBox PrIntervalInput { get; } = new() { Text = "160", Width = 65, IsEnabled = false };
    internal TextBox QrsDurationInput { get; } = new() { Text = "80", Width = 65, IsEnabled = false };
    internal TextBox TDurationInput { get; } = new() { Text = "180", Width = 65, IsEnabled = false };
    internal TextBox UDelayInput { get; } = new() { Text = "30", Width = 65 };
    internal TextBox UDurationInput { get; } = new() { Text = "120", Width = 65 };
    internal CheckBox SeparateZonesInput { get; } = new() { Content = "三区域组合（使用参考底板）", IsChecked = false };
    internal ComboBox IschemiaZoneInput { get; } = new() { ItemsSource = InfarctionZoneSelection.Names, SelectedIndex = 0 };
    internal ComboBox InjuryZoneInput { get; } = new() { ItemsSource = InfarctionZoneSelection.Names, SelectedIndex = 0 };
    internal ComboBox NecrosisZoneInput { get; } = new() { ItemsSource = InfarctionZoneSelection.Names, SelectedIndex = 0 };
    internal CheckBox IndependentComponentsInput { get; } = new() { Content = "独立组合（优先于阶段）", IsChecked = false };
    internal ComboBox NecrosisShapeInput { get; } = new() { SelectedIndex = 0, ItemsSource = new[] { "QRS 参考", "Q＋R 降低", "QS" } };
    internal TextBox QrsTemplateInput { get; } = new() { Text = "100", Width = 65 };
    internal CheckBox ContributionLossInput { get; } = new() { Content = "移除显式 QRS 贡献（需参考QRS及100%模板混合）", IsChecked = false };
    internal TextBox ContributionAmplitudeInput { get; } = new() { Text = "1200", Width = 65 };
    internal TextBox ContributionDurationInput { get; } = new() { Text = "75", Width = 65 };
    internal TextBox ContributionAmountInput { get; } = new() { Text = "100", Width = 65 };
    internal TextBox ComponentTInput { get; } = new() { Width = 65 };
    internal TextBox ComponentJInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox ComponentEndInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox ComponentArchInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox RepolarizationDelayInput { get; } = new() { Text = "0", Width = 65 };
    internal ComboBox InfarctionTerritoryInput { get; } = new()
    {
        SelectedIndex = 0,
        ItemsSource = new[] { "自选胸导联", "下壁：II/III/aVF", "侧壁：I/aVL/V5/V6",
            "前间壁：V1–V3", "前壁：V3–V5", "广泛前壁：V1–V5" }
    };
    internal CheckBox[] InfarctionInputs { get; } = Enumerable.Range(1, 6)
        .Select(i => new CheckBox { Content = $"V{i} 阶段示意", IsChecked = false }).ToArray();
    internal ComboBox InfarctionStageInput { get; } = new()
    {
        SelectedIndex = 0,
        ItemsSource = new[]
    { "关闭阶段示意", "超急性：高大 T", "超急性：QRS 增高增宽＋ST/T", "急性：QS＋单向曲线",
      "急性：Q/R 降低＋ST 抬高＋倒置 T", "急性：QS＋ST 抬高＋倒置 T", "亚急性：Q＋深倒置 T",
      "亚急性：Q＋倒置 T 变浅", "陈旧：Q＋参考 T", "陈旧：Q＋倒置 T", "陈旧：Q＋低平 T" }
    };
    internal CheckBox[] FusionInputs { get; } = Enumerable.Range(1, 6)
        .Select(i => new CheckBox { Content = $"V{i} 融合", IsChecked = false }).ToArray();
    internal TextBox FusionJInput { get; } = new() { Text = "200", Width = 65 };
    internal TextBox FusionPeakInput { get; } = new() { Text = "500", Width = 65 };
    internal TextBox FusionPositionInput { get; } = new() { Text = "50", Width = 65 };
    internal TextBox ChestStArchInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox ChestJInput { get; } = new() { Text = "0", Width = 65 };
    internal TextBox ChestStEndInput { get; } = new() { Text = "0", Width = 65 };
    internal CheckBox SvtInput { get; } = new() { Content = "规则窄QRS室上速示例（200次/分）" };
    internal Button SvtButton { get; } = new() { Content = "载入室上速（重置参数）" };
    internal CheckBox NormalPrDeltaInput { get; } = new() { Content = "正常／延长PR伴delta示例" };
    internal CheckBox ProlongedPrDeltaInput { get; } = new() { Content = "延长PR240ms（仅PR伴delta示例）" };
    internal Button NormalPrDeltaButton { get; } = new() { Content = "载入正常PR伴delta（重置参数）" };
    internal CheckBox ShortPrInput { get; } = new() { Content = "短PR／无delta示例" };
    internal Button ShortPrButton { get; } = new() { Content = "载入短PR／无delta（重置参数）" };
    internal CheckBox WpwInput { get; } = new() { Content = "WPW示例" };
    internal CheckBox WpwSmallerDeltaInput { get; } = new() { Content = "较小delta／QRS110ms（仅WPW）" };
    internal CheckBox WpwNegativeV1Input { get; } = new() { Content = "V1负向delta／QS（仅WPW）" };
    internal Button WpwButton { get; } = new() { Content = "载入WPW示例（重置参数）" };
    internal ComboBox QuinidineInput { get; } = new() { ItemsSource = new[] { "奎尼丁示例关闭", "奎尼丁样：低平T／增高u", "奎尼丁样：倒置T／增高u", "奎尼丁样：宽QRS／更长QT／低平T", "奎尼丁样：宽QRS／更长QT／倒置T" }, SelectedIndex = 0 };
    internal CheckBox QuinidineNotchedPInput { get; } = new() { Content = "奎尼丁样 P 波轻度切迹" };
    internal Button QuinidineButton { get; } = new() { Content = "载入奎尼丁效应（重置参数）" };
    internal ComboBox DigitalisShapeInput { get; } = new() { ItemsSource = new[] { "洋地黄：鱼钩型终末直立T", "洋地黄：低平T", "洋地黄：倒置T" }, SelectedIndex = 0 };
    internal CheckBox DigitalisInput { get; } = new() { Content = "洋地黄效应示例（非中毒判定）" };
    internal Button DigitalisButton { get; } = new() { Content = "载入洋地黄效应（重置参数）" };
    internal ComboBox CalciumInput { get; } = new() { ItemsSource = new[] { "钙相关示例关闭", "高钙样：短ST／短QT", "低钙样：长ST／长QT／窄T", "高钙样：ST消失", "低钙样：低平T", "低钙样：倒置T" }, SelectedIndex = 0 };
    internal Button CalciumButton { get; } = new() { Content = "载入钙相关示例（重置参数）" };
    internal CheckBox HypokalemiaConductionInput { get; } = new() { Content = "低钾 P增高／QRS增宽例（载入或应用）" };
    internal CheckBox HypokalemiaInvertedTInput { get; } = new() { Content = "低钾 T 波倒置（仅改变T分量）" };
    internal CheckBox HypokalemiaFusionInput { get; } = new() { Content = "低钾 T-u 融合" };
    internal CheckBox HypokalemiaInput { get; } = new() { Content = "低钾样复极示例" };
    internal Button HypokalemiaButton { get; } = new() { Content = "载入低钾样复极（重置参数）" };
    internal CheckBox HyperkalemiaFusionInput { get; } = new() { Content = "高钾 QRS–T 融合例（勾选后载入）" };
    internal CheckBox HyperkalemiaAbsentPInput { get; } = new() { Content = "高钾无P例（勾选后载入）" };
    internal CheckBox HyperkalemiaConductionInput { get; } = new() { Content = "高钾传导受损例（需载入或应用）" };
    internal CheckBox HyperkalemiaInput { get; } = new() { Content = "高钾样复极示例" };
    internal Button HyperkalemiaButton { get; } = new() { Content = "载入高钾样复极（重置参数）" };
    internal ComboBox TContourInput { get; } = new() { ItemsSource = new[] { "参考T / 原编辑", "正负双向T", "负正双向T", "双峰T", "对称倒置T（冠状T形态）", "高尖T", "高耸T", "单相正向T（可低平）", "普通倒置T" }, SelectedIndex = 0, Width = 160 };
    internal ComboBox TContourLeadInput { get; } = new() { ItemsSource = new[] { "V1", "V2", "V3", "V4", "V5", "V6", "V1–V6", "I", "II", "III", "aVR", "aVL", "aVF" }, SelectedIndex = 0, Width = 90 };
    internal TextBox TContourCrossingInput { get; } = new() { Width = 65 };
    internal TextBox TContourSecondPeakInput { get; } = new() { Width = 65 };
    internal TextBox TContourPeakInput { get; } = new() { Text = "300", Width = 65 };
    internal ComboBox VentricularInput { get; } = new() { ItemsSource = new[] { "参考心室波形", "左室肥厚伴 ST-T 改变示例", "右室肥厚伴 ST-T 改变示例", "双室肥厚组合征象示例", "重度右室肥厚 qR 示例", "肺源性心脏病 rS 形态示例" }, SelectedIndex = 0, Width = 240 };
    internal ComboBox AtrialInput { get; } = new() { ItemsSource = new[] { "参考 / 手动 P", "左房异常 P 波教学示例", "右房异常 P 波教学示例", "双房异常 P 波教学示例" }, SelectedIndex = 0, Width = 220 };
    internal TextBox PEarlyInput { get; } = new() { Width = 65 };
    internal TextBox PLateInput { get; } = new() { Width = 65 };
    internal TextBox TPeakInput { get; } = new() { Width = 65 };
    internal TextBox[] TScaleInputs { get; } = Enumerable.Range(0, 6).Select(_ => new TextBox { Text = "1", Width = 65 }).ToArray();
    internal TextBox[] UAmplitudeInputs { get; } = Enumerable.Range(0, 6)
        .Select(_ => new TextBox { Text = "0", Width = 65 }).ToArray();
    internal TextBox QtcInput { get; } = new() { Text = "400", Width = 70, IsEnabled = false };
    internal Button VentricularDisorganizationButton { get; } = new() { Content = "载入室扑／室颤示例（重置参数）" };
    internal ComboBox SecondDegreePresetInput { get; } = new() { ItemsSource = new[] { "二度Ⅰ型4:3", "二度Ⅰ型3:2", "二度Ⅰ型5:4", "二度Ⅱ型3:2（窄QRS）", "二度Ⅱ型4:3（窄QRS）", "二度2:1（不据比例分型）", "二度Ⅱ型4:3＋完全RBBB", "二度Ⅱ型4:3＋完全LBBB" }, SelectedIndex = 0 };
    internal ComboBox BundleBlockInput { get; } = new() { ItemsSource = new[] { "参考（无束支模板）", "完全RBBB（1:1）", "不完全RBBB（1:1）", "完全LBBB（1:1）", "不完全LBBB（1:1）", "左前分支阻滞（1:1）", "左后分支阻滞（1:1）" }, SelectedIndex = 0 };
    internal Button BundleBlockButton { get; } = new() { Content = "载入束支阻滞示例（重置参数）" };
    internal Button SecondDegreePresetButton { get; } = new() { Content = "载入二度阻滞示例（重置参数）" };
    internal Button PrematureVentricularButton { get; } = new() { Content = "载入单形室早示例（重置参数）" };
    internal Button PrematureJunctionalButton { get; } = new() { Content = "载入交界性早搏示例（重置参数）" };
    internal Button AberrantPrematureAtrialButton { get; } = new() { Content = "载入差异传导房早示例（重置参数）" };
    internal Button BlockedPrematureAtrialButton { get; } = new() { Content = "载入未下传房早示例（重置参数）" };
    internal Button PrematureAtrialButton { get; } = new() { Content = "载入正常下传房早示例（重置参数）" };
    internal Button FibrillationButton { get; } = new() { Content = "载入房颤粗颤示例（重置参数）" };
    internal Button FlutterButton { get; } = new() { Content = "载入房扑4:1示例（重置参数）" };
    internal Button VentricularEscapeButton { get; } = new() { Content = "载入三度AVB室性逸搏示例（重置参数）" };
    internal Button JunctionalEscapeButton { get; } = new() { Content = "载入三度AVB交界性逸搏示例（重置参数）" };
    internal Button ApplyEcgButton { get; } = new() { Content = "应用并重新开始" };
    internal TextBlock EcgConfigurationStatus { get; } = new() { TextWrapping = TextWrapping.Wrap };
    internal TextBlock QrsMeasurementStatus { get; } = new() { TextWrapping = TextWrapping.Wrap };
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
    internal ComboBox RespiratoryPatternInput { get; } = new() { ItemsSource = new[] { "规则呼吸", "潮式呼吸教学示例", "间停呼吸教学示例" }, SelectedIndex = 0, Width = 180 };
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
            panel.Children.Add(new TextBlock { Text = "潮式／间停示例：深度序列驱动CO₂储库响应；深呼吸后浓度滞后下降，浅呼吸/暂停时储库回升。无呼气时无新呼气末值；输入呼气末及平台值在潮式／间停模式中为参考值。Resp计数幅度仅为信号标度，不直接当作潮气量。" });
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
            conduction.Children.Add(new TextBlock { Text = "传导模式" });
            conduction.Children.Add(ConductionInput);
            conduction.Children.Add(JunctionalEscapeButton);
            conduction.Children.Add(VentricularEscapeButton);
            conduction.Children.Add(SecondDegreePresetInput);
            conduction.Children.Add(SecondDegreePresetButton);
            conduction.Children.Add(BundleBlockInput);
            conduction.Children.Add(BundleBlockButton);
            conduction.Children.Add(WpwInput);
            conduction.Children.Add(WpwNegativeV1Input);
            conduction.Children.Add(WpwSmallerDeltaInput);
            conduction.Children.Add(WpwButton);
            conduction.Children.Add(SvtInput);
            conduction.Children.Add(SvtButton);
            conduction.Children.Add(ShortPrInput);
            conduction.Children.Add(ShortPrButton);
            conduction.Children.Add(NormalPrDeltaInput);
            conduction.Children.Add(ProlongedPrDeltaInput);
            conduction.Children.Add(NormalPrDeltaButton);
            conduction.Children.Add(new TextBlock { Text = "独立束支模板：窦性75次/分、1:1房室传导、PR160ms；完全RBBB / 不完全RBBB / 完全LBBB的QRS分别为140 / 110 / 160ms。保留继发ST–T，固定形态/时限；不从QRS宽度推断射血量。使用载入按钮可清除此前节律/形态设置。" });
            conduction.Children.Add(new TextBlock { Text = "从室扑/室颤、房扑/房颤或逸搏切换至二度阻滞并应用时，将加载完整示例并重置参数。也可使用载入按钮。Ⅱ型示例PR恒定160ms：窄QRS80ms，完全RBBB为140ms；完全LBBB为160ms，宽切迹R与右胸QS/rS，伴继发性ST–T改变。2:1比例本身不区分Ⅰ/Ⅱ型。" });
            conduction.Children.Add(FlutterButton);
            conduction.Children.Add(FibrillationButton);
            conduction.Children.Add(PrematureAtrialButton);
            conduction.Children.Add(BlockedPrematureAtrialButton);
            conduction.Children.Add(AberrantPrematureAtrialButton);
            conduction.Children.Add(PrematureJunctionalButton);
            conduction.Children.Add(new TextBlock { Text = "房早/交界性早搏射血教学配置：正常800、已下传早搏400、间歇后恢复1000；未下传房早不新增射血。权重不是搏出量，恢复增强为作者选值；差异传导及逆行P位置不自动推算强度。ABP/PA/Pleth共用逐搏配置；早搏射血及脉搏时限为各通道基准的75%，正常/恢复搏保持基准；固定作者参数，CVP逐搏形态仍待完善。" });
            conduction.Children.Add(PrematureVentricularButton);
            conduction.Children.Add(new TextBlock { Text = "R-on-T长QT示意：仅室早前一搏QT延至640ms、T440ms，使联律500ms的室早R波落在前一T波上。局部复极与新QRS按电位叠加；作者波形，不预测不应期、折返或自动转为室速/室颤。短联律示意保留窦性QT320ms，室早联律200ms；短联律例采用室早无有效射血的教学配置，其他室早弱射血；正常/室早/恢复搏强度分别800/200/1000，短联律室早为0。恢复搏相对增强，插入性例不预设增强；正常脉搏时限保留；CVP保留原分量时限并允许有限叠加，仍无瓣膜/充盈模型。不是由ECG推算搏出量。" });
            conduction.Children.Add(new TextBlock { Text = "成对室早：三次窦性搏动后连续两次宽QRS，支持单形或双形态示意，无夹在其中的窦性搏动；首次联律500ms、两次室早相隔500ms，随后1400ms恢复窦性QRS。双形态例第二次采用反向电位及QRS180/QT500ms；固定教学时序，未模拟不应期、反复搏动或逐搏搏出量。" });
            conduction.Children.Add(new TextBlock { Text = "插入性室早：基础窦性60次/分，每3次窦性搏动后在原1000ms RR内插入一次室早，前后各500ms；窦性P和下一QRS仍按原时刻出现，无代偿间歇。下一P可与室早T叠加；不模拟隐匿传导或PR延长。" });
            conduction.Children.Add(new TextBlock { Text = "多形/多源室早：A/B两套作者形态交替；B为反向电位向量和更宽QRS180ms/QT500ms。固定联律例均500ms；不同联律例A500/B600ms，均保留完全代偿。仅教学形态示意，不定位真实异位灶；无逐搏搏出量推断。" });
            conduction.Children.Add(new TextBlock { Text = "室早可在列表切换二联律（窦性/室早交替）或三联律（2次窦性后1次室早）；每次室早联律500ms、随后间歇1100ms，形态与单形室早例相同。非逐搏灌注量模型。" });
            conduction.Children.Add(new TextBlock { Text = "单形室早：3次窦性搏动后提前出现无相关P的宽QRS160ms/QT480ms/T220ms，T方向与主要QRS相反；联律500ms＋间歇1100ms为完全代偿。其余窦性QRS80ms/QT320ms。固定教学例，不推断异位灶或逐搏搏出量。" });
            conduction.Children.Add(new TextBlock { Text = "交界性早搏列表可切换P′前置、后置或重叠。后置P′起点为QRS起点后120ms；重叠型P′与80ms QRS同时开始，按电位相加而非删去P′。三例心室时刻/形态相同，房性机械事件跟随逆行P′移动。" });
            conduction.Children.Add(new TextBlock { Text = "交界性早搏：逆行P′在提前QRS前80ms，II/III/aVF倒置、aVR直立，QRS80ms/QT320ms与窦性搏动相同；联律500ms加间歇1100ms等于2个基础RR。固定教学例，未模拟不应期或逐搏充盈/搏出量。" });
            conduction.Children.Add(new TextBlock { Text = "差异传导房早：联律500ms、间歇1000ms与正常下传例相同；仅房早这一搏采用RBBB的QRS140ms/QT400ms/T180ms及继发ST–T，其余窦性搏动仍为QRS80ms/QT320ms。保留P′和房室/机械事件时刻；不自动推断搏出量变化。" });
            conduction.Children.Add(new TextBlock { Text = "未下传房早：联律400ms的P′叠加于前一搏T波，不产生对应QRS–T或室性机械搏动；1100ms后恢复窦性P，长RR为1500ms。房性机械事件保留，既有压力尾波继续回落；不模拟逸搏触发。" });
            conduction.Children.Add(new TextBlock { Text = "正常下传房早：3个窦性搏动后出现不同形态的P′，联律500ms、随后间歇1000ms，两者之和小于正常PP的两倍（1600ms）；P′R160ms、窄QRS80ms、固定QT320ms。切入此模式会重置参数。电/机械事件同源；此按钮载入窄QRS例；不模拟逐搏充盈和搏出量变化。" });
            conduction.Children.Add(VentricularDisorganizationButton);
            conduction.Children.Add(new TextBlock { Text = "室扑／室颤：无独立P/QRS/T，无有效射血；Pleth无搏动，RC压力衰减。呼吸与CO₂仍为独立设置，不自动模拟呼吸停止或气体交换改变。可在列表切换室扑、粗室颤、细室颤。" });
            conduction.Children.Add(new TextBlock { Text = "房颤示例：无正常P，f波不规则且V1明显；可切换粗/细颤。RR逐搏不规则，440–1160ms，长期平均室率75；QRS80ms、固定QT300ms。此例无正常房性机械收缩，可另选Ashman型差异传导；生理波形demo可独立选择体循环脉搏短绌教学例。" });
            conduction.Children.Add(new TextBlock { Text = "房扑示例：房率300，连续F波，选2:1/3:1/4:1对应室率150/100/75；QRS80ms、QT300ms。12导联使用固定形态/时限；未模拟房扑机械收缩，CVP不生成正常a波。" });
            conduction.Children.Add(new TextBlock { Text = "室性逸搏示例：房率75、室率30，宽大切迹QRS160ms/反向T，固定QT480ms；可改室周期1500–3000ms及首次偏移。12导联保留此例形态/时限；不代表逸搏起源定位。" });
            conduction.Children.Add(new TextBlock { Text = "交界性逸搏示例：房率75、室率50，正常80ms QRS，无固定PR；可改独立室周期1000–1500ms及首次偏移。12导联需保留参考形态和固定时限。此示例从已建立的逸搏开始。" });
            conduction.Children.Add(new TextBlock { Text = "固定PR与文氏示意分开：文氏3:2、4:3、5:4的PR附加延迟分别为0/80、0/80/120、0/80/120/140 ms；每组最后一次P不下传，下一组复位。需房室活动、无独立心室周期，机械比例1且清空机械日程；成组传导的QT采用最短RR下界（房性周期）约束；文氏末次机械事件须在同一房性周期内。" });
            conduction.Children.Add(new TextBlock { Text = "心脏源活动" });
            conduction.Children.Add(CardiacActivityInput);
            conduction.Children.Add(AfAberrancyInput);
            ToolTip.SetTip(AfAberrancyInput, "仅粗／细房颤可用：指定长–短RR后的单搏采用RBBB样QRS140ms/QT400ms，其余QRS80ms/QT300ms。保持f波和射血设置；约运行13–15秒观察首个宽QRS。教学阈值不是自动诊断或不应期模型。");
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
                conduction.Children.Add(AfPulseDeficitInput);
                ToolTip.SetTip(AfPulseDeficitInput, "仅粗／细房颤可用。指定长–短RR组合保留QRS，但不新增ABP／Pleth脉搏；旧尾部继续，PA／CVP不随之消失。作者教学设定，不是自动判断；运行约15秒可观察首个缺脉搏。应用后重新开始。");
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
            settings.Children.Add(QuinidineInput);
            settings.Children.Add(QuinidineNotchedPInput);
            settings.Children.Add(QuinidineButton);
            settings.Children.Add(DigitalisInput);
            settings.Children.Add(DigitalisShapeInput);
            settings.Children.Add(DigitalisButton);
            settings.Children.Add(CalciumInput);
            settings.Children.Add(CalciumButton);
            settings.Children.Add(HypokalemiaInput);
            settings.Children.Add(HypokalemiaButton);
            settings.Children.Add(HypokalemiaFusionInput);
            settings.Children.Add(HypokalemiaInvertedTInput);
            settings.Children.Add(HypokalemiaConductionInput);
            settings.Children.Add(new TextBlock { Text = "低钾样复极：60次/分、ST压低、低平T和增高u；QT400ms，QT-u650ms（u不计入QT）。可选T-u融合（源QT仍400ms，融合曲线的T末不可直接辨认）；载入重置其他编辑，不对应血钾浓度。" });
            settings.Children.Add(HyperkalemiaInput);
            settings.Children.Add(HyperkalemiaButton);
            settings.Children.Add(HyperkalemiaConductionInput);
            settings.Children.Add(HyperkalemiaAbsentPInput);
            settings.Children.Add(HyperkalemiaFusionInput);

            settings.Children.Add(new TextBlock { Text = "高钾样复极：复极例75次/分、QT300/T120ms；传导例60次/分、P140/PR240/QRS140/QT440/T160ms、低幅P、R降低/S加深及ST压低；不对应血钾浓度。载入清除其他形态设置；取消勾选可返回参考编辑。" });
            settings.Children.Add(TContourInput);
            settings.Children.Add(TContourLeadInput);
            settings.Children.Add(new TextBlock { Text = "T轮廓峰幅（μV，1～2000；单相正向/普通倒置允许0压平）" });
            settings.Children.Add(TContourPeakInput);
            settings.Children.Add(new TextBlock { Text = "双向T第二瓣峰幅（μV，1～2000，空=第一瓣；其他形态留空）" });
            settings.Children.Add(TContourSecondPeakInput);
            settings.Children.Add(new TextBlock { Text = "双向T过零位置（T时限%，0.1～99.9，留空50；非双向留空）" });
            settings.Children.Add(TContourCrossingInput);
            settings.Children.Add(new TextBlock { Text = "胸导联轮廓仅改所选T；肢体目标会联动其余肢体导联，胸导联保持。需关闭其他T/ST、融合、梗死和心室示例。" });
            settings.Children.Add(VentricularInput);
            settings.Children.Add(new TextBlock { Text = "左室/双室示例QRS=100ms，右室/qR/肺源性形态80ms；双室和肺源性形态保留参考ST/T；肺源性形态可另选右房P。需关闭手动T/ST、融合、梗死/三区域模式，可与P/u组合。" });
            settings.Children.Add(AtrialInput);
            settings.Children.Add(new TextBlock { Text = "右房示例P=100ms，左房/双房P=140ms；保留PR，左房要求PR<227.5ms。请清空手动P；这些形态不具有病因特异性。" });
            settings.Children.Add(new TextBlock { Text = "C1 P 早分量（μV，可空）" });
            settings.Children.Add(PEarlyInput);
            settings.Children.Add(new TextBlock { Text = "C1 P 晚分量（μV，可空）" });
            settings.Children.Add(PLateInput);
            settings.Children.Add(new TextBlock { Text = "T 峰位置（%，可空）" });
            settings.Children.Add(TPeakInput);
            settings.Children.Add(new TextBlock { Text = "胸前电极 J 偏移（μV）" });
            settings.Children.Add(ChestJInput);
            settings.Children.Add(new TextBlock { Text = "ST 末端偏移（μV）" });
            settings.Children.Add(ChestStEndInput);
            settings.Children.Add(new TextBlock { Text = "ST 中段弓起（μV）" });
            settings.Children.Add(ChestStArchInput);
            foreach (var input in FusionInputs) { settings.Children.Add(input); }
            settings.Children.Add(new TextBlock { Text = "融合 J（μV）" });
            settings.Children.Add(FusionJInput);
            settings.Children.Add(new TextBlock { Text = "融合峰（μV）" });
            settings.Children.Add(FusionPeakInput);
            settings.Children.Add(new TextBlock { Text = "融合峰位置（J→QT 终点，%）" });
            settings.Children.Add(FusionPositionInput);
            settings.Children.Add(SeparateZonesInput);
            foreach (var (label, input) in new[] { ("缺血形态区域", IschemiaZoneInput), ("损伤形态区域", InjuryZoneInput), ("坏死形态区域", NecrosisZoneInput) })
            {
                settings.Children.Add(new TextBlock { Text = label });
                settings.Children.Add(input);
            }
            settings.Children.Add(IndependentComponentsInput);
            settings.Children.Add(NecrosisShapeInput);
            settings.Children.Add(new TextBlock { Text = "Q/QS 模板混合（%，0=参考，100=模板）" });
            settings.Children.Add(QrsTemplateInput);
            settings.Children.Add(ContributionLossInput);
            foreach (var (label, input) in new[] { ("贡献峰值（μV）", ContributionAmplitudeInput),
                ("贡献时限（QRS的%，10～100）", ContributionDurationInput), ("移除比例（%，0～100）", ContributionAmountInput) })
            {
                settings.Children.Add(new TextBlock { Text = label });
                settings.Children.Add(input);
            }
            foreach (var (label, input) in new[] { ("组合 T 峰（μV，空=参考）", ComponentTInput),
                ("组合 J（μV）", ComponentJInput), ("组合 ST 末端（μV）", ComponentEndInput), ("组合 ST 弓起（μV）", ComponentArchInput) })
            {
                settings.Children.Add(new TextBlock { Text = label });
                settings.Children.Add(input);
            }
            settings.Children.Add(InfarctionStageInput);
            settings.Children.Add(InfarctionTerritoryInput);
            settings.Children.Add(new TextBlock { Text = "区域复极延长（ms）" });
            settings.Children.Add(RepolarizationDelayInput);
            foreach (var input in InfarctionInputs) { settings.Children.Add(input); }
            settings.Children.Add(ApplyEcgButton);
            foreach (Control item in settings.Children) { item.Margin = new Thickness(0, 0, 8, 8); }
            panel.Children.Add(settings);
            panel.Children.Add(new TextBlock { Text = "应用后暂停扫屏并清空当前及固定画面，从零生成。源心房率不是检测心率；QTc 使用传导后的室性 RR。固定示意模式保留原 QT。输入范围 30–200 次/分，QTc 与各时限 1–1000 ms；P ≤ PR、QRS＋T ≤ QT、PR＋QT ≤ 室性 RR。高心率可按需要缩短各波段，时限不再固定为参考值。" });
            panel.Children.Add(new TextBlock { Text = "u 波幅度 −1000～1000 μV，全部为 0 时关闭；T 后延迟 0～1000 ms，时限 1～1000 ms。启用时须满足 PR＋QT＋u 延迟＋u 时限 ≤ 室性 RR；不计入 QT。此处仅添加胸前 u 波，肢体导联不变，不自动按心率调整幅度。" });
            panel.Children.Add(new TextBlock { Text = "胸前电极 T 波手动倍率：−4～4，精度 0.001；1 保留参考，0 去除该电极 T 分量，负值反转极性。投影前调整，肢体电极不变；不改变 P/QRS/QT/u，不代表正常或病理预设。T 峰位置为 T 自身时限的 10～90%，精度 0.1%；所有电极共用，留空使用参考 62.5%，不改变 T 时限或 QT。" });
            panel.Children.Add(new TextBlock { Text = "手动 J/ST 偏移 −1000～1000 μV，共同作用于 C1～C6，肢体电极不变。附加电位从 QRS 最后四分之一平滑升起，在 T 起点达到 ST 末端值，再随 T 时限回到零；会改变 QRS 末端及 T 上的基线，不改变 QT/u。非病理预设；J、末端及弓起均为 0 时关闭，启用时需有非零 ST 时限。ST 中段弓起为 −1000～1000 μV：正值向上隆起，负值向下凹；只叠加于 ST 内，中点达到所填附加电位，保持 J 和末端值。示例：J=200、末端=200、弓起=200 μV。" });
            panel.Children.Add(new TextBlock { Text = "手动 C1 P 双分量：两项同时留空保留参考，否则各为 −1000～1000 μV 整数。两个圆钝波瓣在 P 时限内重叠，可形成切迹或双向波；数值为分量幅度，非最终 V1 峰值。仅替换 C1 的 P，不改变 P 时限/PR 或其他波段；不是疾病预设。" });
            panel.Children.Add(new TextBlock { Text = "ST–T 融合仅替换勾选的胸导联：J 为 −1000～1000 μV，峰为 −2000～2000 μV；峰位置为 J→QT 终点的 0.1～99.9%（精度 0.1%），可早于原 T 起点。统一轮廓取代该处原 T 及 J/ST/弓起，未选导联保持原配置；从 QRS 最后四分之一平滑接入，QT/u 不变。融合电位以 Wilson 复极参考为基准，避免残留原 T 波。取消勾选恢复原参数；不是心梗区域或病程预设。" });
            panel.Children.Add(new TextBlock { Text = "心梗阶段示意：命名区域优先于胸导联勾选；下壁作用于 II/III/aVF，侧壁作用于 I/aVL/V5/V6。肢体导联保持投影恒等式，相关导联会同时变化（不等同于额外梗死区域），Wilson 不变；后壁 V7–V9、右室 V3R–V4R 暂未接入。替换目标区域的 QRS/ST/T，优先于手动融合及 ST/T 设置；未选胸导联及 P/u 保留；区域 QT 可由复极延长参数改变。超急性损伤示意在所选导联增宽 QRS，需容纳在现有 QT 内。数值为项目示意值，非定量病例；阶段手动切换并从零开始，不代表自动病程、治疗效果或冠脉定位。关闭或取消选择恢复原配置。" });
            panel.Children.Add(new TextBlock { Text = "区域复极延长仅在有效阶段或独立组合及区域启用，0 保留原数据。普通 T 保持起点并增加时限，区域 QT 同量延长；融合轮廓保持 J 点并延伸至新 QT 终点。参数不随阶段自动推断。原 u 波时间不移动，暂不允许延长 T 与 u 重叠；阶段和独立组合均关闭时，保留延长输入但不生效。显示 QTc 仍是输入参考值，不是延长后的测量结果。" });
            panel.Children.Add(new TextBlock { Text = "独立组合使用上述同一区域，可分别设置 Q/QS、T 峰和 ST 轮廓，并使用区域复极延长；无需启用阶段。Q/QS 模板混合0～100%（精度0.1%）：0为参考QRS，100为完整模板，中间值仅混合该处QRS，不改变ST/T；选择参考QRS时此值不起作用。该值不是组织比例，不自动随病程变化。T 留空采用参考，0 去除该 T 分量，负值倒置；ST 各项0关闭附加电位。此模式取代区域内原 QRS/ST/T 和阶段值，不自动判断缺血程度；下方另列隔离 QRS 的数值核验。取消勾选恢复阶段入口。" });
            panel.Children.Add(new TextBlock { Text = "三区域模式分别应用组合 T/复极延长、组合 ST、组合 QRS，区域可重叠或关闭。以参考 QRS/ST/T 为底板，保留 P/u/节律；阶段、同区组合与其他手动 ST/T/融合参数保留但暂不使用。重叠采用独立分量叠加，肢体区域仍有投影联动，不要求人为嵌套。关闭模式恢复旧入口；这不是组织深度、梗死大小或病程模型。" });
            panel.Children.Add(new TextBlock { Text = "贡献移除使用参考QRS=剩余源+显式贡献的教学分解：从QRS起点开始的平滑正向分量，峰值0～2000 μV；时限为QRS的10～100%，移除比例0～100%，均为整数。参考QRS及100%模板混合时可启用，作用于同区组合或三区域中的坏死形态区域；ST/T保持。参数由作者指定，无法从参考心电图唯一推定，也不是心肌组织比例或经过验证的解剖模型。所有阶段和贡献参数只在手动应用时改变，不自动随模拟时间演变。" });
            panel.Children.Add(_activeEcgConfiguration);
            panel.Children.Add(QrsMeasurementStatus);
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
            settings.Children.Add(RespiratoryPatternInput);
            settings.Children.Add(new TextBlock { Text = "潮式：9次渐强渐弱＋2周期暂停；间停：3次呼吸＋2周期暂停、2次呼吸＋3周期暂停，循环（非完整共济失调模型）；需正常呼吸活动并清空活动日程。深度为相对通气示意，CO₂浓度通过储库响应滞后变化；Resp计数幅度仅控制信号标度。" });
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
        VentricularDisorganizationButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.Disorganized(AvConductionPattern.VentricularFlutterIllustration), PhysiologyDemoConfiguration.Disorganized(AvConductionPattern.VentricularFlutterIllustration)); }
        };
        SvtButton.Click += (_, _) =>
        {
            if (!_closed && (_projected || _physiology)) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.SvtPreset, PhysiologyDemoConfiguration.SvtPreset); }
        };
        NormalPrDeltaButton.Click += (_, _) =>
        {
            if (!_closed && (_projected || _physiology)) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.NormalPrDeltaPreset, PhysiologyDemoConfiguration.NormalPrDeltaPreset); }
        };
        ShortPrButton.Click += (_, _) =>
        {
            if (!_closed && (_projected || _physiology)) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.ShortPrPreset, PhysiologyDemoConfiguration.ShortPrPreset); }
        };
        WpwButton.Click += (_, _) =>
        {
            if (!_closed && (_projected || _physiology)) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.WpwPreset, PhysiologyDemoConfiguration.WpwPreset); }
        };
        QuinidineButton.Click += (_, _) =>
        {
            if (!_closed && _projected && QuinidineInput.SelectedIndex is >= 0 and <= 4)
            { Reset(UsesPulse, ProjectedEcgDemoConfiguration.QuinidinePreset((QuinidineIllustration)QuinidineInput.SelectedIndex, QuinidineInput.SelectedIndex > 0 && QuinidineNotchedPInput.IsChecked == true)); }
        };
        DigitalisButton.Click += (_, _) =>
        {
            if (!_closed && _projected && DigitalisShapeInput.SelectedIndex is >= 0 and <= 2) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.Digitalis with { DigitalisShape = (DigitalisTShape)DigitalisShapeInput.SelectedIndex }); }
        };
        CalciumButton.Click += (_, _) =>
        {
            if (_closed || !_projected || CalciumInput.SelectedIndex is < 0 or > 5) { return; }
            Reset(UsesPulse, ProjectedEcgDemoConfiguration.CalciumPreset((CalciumIllustration)CalciumInput.SelectedIndex));
        };
        HypokalemiaButton.Click += (_, _) =>
        {
            if (_closed || !_projected) { return; }
            Reset(UsesPulse, HypokalemiaConductionInput.IsChecked == true ? ProjectedEcgDemoConfiguration.HypokalemiaWithConduction : ProjectedEcgDemoConfiguration.Hypokalemia);
        };
        HyperkalemiaButton.Click += (_, _) =>
        {
            if (_closed || !_projected) { return; }
            Reset(UsesPulse, HyperkalemiaFusionInput.IsChecked == true ? ProjectedEcgDemoConfiguration.HyperkalemiaWithFusion : HyperkalemiaAbsentPInput.IsChecked == true ? ProjectedEcgDemoConfiguration.HyperkalemiaWithoutP : HyperkalemiaConductionInput.IsChecked == true ? ProjectedEcgDemoConfiguration.HyperkalemiaWithConduction : ProjectedEcgDemoConfiguration.Hyperkalemia);
        };
        BundleBlockButton.Click += (_, _) =>
        {
            if (_closed || BundleBlockInput.SelectedIndex is < 0 or > 6) { return; }
            var mode = (EcgBundleBlockIllustration)BundleBlockInput.SelectedIndex;
            Reset(UsesPulse, BundleBlockPreset.Ecg(mode), BundleBlockPreset.Physiology(mode));
        };
        SecondDegreePresetButton.Click += (_, _) =>
        {
            if (_closed || SecondDegreePresetInput.SelectedIndex is < 0 or > 7) { return; }
            Reset(UsesPulse, SecondDegreeBlockPreset.Ecg(SecondDegreePresetInput.SelectedIndex), SecondDegreeBlockPreset.Physiology(SecondDegreePresetInput.SelectedIndex));
        };
        PrematureVentricularButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.PrematureVentricular, PhysiologyDemoConfiguration.PrematureVentricular); }
        };
        PrematureJunctionalButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.PrematureJunctional, PhysiologyDemoConfiguration.PrematureJunctional); }
        };
        AberrantPrematureAtrialButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.AberrantPrematureAtrial, PhysiologyDemoConfiguration.AberrantPrematureAtrial); }
        };
        BlockedPrematureAtrialButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.BlockedPrematureAtrial, PhysiologyDemoConfiguration.BlockedPrematureAtrial); }
        };
        PrematureAtrialButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.PrematureAtrial, PhysiologyDemoConfiguration.PrematureAtrial); }
        };
        FibrillationButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.Fibrillation(), PhysiologyDemoConfiguration.Fibrillation()); }
        };
        FlutterButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.Flutter(4), PhysiologyDemoConfiguration.Flutter(4)); }
        };
        VentricularEscapeButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.VentricularEscape, PhysiologyDemoConfiguration.VentricularEscape); }
        };
        JunctionalEscapeButton.Click += (_, _) =>
        {
            if (!_closed) { Reset(UsesPulse, ProjectedEcgDemoConfiguration.JunctionalEscape, PhysiologyDemoConfiguration.JunctionalEscape); }
        };
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
        ElectrodeWaveformGroup? electrodeTrial = _projected ? _electrodeSource!.Fork() : null;
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

    private bool TryLoadPrematureBeatTransition(AvConductionPattern current)
    {
        if (ConductionInput.SelectedIndex is not (22 or 23 or 24 or 25 or 26 or 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37) || current == ConductionSelection.Pattern(ConductionInput.SelectedIndex)) { return false; }
        if (ConductionInput.SelectedIndex is >= 28 and <= 37)
        {
            var pattern = ConductionSelection.Pattern(ConductionInput.SelectedIndex);
            Reset(UsesPulse, ProjectedEcgDemoConfiguration.Pvc(pattern), PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = pattern });
            return true;
        }
        if (ConductionInput.SelectedIndex is >= 25 and <= 27)
        {
            var pattern = ConductionSelection.Pattern(ConductionInput.SelectedIndex);
            Reset(UsesPulse, ProjectedEcgDemoConfiguration.PrematureJunctional with { ConductionPattern = pattern }, PhysiologyDemoConfiguration.PrematureJunctional with { ConductionPattern = pattern });
            return true;
        }
        bool blocked = ConductionInput.SelectedIndex == 23;
        bool aberrant = ConductionInput.SelectedIndex == 24;
        Reset(UsesPulse, blocked ? ProjectedEcgDemoConfiguration.BlockedPrematureAtrial : aberrant ? ProjectedEcgDemoConfiguration.AberrantPrematureAtrial : ProjectedEcgDemoConfiguration.PrematureAtrial,
            blocked ? PhysiologyDemoConfiguration.BlockedPrematureAtrial : aberrant ? PhysiologyDemoConfiguration.AberrantPrematureAtrial : PhysiologyDemoConfiguration.PrematureAtrial);
        return true;
    }

    private bool TryLoadSecondDegreeTransition(AvConductionPattern current)
    {
        int preset = SecondDegreeBlockPreset.FromSelection(ConductionInput.SelectedIndex);
        if (preset < 0 || !SecondDegreeBlockPreset.RequiresReload(current)) { return false; }
        Reset(UsesPulse, SecondDegreeBlockPreset.Ecg(preset), SecondDegreeBlockPreset.Physiology(preset));
        SecondDegreePresetInput.SelectedIndex = preset;
        return true;
    }

    private void ApplyEcgConfiguration()
    {
        if (_closed || !_projected) { return; }
        if (TryLoadPrematureBeatTransition(EcgConfiguration.ConductionPattern)) { return; }
        if (TryLoadSecondDegreeTransition(EcgConfiguration.ConductionPattern)) { return; }
        try
        {
            ProjectedEcgDemoConfiguration configuration;
            if (QtMethod.SelectedIndex == 0) { configuration = (ConductionInput.SelectedIndex switch { 8 => ProjectedEcgDemoConfiguration.VentricularEscape, 9 => ProjectedEcgDemoConfiguration.Flutter(2), 10 => ProjectedEcgDemoConfiguration.Flutter(4), 38 => ProjectedEcgDemoConfiguration.Flutter(3), 39 => ProjectedEcgDemoConfiguration.VariableFlutter, 11 => ProjectedEcgDemoConfiguration.Fibrillation(), 12 => ProjectedEcgDemoConfiguration.Fibrillation(true), 20 => SecondDegreeBlockPreset.Ecg(6), 21 => SecondDegreeBlockPreset.Ecg(7), 22 => ProjectedEcgDemoConfiguration.PrematureAtrial, 23 => ProjectedEcgDemoConfiguration.BlockedPrematureAtrial, 24 => ProjectedEcgDemoConfiguration.AberrantPrematureAtrial, >= 28 and <= 37 => ProjectedEcgDemoConfiguration.Pvc(ConductionSelection.Pattern(ConductionInput.SelectedIndex)), >= 25 and <= 27 => ProjectedEcgDemoConfiguration.PrematureJunctional with { ConductionPattern = ConductionSelection.Pattern(ConductionInput.SelectedIndex) }, >= 13 and <= 15 => ProjectedEcgDemoConfiguration.Disorganized(ConductionSelection.Pattern(ConductionInput.SelectedIndex)), _ => SvtInput.IsChecked == true ? ProjectedEcgDemoConfiguration.SvtPreset : NormalPrDeltaInput.IsChecked == true ? ProjectedEcgDemoConfiguration.NormalPrDeltaPreset with { PrIntervalMilliseconds = ProlongedPrDeltaInput.IsChecked == true ? 240 : 160 } : ShortPrInput.IsChecked == true ? ProjectedEcgDemoConfiguration.ShortPrPreset : WpwInput.IsChecked == true ? ProjectedEcgDemoConfiguration.WpwPreset with { QrsDurationMilliseconds = WpwSmallerDeltaInput.IsChecked == true ? 110 : 140 } : QuinidineInput.SelectedIndex > 0 ? ProjectedEcgDemoConfiguration.QuinidinePreset((QuinidineIllustration)QuinidineInput.SelectedIndex, QuinidineInput.SelectedIndex > 0 && QuinidineNotchedPInput.IsChecked == true) : DigitalisInput.IsChecked == true ? ProjectedEcgDemoConfiguration.Digitalis : CalciumInput.SelectedIndex > 0 ? ProjectedEcgDemoConfiguration.CalciumPreset((CalciumIllustration)CalciumInput.SelectedIndex) : HypokalemiaInput.IsChecked == true ? HypokalemiaConductionInput.IsChecked == true ? ProjectedEcgDemoConfiguration.HypokalemiaWithConduction : ProjectedEcgDemoConfiguration.Hypokalemia : HyperkalemiaInput.IsChecked == true ? HyperkalemiaFusionInput.IsChecked == true ? ProjectedEcgDemoConfiguration.HyperkalemiaWithFusion : HyperkalemiaAbsentPInput.IsChecked == true ? ProjectedEcgDemoConfiguration.HyperkalemiaWithoutP : HyperkalemiaConductionInput.IsChecked == true ? ProjectedEcgDemoConfiguration.HyperkalemiaWithConduction : ProjectedEcgDemoConfiguration.Hyperkalemia : BundleBlockPreset.Ecg((EcgBundleBlockIllustration)BundleBlockInput.SelectedIndex) }) with { VentricularConductionRatio = ConductionSelection.Resolve(ConductionInput.SelectedIndex).Atrial }; }
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
                configuration = new(hr, qtc, method, ConductionSelection.Resolve(ConductionInput.SelectedIndex).Atrial, p, pr, qrs, t);
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
                !int.TryParse(ChestStEndInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int chestEnd) ||
                !int.TryParse(ChestStArchInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int chestArch))
            { throw new ArgumentException("Invalid ST offset."); }
            EcgPWaveComponents? pWave = null;
            if (!string.IsNullOrWhiteSpace(PEarlyInput.Text) || !string.IsNullOrWhiteSpace(PLateInput.Text))
            {
                if (!int.TryParse(PEarlyInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int early) ||
                    !int.TryParse(PLateInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int late))
                { throw new ArgumentException("Invalid P components."); }
                pWave = new(early, late);
            }
            int fusionMask = 0;
            for (int index = 0; index < FusionInputs.Length; index++)
            {
                if (FusionInputs[index].IsChecked is not { } enabled) { throw new ArgumentException("Invalid fusion selection."); }
                if (enabled) { fusionMask |= 1 << index; }
            }
            if (!int.TryParse(FusionJInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int fusionJ) ||
                !int.TryParse(FusionPeakInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int fusionPeak) ||
                !decimal.TryParse(FusionPositionInput.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal fusionPosition) ||
                fusionPosition is < 0.1m or > 99.9m || fusionPosition * 10 != decimal.Truncate(fusionPosition * 10))
            { throw new ArgumentException("Invalid fusion contour."); }
            ProjectedEcgFusionConfiguration fusion = new(fusionMask, fusionJ, fusionPeak, (int)(fusionPosition * 10));
            int infarctionMask = 0;
            for (int index = 0; index < InfarctionInputs.Length; index++)
            {
                if (InfarctionInputs[index].IsChecked is not { } enabled) { throw new ArgumentException("Invalid infarction selection."); }
                if (enabled) { infarctionMask |= 1 << index; }
            }
            if (!int.TryParse(RepolarizationDelayInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int delayMs) ||
                delayMs is < 0 or > 500) { throw new ArgumentException("Invalid regional repolarization delay."); }
            if (IndependentComponentsInput.IsChecked is not { } independentComponents) { throw new ArgumentException("Invalid component mode."); }
            if (SeparateZonesInput.IsChecked is not { } separateZones) { throw new ArgumentException("Invalid zones mode."); }
            EcgInfarctionComponents? components = null;
            if (independentComponents || separateZones)
            {
                if (!decimal.TryParse(QrsTemplateInput.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal qrsWeight) ||
                    qrsWeight is < 0 or > 100 || qrsWeight * 10 != decimal.Truncate(qrsWeight * 10))
                { throw new ArgumentException("Invalid QRS template weight."); }
                if (ContributionLossInput.IsChecked is not { } removeContribution) { throw new ArgumentException("Invalid contribution mode."); }
                EcgQrsContributionLoss? contribution = null;
                if (removeContribution)
                {
                    if (!int.TryParse(ContributionAmplitudeInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int amplitude) || amplitude is < 0 or > 2000 ||
                        !int.TryParse(ContributionDurationInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int duration) || duration is < 10 or > 100 ||
                        !int.TryParse(ContributionAmountInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int amount) || amount is < 0 or > 100)
                    { throw new ArgumentException("Invalid contribution parameters."); }
                    contribution = new(amplitude, duration * 10, amount * 10);
                }
                int? tPeak = null;
                if (!string.IsNullOrWhiteSpace(ComponentTInput.Text))
                {
                    if (!int.TryParse(ComponentTInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int tValue) ||
                        tValue is < -2000 or > 2000) { throw new ArgumentException("Invalid component T."); }
                    tPeak = tValue;
                }
                if (!int.TryParse(ComponentJInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int jValue) || jValue is < -1000 or > 1000 ||
                    !int.TryParse(ComponentEndInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int endValue) || endValue is < -1000 or > 1000 ||
                    !int.TryParse(ComponentArchInput.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int archValue) || archValue is < -1000 or > 1000)
                { throw new ArgumentException("Invalid component ST."); }
                components = new((NecrosisIllustrationShape)NecrosisShapeInput.SelectedIndex, tPeak, jValue, endValue, archValue, (int)(qrsWeight * 10), contribution);
            }
            var infarction = new EcgChestInfarctionPlan(infarctionMask, (InfarctionIllustrationStage)InfarctionStageInput.SelectedIndex, (InfarctionTerritory)InfarctionTerritoryInput.SelectedIndex, delayMs * 1_000_000L, independentComponents ? components : null);
            EcgInfarctionZones? zones = separateZones ? new(InfarctionZoneSelection.Resolve(IschemiaZoneInput.SelectedIndex),
                InfarctionZoneSelection.Resolve(InjuryZoneInput.SelectedIndex), InfarctionZoneSelection.Resolve(NecrosisZoneInput.SelectedIndex),
                components!, delayMs * 1_000_000L) : null;
            int? independentOffset = ParseIndependentVentricularOffset();
            if (TContourInput.SelectedIndex is < 0 or > 8 || TContourLeadInput.SelectedIndex is < 0 or > 12 ||
                !int.TryParse(TContourPeakInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int contourPeak) || contourPeak is < 0 or > 2000 || (contourPeak == 0 && TContourInput.SelectedIndex is not (7 or 8)))
            { throw new ArgumentException("Invalid T contour selection."); }
            int? secondPeak = null;
            if (!string.IsNullOrWhiteSpace(TContourSecondPeakInput.Text))
            {
                if (!int.TryParse(TContourSecondPeakInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int second) ||
                    second is < 1 or > 2000 || TContourInput.SelectedIndex is not (1 or 2))
                { throw new EventWaveformException("EcgTContour.InvalidSecondPeak", "tContour"); }
                secondPeak = second;
            }
            int? crossing = null;
            if (!string.IsNullOrWhiteSpace(TContourCrossingInput.Text))
            {
                if (!decimal.TryParse(TContourCrossingInput.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) ||
                    value is < 0.1m or > 99.9m || value * 10 != decimal.Truncate(value * 10) || TContourInput.SelectedIndex is not (1 or 2))
                { throw new EventWaveformException("EcgTContour.InvalidCrossing", "tContour"); }
                crossing = (int)(value * 10);
            }
            EcgTContourPlan? contour = TContourInput.SelectedIndex == 0 ? null : new(TContourLeadInput.SelectedIndex == 6 ? 63 : TContourLeadInput.SelectedIndex > 6 ? 1 : 1 << TContourLeadInput.SelectedIndex,
                (EcgTContourShape)TContourInput.SelectedIndex, contourPeak, TContourLeadInput.SelectedIndex > 6 ? (EcgTContourTarget)(TContourLeadInput.SelectedIndex - 6) : EcgTContourTarget.Chest, crossing, secondPeak);
            if (AfAberrancyInput.IsChecked is not { } afAberrancy) { throw new ArgumentException("Explicit AF aberrancy selection required."); }
            // When changing the named absent-P variant, replace its authored
            // activity only if the user has not independently edited it.
            var selectedActivity = (CardiacActivity)CardiacActivityInput.SelectedIndex;
            var ecgActivity = (HyperkalemiaAbsentPInput.IsChecked == true) != EcgConfiguration.HyperkalemiaAbsentP &&
                selectedActivity == EcgConfiguration.CardiacActivity ? configuration.CardiacActivity : selectedActivity;
            configuration = configuration with { Svt = SvtInput.IsChecked == true, ProlongedPrDelta = NormalPrDeltaInput.IsChecked == true && ProlongedPrDeltaInput.IsChecked == true, WpwSmallerDelta = WpwInput.IsChecked == true && WpwSmallerDeltaInput.IsChecked == true, NormalPrDelta = NormalPrDeltaInput.IsChecked == true, ShortPr = ShortPrInput.IsChecked == true, Wpw = WpwInput.IsChecked == true, WpwNegativeV1 = WpwInput.IsChecked == true && WpwNegativeV1Input.IsChecked == true, QuinidineNotchedP = QuinidineInput.SelectedIndex > 0 && QuinidineNotchedPInput.IsChecked == true, Quinidine = (QuinidineIllustration)QuinidineInput.SelectedIndex, DigitalisShape = DigitalisInput.IsChecked == true ? (DigitalisTShape)DigitalisShapeInput.SelectedIndex : DigitalisTShape.FishHook, DigitalisEffect = DigitalisInput.IsChecked == true, Calcium = (CalciumIllustration)CalciumInput.SelectedIndex, HypokalemiaConduction = HypokalemiaConductionInput.IsChecked == true, HypokalemiaInvertedT = HypokalemiaInvertedTInput.IsChecked == true, HyperkalemiaFusion = HyperkalemiaFusionInput.IsChecked == true, HyperkalemiaAbsentP = HyperkalemiaAbsentPInput.IsChecked == true, HyperkalemiaConduction = HyperkalemiaConductionInput.IsChecked == true, HypokalemiaTuFusion = HypokalemiaFusionInput.IsChecked == true, HypokalemiaRepolarization = HypokalemiaInput.IsChecked == true, HyperkalemiaRepolarization = HyperkalemiaInput.IsChecked == true, IllustrateAfAberrancy = afAberrancy, BundleBlock = (EcgBundleBlockIllustration)BundleBlockInput.SelectedIndex, ConductionPattern = ConductionSelection.Pattern(ConductionInput.SelectedIndex), ConductedBeatsPerGroup = ConductionSelection.Resolve(ConductionInput.SelectedIndex).Conducted, TContour = contour, Ventricular = (EcgVentricularIllustration)VentricularInput.SelectedIndex, Atrial = (EcgAtrialIllustration)AtrialInput.SelectedIndex, Zones = zones, Infarction = infarction.ChestMask == 0 && infarction.Stage == InfarctionIllustrationStage.None && infarction.Territory == InfarctionTerritory.CustomChest && infarction.RepolarizationDelayNs == 0 && infarction.Components is null ? null : infarction, Fusion = fusion == ProjectedEcgFusionConfiguration.Default ? null : fusion, ChestStArchMicrovolts = chestArch, ChestP = pWave, ChestJMicrovolts = chestJ, ChestStEndMicrovolts = chestEnd, TWave = tWave == ProjectedEcgTConfiguration.Default ? null : tWave, IndependentVentricularOffsetMilliseconds = independentOffset, IndependentVentricularPeriodMilliseconds = independentPeriod, UWave = u == ProjectedEcgUConfiguration.Default ? null : u, CardiacActivity = ecgActivity, Placement = (EcgLimbPlacement)LimbPlacementInput.SelectedIndex };
            Reset(UsesPulse, configuration);
        }
        catch (EventWaveformException error) when (error.ReasonCode == "Svt.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：室上速为固定节律示例，请载入以重置其他形态／时序参数。当前波形保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "NormalPrDelta.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：PR伴delta为固定示例，请载入以重置冲突参数。当前波形保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "ShortPr.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：短PR／无delta为固定示例，请使用载入按钮重置冲突参数。当前波形保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "Wpw.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：WPW示例使用固定参数，请载入重置；当前状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode is "Quinidine.InvalidMode" or "Quinidine.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：奎尼丁示例使用固定参数，不能叠加其他形态。请载入重置；当前状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "Digitalis.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：洋地黄效应示例使用固定参数，不可叠加其他形态。请载入以重置；当前状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode is "Calcium.InvalidMode" or "Calcium.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：钙相关示例使用固定参数，不能叠加其他形态；请载入以重置。当前状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "Hypokalemia.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：低钾样复极示例使用固定参数，不可叠加高钾或其他形态编辑；请使用载入按钮重置。当前状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "Hyperkalemia.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：高钾样复极示例使用固定参数，需清除其他节律和形态编辑；请使用载入按钮重置。当前状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "EcgTContour.InvalidSecondPeak")
        { EcgConfigurationStatus.Text = "未应用：第二瓣峰幅仅用于双向T，须为1～2000 μV整数；留空与第一瓣等幅，其他形态须留空。当前数据与扫屏状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "EcgTContour.InvalidCrossing")
        { EcgConfigurationStatus.Text = "未应用：过零位置仅用于双向T，范围0.1～99.9%，精度0.1%；留空为50%，非双向T须留空。当前数据与扫屏状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "EcgTContour.ConflictingModes")
        { EcgConfigurationStatus.Text = "未应用：T轮廓需要关闭其他T/ST、融合、梗死/三区域和心室示例。当前数据与扫屏状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "EcgVentricular.ConflictingModes")
        {
            EcgConfigurationStatus.Text = "未应用：心室示例不能与手动 T/ST、融合、梗死或三区域编辑同时使用。请关闭这些设置后应用；当前数据与扫屏状态保持。";
        }
        catch (EventWaveformException error) when (error.ReasonCode is "EcgBundleBlock.ConflictingModes" or "EcgBundleBlock.InvalidMode")
        {
            var status = _projected ? EcgConfigurationStatus : BreathConfigurationStatus;
            status.Text = "未应用：独立束支模板需1:1房室活动、固定形态/时限且无独立心室时钟；请使用载入按钮重置，或选择参考以关闭模板。当前数据与扫屏状态保持。";
        }
        catch (ArgumentException)
        {
            EcgConfigurationStatus.Text = "未应用：可使用“载入二度阻滞示例（重置参数）”清除此前节律参数；成组下传需房室活动且关闭独立心室周期；贡献移除需参考QRS、100%模板混合，峰值0～2000 μV、时限10～100%、移除0～100%均为整数；三区域须选择有效区域；独立组合需有效 QRS 形态、Q/QS 混合 0～100%（精度0.1%）、T 峰 ±2000 μV（或空）、J/末端/弓起各 ±1000 μV；复极延长须为 0～500 ms 整数，延长后 PR＋QT ≤ 室性 RR；启用 u 波时延长量不能超过原 T→u 间隔；请选择有效阶段及区域，所选 QRS 增宽后仍须满足 QT 时限；融合 J 须为 ±1000 μV 整数、融合峰 ±2000 μV 整数，融合峰位置 0.1～99.9%（精度 0.1%）；C1 P 双分量须同时留空或均为 −1000～1000 μV 整数；J/ST 偏移及中段弓起须为 −1000～1000 μV 整数，启用时 ST 时限须大于零；T 峰位置须为 10～90%，精度 0.1% 或留空；T 倍率须为 −4～4 且精度不超过 0.001；起始偏移需独立心室周期，且 ≥0、偏移＋80 ms < 心室周期；独立心室周期须为 800～3200 ms 且不短于基础心房周期，需比例 1:1，或留空；起始偏移留空时沿用 PR。请输入范围内的整数，传导比例须为 1:1～4:1，各时限为 1～1000 ms，并满足 P ≤ PR、QRS＋T ≤ QT、PR＋QT ≤ 室性 RR；u 波参数须在所示范围内，启用时 PR＋QT＋u 延迟＋u 时限 ≤ 室性 RR。当前数据与扫屏状态保持。";
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
        if (TryLoadPrematureBeatTransition(BreathConfiguration.ConductionPattern)) { return; }
        if (TryLoadSecondDegreeTransition(BreathConfiguration.ConductionPattern)) { return; }
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
            if (AfPulseDeficitInput.IsChecked is not { } afPulseDeficit)
            { throw new ArgumentException("Explicit AF pulse-deficit selection required."); }
            if (AfAberrancyInput.IsChecked is not { } afAberrancy) { throw new ArgumentException("Explicit AF aberrancy selection required."); }
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
                (RespiratoryActivity)RespiratoryActivityInput.SelectedIndex, afterBreaths, durationBreaths, ConductionSelection.Resolve(ConductionInput.SelectedIndex).Atrial, (CardiacActivity)CardiacActivityInput.SelectedIndex, mechanicalEnabled, mechanicalAfter, mechanicalDuration, MechanicalEveryCyclesInput.SelectedIndex + 1, vascularReservoir, independentPeriod, ParseIndependentVentricularOffset(), (RespiratoryPattern)RespiratoryPatternInput.SelectedIndex, ConductionSelection.Resolve(ConductionInput.SelectedIndex).Conducted, ConductionSelection.Pattern(ConductionInput.SelectedIndex), (EcgBundleBlockIllustration)BundleBlockInput.SelectedIndex, afPulseDeficit, afAberrancy, WpwInput.IsChecked == true, WpwInput.IsChecked == true && WpwNegativeV1Input.IsChecked == true, ShortPrInput.IsChecked == true, NormalPrDeltaInput.IsChecked == true, WpwInput.IsChecked == true && WpwSmallerDeltaInput.IsChecked == true, NormalPrDeltaInput.IsChecked == true && ProlongedPrDeltaInput.IsChecked == true, SvtInput.IsChecked == true));
        }
        catch (EventWaveformException error) when (error.ReasonCode == "Svt.ConflictingModes")
        { BreathConfigurationStatus.Text = "未应用：室上速需启用血管回落模型，且不能与其他节律、形态或独立心室时钟叠加。当前状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "NormalPrDelta.ConflictingModes")
        { BreathConfigurationStatus.Text = "未应用：PR伴delta不能与其他节律或束支示例叠加。当前波形保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "ShortPr.ConflictingModes")
        { BreathConfigurationStatus.Text = "未应用：短PR／无delta不能与其他节律或束支示例叠加。当前波形保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode == "Wpw.ConflictingModes")
        { BreathConfigurationStatus.Text = "未应用：WPW示例需1:1房室活动、无独立心室时钟及其他心电模板；请载入重置。当前状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode is "Capnogram.Co2ResponseOutOfRange" or "Capnogram.InvalidCo2Response")
        { BreathConfigurationStatus.Text = "未应用：呼吸模式耦合后的CO₂超过支持范围（0～327 mmHg）；请降低参考呼气末值或检查呼吸周期。当前数据与扫屏状态保持。"; }
        catch (EventWaveformException error) when (error.ReasonCode is "EcgBundleBlock.ConflictingModes" or "EcgBundleBlock.InvalidMode")
        {
            var status = _projected ? EcgConfigurationStatus : BreathConfigurationStatus;
            status.Text = "未应用：独立束支模板需1:1房室活动、固定形态/时限且无独立心室时钟；请使用载入按钮重置，或选择参考以关闭模板。当前数据与扫屏状态保持。";
        }
        catch (ArgumentException error) when (error.Message == "AF pulse deficit requires an AF rhythm.")
        { BreathConfigurationStatus.Text = "未应用：脉搏短绌示例仅适用于粗／细房颤；请关闭该选项或载入房颤。当前数据与扫屏状态保持。"; }
        catch (ArgumentException)
        {
            BreathConfigurationStatus.Text = "未应用：可用“载入二度阻滞示例（重置参数）”清除旧参数；成组下传需房室活动、无独立心室周期、机械比例1且清空机械日程；潮式／间停示例须使用正常呼吸活动并清空活动先完成/持续次数；起始偏移需独立心室周期，且 ≥0、偏移＋80 ms < 心室周期；请检查参数范围；独立心室周期须为 800～3200 ms 或留空，设置时比例须为 1:1；机械搏动比例须为每 1～4 个室性周期一次；机械停止持续周期须为 1～100 或留空，且需先设置机械完成周期；机械先完成周期须为 1～100 或留空，需关闭室性机械事件并选择含心室事件的模式；先完成次数须为 1～100 或留空，且不能用于正常呼吸；恢复所需周期数须为 1～100 或留空，并先设置完成次数；Resp 心源伪差幅度为 −200～200；吸气／呼气末停顿须 ≥0 且短于各自总时长；基线 ≤ 平台起始 ≤ 呼气末目标，各时长须为正，下降不超过吸气，死腔＋上升须短于呼气。平台精确到 0.01 mmHg 或留空，管路滞后为 0～5000 ms，展宽步长为 0～500 ms。当前状态保持。";
        }
    }

    private void Reset(bool pulse, ProjectedEcgDemoConfiguration? configuration = null, PhysiologyDemoConfiguration? breathConfiguration = null)
    {
        configuration ??= EcgConfiguration;
        breathConfiguration ??= BreathConfiguration;
        PeriodicWaveformGroup source = CreateSource(pulse);
        PhysiologyWaveformGroup? eventSource = _physiology ? PhysiologyDemoSource.Create(breathConfiguration) : null;
        ElectrodeWaveformGroup? electrodeSource = _projected ? ProjectedEcgDemoSource.Create(configuration) : null;
        string qrsSummary = electrodeSource is null ? "" : AuthoredQrsSummary.Create(electrodeSource, configuration.CardiacActivity);
        RawTrace empty = new([], _physiology, projected: _projected);
        Pause();
        _source = source;
        _physiologySource = eventSource;
        _electrodeSource = electrodeSource;
        EcgConfiguration = configuration;
        QrsMeasurementStatus.Text = qrsSummary;
        BreathConfiguration = breathConfiguration;
        AfAberrancyInput.IsChecked = _projected ? configuration.IllustrateAfAberrancy : breathConfiguration.IllustrateAfAberrancy;
        SvtInput.IsChecked = _projected ? configuration.Svt : breathConfiguration.Svt;
        ProlongedPrDeltaInput.IsChecked = _projected ? configuration.ProlongedPrDelta : breathConfiguration.ProlongedPrDelta;
        NormalPrDeltaInput.IsChecked = _projected ? configuration.NormalPrDelta : breathConfiguration.NormalPrDelta;
        ShortPrInput.IsChecked = _projected ? configuration.ShortPr : breathConfiguration.ShortPr;
        WpwInput.IsChecked = _projected ? configuration.Wpw : breathConfiguration.Wpw;
        WpwSmallerDeltaInput.IsChecked = _projected ? configuration.WpwSmallerDelta : breathConfiguration.WpwSmallerDelta;
        WpwNegativeV1Input.IsChecked = _projected ? configuration.WpwNegativeV1 : breathConfiguration.WpwNegativeV1;
        BundleBlockInput.SelectedIndex = (int)(_projected ? configuration.BundleBlock : breathConfiguration.BundleBlock);
        if (_physiology)
        {
            ConductionInput.SelectedIndex = ConductionSelection.Index(breathConfiguration.VentricularConductionRatio, breathConfiguration.ConductedBeatsPerGroup, breathConfiguration.ConductionPattern);
            IndependentVentricularOffsetInput.Text = breathConfiguration.IndependentVentricularOffsetMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            IndependentVentricularPeriodInput.Text = breathConfiguration.IndependentVentricularPeriodMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            CardiacActivityInput.SelectedIndex = (int)breathConfiguration.CardiacActivity;
            VentricularMechanicalInput.IsChecked = breathConfiguration.VentricularMechanicalEnabled;
            VascularReservoirInput.IsChecked = breathConfiguration.UseVascularReservoir;
            AfPulseDeficitInput.IsChecked = breathConfiguration.IllustrateAfSystemicPulseDeficit;
            VascularPressureModeStatus.Text = breathConfiguration.UseVascularReservoir
                ? "已应用血管储压模型：ABP／PA 保留各自的上升支、切迹与下降支；停搏波尾结束后分别衰减至 10／5 mmHg，恢复后从残余压力逐搏充盈。形态与储压组合为教学近似，CVP 仍使用原有分量模型。"
                : "已应用固定基线形态模板：无新机械事件时 ABP／PA 波尾结束后保持 80／10 mmHg；启用血管储压模型可观察压力衰减与恢复。";
            if (AtrialFibrillationReference.IsPattern(breathConfiguration.ConductionPattern))
            { VascularPressureModeStatus.Text += " 房颤示例中 Pleth、ABP、PA 使用共享的逐搏强弱变化；强度为教学设定，不代表真实搏出量测量。"; }
            if (breathConfiguration.IllustrateAfSystemicPulseDeficit)
            { VascularPressureModeStatus.Text += " 已启用体循环脉搏短绌教学例：部分QRS无新ABP／Pleth脉搏，PA／CVP保持各自示例。"; }
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
            RespiratoryPatternInput.SelectedIndex = (int)breathConfiguration.RespiratoryPattern;
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
            _activeBreathConfiguration.Text = VentricularDisorganizationReference.IsPattern(breathConfiguration.ConductionPattern)
                ? $"已应用：{ConductionInput.SelectedItem}；无独立P/QRS/T或有效射血；Pleth无搏动，RC压力衰减。呼吸周期{breathConfiguration.BreathPeriodMilliseconds} ms，吸气{breathConfiguration.InspirationMilliseconds} ms；Resp/CO₂仍依独立呼吸设置生成，不模拟气体交换反馈。"
                : string.Create(CultureInfo.InvariantCulture,
                $"已应用：{RespiratoryPatternInput.SelectedItem}；{CardiacActivityInput.SelectedItem}；首次 QRS 偏移 {breathConfiguration.IndependentVentricularOffsetMilliseconds ?? (breathConfiguration.Svt ? 0 : breathConfiguration.NormalPrDelta ? (breathConfiguration.ProlongedPrDelta ? 240 : 160) : breathConfiguration.Wpw || breathConfiguration.ShortPr ? 100 : AtrialFlutterReference.IsPattern(breathConfiguration.ConductionPattern) || AtrialFibrillationReference.IsPattern(breathConfiguration.ConductionPattern) ? 80 : 160)} ms；独立心室周期 {breathConfiguration.IndependentVentricularPeriodMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "未启用"} ms；室性机械事件{(breathConfiguration.VentricularMechanicalEnabled ? "启用" : "关闭")}、每 {breathConfiguration.MechanicalEveryCycles} 个室性周期一次（先完成周期数 {MechanicalAfterCyclesInput.Text}，空为立即；停止持续周期数 {MechanicalDurationCyclesInput.Text}，空为不恢复）；{ConductionInput.SelectedItem}；{ConductionSelection.Summary(breathConfiguration.VentricularConductionRatio, breathConfiguration.ConductedBeatsPerGroup, breathConfiguration.ConductionPattern)}；目标呼吸活动 {RespiratoryActivityInput.SelectedItem}（先完成次数 {ActivityAfterBreathsInput.Text}，留空立即；状态持续周期 {ActivityDurationBreathsInput.Text}，留空不恢复）；周期 {breathConfiguration.BreathPeriodMilliseconds} ms；吸气/呼气 {breathConfiguration.InspirationMilliseconds}/{breathConfiguration.BreathPeriodMilliseconds - breathConfiguration.InspirationMilliseconds} ms（吸气／呼气末停顿 {breathConfiguration.InspiratoryPauseMilliseconds}/{breathConfiguration.ExpiratoryPauseMilliseconds} ms）；Resp 幅度 {breathConfiguration.RespAmplitudeCounts}，心源伪差幅度 {breathConfiguration.RespCardiacArtifactCounts}。Resp、CO₂、CVP 共用呼吸时序；不是测得的 RR。CO₂ 平台起始 {(breathConfiguration.Co2PlateauStartCentiMmHg is null ? "参考比例" : Co2PlateauInput.Text + " mmHg")}，基线/参考呼气末 {breathConfiguration.Co2BaselineMmHg}/{breathConfiguration.Co2EndExpiratoryMmHg} mmHg；死腔/上升/下降 {breathConfiguration.Co2DeadSpaceMilliseconds}/{breathConfiguration.Co2RiseMilliseconds}/{breathConfiguration.Co2FallMilliseconds} ms；CO₂ 管路滞后 {breathConfiguration.Co2TransportDelayMilliseconds} ms；展宽步长 {breathConfiguration.Co2DispersionStepMilliseconds} ms。");
            if (breathConfiguration.Svt)
            { _activeBreathConfiguration.Text += "；规则窄QRS室上速200次/分，P′与QRS重叠；电/机械事件0/80ms；灌注采用固定输入示例"; }
            if (breathConfiguration.NormalPrDelta)
            { _activeBreathConfiguration.Text += $"；{(breathConfiguration.ProlongedPrDelta ? "延长" : "正常")}PR伴delta固定示例，PR{(breathConfiguration.ProlongedPrDelta ? 240 : 160)}/QRS140ms；心室电事件{(breathConfiguration.ProlongedPrDelta ? 240 : 160)}ms、机械事件{(breathConfiguration.ProlongedPrDelta ? 320 : 240)}ms"; }
            if (breathConfiguration.ShortPr)
            { _activeBreathConfiguration.Text += "；短PR／无delta示例，PR100/QRS80ms；心室电事件100ms、机械事件180ms"; }
            if (breathConfiguration.Wpw)
            { _activeBreathConfiguration.Text += $"；WPW{(breathConfiguration.WpwNegativeV1 ? "负向" : "正向")}V1 delta示例，PR100/QRS{(breathConfiguration.WpwSmallerDelta ? 110 : 140)}/P-J{(breathConfiguration.WpwSmallerDelta ? 210 : 240)}ms；心室电事件100ms、机械事件180ms"; }
            if (breathConfiguration.BundleBlock != EcgBundleBlockIllustration.Reference)
            { _activeBreathConfiguration.Text += $"；{BundleBlockInput.SelectedItem}，QRS {BundleBlockReference.Timing(breathConfiguration.BundleBlock).QrsDurationNs / 1_000_000} ms"; }
            BreathConfigurationStatus.Text = "";
        }
        if (_projected)
        {
            var timing = configuration.ResolveTiming();
            LimbPlacementInput.SelectedIndex = (int)configuration.Placement;
            QtMethod.SelectedIndex = configuration.MethodId switch { EcgQtCorrection.Bazett => 1, EcgQtCorrection.Fridericia => 2, _ => 0 };
            ConductionInput.SelectedIndex = ConductionSelection.Index(configuration.VentricularConductionRatio, configuration.ConductedBeatsPerGroup, configuration.ConductionPattern);
            IndependentVentricularOffsetInput.Text = configuration.IndependentVentricularOffsetMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            IndependentVentricularPeriodInput.Text = configuration.IndependentVentricularPeriodMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "";
            CardiacActivityInput.SelectedIndex = (int)configuration.CardiacActivity;
            HeartRateInput.Text = configuration.HeartRateBpm.ToString(CultureInfo.InvariantCulture);
            PDurationInput.Text = configuration.PDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
            PrIntervalInput.Text = configuration.PrIntervalMilliseconds.ToString(CultureInfo.InvariantCulture);
            QrsDurationInput.Text = configuration.QrsDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
            TDurationInput.Text = configuration.TDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
            QtcInput.Text = configuration.QtcMilliseconds.ToString(CultureInfo.InvariantCulture);
            foreach (var input in new[] { PDurationInput, PrIntervalInput, QrsDurationInput, TDurationInput, QtcInput })
            {
                input.IsEnabled = !configuration.HyperkalemiaFusion && configuration.MethodId is not null;
                if (configuration.HyperkalemiaFusion) { input.Text = "—"; }
            }
            if (configuration.Svt) { PDurationInput.Text = "—"; PrIntervalInput.Text = "—"; PDurationInput.IsEnabled = false; PrIntervalInput.IsEnabled = false; }
            if (configuration.DigitalisEffect) { TDurationInput.Text = "—"; TDurationInput.IsEnabled = false; }
            QtMethod.IsEnabled = !configuration.HyperkalemiaFusion;
            var u = configuration.UWave ?? ProjectedEcgUConfiguration.Default;
            UDelayInput.Text = u.DelayMilliseconds.ToString(CultureInfo.InvariantCulture);
            UDurationInput.Text = u.DurationMilliseconds.ToString(CultureInfo.InvariantCulture);
            for (int index = 0; index < UAmplitudeInputs.Length; index++)
            { UAmplitudeInputs[index].Text = u.ChestAmplitudes[index].ToString(CultureInfo.InvariantCulture); }
            var infarction = configuration.Infarction ?? new(0, InfarctionIllustrationStage.None);
            SeparateZonesInput.IsChecked = configuration.Zones is not null;
            IschemiaZoneInput.SelectedIndex = configuration.Zones is { } z1 ? InfarctionZoneSelection.Index(z1.Ischemia) : 0;
            InjuryZoneInput.SelectedIndex = configuration.Zones is { } z2 ? InfarctionZoneSelection.Index(z2.Injury) : 0;
            NecrosisZoneInput.SelectedIndex = configuration.Zones is { } z3 ? InfarctionZoneSelection.Index(z3.Necrosis) : 0;
            IndependentComponentsInput.IsChecked = infarction.Components is not null;
            var components = configuration.Zones?.Components ?? infarction.Components ?? new EcgInfarctionComponents();
            NecrosisShapeInput.SelectedIndex = (int)components.Necrosis;
            QrsTemplateInput.Text = (components.QrsTemplatePermille / 10m).ToString("0.#", CultureInfo.InvariantCulture);
            ContributionLossInput.IsChecked = components.ContributionLoss is not null;
            var contribution = components.ContributionLoss ?? new EcgQrsContributionLoss();
            ContributionAmplitudeInput.Text = contribution.AmplitudeMicrovolts.ToString(CultureInfo.InvariantCulture);
            ContributionDurationInput.Text = (contribution.DurationPermille / 10).ToString(CultureInfo.InvariantCulture);
            ContributionAmountInput.Text = (contribution.LossPermille / 10).ToString(CultureInfo.InvariantCulture);
            ComponentTInput.Text = components.TPeakMicrovolts?.ToString(CultureInfo.InvariantCulture) ?? "";
            ComponentJInput.Text = components.JMicrovolts.ToString(CultureInfo.InvariantCulture);
            ComponentEndInput.Text = components.StEndMicrovolts.ToString(CultureInfo.InvariantCulture);
            ComponentArchInput.Text = components.StArchMicrovolts.ToString(CultureInfo.InvariantCulture);
            RepolarizationDelayInput.Text = ((configuration.Zones?.RepolarizationDelayNs ?? infarction.RepolarizationDelayNs) / 1_000_000).ToString(CultureInfo.InvariantCulture);
            InfarctionTerritoryInput.SelectedIndex = (int)infarction.Territory;
            InfarctionStageInput.SelectedIndex = (int)infarction.Stage;
            for (int index = 0; index < InfarctionInputs.Length; index++)
            { InfarctionInputs[index].IsChecked = (infarction.ChestMask & (1 << index)) != 0; }
            var fusion = configuration.Fusion ?? ProjectedEcgFusionConfiguration.Default;
            for (int index = 0; index < FusionInputs.Length; index++)
            { FusionInputs[index].IsChecked = (fusion.ChestMask & (1 << index)) != 0; }
            FusionJInput.Text = fusion.JMicrovolts.ToString(CultureInfo.InvariantCulture);
            FusionPeakInput.Text = fusion.PeakMicrovolts.ToString(CultureInfo.InvariantCulture);
            FusionPositionInput.Text = (fusion.PeakPositionPermille / 10m).ToString("0.#", CultureInfo.InvariantCulture);
            HypokalemiaFusionInput.IsChecked = configuration.HypokalemiaTuFusion;
            HypokalemiaInvertedTInput.IsChecked = configuration.HypokalemiaInvertedT;
            HypokalemiaConductionInput.IsChecked = configuration.HypokalemiaConduction;
            CalciumInput.SelectedIndex = (int)configuration.Calcium;
            QuinidineInput.SelectedIndex = (int)configuration.Quinidine;
            QuinidineNotchedPInput.IsChecked = configuration.QuinidineNotchedP;
            DigitalisInput.IsChecked = configuration.DigitalisEffect;
            DigitalisShapeInput.SelectedIndex = (int)configuration.DigitalisShape;
            HypokalemiaInput.IsChecked = configuration.HypokalemiaRepolarization;
            HyperkalemiaFusionInput.IsChecked = configuration.HyperkalemiaFusion;
            HyperkalemiaAbsentPInput.IsChecked = configuration.HyperkalemiaAbsentP;
            HyperkalemiaConductionInput.IsChecked = configuration.HyperkalemiaConduction;
            HyperkalemiaInput.IsChecked = configuration.HyperkalemiaRepolarization;
            TContourInput.SelectedIndex = configuration.TContour is { } contour ? (int)contour.Shape : 0;
            TContourLeadInput.SelectedIndex = configuration.TContour is { } contourTarget ? contourTarget.Target != EcgTContourTarget.Chest ? (int)contourTarget.Target + 6 : contourTarget.ChestMask == 63 ? 6 : Enumerable.Range(0, 6).Single(i => contourTarget.ChestMask == 1 << i) : 0;
            TContourCrossingInput.Text = configuration.TContour?.CrossingPositionPermille is { } crossing ? (crossing / 10m).ToString("0.#", CultureInfo.InvariantCulture) : "";
            TContourSecondPeakInput.Text = configuration.TContour?.SecondPeakMicrovolts?.ToString(CultureInfo.InvariantCulture) ?? "";
            TContourPeakInput.Text = (configuration.TContour?.PeakMicrovolts ?? 300).ToString(CultureInfo.InvariantCulture);
            VentricularInput.SelectedIndex = (int)configuration.Ventricular;
            AtrialInput.SelectedIndex = (int)configuration.Atrial;
            PEarlyInput.Text = configuration.ChestP?.EarlyMicrovolts.ToString(CultureInfo.InvariantCulture) ?? "";
            PLateInput.Text = configuration.ChestP?.LateMicrovolts.ToString(CultureInfo.InvariantCulture) ?? "";
            var tWave = configuration.TWave ?? ProjectedEcgTConfiguration.Default;
            ChestStArchInput.Text = configuration.ChestStArchMicrovolts.ToString(CultureInfo.InvariantCulture);
            ChestJInput.Text = configuration.ChestJMicrovolts.ToString(CultureInfo.InvariantCulture);
            ChestStEndInput.Text = configuration.ChestStEndMicrovolts.ToString(CultureInfo.InvariantCulture);
            TPeakInput.Text = tWave.PeakPositionPermille is { } peak ? (peak / 10m).ToString("0.#", CultureInfo.InvariantCulture) : "";
            for (int index = 0; index < TScaleInputs.Length; index++)
            { TScaleInputs[index].Text = (tWave.ChestScales[index] / 1000m).ToString("0.###", CultureInfo.InvariantCulture); }
            string waveTiming = configuration.Svt ? "逆行P′与QRS重叠；PR不可单独测量；QRS80/T100ms；首次QRS偏移0ms" : configuration.DigitalisEffect ? "P100／PR160／QRS80ms；ST–T复合轮廓，T时限不单独标注" : configuration.HyperkalemiaFusion ? "无P；PR不适用；QRS–T融合，独立QRS/T/QT不可辨；复合轮廓720ms" : configuration.HyperkalemiaAbsentP ? "无P；PR不适用；QRS140/T160ms；首次QRS偏移240ms仅为源时序" : AtrialFibrillationReference.IsPattern(configuration.ConductionPattern) ? (configuration.IllustrateAfAberrancy ? "无正常P；f波不规则；普通QRS80/QT300 ms，差异传导QRS140/QT400 ms" : "无正常P；f波不规则；QRS80 ms；T140 ms") : AtrialFlutterReference.IsPattern(configuration.ConductionPattern)
                ? "F周期200 ms（无正常P）；QRS80 ms；T140 ms"
                : string.Create(CultureInfo.InvariantCulture, $"P/模板PR/QRS/T {timing.PDurationNs / 1_000_000m:0.###}/{configuration.PrIntervalMilliseconds}/{timing.QrsDurationNs / 1_000_000m:0.###}/{configuration.TDurationMilliseconds} ms");
            string rateSummary = configuration.ConductionPattern == AvConductionPattern.VariableAtrialFlutterIllustration ? "房率300次/分；平均室率100次/分，RR400/600/800ms" : PrematureVentricularReference.IsPattern(configuration.ConductionPattern) ? $"基础窦性{configuration.HeartRateBpm}次/分；含室早，RR非等间距" : PrematureJunctionalReference.IsPattern(configuration.ConductionPattern) ? "基础窦性75次/分；含交界性早搏，RR非等间距" : PrematureAtrialReference.IsPattern(configuration.ConductionPattern) ? "基础窦性75次/分；含房早，RR非等间距" : AtrialFibrillationReference.IsPattern(configuration.ConductionPattern) ? "长期平均室率75次/分（当前RR不固定）"
                : string.Create(CultureInfo.InvariantCulture, $"基础周期率 {configuration.HeartRateBpm} 次/分");
            _activeEcgConfiguration.Text = VentricularDisorganizationReference.IsPattern(configuration.ConductionPattern)
                ? $"已应用：{ConductionInput.SelectedItem}；无独立 P/QRS/T；PR、QRS时限、QT及QTc不适用；无有效射血。"
                : string.Create(CultureInfo.InvariantCulture,
                $"已应用接线：{LimbPlacementInput.SelectedItem}；{CardiacActivityInput.SelectedItem}；{rateSummary}；{(configuration.HyperkalemiaAbsentP ? "规则心室事件，房室比例不可由P波判读" : ConductionInput.SelectedItem)}；{(configuration.Svt ? "房室同步固定示意" : configuration.HyperkalemiaAbsentP ? "无可测PR" : ConductionSelection.Summary(configuration.VentricularConductionRatio, configuration.ConductedBeatsPerGroup, configuration.ConductionPattern))}；{configuration.MethodId ?? "固定示意（不使用 QTc）"}；QT参考RR {timing.RrIntervalNs / 1_000_000m:0.###} ms；{waveTiming}{(PrematureJunctionalReference.IsPattern(configuration.ConductionPattern) ? "（P/PR为窦性搏动；逆行P′80ms，位置见传导摘要）" : "")}{(PrematureVentricularReference.IsPattern(configuration.ConductionPattern) ? "（以上时限为窦性搏动；室早无相关P，具体时限见传导摘要，T220ms）" : "")}；{(configuration.HyperkalemiaFusion ? "QT不单独标注" : $"QT {timing.QtIntervalNs / 1_000_000m:0.###} ms")}{(configuration.ConductionPattern == AvConductionPattern.AberrantPrematureAtrialIllustration ? "（以上时限仅指窦性搏动；房早使用独立140/400/180ms QRS/QT/T）" : "")}") +
                (configuration.HypokalemiaRepolarization ? configuration.HypokalemiaTuFusion ? "；低钾T-u融合：u于源T末前100ms开始、时限350ms；源QT400ms不等于可测QT，QT-u650ms；融合曲线不提供独立T末测量" : "；低钾样ST压低／低幅T／增高u；模板u延迟30ms、时限220ms，QT-u650ms（不计入QT）" : "") +
                (configuration.Svt ? "；规则窄QRS室上速固定例；无独立窦性P或delta；非折返机制模型" : "") +
                (configuration.NormalPrDelta ? $"；{(configuration.ProlongedPrDelta ? "延长" : "正常")}PR伴delta固定示例；PR{(configuration.ProlongedPrDelta ? 240 : 160)}/QRS140ms；含继发ST-T" : "") +
                (configuration.ShortPr ? "；短PR／无delta固定示例；PR100/QRS80ms" : "") +
                (configuration.Wpw ? $"；WPW{(configuration.WpwNegativeV1 ? "负向" : "正向")}V1 delta示例；P-J{(configuration.WpwSmallerDelta ? 210 : 240)}ms；{(configuration.WpwSmallerDelta ? "较小delta／较弱继发ST-T" : "含继发ST-T")}" : "") +
                (configuration.Quinidine != QuinidineIllustration.Reference ? $"；{QuinidineInput.SelectedItem}；QT{timing.QtIntervalNs / 1_000_000}ms、QT-u{QuinidineEffectReference.ResolveQuIntervalNs(configuration.Quinidine) / 1_000_000}ms；P120/PR200ms{(configuration.QuinidineNotchedP ? "、P轻度切迹" : "")}；非剂量或中毒模型" : "") +
                (configuration.DigitalisEffect ? $"；{DigitalisShapeInput.SelectedItem}；下垂型ST压低、QT320ms；非剂量或中毒模型" : "") +
                (configuration.Calcium switch
                {
                    CalciumIllustration.High => "；高钙样固定例：ST40ms、QT300ms",
                    CalciumIllustration.HighAbsentSt => "；高钙样ST消失固定例：T紧接QRS末端、QT260ms",
                    CalciumIllustration.Low => "；低钙样固定例：ST260ms、QT460ms、T120ms",
                    CalciumIllustration.LowFlatT => "；低钙样低平T：参考T幅度1/4；ST260ms、源QT460ms",
                    CalciumIllustration.LowInvertedT => "；低钙样倒置T：保留ST260ms、QT460ms",
                    _ => ""
                }) +
                (configuration.HypokalemiaConduction ? "；低钾P幅度参考×1.5、QRS120ms；固定教学例，非浓度模型" : "") +
                (configuration.HypokalemiaInvertedT ? "；低钾T分量倒置，u方向保持；重叠后合成曲线不保证全程负向" : "") +
                (configuration.HyperkalemiaRepolarization ? configuration.HyperkalemiaFusion ? "；高钾正弦波样QRS–T融合固定例；无独立ST间隙，不对应浓度或自动演变" : configuration.HyperkalemiaAbsentP ? "；高钾无P教学例：保留规则宽QRS（R降低/S加深）/ST压低/高尖T；不据此诊断窦停或逸搏" : configuration.HyperkalemiaConduction ? "；高钾传导受损教学例：宽低P、长PR、宽QRS（R降低/S加深）、ST压低和高尖T（非浓度模型）" : "；高钾样弥漫高尖T／短QT教学例（非浓度或诊断模型）" : "") +
                (configuration.BundleBlock != EcgBundleBlockIllustration.Reference ? $"；{BundleBlockInput.SelectedItem}{(configuration.BundleBlock == EcgBundleBlockIllustration.LeftAnteriorFascicular ? "（固定左轴示例）" : configuration.BundleBlock == EcgBundleBlockIllustration.LeftPosteriorFascicular ? "（固定右轴示例）" : "（含继发ST–T）")}" : "") +
                (configuration.IndependentVentricularPeriodMilliseconds is { } independent ? $"；独立心室周期 {independent} ms、首次 QRS 偏移 {configuration.IndependentVentricularOffsetMilliseconds ?? configuration.PrIntervalMilliseconds} ms（后续 P-QRS 间隔不固定）" : "") +
                (configuration.Zones is not null ? "" : !infarction.HasActiveRegion ? "；阶段示意关闭" : $"；{(infarction.Components is null ? InfarctionStageInput.SelectedItem : "独立组合")}／{InfarctionTerritoryInput.SelectedItem}（仅自选模式使用勾选项）：{string.Join("/", Enumerable.Range(0, 6).Where(i => (infarction.ChestMask & (1 << i)) != 0).Select(i => $"V{i + 1}"))}（覆盖该处 QRS/ST/T）") +
                (configuration.Zones is not null ? $"；三区域：缺血={IschemiaZoneInput.SelectedItem}，损伤={InjuryZoneInput.SelectedItem}，坏死={NecrosisZoneInput.SelectedItem}；缺血区域复极延长 {RepolarizationDelayInput.Text} ms（区域关闭时不生效）；使用组合 Q/ST/T 值，阶段及其他手动 ST/T/融合暂不生效" : "") +
                (configuration.Zones is null && infarction.Components is not null ? $"；独立组合：{NecrosisShapeInput.SelectedItem}，T={ComponentTInput.Text ?? ""}（空=参考），J/末端/弓起={components.JMicrovolts}/{components.StEndMicrovolts}/{components.StArchMicrovolts} μV；阶段参数不生效" : "") +
                (configuration.Zones is not null || infarction.Components is not null ? $"；Q/QS 模板混合 {QrsTemplateInput.Text}%（参考QRS时不生效）" : "") +
                ((configuration.Zones is not null || infarction.Components is not null) && components.ContributionLoss is not null ? $"；显式QRS贡献：{contribution.AmplitudeMicrovolts} μV、QRS时限的{contribution.DurationPermille / 10}%、移除{contribution.LossPermille / 10}%（固定参数）" : "") +
                (configuration.Zones is null && infarction.HasActiveRegion ? $"；区域复极延长 {RepolarizationDelayInput.Text} ms，区域 QT {(timing.QtIntervalNs + infarction.RepolarizationDelayNs) / 1_000_000m:0.###} ms（输入 QTc 为参考）" : "") +
                (configuration.Zones is not null ? "" : fusion.ChestMask == 0 ? "；融合关闭" : $"；ST–T 融合 {string.Join("/", Enumerable.Range(0, 6).Where(i => (fusion.ChestMask & (1 << i)) != 0).Select(i => $"V{i + 1}"))}，J/峰 {fusion.JMicrovolts}/{fusion.PeakMicrovolts} μV，J→QT 峰位置 {FusionPositionInput.Text}%（替换该处原 ST/T）") +
                (configuration.TContour is { } tc ? $"；{TContourLeadInput.SelectedItem} {TContourInput.SelectedItem} 峰幅{tc.PeakMicrovolts} μV" + (tc.Shape is not (EcgTContourShape.PositiveNegative or EcgTContourShape.NegativePositive) ? "" : $"，第二瓣{tc.SecondPeakMicrovolts ?? tc.PeakMicrovolts} μV，过零{(tc.CrossingPositionPermille ?? 500) / 10m:0.#}%") : "") +
                (configuration.Ventricular != EcgVentricularIllustration.Reference ? $"；{VentricularInput.SelectedItem}（非特异）" : "") +
                (configuration.Atrial != EcgAtrialIllustration.Reference ? $"；{AtrialInput.SelectedItem}（非特异）" : configuration.ChestP is { } p ? $"；手动 C1 P 双分量 {p.EarlyMicrovolts}/{p.LateMicrovolts} μV" : configuration.HyperkalemiaAbsentP ? "；无有效心房电/机械事件" : configuration.HypokalemiaConduction ? "；P幅度为参考的1.5倍" : configuration.HyperkalemiaConduction ? "；P幅度为参考的1/3" : "；P 参考形态") +
                (configuration.NormalPrDelta || configuration.Wpw || configuration.Quinidine != QuinidineIllustration.Reference || configuration.DigitalisEffect || configuration.HypokalemiaRepolarization || configuration.HyperkalemiaRepolarization || configuration.BundleBlock != EcgBundleBlockIllustration.Reference || configuration.TContour is not null || configuration.Zones is not null || configuration.Ventricular != EcgVentricularIllustration.Reference ? "" : configuration.TWave is null ? "；T 参考倍率" : "；手动 C1–C6 T 倍率 " + string.Join("/", TScaleInputs.Select(input => input.Text))) +
                (configuration.NormalPrDelta || configuration.Wpw || configuration.Quinidine != QuinidineIllustration.Reference || configuration.DigitalisEffect || configuration.HypokalemiaRepolarization || configuration.HyperkalemiaRepolarization || configuration.BundleBlock != EcgBundleBlockIllustration.Reference || configuration.Zones is not null || configuration.Ventricular != EcgVentricularIllustration.Reference ? "" : configuration.ChestJMicrovolts == 0 && configuration.ChestStEndMicrovolts == 0 && configuration.ChestStArchMicrovolts == 0 ? "；J/ST 附加电位关闭" : $"；手动胸前电极 J/ST 末端/中段弓起 {configuration.ChestJMicrovolts}/{configuration.ChestStEndMicrovolts}/{configuration.ChestStArchMicrovolts} μV") +
                (configuration.HyperkalemiaRepolarization || configuration.TContour is not null || configuration.Zones is not null ? "" : tWave.PeakPositionPermille is null ? "；T 峰参考 62.5%" : $"；手动 T 峰 {TPeakInput.Text}%") +
                (configuration.MethodId is null ? "" : $"；QTc {configuration.QtcMilliseconds} ms") +
                (configuration.Quinidine != QuinidineIllustration.Reference || configuration.HypokalemiaRepolarization ? "" : u.ChestAmplitudes.All(value => value == 0) ? "；u 波关闭" :
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
