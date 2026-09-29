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
    private EcgTContourPlan? _preset;
    private readonly StackPanel _biphasic = new() { Spacing = 8 };
    internal TContourParameterEditor()
    {
        Spacing = 8;
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
        return preset with
        {
            PeakMicrovolts = ReadInteger(Peak, 1),
            SecondPeakMicrovolts = _biphasic.IsVisible ? ReadInteger(SecondPeak, 1) : null,
            CrossingPositionPermille = _biphasic.IsVisible ? ReadInteger(Crossing, 10) : null
        };
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
