// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class OxygenationDefaultsSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FrcCentersMatchIndependentPublishedEquationVectors), FrcCentersMatchIndependentPublishedEquationVectors),
        new(nameof(AdultDefaultsRespectSexAgeAndUnits), AdultDefaultsRespectSexAgeAndUnits),
        new(nameof(ExplicitBaselinesAndReferenceLimitsRemainDistinct), ExplicitBaselinesAndReferenceLimitsRemainDistinct),
        new(nameof(BaselinePreferencesRetainValuesAndProvenance), BaselinePreferencesRetainValuesAndProvenance),
        new(nameof(TeacherMultiplierChangesDemandWithoutResettingStores), TeacherMultiplierChangesDemandWithoutResettingStores),
        new(nameof(TeacherMultiplierRetainsDeterministicConservation), TeacherMultiplierRetainsDeterministicConservation),
    ];

    private static void FrcCentersMatchIndependentPublishedEquationVectors()
    {
        using var stream = typeof(OxygenationDefaultsSpecifications).Assembly.GetManifestResourceStream("OxygenationDefaultsValidation")!;
        using var document = JsonDocument.Parse(stream);
        Check.That(document.RootElement.GetProperty("ReferenceSetId").GetString() == OxygenationPatientDefaults.ReferenceSetId,
            "independent vector reference version matches");
        int count = 0;
        foreach (var row in document.RootElement.GetProperty("Frc").EnumerateArray())
        {
            var profile = new OxygenationPatientProfile(row.GetProperty("AgeYears").GetDecimal(),
                row.GetProperty("Sex").GetString() == "male" ? OxygenationReferenceSex.Male : OxygenationReferenceSex.Female,
                row.GetProperty("HeightCm").GetDecimal(), 70);
            decimal actual = OxygenationPatientDefaults.PredictFrcMlBtps(profile);
            Check.That(Math.Abs(actual - row.GetProperty("FrcMlBtps").GetDecimal()) < .000001m,
                "decimal LMS M matches independent Python log/exp with the original quarter-year spline, including interpolation and age80");
            count++;
        }
        Check.That(count == 110, "all independent FRC vectors checked");
    }

    private static void AdultDefaultsRespectSexAgeAndUnits()
    {
        var male = OxygenationPatientProfile.Default;
        var female = male with { Sex = OxygenationReferenceSex.Female };
        var first = OxygenationPatientDefaults.Resolve(male);
        var second = OxygenationPatientDefaults.Resolve(female);
        Check.That(first.BloodVolume.Value == 4823.7546875m && second.BloodVolume.Value == 4407.3734375m,
            "Nadler uses height in metres, sex-specific coefficients and liquid millilitres");
        Check.That(first.Hemoglobin.Value == 15.29m && second.Hemoglobin.Value == 13.66m &&
            first.Frc.Value > second.Frc.Value && first.BasalOxygenDemand.Value > second.BasalOxygenDemand.Value,
            "sex-specific central values replace the former fixed constants");
        foreach (var (age, maleHb, femaleHb) in new (decimal, decimal, decimal)[]
        {
            (20, 15.51m, 13.57m), (29.99m, 15.51m, 13.57m), (30, 15.29m, 13.66m),
            (40, 15.20m, 13.58m), (50, 15.17m, 13.76m), (60, 14.94m, 13.66m),
            (70, 14.70m, 13.68m), (80, 14.29m, 13.44m), (90, 14.29m, 13.44m)
        })
        {
            Check.That(OxygenationPatientDefaults.ReferenceHemoglobinGramsPerDl(male with { AgeYears = age }) == maleHb &&
                OxygenationPatientDefaults.ReferenceHemoglobinGramsPerDl(female with { AgeYears = age }) == femaleHb,
                "Hb is the published age-bin mean, not a range midpoint or interpolated value");
        }
        foreach (var (age, maleKcal, femaleKcal) in new (decimal, decimal, decimal)[]
        { (29.99m, 1746.19m, 1523.86m), (30, 1676.14m, 1414.42m), (59.99m, 1676.14m, 1414.42m), (60, 1407.47m, 1294.24m) })
        {
            Check.That(Math.Abs(OxygenationPatientDefaults.PredictBasalOxygenDemandMlStpdPerMinute(male with { AgeYears = age }) *
                1440 * (3.941m + 1.106m * .8m) / 1000 - maleKcal) < .000000001m &&
                Math.Abs(OxygenationPatientDefaults.PredictBasalOxygenDemandMlStpdPerMinute(female with { AgeYears = age }) *
                1440 * (3.941m + 1.106m * .8m) / 1000 - femaleKcal) < .000000001m,
                "Schofield kcal/day at zero residual converts to STPD oxygen without an activity factor");
        }
        Check.That(first.Frc.CenterSelection == "LMS z=0 median" && first.Hemoglobin.Origin == OxygenationBaselineOrigin.ReferenceCenter &&
            first.BasalOxygenDemand.Origin == OxygenationBaselineOrigin.ReferencePrediction,
            "central values and regression predictions have distinct provenance");
    }

    private static void ExplicitBaselinesAndReferenceLimitsRemainDistinct()
    {
        var profile = OxygenationPatientProfile.Default;
        var manual = new OxygenationBaselineOverrides(HemoglobinGramsPerDl: 10, FrcMlBtps: 2200);
        var original = OxygenationPatientDefaults.Resolve(profile, manual);
        var edited = OxygenationPatientDefaults.Resolve(profile with { AgeYears = 65, Sex = OxygenationReferenceSex.Female }, manual);
        Check.That(original.Frc == edited.Frc && original.Hemoglobin == edited.Hemoglobin &&
            edited.Hemoglobin.Origin == OxygenationBaselineOrigin.Explicit && original.BasalOxygenDemand != edited.BasalOxygenDemand,
            "demographic edits recompute automatic fields and retain each explicit override");
        Reject(() => OxygenationPatientDefaults.Resolve(profile with { AgeYears = 19 }));
        _ = OxygenationPatientDefaults.Resolve(profile with { AgeYears = 19 }, new(HemoglobinGramsPerDl: 14));
        Reject(() => OxygenationPatientDefaults.Resolve(profile with { AgeYears = 81 }));
        _ = OxygenationPatientDefaults.Resolve(profile with { AgeYears = 81 }, new(FrcMlBtps: 3000));
        Reject(() => OxygenationPatientDefaults.Resolve(profile with { WeightKg = 100 }));
        _ = OxygenationPatientDefaults.Resolve(profile with { WeightKg = 100 }, new(BloodVolumeMl: 5000));
        Reject(() => OxygenationPatientDefaults.Resolve(profile with { Sex = (OxygenationReferenceSex)9 }));
        Reject(() => OxygenationPatientDefaults.Resolve(profile with { AgeYears = 17 }));
        Reject(() => OxygenationPatientDefaults.Resolve(profile, new(FrcMlBtps: 0)));
        Reject(() => OxygenationPatientDefaults.Resolve(profile, new(HemoglobinGramsPerDl: 6, BasalOxygenDemandMlStpdPerMinute: 500)));
        _ = OxygenationPatientDefaults.Resolve(profile, new(BasalOxygenDemandMlStpdPerMinute: 0));
    }

    private static void BaselinePreferencesRetainValuesAndProvenance()
    {
        var profile = OxygenationPatientProfile.Default;
        var overrides = new OxygenationBaselineOverrides(FrcMlBtps: 2500);
        var saved = new OxygenationPatientPreferences(profile, overrides, OxygenationPatientDefaults.Resolve(profile, overrides));
        saved.Validate();
        var restored = JsonSerializer.Deserialize<OxygenationPatientPreferences>(JsonSerializer.Serialize(saved))!;
        restored.Validate();
        Check.That(saved == restored, "reference set, inputs, explicit flags and exact resolved values round-trip");
        Reject(() => (saved with { Resolved = saved.Resolved with { ReferenceSetId = "unknown" } }).Validate());
        Reject(() => (saved with { Profile = profile with { AgeYears = 60 } }).Validate());
        var legacy = new OxygenationEditorPreferences(true, 450000, 150000, 210000, true);
        Check.That(legacy.CreateConfiguration() == RealtimeOxygenationConfiguration.ReferenceAdult,
            "old preferences without patient references retain the original authored baseline and multiplier1");
    }

    private static void TeacherMultiplierChangesDemandWithoutResettingStores()
    {
        var baseline = OxygenationPatientDefaults.Resolve(OxygenationPatientProfile.Default).Parameters;
        var transport = PhysiologyIllustrationSource.CreateTransport(PhysiologyIllustrationConfiguration.Default, new(450000, 150000, 210000), 66667);
        var first = new RealtimeOxygenationSource(transport, baseline);
        var doubled = new RealtimeOxygenationSource(transport, baseline, 2);
        Check.That(first.Snapshot.Reservoirs == doubled.Snapshot.Reservoirs, "scenario multiplier does not redefine basal initial stores");
        first.AdvanceTo(1_000_000_000);
        doubled.AdvanceTo(1_000_000_000);
        Check.That(Math.Abs(doubled.Snapshot.Reservoirs.ConsumedOxygenMl - 2 * first.Snapshot.Reservoirs.ConsumedOxygenMl) < .000000000000001m,
            "2x consumes twice the baseline oxygen while supply is sufficient");
        var before = first.Snapshot;
        var historical = first.ReadAt(800_000_000);
        first.ChangeVentilation(new(450000, 150000, 210000), 1_001_000_000, 3);
        Check.That(first.Snapshot == before && first.ReadAt(800_000_000) == historical, "live multiplier edit preserves stores and delayed history");
        first.AdvanceTo(1_008_000_000);
        Check.That(first.Snapshot.OxygenDemandMultiplier == 1, "the partially elapsed grid interval uses its original demand");
        first.AdvanceTo(1_016_000_000);
        Check.That(first.Snapshot.OxygenDemandMultiplier == 3 && baseline.OxygenDemandMlStpdPerMinute < 250,
            "multiplier takes effect at the next boundary; baseline remains unchanged");
        before = first.Snapshot;
        Reject(() => first.ChangeVentilation(new(0, 150000, 210000), first.SourceSimTimeNs, 5));
        Check.That(first.Snapshot == before, "invalid multiplier rejects ventilation and demand together");
    }

    private static void TeacherMultiplierRetainsDeterministicConservation()
    {
        var baseline = OxygenationPatientDefaults.Resolve(OxygenationPatientProfile.Default).Parameters;
        var transport = PhysiologyIllustrationSource.CreateTransport(PhysiologyIllustrationConfiguration.Default, new(0, 150000, 210000), 66667);
        var large = new RealtimeOxygenationSource(transport, baseline, 4);
        var small = large.Fork();
        for (long time = 250_000_000; time <= 120_000_000_000; time += 250_000_000)
        {
            large.AdvanceTo(time);
            for (int part = 9; part >= 0; part--) { small.AdvanceTo(time - part * 25_000_000); }
            Check.That(large.Snapshot == small.Snapshot, "teacher demand retains fixed-grid determinism");
        }
        Check.That(Math.Abs(large.Snapshot.OxygenBalanceResidualMl) < .000000000000000001m &&
            large.Snapshot.Reservoirs.UnmetOxygenDemandMl > 0 && large.Snapshot.Reservoirs.VenousOxygenMl >= 0,
            "supply-limited consumption records unmet demand without negative stores or lost oxygen");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid or unsupported baseline rejection.");
    }
}
