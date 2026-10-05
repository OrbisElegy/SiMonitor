// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed class OxygenationPatientPanel : StackPanel
{
    internal CheckBox UseDefaults { get; } = new() { IsChecked = true };
    internal NumericUpDown Age { get; } = Field(18, 90, 35, 1);
    internal ComboBox Sex { get; } = new() { MinWidth = 180 };
    internal NumericUpDown PatientHeight { get; } = Field(140, 210, 175, 1);
    internal NumericUpDown Weight { get; } = Field(40, 150, 70, 1);
    internal CheckBox OverrideBloodVolume { get; } = new();
    internal CheckBox OverrideFrc { get; } = new();
    internal CheckBox OverrideHemoglobin { get; } = new();
    internal CheckBox OverrideBasalDemand { get; } = new();
    internal NumericUpDown BloodVolume { get; } = Field(2500, 7500, 4823.755m, 100);
    internal NumericUpDown Frc { get; } = Field(500, 5000, 2200, 100);
    internal NumericUpDown Hemoglobin { get; } = Field(6, 20, 15, .1m);
    internal NumericUpDown BasalDemand { get; } = Field(0, 500, 250, 10);
    internal TextBlock Summary { get; } = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _details = new() { Spacing = 10 };
    private readonly DesktopLocalization _localization;
    private bool _refreshing;

    internal OxygenationPatientPanel(DesktopLocalization? localization = null)
    {
        _localization = localization ?? new DesktopLocalization();
        Spacing = 10;
        _localization.Bind(UseDefaults, ContentControl.ContentProperty, "oxygenation.useDefaults");
        _localization.Bind(OverrideBloodVolume, ContentControl.ContentProperty, "oxygenation.overrideBloodVolume");
        _localization.Bind(OverrideFrc, ContentControl.ContentProperty, "oxygenation.overrideFrc");
        _localization.Bind(OverrideHemoglobin, ContentControl.ContentProperty, "oxygenation.overrideHemoglobin");
        _localization.Bind(OverrideBasalDemand, ContentControl.ContentProperty, "oxygenation.overrideBasalDemand");
        _localization.SetChoices(Sex, "oxygenation.sexMale", "oxygenation.sexFemale");
        Sex.SelectedIndex = 0;
        Children.Add(UseDefaults);
        Children.Add(_details);
        Add("oxygenation.age", Age);
        Add("oxygenation.sex", Sex);
        Add("oxygenation.height", PatientHeight);
        Add("oxygenation.weight", Weight);
        AddOverride("oxygenation.bloodVolume", OverrideBloodVolume, BloodVolume);
        AddOverride("oxygenation.frc", OverrideFrc, Frc);
        AddOverride("oxygenation.hemoglobin", OverrideHemoglobin, Hemoglobin);
        AddOverride("oxygenation.basalDemand", OverrideBasalDemand, BasalDemand);
        Children.Add(Summary);
        foreach (var field in new[] { Age, PatientHeight, Weight, BloodVolume, Frc, Hemoglobin, BasalDemand })
        { field.ValueChanged += (_, _) => RefreshDefaults(); }
        foreach (var check in new[] { UseDefaults, OverrideBloodVolume, OverrideFrc, OverrideHemoglobin, OverrideBasalDemand })
        { check.IsCheckedChanged += (_, _) => RefreshDefaults(); }
        Sex.SelectionChanged += (_, _) => RefreshDefaults();
        RefreshDefaults();
    }

    private void Add(string key, Control field)
    {
        var label = new TextBlock();
        _localization.Bind(label, TextBlock.TextProperty, key);
        _details.Children.Add(label);
        _details.Children.Add(field);
        _localization.Bind(field, AutomationProperties.NameProperty, key);
    }
    private void AddOverride(string key, CheckBox check, NumericUpDown field)
    {
        _details.Children.Add(check);
        Add(key, field);
    }
    private static NumericUpDown Field(decimal minimum, decimal maximum, decimal value, decimal increment) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Increment = increment,
        FormatString = "0.###",
        Width = 180,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    internal OxygenationPatientPreferences? Read()
    {
        if (UseDefaults.IsChecked != true) { return null; }
        var profile = new OxygenationPatientProfile(ReadNumber(Age, 100, "oxygenation.ageField"), (OxygenationReferenceSex)Sex.SelectedIndex,
            ReadNumber(PatientHeight, 100, "oxygenation.heightField"), ReadNumber(Weight, 100, "oxygenation.weightField"));
        var overrides = new OxygenationBaselineOverrides(
            OverrideBloodVolume.IsChecked == true ? ReadNumber(BloodVolume, 1000, "oxygenation.bloodVolumeField") : null,
            OverrideFrc.IsChecked == true ? ReadNumber(Frc, 1000, "oxygenation.frcField") : null,
            OverrideHemoglobin.IsChecked == true ? ReadNumber(Hemoglobin, 1000, "oxygenation.hemoglobinField") : null,
            OverrideBasalDemand.IsChecked == true ? ReadNumber(BasalDemand, 1000, "oxygenation.basalDemandField") : null);
        return new(profile, overrides, OxygenationPatientDefaults.Resolve(profile, overrides));
    }
    private static decimal ReadNumber(NumericUpDown field, int scale, string key) =>
        DesignPreviewSettings.ReadVitalValue(field, scale, key) / (decimal)scale;

    private void RefreshDefaults()
    {
        if (_refreshing) { return; }
        _refreshing = true;
        try
        {
            _details.IsEnabled = UseDefaults.IsChecked == true;
            BloodVolume.IsEnabled = OverrideBloodVolume.IsChecked == true;
            Frc.IsEnabled = OverrideFrc.IsChecked == true;
            Hemoglobin.IsEnabled = OverrideHemoglobin.IsChecked == true;
            BasalDemand.IsEnabled = OverrideBasalDemand.IsChecked == true;
            var patient = Read();
            if (patient is null)
            {
                SetSummary("oxygenation.fixedBaseline");
                return;
            }
            var values = patient.Resolved;
            if (!BloodVolume.IsEnabled) { BloodVolume.Value = decimal.Round(values.BloodVolume.Value, 3); }
            if (!Frc.IsEnabled) { Frc.Value = decimal.Round(values.Frc.Value, 3); }
            if (!Hemoglobin.IsEnabled) { Hemoglobin.Value = decimal.Round(values.Hemoglobin.Value, 3); }
            if (!BasalDemand.IsEnabled) { BasalDemand.Value = decimal.Round(values.BasalOxygenDemand.Value, 3); }
            SetSummary("oxygenation.patientBaseline");
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { SetSummary(Explain(error)); }
        finally { _refreshing = false; }
    }

    private void SetSummary(string key) => _localization.Bind(Summary, TextBlock.TextProperty, key);

    // The catalog key explaining why a patient baseline cannot be resolved.
    internal static string Explain(Exception error) => error.Message switch
    {
        "OxygenationDefaults.HemoglobinNeedsOverride" => "oxygenation.hemoglobinNeedsOverride",
        "OxygenationDefaults.FrcNeedsOverride" => "oxygenation.frcNeedsOverride",
        "OxygenationDefaults.BloodVolumeNeedsOverride" => "oxygenation.bloodVolumeNeedsOverride",
        "OxygenReservoir.NoReferenceEquilibrium" => "oxygenation.noEquilibrium",
        _ => "oxygenation.invalidBaseline"
    };

    internal void Restore(OxygenationPatientPreferences? saved)
    {
        saved?.Validate();
        _refreshing = true;
        try
        {
            var profile = saved?.Profile ?? OxygenationPatientProfile.Default;
            var overrides = saved?.Overrides ?? new();
            UseDefaults.IsChecked = saved is not null;
            Age.Value = profile.AgeYears;
            Sex.SelectedIndex = (int)profile.Sex;
            PatientHeight.Value = profile.HeightCm;
            Weight.Value = profile.WeightKg;
            OverrideBloodVolume.IsChecked = overrides.BloodVolumeMl.HasValue;
            OverrideFrc.IsChecked = overrides.FrcMlBtps.HasValue;
            OverrideHemoglobin.IsChecked = overrides.HemoglobinGramsPerDl.HasValue;
            OverrideBasalDemand.IsChecked = overrides.BasalOxygenDemandMlStpdPerMinute.HasValue;
            BloodVolume.Value = overrides.BloodVolumeMl ?? 4823.755m;
            Frc.Value = overrides.FrcMlBtps ?? 2200;
            Hemoglobin.Value = overrides.HemoglobinGramsPerDl ?? 15;
            BasalDemand.Value = overrides.BasalOxygenDemandMlStpdPerMinute ?? 250;
        }
        finally { _refreshing = false; }
        RefreshDefaults();
    }
}
