// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class TContourProductSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(TContoursPreserveQrsDetectionAndNonEcgChannels), TContoursPreserveQrsDetectionAndNonEcgChannels),
        new(nameof(TContoursMatchProjectionAndRejectConflicts), TContoursMatchProjectionAndRejectConflicts),
    ];
    private static IEnumerable<EcgTContourPlan> Contours()
    {
        foreach (var shape in Enum.GetValues<EcgTContourShape>())
        {
            bool biphasic = shape is EcgTContourShape.PositiveNegative or EcgTContourShape.NegativePositive;
            yield return new(1, shape, shape is EcgTContourShape.PeakedUpright or EcgTContourShape.BroadUpright ? 600 :
                shape == EcgTContourShape.ReferenceUpright ? 80 : 300, EcgTContourTarget.II,
                biphasic ? (shape == EcgTContourShape.PositiveNegative ? 350 : 650) : null, biphasic ? 200 : null);
        }
    }
    private static void TContoursPreserveQrsDetectionAndNonEcgChannels()
    {
        var reference = PhysiologyIllustrationSource.Create();
        var referenceBlocks = Enumerable.Range(1, 110)
            .Select(step => reference.AdvanceTo(step * 200_000_000L, 50, 1, 100)).ToArray();
        foreach (var shape in Contours())
        {
            var config = PhysiologyIllustrationConfiguration.Default with { TContour = shape };
            Check.That(config.ResolvePlan() == PhysiologyIllustrationConfiguration.Default.ResolvePlan(), "T contour does not shift atrial/ventricular event clocks");
            var source = PhysiologyIllustrationSource.Create(config);
            var detector = new EcgHeartRateMeasurement(PhysiologyIllustrationSource.ChannelId(0));
            var normal = new EcgHeartRateMeasurement(PhysiologyIllustrationSource.ChannelId(0));
            EcgHeartRateMeasurement? restored = null;
            int count = 0; bool changed = false;
            for (int step = 1; step <= 110; step++)
            {
                var actual = source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                var expected = referenceBlocks[step - 1];
                Check.That(actual.Count == expected.Count, "T contour retains sample frontiers");
                foreach (var pair in actual.Zip(expected))
                {
                    var a = WaveformEnvelopeCodec.Decode(pair.First); var b = WaveformEnvelopeCodec.Decode(pair.Second);
                    foreach (var plane in a.Planes)
                    {
                        bool equal = plane.Samples.SequenceEqual(b.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples);
                        if (plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)) { changed |= !equal; }
                        else { Check.That(equal, "T contour does not invent altered mechanics or respiration"); }
                    }
                    var events = detector.Consume(pair.First); count += events.Count;
                    Check.That(events.SequenceEqual(normal.Consume(pair.Second)), "confirm actual QRS at the same times as reference, never T peaks");
                    long end = a.StartSimTimeNs + 196_000_000; var reading = detector.Read(end);
                    if (restored is not null)
                    { Check.That(events.SequenceEqual(restored.Consume(pair.First)) && reading == restored.Read(end), "QRS candidate restores exactly"); }
                    restored = EcgHeartRateMeasurement.Restore(detector.Capture());
                    if (end > 3_000_000_000)
                    { Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.MilliBeatsPerMinute == 75000, "T contour preserves75bpm sample rate"); }
                }
                if (step == 19) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
            }
            Check.That(changed && count == 25, "actual ECG changes while all QRS remain detected");
        }
    }
    private static void TContoursMatchProjectionAndRejectConflicts()
    {
        var plan = PhysiologyIllustrationConfiguration.Default.ResolvePlan();
        foreach (var shape in Contours())
        {
            var projected = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(tContour: shape)).GenerateBefore(800_000_000, 200, 100);
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                shape.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
            Check.That(projected.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[(int)EcgLead.II] - p.Second.NormalizedValue) <= 1), "monitor II is LL minus RA of the paper electrodes");
        }
        var config = PhysiologyIllustrationConfiguration.Default with { TContour = Contours().First() };
        foreach (var invalid in new[] { config with { TContour = Contours().First() with { Shape = (EcgTContourShape)99 } },
            config with { TContour = Contours().First() with { CrossingPositionPermille = 0 } },
            config with { VentricularShape = EcgVentricularIllustration.LeftHypertrophyWithStrain },
            config with { AtrialShape = EcgAtrialIllustration.LeftAtrialAbnormality },
            config with { DigitalisEffect = true }, config with { HypokalemiaRepolarization = true },
            config with { Wpw = true }, config with { CardiacActivity = CardiacActivity.VentricularOnly },
            config with { SeededRate = new SeededCardiacRate(75, new string('0', 64), 0) } })
        {
            bool rejected = false;
            try { PhysiologyIllustrationSource.Create(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "incompatible T contour combination rejects before construction");
        }
        _ = PhysiologyIllustrationSource.Create(config with { VentricularMechanicalEnabled = false });
    }
}
