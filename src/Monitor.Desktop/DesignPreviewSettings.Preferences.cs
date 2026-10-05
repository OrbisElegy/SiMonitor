// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewSettings
{
    private Dictionary<string, NumericUpDown> GeneratorNumbers() => new()
    {
        ["HeartRate"] = HeartRate,
        ["RateVariation"] = RateVariation,
        ["RespiratoryRate"] = RespiratoryRate,
        ["InspirationPercent"] = InspirationPercent,
        ["EtCo2Target"] = EtCo2Target,
        ["EtCo2Variation"] = EtCo2Variation,
        ["AbpPulseGain"] = AbpPulseGain,
        ["PaPulseGain"] = PaPulseGain,
        ["CvpBaseline"] = CvpBaseline,
        ["AbpSystolic"] = AbpSystolic,
        ["AbpDiastolic"] = AbpDiastolic,
        ["PaSystolic"] = PaSystolic,
        ["PaDiastolic"] = PaDiastolic,
        ["OpticalTarget"] = OpticalTarget,
        ["OpticalVariation"] = OpticalVariation,
        ["OpticalModulation"] = OpticalModulation,
        ["RespSignalAmplitude"] = RespSignalAmplitude,
        ["RespCardiacArtifact"] = RespCardiacArtifact,
        ["Co2TransportDelay"] = Co2TransportDelay,
        ["Co2DispersionStep"] = Co2DispersionStep,
        ["Co2DeadSpace"] = Co2DeadSpace,
        ["Co2Rise"] = Co2Rise,
        ["Co2Fall"] = Co2Fall,
        ["Co2Baseline"] = Co2Baseline,
        ["Co2PlateauStart"] = Co2PlateauStart,
        ["TContourParameters.Peak"] = TContourParameters.Peak,
        ["TContourParameters.SecondPeak"] = TContourParameters.SecondPeak,
        ["TContourParameters.Crossing"] = TContourParameters.Crossing,
        ["InfarctionParameters.Delay"] = InfarctionParameters.Delay,
        ["InfarctionParameters.QrsWeight"] = InfarctionParameters.QrsWeight,
        ["InfarctionParameters.TPeak"] = InfarctionParameters.TPeak,
        ["InfarctionParameters.JPoint"] = InfarctionParameters.JPoint,
        ["InfarctionParameters.StEnd"] = InfarctionParameters.StEnd,
        ["InfarctionParameters.StArch"] = InfarctionParameters.StArch,
    };

    private Dictionary<string, ComboBox> GeneratorChoices() => new()
    {
        ["TContourParameters.Target"] = TContourParameters.Target,
        ["InfarctionParameters.Necrosis"] = InfarctionParameters.Necrosis,
        ["InfarctionParameters.IschemiaRegion"] = InfarctionParameters.IschemiaRegion,
        ["InfarctionParameters.InjuryRegion"] = InfarctionParameters.InjuryRegion,
        ["InfarctionParameters.NecrosisRegion"] = InfarctionParameters.NecrosisRegion,
    };

    private Dictionary<string, CheckBox> GeneratorFlags()
    {
        var flags = new Dictionary<string, CheckBox>
        {
            ["OpticalEnabled"] = OpticalEnabled,
            ["CardiacRateEnabled"] = CardiacRateEnabled,
            ["AbpTargetEnabled"] = AbpTargetEnabled,
            ["PaTargetEnabled"] = PaTargetEnabled,
            ["Co2CustomPlateau"] = Co2CustomPlateau,
            ["InfarctionParameters.ComponentsEnabled"] = InfarctionParameters.ComponentsEnabled,
            ["InfarctionParameters.ReferenceT"] = InfarctionParameters.ReferenceT,
            ["InfarctionParameters.SeparateRegions"] = InfarctionParameters.SeparateRegions,
        };
        for (int i = 0; i < 6; i++)
        {
            flags["TChest" + i] = TContourParameters.ChestLeads[i];
            flags["InfarctionChest" + i] = InfarctionParameters.ChestLeads[i];
        }
        return flags;
    }
    // Fields added after the first saved format. Older files omit them, and restoring
    // keeps these controls at their defaults; every other field must be present.
    private static readonly HashSet<string> LaterGeneratorFields = new(StringComparer.Ordinal)
    {
        "AbpSystolic", "AbpDiastolic", "PaSystolic", "PaDiastolic", "AbpTargetEnabled", "PaTargetEnabled"
    };
    private static bool FieldsMatch(IEnumerable<string> expected, IEnumerable<string> saved)
    {
        var savedKeys = saved.ToHashSet(StringComparer.Ordinal);
        var expectedKeys = expected.ToHashSet(StringComparer.Ordinal);
        return savedKeys.IsSubsetOf(expectedKeys) && expectedKeys.Where(key => !savedKeys.Contains(key)).All(LaterGeneratorFields.Contains);
    }
    internal MonitorGeneratorPreferences CaptureGenerator()
    {
        // Dormant invalid numeric drafts are not effective source inputs.
        var result = new MonitorGeneratorPreferences(EcgSelection, EcgChoices[EcgSelection], RespirationSelection,
            EjectionSelection, RateSeed.Text ?? "", GeneratorNumbers().ToDictionary(p => p.Key,
                p => p.Value.Value is { } v && v >= p.Value.Minimum && v <= p.Value.Maximum ? (decimal?)v : null),
            GeneratorFlags().ToDictionary(p => p.Key, p => p.Value.IsChecked == true),
            GeneratorChoices().ToDictionary(p => p.Key, p => p.Value.SelectedIndex))
        { ApplyDelaySeconds = ReadApplyDelayNs() / 1_000_000_000m, Oxygenation = Oxygenation.Capture(OpticalEnabled.IsChecked == true) };
        result.Validate();
        return result;
    }
    internal void RestoreGenerator(MonitorGeneratorPreferences saved)
    {
        saved.Validate();
        if (saved.Ecg >= EcgChoices.Length || EcgChoices[saved.Ecg] != saved.EcgName)
        { throw new ArgumentException("GeneratorPreferences.TemplateMismatch"); }
        var numbers = GeneratorNumbers();
        var flags = GeneratorFlags();
        var choices = GeneratorChoices();
        if (!FieldsMatch(numbers.Keys, saved.Numbers.Keys) ||
            !FieldsMatch(flags.Keys, saved.Flags.Keys) || !choices.Keys.ToHashSet().SetEquals(saved.Choices.Keys))
        { throw new ArgumentException("GeneratorPreferences.FieldsMismatch"); }
        foreach (var (key, field) in numbers)
        {
            if (saved.Numbers.TryGetValue(key, out decimal? value) && value is { } number && (number < field.Minimum || number > field.Maximum))
            { throw new ArgumentException("GeneratorPreferences.Range"); }
        }
        foreach (var (key, field) in choices)
        {
            if (saved.Choices[key] < -1 || saved.Choices[key] >= field.Items.Count)
            { throw new ArgumentException("GeneratorPreferences.Choice"); }
        }
        ApplyDelaySeconds.Value = saved.ApplyDelaySeconds;
        EcgSelection = saved.Ecg;
        RespirationSelection = saved.Respiration;
        EjectionSelection = saved.Ejection;
        foreach (var (key, field) in numbers)
        {
            if (saved.Numbers.TryGetValue(key, out decimal? value)) { field.Value = value; }
        }
        foreach (var (key, field) in choices) { field.SelectedIndex = saved.Choices[key]; }
        foreach (var (key, field) in flags)
        {
            if (saved.Flags.TryGetValue(key, out bool value)) { field.IsChecked = value; }
        }
        Oxygenation.Restore(saved.Oxygenation);
        RateSeed.Text = saved.Seed;
        foreach (var refresh in _refreshSignalRows) { refresh(); }
    }
}
