// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentricularShapeProductSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(VentricularShapesPreserveQrsDetectionAndNonEcgChannels), VentricularShapesPreserveQrsDetectionAndNonEcgChannels),
        new(nameof(VentricularShapesMatchProjectionAndRejectConflicts), VentricularShapesMatchProjectionAndRejectConflicts),
    ];
    private static void VentricularShapesPreserveQrsDetectionAndNonEcgChannels()
    {
        foreach (var shape in Enum.GetValues<EcgVentricularIllustration>().Where(s => s != EcgVentricularIllustration.Reference))
        {
            var config = PhysiologyIllustrationConfiguration.Default with { VentricularShape = shape };
            Check.That(config.ResolvePlan() == PhysiologyIllustrationConfiguration.Default.ResolvePlan(), "QRS shape does not shift atrial/ventricular event clocks");
            var source = PhysiologyIllustrationSource.Create(config); var reference = PhysiologyIllustrationSource.Create();
            var detector = new EcgHeartRateMeasurement(PhysiologyIllustrationSource.ChannelId(0));
            EcgHeartRateMeasurement? restored = null;
            int count = 0; bool changed = false;
            for (int step = 1; step <= 110; step++)
            {
                var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                var expected = reference.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                Check.That(actual.Count == expected.Count, "ventricular morphology retains sample frontiers");
                foreach (var pair in actual.Zip(expected))
                {
                    var a = WaveformEnvelopeCodec.Decode(pair.First); var b = WaveformEnvelopeCodec.Decode(pair.Second);
                    foreach (var plane in a.Planes)
                    {
                        bool equal = plane.Samples.SequenceEqual(b.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                        if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                        else { Check.That(equal, "electrical ventricular shape does not invent altered mechanics or respiration"); }
                    }
                    var events = detector.Consume(pair.First); count += events.Count;
                    Check.That(events.All(e => e.PeakTimeNs % 800_000_000 >= 160_000_000 &&
                        e.PeakTimeNs % 800_000_000 < 260_000_000), "detected peaks stay inside QRS, not secondary T");
                    long end = a.StartSimTimeNs + 196_000_000; var reading = detector.Read(end);
                    if (restored is not null)
                    { Check.That(events.SequenceEqual(restored.Consume(pair.First)) && reading == restored.Read(end), "QRS candidate restores exactly"); }
                    restored = EcgHeartRateMeasurement.Restore(detector.Capture());
                    if (end > 3_000_000_000)
                    { Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.MilliBeatsPerMinute == 75000, "ventricular shape preserves the measured 75 bpm heart rate"); }
                }
                if (step == 19) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
            }
            Check.That(changed && count == 25, "actual ECG changes while all QRS remain detected");
        }
    }
    private static void VentricularShapesMatchProjectionAndRejectConflicts()
    {
        var plan = PhysiologyIllustrationConfiguration.Default.ResolvePlan();
        foreach (var shape in Enum.GetValues<EcgVentricularIllustration>())
        {
            var projected = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(ventricular: shape)).GenerateBefore(800_000_000, 200, 100);
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                EcgVentricularIllustrations.CreateLeadIIBands(shape)).GenerateBefore(800_000_000, 200, 100);
            EcgProjectionChecks.RequireMatchingLeadII(projected, monitor, 200, $"ventricular shape {shape}");
        }
        var config = PhysiologyIllustrationConfiguration.Default with { VentricularShape = EcgVentricularIllustration.BiventricularCombinedSigns };
        foreach (var invalid in new[] { config with { VentricularShape = (EcgVentricularIllustration)99 },
            config with { DigitalisEffect = true }, config with { HypokalemiaRepolarization = true },
            config with { Wpw = true }, config with { AtrialShape = EcgAtrialIllustration.LeftAtrialAbnormality },
            config with { SeededRate = new SeededCardiacRate(75, new string('0', 64), 0) } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "incompatible ventricular shape combination rejects before construction");
        }
        _ = PhysiologyIllustrationSource.Create(config with { VentricularMechanicalEnabled = false });
    }
}
