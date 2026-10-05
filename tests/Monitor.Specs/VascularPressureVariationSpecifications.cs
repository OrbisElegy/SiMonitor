// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VascularPressureVariationSpecifications
{
    private static readonly string SeedA = new('a', 64);
    private static readonly string SeedB = new('b', 64);

    public static Specification[] All =>
    [
        new(nameof(SeededDriftIsSmoothBoundedAndReproducible), SeededDriftIsSmoothBoundedAndReproducible),
        new(nameof(PressureDriftsAroundItsTarget), PressureDriftsAroundItsTarget),
        new(nameof(PressureVariationRejectsUnsupportedInputs), PressureVariationRejectsUnsupportedInputs),
        new(nameof(DriftCheckpointSurvivesJsonWithContinuationHistory), DriftCheckpointSurvivesJsonWithContinuationHistory),
        new(nameof(ReapplyingDriftLeavesEverySampleUnchanged), ReapplyingDriftLeavesEverySampleUnchanged),
        new(nameof(DriftAmplitudeUsesMeasuredPressureResponse), DriftAmplitudeUsesMeasuredPressureResponse),
        new(nameof(StoredDriftDoesNotCreateAbsentEjections), StoredDriftDoesNotCreateAbsentEjections),
    ];

    private static readonly PhysiologyIllustrationConfiguration Drifting = PhysiologyIllustrationConfiguration.Default with
    {
        AbpTarget = new(12000, 8000),
        PaTarget = new(2500, 1000),
        PressureVariation = new(1000, 300, SeedA)
    };

    private static void DriftCheckpointSurvivesJsonWithContinuationHistory()
    {
        var source = PhysiologyIllustrationSource.Create(Drifting);
        for (int i = 1; i <= 81; i++) { source.AdvanceTo(i * 50_000_000L, 50, 2, 100); }
        source.ContinueWith(PhysiologyIllustrationSource.Create(Drifting with { PressureVariation = new(500, 200, SeedB) }));
        source.AdvanceTo(4_100_000_000, 50, 2, 100);
        string json = JsonSerializer.Serialize(source.CaptureState());
        var checkpoint = JsonSerializer.Deserialize<PhysiologyWaveformGroupState>(json)!;
        Check.That(checkpoint.Channels[3].Generator.History.Count == 1, "checkpoint covers old drift and pending acquisition samples");
        var restored = PhysiologyWaveformGroup.Restore(checkpoint);
        for (int i = 83; i <= 200; i++)
        {
            var expected = source.AdvanceTo(i * 50_000_000L, 50, 2, 100);
            var actual = restored.AdvanceTo(i * 50_000_000L, 50, 2, 100);
            Check.That(actual.Count == expected.Count && actual.Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)),
                "JSON restoration reproduces the old and new seeded pressure responses byte for byte");
        }
    }

    private static void ReapplyingDriftLeavesEverySampleUnchanged()
    {
        var source = PhysiologyIllustrationSource.Create(Drifting);
        for (int i = 1; i <= 100; i++) { source.AdvanceTo(i * 200_000_000L, 50, 2, 100); }
        var reapplied = source.Fork();
        reapplied.ContinueWith(PhysiologyIllustrationSource.Create(Drifting));
        Check.That(reapplied.CaptureState().Channels.All(c => c.Generator.History.Count == 0), "equal drift does not split pressure history");
        for (int i = 101; i <= 160; i++)
        {
            var expected = source.AdvanceTo(i * 200_000_000L, 50, 2, 100);
            var actual = reapplied.AdvanceTo(i * 200_000_000L, 50, 2, 100);
            Check.That(actual.Count == expected.Count && actual.Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)),
                "reapplying identical seed, amplitudes and targets preserves every channel sample");
        }
        var changed = PhysiologyIllustrationSource.Create(Drifting with { PressureVariation = new(1000, 300, SeedB) });
        reapplied.ContinueWith(changed);
        Check.That(reapplied.CaptureState().Channels[3].Generator.History.Count == 1, "a different seed still changes the pressure source");
    }

    private static void DriftAmplitudeUsesMeasuredPressureResponse()
    {
        foreach (var configuration in new[]
        {
            Drifting with { PressureVariation = null },
            PhysiologyIllustrationConfiguration.Default with { AbpPulsePermille = 2000, PaPulsePermille = 2000 },
            PhysiologyIllustrationConfiguration.Default with { AbpPulsePermille = 500, PaPulsePermille = 500 }
        })
        {
            int abpAmplitudeCentiMmHg = configuration.AbpPulsePermille == 500 ? 500 : 1000;
            int paAmplitudeCentiMmHg = configuration.PaPulsePermille == 500 ? 150 : 300;
            var reference = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), enableMeasurements: true);
            var varied = new LocalMonitorPreviewSession(configuration with { PressureVariation = new(abpAmplitudeCentiMmHg, paAmplitudeCentiMmHg, SeedA) },
                MonitorDisplayConfiguration.Default(), enableMeasurements: true);
            reference.DiscardStartup();
            varied.DiscardStartup();
            int[] minimum = [0, 0];
            int[] maximum = [0, 0];
            for (int step = 0; step < 90 * 20; step++)
            {
                reference.Advance(50_000_000);
                varied.Advance(50_000_000);
                if (step < 200 || step % 20 != 0) { continue; }
                int[] differences =
                [varied.Measurements!.AbpMean.MeanCentiMmHg!.Value - reference.Measurements!.AbpMean.MeanCentiMmHg!.Value,
                 varied.Measurements.PaMean.MeanCentiMmHg!.Value - reference.Measurements.PaMean.MeanCentiMmHg!.Value];
                for (int channel = 0; channel < 2; channel++)
                {
                    minimum[channel] = Math.Min(minimum[channel], differences[channel]);
                    maximum[channel] = Math.Max(maximum[channel], differences[channel]);
                }
            }
            Check.That(minimum[0] >= -abpAmplitudeCentiMmHg * 11 / 10 && maximum[0] <= abpAmplitudeCentiMmHg * 11 / 10 &&
                minimum[1] >= -paAmplitudeCentiMmHg * 11 / 10 && maximum[1] <= paAmplitudeCentiMmHg * 11 / 10,
                $"measured drift follows the approximate authored amplitude: ABP {minimum[0]}..{maximum[0]}, PA {minimum[1]}..{maximum[1]}");
            Check.That(minimum[0] < -abpAmplitudeCentiMmHg / 2 && maximum[0] > abpAmplitudeCentiMmHg / 2 &&
                minimum[1] < -paAmplitudeCentiMmHg / 2 && maximum[1] > paAmplitudeCentiMmHg / 2,
                "calibration retains visible excursions in both directions instead of suppressing drift");
        }
    }

    private static void StoredDriftDoesNotCreateAbsentEjections()
    {
        foreach (var configuration in new[]
        {
            PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent },
            PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.AtrialOnly },
            PhysiologyIllustrationConfiguration.Default with { VentricularMechanicalEnabled = false },
            PhysiologyIllustrationConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration)
        })
        {
            var original = PhysiologyIllustrationSource.Create(configuration);
            var varied = PhysiologyIllustrationSource.Create(configuration with { PressureVariation = new(1000, 300, SeedA) });
            for (int i = 1; i <= 40; i++)
            {
                var expected = original.AdvanceTo(i * 200_000_000L, 50, 2, 100);
                var actual = varied.AdvanceTo(i * 200_000_000L, 50, 2, 100);
                Check.That(actual.Count == expected.Count && actual.Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)),
                    "stored drift leaves no-ejection pressure runoff unchanged");
            }
        }
    }

    private static void SeededDriftIsSmoothBoundedAndReproducible()
    {
        var drift = new SeededVascularVariation(100, SeedA, "physiology.pressure.arterial");
        var same = new SeededVascularVariation(100, SeedA, "physiology.pressure.arterial");
        var other = new SeededVascularVariation(100, SeedB, "physiology.pressure.arterial");
        long[] times = Enumerable.Range(0, 400).Select(i => i * 2_400_000_000L).ToArray();
        Check.That(drift.GainPermille(0) == 1000 && drift.GainPermille(SeededVascularVariation.KnotPeriodNs * SeededVascularVariation.KnotCount) == 1000,
            "the drift starts and wraps at the level");
        Check.That(times.All(t => drift.GainPermille(t) is >= 900 and <= 1100), "the drift stays within its amplitude");
        Check.That(times.All(t => drift.GainPermille(t) == same.GainPermille(t)) && times.Any(t => drift.GainPermille(t) != other.GainPermille(t)),
            "the same seed reproduces the drift and another seed changes it");
        Check.That(times.Any(t => drift.GainPermille(t) >= 1060) && times.Any(t => drift.GainPermille(t) <= 940),
            "excursions reach most of the amplitude in both directions");
        Check.That(Enumerable.Range(0, 1000).All(i => Math.Abs(drift.GainPermille((i + 1) * 100_000_000L) - drift.GainPermille(i * 100_000_000L)) <= 3),
            "the drift changes smoothly between beats");
    }

    private static void PressureDriftsAroundItsTarget()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        {
            AbpTarget = new(12000, 8000),
            PressureVariation = new(1000, 300, SeedA)
        };
        var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), enableMeasurements: true);
        session.DiscardStartup();
        var diastolic = new List<int>();
        for (int step = 0; step < 180 * 20; step++)
        {
            session.Advance(50_000_000);
            if (step % 100 == 99 && session.Measurements!.AbpMean.Pulse is { DiastolicCentiMmHg: { } value }) { diastolic.Add(value); }
        }
        Check.That(diastolic.Count > 30 && diastolic.Max() - diastolic.Min() >= 600 && Math.Abs(diastolic.Average() - 8000) <= 300,
            "measured diastolic pressure drifts by several mmHg around its target");
        string first = JsonSerializer.Serialize(PhysiologyIllustrationSource.Create(configuration).CaptureState());
        string second = JsonSerializer.Serialize(PhysiologyIllustrationSource.Create(configuration).CaptureState());
        Check.That(first == second, "the same configuration prepares the same drift");
    }

    private static void PressureVariationRejectsUnsupportedInputs()
    {
        static string Reason(PhysiologyIllustrationConfiguration configuration)
        {
            try { _ = PhysiologyIllustrationSource.Create(configuration); }
            catch (ArgumentException exception) { return exception.Message; }
            return "accepted";
        }
        var defaults = PhysiologyIllustrationConfiguration.Default;
        Check.That(Reason(defaults with { PressureVariation = new(2001, 0, SeedA) }) == "Physiology.PressureVariationOutOfRange" &&
            Reason(defaults with { PressureVariation = new(0, 501, SeedA) }) == "Physiology.PressureVariationOutOfRange",
            "amplitudes outside the channel ranges are rejected");
        Check.That(Reason(defaults with { UseVascularReservoir = false, PressureVariation = new(500, 0, SeedA) }) == "Physiology.PressureVariationRequiresReservoir",
            "drift needs the reservoir pressure source");
        Check.That(Reason(defaults with { PressureVariation = new(0, 0, SeedA) }) == "accepted" &&
            Reason(defaults with { PaTarget = new(1500, 1000), PressureVariation = new(0, 500, SeedA) }) == "Physiology.PressureVariationTooLarge",
            "zero amplitudes are inert and a drift too large for a low level is rejected");
        bool invalid = false;
        try { _ = new SeededVascularVariation(401, SeedA, "physiology.pressure.arterial"); }
        catch (ArgumentException) { invalid = true; }
        Check.That(invalid, "the drift factor is bounded");
    }
}
