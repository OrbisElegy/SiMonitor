// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Therapy;
using Monitor.Simulation.Therapy;

namespace Monitor.Desktop;

internal sealed class DefibrillatorSettingsEditor : UserControl
{
    private readonly StackPanel _fields = new() { Spacing = 10 };
    private readonly TextBlock _locked = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    internal TextBlock Error { get; } = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, IsVisible = false };
    internal TextBox Steps { get; } = new() { MaxLength = 512, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
    internal ComboBox Waveform { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal NumericUpDown ChargeSeconds { get; } = new() { Minimum = 0.1m, Maximum = 60, Increment = 0.1m, FormatString = "0.0" };
    internal NumericUpDown AutoDisarmSeconds { get; } = new() { Minimum = 1, Maximum = 300, Increment = 1, FormatString = "0" };
    internal NumericUpDown EcgRecoverySeconds { get; } = new() { Minimum = 0.1m, Maximum = 10, Increment = 0.1m, FormatString = "0.0" };
    internal DefibrillatorConfiguration? DeviceOverride { get; private set; }

    internal DefibrillatorSettingsEditor(DesktopLocalization localization)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(DesktopInformationPages.Help("defibrillator"));
        localization.Bind(_locked, TextBlock.TextProperty, "defib.locked");
        panel.Children.Add(_locked);
        foreach (var (key, control) in new (string, Control)[]
        {
            ("defib.steps", Steps), ("defib.waveform", Waveform),
            ("defib.chargeSeconds", ChargeSeconds), ("defib.autoDisarmSeconds", AutoDisarmSeconds), ("defib.ecgRecoverySeconds", EcgRecoverySeconds)
        })
        {
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
            localization.Bind(label, TextBlock.TextProperty, key);
            localization.Bind(control, AutomationProperties.NameProperty, key);
            _fields.Children.Add(label);
            _fields.Children.Add(control);
        }
        localization.SetChoices(Waveform, Enum.GetValues<DefibrillationWaveformKind>().Select(kind =>
            (Func<Monitor.Application.Localization.ITextLocalizer, string>)(text => text.GetString("defib.waveform." + kind))));
        panel.Children.Add(_fields);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap };
        localization.Bind(hint, TextBlock.TextProperty, "defib.applyHint");
        panel.Children.Add(hint);
        localization.Bind(Error, TextBlock.TextProperty, "defib.invalid");
        panel.Children.Add(Error);
        Content = panel;
        Restore(DefibrillatorConfiguration.Default);
        Steps.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) { ValidateDraft(); } };
        Waveform.SelectionChanged += (_, _) => ValidateDraft();
        ChargeSeconds.ValueChanged += (_, _) => ValidateDraft();
        AutoDisarmSeconds.ValueChanged += (_, _) => ValidateDraft();
        EcgRecoverySeconds.ValueChanged += (_, _) => ValidateDraft();
    }

    internal void SetDeviceOverride(DefibrillatorConfiguration? configuration)
    {
        DeviceOverride = configuration?.Snapshot();
        _fields.IsEnabled = configuration is null;
        _locked.IsVisible = configuration is not null;
        if (configuration is not null) { Restore(configuration); }
        ValidateDraft();
    }

    internal DefibrillatorConfiguration Read()
    {
        if (DeviceOverride is { } fixedConfiguration) { return fixedConfiguration.Snapshot(); }
        try
        {
            string[] values = (Steps.Text ?? "").Split([',', '，', ' ', '\n', '\r', ';', '；'], StringSplitOptions.RemoveEmptyEntries);
            if (ChargeSeconds.Value is not { } seconds || seconds * 1000 != decimal.Truncate(seconds * 1000) ||
                AutoDisarmSeconds.Value is not { } timeout || timeout != decimal.Truncate(timeout) ||
                EcgRecoverySeconds.Value is not { } recovery || recovery * 1000 != decimal.Truncate(recovery * 1000))
            { throw new ArgumentException("Defibrillator.InvalidConfiguration"); }
            return new DefibrillatorConfiguration(values.Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray(),
                (DefibrillationWaveformKind)Waveform.SelectedIndex, checked((int)(seconds * 1000)), checked((int)timeout))
            { EcgRecoveryMilliseconds = checked((int)(recovery * 1000)) }.Snapshot();
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        { throw new ArgumentException("Defibrillator.InvalidConfiguration", error); }
    }

    internal void Restore(DefibrillatorConfiguration configuration)
    {
        var owned = configuration.Snapshot();
        Steps.Text = string.Join(", ", owned.EnergyStepsJoules);
        Waveform.SelectedIndex = (int)owned.Waveform;
        ChargeSeconds.Value = owned.ChargeDurationMilliseconds / 1000m;
        AutoDisarmSeconds.Value = owned.AutoDisarmSeconds;
        EcgRecoverySeconds.Value = owned.EcgRecoveryMilliseconds / 1000m;
    }

    private void ValidateDraft()
    {
        try { _ = Read(); Error.IsVisible = false; }
        catch (ArgumentException) { Error.IsVisible = true; }
    }
}
