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
    internal CheckBox UseDefaults { get; } = new() { Content = "按患者资料计算基线（0 SD）", IsChecked = true };
    internal NumericUpDown Age { get; } = Field(18, 90, 35, 1);
    internal ComboBox Sex { get; } = new() { ItemsSource = new[] { "男性参考系数", "女性参考系数" }, SelectedIndex = 0, MinWidth = 180 };
    internal NumericUpDown PatientHeight { get; } = Field(140, 210, 175, 1);
    internal NumericUpDown Weight { get; } = Field(40, 150, 70, 1);
    internal CheckBox OverrideBloodVolume { get; } = new() { Content = "手工指定血容量" };
    internal CheckBox OverrideFrc { get; } = new() { Content = "手工指定 FRC" };
    internal CheckBox OverrideHemoglobin { get; } = new() { Content = "手工指定 Hb" };
    internal CheckBox OverrideBasalDemand { get; } = new() { Content = "手工指定基础耗氧量" };
    internal NumericUpDown BloodVolume { get; } = Field(2500, 7500, 4823.755m, 100);
    internal NumericUpDown Frc { get; } = Field(500, 5000, 2200, 100);
    internal NumericUpDown Hemoglobin { get; } = Field(6, 20, 15, .1m);
    internal NumericUpDown BasalDemand { get; } = Field(0, 500, 250, 10);
    internal TextBlock Summary { get; } = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _details = new() { Spacing = 10 };
    private bool _refreshing;

    internal OxygenationPatientPanel()
    {
        Spacing = 10;
        Children.Add(UseDefaults);
        Children.Add(_details);
        Add("年龄（岁）", Age);
        Add("生理参考公式", Sex);
        Add("身高（cm）", PatientHeight);
        Add("体重（kg）", Weight);
        AddOverride("血容量（mL）", OverrideBloodVolume, BloodVolume);
        AddOverride("功能残气量 FRC（mL，BTPS）", OverrideFrc, Frc);
        AddOverride("血红蛋白 Hb（g/dL）", OverrideHemoglobin, Hemoglobin);
        AddOverride("基础耗氧需求（mL O₂/min，STPD）", OverrideBasalDemand, BasalDemand);
        Children.Add(Summary);
        foreach (var field in new[] { Age, PatientHeight, Weight, BloodVolume, Frc, Hemoglobin, BasalDemand })
        { field.ValueChanged += (_, _) => RefreshDefaults(); }
        foreach (var check in new[] { UseDefaults, OverrideBloodVolume, OverrideFrc, OverrideHemoglobin, OverrideBasalDemand })
        { check.IsCheckedChanged += (_, _) => RefreshDefaults(); }
        Sex.SelectionChanged += (_, _) => RefreshDefaults();
        RefreshDefaults();
    }

    private void Add(string label, Control field)
    {
        _details.Children.Add(new TextBlock { Text = label });
        _details.Children.Add(field);
        AutomationProperties.SetName(field, label);
    }
    private void AddOverride(string label, CheckBox check, NumericUpDown field)
    {
        _details.Children.Add(check);
        Add(label, field);
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
        var profile = new OxygenationPatientProfile(ReadNumber(Age, 100, "年龄"), (OxygenationReferenceSex)Sex.SelectedIndex,
            ReadNumber(PatientHeight, 100, "身高"), ReadNumber(Weight, 100, "体重"));
        var overrides = new OxygenationBaselineOverrides(
            OverrideBloodVolume.IsChecked == true ? ReadNumber(BloodVolume, 1000, "血容量") : null,
            OverrideFrc.IsChecked == true ? ReadNumber(Frc, 1000, "FRC") : null,
            OverrideHemoglobin.IsChecked == true ? ReadNumber(Hemoglobin, 1000, "Hb") : null,
            OverrideBasalDemand.IsChecked == true ? ReadNumber(BasalDemand, 1000, "基础耗氧量") : null);
        return new(profile, overrides, OxygenationPatientDefaults.Resolve(profile, overrides));
    }
    private static decimal ReadNumber(NumericUpDown field, int scale, string label) =>
        DesignPreviewSettings.ReadVitalValue(field, scale, label) / (decimal)scale;

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
                Summary.Text = "使用固定基线；切换基线需从头开始。";
                return;
            }
            var values = patient.Resolved;
            if (!BloodVolume.IsEnabled) { BloodVolume.Value = decimal.Round(values.BloodVolume.Value, 3); }
            if (!Frc.IsEnabled) { Frc.Value = decimal.Round(values.Frc.Value, 3); }
            if (!Hemoglobin.IsEnabled) { Hemoglobin.Value = decimal.Round(values.Hemoglobin.Value, 3); }
            if (!BasalDemand.IsEnabled) { BasalDemand.Value = decimal.Round(values.BasalOxygenDemand.Value, 3); }
            Summary.Text = "患者资料与基线修改需从头开始。";
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { Summary.Text = Explain(error); }
        finally { _refreshing = false; }
    }

    internal static string Explain(Exception error) => error.Message switch
    {
        "OxygenationDefaults.HemoglobinNeedsOverride" => "当前 Hb 参考资料不覆盖 18–19 岁，请手工指定 Hb。",
        "OxygenationDefaults.FrcNeedsOverride" => "GLI FRC 参考资料不覆盖 80 岁以上，请手工指定 FRC。",
        "OxygenationDefaults.BloodVolumeNeedsOverride" => "当前血容量估算仅支持 BMI 18.5–<30，请手工指定血容量。",
        "OxygenReservoir.NoReferenceEquilibrium" => "这组 Hb、基础耗氧和参考循环无法建立初始平衡，请检查基线。",
        _ => "患者基线无效或超出成人模型范围，请检查资料和手工指定的数值。"
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
