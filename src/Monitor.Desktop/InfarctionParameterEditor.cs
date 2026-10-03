// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Regional authoring controls reuse the existing fixed infarction snapshots.
internal sealed class InfarctionParameterEditor : StackPanel
{
    internal CheckBox[] ChestLeads { get; } = Enumerable.Range(1, 6).Select(i => new CheckBox { Content = $"V{i}" }).ToArray();
    internal NumericUpDown Delay { get; } = new() { Minimum = 0, Maximum = 500, Increment = 10, Value = 0, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal CheckBox ComponentsEnabled { get; } = new() { Content = "独立编辑 QRS／ST／T（替代阶段模板形态）" };
    internal ComboBox Necrosis { get; } = new() { ItemsSource = new[] { "参考 QRS", "异常 Q 波伴 R 波降低", "QS 波" }, SelectedIndex = 0, MinWidth = 220 };
    internal NumericUpDown QrsWeight { get; } = Field(0, 100, 100, 1);
    internal CheckBox ReferenceT { get; } = new() { Content = "沿用参考 T 波", IsChecked = true };
    internal NumericUpDown TPeak { get; } = Field(-4000, 4000, 300, 10);
    internal NumericUpDown JPoint { get; } = Field(-4000, 4000, 0, 10);
    internal NumericUpDown StEnd { get; } = Field(-4000, 4000, 0, 10);
    internal NumericUpDown StArch { get; } = Field(-4000, 4000, 0, 10);
    internal CheckBox SeparateRegions { get; } = new() { Content = "缺血／损伤／坏死分别选区" };
    internal ComboBox IschemiaRegion { get; } = Region();
    internal ComboBox InjuryRegion { get; } = Region();
    internal ComboBox NecrosisRegion { get; } = Region();
    private readonly StackPanel _zones = new() { Spacing = 8, IsVisible = false };
    internal TabControl Groups { get; } = new() { Padding = new Avalonia.Thickness(0) };
    private readonly StackPanel _tOverride = new() { Spacing = 8 };
    private readonly TextBlock _componentNote = new() { Text = "本模式不保留阶段模板的 ST–T 融合；QRS 混合比例仅为形态参数。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _chest = new() { Spacing = 8 };
    private readonly TextBlock _region = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal event Action? Changed;
    private EcgChestInfarctionPlan? _preset;
    internal InfarctionParameterEditor()
    {
        Spacing = 8;
        Delay.ValueChanged += (_, _) => Changed?.Invoke();

        var regionPage = new StackPanel { Spacing = 8 };
        var repolarizationPage = new StackPanel { Spacing = 8 };
        var injuryPage = new StackPanel { Spacing = 8 };
        var necrosisPage = new StackPanel { Spacing = 8 };
        regionPage.Children.Add(ComponentsEnabled);
        regionPage.Children.Add(_componentNote);
        regionPage.Children.Add(_region);
        _chest.Children.Add(new TextBlock { Text = "胸导联区域（至少选择一个）" });
        var row = new WrapPanel();
        foreach (var lead in ChestLeads)
        {
            lead.Margin = new Avalonia.Thickness(0, 0, 16, 0);
            AutomationProperties.SetName(lead, $"梗死快照目标胸导联 {lead.Content}");
            lead.IsCheckedChanged += (_, _) => Changed?.Invoke();
            row.Children.Add(lead);
        }
        _chest.Children.Add(row); regionPage.Children.Add(_chest);
        regionPage.Children.Add(SeparateRegions);
        foreach (var (zoneLabel, selector) in new[] { ("缺血（T 波及局部复极延长）", IschemiaRegion), ("损伤（J／ST）", InjuryRegion), ("坏死（QRS）", NecrosisRegion) })
        {
            Add(_zones, zoneLabel, selector);
            selector.SelectionChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        }
        regionPage.Children.Add(_zones);
        Add(repolarizationPage, "局部复极延长（ms；T 时限与 QT 同步增加）", Delay);
        _tOverride.Children.Add(ReferenceT); Add(_tOverride, "T 波峰幅（μV；负值倒置，0 为低平）", TPeak);
        repolarizationPage.Children.Add(_tOverride);
        Add(injuryPage, "J 点偏移（μV）", JPoint); Add(injuryPage, "ST 末端偏移（μV）", StEnd); Add(injuryPage, "ST 弓形幅度（μV）", StArch);
        Add(necrosisPage, "QRS 形态", Necrosis); Add(necrosisPage, "异常 QRS 模板混合比例（%）", QrsWeight);
        Groups.ItemsSource = new[]
        {
            Page("区域", regionPage), Page("复极／T", repolarizationPage),
            Page("损伤／ST", injuryPage), Page("坏死／QRS", necrosisPage)
        };
        Groups.SelectedIndex = 0;
        AutomationProperties.SetName(Groups, "梗死高级参数分组");
        Children.Add(Groups);
        SeparateRegions.IsCheckedChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        ComponentsEnabled.IsCheckedChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        ReferenceT.IsCheckedChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        Necrosis.SelectionChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        foreach (var field in new[] { QrsWeight, TPeak, JPoint, StEnd, StArch }) { field.ValueChanged += (_, _) => Changed?.Invoke(); }
        RefreshComponents();
        static TabItem Page(string title, Control content) => new()
        {
            Header = title,
            Content = content,
            Padding = new Avalonia.Thickness(0),
            Margin = new Avalonia.Thickness(0, 0, 20, 0),
            FontSize = 14,
            MinHeight = 44
        };
        static void Add(StackPanel host, string label, Control control)
        {
            host.Children.Add(new TextBlock { Text = label }); host.Children.Add(control);
            AutomationProperties.SetName(control, label);
        }
        var reset = new Button { Content = "恢复梗死模板参数", MinHeight = 44 };
        reset.Click += (_, _) => Reset(_preset); Children.Add(reset);
        Children.Add(new TextBlock { Text = "应用后同步十二导联与监护；卡片保留模板预览，快照不会随时间演变。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
    }
    internal void Reset(EcgChestInfarctionPlan? preset)
    {
        _preset = preset; IsVisible = preset is not null;
        Groups.SelectedIndex = 0;
        if (preset is null) { return; }
        _chest.IsVisible = preset.Territory is InfarctionTerritory.Anteroseptal or InfarctionTerritory.Anterior or InfarctionTerritory.ExtensiveAnterior;
        int mask = preset.Territory switch
        {
            InfarctionTerritory.Anteroseptal => 7,
            InfarctionTerritory.Anterior => 28,
            InfarctionTerritory.ExtensiveAnterior => 31,
            _ => 0
        };
        for (int i = 0; i < 6; i++) { ChestLeads[i].IsChecked = (mask & (1 << i)) != 0; }
        Delay.Value = preset.RepolarizationDelayNs / 1_000_000m;
        ComponentsEnabled.IsChecked = false; SeparateRegions.IsChecked = false;
        int region = preset.Territory switch { InfarctionTerritory.Inferior => 10, InfarctionTerritory.Lateral => 11, InfarctionTerritory.Anteroseptal => 7, InfarctionTerritory.Anterior => 8, _ => 9 };
        IschemiaRegion.SelectedIndex = InjuryRegion.SelectedIndex = NecrosisRegion.SelectedIndex = region;
        Necrosis.SelectedIndex = 0; QrsWeight.Value = 100;
        ReferenceT.IsChecked = true; TPeak.Value = 300; JPoint.Value = StEnd.Value = StArch.Value = 0;
        RefreshComponents();
        _region.Text = _chest.IsVisible ? "仅修改所选胸导联；其他胸导联及肢体导联保持原样。"
            : "保留模板区域及肢体导联投影关系，仅调整局部复极时限。";
        Changed?.Invoke();
    }
    internal EcgChestInfarctionPlan? Read(EcgChestInfarctionPlan? preset)
    {
        if (preset is null) { return null; }
        if (preset != _preset) { throw new ArgumentException("Preview.StaleInfarction"); }
        decimal delay = RegionActive(IschemiaRegion) ? Delay.Value ?? throw new ArgumentException("Preview.InvalidInfarctionDelay") : 0;
        if (delay is < 0 or > 500 || delay != decimal.Truncate(delay)) { throw new ArgumentException("Preview.InvalidInfarctionDelay"); }
        EcgInfarctionComponents? components = null;
        if (ComponentsEnabled.IsChecked == true)
        {
            int necrosis = RegionActive(NecrosisRegion) ? Necrosis.SelectedIndex : 0;
            if (necrosis is < 0 or > 2) { throw new ArgumentException("Preview.InvalidInfarctionComponents"); }
            bool injury = RegionActive(InjuryRegion);
            components = new((NecrosisIllustrationShape)necrosis,
                !RegionActive(IschemiaRegion) || ReferenceT.IsChecked == true ? null : Integer(TPeak),
                injury ? Integer(JPoint) : 0, injury ? Integer(StEnd) : 0, injury ? Integer(StArch) : 0,
                necrosis == 0 ? 1000 : Integer(QrsWeight) * 10);
        }
        var edited = preset with { RepolarizationDelayNs = (long)delay * 1_000_000, Components = components };
        if (!_chest.IsVisible) { return edited; }
        int mask = ChestLeads.Select((lead, i) => lead.IsChecked == true ? 1 << i : 0).Aggregate(0, (a, b) => a | b);
        if (mask == 0) { throw new ArgumentException("Preview.InfarctionChestRequired"); }
        int original = preset.Territory switch { InfarctionTerritory.Anteroseptal => 7, InfarctionTerritory.Anterior => 28, _ => 31 };
        return mask == original ? edited : edited with { Territory = InfarctionTerritory.CustomChest, ChestMask = mask };
    }
    internal EcgInfarctionZones? ReadZones(EcgChestInfarctionPlan? preset)
    {
        if (preset is null || ComponentsEnabled.IsChecked != true || SeparateRegions.IsChecked != true) { return null; }
        var edited = Read(preset)!;
        return new(InfarctionZoneSelection.Resolve(IschemiaRegion.SelectedIndex), InfarctionZoneSelection.Resolve(InjuryRegion.SelectedIndex),
            InfarctionZoneSelection.Resolve(NecrosisRegion.SelectedIndex), edited.Components!, edited.RepolarizationDelayNs);
    }
    private static ComboBox Region() => new() { ItemsSource = InfarctionZoneSelection.Names, SelectedIndex = 0, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private bool RegionActive(ComboBox region) =>
        ComponentsEnabled.IsChecked != true || SeparateRegions.IsChecked != true || region.SelectedIndex != 0;
    private void RefreshComponents()
    {
        bool components = ComponentsEnabled.IsChecked == true;
        _componentNote.IsVisible = _tOverride.IsVisible = SeparateRegions.IsVisible = components;
        _zones.IsVisible = components && SeparateRegions.IsChecked == true;
        var pages = Groups.Items.OfType<TabItem>().ToArray();
        if (pages.Length == 4)
        {
            pages[1].IsEnabled = RegionActive(IschemiaRegion);
            pages[2].IsEnabled = components && RegionActive(InjuryRegion);
            pages[3].IsEnabled = components && RegionActive(NecrosisRegion);
            if (Groups.SelectedIndex >= 0 && !pages[Groups.SelectedIndex].IsEnabled) { Groups.SelectedIndex = 0; }
        }
        _chest.IsVisible = !_zones.IsVisible && _preset?.Territory is InfarctionTerritory.Anteroseptal or InfarctionTerritory.Anterior or InfarctionTerritory.ExtensiveAnterior;
        _region.IsVisible = !_zones.IsVisible;
        TPeak.IsEnabled = ReferenceT.IsChecked != true;
        QrsWeight.IsEnabled = Necrosis.SelectedIndex > 0;
    }
    private static NumericUpDown Field(decimal minimum, decimal maximum, decimal value, decimal increment) =>
        new() { Minimum = minimum, Maximum = maximum, Value = value, Increment = increment, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private static int Integer(NumericUpDown field)
    {
        decimal value = field.Value ?? throw new ArgumentException("Preview.InvalidInfarctionComponents");
        if (value < field.Minimum || value > field.Maximum || value != decimal.Truncate(value))
        { throw new ArgumentException("Preview.InvalidInfarctionComponents"); }
        return checked((int)value);
    }

}
