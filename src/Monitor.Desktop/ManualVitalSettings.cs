// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class ManualVitalSettings : StackPanel
{
    internal event Action? DraftChanged;
    internal CheckBox NibpEnabled { get; } = new();
    internal CheckBox TemperatureEnabled { get; } = new();
    internal CheckBox[] CustomEnabled { get; } = [new(), new()];
    internal NumericUpDown Systolic { get; } = Number(0, 300, 120, 1);
    internal NumericUpDown Diastolic { get; } = Number(0, 300, 80, 1);
    internal NumericUpDown Mean { get; } = Number(0, 300, 93, 1);
    internal NumericUpDown Temperature { get; } = Number(0, 50, 36.5m, .1m);
    internal TextBox[] Names { get; } = [new() { MaxLength = 24 }, new() { MaxLength = 24 }];
    internal TextBox[] Units { get; } = [new() { MaxLength = 12 }, new() { MaxLength = 12 }];
    internal NumericUpDown[] Values { get; } = [Number(-99999, 99999, 0, .01m), Number(-99999, 99999, 0, .01m)];
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };

    internal ManualVitalSettings(DesktopLocalization localization)
    {
        Spacing = 12;
        AddToggle(NibpEnabled, "manual.nibp", Systolic, Diastolic, Mean);
        Add("manual.systolic", Systolic);
        Add("manual.diastolic", Diastolic);
        Add("manual.mean", Mean);
        AddToggle(TemperatureEnabled, "manual.temperature", Temperature);
        Add("manual.celsius", Temperature);
        for (int i = 0; i < 2; i++)
        {
            AddToggle(CustomEnabled[i], "manual.custom" + (i + 1), Names[i], Values[i], Units[i]);
            Add("manual.name", Names[i]);
            Add("manual.value", Values[i]);
            Add("manual.unit", Units[i]);
        }
        Children.Add(_error);
        localization.Bind(_error, TextBlock.TextProperty, text => ErrorKey() is { } key ? text.GetString(key) : "");
        foreach (var field in new[] { Systolic, Diastolic, Mean, Temperature }.Concat(Values))
        { field.ValueChanged += (_, _) => Refresh(); }
        foreach (var field in Names.Concat(Units)) { field.TextChanged += (_, _) => Refresh(); }
        Refresh();

        void Refresh()
        {
            localization.Bind(_error, TextBlock.TextProperty, text => ErrorKey() is { } key ? text.GetString(key) : "");
            _error.IsVisible = ErrorKey() is not null;
            DraftChanged?.Invoke();
        }
        void Add(string key, Control control)
        {
            var label = new TextBlock();
            localization.Bind(label, TextBlock.TextProperty, key);
            localization.Bind(control, AutomationProperties.NameProperty, key);
            control.Width = 200;
            control.HorizontalAlignment = HorizontalAlignment.Left;
            Children.Add(label);
            Children.Add(control);
        }
        void AddToggle(CheckBox toggle, string key, params Control[] fields)
        {
            localization.Bind(toggle, ContentControl.ContentProperty, key);
            Children.Add(toggle);
            foreach (var field in fields) { field.IsEnabled = false; }
            toggle.IsCheckedChanged += (_, _) =>
            {
                foreach (var field in fields) { field.IsEnabled = toggle.IsChecked == true; }
                Refresh();
            };
        }
    }

    internal string? ErrorKey()
    {
        try { Read(); return null; }
        catch (VitalValueException) { return "manual.invalidNumber"; }
        catch (ArgumentException error) { return error.Message; }
    }

    internal ManualVitalSigns Read() => new(
        NibpEnabled.IsChecked == true ? new(
            DesignPreviewSettings.ReadVitalValue(Systolic, 1, "manual.systolic"),
            DesignPreviewSettings.ReadVitalValue(Diastolic, 1, "manual.diastolic"),
            DesignPreviewSettings.ReadVitalValue(Mean, 1, "manual.mean")) : null,
        TemperatureEnabled.IsChecked == true ? DesignPreviewSettings.ReadVitalValue(Temperature, 10, "manual.celsius") : null,
        ReadCustom(0), ReadCustom(1));

    private ManualCustomVital? ReadCustom(int slot) => CustomEnabled[slot].IsChecked == true
        ? new(Names[slot].Text ?? "", DesignPreviewSettings.ReadVitalValue(Values[slot], 100, "manual.value") / 100m,
            Units[slot].Text ?? "") : null;

    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, decimal increment) => new()
    { Minimum = minimum, Maximum = maximum, Value = value, Increment = increment, FormatString = "0.##" };
}
