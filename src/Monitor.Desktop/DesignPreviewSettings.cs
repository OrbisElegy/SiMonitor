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

namespace Monitor.Desktop;

internal sealed class DesignPreviewSettings : UserControl
{
    private static readonly string[] EcgChoices = ["窦性参考", "窦性停搏（无逸搏）", "单形室早", "窦性心律不齐", "房性早搏", "交界性早搏", "房颤（粗波）", "房颤（细波）", "房扑（2:1下传）", "房扑（4:1下传）", "二度Ⅰ型4:3", "二度Ⅰ型3:2", "二度Ⅰ型5:4", "二度Ⅱ型3:2（窄QRS）", "二度Ⅱ型4:3（窄QRS）", "二度2:1（不分型）", "二度Ⅱ型4:3＋RBBB", "二度Ⅱ型4:3＋LBBB", "三度AVB（交界性逸搏）", "三度AVB（室性逸搏）", "室扑", "室颤（粗波）", "室颤（细波）", "室上速（窄QRS）", "室上速伴RBBB", "室上速伴LBBB", "单形室速", "室速伴融合波", "室速伴心室夺获", "双向室速", "室速扭转形态示意", "加速性房性自主心律", "加速性交界性自主心律", "加速性室性自主心律", "加速性室性自主心律伴融合", "加速性室性自主心律伴同相夺获", "房性逸搏心律", "房扑（1:1下传）", "房扑（3:1下传）", "房扑（2:1／3:1／4:1交替）", "房早（未下传）", "房早伴RBBB差异传导", "交界性早搏（P′后置）", "交界性早搏（P′重叠）", "室早二联律", "室早三联律", "成对室早", "插入性室早", "双形态室早示意", "多源室早示意", "多形性成对室早", "R-on-T室早（长QT）", "R-on-T室早（短联律）", "完全性右束支阻滞", "不完全性右束支阻滞", "完全性左束支阻滞", "不完全性左束支阻滞", "左前分支阻滞", "左后分支阻滞", "WPW示意（V1正向）", "WPW示意（V1负向）", "WPW较小δ（V1正向）", "WPW较小δ（V1负向）", "短PR（无δ波）", "正常PR伴δ波", "延长PR伴δ波", "粗房颤伴Ashman样差异传导", "细房颤伴Ashman样差异传导", "粗房颤伴脉搏短绌示意", "细房颤伴脉搏短绌示意", "粗房颤伴差异传导及短绌", "细房颤伴差异传导及短绌", "全心静止", "无脉电活动（窦性电活动示意）", "心室静止（保留心房活动）", "高钾样高尖T波（复极示意）", "高钾样传导异常", "高钾样传导异常（无P波）", "低钾样低平T／U波增高", "低钾样倒置T波", "低钾样T–U融合", "低钾样P增高／QRS增宽", "高钙样短QT", "低钙样长QT", "高钙样ST段消失", "低钙样长QT／低平T", "低钙样长QT／倒置T", "洋地黄样鱼钩ST–T", "洋地黄样低平T", "洋地黄样倒置T", "奎尼丁样低平T", "奎尼丁样倒置T", "奎尼丁样宽QRS／低平T", "奎尼丁样宽QRS／倒置T", "奎尼丁样P切迹／低平T", "奎尼丁样P切迹／倒置T", "奎尼丁样P切迹／宽QRS／低平T", "奎尼丁样P切迹／宽QRS／倒置T", "高钾样QRS–T融合", "左房异常P波", "右房异常P波", "双房异常P波", "左室肥厚伴劳损", "右室肥厚伴劳损", "双室肥厚综合征象", "重度右室肥厚qR型", "肺源性心脏改变示意", "Ⅱ导联正负双向T", "Ⅱ导联负正双向T", "Ⅱ导联双峰T", "Ⅱ导联对称倒置T", "Ⅱ导联高尖T", "Ⅱ导联宽大T", "Ⅱ导联低平T", "Ⅱ导联倒置T", "下壁超急性高T", "下壁超急性损伤", "下壁急性单向曲线", "下壁急性Q波／倒置T", "下壁急性QS／倒置T", "下壁亚急性深倒T", "下壁亚急性T变浅", "下壁陈旧Q波／正常T", "下壁陈旧Q波／倒置T", "下壁陈旧Q波／低平T", "侧壁超急性高T", "侧壁超急性损伤", "侧壁急性单向曲线", "侧壁急性Q波／倒置T", "侧壁急性QS／倒置T", "侧壁亚急性深倒T", "侧壁亚急性T变浅", "侧壁陈旧Q波／正常T", "侧壁陈旧Q波／倒置T", "侧壁陈旧Q波／低平T"];
    internal static int EcgChoiceCount => EcgChoices.Length;
    private static readonly string[] RespirationChoices = ["规则呼吸", "潮式呼吸", "间断呼吸示意", "无呼吸分量"];
    private static readonly string[] EjectionChoices = ["随当前节律", "早搏弱射血（需室早）", "2:1漏搏（需窦性参考）", "无有效射血"];
    internal int EcgSelection { get; set; }
    internal int RespirationSelection { get; set; }
    internal int EjectionSelection { get; set; }
    internal Button Apply { get; } = new() { Content = "应用并从头开始", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal Button Run { get; } = new() { Content = "暂停生成", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal ComboBox Skin { get; } = new() { ItemsSource = new[] { "紧凑 · 固定 3 行", "标准 · 固定 5 行", "扩展 · 固定 7 行" }, SelectedIndex = 1, MinWidth = 220 };
    internal ListBox Tabs { get; } = new();
    internal Dictionary<int, SettingsSections> SectionPages { get; } = [];
    private readonly ComboBox _compactCategory = new() { MinHeight = 44, MinWidth = 220 };
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
    internal NumericUpDown OpticalTarget { get; } = new() { Minimum = 75, Maximum = 100, Value = 98, Increment = .1m, FormatString = "0.#", IsEnabled = false, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown OpticalModulation { get; } = new() { Minimum = .1m, Maximum = 2, Value = 1, Increment = .1m, IsEnabled = false, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBlock Status { get; } = Text("选择后应用；显示设置不改变患者原始波形。");
    internal sealed record SlotEditor(ComboBox Channel, CheckBox Auto, TextBox Minimum, TextBox Maximum, ComboBox Speed);
    internal List<SlotEditor> Slots { get; } = [];
    private readonly StackPanel _slotRows = new() { Spacing = 12 };
    internal const long RespirationPreviewDurationNs = 41_250_000_000;
    private readonly Dictionary<int, (long TimeNs, double Value)[]> _respirationPreviews = [];
    private readonly Func<int, (long TimeNs, double Value)[]> _respirationPreview;
    private readonly ContentControl _generation = new();
    private readonly Dictionary<(int, int, int), StylePreviewData> _previews = [];
    private readonly Func<int, int, int, StylePreviewData> _preview;
    private readonly StackPanel _advancedParameters = new() { Spacing = 16, Margin = new Thickness(20) };
    internal ComboBox PaperLayout { get; } = new() { ItemsSource = new[] { "3 × 4 ＋ 长Ⅱ", "6 × 2 ＋ 长Ⅱ" }, SelectedIndex = 0, MinWidth = 220 };
    private Control? _home;
    private Button? _returnFocus;
    internal int PreviewCacheCount => _previews.Count;
    internal DesignPreviewSettings(Func<int, int, int, StylePreviewData> preview, Func<int, (long TimeNs, double Value)[]> respirationPreview, Action apply, Action run, Action advanced)
    {
        _preview = preview; _respirationPreview = respirationPreview;
        AutomationProperties.SetName(Skin, "监护皮肤与固定行数");
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
        generation.Children.Add(Text("具体波形提供预生成预览。浏览分类不会改变波形；选择后仍需应用。"));
        _home = generation; _generation.Content = generation;
        var display = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        display.Children.Add(Text("监护皮肤决定固定槽位数；调整窗口不增减行数。每行可独立选通道和量程。"));
        display.Children.Add(Skin);
        display.Children.Add(Text("纸质十二导联排布")); display.Children.Add(PaperLayout);
        AutomationProperties.SetName(PaperLayout, "纸质十二导联排布");
        display.Children.Add(DesktopInformationPages.Help("topic-1"));
        var headings = new Grid { ColumnDefinitions = new("30,160,70,*,*,115") };
        string[] labels = ["行", "通道 / 单位", "量程", "下限", "上限", "扫速 mm/s* "];
        for (int column = 0; column < labels.Length; column++)
        { var label = Text(labels[column]); Grid.SetColumn(label, column); headings.Children.Add(label); }
        display.Children.Add(headings);
        display.Children.Add(_slotRows);
        display.Children.Add(DesktopInformationPages.Help("topic-2"));
        display.Children.Add(Text("十二导联：整张纸按显示区等比适配，保持纸格/波形/标定的相对比例；不校准屏幕毫米，允许高幅波形跨导联区域。"));
        Skin.SelectionChanged += (_, _) => BuildRows(); BuildRows();
        var vitals = (StackPanel)VitalSigns();
        Control Before(StackPanel panel, Control control) => panel.Children[panel.Children.IndexOf(control) - 1];
        var paper = new StackPanel { Spacing = 16 };
        Control paperLabel = Before(display, PaperLayout), paperHelp = display.Children[^1];
        foreach (var control in new[] { paperLabel, PaperLayout, paperHelp }) { display.Children.Remove(control); paper.Children.Add(control); }
        display.Margin = new Thickness(0);
        SectionPages[1] = new SettingsSections("显示", ("监护波形", display), ("十二导联纸图", paper));
        SectionPages[2] = SettingsSections.Split("声音", Sound,
            ("输出与主音量", Sound.Children[0]), ("心搏提示音", Sound.HeartbeatEnabled),
            ("报警声音暂停", Before(Sound, Sound.PauseSeconds)));
        SectionPages[3] = SettingsSections.Split("报警", Alerts,
            ("ECG 心率", Alerts.Children[0]), ("SpO₂", Alerts.SpO2Enabled),
            ("其他测量参数", Alerts.AdditionalLimits), ("CO₂ 呼吸检测", Alerts.NoExpirationEnabled),
            ("显示与联调", (Control)Alerts.TestLevel.Parent!), ("声音节奏", Alerts.InfoTone));
        SectionPages[4] = SettingsSections.Split("生命体征", vitals,
            ("心率", vitals.Children[0]), ("共用随机种子", Before(vitals, RateSeed)),
            ("呼吸与 CO₂", Before(vitals, RespiratoryRate)), ("指脉氧", Before(vitals, OpticalEnabled)),
            ("压力", Before(vitals, AbpPulseGain)));
        string[] categories = ["波形生成", "显示", "声音", "报警", "生命体征", "高级参数"];
        Control[] pages = [Scroll(_generation), SectionPages[1], SectionPages[2], SectionPages[3], SectionPages[4], Scroll(_advancedParameters)];
        var detail = new ContentControl();
        var navigation = new Grid { ColumnDefinitions = new("160,*"), Margin = new Thickness(12, 0) };
        SettingsSections.StyleNavigation(Tabs);
        Tabs.ItemsSource = categories.Select(title => SettingsSections.Item(title)).ToArray();
        AutomationProperties.SetName(Tabs, "设置分类"); AutomationProperties.SetName(_compactCategory, "设置分类");
        _compactCategory.ItemsSource = categories;
        navigation.Children.Add(Tabs); Grid.SetColumn(detail, 1); navigation.Children.Add(detail);
        Tabs.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.Source, Tabs) || Tabs.SelectedIndex < 0) { return; }
            int selected = Tabs.SelectedIndex; _compactCategory.SelectedIndex = selected;
            if (selected == 5) { RefreshAdvanced(more); }
            detail.Content = pages[selected];
        };
        _compactCategory.SelectionChanged += (_, args) => { if (ReferenceEquals(args.Source, _compactCategory) && _compactCategory.SelectedIndex >= 0) { Tabs.SelectedIndex = _compactCategory.SelectedIndex; } };
        _adaptNavigation = width =>
        {
            bool compact = width < 1040; Tabs.IsVisible = !compact; _compactCategory.IsVisible = compact;
            navigation.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 160);
        };
        Tabs.SelectedIndex = 0;
        var controls = new WrapPanel { Margin = new Thickness(20, 12), Orientation = Orientation.Horizontal };
        Apply.Margin = new Thickness(0, 0, 12, 0); controls.Children.Add(Apply); controls.Children.Add(Run);
        Apply.Click += (_, _) => apply(); Run.Click += (_, _) => run();
        var root = new Grid { RowDefinitions = new("Auto,*,Auto,Auto") };
        _compactCategory.Margin = new Thickness(20, 12); root.Children.Add(_compactCategory);
        Grid.SetRow(navigation, 1); root.Children.Add(navigation); Grid.SetRow(controls, 2); root.Children.Add(controls);
        Status.Margin = new Thickness(20, 0, 20, 16); Grid.SetRow(Status, 3); root.Children.Add(Status); Content = root;
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
        string Group(int i) => channel == 0 ? i switch { 0 or 1 or 3 => "窦性心律", 4 or 6 or 7 or 8 or 9 or 31 or 36 or 37 or 38 or 39 or 40 or 41 => "房性心律", 5 or 32 or 42 or 43 => "交界性心律", >= 23 and <= 25 => "室上性心动过速", >= 10 and <= 19 => "房室传导阻滞", >= 53 and <= 58 => "室内传导阻滞", >= 59 and <= 65 => "预激与PR变异", >= 66 and <= 71 => "房性心律", 72 or 73 or 74 => "静止与电机械分离", >= 75 and <= 98 => "电解质与药物形态", >= 99 and <= 101 => "心房形态", >= 102 and <= 106 => "心室形态", >= 107 and <= 114 => "T波形态", >= 115 and <= 124 => "下壁梗死形态", >= 125 and <= 134 => "侧壁梗死形态", _ => "室性心律" }
            : channel == 1 ? i == 0 ? "规则呼吸" : "异常呼吸示意" : i == 0 ? "节律相关" : "异常射血示意";
        var shell = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        var navigation = new WrapPanel { Orientation = Orientation.Horizontal };
        var back = new Button { Content = "返回波形设置", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        back.Click += (_, _) => { _generation.Content = _home; if (_returnFocus is { } origin) { RestoreFocus(origin); } };
        navigation.Children.Add(back);
        Button focusTarget = back;
        if (activeGroup is not null)
        {
            var parent = new Button { Content = "返回" + title + "分组", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            parent.Click += (_, _) => OpenChooser(title, channel, choices, read, write);
            navigation.Children.Add(parent);
        }
        shell.Children.Add(navigation);
        shell.Children.Add(new TextBlock { Text = "波形生成 / " + title + (activeGroup is null ? "" : " / " + activeGroup), FontSize = 20, FontWeight = FontWeight.SemiBold });
        shell.Children.Add(Text("当前选择：" + choices[read()] + " · 应用后生效"));
        if (activeGroup is null)
        {
            foreach (string group in Enumerable.Range(0, choices.Length).Select(Group).Distinct())
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
                shell.Children.Add(button);
                if (current) { focusTarget = button; }
            }
            _generation.Content = shell; RestoreFocus(focusTarget); return;
        }
        var parameters = new Button { Content = "当前波形高级参数", MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        parameters.Click += (_, _) => Tabs.SelectedIndex = 5;
        shell.Children.Add(parameters);
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
        shell.Children.Add(candidates); _generation.Content = shell; RestoreFocus(focusTarget);
    }
    internal int? ReadOpticalTarget() => OpticalEnabled.IsChecked == true
        ? checked((int)((OpticalTarget.Value ?? throw new ArgumentException("SpO2 target required")) * 1000)) : null;
    internal (int Period, int Inspiration) ReadBreathingTiming()
    {
        decimal rate = RespiratoryRate.Value ?? throw new ArgumentException("Preview.InvalidBreathingTiming");
        decimal fraction = InspirationPercent.Value ?? throw new ArgumentException("Preview.InvalidBreathingTiming");
        if (rate is < 6 or > 60 || fraction is < 10 or > 90) { throw new ArgumentException("Preview.InvalidBreathingTiming"); }
        int period = checked((int)decimal.Round(60000 / rate, 0, MidpointRounding.ToEven));
        int inspiration = checked((int)decimal.Round(period * fraction / 100, 0, MidpointRounding.ToEven));
        var reference = PhysiologyDemoConfiguration.Default;
        if (inspiration < reference.Co2FallMilliseconds || period - inspiration <= reference.Co2DeadSpaceMilliseconds + reference.Co2RiseMilliseconds)
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
        catch (ArgumentException)
        { BreathingTiming.Text = "当前组合不可用：吸气须至少200 ms，呼气须大于375 ms。请调整呼吸频率或吸气占比。"; }
    }
    private StackPanel VitalSigns()
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        panel.Children.Add(CardiacRateEnabled);
        Add("心率目标（bpm，30–180）", HeartRate);
        Add("心搏周期慢波动上限（±%，0–5）", RateVariation);
        Add("波动共用种子（64位小写十六进制）", RateSeed);
        panel.Children.Add(GenerateSeed);
        GenerateSeed.Click += (_, _) => RateSeed.Text = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        panel.Children.Add(Text("使用系统加密随机源生成256位种子；应用后生效。保留种子与设置即可复现。"));
        Add("基础呼吸频率（次/分，6–60）", RespiratoryRate);
        Add("吸气占周期比例（%，10–90；50表示吸呼1:1）", InspirationPercent);
        panel.Children.Add(BreathingTiming);
        RespiratoryRate.ValueChanged += (_, _) => RefreshBreathingTiming();
        InspirationPercent.ValueChanged += (_, _) => RefreshBreathingTiming();
        RefreshBreathingTiming();
        Add("EtCO₂目标（mmHg，5–80）", EtCo2Target);
        Add("EtCO₂逐呼吸波动（±mmHg，0–5；0关闭）", EtCo2Variation);
        panel.Children.Add(DesktopInformationPages.Help("topic-3"));
        panel.Children.Add(DesktopInformationPages.Help("topic-4"));
        void Add(string label, Control control)
        { control.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(Text(label)); panel.Children.Add(control); AutomationProperties.SetName(control, label); }
        panel.Children.Add(Text("指脉氧"));
        panel.Children.Add(OpticalEnabled);
        panel.Children.Add(Text("SpO₂ 教学目标（75–100%）")); panel.Children.Add(OpticalTarget);
        panel.Children.Add(Text("光学脉动幅度倍率（影响实测 PI）")); panel.Children.Add(OpticalModulation);
        AutomationProperties.SetName(OpticalModulation, "光学脉动幅度倍率，0.1至2");
        AutomationProperties.SetName(OpticalTarget, "SpO₂ 教学目标，百分比，75至100");
        Add("SpO₂波动幅度（±百分点，0–2.5；0关闭）", OpticalVariation);
        OpticalEnabled.IsCheckedChanged += (_, _) => OpticalTarget.IsEnabled = OpticalModulation.IsEnabled = OpticalVariation.IsEnabled = OpticalEnabled.IsChecked == true;
        panel.Children.Add(DesktopInformationPages.Help("topic-5"));
        panel.Children.Add(DesktopInformationPages.Help("topic-6"));
        Add("ABP脉搏分量倍率（0.5–2）", AbpPulseGain);
        Add("PA脉搏分量倍率（0.5–2）", PaPulseGain);
        panel.Children.Add(Text("调整压力波形的脉搏分量，保留长间期回落；均压仍从采样计算。此项不是收缩压/舒张压目标，也不是显示缩放。"));
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
    private void RefreshAdvanced(Button developer)
    {
        _advancedParameters.Children.Clear();
        _advancedParameters.Children.Add(Text("当前波形高级参数 · 模板默认值（非运行值）"));
        _advancedParameters.Children.Add(DesktopInformationPages.Help("topic-8"));
        _advancedParameters.Children.Add(Text("心电图 · " + EcgChoices[EcgSelection]));
        var config = DesignPreviewWindow.ResolveStyle(EcgSelection, RespirationSelection, 0);
        _advancedParameters.Children.Add(Text(EcgTemplateSummary.Describe(config.Ecg)));
        _advancedParameters.Children.Add(Text("呼吸 · " + RespirationChoices[RespirationSelection]));
        _advancedParameters.Children.Add(Text(RespirationSelection == 3 ? "当前无呼吸分量，不提供吸呼比、呼吸深度等编辑。" :
            $"周期 {config.Physiology.BreathPeriodMilliseconds} ms · 吸气 {config.Physiology.InspirationMilliseconds} ms · 相对深度 {config.Physiology.RespAmplitudeCounts}"));
        _advancedParameters.Children.Add(Text("射血 · " + EjectionChoices[EjectionSelection]));
        try
        {
            var selected = DesignPreviewWindow.ResolveStyle(EcgSelection, RespirationSelection, EjectionSelection).Physiology;
            bool noEjection = !selected.VentricularMechanicalEnabled || selected.CardiacActivity is
                Monitor.Simulation.Physiology.CardiacActivity.Absent or Monitor.Simulation.Physiology.CardiacActivity.AtrialOnly ||
                Monitor.Simulation.Physiology.VentricularDisorganizationReference.IsPattern(selected.ConductionPattern);
            _advancedParameters.Children.Add(Text(noEjection ? "当前无有效射血，不提供射血强度编辑。" : "当前射血模板参数由节律与机械事件共同约束，自定义编辑尚未接入。"));
        }
        catch (ArgumentException error) { _advancedParameters.Children.Add(Text("当前组合不兼容：" + error.Message)); }
        _advancedParameters.Children.Add(Text("以下为独立开发工具，不会同步本页模板或参数。"));
        _advancedParameters.Children.Add(developer);
    }
    private static void RestoreFocus(Control control) => Dispatcher.UIThread.Post(() => control.Focus(), DispatcherPriority.Loaded);
    private void BuildRows()
    {
        if (Skin.SelectedIndex < 0) { return; }
        var defaults = MonitorDisplayConfiguration.Default((MonitorSkin)Skin.SelectedIndex);
        Slots.Clear(); _slotRows.Children.Clear();
        for (int i = 0; i < defaults.Slots.Count; i++)
        {
            var slot = defaults.Slots[i];
            var channel = new ComboBox { ItemsSource = LiveMonitorTrace.Names.Select((name, index) => name + " / " + LiveMonitorTrace.Units[index]).ToArray(), SelectedIndex = slot.Channel, MinWidth = 155 };
            var automatic = new CheckBox { Content = "自动", IsChecked = true };
            var minimum = new TextBox { Text = slot.Range.Minimum.ToString(CultureInfo.InvariantCulture), MinWidth = 85, IsEnabled = false };
            var maximum = new TextBox { Text = slot.Range.Maximum.ToString(CultureInfo.InvariantCulture), MinWidth = 85, IsEnabled = false };
            automatic.IsCheckedChanged += (_, _) => minimum.IsEnabled = maximum.IsEnabled = automatic.IsChecked != true;
            channel.SelectionChanged += (_, _) =>
            {
                if (channel.SelectedIndex < 0) { return; }
                var range = MonitorDisplayConfiguration.ReferenceRange(channel.SelectedIndex);
                minimum.Text = range.Minimum.ToString(CultureInfo.InvariantCulture); maximum.Text = range.Maximum.ToString(CultureInfo.InvariantCulture);
            };
            Avalonia.Automation.AutomationProperties.SetName(channel, $"第{i + 1}行通道");
            Avalonia.Automation.AutomationProperties.SetName(minimum, $"第{i + 1}行下限");
            Avalonia.Automation.AutomationProperties.SetName(maximum, $"第{i + 1}行上限");
            var row = new Grid { ColumnDefinitions = new("30,160,70,*,*,115") };
            var speed = new ComboBox { ItemsSource = new[] { "12.5", "25", "50" }, SelectedIndex = 1 };
            AutomationProperties.SetName(automatic, $"第{i + 1}行自动量程");
            AutomationProperties.SetName(speed, $"第{i + 1}行扫描速度，相对毫米每秒");
            Control[] children = [Text((i + 1).ToString(CultureInfo.InvariantCulture)), channel, automatic, minimum, maximum, speed];
            for (int column = 0; column < children.Length; column++) { children[column].Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(children[column], column); row.Children.Add(children[column]); }
            _slotRows.Children.Add(row); Slots.Add(new(channel, automatic, minimum, maximum, speed));
        }
    }
    internal MonitorDisplayConfiguration ReadDisplay() => new((MonitorSkin)Skin.SelectedIndex, Slots.Select(slot =>
    {
        if (!double.TryParse(slot.Minimum.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double minimum) ||
            !double.TryParse(slot.Maximum.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double maximum))
        { throw new ArgumentException("量程上下限须为有效数值。"); }
        return new MonitorDisplaySlot(slot.Channel.SelectedIndex, slot.Auto.IsChecked == true, new(minimum, maximum), slot.Speed.SelectedIndex switch { 0 => 125, 1 => 250, 2 => 500, _ => 0 });
    }).ToArray());
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
            if (channel == 1) { context.DrawText(new FormattedText("41.25 s · 完整呼吸分组", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(DesignPreviewWindow.PreviewFont), 10, Brushes.White), new Point(0, 64)); }
            if (_geometry is not null) { context.DrawGeometry(null, new Pen(Brush.Parse(LiveMonitorTrace.Colors[channel]), 1.2), _geometry); }
        }
    }
}
