// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class QuinidineProductSourceSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(QuinidineVariantsPreserveMechanicsAndMeasuredRate), QuinidineVariantsPreserveMechanicsAndMeasuredRate),
        new(nameof(QuinidineVariantsRejectConflictingModes), QuinidineVariantsRejectConflictingModes),
    ];
    private static void QuinidineVariantsPreserveMechanicsAndMeasuredRate()
    {
        var basic = PhysiologyIllustrationConfiguration.Default with { Quinidine = QuinidineIllustration.LowT };
        foreach (var mode in Enum.GetValues<QuinidineIllustration>().Where(mode => mode != QuinidineIllustration.Reference))
            foreach (bool notched in new[] { false, true })
            {
                var config = basic with { Quinidine = mode, QuinidineNotchedP = notched };
                Check.That(config.ResolvePlan() == QuinidineEffectReference.CreatePlan(mode), "quinidine variants share existing60bpm event grid");
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
                            else { Check.That(equal, "Quinidine morphology does not invent a mechanical or respiratory change"); }
                        }
                        var events = detector.Consume(pair.First); beats.AddRange(events);
                        long end = a.StartSimTimeNs + 196_000_000;
                        var reading = detector.Read(end);
                        if (restored is not null)
                        { Check.That(events.SequenceEqual(restored.Consume(pair.First)) && reading == restored.Read(end), "Repolarization candidate handling restores exactly"); }
                        restored = EcgHeartRateMeasurement.Restore(detector.Capture());
                        if (end > 3_000_000_000)
                        { Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.MilliBeatsPerMinute == 60000, "short or prolonged repolarization does not become an extra QRS: " + reading); }
                    }
                    if (step == 19) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
                }
                Check.That(changed == (config != basic), "variants change actual acquired ECG");
                Check.That(beats.Count >= 18 && beats.Zip(beats.Skip(1)).All(p => p.Second.PeakTimeNs - p.First.PeakTimeNs == 1_000_000_000), "one detection per QRS");
            }
    }
    private static void QuinidineVariantsRejectConflictingModes()
    {
        var config = PhysiologyIllustrationConfiguration.Default with { Quinidine = QuinidineIllustration.LowT };
        foreach (var invalid in new[] { config with { Quinidine = (QuinidineIllustration)99 },
            config with { Quinidine = QuinidineIllustration.Reference, QuinidineNotchedP = true },
            config with { DigitalisEffect = true }, config with { HypokalemiaRepolarization = true },
            config with { HyperkalemiaRepolarization = true }, config with { Calcium = CalciumIllustration.High },
            config with { Wpw = true }, config with { CardiacActivity = CardiacActivity.VentricularOnly },
            config with { SeededRate = new SeededCardiacRate(60, new string('0', 64), 0) } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "orphan or incompatible quinidine modes reject at construction");
        }
        _ = PhysiologyIllustrationSource.Create(config with { VentricularMechanicalEnabled = false });
    }
}
