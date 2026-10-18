// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed class EcgInterferenceEditor : UserControl
{
    internal NumericUpDown SpikeAmplitude { get; } = Field(0, 5000, 50, 0);
    internal NumericUpDown SpikeProbability { get; } = Field(0, 100, 1, 10);
    internal NumericUpDown BaselineAmplitude { get; } = Field(0, 2000, 25, 0);
    internal NumericUpDown BaselineKnotSeconds { get; } = Field(0.2m, 10, 0.1m, 2);
    internal TextBlock Error { get; } = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, IsVisible = false };

    internal EcgInterferenceEditor(DesktopLocalization localization)
    {
        var panel = new StackPanel { Spacing = 8 };
        var title = new TextBlock { FontWeight = FontWeight.SemiBold };
        localization.Bind(title, TextBlock.TextProperty, "ecgInterference.title");
        panel.Children.Add(title);
        foreach (var (key, control) in new (string, NumericUpDown)[]
        {
            ("ecgInterference.spikeAmplitude", SpikeAmplitude), ("ecgInterference.spikeProbability", SpikeProbability),
            ("ecgInterference.baselineAmplitude", BaselineAmplitude), ("ecgInterference.baselineKnotSeconds", BaselineKnotSeconds)
        })
        {
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
            localization.Bind(label, TextBlock.TextProperty, key);
            localization.Bind(control, AutomationProperties.NameProperty, key);
            panel.Children.Add(label);
            panel.Children.Add(control);
            control.ValueChanged += (_, _) =>
            {
                try { _ = Read(new string('0', 63) + "1"); Error.IsVisible = false; }
                catch (ArgumentException) { Error.IsVisible = true; }
            };
        }
        localization.Bind(Error, TextBlock.TextProperty, "ecgInterference.invalid");
        panel.Children.Add(Error);
        panel.Children.Add(DesktopInformationPages.Help("ecg-interference"));
        Content = panel;
    }

    internal SeededEcgInterference? Read(string seedHex)
    {
        int spike = ReadNumber(SpikeAmplitude, 1);
        int probability = ReadNumber(SpikeProbability, 10);
        int baseline = ReadNumber(BaselineAmplitude, 1);
        int knot = ReadNumber(BaselineKnotSeconds, 1000);
        return spike == 0 && baseline == 0 ? null : new(seedHex, spike, probability, baseline, knot);
    }

    private static int ReadNumber(NumericUpDown field, int multiplier)
    {
        if (field.Value is not { } value || value < field.Minimum || value > field.Maximum ||
            value * multiplier != decimal.Truncate(value * multiplier))
        { throw new ArgumentException("EcgInterference.InvalidConfiguration"); }
        return checked((int)(value * multiplier));
    }

    private static NumericUpDown Field(decimal minimum, decimal maximum, decimal increment, decimal value) =>
        new() { Minimum = minimum, Maximum = maximum, Increment = increment, Value = value, FormatString = "0.#" };
}
