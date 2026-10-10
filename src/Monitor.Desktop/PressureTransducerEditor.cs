// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Application.Measurements;

namespace Monitor.Desktop;

internal sealed class PressureTransducerEditor : UserControl
{
    internal NumericUpDown Abp { get; } = Field();
    internal NumericUpDown Pa { get; } = Field();
    internal NumericUpDown Cvp { get; } = Field();
    internal NumericUpDown AbpPulse { get; } = PulseField();
    internal NumericUpDown PaPulse { get; } = PulseField();
    internal TextBlock Error { get; } = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, IsVisible = false };

    internal PressureTransducerEditor(DesktopLocalization localization)
    {
        var panel = new StackPanel { Spacing = 10 };
        foreach (var (key, field) in new[] { ("pressureRange.abp", Abp), ("pressureRange.pa", Pa), ("pressureRange.cvp", Cvp),
            ("pressureRange.abpPulse", AbpPulse), ("pressureRange.paPulse", PaPulse) })
        {
            var label = new TextBlock();
            localization.Bind(label, TextBlock.TextProperty, key);
            localization.Bind(field, AutomationProperties.NameProperty, key);
            panel.Children.Add(label);
            panel.Children.Add(field);
            field.ValueChanged += (_, _) => ValidateDraft();
        }
        localization.Bind(Error, TextBlock.TextProperty, "pressureRange.invalid");
        panel.Children.Add(Error);
        panel.Children.Add(DesktopInformationPages.Help("pressure-transducers"));
        Content = panel;
    }

    private static NumericUpDown Field() => new() { Minimum = -100, Maximum = 400, Value = -50, Increment = 1, FormatString = "0.00" };

    private static NumericUpDown PulseField() => new() { Minimum = 2, Maximum = 100, Value = 3, Increment = 1, FormatString = "0.00" };

    internal PressureTransducerLimits Read()
    {
        int ReadField(NumericUpDown field)
        {
            if (field.Value is not { } value || value < field.Minimum || value > field.Maximum || value * 100 != decimal.Truncate(value * 100))
            { throw new ArgumentException("PressureTransducer.InvalidLimits"); }
            return (int)(value * 100);
        }
        var limits = new PressureTransducerLimits(ReadField(Abp), ReadField(Pa), ReadField(Cvp), ReadField(AbpPulse), ReadField(PaPulse));
        limits.Validate();
        return limits;
    }

    internal void Restore(PressureTransducerLimits limits)
    {
        limits.Validate();
        Abp.Value = limits.AbpMinimumCentiMmHg / 100m;
        Pa.Value = limits.PaMinimumCentiMmHg / 100m;
        Cvp.Value = limits.CvpMinimumCentiMmHg / 100m;
        AbpPulse.Value = limits.AbpMinimumPulseCentiMmHg / 100m;
        PaPulse.Value = limits.PaMinimumPulseCentiMmHg / 100m;
    }

    private void ValidateDraft()
    {
        try { _ = Read(); Error.IsVisible = false; }
        catch (ArgumentException) { Error.IsVisible = true; }
    }
}
