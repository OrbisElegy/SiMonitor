// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Monitor.Application.Presentation;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed class EcgAdvancedParameterEditor : TabControl
{
    protected override Type StyleKeyOverride => typeof(TabControl);
    internal Dictionary<string, NumericUpDown> Numbers { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, CheckBox> Flags { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, ComboBox> Choices { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, TabItem> Groups { get; } = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StackPanel> _fieldPages = new(StringComparer.Ordinal);
    private EcgAdvancedPreferences? _defaults;
    private readonly DesktopLocalization _localization;
    private ProjectedEcgDemoConfiguration _preset = ProjectedEcgDemoConfiguration.Default;
    private bool _pulseDeficit;
    private bool _resetting;
    internal EcgTemplateCapabilities Capabilities { get; private set; } = EcgTemplateCapabilities.For(ProjectedEcgDemoConfiguration.Default);
    internal bool HasWaveformParameters => Groups.Values.Any(group => group.IsVisible);
    internal event Action? Changed;

    internal EcgAdvancedParameterEditor(DesktopLocalization localization)
    {
        _localization = localization;
        Padding = new Thickness(0);
        ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel());
        _localization.Bind(this, AutomationProperties.NameProperty, "ecgAdvanced.groupsName");
        var timing = Group("timing");
        Flag(timing, "customTiming");
        Choice(timing, "qtMethod", ["ecgAdvanced.referenceTiming", "Bazett", "Fridericia"]);
        Number(timing, "pDuration", 1, 1000, 100);
        Number(timing, "prInterval", 1, 1000, 160);
        Number(timing, "qrsDuration", 1, 1000, 80);
        Number(timing, "tDuration", 1, 1000, 180);
        Number(timing, "qtc", 1, 1000, 400);
        var pWave = Group("pWave");
        Flag(pWave, "customP");
        Number(pWave, "pEarly", -1000, 1000, 100, 10);
        Number(pWave, "pLate", -1000, 1000, -100, 10);
        var stT = Group("stT");
        Number(stT, "jPoint", -1000, 1000, 0, 10);
        Number(stT, "stEnd", -1000, 1000, 0, 10);
        Number(stT, "stArch", -1000, 1000, 0, 10);
        for (int i = 1; i <= 6; i++) { Number(stT, "tScale" + i, -4, 4, 1, .1m, "ecgAdvanced.tScale", $"V{i}"); }
        Flag(stT, "customTPeak");
        Number(stT, "tPeakPosition", 10, 90, 50, .1m);
        var fusion = Group("fusion");
        Flag(fusion, "fusionEnabled");
        for (int i = 1; i <= 6; i++) { Flag(fusion, "fusionLead" + i, "ecgAdvanced.fusionLead", $"V{i}"); }
        Number(fusion, "fusionJ", -1000, 1000, 200, 10);
        Number(fusion, "fusionPeak", -2000, 2000, 500, 10);
        Number(fusion, "fusionPosition", .1m, 99.9m, 50, .1m);
        var uWave = Group("uWave");
        Flag(uWave, "uEnabled");
        Number(uWave, "uDelay", 0, 1000, 30, 10);
        Number(uWave, "uDuration", 1, 1000, 120, 10);
        for (int i = 1; i <= 6; i++) { Number(uWave, "uAmplitude" + i, -1000, 1000, 0, 10, "ecgAdvanced.uAmplitude", $"V{i}"); }
        var escape = Group("escape");
        Number(escape, "escapePeriod", 800, 3200, 1200, 10);
        Number(escape, "escapeOffset", 0, 3199, 400, 10);
        var fibrillation = Group("fibrillation");
        Flag(fibrillation, "afAberrancy");
        Flag(fibrillation, "afPulseDeficit");
        var placement = Group("placement");
        Choice(placement, "limbPlacement", ["ecgAdvanced.standardPlacement", "RA ↔ LA", "RA ↔ LL", "LA ↔ LL"]);
        foreach (var (key, group) in Groups)
        {
            var reset = new Button { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
            _localization.Bind(reset, ContentControl.ContentProperty, "advanced.resetPage");
            reset.Click += (_, _) => ResetPage(key);
            ((StackPanel)group.Content!).Children.Add(reset);
        }
        Reset(_preset, false);
    }

    internal void Reset(ProjectedEcgDemoConfiguration preset, bool pulseDeficit)
    {
        _resetting = true;
        try
        {
            _preset = preset;
            _pulseDeficit = pulseDeficit;
            Capabilities = EcgTemplateCapabilities.For(preset);
            foreach (var flag in Flags.Values) { flag.IsChecked = false; }
            Choices["qtMethod"].SelectedIndex = 0;
            Choices["limbPlacement"].SelectedIndex = (int)preset.Placement;
            Numbers["pDuration"].Value = preset.PDurationMilliseconds;
            Numbers["pDuration"].Maximum = Capabilities.Ventricular ? 1000 : preset.PrIntervalMilliseconds;
            Numbers["prInterval"].Value = preset.PrIntervalMilliseconds;
            Numbers["qrsDuration"].Value = preset.QrsDurationMilliseconds;
            Numbers["tDuration"].Value = preset.TDurationMilliseconds;
            Numbers["qtc"].Value = preset.QtcMilliseconds;
            Numbers["pEarly"].Value = 100;
            Numbers["pLate"].Value = -100;
            Numbers["jPoint"].Value = Numbers["stEnd"].Value = Numbers["stArch"].Value = 0;
            Numbers["tPeakPosition"].Value = 50;
            Numbers["fusionJ"].Value = 200;
            Numbers["fusionPeak"].Value = 500;
            Numbers["fusionPosition"].Value = 50;
            Numbers["uDelay"].Value = 30;
            Numbers["uDuration"].Value = 120;
            for (int i = 1; i <= 6; i++)
            {
                Numbers["tScale" + i].Value = 1;
                Numbers["uAmplitude" + i].Value = 0;
            }
            bool ventricularEscape = preset.ConductionPattern == AvConductionPattern.CompleteAvBlockVentricularIllustration;
            Numbers["escapePeriod"].Minimum = ventricularEscape ? 1500 : 1000;
            Numbers["escapePeriod"].Maximum = ventricularEscape ? 3000 : 1500;
            Numbers["escapePeriod"].Value = preset.IndependentVentricularPeriodMilliseconds ?? 1200;
            Numbers["escapeOffset"].Value = preset.IndependentVentricularOffsetMilliseconds ?? 400;
            Flags["afAberrancy"].IsChecked = preset.IllustrateAfAberrancy;
            Flags["afPulseDeficit"].IsChecked = pulseDeficit;
            _defaults = Capture();
        }
        finally { _resetting = false; }
        Refresh();
    }

    private void ResetPage(string key)
    {
        if (_defaults is null) { return; }
        object? page = Groups[key].Content;
        _resetting = true;
        try
        {
            foreach (var (field, control) in Numbers)
            {
                if (ReferenceEquals(_fieldPages[field], page)) { control.Value = _defaults.Numbers[field]; }
            }
            foreach (var (field, control) in Flags)
            {
                if (ReferenceEquals(_fieldPages[field], page)) { control.IsChecked = _defaults.Flags[field]; }
            }
            foreach (var (field, control) in Choices)
            {
                if (ReferenceEquals(_fieldPages[field], page)) { control.SelectedIndex = _defaults.Choices[field]; }
            }
        }
        finally { _resetting = false; }
        Refresh();
    }

    internal TabItem AddPage(string title, Control content)
    {
        var page = new TabItem { Content = content, Padding = new Thickness(0), Margin = new Thickness(0, 0, 20, 0), FontSize = 14, MinHeight = 44 };
        _localization.Bind(page, HeaderedContentControl.HeaderProperty, title);
        Items.Add(page);
        return page;
    }

    internal void RefreshPages()
    {
        foreach (var page in Items.Cast<TabItem>())
        {
            if (!Groups.ContainsValue(page)) { page.IsVisible = ((Control)page.Content!).IsVisible; }
            page.IsEnabled = page.IsVisible;
        }
        if (SelectedItem is not TabItem { IsVisible: true })
        { SelectedItem = Items.Cast<TabItem>().FirstOrDefault(page => page.IsVisible); }
        IsVisible = Items.Cast<TabItem>().Any(page => page.IsVisible);
    }

    private bool Enabled(string key) => Flags[key].IsChecked == true;

    internal ProjectedEcgDemoConfiguration Read(ProjectedEcgDemoConfiguration config)
    {
        var support = Capabilities;
        if (support.Placement)
        {
            int placement = Choices["limbPlacement"].SelectedIndex;
            if (placement is < 0 or > 3) { throw new ArgumentException("Preview.InvalidEcgPlacement"); }
            config = config with { Placement = (EcgLimbPlacement)placement };
        }
        if (support.Timing)
        {
            int method = support.Ventricular ? Choices["qtMethod"].SelectedIndex : Enabled("customTiming") ? 2 : 0;
            if (method is < 0 or > 2) { throw new ArgumentException("Preview.InvalidEcgTiming"); }
            if (method != 0)
            {
                config = config with
                {
                    MethodId = method == 1 ? EcgQtCorrection.Bazett : EcgQtCorrection.Fridericia,
                    PDurationMilliseconds = support.PDuration ? ReadNumber("pDuration") : config.PDurationMilliseconds,
                    PrIntervalMilliseconds = support.PrInterval ? ReadNumber("prInterval") : config.PrIntervalMilliseconds,
                    QrsDurationMilliseconds = support.QrsDuration ? ReadNumber("qrsDuration") : config.QrsDurationMilliseconds,
                    TDurationMilliseconds = support.Ventricular ? ReadNumber("tDuration") : config.TDurationMilliseconds,
                    QtcMilliseconds = support.Ventricular ? ReadNumber("qtc") : config.QtcMilliseconds
                };
            }
        }
        if (support.PWave && Enabled("customP"))
        { config = config with { ChestP = new(ReadNumber("pEarly"), ReadNumber("pLate")) }; }
        if (support.StT)
        {
            int mask = Enabled("fusionEnabled") ? Enumerable.Range(1, 6).Where(i => Enabled("fusionLead" + i)).Sum(i => 1 << (i - 1)) : 0;
            if (Enabled("fusionEnabled") && mask == 0) { throw new ArgumentException("Preview.EcgFusionLeadRequired"); }
            // A fused lead replaces its T contour. Do not read dormant inputs.
            int[] scales = Enumerable.Range(1, 6).Select(i => (mask & (1 << (i - 1))) != 0 ? 1000 : ReadNumber("tScale" + i, 1000)).ToArray();
            var tWave = new ProjectedEcgTConfiguration(scales[0], scales[1], scales[2], scales[3], scales[4], scales[5],
                Enabled("customTPeak") ? ReadNumber("tPeakPosition", 10) : null);
            config = config with
            {
                ChestJMicrovolts = mask == 63 ? 0 : ReadNumber("jPoint"),
                ChestStEndMicrovolts = mask == 63 ? 0 : ReadNumber("stEnd"),
                ChestStArchMicrovolts = mask == 63 ? 0 : ReadNumber("stArch"),
                TWave = tWave == ProjectedEcgTConfiguration.Default ? null : tWave,
                Fusion = mask == 0 ? null : new(mask, ReadNumber("fusionJ"), ReadNumber("fusionPeak"), ReadNumber("fusionPosition", 10))
            };
        }
        if (support.UWave && Enabled("uEnabled"))
        {
            int[] amplitudes = Enumerable.Range(1, 6).Select(i => ReadNumber("uAmplitude" + i)).ToArray();
            config = config with
            {
                UWave = new(ReadNumber("uDelay"), ReadNumber("uDuration"),
                amplitudes[0], amplitudes[1], amplitudes[2], amplitudes[3], amplitudes[4], amplitudes[5])
            };
        }
        if (support.Escape)
        {
            config = config with
            {
                IndependentVentricularPeriodMilliseconds = ReadNumber("escapePeriod"),
                IndependentVentricularOffsetMilliseconds = ReadNumber("escapeOffset")
            };
        }
        if (support.Fibrillation) { config = config with { IllustrateAfAberrancy = Enabled("afAberrancy") }; }
        return config;
    }

    internal PhysiologyDemoConfiguration ReadPhysiology(PhysiologyDemoConfiguration config, ProjectedEcgDemoConfiguration ecg) => config with
    {
        IndependentVentricularPeriodMilliseconds = Capabilities.Escape ? ecg.IndependentVentricularPeriodMilliseconds : config.IndependentVentricularPeriodMilliseconds,
        IndependentVentricularOffsetMilliseconds = Capabilities.Escape ? ecg.IndependentVentricularOffsetMilliseconds : config.IndependentVentricularOffsetMilliseconds,
        IllustrateAfAberrancy = ecg.IllustrateAfAberrancy,
        IllustrateAfSystemicPulseDeficit = Capabilities.Fibrillation ? Enabled("afPulseDeficit") : config.IllustrateAfSystemicPulseDeficit
    };

    internal EcgAdvancedPreferences Capture() => new(
        Numbers.ToDictionary(pair => pair.Key, pair => pair.Value.Value),
        Flags.ToDictionary(pair => pair.Key, pair => pair.Value.IsChecked == true),
        Choices.ToDictionary(pair => pair.Key, pair => pair.Value.SelectedIndex));

    internal void Restore(EcgAdvancedPreferences? preferences)
    {
        Reset(_preset, _pulseDeficit);
        if (preferences is null) { return; }
        preferences.Validate();
        if (!Numbers.Keys.ToHashSet().SetEquals(preferences.Numbers.Keys) || !Flags.Keys.ToHashSet().SetEquals(preferences.Flags.Keys) ||
            !Choices.Keys.ToHashSet().SetEquals(preferences.Choices.Keys))
        { throw new ArgumentException("EcgPreferences.FieldsMismatch"); }
        foreach (var (key, value) in preferences.Numbers)
        {
            if (value is { } number && (number < Numbers[key].Minimum || number > Numbers[key].Maximum))
            { throw new ArgumentException("EcgPreferences.Range"); }
        }
        foreach (var (key, value) in preferences.Choices)
        {
            if (value < 0 || value >= Choices[key].Items.Count) { throw new ArgumentException("EcgPreferences.Choice"); }
        }
        _resetting = true;
        try
        {
            foreach (var (key, value) in preferences.Numbers) { Numbers[key].Value = value; }
            foreach (var (key, value) in preferences.Flags) { Flags[key].IsChecked = value; }
            foreach (var (key, value) in preferences.Choices) { Choices[key].SelectedIndex = value; }
        }
        finally { _resetting = false; }
        Refresh();
    }

    private void Refresh()
    {
        if (_resetting) { return; }
        var support = Capabilities;
        Groups["timing"].IsVisible = support.Timing;
        Groups["pWave"].IsVisible = support.PWave;
        Groups["stT"].IsVisible = Groups["fusion"].IsVisible = support.StT;
        Groups["uWave"].IsVisible = support.UWave;
        Groups["escape"].IsVisible = support.Escape;
        Groups["fibrillation"].IsVisible = support.Fibrillation;
        Groups["placement"].IsVisible = support.Placement;
        RefreshPages();
        Choices["qtMethod"].IsVisible = support.Ventricular;
        _rows["customTiming"].IsVisible = !support.Ventricular;
        bool timing = support.Ventricular ? Choices["qtMethod"].SelectedIndex != 0 : Enabled("customTiming");
        _rows["pDuration"].IsVisible = timing && support.PDuration;
        _rows["prInterval"].IsVisible = timing && support.PrInterval;
        _rows["qrsDuration"].IsVisible = timing && support.QrsDuration;
        _rows["tDuration"].IsVisible = _rows["qtc"].IsVisible = timing && support.Ventricular;
        _rows["pEarly"].IsVisible = _rows["pLate"].IsVisible = Enabled("customP");
        bool fusion = Enabled("fusionEnabled");
        bool allFused = fusion && Enumerable.Range(1, 6).All(i => Enabled("fusionLead" + i));
        foreach (string key in new[] { "jPoint", "stEnd", "stArch" }) { _rows[key].IsVisible = !allFused; }
        _rows["tPeakPosition"].IsVisible = Enabled("customTPeak");
        foreach (string key in new[] { "fusionJ", "fusionPeak", "fusionPosition" }) { _rows[key].IsVisible = fusion; }
        foreach (string key in new[] { "uDelay", "uDuration" }) { _rows[key].IsVisible = Enabled("uEnabled"); }
        for (int i = 1; i <= 6; i++)
        {
            _rows["tScale" + i].IsVisible = !fusion || !Enabled("fusionLead" + i);
            _rows["fusionLead" + i].IsVisible = fusion;
            _rows["uAmplitude" + i].IsVisible = Enabled("uEnabled");
        }
        Changed?.Invoke();
    }

    private int ReadNumber(string key, int scale = 1)
    {
        var field = Numbers[key];
        return DesignPreviewSettings.ReadVitalValue(field, scale, AutomationProperties.GetName(field) ?? key);
    }

    private StackPanel Group(string key)
    {
        var panel = new StackPanel { Spacing = 8 };
        var group = AddPage("ecgAdvanced." + key, panel);
        Groups.Add(key, group);
        return panel;
    }

    private void Number(StackPanel panel, string key, decimal min, decimal max, decimal value, decimal increment = 1, string? label = null, params object[] args)
    {
        var field = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Increment = increment,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Numbers.Add(key, field);
        _fieldPages.Add(key, panel);
        var row = new StackPanel { Spacing = 4, Width = 220, Margin = new Thickness(0, 0, 16, 8) };
        var caption = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _localization.Bind(caption, TextBlock.TextProperty, label ?? "ecgAdvanced." + key, args);
        _localization.Bind(field, AutomationProperties.NameProperty, label ?? "ecgAdvanced." + key, args);
        row.Children.Add(caption);
        row.Children.Add(field);
        _rows.Add(key, row);
        if (panel.Children.LastOrDefault() is not WrapPanel fields)
        {
            fields = new WrapPanel();
            panel.Children.Add(fields);
        }
        fields.Children.Add(row);
        field.ValueChanged += (_, _) => { if (!_resetting) { Changed?.Invoke(); } };
    }

    private void Flag(StackPanel panel, string key, string? label = null, params object[] args)
    {
        var field = new CheckBox();
        _localization.Bind(field, ContentControl.ContentProperty, label ?? "ecgAdvanced." + key, args);
        Flags.Add(key, field);
        _fieldPages.Add(key, panel);
        _rows.Add(key, field);
        panel.Children.Add(field);
        field.IsCheckedChanged += (_, _) => Refresh();
    }

    private void Choice(StackPanel panel, string key, string[] labels)
    {
        var field = new ComboBox { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
        _localization.SetChoices(field, labels.Select(label => (Func<Monitor.Application.Localization.ITextLocalizer, string>)
            (text => DesktopLocalization.Label(text, label))));
        _localization.Bind(field, AutomationProperties.NameProperty, "ecgAdvanced." + key);
        field.SelectedIndex = 0;
        Choices.Add(key, field);
        _fieldPages.Add(key, panel);
        panel.Children.Add(field);
        field.SelectionChanged += (_, _) => Refresh();
    }
}
