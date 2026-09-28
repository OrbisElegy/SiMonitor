// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class HyperkalemiaProductSourceSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(RepolarizationSourcePreservesMechanicsAndRecovery), RepolarizationSourcePreservesMechanicsAndRecovery),
        new(nameof(RepolarizationSourceRejectsConflictingCardiacModes), RepolarizationSourceRejectsConflictingCardiacModes),
        new(nameof(ConductionSourceUsesAuthoredClocksAndAtrialActivity), ConductionSourceUsesAuthoredClocksAndAtrialActivity),
        new(nameof(FusionSourcePreservesMechanicalClockAndRejectsOrphans), FusionSourcePreservesMechanicalClockAndRejectsOrphans),
    ];
    private static void FusionSourcePreservesMechanicalClockAndRejectsOrphans()
    {
        var normal = PhysiologyIllustrationConfiguration.Default with
        {
            HyperkalemiaRepolarization = true,
            HyperkalemiaConduction = true,
            HyperkalemiaAbsentP = true,
            CardiacActivity = CardiacActivity.VentricularOnly
        };
        var config = normal with { HyperkalemiaFusion = true };
        Check.That(config.ResolvePlan() == HyperkalemiaFusionReference.CreatePlan(), "fusion does not invent a new electrical or mechanical event grid");
        var source = PhysiologyIllustrationSource.Create(config); var reference = PhysiologyIllustrationSource.Create(normal);
        bool changed = false;
        for (int step = 1; step <= 40; step++)
        {
            var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            var expected = reference.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            Check.That(actual.Count == expected.Count, "fusion preserves all sample frontiers");
            foreach (var pair in actual.Zip(expected))
            {
                var a = WaveformEnvelopeCodec.Decode(pair.First); var b = WaveformEnvelopeCodec.Decode(pair.Second);
                foreach (var plane in a.Planes)
                {
                    bool equal = plane.Samples.SequenceEqual(b.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                    if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                    else { Check.That(equal, "compound ECG does not invent pump failure or alter other channels"); }
                }
            }
            if (step == 17) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
        }
        Check.That(changed, "fusion replaces acquired ECG contour");
        foreach (var invalid in new[] { config with { HyperkalemiaRepolarization = false },
            config with { HyperkalemiaConduction = false }, config with { HyperkalemiaAbsentP = false },
            config with { CardiacActivity = CardiacActivity.AtrialAndVentricular },
            config with { DigitalisEffect = true }, config with { HypokalemiaRepolarization = true } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "orphan or incompatible fusion flags reject");
        }
        _ = PhysiologyIllustrationSource.Create(config with { VentricularMechanicalEnabled = false });
    }

    private static void ConductionSourceUsesAuthoredClocksAndAtrialActivity()
    {
        var config = PhysiologyIllustrationConfiguration.Default with { HyperkalemiaRepolarization = true, HyperkalemiaConduction = true };
        var withoutP = config with { HyperkalemiaAbsentP = true, CardiacActivity = CardiacActivity.VentricularOnly };
        Check.That(config.ResolvePlan() == HyperkalemiaConductionReference.CreatePlan() &&
            withoutP.ResolvePlan() == HyperkalemiaConductionReference.CreatePlan(true), "conduction clocks use RR1000, PR240 and ejection320ms");
        var source = PhysiologyIllustrationSource.Create(config); var absent = PhysiologyIllustrationSource.Create(withoutP);
        bool ecgChanged = false, cvpChanged = false;
        for (int step = 1; step <= 40; step++)
        {
            var a = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            var b = absent.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            Check.That(a.Count == b.Count, "atrial suppression preserves sample frontier");
            foreach (var pair in a.Zip(b))
            {
                var left = WaveformEnvelopeCodec.Decode(pair.First); var right = WaveformEnvelopeCodec.Decode(pair.Second);
                for (int channel = 0; channel < 7; channel++)
                {
                    var id = PhysiologyIllustrationSource.ChannelId(channel);
                    bool equal = left.Planes.Single(p => p.ChannelId == id).Samples.SequenceEqual(right.Planes.Single(p => p.ChannelId == id).Samples);
                    if (channel == 0) { ecgChanged |= !equal; }
                    else if (channel == 6) { cvpChanged |= !equal; }
                    else { Check.That(equal, "absent P does not suppress ventricular perfusion or independent respiration"); }
                }
            }
            if (step == 17)
            {
                source = PhysiologyWaveformGroup.Restore(source.CaptureState());
                absent = PhysiologyWaveformGroup.Restore(absent.CaptureState());
            }
        }
        Check.That(ecgChanged && cvpChanged, "absent P removes both electrical P and CVP atrial contribution");
        foreach (var invalid in new[] { config with { HyperkalemiaRepolarization = false },
            withoutP with { HyperkalemiaConduction = false }, withoutP with { CardiacActivity = CardiacActivity.AtrialAndVentricular },
            config with { CardiacActivity = CardiacActivity.VentricularOnly } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "orphan or inconsistent conduction/atrial flags reject");
        }
    }

    private static void RepolarizationSourcePreservesMechanicsAndRecovery()
    {
        var config = PhysiologyIllustrationConfiguration.Default with { HyperkalemiaRepolarization = true };
        Check.That(config.ResolvePlan() == PhysiologyIllustrationConfiguration.Default.ResolvePlan(), "repolarization changes no electrical/mechanical event schedule");
        var source = PhysiologyIllustrationSource.Create(config);
        var normal = PhysiologyIllustrationSource.Create();
        bool changed = false;
        for (int step = 1; step <= 40; step++)
        {
            var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            var expected = normal.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            Check.That(actual.Count == expected.Count, "all channel frontiers are preserved");
            foreach (var pair in actual.Zip(expected))
            {
                var a = WaveformEnvelopeCodec.Decode(pair.First); var b = WaveformEnvelopeCodec.Decode(pair.Second);
                foreach (var plane in a.Planes)
                {
                    bool equal = plane.Samples.SequenceEqual(b.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                    if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                    else { Check.That(equal, "repolarization preserves all six non-ECG sampled channels"); }
                }
            }
            if (step is 12 or 19 or 31)
            {
                var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
                var a = source.AdvanceTo(step * 200_000_000L + 50_000_000, 50, 1, 100);
                var b = restored.AdvanceTo(step * 200_000_000L + 50_000_000, 50, 1, 100);
                Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.SequenceEqual(p.Second)), "mid-cycle restoration retains exact acquired wire output");
                source = restored;
            }
        }
        Check.That(changed, "new source actually changes acquired ECG");
    }
    private static void RepolarizationSourceRejectsConflictingCardiacModes()
    {
        var config = PhysiologyIllustrationConfiguration.Default with { HyperkalemiaRepolarization = true };
        foreach (var invalid in new[] {
            config with { Wpw = true }, config with { Svt = true }, config with { CardiacActivity = CardiacActivity.Absent },
            config with { ConductionPattern = AvConductionPattern.AtrialFibrillationCoarseIllustration },
            config with { BundleBlock = EcgBundleBlockIllustration.CompleteRight },
            config with { IndependentVentricularPeriodMilliseconds = 1200 },
            config with { VentricularConductionRatio = 2 },
            config with { SeededRate = new SeededCardiacRate(75, new string('0', 64), 0) } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "unsupported repolarization composition is rejected at construction");
        }
        _ = PhysiologyIllustrationSource.Create(config with { VentricularMechanicalEnabled = false });
        _ = PhysiologyIllustrationSource.Create(config with { RespiratoryActivity = RespiratoryActivity.Absent });
    }
}
