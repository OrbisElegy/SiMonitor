// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
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
        ItemsSource = new[] { "胸导联（多选）", "I", "II", "III", "aVR", "aVL", "aVF" },
        SelectedIndex = 2,
        MinWidth = 180,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    internal CheckBox[] ChestLeads { get; } = Enumerable.Range(1, 6).Select(i => new CheckBox { Content = $"V{i}" }).ToArray();
    private readonly WrapPanel _chest = new();
    private readonly TextBlock _targetNote = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private EcgTContourPlan? _preset;
    private readonly StackPanel _biphasic = new() { Spacing = 8 };
    internal TContourParameterEditor()
    {
        Spacing = 8;
        Add(this, "T 波目标导联", Target);
        foreach (var lead in ChestLeads)
        {
            lead.Margin = new Avalonia.Thickness(0, 0, 16, 0);
            AutomationProperties.SetName(lead, $"T 波目标胸导联 {lead.Content}");
            _chest.Children.Add(lead);
        }
        Children.Add(_chest); Children.Add(_targetNote);
        Target.SelectionChanged += (_, _) => RefreshTarget();
        RefreshTarget();
        Add(this, "T 波幅度／第一瓣幅度（μV）", Peak);
        Add(_biphasic, "第二瓣幅度（μV）", SecondPeak);
        Add(_biphasic, "跨越基线位置（占 T 波时限 %）", Crossing);
        Children.Add(_biphasic);
        var reset = new Button { Content = "恢复 T 波模板参数", MinHeight = 44 };
        reset.Click += (_, _) => Reset(_preset);
        Children.Add(reset);
        Children.Add(new TextBlock { Text = "应用后同步监护与十二导联；波形卡片保留模板预览。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
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
        _targetNote.Text = _chest.IsVisible
            ? "至少选择一个胸导联；未选胸导联及肢体导联保持原样。"
            : "肢体导联按电极投影关系联动；胸导联保持原样。";
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
    private static void Add(StackPanel host, string label, Control field)
    {
        host.Children.Add(new TextBlock { Text = label }); host.Children.Add(field);
        AutomationProperties.SetName(field, label);
    }
}
