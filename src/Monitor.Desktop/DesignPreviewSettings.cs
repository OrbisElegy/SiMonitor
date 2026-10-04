// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewSettings : UserControl
{
    private static readonly string[] EcgChoices = ["窦性参考", "窦性停搏（无逸搏）", "单形室早", "窦性心律不齐", "房性早搏", "交界性早搏", "房颤（粗波）", "房颤（细波）", "房扑（2:1下传）", "房扑（4:1下传）", "二度Ⅰ型4:3", "二度Ⅰ型3:2", "二度Ⅰ型5:4", "二度Ⅱ型3:2（窄QRS）", "二度Ⅱ型4:3（窄QRS）", "二度2:1（不分型）", "二度Ⅱ型4:3＋RBBB", "二度Ⅱ型4:3＋LBBB", "三度AVB（交界性逸搏）", "三度AVB（室性逸搏）", "室扑", "室颤（粗波）", "室颤（细波）", "室上速（窄QRS）", "室上速伴RBBB", "室上速伴LBBB", "单形室速", "室速伴融合波", "室速伴心室夺获", "双向室速", "室速扭转形态示意", "加速性房性自主心律", "加速性交界性自主心律", "加速性室性自主心律", "加速性室性自主心律伴融合", "加速性室性自主心律伴同相夺获", "房性逸搏心律", "房扑（1:1下传）", "房扑（3:1下传）", "房扑（2:1／3:1／4:1交替）", "房早（未下传）", "房早伴RBBB差异传导", "交界性早搏（P′后置）", "交界性早搏（P′重叠）", "室早二联律", "室早三联律", "成对室早", "插入性室早", "双形态室早示意", "多源室早示意", "多形性成对室早", "R-on-T室早（长QT）", "R-on-T室早（短联律）", "完全性右束支阻滞", "不完全性右束支阻滞", "完全性左束支阻滞", "不完全性左束支阻滞", "左前分支阻滞", "左后分支阻滞", "WPW示意（V1正向）", "WPW示意（V1负向）", "WPW较小δ（V1正向）", "WPW较小δ（V1负向）", "短PR（无δ波）", "正常PR伴δ波", "延长PR伴δ波", "粗房颤伴Ashman样差异传导", "细房颤伴Ashman样差异传导", "粗房颤伴脉搏短绌示意", "细房颤伴脉搏短绌示意", "粗房颤伴差异传导及短绌", "细房颤伴差异传导及短绌", "全心静止", "无脉电活动（窦性电活动示意）", "心室静止（保留心房活动）", "高钾样高尖T波（复极示意）", "高钾样传导异常", "高钾样传导异常（无P波）", "低钾样低平T／U波增高", "低钾样倒置T波", "低钾样T–U融合", "低钾样P增高／QRS增宽", "高钙样短QT", "低钙样长QT", "高钙样ST段消失", "低钙样长QT／低平T", "低钙样长QT／倒置T", "洋地黄样鱼钩ST–T", "洋地黄样低平T", "洋地黄样倒置T", "奎尼丁样低平T", "奎尼丁样倒置T", "奎尼丁样宽QRS／低平T", "奎尼丁样宽QRS／倒置T", "奎尼丁样P切迹／低平T", "奎尼丁样P切迹／倒置T", "奎尼丁样P切迹／宽QRS／低平T", "奎尼丁样P切迹／宽QRS／倒置T", "高钾样QRS–T融合", "左房异常P波", "右房异常P波", "双房异常P波", "左室肥厚伴劳损", "右室肥厚伴劳损", "双室肥厚综合征象", "重度右室肥厚qR型", "肺源性心脏改变示意", "Ⅱ导联正负双向T", "Ⅱ导联负正双向T", "Ⅱ导联双峰T", "Ⅱ导联对称倒置T", "Ⅱ导联高尖T", "Ⅱ导联宽大T", "Ⅱ导联低平T", "Ⅱ导联倒置T", "下壁超急性高T", "下壁超急性损伤", "下壁急性单向曲线", "下壁急性Q波／倒置T", "下壁急性QS／倒置T", "下壁亚急性深倒T", "下壁亚急性T变浅", "下壁陈旧Q波／正常T", "下壁陈旧Q波／倒置T", "下壁陈旧Q波／低平T", "侧壁超急性高T", "侧壁超急性损伤", "侧壁急性单向曲线", "侧壁急性Q波／倒置T", "侧壁急性QS／倒置T", "侧壁亚急性深倒T", "侧壁亚急性T变浅", "侧壁陈旧Q波／正常T", "侧壁陈旧Q波／倒置T", "侧壁陈旧Q波／低平T", "前间壁超急性高T", "前间壁超急性损伤", "前间壁急性单向曲线", "前间壁急性Q波／倒置T", "前间壁急性QS／倒置T", "前间壁亚急性深倒T", "前间壁亚急性T变浅", "前间壁陈旧Q波／正常T", "前间壁陈旧Q波／倒置T", "前间壁陈旧Q波／低平T", "前壁超急性高T", "前壁超急性损伤", "前壁急性单向曲线", "前壁急性Q波／倒置T", "前壁急性QS／倒置T", "前壁亚急性深倒T", "前壁亚急性T变浅", "前壁陈旧Q波／正常T", "前壁陈旧Q波／倒置T", "前壁陈旧Q波／低平T", "广泛前壁超急性高T", "广泛前壁超急性损伤", "广泛前壁急性单向曲线", "广泛前壁急性Q波／倒置T", "广泛前壁急性QS／倒置T", "广泛前壁亚急性深倒T", "广泛前壁亚急性T变浅", "广泛前壁陈旧Q波／正常T", "广泛前壁陈旧Q波／倒置T", "广泛前壁陈旧Q波／低平T"];
    internal static int EcgChoiceCount => EcgChoices.Length;
    private static readonly string[] RespirationChoices = ["规则呼吸", "潮式呼吸", "间断呼吸示意", "无呼吸分量"];
    private static readonly string[] EjectionChoices = ["随当前节律", "早搏弱射血（需室早）", "2:1漏搏（需窦性参考）", "无有效射血"];
    private int _ecgSelection;
    private int _appliedShapeSelection;
    private EcgTContourPlan? _appliedTContour;
    private EcgChestInfarctionPlan? _appliedInfarction;
    private EcgInfarctionZones? _appliedZones;
    internal TextBlock ShapeEditStatus { get; } = Text("");
    internal TextBlock ShapeEditSummary { get; } = Text("");
    internal TextBlock AppliedEcgParameters { get; } = Text("");
    internal TextBlock AppliedRespirationParameters { get; } = Text("");
    internal TextBlock AppliedEjectionParameters { get; } = Text("");
    internal void MarkParametersApplied(ProjectedEcgDemoConfiguration configuration, PhysiologyDemoConfiguration physiology,
        int? ecgSelection = null, int? respirationSelection = null, int? ejectionSelection = null)
    {
        _appliedShapeSelection = ecgSelection ?? EcgSelection;
        _appliedTContour = configuration.TContour;
        _appliedInfarction = configuration.Infarction;
        _appliedZones = configuration.Zones;
        AppliedEcgParameters.Text = "心电图 · " + EcgChoices[ecgSelection ?? EcgSelection] + "\n" + EcgTemplateSummary.Describe(configuration);
        string plateau = physiology.Co2PlateauStartCentiMmHg is { } value
            ? (value / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " mmHg" : "随模板";
        AppliedRespirationParameters.Text = $"呼吸 · {RespirationChoices[respirationSelection ?? RespirationSelection]}\n" +
            $"周期 {physiology.BreathPeriodMilliseconds} ms · 吸气 {physiology.InspirationMilliseconds} ms\n" +
            $"RESP 相对信号幅度 {physiology.RespAmplitudeCounts} · 心源性干扰幅度 {physiology.RespCardiacArtifactCounts}\n" +
            $"CO₂ 基线 {physiology.Co2BaselineMmHg} mmHg · 呼气末目标 {physiology.Co2EndExpiratoryMmHg} mmHg · 平台起始高度 {plateau}\n" +
            $"死腔 {physiology.Co2DeadSpaceMilliseconds} ms · 上升 {physiology.Co2RiseMilliseconds} ms · 下降 {physiology.Co2FallMilliseconds} ms\n" +
            $"管路延迟 {physiology.Co2TransportDelayMilliseconds} ms · 展宽步长 {physiology.Co2DispersionStepMilliseconds} ms";
        bool noEjection = !physiology.VentricularMechanicalEnabled || physiology.CardiacActivity is
            CardiacActivity.Absent or CardiacActivity.AtrialOnly || VentricularDisorganizationReference.IsPattern(physiology.ConductionPattern);
        AppliedEjectionParameters.Text = "射血 · " + EjectionChoices[ejectionSelection ?? EjectionSelection] + "\n" +
            (noEjection ? "无有效射血" : "按已应用节律与机械事件生成射血");
        RefreshShapeSummary();
    }
    private void RefreshShapeSummary()
    {
        try
        {
            var config = DesignPreviewWindow.ResolveStyle(EcgSelection, RespirationSelection, 0).Ecg;
            bool editable = config.TContour is not null || config.Infarction is not null;
            var zones = InfarctionParameters.ReadZones(config.Infarction);
            config = config with
            {
                Zones = zones,
                TContour = TContourParameters.Read(config.TContour),
                Infarction = zones is null ? InfarctionParameters.Read(config.Infarction) : null
            };
            bool applied = _appliedShapeSelection == EcgSelection && _appliedTContour == config.TContour && _appliedInfarction == config.Infarction && _appliedZones == config.Zones;
            ShapeEditStatus.Text = !editable ? "模板默认参数（非运行值）" : applied ? "形态参数 · 已应用（非测量值）" : "形态参数 · 待应用（非测量值）";
            ShapeEditSummary.Text = EcgTemplateSummary.Describe(config);
        }
        catch (ArgumentException)
        {
            ShapeEditStatus.Text = "形态参数 · 输入不完整或无效，尚未应用";
            ShapeEditSummary.Text = "请检查导联选择、幅度及时间参数；正在运行的波形保持不变。";
        }
        ShapeEditStatus.IsVisible = ShapeEditStatus.Text!.Contains("待应用", StringComparison.Ordinal) ||
            ShapeEditStatus.Text.Contains("无效", StringComparison.Ordinal);
    }
    internal TContourParameterEditor TContourParameters { get; } = new();
    internal InfarctionParameterEditor InfarctionParameters { get; } = new();
    internal int EcgSelection
    {
        get => _ecgSelection;
        set
        {
            if (_ecgSelection == value) { return; }
            _ecgSelection = value;
            TContourParameters.Reset(value is >= 107 and <= 114 ? TContourProductPreset.Create(value - 107) : null);
            InfarctionParameters.Reset(value is >= 115 and <= 164 ? InfarctionProductPreset.Create((value - 115) % 10,
                (Monitor.Simulation.Physiology.InfarctionTerritory)((value - 115) / 10 + 1)) : null);
            RefreshShapeSummary();
        }
    }
    private int _respirationSelection;
    internal int RespirationSelection
    {
        get => _respirationSelection;
        set { _respirationSelection = value; RespSignalAmplitude.IsEnabled = value != 3; }
    }
    internal NumericUpDown RespSignalAmplitude { get; } = new() { Minimum = -1000, Maximum = 1000, Value = 1000, Increment = 50, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown RespCardiacArtifact { get; } = new() { Minimum = -200, Maximum = 200, Value = 0, Increment = 10, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal (int Amplitude, int Artifact) ReadRespirationSignal()
    {
        static int Read(NumericUpDown field)
        {
            decimal value = field.Value ?? throw new ArgumentException("Preview.InvalidRespirationSignal");
            if (value < field.Minimum || value > field.Maximum || value != decimal.Truncate(value))
            { throw new ArgumentException("Preview.InvalidRespirationSignal"); }
            return checked((int)value);
        }
        return (RespirationSelection == 3 ? 1000 : Read(RespSignalAmplitude), Read(RespCardiacArtifact));
    }
    internal NumericUpDown Co2TransportDelay { get; } = new() { Minimum = 0, Maximum = 5000, Value = 0, Increment = 100, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown Co2DispersionStep { get; } = new() { Minimum = 0, Maximum = 500, Value = 0, Increment = 25, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal (int DelayMilliseconds, int DispersionMilliseconds) ReadCo2Response()
    {
        static int Read(NumericUpDown field)
        {
            decimal value = field.Value ?? throw new ArgumentException("Preview.InvalidCo2Response");
            if (value < field.Minimum || value > field.Maximum || value != decimal.Truncate(value))
            { throw new ArgumentException("Preview.InvalidCo2Response"); }
            return checked((int)value);
        }
        return (Read(Co2TransportDelay), Read(Co2DispersionStep));
    }
    internal NumericUpDown Co2DeadSpace { get; } = new() { Minimum = 1, Maximum = 10000, Value = 125, Increment = 25, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown Co2Rise { get; } = new() { Minimum = 1, Maximum = 10000, Value = 250, Increment = 25, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown Co2Fall { get; } = new() { Minimum = 1, Maximum = 10000, Value = 200, Increment = 25, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal (int DeadSpaceMilliseconds, int RiseMilliseconds, int FallMilliseconds) ReadCo2Timing()
    {
        static int Read(NumericUpDown field)
        {
            decimal value = field.Value ?? throw new ArgumentException("Preview.InvalidCo2Timing");
            if (value < field.Minimum || value > field.Maximum || value != decimal.Truncate(value))
            { throw new ArgumentException("Preview.InvalidCo2Timing"); }
            return checked((int)value);
        }
        return (Read(Co2DeadSpace), Read(Co2Rise), Read(Co2Fall));
    }
    internal string BreathingConstraintDescription()
    {
        var timing = ReadCo2Timing();
        return $"吸气须至少 {timing.FallMilliseconds} ms，呼气须大于 {timing.DeadSpaceMilliseconds + timing.RiseMilliseconds} ms；请调整 CO₂ 时长、呼吸频率或吸气占比。";
    }
    internal NumericUpDown Co2Baseline { get; } = new() { Minimum = 0, Maximum = 80, Value = 0, Increment = 1, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal CheckBox Co2CustomPlateau { get; } = new() { Content = "自定义 CO₂ 平台起始高度" };
    internal NumericUpDown Co2PlateauStart { get; } = new() { Minimum = 0, Maximum = 80, Value = 35, Increment = .25m, Width = 180, HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false };
    internal (int BaselineMmHg, int TargetMmHg, int? PlateauCentiMmHg) ReadCo2Levels()
    {
        static int Read(NumericUpDown field, int scale)
        {
            decimal value = field.Value ?? throw new ArgumentException("Preview.InvalidCo2Levels");
            if (value < field.Minimum || value > field.Maximum || value * scale != decimal.Truncate(value * scale))
            { throw new ArgumentException("Preview.InvalidCo2Levels"); }
            return checked((int)(value * scale));
        }
        int baselineMmHg = Read(Co2Baseline, 1), targetMmHg = Read(EtCo2Target, 1);
        int? plateauCentiMmHg = Co2CustomPlateau.IsChecked == true ? Read(Co2PlateauStart, 100) : null;
        if (baselineMmHg > targetMmHg || plateauCentiMmHg < baselineMmHg * 100 || plateauCentiMmHg > targetMmHg * 100)
        { throw new ArgumentException("Preview.InvalidCo2Levels"); }
        return (baselineMmHg, targetMmHg, plateauCentiMmHg);
    }
    internal int EjectionSelection { get; set; }
    internal Button Apply { get; } = new() { Content = "应用", MinWidth = 88, MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal Button Restart { get; } = new() { Content = "从头开始", MinWidth = 104, MinHeight = 44 };
    internal NumericUpDown ApplyDelaySeconds { get; } = new()
    { Minimum = 0, Maximum = 60, Increment = 0.1m, Value = 3, FormatString = "0.0", Width = 116, Height = 44, VerticalContentAlignment = VerticalAlignment.Center };
    internal long ReadApplyDelayNs()
    {
        if (ApplyDelaySeconds.Value is not { } seconds || seconds < 0 || seconds > 60 || seconds * 10 != decimal.Truncate(seconds * 10))
        { throw new ArgumentException("Preview.InvalidApplyDelay"); }
        return checked((long)(seconds * 1_000_000_000));
    }
    internal Button ResetAll { get; } = new() { Content = "恢复默认设置…", MinHeight = 44, Padding = new Thickness(8, 0) };
    internal Button Run { get; } = new() { Content = "暂停扫描", MinWidth = 104, MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal ComboBox Skin { get; } = new() { ItemsSource = new[] { "紧凑 · 固定 3 行", "标准 · 固定 5 行", "扩展 · 固定 7 行" }, SelectedIndex = 1, MinWidth = 220 };
    internal ListBox Tabs { get; } = new();
    internal Dictionary<int, SettingsSections> SectionPages { get; } = [];
    private readonly ComboBox _compactCategory = new() { MinHeight = 44, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
    private Action<double>? _adaptNavigation;
    protected override Size MeasureOverride(Size availableSize)
    {
        _adaptNavigation?.Invoke(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }
    internal bool CompactNavigation => _compactCategory.IsVisible;
    internal SoundSettingsPanel Sound { get; } = new();
    internal MonitorAlertSettings Alerts { get; } = new();
    internal CheckBox OpticalEnabled { get; } = new() { Content = "启用双波长指脉氧教学源", IsChecked = false };
    internal OxygenationSettingsPanel Oxygenation { get; } = new();
    internal CheckBox CardiacRateEnabled { get; } = new() { Content = "调整窦性参考心率（1:1下传）", IsChecked = false };
    internal NumericUpDown HeartRate { get; } = new() { Minimum = 30, Maximum = 180, Value = 75, Increment = 1, Width = 180 };
    internal NumericUpDown RateVariation { get; } = new() { Minimum = 0, Maximum = 5, Value = 0, Increment = .5m, Width = 180 };
    internal Button GenerateSeed { get; } = new() { Content = "生成随机种子", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal TextBox RateSeed { get; } = new() { Text = new string('0', 63) + "1", MaxWidth = 650 };
    internal NumericUpDown InspirationPercent { get; } = new() { Minimum = 10, Maximum = 90, Value = 50, Increment = 1, Width = 180 };
    internal TextBlock BreathingTiming { get; } = Text("");
    internal NumericUpDown RespiratoryRate { get; } = new() { Minimum = 6, Maximum = 60, Value = 16, Increment = 1, Width = 180 };
    internal NumericUpDown EtCo2Variation { get; } = new() { Minimum = 0, Maximum = 5, Value = 0, Increment = .5m, Width = 180 };
    internal NumericUpDown AbpPulseGain { get; } = new() { Minimum = .5m, Maximum = 2, Value = 1, Increment = .1m, Width = 180 };
    internal NumericUpDown PaPulseGain { get; } = new() { Minimum = .5m, Maximum = 2, Value = 1, Increment = .1m, Width = 180 };
    internal NumericUpDown CvpBaseline { get; } = new() { Minimum = -5, Maximum = 30, Value = 6, Increment = .5m, Width = 180 };
    internal NumericUpDown EtCo2Target { get; } = new() { Minimum = 5, Maximum = 80, Value = 40, Increment = 1, Width = 180 };
    internal NumericUpDown OpticalVariation { get; } = new() { Minimum = 0, Maximum = 2.5m, Value = 0, Increment = .1m, IsEnabled = false, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown OpticalTarget { get; } = new() { Minimum = 0, Maximum = 100, Value = 98, Increment = .1m, FormatString = "0.#", IsEnabled = false, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown OpticalModulation { get; } = new() { Minimum = .1m, Maximum = 2, Value = 1, Increment = .1m, IsEnabled = false, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBlock Status { get; } = Text("应用后接续当前波形；从头开始会清空扫描历史。");
    internal sealed record SlotEditor(ComboBox Channel, CheckBox Auto, TextBox Minimum, TextBox Maximum, ComboBox Speed);
    internal List<SlotEditor> Slots { get; } = [];
    private readonly StackPanel _slotRows = new() { Spacing = 12 };
    internal const long RespirationPreviewDurationNs = 41_250_000_000;
    private readonly Dictionary<int, (long TimeNs, double Value)[]> _respirationPreviews = [];
    private readonly Func<int, (long TimeNs, double Value)[]> _respirationPreview;
    private readonly ContentControl _generation = new();
    private readonly Dictionary<(int, int, int), StylePreviewData> _previews = [];
    private readonly Func<int, int, int, StylePreviewData> _preview;
    private readonly StackPanel _advancedEcg = new() { Spacing = 16 };
    private readonly StackPanel _advancedRespiration = new() { Spacing = 16 };
    internal TabControl RespirationGroups { get; } = new();
    internal Button ResetRespirationPage { get; } = new()
    {
        Content = "恢复本页默认参数",
        MinHeight = 44,
        HorizontalAlignment = HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly StackPanel _respSignal = new() { Spacing = 16 };
    private readonly StackPanel _co2Shape = new() { Spacing = 16 };
    private readonly StackPanel _co2Response = new() { Spacing = 16 };
    private readonly WrapPanel _co2Timing = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _advancedEjection = new() { Spacing = 16 };
    private readonly StackPanel _advancedTools = new() { Spacing = 16 };
    internal ComboBox PaperLayout { get; } = new() { ItemsSource = new[] { "3 × 4 ＋ 长Ⅱ", "6 × 2 ＋ 长Ⅱ" }, SelectedIndex = 0, MinWidth = 220 };
    private readonly List<Action> _refreshSignalRows = [];
    private Control? _home;
    private Button? _returnFocus;
    internal int PreviewCacheCount => _previews.Count;
    internal DesignPreviewSettings(Func<int, int, int, StylePreviewData> preview, Func<int, (long TimeNs, double Value)[]> respirationPreview, Action apply, Action run, Action advanced, DesktopLocalization? localization = null)
    {
        Localization = localization ?? new DesktopLocalization();
        InitializeLocalization();
        _preview = preview; _respirationPreview = respirationPreview;
        TContourParameters.Changed += RefreshShapeSummary;
        InfarctionParameters.Changed += RefreshShapeSummary;
        var generation = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        var instruction = Text("选择生理信号，再选择分组与具体波形；应用后更新监护和十二导联。");
        instruction.Height = 44; generation.Children.Add(instruction);
        var cards = new StackPanel { Spacing = 12 };
        cards.Children.Add(SignalRow("心电图", 0, EcgChoices,
            () => EcgSelection, x => EcgSelection = x));
        cards.Children.Add(SignalRow("呼吸", 1, RespirationChoices,
            () => RespirationSelection, x => RespirationSelection = x));
        cards.Children.Add(SignalRow("射血", 3, EjectionChoices,
            () => EjectionSelection, x => EjectionSelection = x));
        generation.Children.Add(cards);
        var more = new Button { Content = "完整心电图参数（现有开发入口）", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        more.Click += (_, _) => advanced();
        generation.Children.Add(DesktopInformationPages.Help("settings-detail-1"));
        _home = Scroll(generation); _generation.Content = _home;
        var display = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        display.Children.Add(DesktopInformationPages.Help("settings-detail-2"));
        display.Children.Add(Skin);
        display.Children.Add(LocalizedText("display.paperLayout")); display.Children.Add(PaperLayout);
        display.Children.Add(DesktopInformationPages.Help("topic-1"));
        var headings = new Grid { ColumnDefinitions = new("30,160,70,*,*,115") };
        string[] labels = ["display.row", "display.channel", "display.range", "display.minimum", "display.maximum", "display.speed"];
        for (int column = 0; column < labels.Length; column++)
        { var label = LocalizedText(labels[column]); Grid.SetColumn(label, column); headings.Children.Add(label); }
        display.Children.Add(headings);
        display.Children.Add(_slotRows);
        display.Children.Add(DesktopInformationPages.Help("topic-2"));
        display.Children.Add(DesktopInformationPages.Help("settings-detail-3"));
        Skin.SelectionChanged += (_, _) => BuildRows(); BuildRows();
        var vitals = (StackPanel)VitalSigns();
        Control Before(StackPanel panel, Control control) => panel.Children[panel.Children.IndexOf(control) - 1];
        var paper = new StackPanel { Spacing = 16 };
        Control paperLabel = Before(display, PaperLayout), paperHelp = display.Children[^1];
        foreach (var control in new[] { paperLabel, PaperLayout, paperHelp }) { display.Children.Remove(control); paper.Children.Add(control); }
        display.Margin = new Thickness(0);
        SectionPages[2] = new SettingsSections(Localization, "settings.display", ("shell.monitor", display), ("display.paper", paper));
        SectionPages[3] = SettingsSections.Split("声音", Sound,
            ("输出与主音量", Sound.Children[0]), ("心搏提示音", Sound.HeartbeatEnabled),
            ("报警声音暂停", Before(Sound, Sound.PauseSeconds)));
        var alertGroups = new List<(string Title, Control Start)>
        { ("ECG 心率", Alerts.Children[0]), ("SpO₂", Alerts.SpO2Enabled) };
        alertGroups.AddRange(MeasuredLimitNotice.Descriptors.Select(d => (d.Label, (Control)Alerts.AdditionalLimits.Editors[d.Numeric])));
        alertGroups.Add((ProductIdentity.DevelopmentFeatures ? "显示与联调" : "显示", ProductIdentity.DevelopmentFeatures ? (Control)Alerts.TestLevel.Parent! : Alerts.NoticeColorEnabled)); alertGroups.Add(("声音节奏", Alerts.InfoTone));
        alertGroups.Add(("通知策略", Alerts.NotificationSettings));
        SectionPages[4] = SettingsSections.Split("报警", Alerts, alertGroups.ToArray());
        SectionPages[5] = SettingsSections.Split("生命体征", vitals,
            ("心率", vitals.Children[0]), ("共用随机种子", Before(vitals, RateSeed)),
            ("呼吸与 CO₂", Before(vitals, RespiratoryRate)), ("指脉氧", Before(vitals, OpticalEnabled)),
            ("压力", Before(vitals, AbpPulseGain)));
        var advancedGroups = new List<(string Title, Control Content)>
        { ("心电图", _advancedEcg), ("呼吸", _advancedRespiration), ("射血", _advancedEjection) };
        var appliedParameters = new StackPanel { Spacing = 20 };
        appliedParameters.Children.Add(Text("当前运行采用的参数（非测量值）；成功应用后更新。"));
        appliedParameters.Children.Add(AppliedEcgParameters);
        appliedParameters.Children.Add(AppliedRespirationParameters);
        appliedParameters.Children.Add(AppliedEjectionParameters);
        advancedGroups.Add(("当前已应用参数", appliedParameters));
        if (ProductIdentity.DevelopmentFeatures) { advancedGroups.Add(("开发工具", _advancedTools)); }
        SectionPages[6] = new SettingsSections("高级参数", advancedGroups.ToArray());
        RespirationGroups.ItemsSource = new[]
        {
            new TabItem { Header = "RESP 信号", Content = _respSignal, Padding = new Thickness(0), Margin = new Thickness(0, 0, 20, 0), FontSize = 14, MinHeight = 44 },
            new TabItem { Header = "CO₂ 形态", Content = _co2Shape, Padding = new Thickness(0), Margin = new Thickness(0, 0, 20, 0), FontSize = 14, MinHeight = 44 },
            new TabItem { Header = "CO₂ 管路", Content = _co2Response, Padding = new Thickness(0), Margin = new Thickness(0, 0, 20, 0), FontSize = 14, MinHeight = 44 }
        };
        RespirationGroups.Padding = new Thickness(0);
        RespirationGroups.SelectedIndex = 0;
        ResetRespirationPage.Click += (_, _) => ResetRespirationDraft();
        AutomationProperties.SetName(RespirationGroups, "呼吸高级参数分组");
        Co2CustomPlateau.IsCheckedChanged += (_, _) => Co2PlateauStart.IsEnabled = Co2CustomPlateau.IsChecked == true;
        string[] categories = ["settings.general", "settings.generation", "settings.display", "settings.sound", "settings.alarms", "settings.vitals", "settings.advanced"];
        Control[] pages = [BuildGeneralPage(), _generation, SectionPages[2], SectionPages[3], SectionPages[4], SectionPages[5], SectionPages[6]];
        var detail = new ContentControl();
        var navigation = new Grid { ColumnDefinitions = new("160,*"), Margin = new Thickness(12, 0) };
        SettingsSections.StyleNavigation(Tabs);
        Tabs.ItemsSource = categories.Select(title => SettingsSections.Item(title, localization: Localization)).ToArray();
        Localization.Bind(Tabs, AutomationProperties.NameProperty, "settings.categories");
        Localization.Bind(_compactCategory, AutomationProperties.NameProperty, "settings.categories");
        Localization.SetChoices(_compactCategory, categories);
        navigation.Children.Add(Tabs); Grid.SetColumn(detail, 1); navigation.Children.Add(detail);
        var controls = BuildActionFooter(apply, run);
        Tabs.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.Source, Tabs) || Tabs.SelectedIndex < 0) { return; }
            int selected = Tabs.SelectedIndex; _compactCategory.SelectedIndex = selected;
            if (selected == 1) { _generation.Content = _home; }
            if (selected == 6) { RefreshAdvanced(more); }
            detail.Content = pages[selected];
            controls.IsVisible = selected != 0;
            Status.IsVisible = selected != 0;
        };
        _compactCategory.SelectionChanged += (_, args) => { if (ReferenceEquals(args.Source, _compactCategory) && _compactCategory.SelectedIndex >= 0) { Tabs.SelectedIndex = _compactCategory.SelectedIndex; } };
        _adaptNavigation = width =>
        {
            bool compact = width < 1040; Tabs.IsVisible = !compact; _compactCategory.IsVisible = compact;
            navigation.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 160);
        };
        Tabs.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, args) =>
        {
            if (Tabs.SelectedIndex == 1) { _generation.Content = _home; }
        }, handledEventsToo: true);
        Tabs.SelectedIndex = 1;
        var root = new Grid { RowDefinitions = new("Auto,*,Auto,Auto") };
        _compactCategory.Margin = new Thickness(28, 12); root.Children.Add(_compactCategory);
        Grid.SetRow(navigation, 1); root.Children.Add(navigation); Grid.SetRow(controls, 2); root.Children.Add(controls);
        Status.Margin = new Thickness(20, 0, 20, 16); Status.Foreground = Brush.Parse("#616161"); Grid.SetRow(Status, 3); root.Children.Add(Status); Content = root;
    }
    private StylePreviewData Preview(int ecg, int resp, int ejection)
    {
        var key = (ecg, resp, ejection);
        if (_previews.TryGetValue(key, out var cached)) { return cached; }
        var result = _preview(ecg, resp, ejection);
        if (_previews.Count >= 24) { _previews.Clear(); }
        _previews.Add(key, result); return result;
    }
    private Button SignalRow(string title, int channel, string[] choices, Func<int> read, Action<int> write)
    {
        var caption = Text(title + " · " + choices[read()] + "  ›");
        var card = new Button
        {
            Content = caption,
            MinHeight = 64,
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(4)
        };
        _refreshSignalRows.Add(() =>
        {
            caption.Text = title + " · " + choices[read()] + "  ›";
            AutomationProperties.SetName(card, title + "样式，" + choices[read()]);
        });
        AutomationProperties.SetName(card, title + "样式，" + choices[read()]);
        card.Click += (_, _) =>
        {
            _returnFocus = card;
            OpenChooser(title, channel, choices, read, selected =>
            {
                write(selected); caption.Text = title + " · " + choices[selected] + "  ›";
                AutomationProperties.SetName(card, title + "样式，" + choices[selected]);
            });
        };
        return card;
    }
    private void OpenChooser(string title, int channel, string[] choices, Func<int> read, Action<int> write, string? activeGroup = null, bool focusSelection = false)
    {
        string Group(int i) => channel == 0 ? EcgChooserGroups.For(i)
            : channel == 1 ? i == 0 ? "规则呼吸" : "异常呼吸示意" : i == 0 ? "节律相关" : "异常射血示意";
        var shell = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(20) };
        var header = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
        shell.Children.Add(header);
        var body = new StackPanel { Spacing = 12 };
        var scroll = SettingsScroll.Create(body);
        Grid.SetRow(scroll, 1); shell.Children.Add(scroll);

        var navigation = new WrapPanel { Orientation = Orientation.Horizontal };
        var back = new Button { Content = "返回波形设置", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        back.Click += (_, _) => { _generation.Content = _home; if (_returnFocus is { } origin) { RestoreFocus(origin); } };
        navigation.Children.Add(back);
        Button focusTarget = back;
        if (activeGroup is not null)
        {
            var parent = new Button { Content = "返回" + title + "分组", Margin = new Thickness(0, 0, 12, 0), MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            parent.Click += (_, _) => OpenChooser(title, channel, choices, read, write);
            navigation.Children.Add(parent);
        }
        header.Children.Add(navigation);
        header.Children.Add(new TextBlock { Text = "波形生成 / " + title + (activeGroup is null ? "" : " / " + activeGroup), FontSize = 20, FontWeight = FontWeight.SemiBold });
        header.Children.Add(Text("当前选择：" + choices[read()] + " · 应用后生效"));
        if (activeGroup is null)
        {
            foreach (string group in channel == 0 ? EcgChooserGroups.Ordered : Enumerable.Range(0, choices.Length).Select(Group).Distinct())
            {
                bool current = group == Group(read());
                var button = new Button
                {
                    Content = group + "  ›",
                    MinHeight = 56,
                    Padding = new Thickness(16),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal
                };
                AutomationProperties.SetName(button, group + (current ? "，当前分组" : "，选择分组"));
                button.Click += (_, _) => OpenChooser(title, channel, choices, read, write, group);
                body.Children.Add(button);
                if (current) { focusTarget = button; }
            }
            _generation.Content = shell; RestoreFocus(focusTarget); return;
        }
        var parameters = new Button { Content = "当前波形高级参数", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        parameters.Click += (_, _) => OpenAdvanced(channel);
        navigation.Children.Add(parameters);
        var candidates = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < choices.Length; i++)
        {
            if (Group(i) != activeGroup) { continue; }
            int value = i; var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Height = 40, Text = choices[i] + (read() == i ? " ✓" : ""), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            var candidate = new Button { Content = panel, Width = 238, Height = 162, CornerRadius = new CornerRadius(8), VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12), Margin = new Thickness(0, 0, 10, 10), Background = Brushes.Black };
            AutomationProperties.SetName(candidate, choices[i] + (read() == i ? "，已选择" : "，选择此样式"));
            if (focusSelection && read() == i) { focusTarget = candidate; }
            try
            {
                var source = Preview(channel == 0 ? i : EcgSelection, channel == 1 ? i : RespirationSelection, channel == 3 ? i : EjectionSelection);
                panel.Children.Add(Thumbnail(() => source, channel, channel == 1 ? i : null));
                candidate.Click += (_, _) =>
                {
                    write(value); Status.Text = "已选择 " + choices[value] + "；预览已更新，运行数据须应用后改变。";
                    OpenChooser(title, channel, choices, read, write, activeGroup, focusSelection: true);
                };
            }
            catch (ArgumentException)
            {
                candidate.IsEnabled = false;
                panel.Children.Add(new TextBlock { Text = "与当前其他参数不兼容", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
                AutomationProperties.SetHelpText(candidate, "请先调整其他波形设置，此组合当前不可用。");
            }
            candidates.Children.Add(candidate);
        }
        body.Children.Add(candidates); _generation.Content = shell; RestoreFocus(focusTarget);
    }
    internal static int ReadVitalValue(NumericUpDown field, int scale, string name)
    {
        if (field.Value is not { } value || value < field.Minimum || value > field.Maximum ||
            value * scale != decimal.Truncate(value * scale))
        {
            throw new ArgumentException("Preview.InvalidVitalValue",
                $"{name}（{field.Minimum}–{field.Maximum}，最小单位 {1m / scale}）");
        }
        return checked((int)(value * scale));
    }
    internal int? ReadOpticalTarget() => OpticalEnabled.IsChecked == true && Oxygenation.Realtime.IsChecked != true
        ? ReadVitalValue(OpticalTarget, 1000, "SpO₂ 目标") : null;
    internal (int Period, int Inspiration) ReadBreathingTiming()
    {
        var timing = ReadCo2Timing();
        decimal rate = RespiratoryRate.Value ?? throw new ArgumentException("Preview.InvalidBreathingTiming");
        decimal fraction = InspirationPercent.Value ?? throw new ArgumentException("Preview.InvalidBreathingTiming");
        if (rate is < 6 or > 60 || fraction is < 10 or > 90) { throw new ArgumentException("Preview.InvalidBreathingTiming"); }
        int period = checked((int)decimal.Round(60000 / rate, 0, MidpointRounding.ToEven));
        int inspiration = checked((int)decimal.Round(period * fraction / 100, 0, MidpointRounding.ToEven));
        if (inspiration < timing.FallMilliseconds || period - inspiration <= timing.DeadSpaceMilliseconds + timing.RiseMilliseconds)
        { throw new ArgumentException("Preview.InvalidBreathingTiming"); }
        return (period, inspiration);
    }
    private void RefreshBreathingTiming()
    {
        try
        {
            var timing = ReadBreathingTiming();
            BreathingTiming.Text = $"吸气 {timing.Inspiration} ms · 呼气 {timing.Period - timing.Inspiration} ms · 周期 {timing.Period} ms（设置预览，非实测）";
        }
        catch (ArgumentException error) when (error.Message == "Preview.InvalidCo2Timing")
        { BreathingTiming.Text = "当前 CO₂ 时长未填写完整或不是正整数。"; }
        catch (ArgumentException)
        { BreathingTiming.Text = "当前组合不可用：" + BreathingConstraintDescription(); }
    }
    private StackPanel VitalSigns()
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        panel.Children.Add(CardiacRateEnabled);
        Add("心率目标（bpm，30–180）", HeartRate);
        Add("心搏周期慢波动上限（±%，0–5）", RateVariation);
        Add("波动共用种子（64 个小写十六进制字符，256 位）", RateSeed);
        panel.Children.Add(GenerateSeed);
        GenerateSeed.Click += (_, _) => RateSeed.Text = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        panel.Children.Add(DesktopInformationPages.Help("settings-detail-4"));
        Add("基础呼吸频率（次/分，6–60）", RespiratoryRate);
        Add("吸气占周期比例（%，10–90；50表示吸呼1:1）", InspirationPercent);
        panel.Children.Add(BreathingTiming);
        RespiratoryRate.ValueChanged += (_, _) => RefreshBreathingTiming();
        InspirationPercent.ValueChanged += (_, _) => RefreshBreathingTiming();
        Co2DeadSpace.ValueChanged += (_, _) => RefreshBreathingTiming();
        Co2Rise.ValueChanged += (_, _) => RefreshBreathingTiming();
        Co2Fall.ValueChanged += (_, _) => RefreshBreathingTiming();
        RefreshBreathingTiming();
        Add("EtCO₂目标（mmHg，5–80）", EtCo2Target);
        Add("EtCO₂逐呼吸波动（±mmHg，0–5；0关闭）", EtCo2Variation);
        panel.Children.Add(DesktopInformationPages.Help("topic-3"));
        panel.Children.Add(DesktopInformationPages.Help("topic-4"));
        void Add(string label, Control control)
        { control.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(Text(label)); panel.Children.Add(control); AutomationProperties.SetName(control, label); }
        panel.Children.Add(Text("指脉氧"));
        panel.Children.Add(OpticalEnabled);
        panel.Children.Add(Oxygenation);
        panel.Children.Add(Text("SpO₂ 教学目标（0–100%）")); panel.Children.Add(OpticalTarget);
        panel.Children.Add(Text("光学脉动幅度倍率（影响实测 PI）")); panel.Children.Add(OpticalModulation);
        AutomationProperties.SetName(OpticalModulation, "光学脉动幅度倍率，0.1至2");
        AutomationProperties.SetName(OpticalTarget, "SpO₂ 教学目标，百分比，0至100");
        Add("SpO₂波动幅度（±百分点，0–2.5；0关闭）", OpticalVariation);
        void RefreshOpticalControls()
        {
            bool enabled = OpticalEnabled.IsChecked == true;
            OpticalModulation.IsEnabled = enabled;
            OpticalTarget.IsEnabled = OpticalVariation.IsEnabled = enabled && Oxygenation.Realtime.IsChecked != true;
            Oxygenation.IsEnabled = enabled;
        }
        OpticalEnabled.IsCheckedChanged += (_, _) => RefreshOpticalControls();
        Oxygenation.Realtime.IsCheckedChanged += (_, _) => RefreshOpticalControls();
        RefreshOpticalControls();
        panel.Children.Add(DesktopInformationPages.Help("topic-5"));
        panel.Children.Add(DesktopInformationPages.Help("topic-6"));
        Add("ABP脉搏分量倍率（0.5–2）", AbpPulseGain);
        Add("PA脉搏分量倍率（0.5–2）", PaPulseGain);
        panel.Children.Add(DesktopInformationPages.Help("settings-detail-5"));
        Add("CVP基线压力（mmHg，−5–30）", CvpBaseline);
        panel.Children.Add(DesktopInformationPages.Help("topic-7"));
        panel.Children.Add(Text("其他生命体征 · 预留编辑，下列项目尚未接入设置。"));
        foreach (string name in new[] { "无创血压（mmHg）", "体温（°C）", "ABP收缩压/舒张压（mmHg）", "PA收缩压/舒张压（mmHg）" })
        {
            var row = new Grid { ColumnDefinitions = new("220,*") };
            row.Children.Add(Text(name));
            var field = new TextBox { Text = "尚未接入", IsEnabled = false, MaxWidth = 300, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(field, name + "，尚未接入"); Grid.SetColumn(field, 1); row.Children.Add(field); panel.Children.Add(row);
        }
        return panel;
    }
    internal void OpenAdvanced(int channel)
    {
        Tabs.SelectedIndex = 6;
        SectionPages[6].Sections.SelectedIndex = channel switch { 0 => 0, 1 => 1, _ => 2 };
    }
    private void ResetRespirationDraft()
    {
        var defaults = PhysiologyDemoConfiguration.Default;
        string page;
        switch (RespirationGroups.SelectedIndex)
        {
            case 0:
                RespSignalAmplitude.Value = defaults.RespAmplitudeCounts;
                RespCardiacArtifact.Value = defaults.RespCardiacArtifactCounts;
                page = "RESP 信号";
                break;
            case 1:
                Co2Baseline.Value = defaults.Co2BaselineMmHg;
                Co2CustomPlateau.IsChecked = false;
                Co2PlateauStart.Value = 35;
                Co2DeadSpace.Value = defaults.Co2DeadSpaceMilliseconds;
                Co2Rise.Value = defaults.Co2RiseMilliseconds;
                Co2Fall.Value = defaults.Co2FallMilliseconds;
                page = "CO₂ 形态";
                break;
            case 2:
                Co2TransportDelay.Value = defaults.Co2TransportDelayMilliseconds;
                Co2DispersionStep.Value = defaults.Co2DispersionStepMilliseconds;
                page = "CO₂ 管路";
                break;
            default: return;
        }
        Status.Text = $"已恢复 {page} 默认参数，尚未应用；当前运行保持不变。";
    }
    private void RefreshAdvanced(Button developer)
    {
        _advancedEcg.Children.Clear(); _advancedRespiration.Children.Clear();
        _advancedEjection.Children.Clear(); _advancedTools.Children.Clear();
        _respSignal.Children.Clear(); _co2Shape.Children.Clear(); _co2Response.Children.Clear();
        _advancedEcg.Children.Add(DesktopInformationPages.Help("topic-8"));
        var config = DesignPreviewWindow.ResolveStyle(EcgSelection, RespirationSelection, 0);
        RefreshShapeSummary();
        if (config.Ecg.TContour is not null) { _advancedEcg.Children.Add(TContourParameters); }
        if (config.Ecg.Infarction is not null) { _advancedEcg.Children.Add(InfarctionParameters); }
        if (config.Ecg.TContour is null && config.Ecg.Infarction is null)
        { _advancedEcg.Children.Add(Text("此模板暂无可编辑的心电图高级参数。")); }
        _advancedEcg.Children.Add(ShapeEditStatus);
        ToolTip.SetTip(RespirationGroups, "当前呼吸模板：" + RespirationChoices[RespirationSelection]);
        _respSignal.Children.Add(Text("RESP 相对信号幅度（−1000–1000；负值反相，0 隐去呼吸分量）"));
        _respSignal.Children.Add(RespSignalAmplitude);
        _respSignal.Children.Add(Text("心源性干扰幅度（−200–200；0 关闭）"));
        _respSignal.Children.Add(RespCardiacArtifact);
        AutomationProperties.SetName(RespSignalAmplitude, "RESP 相对信号幅度，负值反相，不代表通气量");
        AutomationProperties.SetName(RespCardiacArtifact, "RESP 心源性干扰幅度，0 关闭");
        if (RespirationSelection == 3) { _respSignal.Children.Add(Text("当前无呼吸分量")); }
        _co2Shape.Children.Add(Text("CO₂ 基线（mmHg，0–80；不高于呼气末目标）"));
        _co2Shape.Children.Add(Co2Baseline);
        _co2Shape.Children.Add(Co2CustomPlateau);
        _co2Shape.Children.Add(Text("平台起始高度（mmHg，最多两位小数；基线至呼气末目标之间）"));
        _co2Shape.Children.Add(Co2PlateauStart);
        AutomationProperties.SetName(Co2Baseline, "CO₂ 基线，毫米汞柱");
        AutomationProperties.SetName(Co2PlateauStart, "CO₂ 平台起始高度，毫米汞柱");
        if (_co2Timing.Children.Count == 0)
        {
            foreach (var (label, input) in new[] { ("CO₂ 死腔时长（ms）", Co2DeadSpace), ("CO₂ 上升时长（ms）", Co2Rise), ("CO₂ 下降时长（ms）", Co2Fall) })
            {
                var field = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 16, 12) };
                field.Children.Add(Text(label)); field.Children.Add(input); _co2Timing.Children.Add(field);
                AutomationProperties.SetName(input, label);
            }
        }
        _co2Shape.Children.Add(_co2Timing);
        _co2Response.Children.Add(Text("CO₂ 管路延迟（0–5000 ms）"));
        _co2Response.Children.Add(Co2TransportDelay);
        _co2Response.Children.Add(Text("CO₂ 展宽步长（0–500 ms）"));
        _co2Response.Children.Add(Co2DispersionStep);
        AutomationProperties.SetName(Co2TransportDelay, "CO₂ 管路延迟，毫秒");
        AutomationProperties.SetName(Co2DispersionStep, "CO₂ 展宽步长，毫秒");
        _co2Response.Children.Add(DesktopInformationPages.Help("settings-detail-6"));
        _advancedRespiration.Children.Add(RespirationGroups);
        _advancedRespiration.Children.Add(ResetRespirationPage);
        try
        {
            var selected = DesignPreviewWindow.ResolveStyle(EcgSelection, RespirationSelection, EjectionSelection).Physiology;
            bool noEjection = !selected.VentricularMechanicalEnabled || selected.CardiacActivity is
                Monitor.Simulation.Physiology.CardiacActivity.Absent or Monitor.Simulation.Physiology.CardiacActivity.AtrialOnly ||
                Monitor.Simulation.Physiology.VentricularDisorganizationReference.IsPattern(selected.ConductionPattern);
            _advancedEjection.Children.Add(Text(noEjection ? "当前无有效射血，不提供射血强度编辑。" : "当前射血模板参数由节律与机械事件共同约束，自定义编辑尚未接入。"));
        }
        catch (ArgumentException error) { _advancedEjection.Children.Add(Text("当前组合不兼容：" + error.Message)); }
        _advancedTools.Children.Add(Text("以下为独立开发工具，不会同步本页模板或参数。"));
        _advancedTools.Children.Add(developer);
    }
    private static void RestoreFocus(Control control) => Dispatcher.UIThread.Post(() => { control.Focus(); control.BringIntoView(); }, DispatcherPriority.Loaded);
    private void BuildRows()
    {
        if (Skin.SelectedIndex < 0) { return; }
        var defaults = MonitorDisplayConfiguration.Default((MonitorSkin)Skin.SelectedIndex);
        Slots.Clear(); _slotRows.Children.Clear();
        for (int i = 0; i < defaults.Slots.Count; i++)
        {
            var slot = defaults.Slots[i];
            var channel = new ComboBox { MinWidth = 155 };
            Localization.SetChoices(channel, LiveMonitorTrace.Names.Select((name, index) =>
                (Func<Monitor.Application.Localization.ITextLocalizer, string>)(text => text.Format("display.channelUnit", name,
                    index is 1 or 2 ? text.GetString("display.relative") : LiveMonitorTrace.Units[index]))));
            channel.SelectedIndex = slot.Channel;
            var automatic = new CheckBox { IsChecked = true };
            Localization.Bind(automatic, ContentControl.ContentProperty, "display.automatic");
            var minimum = new TextBox { Text = slot.Range.Minimum.ToString(CultureInfo.InvariantCulture), MinWidth = 85, IsEnabled = false };
            var maximum = new TextBox { Text = slot.Range.Maximum.ToString(CultureInfo.InvariantCulture), MinWidth = 85, IsEnabled = false };
            automatic.IsCheckedChanged += (_, _) => minimum.IsEnabled = maximum.IsEnabled = automatic.IsChecked != true;
            channel.SelectionChanged += (_, _) =>
            {
                if (channel.SelectedIndex < 0) { return; }
                var range = MonitorDisplayConfiguration.ReferenceRange(channel.SelectedIndex);
                minimum.Text = range.Minimum.ToString(CultureInfo.InvariantCulture); maximum.Text = range.Maximum.ToString(CultureInfo.InvariantCulture);
            };
            Localization.Bind(channel, AutomationProperties.NameProperty, "display.rowChannel", i + 1);
            Localization.Bind(minimum, AutomationProperties.NameProperty, "display.rowMinimum", i + 1);
            Localization.Bind(maximum, AutomationProperties.NameProperty, "display.rowMaximum", i + 1);
            var row = new Grid { ColumnDefinitions = new("30,160,70,*,*,115") };
            var speed = new ComboBox { ItemsSource = new[] { "12.5", "25", "50" }, SelectedIndex = 1 };
            Localization.Bind(automatic, AutomationProperties.NameProperty, "display.rowAutomatic", i + 1);
            Localization.Bind(speed, AutomationProperties.NameProperty, "display.rowSpeed", i + 1);
            Control[] children = [Text((i + 1).ToString(CultureInfo.InvariantCulture)), channel, automatic, minimum, maximum, speed];
            for (int column = 0; column < children.Length; column++) { children[column].Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(children[column], column); row.Children.Add(children[column]); }
            _slotRows.Children.Add(row); Slots.Add(new(channel, automatic, minimum, maximum, speed));
        }
    }
    internal void RestoreDisplay(MonitorDisplayConfiguration display, int paperLayout)
    {
        Skin.SelectedIndex = (int)display.Skin;
        for (int i = 0; i < display.Slots.Count; i++)
        {
            var saved = display.Slots[i]; var field = Slots[i];
            field.Channel.SelectedIndex = saved.Channel;
            field.Auto.IsChecked = saved.Automatic;
            field.Minimum.Text = saved.Range.Minimum.ToString(CultureInfo.InvariantCulture);
            field.Maximum.Text = saved.Range.Maximum.ToString(CultureInfo.InvariantCulture);
            field.Speed.SelectedIndex = saved.SpeedTenthsMmPerSecond switch { 125 => 0, 250 => 1, _ => 2 };
        }
        PaperLayout.SelectedIndex = paperLayout;
    }
    internal MonitorDisplayConfiguration ReadDisplay() => new((MonitorSkin)Skin.SelectedIndex, Slots.Select(slot =>
    {
        if (!double.TryParse(slot.Minimum.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double minimum) ||
            !double.TryParse(slot.Maximum.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double maximum))
        { throw new ArgumentException("量程上下限须为有效数值。"); }
        return new MonitorDisplaySlot(slot.Channel.SelectedIndex, slot.Auto.IsChecked == true, new(minimum, maximum), slot.Speed.SelectedIndex switch { 0 => 125, 1 => 250, 2 => 500, _ => 0 });
    }).ToArray());
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private static ScrollViewer Scroll(Control content) => SettingsScroll.Create(content);
    private static StackPanel Note(string title, string body)
    {
        var panel = new StackPanel { Spacing = 18, Margin = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23 }); panel.Children.Add(Text(body)); return panel;
    }
    private StyleThumbnail Thumbnail(Func<StylePreviewData> session, int channel, int? respiration = null) =>
        new(session, channel, () =>
        {
            int selected = respiration ?? RespirationSelection;
            if (!_respirationPreviews.TryGetValue(selected, out var samples))
            { samples = _respirationPreview(selected); _respirationPreviews.Add(selected, samples); }
            return samples;
        });
    private sealed class StyleThumbnail(Func<StylePreviewData> session, int channel, Func<(long TimeNs, double Value)[]> respiration) : Control
    {
        private StylePreviewData? _cachedSource;
        private StreamGeometry? _geometry;
        private (long TimeNs, double Value)[]? _cachedRespiration;
        protected override Size MeasureOverride(Size availableSize) => new(210, 74);
        public override void Render(DrawingContext context)
        {
            var source = channel == 1 ? null : session();
            var respiratory = channel == 1 ? respiration() : null;
            if (_geometry is null || !ReferenceEquals(source, _cachedSource) || !ReferenceEquals(respiratory, _cachedRespiration))
            {
                _cachedSource = source; _cachedRespiration = respiratory;
                var samples = respiratory ?? source!.Samples(channel);
                double duration = channel == 1 ? RespirationPreviewDurationNs : 3e9;
                _geometry = new StreamGeometry();
                using var path = _geometry.Open();
                if (samples.Length > 1)
                {
                    double minimum = samples.Min(s => s.Value), span = Math.Max(1, samples.Max(s => s.Value) - minimum);
                    for (int i = 0; i < samples.Length; i++)
                    {
                        var sample = samples[i];
                        Point point = new((sample.TimeNs - samples[0].TimeNs) / duration * 210, 8 + (1 - (sample.Value - minimum) / span) * 55);
                        if (i == 0) { path.BeginFigure(point, false); } else { path.LineTo(point); }
                    }
                    path.EndFigure(false);
                }
            }
            if (channel == 0 && source is not null) { context.DrawText(new FormattedText($"{source.Lead} 导联", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(DesignPreviewWindow.PreviewFont), 10, Brushes.White), new Point(0, 64)); }
            if (channel == 1) { context.DrawText(new FormattedText("41.25 s · 完整呼吸分组", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(DesignPreviewWindow.PreviewFont), 10, Brushes.White), new Point(0, 64)); }
            if (_geometry is not null) { context.DrawGeometry(null, new Pen(Brush.Parse(LiveMonitorTrace.Colors[channel]), 1.2), _geometry); }
        }
    }
}
