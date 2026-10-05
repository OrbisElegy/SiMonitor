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
    internal CheckBox ComponentsEnabled { get; } = new();
    internal ComboBox Necrosis { get; } = new() { MinWidth = 220 };
    internal NumericUpDown QrsWeight { get; } = Field(0, 100, 100, 1);
    internal CheckBox ReferenceT { get; } = new() { IsChecked = true };
    internal NumericUpDown TPeak { get; } = Field(-4000, 4000, 300, 10);
    internal NumericUpDown JPoint { get; } = Field(-4000, 4000, 0, 10);
    internal NumericUpDown StEnd { get; } = Field(-4000, 4000, 0, 10);
    internal NumericUpDown StArch { get; } = Field(-4000, 4000, 0, 10);
    internal CheckBox SeparateRegions { get; } = new();
    internal ComboBox IschemiaRegion { get; } = Region();
    internal ComboBox InjuryRegion { get; } = Region();
    internal ComboBox NecrosisRegion { get; } = Region();
    private readonly DesktopLocalization _localization;
    private readonly StackPanel _zones = new() { Spacing = 8, IsVisible = false };
    internal TabControl Groups { get; } = new() { Padding = new Avalonia.Thickness(0) };
    private readonly StackPanel _tOverride = new() { Spacing = 8 };
    private readonly TextBlock _componentNote = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _chest = new() { Spacing = 8 };
    private readonly TextBlock _region = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal event Action? Changed;
    private EcgChestInfarctionPlan? _preset;
    internal InfarctionParameterEditor(DesktopLocalization? localization = null)
    {
        _localization = localization ?? new DesktopLocalization();
        Spacing = 8;
        _localization.Bind(ComponentsEnabled, ContentControl.ContentProperty, "infarction.componentsEnabled");
        _localization.Bind(ReferenceT, ContentControl.ContentProperty, "infarction.referenceT");
        _localization.Bind(SeparateRegions, ContentControl.ContentProperty, "infarction.separateRegions");
        _localization.Bind(_componentNote, TextBlock.TextProperty, "infarction.componentNote");
        _localization.SetChoices(Necrosis, "infarction.necrosisReference", "infarction.necrosisQ", "infarction.necrosisQs");
        Necrosis.SelectedIndex = 0;
        foreach (var selector in new[] { IschemiaRegion, InjuryRegion, NecrosisRegion })
        {
            _localization.SetChoices(selector, InfarctionZoneSelection.LocalizedNames);
            selector.SelectedIndex = 0;
        }
        Delay.ValueChanged += (_, _) => Changed?.Invoke();

        var regionPage = new StackPanel { Spacing = 8 };
        var repolarizationPage = new StackPanel { Spacing = 8 };
        var injuryPage = new StackPanel { Spacing = 8 };
        var necrosisPage = new StackPanel { Spacing = 8 };
        regionPage.Children.Add(ComponentsEnabled);
        regionPage.Children.Add(_componentNote);
        regionPage.Children.Add(_region);
        var chestLabel = new TextBlock();
        _localization.Bind(chestLabel, TextBlock.TextProperty, "infarction.chestRegion");
        _chest.Children.Add(chestLabel);
        var row = new WrapPanel();
        foreach (var lead in ChestLeads)
        {
            lead.Margin = new Avalonia.Thickness(0, 0, 16, 0);
            _localization.Bind(lead, AutomationProperties.NameProperty, "infarction.chestLeadName", lead.Content);
            lead.IsCheckedChanged += (_, _) => Changed?.Invoke();
            row.Children.Add(lead);
        }
        _chest.Children.Add(row); regionPage.Children.Add(_chest);
        regionPage.Children.Add(SeparateRegions);
        foreach (var (zoneLabel, selector) in new[] { ("infarction.zoneIschemia", IschemiaRegion), ("infarction.zoneInjury", InjuryRegion), ("infarction.zoneNecrosis", NecrosisRegion) })
        {
            Add(_zones, zoneLabel, selector);
            selector.SelectionChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        }
        regionPage.Children.Add(_zones);
        Add(repolarizationPage, "infarction.delay", Delay);
        _tOverride.Children.Add(ReferenceT);
        Add(_tOverride, "infarction.tPeak", TPeak);
        repolarizationPage.Children.Add(_tOverride);
        Add(injuryPage, "infarction.jPoint", JPoint);
        Add(injuryPage, "infarction.stEnd", StEnd);
        Add(injuryPage, "infarction.stArch", StArch);
        Add(necrosisPage, "infarction.qrsShape", Necrosis);
        Add(necrosisPage, "infarction.qrsWeight", QrsWeight);
        Groups.ItemsSource = new[]
        {
            Page("infarction.pageRegion", regionPage), Page("infarction.pageRepolarization", repolarizationPage),
            Page("infarction.pageInjury", injuryPage), Page("infarction.pageNecrosis", necrosisPage)
        };
        Groups.SelectedIndex = 0;
        _localization.Bind(Groups, AutomationProperties.NameProperty, "infarction.groupsName");
        Children.Add(Groups);
        SeparateRegions.IsCheckedChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        ComponentsEnabled.IsCheckedChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        ReferenceT.IsCheckedChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        Necrosis.SelectionChanged += (_, _) => { RefreshComponents(); Changed?.Invoke(); };
        foreach (var field in new[] { QrsWeight, TPeak, JPoint, StEnd, StArch }) { field.ValueChanged += (_, _) => Changed?.Invoke(); }
        RefreshComponents();
        TabItem Page(string key, Control content)
        {
            var page = new TabItem
            {
                Content = content,
                Padding = new Avalonia.Thickness(0),
                Margin = new Avalonia.Thickness(0, 0, 20, 0),
                FontSize = 14,
                MinHeight = 44
            };
            _localization.Bind(page, TabItem.HeaderProperty, key);
            return page;
        }
        void Add(StackPanel host, string key, Control control)
        {
            var label = new TextBlock();
            _localization.Bind(label, TextBlock.TextProperty, key);
            host.Children.Add(label);
            host.Children.Add(control);
            _localization.Bind(control, AutomationProperties.NameProperty, key);
        }
        var reset = new Button { MinHeight = 44 };
        _localization.Bind(reset, ContentControl.ContentProperty, "infarction.reset");
        reset.Click += (_, _) => Reset(_preset); Children.Add(reset);
        var note = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _localization.Bind(note, TextBlock.TextProperty, "infarction.applyNote");
        Children.Add(note);
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
        _localization.Bind(_region, TextBlock.TextProperty, _chest.IsVisible ? "infarction.chestNote" : "infarction.territoryNote");
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
    private static ComboBox Region() => new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
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
