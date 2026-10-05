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
    // Stable template identities, persisted with generator preferences.
    internal static IReadOnlyList<string> EcgTemplateIdentities => EcgChoices;
    internal static IReadOnlyList<string> RespirationTemplateIdentities => RespirationChoices;
    internal static IReadOnlyList<string> EjectionTemplateIdentities => EjectionChoices;
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
        int ecg = ecgSelection ?? EcgSelection;
        int respiration = respirationSelection ?? RespirationSelection;
        int ejection = ejectionSelection ?? EjectionSelection;
        Localization.Bind(AppliedEcgParameters, TextBlock.TextProperty, text =>
            text.Format("advanced.appliedEcg", text.GetString(EcgTemplateKey(ecg)), EcgTemplateSummary.Describe(text, configuration)));
        Localization.Bind(AppliedRespirationParameters, TextBlock.TextProperty, text =>
        {
            string plateau = physiology.Co2PlateauStartCentiMmHg is { } value
                ? (value / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " mmHg" : text.GetString("advanced.plateauFromTemplate");
            return text.Format("advanced.appliedRespiration", text.GetString(RespirationTemplateKey(respiration)),
                physiology.BreathPeriodMilliseconds, physiology.InspirationMilliseconds, physiology.RespAmplitudeCounts, physiology.RespCardiacArtifactCounts,
                physiology.Co2BaselineMmHg, physiology.Co2EndExpiratoryMmHg, plateau, physiology.Co2DeadSpaceMilliseconds, physiology.Co2RiseMilliseconds,
                physiology.Co2FallMilliseconds, physiology.Co2TransportDelayMilliseconds, physiology.Co2DispersionStepMilliseconds);
        });
        bool noEjection = !physiology.VentricularMechanicalEnabled || physiology.CardiacActivity is
            CardiacActivity.Absent or CardiacActivity.AtrialOnly || VentricularDisorganizationReference.IsPattern(physiology.ConductionPattern);
        Localization.Bind(AppliedEjectionParameters, TextBlock.TextProperty, text => text.Format("advanced.appliedEjection",
            text.GetString(EjectionTemplateKey(ejection)), text.GetString(noEjection ? "advanced.noEjection" : "advanced.ejectionFromRhythm")));
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
            string status = !editable ? "advanced.shapeDefaults" : applied ? "advanced.shapeApplied" : "advanced.shapePending";
            Localization.Bind(ShapeEditStatus, TextBlock.TextProperty, status);
            Localization.Bind(ShapeEditSummary, TextBlock.TextProperty, text => EcgTemplateSummary.Describe(text, config));
            // Only drafts that still need Apply are called out.
            ShapeEditStatus.IsVisible = status == "advanced.shapePending";
        }
        catch (ArgumentException)
        {
            Localization.Bind(ShapeEditStatus, TextBlock.TextProperty, "advanced.shapeInvalid");
            Localization.Bind(ShapeEditSummary, TextBlock.TextProperty, "advanced.shapeInvalidHelp");
            ShapeEditStatus.IsVisible = true;
        }
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
        return Localization.Format("vitals.breathingConstraint", timing.FallMilliseconds, timing.DeadSpaceMilliseconds + timing.RiseMilliseconds);
    }
    internal NumericUpDown Co2Baseline { get; } = new() { Minimum = 0, Maximum = 80, Value = 0, Increment = 1, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal CheckBox Co2CustomPlateau { get; } = new();
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
    internal Button Apply { get; } = new() { MinWidth = 88, MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal Button Restart { get; } = new() { MinWidth = 104, MinHeight = 44 };
    internal NumericUpDown ApplyDelaySeconds { get; } = new()
    { Minimum = 0, Maximum = 60, Increment = 0.1m, Value = 3, FormatString = "0.0", Width = 116, Height = 44, VerticalContentAlignment = VerticalAlignment.Center };
    internal long ReadApplyDelayNs()
    {
        if (ApplyDelaySeconds.Value is not { } seconds || seconds < 0 || seconds > 60 || seconds * 10 != decimal.Truncate(seconds * 10))
        { throw new ArgumentException("Preview.InvalidApplyDelay"); }
        return checked((long)(seconds * 1_000_000_000));
    }
    internal Button ResetAll { get; } = new() { MinHeight = 44, Padding = new Thickness(8, 0) };
    internal Button Run { get; } = new() { MinWidth = 104, MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal ComboBox Skin { get; } = new() { SelectedIndex = 1, MinWidth = 220 };
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
    internal SoundSettingsPanel Sound { get; }
    internal MonitorAlertSettings Alerts { get; }
    internal CheckBox OpticalEnabled { get; } = new() { IsChecked = false };
    internal OxygenationSettingsPanel Oxygenation { get; }
    internal CheckBox CardiacRateEnabled { get; } = new() { IsChecked = false };
    internal NumericUpDown HeartRate { get; } = new() { Minimum = 30, Maximum = 180, Value = 75, Increment = 1, Width = 180 };
    internal NumericUpDown RateVariation { get; } = new() { Minimum = 0, Maximum = 5, Value = 0, Increment = .5m, Width = 180 };
    internal Button GenerateSeed { get; } = new() { MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal TextBox RateSeed { get; } = new() { Text = new string('0', 63) + "1", MaxWidth = 650 };
    internal TextBlock SeedError { get; } = new()
    {
        Foreground = Brushes.OrangeRed,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };
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
    internal TextBlock Status { get; } = Text("");
    private const string DisplayColumns = "40,165,80,*,*,115";
    internal sealed record SlotEditor(ComboBox Channel, CheckBox Auto, TextBox Minimum, TextBox Maximum, ComboBox Speed);
    internal List<SlotEditor> Slots { get; } = [];
    private readonly StackPanel _slotRows = new() { Spacing = 12 };
    internal const long RespirationPreviewDurationNs = 41_250_000_000;
    private readonly Dictionary<int, (long TimeNs, double Value)[]> _respirationPreviews = [];
    private readonly Func<int, (long TimeNs, double Value)[]> _respirationPreview;
    private readonly Dictionary<(int, int, int), StylePreviewData> _previews = [];
    private readonly Func<int, int, int, StylePreviewData> _preview;
    private readonly StackPanel _advancedEcg = new() { Spacing = 16 };
    private readonly StackPanel _advancedRespiration = new() { Spacing = 16 };
    internal TabControl RespirationGroups { get; } = new();
    internal Button ResetRespirationPage { get; } = new()
    {
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
    internal ComboBox PaperLayout { get; } = new() { SelectedIndex = 0, MinWidth = 220 };
    private readonly List<Action> _refreshSignalRows = [];
    internal int PreviewCacheCount => _previews.Count;
    internal DesignPreviewSettings(Func<int, int, int, StylePreviewData> preview, Func<int, (long TimeNs, double Value)[]> respirationPreview, Action apply, Action run, Action advanced, DesktopLocalization? localization = null)
    {
        Localization = localization ?? new DesktopLocalization();
        Sound = new SoundSettingsPanel(localization: Localization);
        Alerts = new MonitorAlertSettings(Localization);
        Oxygenation = new OxygenationSettingsPanel(Localization);
        InitializeLocalization();
        _preview = preview; _respirationPreview = respirationPreview;
        TContourParameters.Changed += RefreshShapeSummary;
        InfarctionParameters.Changed += RefreshShapeSummary;
        var generation = new Grid { RowDefinitions = new("Auto,*") };
        generation.Children.Add(DesktopInformationPages.Help("settings-detail-1"));
        var templates = BuildTemplatePages();
        Grid.SetRow(templates, 1); generation.Children.Add(templates);
        var more = new Button { MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        Localization.Bind(more, ContentControl.ContentProperty, "advanced.developerEcg");
        more.Click += (_, _) => advanced();
        var display = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        display.Children.Add(DesktopInformationPages.Help("settings-detail-2"));
        display.Children.Add(Skin);
        display.Children.Add(LocalizedText("display.paperLayout")); display.Children.Add(PaperLayout);
        display.Children.Add(DesktopInformationPages.Help("topic-1"));
        // Headings share the row columns and right margins so translated labels stay aligned.
        var headings = new Grid { ColumnDefinitions = new(DisplayColumns) };
        string[] labels = ["display.row", "display.channel", "display.range", "display.minimum", "display.maximum", "display.speed"];
        for (int column = 0; column < labels.Length; column++)
        {
            var label = LocalizedText(labels[column]);
            label.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(label, column); headings.Children.Add(label);
        }
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
        SectionPages[3] = SettingsSections.Split(Localization, "settings.sound", Sound,
            ("sound.sectionOutput", Sound.Children[0]), ("sound.sectionHeartbeat", Sound.HeartbeatEnabled),
            ("sound.sectionPause", Before(Sound, Sound.PauseSeconds)));
        var alertGroups = new List<(string Title, Control Start)>
        { (Alerts.Parameters[0].Title, Alerts.Children[0]), (Alerts.Parameters[1].Title, Alerts.SpO2Enabled) };
        alertGroups.AddRange(Alerts.Parameters.Skip(2).Select(p => (p.Title, (Control)Alerts.AdditionalLimits.Editors[p.Numeric])));
        int alertSettings = alertGroups.Count;
        alertGroups.Add((ProductIdentity.DevelopmentFeatures ? "alarm.sectionDisplayTest" : "alarm.sectionDisplay", ProductIdentity.DevelopmentFeatures ? (Control)Alerts.TestLevel.Parent! : Alerts.NoticeColorEnabled)); alertGroups.Add(("alarm.sectionRhythm", Alerts.InfoTone));
        alertGroups.Add(("alarm.sectionNotifications", Alerts.NotificationSettings));
        var alarmSections = SettingsSections.Split(Localization, "settings.alarms", Alerts, new Dictionary<int, string> { [0] = "alarm.headerMeasurements", [alertSettings] = "alarm.headerNotices" }, alertGroups.ToArray());
        SectionPages[4] = alarmSections;
        for (int section = 0; section < Alerts.Parameters.Count; section++)
        {
            int index = section;
            var switches = Alerts.SwitchesFor(Alerts.Parameters[index].Numeric);
            void RefreshState()
            {
                bool enabled = switches.Any(s => s.IsChecked == true);
                alarmSections.SetDetail(index, enabled ? "settings.stateOn" : "settings.stateOff", enabled ? "settings.stateEnabled" : "settings.stateDisabled");
            }
            foreach (var toggle in switches) { toggle.IsCheckedChanged += (_, _) => RefreshState(); }
            RefreshState();
        }
        Alerts.NotificationSettings.ParameterRequested += numeric =>
        {
            Alerts.ShowSoundPage(numeric);
            alarmSections.SelectedSection = Alerts.Parameters.Select(p => p.Numeric).ToList().IndexOf(numeric);
        };
        SectionPages[5] = SettingsSections.Split(Localization, "settings.vitals", vitals,
            ("vitals.sectionHeartRate", vitals.Children[0]), ("vitals.sectionBreathing", Before(vitals, RespiratoryRate)),
            ("vitals.sectionOximetry", Oxygenation), ("vitals.sectionPressure", Before(vitals, AbpPulseGain)),
            ("vitals.sectionSeed", Before(vitals, RateSeed)));
        TrackVitalSections(SectionPages[5]);
        var advancedGroups = new List<(string Title, Control Content)>
        { ("generation.ecg", _advancedEcg), ("generation.respiration", _advancedRespiration), ("generation.ejection", _advancedEjection) };
        var appliedParameters = new StackPanel { Spacing = 20 };
        appliedParameters.Children.Add(LocalizedText("advanced.appliedIntro"));
        appliedParameters.Children.Add(AppliedEcgParameters);
        appliedParameters.Children.Add(AppliedRespirationParameters);
        appliedParameters.Children.Add(AppliedEjectionParameters);
        advancedGroups.Add(("advanced.sectionApplied", appliedParameters));
        if (ProductIdentity.DevelopmentFeatures) { advancedGroups.Add(("advanced.sectionTools", _advancedTools)); }
        var advancedHeaders = new Dictionary<int, string> { [0] = "advanced.headerWaveform", [3] = ProductIdentity.DevelopmentFeatures ? "advanced.headerOverviewTools" : "advanced.headerOverview" };
        SectionPages[6] = new SettingsSections(Localization, "settings.advanced", advancedHeaders, advancedGroups.ToArray());
        RespirationGroups.ItemsSource = new[] { ("advanced.respSignal", _respSignal), ("advanced.co2Shape", _co2Shape), ("advanced.co2Response", _co2Response) }
            .Select(page =>
            {
                var tab = new TabItem { Content = page.Item2, Padding = new Thickness(0), Margin = new Thickness(0, 0, 20, 0), FontSize = 14, MinHeight = 44 };
                Localization.Bind(tab, TabItem.HeaderProperty, page.Item1);
                return tab;
            }).ToArray();
        RespirationGroups.Padding = new Thickness(0);
        RespirationGroups.SelectedIndex = 0;
        ResetRespirationPage.Click += (_, _) => ResetRespirationDraft();
        Localization.Bind(RespirationGroups, AutomationProperties.NameProperty, "advanced.respirationGroupsName");
        Localization.Bind(Co2CustomPlateau, ContentControl.ContentProperty, "advanced.customPlateau");
        Localization.Bind(OpticalEnabled, ContentControl.ContentProperty, "vitals.opticalEnabled");
        Localization.Bind(CardiacRateEnabled, ContentControl.ContentProperty, "vitals.cardiacRateEnabled");
        Localization.Bind(GenerateSeed, ContentControl.ContentProperty, "vitals.generateSeed");
        Localization.Bind(SeedError, TextBlock.TextProperty, "vitals.seedError");
        Localization.Bind(ResetRespirationPage, ContentControl.ContentProperty, "advanced.resetPage");
        Localization.LocaleChanged += RefreshBreathingTiming;
        Co2CustomPlateau.IsCheckedChanged += (_, _) => Co2PlateauStart.IsEnabled = Co2CustomPlateau.IsChecked == true;
        string[] categories = ["settings.general", "settings.generation", "settings.display", "settings.sound", "settings.alarms", "settings.vitals", "settings.advanced"];
        Control[] pages = [BuildGeneralPage(), generation, SectionPages[2], SectionPages[3], SectionPages[4], SectionPages[5], SectionPages[6]];
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
            if (selected == 1) { foreach (var page in _templatePages) { page.Refresh(); } }
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
    // name is a catalog key (or a verbatim title) for the field shown in validation text.
    internal static int ReadVitalValue(NumericUpDown field, int scale, string name)
    {
        if (field.Value is not { } value || value < field.Minimum || value > field.Maximum ||
            value * scale != decimal.Truncate(value * scale))
        {
            throw new VitalValueException(name, field.Minimum, field.Maximum, 1m / scale);
        }
        return checked((int)(value * scale));
    }
    internal int? ReadOpticalTarget() => OpticalEnabled.IsChecked == true && Oxygenation.Realtime.IsChecked != true
        ? ReadVitalValue(OpticalTarget, 1000, "vitals.opticalTargetField") : null;
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
            BreathingTiming.Text = Localization.Format("vitals.breathingPreview", timing.Inspiration, timing.Period - timing.Inspiration, timing.Period);
        }
        catch (ArgumentException error) when (error.Message == "Preview.InvalidCo2Timing")
        { BreathingTiming.Text = Localization.Get("vitals.co2TimingIncomplete"); }
        catch (ArgumentException)
        { BreathingTiming.Text = Localization.Format("vitals.breathingUnavailable", BreathingConstraintDescription()); }
    }
    private StackPanel VitalSigns()
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        panel.Children.Add(CardiacRateEnabled);
        Add("vitals.heartRate", HeartRate);
        Add("vitals.rateVariation", RateVariation);
        Add("vitals.respiratoryRate", RespiratoryRate);
        Add("vitals.inspirationPercent", InspirationPercent);
        panel.Children.Add(BreathingTiming);
        RespiratoryRate.ValueChanged += (_, _) => RefreshBreathingTiming();
        InspirationPercent.ValueChanged += (_, _) => RefreshBreathingTiming();
        Co2DeadSpace.ValueChanged += (_, _) => RefreshBreathingTiming();
        Co2Rise.ValueChanged += (_, _) => RefreshBreathingTiming();
        Co2Fall.ValueChanged += (_, _) => RefreshBreathingTiming();
        RefreshBreathingTiming();
        Add("vitals.etco2Target", EtCo2Target);
        Add("vitals.etco2Variation", EtCo2Variation);
        panel.Children.Add(DesktopInformationPages.Help("topic-3"));
        panel.Children.Add(DesktopInformationPages.Help("topic-4"));
        void Add(string key, Control control)
        {
            control.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(LocalizedText(key));
            panel.Children.Add(control);
            Localization.Bind(control, AutomationProperties.NameProperty, key);
        }
        var source = new StackPanel { Spacing = 16 };
        source.Children.Add(OpticalEnabled);
        source.Children.Add(Oxygenation.Realtime);
        source.Children.Add(LocalizedText("vitals.opticalTarget")); source.Children.Add(OpticalTarget);
        source.Children.Add(LocalizedText("vitals.opticalVariation")); source.Children.Add(OpticalVariation);
        source.Children.Add(LocalizedText("vitals.opticalModulation")); source.Children.Add(OpticalModulation);
        Localization.Bind(OpticalModulation, AutomationProperties.NameProperty, "vitals.opticalModulationName");
        Localization.Bind(OpticalTarget, AutomationProperties.NameProperty, "vitals.opticalTargetName");
        Localization.Bind(OpticalVariation, AutomationProperties.NameProperty, "vitals.opticalVariation");
        source.Children.Add(DesktopInformationPages.Help("topic-5"));
        source.Children.Add(DesktopInformationPages.Help("topic-6"));
        Oxygenation.SetSourceContent(source);
        panel.Children.Add(Oxygenation);
        void RefreshOpticalControls()
        {
            bool enabled = OpticalEnabled.IsChecked == true;
            OpticalModulation.IsEnabled = enabled;
            OpticalTarget.IsEnabled = OpticalVariation.IsEnabled = enabled && Oxygenation.Realtime.IsChecked != true;
            Oxygenation.SetSourceEnabled(enabled);
        }
        OpticalEnabled.IsCheckedChanged += (_, _) => RefreshOpticalControls();
        Oxygenation.Realtime.IsCheckedChanged += (_, _) => RefreshOpticalControls();
        RefreshOpticalControls();
        Add("vitals.abpPulseGain", AbpPulseGain);
        Add("vitals.paPulseGain", PaPulseGain);
        panel.Children.Add(DesktopInformationPages.Help("settings-detail-5"));
        Add("vitals.cvpBaseline", CvpBaseline);
        panel.Children.Add(DesktopInformationPages.Help("topic-7"));
        Add("vitals.seed", RateSeed);
        panel.Children.Add(SeedError);
        panel.Children.Add(GenerateSeed);
        GenerateSeed.Click += (_, _) => RateSeed.Text = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        RateSeed.TextChanged += (_, _) => SeedError.IsVisible = !IsSeedFormatValid(RateSeed.Text);
        panel.Children.Add(DesktopInformationPages.Help("settings-detail-4"));
        return panel;
    }
    // Mirrors DeterministicStreamFactory.FromLowercaseHex so the draft can be corrected before apply.
    internal static bool IsSeedFormatValid(string? seed) =>
        seed is { Length: 64 } && seed.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private void TrackVitalSections(SettingsSections sections)
    {
        bool Filled(params NumericUpDown[] fields) => fields.All(field => field.Value is not null);
        bool OxygenationValid()
        {
            if (OpticalEnabled.IsChecked != true) { return true; }
            if (Oxygenation.Realtime.IsChecked != true) { return Filled(OpticalTarget, OpticalVariation, OpticalModulation); }
            try { Oxygenation.Capture(true); }
            catch (ArgumentException) { return false; }
            return Filled(OpticalModulation);
        }
        bool BreathingValid()
        {
            try { ReadBreathingTiming(); }
            catch (ArgumentException) { return false; }
            return Filled(EtCo2Target, EtCo2Variation);
        }
        var states = new (CheckBox? Toggle, Func<bool> Valid)[]
        {
            (CardiacRateEnabled, () => Filled(HeartRate, RateVariation)),
            (null, BreathingValid),
            (OpticalEnabled, OxygenationValid),
            (null, () => Filled(AbpPulseGain, PaPulseGain, CvpBaseline)),
            (null, () => IsSeedFormatValid(RateSeed.Text))
        };
        void Refresh()
        {
            for (int section = 0; section < states.Length; section++)
            {
                var (toggle, valid) = states[section];
                if (!valid()) { sections.SetDetail(section, "settings.stateNeedsFix", "settings.stateNeedsFix"); }
                else if (toggle is null) { sections.SetDetail(section, null); }
                else
                {
                    bool enabled = toggle.IsChecked == true;
                    sections.SetDetail(section, enabled ? "settings.stateOn" : "settings.stateOff", enabled ? "settings.stateEnabled" : "settings.stateDisabled");
                }
            }
        }
        var patient = Oxygenation.Patient;
        var vitalFields = new[] { HeartRate, RateVariation, RespiratoryRate, InspirationPercent, EtCo2Target, EtCo2Variation, OpticalTarget,
            OpticalVariation, OpticalModulation, AbpPulseGain, PaPulseGain, CvpBaseline, Co2DeadSpace, Co2Rise, Co2Fall,
            Oxygenation.TidalVolume, Oxygenation.DeadSpace, Oxygenation.InspiredOxygen, Oxygenation.DemandMultiplier,
            patient.Age, patient.PatientHeight, patient.Weight, patient.BloodVolume, patient.Frc, patient.Hemoglobin, patient.BasalDemand };
        foreach (var field in vitalFields) { field.ValueChanged += (_, _) => Refresh(); }
        var toggles = new[] { CardiacRateEnabled, OpticalEnabled, Oxygenation.Realtime, Oxygenation.AirwayOpen, patient.UseDefaults,
            patient.OverrideBloodVolume, patient.OverrideFrc, patient.OverrideHemoglobin, patient.OverrideBasalDemand };
        foreach (var toggle in toggles) { toggle.IsCheckedChanged += (_, _) => Refresh(); }
        patient.Sex.SelectionChanged += (_, _) => Refresh();
        RateSeed.TextChanged += (_, _) => Refresh();
        Refresh();
    }
    internal void OpenAdvanced(int channel)
    {
        Tabs.SelectedIndex = 6;
        SectionPages[6].SelectedSection = channel switch { 0 => 0, 1 => 1, _ => 2 };
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
                page = "advanced.respSignal";
                break;
            case 1:
                Co2Baseline.Value = defaults.Co2BaselineMmHg;
                Co2CustomPlateau.IsChecked = false;
                Co2PlateauStart.Value = 35;
                Co2DeadSpace.Value = defaults.Co2DeadSpaceMilliseconds;
                Co2Rise.Value = defaults.Co2RiseMilliseconds;
                Co2Fall.Value = defaults.Co2FallMilliseconds;
                page = "advanced.co2Shape";
                break;
            case 2:
                Co2TransportDelay.Value = defaults.Co2TransportDelayMilliseconds;
                Co2DispersionStep.Value = defaults.Co2DispersionStepMilliseconds;
                page = "advanced.co2Response";
                break;
            default: return;
        }
        Localization.Bind(Status, TextBlock.TextProperty, text => text.Format("settings.pageDefaultsRestored", text.GetString(page)));
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
        bool ecgEditable = config.Ecg.TContour is not null || config.Ecg.Infarction is not null;
        if (!ecgEditable)
        { _advancedEcg.Children.Add(LocalizedText("advanced.ecgNoParameters")); }
        SectionPages[6].SetDetail(0, ecgEditable ? null : "advanced.noParameters", ecgEditable ? null : "advanced.noParametersName");
        _advancedEcg.Children.Add(ShapeEditStatus);
        int respirationTemplate = RespirationSelection;
        Localization.Bind(RespirationGroups, ToolTip.TipProperty, text => text.Format("advanced.currentRespiration", text.GetString(RespirationTemplateKey(respirationTemplate))));
        _respSignal.Children.Add(LocalizedText("advanced.respAmplitude"));
        _respSignal.Children.Add(RespSignalAmplitude);
        _respSignal.Children.Add(LocalizedText("advanced.respArtifact"));
        _respSignal.Children.Add(RespCardiacArtifact);
        Localization.Bind(RespSignalAmplitude, AutomationProperties.NameProperty, "advanced.respAmplitudeName");
        Localization.Bind(RespCardiacArtifact, AutomationProperties.NameProperty, "advanced.respArtifactName");
        if (RespirationSelection == 3) { _respSignal.Children.Add(LocalizedText("advanced.noRespiration")); }
        _co2Shape.Children.Add(LocalizedText("advanced.co2Baseline"));
        _co2Shape.Children.Add(Co2Baseline);
        _co2Shape.Children.Add(Co2CustomPlateau);
        _co2Shape.Children.Add(LocalizedText("advanced.plateauStart"));
        _co2Shape.Children.Add(Co2PlateauStart);
        Localization.Bind(Co2Baseline, AutomationProperties.NameProperty, "advanced.co2BaselineName");
        Localization.Bind(Co2PlateauStart, AutomationProperties.NameProperty, "advanced.plateauStartName");
        if (_co2Timing.Children.Count == 0)
        {
            foreach (var (key, input) in new[] { ("advanced.co2DeadSpace", Co2DeadSpace), ("advanced.co2Rise", Co2Rise), ("advanced.co2Fall", Co2Fall) })
            {
                var field = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 16, 12) };
                field.Children.Add(LocalizedText(key)); field.Children.Add(input); _co2Timing.Children.Add(field);
                Localization.Bind(input, AutomationProperties.NameProperty, key);
            }
        }
        _co2Shape.Children.Add(_co2Timing);
        _co2Response.Children.Add(LocalizedText("advanced.co2Delay"));
        _co2Response.Children.Add(Co2TransportDelay);
        _co2Response.Children.Add(LocalizedText("advanced.co2Dispersion"));
        _co2Response.Children.Add(Co2DispersionStep);
        Localization.Bind(Co2TransportDelay, AutomationProperties.NameProperty, "advanced.co2DelayName");
        Localization.Bind(Co2DispersionStep, AutomationProperties.NameProperty, "advanced.co2DispersionName");
        _co2Response.Children.Add(DesktopInformationPages.Help("settings-detail-6"));
        _advancedRespiration.Children.Add(RespirationGroups);
        _advancedRespiration.Children.Add(ResetRespirationPage);
        try
        {
            var selected = DesignPreviewWindow.ResolveStyle(EcgSelection, RespirationSelection, EjectionSelection).Physiology;
            bool noEjection = !selected.VentricularMechanicalEnabled || selected.CardiacActivity is
                Monitor.Simulation.Physiology.CardiacActivity.Absent or Monitor.Simulation.Physiology.CardiacActivity.AtrialOnly ||
                Monitor.Simulation.Physiology.VentricularDisorganizationReference.IsPattern(selected.ConductionPattern);
            _advancedEjection.Children.Add(LocalizedText(noEjection ? "advanced.ejectionNone" : "advanced.ejectionConstrained"));
            SectionPages[6].SetDetail(2, "advanced.noParameters", "advanced.noParametersName");
        }
        catch (ArgumentException error)
        {
            var incompatible = Text("");
            string reason = error.Message;
            Localization.Bind(incompatible, TextBlock.TextProperty, text => text.Format("advanced.ejectionIncompatible", reason));
            _advancedEjection.Children.Add(incompatible);
            SectionPages[6].SetDetail(2, "advanced.incompatible", "advanced.incompatibleName");
        }
        _advancedTools.Children.Add(LocalizedText("advanced.toolsNote"));
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
                    LiveMonitorTrace.Unit(text, index)))));
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
            var row = new Grid { ColumnDefinitions = new(DisplayColumns) };
            var speed = new ComboBox { ItemsSource = new[] { "12.5", "25", "50" }, SelectedIndex = 1 };
            Localization.Bind(automatic, AutomationProperties.NameProperty, "display.rowAutomatic", i + 1);
            Localization.Bind(speed, AutomationProperties.NameProperty, "display.rowSpeed", i + 1);
            var number = Text((i + 1).ToString(CultureInfo.InvariantCulture));
            number.VerticalAlignment = VerticalAlignment.Center;
            Control[] children = [number, channel, automatic, minimum, maximum, speed];
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
        { throw new ArgumentException("Preview.InvalidRange"); }
        return new MonitorDisplaySlot(slot.Channel.SelectedIndex, slot.Auto.IsChecked == true, new(minimum, maximum), slot.Speed.SelectedIndex switch { 0 => 125, 1 => 250, 2 => 500, _ => 0 });
    }).ToArray());
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
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
        }, Localization);
    private sealed class StyleThumbnail(Func<StylePreviewData> session, int channel, Func<(long TimeNs, double Value)[]> respiration, DesktopLocalization localization) : Control
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
            string? caption = channel == 0 && source is not null ? localization.Format("generation.thumbnailLead", source.Lead.ToString())
                : channel == 1 ? localization.Get("generation.thumbnailRespiration") : null;
            if (caption is not null)
            { context.DrawText(new FormattedText(caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(DesignPreviewWindow.PreviewFont), 10, Brushes.White), new Point(0, 64)); }
            if (_geometry is not null) { context.DrawGeometry(null, new Pen(Brush.Parse(LiveMonitorTrace.Colors[channel]), 1.2), _geometry); }
        }
    }
}

// An invalid draft field. ParamName is the field's label; the window formats the
// range and step in the selected language instead of baking them into the message.
internal sealed class VitalValueException(string fieldName, decimal minimum, decimal maximum, decimal step)
    : ArgumentException("Preview.InvalidVitalValue", fieldName)
{
    internal string FieldName { get; } = fieldName;
    internal decimal Minimum { get; } = minimum;
    internal decimal Maximum { get; } = maximum;
    internal decimal Step { get; } = step;
}
