// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Application.Localization;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Edits the existing contour plan; no new morphology or per-lead scaling.
internal sealed class TContourParameterEditor : StackPanel
{
    internal NumericUpDown Peak { get; } = Field(1, 4000, 10);
    internal NumericUpDown SecondPeak { get; } = Field(1, 4000, 10);
    internal NumericUpDown Crossing { get; } = Field(.1m, 99.9m, .1m);
    internal ComboBox Target { get; } = new()
    {
        MinWidth = 180,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    internal CheckBox[] ChestLeads { get; } = Enumerable.Range(1, 6).Select(i => new CheckBox { Content = $"V{i}" }).ToArray();
    private readonly WrapPanel _chest = new();
    private readonly TextBlock _targetNote = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal event Action? Changed;
    private EcgTContourPlan? _preset;
    private readonly StackPanel _biphasic = new() { Spacing = 8 };
    private readonly DesktopLocalization _localization;
    // Indexed by EcgTContourTarget; lead names stay verbatim.
    private static readonly string[] TargetChoices = ["tContour.chestLeads", "I", "II", "III", "aVR", "aVL", "aVF"];
    internal TContourParameterEditor(DesktopLocalization? localization = null)
    {
        _localization = localization ?? new DesktopLocalization();
        Spacing = 8;
        _localization.SetChoices(Target, TargetChoices.Select(choice => (Func<ITextLocalizer, string>)(text => DesktopLocalization.Label(text, choice))));
        Target.SelectedIndex = 2;
        Peak.ValueChanged += (_, _) => Changed?.Invoke();
        SecondPeak.ValueChanged += (_, _) => Changed?.Invoke();
        Crossing.ValueChanged += (_, _) => Changed?.Invoke();

        Add(this, "tContour.target", Target);
        foreach (var lead in ChestLeads)
        {
            lead.Margin = new Avalonia.Thickness(0, 0, 16, 0);
            _localization.Bind(lead, AutomationProperties.NameProperty, "tContour.chestLeadName", lead.Content);
            lead.IsCheckedChanged += (_, _) => Changed?.Invoke();
            _chest.Children.Add(lead);
        }
        Children.Add(_chest); Children.Add(_targetNote);
        Target.SelectionChanged += (_, _) => { RefreshTarget(); Changed?.Invoke(); };
        RefreshTarget();
        Add(this, "tContour.peak", Peak);
        Add(_biphasic, "tContour.secondPeak", SecondPeak);
        Add(_biphasic, "tContour.crossing", Crossing);
        Children.Add(_biphasic);
        var reset = new Button { MinHeight = 44 };
        _localization.Bind(reset, ContentControl.ContentProperty, "tContour.reset");
        reset.Click += (_, _) => Reset(_preset);
        Children.Add(reset);
        var note = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _localization.Bind(note, TextBlock.TextProperty, "tContour.applyNote");
        Children.Add(note);
    }
    internal void Reset(EcgTContourPlan? preset)
    {
        _preset = preset;
        IsVisible = preset is not null;
        if (preset is null) { return; }
        Target.SelectedIndex = (int)preset.Target;
        for (int i = 0; i < ChestLeads.Length; i++) { ChestLeads[i].IsChecked = (preset.ChestMask & (1 << i)) != 0; }
        RefreshTarget();
        Peak.Minimum = preset.Shape is EcgTContourShape.ReferenceUpright or EcgTContourShape.ReferenceInverted ? 0 : 1;
        Peak.Value = preset.PeakMicrovolts;
        SecondPeak.Value = preset.SecondPeakMicrovolts ?? preset.PeakMicrovolts;
        Crossing.Value = (preset.CrossingPositionPermille ?? 500) / 10m;
        _biphasic.IsVisible = preset.Shape is EcgTContourShape.PositiveNegative or EcgTContourShape.NegativePositive;
        Changed?.Invoke();
    }
    internal EcgTContourPlan? Read(EcgTContourPlan? preset)
    {
        if (preset is null) { return null; }
        if (preset != _preset) { throw new ArgumentException("Preview.StaleTContour"); }
        if (Target.SelectedIndex is < 0 or > 6) { throw new ArgumentException("Preview.InvalidTContourTarget"); }
        int mask = Target.SelectedIndex == 0
            ? ChestLeads.Select((lead, index) => lead.IsChecked == true ? 1 << index : 0).Aggregate(0, (a, b) => a | b)
            : preset.ChestMask;
        if (mask == 0) { throw new ArgumentException("Preview.TContourChestRequired"); }
        return preset with
        {
            Target = (EcgTContourTarget)Target.SelectedIndex,
            ChestMask = mask,
            PeakMicrovolts = ReadInteger(Peak, 1),
            SecondPeakMicrovolts = _biphasic.IsVisible ? ReadInteger(SecondPeak, 1) : null,
            CrossingPositionPermille = _biphasic.IsVisible ? ReadInteger(Crossing, 10) : null
        };
    }
    private void RefreshTarget()
    {
        _chest.IsVisible = Target.SelectedIndex == 0;
        _localization.Bind(_targetNote, TextBlock.TextProperty, _chest.IsVisible ? "tContour.chestNote" : "tContour.limbNote");
    }
    private static int ReadInteger(NumericUpDown field, int scale)
    {
        decimal value = field.Value ?? throw new ArgumentException("Preview.TContourValueRequired");
        if (value < field.Minimum || value > field.Maximum || value * scale != decimal.Truncate(value * scale))
        { throw new ArgumentException("Preview.InvalidTContourValue"); }
        return checked((int)(value * scale));
    }
    private static NumericUpDown Field(decimal minimum, decimal maximum, decimal increment) =>
        new() { Minimum = minimum, Maximum = maximum, Increment = increment, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private void Add(StackPanel host, string key, Control field)
    {
        var label = new TextBlock();
        _localization.Bind(label, TextBlock.TextProperty, key);
        host.Children.Add(label);
        host.Children.Add(field);
        _localization.Bind(field, AutomationProperties.NameProperty, key);
    }
}
