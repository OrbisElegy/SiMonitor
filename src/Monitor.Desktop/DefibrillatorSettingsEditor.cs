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
    private DefibrillatorConfiguration _editable = DefibrillatorConfiguration.Default;
    private IReadOnlyList<int> _skinEnergySteps = DefibrillatorConfiguration.Default.EnergyStepsJoules;
    private string _manualSteps = string.Join(", ", DefibrillatorConfiguration.Default.EnergyStepsJoules);
    private bool _restoring;
    internal TextBlock Error { get; } = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, IsVisible = false };
    internal TextBox AedSteps { get; } = new() { MaxLength = 512, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
    internal TextBox Steps { get; } = new() { MaxLength = 512, TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
    internal ComboBox Waveform { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal NumericUpDown ChargeSeconds { get; } = new() { Minimum = 0.1m, Maximum = 60, Increment = 0.1m, FormatString = "0.0" };
    internal NumericUpDown AutoDisarmSeconds { get; } = new() { Minimum = 1, Maximum = 300, Increment = 1, FormatString = "0" };
    internal NumericUpDown EcgRecoverySeconds { get; } = new() { Minimum = 0.1m, Maximum = 10, Increment = 0.1m, FormatString = "0.0" };
    internal DefibrillatorConfiguration? DeviceOverride { get; private set; }
    internal ToggleSwitch UseSkinEnergySteps { get; } = new() { IsChecked = true, MinHeight = 44 };
    internal bool CustomEnergySteps => UseSkinEnergySteps.IsChecked != true;

    internal DefibrillatorSettingsEditor(DesktopLocalization localization)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(DesktopInformationPages.Help("defibrillator"));
        localization.Bind(_locked, TextBlock.TextProperty, "defib.profileLocked");
        panel.Children.Add(_locked);
        localization.Bind(UseSkinEnergySteps, ContentControl.ContentProperty, "defib.useSkinEnergySteps");
        localization.Bind(UseSkinEnergySteps, AutomationProperties.NameProperty, "defib.useSkinEnergySteps");
        panel.Children.Add(UseSkinEnergySteps);
        foreach (var (key, control) in new (string, Control)[]
        {
            ("defib.steps", Steps), ("defib.aedSteps", AedSteps), ("defib.waveform", Waveform),
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
        Steps.PropertyChanged += (_, args) =>
        {
            if (args.Property != TextBox.TextProperty || _restoring) { return; }
            if (CustomEnergySteps) { _manualSteps = Steps.Text ?? ""; }
            ValidateDraft();
        };
        UseSkinEnergySteps.IsCheckedChanged += (_, _) =>
        {
            if (_restoring) { return; }
            if (!CustomEnergySteps) { _manualSteps = Steps.Text ?? ""; }
            RefreshEnergySelection();
            ValidateDraft();
        };
        AedSteps.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty) { ValidateDraft(); }
        };
        Waveform.SelectionChanged += (_, _) => ValidateDraft();
        ChargeSeconds.ValueChanged += (_, _) => ValidateDraft();
        AutoDisarmSeconds.ValueChanged += (_, _) => ValidateDraft();
        EcgRecoverySeconds.ValueChanged += (_, _) => ValidateDraft();
    }

    internal void SetDeviceOverride(DefibrillatorConfiguration? configuration)
    {
        SetSkinDefaults(configuration, configuration?.EnergyStepsJoules ?? _skinEnergySteps, CustomEnergySteps, _editable);
    }

    internal void SetSkinDefaults(DefibrillatorConfiguration? deviceProfile, IReadOnlyList<int> skinEnergySteps,
        bool customEnergySteps, DefibrillatorConfiguration editable)
    {
        // Validate and snapshot every input before publishing a new editor state.
        var ownedEditable = editable.Snapshot();
        var ownedProfile = deviceProfile?.Snapshot();
        var ownedSteps = (ownedEditable with { EnergyStepsJoules = skinEnergySteps }).Snapshot().EnergyStepsJoules;
        var effective = (ownedEditable with
        { EnergyStepsJoules = customEnergySteps ? ownedEditable.EnergyStepsJoules : ownedSteps }).Snapshot();
        _restoring = true;
        try
        {
            DeviceOverride = ownedProfile;
            _editable = ownedEditable;
            _skinEnergySteps = ownedSteps;
            _manualSteps = string.Join(", ", ownedEditable.EnergyStepsJoules);
            AedSteps.Text = string.Join(", ", ownedEditable.AedEnergySequenceJoules?.Select(effective.NearestEnergy) ?? []);
            UseSkinEnergySteps.IsChecked = !customEnergySteps;
            RestoreBehaviorFields(ownedProfile ?? ownedEditable);
            foreach (var control in new Control[] { Waveform, ChargeSeconds, AutoDisarmSeconds, EcgRecoverySeconds })
            { control.IsEnabled = ownedProfile is null; }
            _locked.IsVisible = ownedProfile is not null;
        }
        finally { _restoring = false; }
        RefreshEnergySelection();
        ValidateDraft();
    }

    internal DefibrillatorConfiguration Read()
    {
        try
        {
            IReadOnlyList<int> steps = _skinEnergySteps;
            if (CustomEnergySteps)
            {
                string[] values = (Steps.Text ?? "").Split([',', '，', ' ', '\n', '\r', ';', '；'], StringSplitOptions.RemoveEmptyEntries);
                steps = values.Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            }
            int[]? sequence = string.IsNullOrWhiteSpace(AedSteps.Text) ? null :
                AedSteps.Text.Split([',', '，', ' ', '\n', '\r', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            if (sequence is not null && sequence.Any(energy => !steps.Contains(energy)))
            { throw new ArgumentException("Defibrillator.InvalidConfiguration"); }
            if (DeviceOverride is { } fixedConfiguration)
            { return (fixedConfiguration with { EnergyStepsJoules = steps, AedEnergySequenceJoules = sequence }).Snapshot(); }
            if (ChargeSeconds.Value is not { } seconds || seconds * 1000 != decimal.Truncate(seconds * 1000) ||
                AutoDisarmSeconds.Value is not { } timeout || timeout != decimal.Truncate(timeout) ||
                EcgRecoverySeconds.Value is not { } recovery || recovery * 1000 != decimal.Truncate(recovery * 1000))
            { throw new ArgumentException("Defibrillator.InvalidConfiguration"); }
            return new DefibrillatorConfiguration(steps,
                (DefibrillationWaveformKind)Waveform.SelectedIndex, checked((int)(seconds * 1000)), checked((int)timeout))
            { EcgRecoveryMilliseconds = checked((int)(recovery * 1000)), AedEnergySequenceJoules = sequence }.Snapshot();
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        { throw new ArgumentException("Defibrillator.InvalidConfiguration", error); }
    }

    internal void Restore(DefibrillatorConfiguration configuration)
    {
        var owned = configuration.Snapshot();
        _restoring = true;
        try
        {
            _editable = owned;
            _manualSteps = string.Join(", ", owned.EnergyStepsJoules);
            AedSteps.Text = string.Join(", ", owned.AedEnergySequenceJoules ?? []);
            RestoreBehaviorFields(DeviceOverride ?? owned);
        }
        finally { _restoring = false; }
        RefreshEnergySelection();
        ValidateDraft();
    }

    private void RestoreBehaviorFields(DefibrillatorConfiguration configuration)
    {
        Waveform.SelectedIndex = (int)configuration.Waveform;
        ChargeSeconds.Value = configuration.ChargeDurationMilliseconds / 1000m;
        AutoDisarmSeconds.Value = configuration.AutoDisarmSeconds;
        EcgRecoverySeconds.Value = configuration.EcgRecoveryMilliseconds / 1000m;
    }

    private void RefreshEnergySelection()
    {
        _restoring = true;
        try
        {
            Steps.Text = CustomEnergySteps ? _manualSteps : string.Join(", ", _skinEnergySteps);
            Steps.IsEnabled = CustomEnergySteps;
        }
        finally { _restoring = false; }
    }

    private void ValidateDraft()
    {
        if (_restoring) { return; }
        try { _ = Read(); Error.IsVisible = false; }
        catch (ArgumentException) { Error.IsVisible = true; }
    }
}
