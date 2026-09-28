// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class HypokalemiaProductSourceSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(LowPotassiumVariantsPreserveMechanicsAndMeasuredRate), LowPotassiumVariantsPreserveMechanicsAndMeasuredRate),
        new(nameof(LowPotassiumVariantsRejectConflictingModes), LowPotassiumVariantsRejectConflictingModes),
    ];
    private static void LowPotassiumVariantsPreserveMechanicsAndMeasuredRate()
    {
        var basic = PhysiologyIllustrationConfiguration.Default with { HypokalemiaRepolarization = true };
        foreach (var config in new[] { basic, basic with { HypokalemiaInvertedT = true },
            basic with { HypokalemiaTuFusion = true }, basic with { HypokalemiaConduction = true } })
        {
            Check.That(config.ResolvePlan() == HypokalemiaRepolarizationReference.CreatePlan(), "all low-potassium variants share existing60bpm event grid");
            var source = PhysiologyIllustrationSource.Create(config); var reference = PhysiologyIllustrationSource.Create(basic);
            var detector = new EcgHeartRateMeasurement(PhysiologyIllustrationSource.ChannelId(0));
            EcgHeartRateMeasurement? restored = null;
            var beats = new List<DetectedEcgBeat>(); bool changed = false;
            for (int step = 1; step <= 110; step++)
            {
                var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                var expected = reference.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                Check.That(actual.Count == expected.Count, "variant sample frontiers match");
                foreach (var pair in actual.Zip(expected))
                {
                    var a = WaveformEnvelopeCodec.Decode(pair.First); var b = WaveformEnvelopeCodec.Decode(pair.Second);
                    foreach (var plane in a.Planes)
                    {
                        bool equal = plane.Samples.SequenceEqual(b.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                        if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                        else { Check.That(equal, "T/U/P/QRS morphology does not invent a mechanical or respiratory change"); }
                    }
                    var events = detector.Consume(pair.First); beats.AddRange(events);
                    long end = a.StartSimTimeNs + 196_000_000;
                    var reading = detector.Read(end);
                    if (restored is not null)
                    { Check.That(events.SequenceEqual(restored.Consume(pair.First)) && reading == restored.Read(end), "U-wave candidate handling restores exactly"); }
                    restored = EcgHeartRateMeasurement.Restore(detector.Capture());
                    if (end > 3_000_000_000)
                    { Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.MilliBeatsPerMinute == 60000, "prominent U or P does not become an extra QRS: " + reading); }
                }
                if (step == 19) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
            }
            Check.That(changed == (config != basic), "variants change actual acquired ECG");
            Check.That(beats.Count >= 18 && beats.Zip(beats.Skip(1)).All(p => p.Second.PeakTimeNs - p.First.PeakTimeNs == 1_000_000_000), "one detection per QRS");
        }
    }
    private static void LowPotassiumVariantsRejectConflictingModes()
    {
        var config = PhysiologyIllustrationConfiguration.Default with { HypokalemiaRepolarization = true };
        foreach (var invalid in new[] { config with { HypokalemiaRepolarization = false, HypokalemiaTuFusion = true },
            config with { HypokalemiaRepolarization = false, HypokalemiaConduction = true },
            config with { HypokalemiaRepolarization = false, HypokalemiaInvertedT = true },
            config with { HyperkalemiaRepolarization = true }, config with { HyperkalemiaConduction = true },
            config with { Wpw = true }, config with { CardiacActivity = CardiacActivity.VentricularOnly },
            config with { SeededRate = new SeededCardiacRate(60, new string('0', 64), 0) } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "orphan or incompatible electrolyte modes reject at construction");
        }
        _ = PhysiologyIllustrationSource.Create(config with { VentricularMechanicalEnabled = false });
    }
}
