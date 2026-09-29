// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class InferiorInfarctionProductSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(InferiorSnapshotsPreserveQrsDetectionAndNonEcgChannels), InferiorSnapshotsPreserveQrsDetectionAndNonEcgChannels),
        new(nameof(InferiorSnapshotsMatchProjectionAndRejectConflicts), InferiorSnapshotsMatchProjectionAndRejectConflicts),
    ];
    private static IEnumerable<EcgChestInfarctionPlan> Snapshots() =>
        Enum.GetValues<InfarctionIllustrationStage>().Where(stage => stage != InfarctionIllustrationStage.None)
            .Select(stage => new EcgChestInfarctionPlan(0, stage, InfarctionTerritory.Inferior));

    private static void InferiorSnapshotsPreserveQrsDetectionAndNonEcgChannels()
    {
        foreach (var shape in Snapshots())
        {
            var config = PhysiologyIllustrationConfiguration.Default with { Infarction = shape };
            Check.That(config.ResolvePlan() == PhysiologyIllustrationConfiguration.Default.ResolvePlan(), "QRS shape does not shift atrial/ventricular event clocks");
            var source = PhysiologyIllustrationSource.Create(config); var reference = PhysiologyIllustrationSource.Create();
            var detector = new EcgHeartRateMeasurement(PhysiologyIllustrationSource.ChannelId(0));
            EcgHeartRateMeasurement? restored = null;
            int count = 0; bool changed = false;
            for (int step = 1; step <= 110; step++)
            {
                var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                var expected = reference.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                Check.That(actual.Count == expected.Count, "inferior snapshot retains sample frontiers");
                foreach (var pair in actual.Zip(expected))
                {
                    var a = WaveformEnvelopeCodec.Decode(pair.First); var b = WaveformEnvelopeCodec.Decode(pair.Second);
                    foreach (var plane in a.Planes)
                    {
                        bool equal = plane.Samples.SequenceEqual(b.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                        if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                        else { Check.That(equal, "inferior snapshot does not invent altered mechanics or respiration"); }
                    }
                    var events = detector.Consume(pair.First); count += events.Count;
                    Check.That(events.All(e => e.PeakTimeNs % 800_000_000 >= 160_000_000 &&
                        e.PeakTimeNs % 800_000_000 < 260_000_000), "detected peaks stay inside QRS, not secondary T");
                    long end = a.StartSimTimeNs + 196_000_000; var reading = detector.Read(end);
                    if (restored is not null)
                    { Check.That(events.SequenceEqual(restored.Consume(pair.First)) && reading == restored.Read(end), "QRS candidate restores exactly"); }
                    restored = EcgHeartRateMeasurement.Restore(detector.Capture());
                    if (end > 3_000_000_000)
                    { Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.MilliBeatsPerMinute == 75000, "inferior snapshot preserves75bpm sample rate"); }
                }
                if (step == 19) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
            }
            Check.That(changed && count == 25, "actual ECG changes while all QRS remain detected");
        }
    }
    private static void InferiorSnapshotsMatchProjectionAndRejectConflicts()
    {
        var plan = PhysiologyIllustrationConfiguration.Default.ResolvePlan();
        var baseline = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        foreach (var shape in Snapshots())
        {
            var projected = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(infarction: shape)).GenerateBefore(800_000_000, 200, 100);
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                shape.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
            Check.That(projected.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[(int)EcgLead.II] - p.Second.NormalizedValue) <= 1), "monitor II is LL minus RA of the paper electrodes");
            Check.That(projected.Zip(baseline).All(pair => Enumerable.Range((int)EcgLead.V1, 6)
                .All(lead => Math.Abs(pair.First.MicrovoltValues[lead] - pair.Second.MicrovoltValues[lead]) <= 1)),
                "inferior limb projection preserves all six chest leads");
        }
        var config = PhysiologyIllustrationConfiguration.Default with { Infarction = Snapshots().First() };
        foreach (var invalid in new[] { config with { Infarction = Snapshots().First() with { Stage = (InfarctionIllustrationStage)99 } },
            config with { Infarction = Snapshots().First() with { Territory = (InfarctionTerritory)99 } },
            config with { TContour = new(1, EcgTContourShape.Notched, 300) },
            config with { VentricularShape = EcgVentricularIllustration.LeftHypertrophyWithStrain },
            config with { DigitalisEffect = true }, config with { HypokalemiaRepolarization = true },
            config with { Wpw = true }, config with { AtrialShape = EcgAtrialIllustration.LeftAtrialAbnormality },
            config with { SeededRate = new SeededCardiacRate(75, new string('0', 64), 0) } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "incompatible inferior snapshot combination rejects before construction");
        }
        _ = PhysiologyIllustrationSource.Create(config with { VentricularMechanicalEnabled = false });
    }
}
