// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class DesignPreviewSettings : UserControl
{
    private static readonly string[] EcgChoices = ["窦性参考", "窦性停搏（无逸搏）", "单形室早", "窦性心律不齐", "房性早搏", "交界性早搏"];
    private static readonly string[] RespirationChoices = ["规则呼吸", "潮式呼吸", "间断呼吸示意", "无呼吸分量"];
    private static readonly string[] EjectionChoices = ["随当前节律", "早搏弱射血（需室早）", "2:1漏搏（需窦性参考）", "无有效射血"];
    internal int EcgSelection { get; set; }
    internal int RespirationSelection { get; set; }
    internal int EjectionSelection { get; set; }
    internal Button Apply { get; } = new() { Content = "应用并从头开始", MinHeight = 38, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal Button Run { get; } = new() { Content = "暂停生成", MinHeight = 38, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    internal ComboBox Skin { get; } = new() { ItemsSource = new[] { "紧凑 · 固定 3 行", "标准 · 固定 5 行", "扩展 · 固定 7 行" }, SelectedIndex = 1, MinWidth = 220 };
    internal TabControl Tabs { get; } = new();
    internal TextBlock Status { get; } = Text("选择后应用；显示设置不改变患者原始波形。");
    internal sealed record SlotEditor(ComboBox Channel, CheckBox Auto, TextBox Minimum, TextBox Maximum, ComboBox Speed);
    internal List<SlotEditor> Slots { get; } = [];
    private readonly StackPanel _slotRows = new() { Spacing = 12 };
    private readonly ContentControl _generation = new();
    private readonly Dictionary<(int, int, int), LocalMonitorPreviewSession> _previews = [];
    private readonly Func<int, int, int, LocalMonitorPreviewSession> _preview;
    private readonly Func<int, int, int, Control> _paper;
    private Control? _home;
    private readonly List<StyleThumbnail> _homeThumbnails = [];
    internal int PreviewCacheCount => _previews.Count;
    internal DesignPreviewSettings(Func<int, int, int, LocalMonitorPreviewSession> preview, Func<int, int, int, Control> paper, Action apply, Action run, Action advanced)
    {
        _preview = preview; _paper = paper;
        Func<LocalMonitorPreviewSession> session = () => Preview(EcgSelection, RespirationSelection, EjectionSelection);
        var generation = new StackPanel { Spacing = 18, Margin = new Thickness(20) };
        generation.Children.Add(Text("点击黑底波形选择样式；修改保留为草稿，应用后重启监护并更新十二导联快照。"));
        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        cards.Children.Add(Card("心电图", 0, EcgChoices,
            () => EcgSelection, x => EcgSelection = x, session));
        cards.Children.Add(Card("呼吸", 1, RespirationChoices,
            () => RespirationSelection, x => RespirationSelection = x, session));
        cards.Children.Add(Card("射血", 3, EjectionChoices,
            () => EjectionSelection, x => EjectionSelection = x, session));
        generation.Children.Add(cards);
        var more = new Button { Content = "完整心电图参数（现有开发入口）", MinHeight = 36, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        more.Click += (_, _) => advanced(); generation.Children.Add(more);
        generation.Children.Add(Text("卡片与候选样式均为所选配置的缓存预览；应用前不会改变监护。现有开发窗口中的参数仍独立。"));
        _home = generation; _generation.Content = generation;
        var display = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        display.Children.Add(Text("监护皮肤决定固定槽位数；调整窗口不增减行数。每行可独立选通道和量程。"));
        display.Children.Add(Skin);
        display.Children.Add(Text("自动：每行自己的扫屏起点，以前一轮样本更新量程，目标占用85%（ECG含1mV标定）。旧轨迹保留旧比例。固定：输入上下限。超界压平到边界，各行用实线隔开。"));
        var headings = new Grid { ColumnDefinitions = new("30,160,70,*,*,115") };
        string[] labels = ["行", "通道 / 单位", "量程", "下限", "上限", "扫速 mm/s* "];
        for (int column = 0; column < labels.Length; column++)
        { var label = Text(labels[column]); Grid.SetColumn(label, column); headings.Children.Add(label); }
        display.Children.Add(headings);
        display.Children.Add(_slotRows);
        display.Children.Add(Text("* 相对纸面速度：12.5 / 25 / 50 对应20 / 10 / 5秒时间窗，整区仍随窗口适配，不校准真实毫米。"));
        display.Children.Add(Text("十二导联：整张纸按显示区等比适配，保持纸格/波形/标定的相对比例；不校准屏幕毫米，允许高幅波形跨导联区域。"));
        Skin.SelectionChanged += (_, _) => BuildRows(); BuildRows();
        Tabs.ItemsSource = new[]
        {
            new TabItem { Header = "波形生成", Content = Scroll(_generation) },
            new TabItem { Header = "显示", Content = Scroll(display) },
            new TabItem { Header = "声音", Content = Scroll(Note("声音尚未启用", "心搏提示音来源、音量等设置将在此接入。当前不会发声。")) },
            new TabItem { Header = "报警", Content = Scroll(Note("报警尚未启用", "报警阈值、确认与限时声音暂停将在此接入。未启用不表示不存在报警条件。")) },
        };
        var controls = new WrapPanel { Margin = new Thickness(20, 12), Orientation = Orientation.Horizontal };
        Apply.Margin = new Thickness(0, 0, 12, 0); controls.Children.Add(Apply); controls.Children.Add(Run);
        Apply.Click += (_, _) => apply(); Run.Click += (_, _) => run();
        var root = new Grid { RowDefinitions = new("*,Auto,Auto") };
        root.Children.Add(Tabs); Grid.SetRow(controls, 1); root.Children.Add(controls);
        Status.Margin = new Thickness(20, 0, 20, 16); Grid.SetRow(Status, 2); root.Children.Add(Status); Content = root;
    }
    private LocalMonitorPreviewSession Preview(int ecg, int resp, int ejection)
    {
        var key = (ecg, resp, ejection);
        if (_previews.TryGetValue(key, out var cached)) { return cached; }
        var result = _preview(ecg, resp, ejection);
        if (_previews.Count >= 24) { _previews.Clear(); }
        _previews.Add(key, result); return result;
    }
    private Button Card(string title, int channel, string[] choices, Func<int> read, Action<int> write, Func<LocalMonitorPreviewSession> session)
    {
        var caption = new TextBlock { Text = title + " · " + choices[read()], Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 8 };
        var thumbnail = new StyleThumbnail(session, channel); _homeThumbnails.Add(thumbnail);
        content.Children.Add(caption); content.Children.Add(thumbnail);
        var card = new Button
        {
            Content = content,
            Background = Brushes.Black,
            Padding = new Thickness(12),
            Width = 238,
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(8)
        };
        card.Click += (_, _) => OpenChooser(title, channel, choices, read, selected =>
        { write(selected); caption.Text = title + " · " + choices[selected]; foreach (var item in _homeThumbnails) { item.InvalidateVisual(); } }, session);
        return card;
    }
    internal void OpenEcgChooser() => OpenChooser("心电图", 0, EcgChoices, () => EcgSelection, x => EcgSelection = x,
        () => Preview(EcgSelection, RespirationSelection, EjectionSelection));
    private void OpenChooser(string title, int channel, string[] choices, Func<int> read, Action<int> write, Func<LocalMonitorPreviewSession> session, string? activeGroup = null)
    {
        string Group(int i) => channel == 0 ? i switch { 0 or 1 or 3 => "窦性心律", 4 => "房性心律", 5 => "交界性心律", _ => "室性心律" }
            : channel == 1 ? i == 0 ? "规则呼吸" : "异常呼吸示意" : i == 0 ? "节律相关" : "异常射血示意";
        activeGroup ??= Group(read());
        var layout = new Grid { ColumnDefinitions = new("250,150,*"), Margin = new Thickness(16) };
        var selected = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 12, 0) };
        var back = new Button { Content = "返回波形设置" };
        back.Click += (_, _) => { _generation.Content = _home; _home?.InvalidateVisual(); };
        selected.Children.Add(back); selected.Children.Add(Text("已选 · " + choices[read()]));
        selected.Children.Add(new Border { Background = Brushes.Black, Padding = new Thickness(10), Child = new StyleThumbnail(session, channel) });
        if (channel == 0)
        {
            var expanded = new Expander { Header = "展开十二导联" };
            expanded.Expanding += (_, _) => expanded.Content ??= _paper(EcgSelection, RespirationSelection, EjectionSelection);
            selected.Children.Add(expanded);
        }
        layout.Children.Add(selected);
        var groups = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 12, 0) };
        foreach (string group in Enumerable.Range(0, choices.Length).Select(Group).Distinct())
        {
            var button = new Button
            {
                Content = group,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brush.Parse(group == activeGroup ? "#D3E3FA" : "#EBEEF2")
            };
            button.Click += (_, _) => OpenChooser(title, channel, choices, read, write, session, group); groups.Children.Add(button);
        }
        Grid.SetColumn(groups, 1); layout.Children.Add(groups);
        var candidates = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < choices.Length; i++)
        {
            if (Group(i) != activeGroup) { continue; }
            int value = i; var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = choices[i] + (read() == i ? " ✓" : ""), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            var candidate = new Button { Content = panel, Width = 238, Padding = new Thickness(10), Margin = new Thickness(0, 0, 10, 10), Background = Brushes.Black };
            try
            {
                var source = Preview(channel == 0 ? i : EcgSelection, channel == 1 ? i : RespirationSelection, channel == 3 ? i : EjectionSelection);
                panel.Children.Add(new StyleThumbnail(() => source, channel));
                candidate.Click += (_, _) =>
                {
                    write(value); Status.Text = "已选择 " + choices[value] + "；预览已更新，运行数据须应用后改变。";
                    OpenChooser(title, channel, choices, read, write, session, activeGroup);
                };
            }
            catch (ArgumentException)
            { candidate.IsEnabled = false; panel.Children.Add(Text("与当前其他参数不兼容")); }
            candidates.Children.Add(candidate);
        }
        Grid.SetColumn(candidates, 2); layout.Children.Add(candidates); _generation.Content = layout;
    }
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
    private sealed class StyleThumbnail(Func<LocalMonitorPreviewSession> session, int channel) : Control
    {
        private LocalMonitorPreviewSession? _cachedSource;
        private StreamGeometry? _geometry;
        protected override Size MeasureOverride(Size availableSize) => new(210, 74);
        public override void Render(DrawingContext context)
        {
            var source = session();
            if (!ReferenceEquals(source, _cachedSource))
            {
                _cachedSource = source;
                var samples = source.Samples(channel, Math.Max(0, source.FrontierNs - 3_000_000_000), source.FrontierNs).ToArray();
                _geometry = new StreamGeometry();
                using var path = _geometry.Open();
                if (samples.Length > 1)
                {
                    double minimum = samples.Min(s => s.Value), span = Math.Max(1, samples.Max(s => s.Value) - minimum);
                    for (int i = 0; i < samples.Length; i++)
                    {
                        var sample = samples[i];
                        Point point = new((sample.TimeNs - samples[0].TimeNs) / 3e9 * 210, 8 + (1 - (sample.Value - minimum) / span) * 55);
                        if (i == 0) { path.BeginFigure(point, false); } else { path.LineTo(point); }
                    }
                    path.EndFigure(false);
                }
            }
            if (_geometry is not null) { context.DrawGeometry(null, new Pen(Brush.Parse(LiveMonitorTrace.Colors[channel]), 1.2), _geometry); }
        }
    }
}
