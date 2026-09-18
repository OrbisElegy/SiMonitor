// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RetrogradeJunctionalSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(RetrogradePositionChangesOnlyAtrialTimingAndSuperposition), RetrogradePositionChangesOnlyAtrialTimingAndSuperposition),
        new(nameof(RetrogradeVariantsRestoreAndQueryWithOriginalOrdinals), RetrogradeVariantsRestoreAndQueryWithOriginalOrdinals),
    ];
    private static readonly AvConductionPattern[] Patterns = [AvConductionPattern.PrematureJunctionalIllustration,
        AvConductionPattern.PrematureJunctionalAfterQrsIllustration, AvConductionPattern.PrematureJunctionalOverlappingIllustration];

    private static void RetrogradePositionChangesOnlyAtrialTimingAndSuperposition()
    {
        var before = PrematureJunctionalReference.CreatePlan();
        var referenceEvents = RegularPhysiologyTimeline.Start(before).AdvanceBefore(6_400_000_000, 100);
        var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var referencePressure = VascularPressureSource.Create(before, pressurePlan);
        foreach (var (pattern, start) in new[] { (Patterns[1], 2_380_000_000L), (Patterns[2], 2_260_000_000L) })
        {
            var plan = PrematureJunctionalReference.CreatePlan(pattern);
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
            Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.RetrogradeAtrialElectrical).Select(e => e.SimTimeNs).SequenceEqual(new[] { start, start + 3_200_000_000 }), "post-QRS or overlapping retrograde atrial event retains original slot");
            Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialMechanical && e.CycleIndex % 4 == 3).Select(e => e.SimTimeNs).SequenceEqual(new[] { start + 80_000_000, start + 3_280_000_000 }), "atrial mechanics follows shifted P-prime");
            Check.That(events.Where(Unchanged).SequenceEqual(referenceEvents.Where(Unchanged)), "sinus, ventricular and respiratory events unchanged across variants");
            var pressure = VascularPressureSource.Create(plan, pressurePlan);
            for (long time = 0; time < 6_400_000_000; time += 13_000_000)
            { Check.That(pressure.EvaluateAt(time) == referencePressure.EvaluateAt(time), "atrial timing does not invent a ventricular pressure change"); }
            var electrodes = PrematureJunctionalReference.CreateElectrodes();
            var full = Generate(electrodes);
            var noP = Generate(electrodes.Select(e => e with { Bands = e.Bands.Where(b => b.Trigger != PhysiologyCycleEventKind.RetrogradeAtrialElectrical).ToArray() }).ToArray());
            var onlyP = Generate(electrodes.Select(e => e with { Bands = e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.RetrogradeAtrialElectrical).ToArray() }).ToArray());
            for (int i = 0; i < 1600; i++)
            {
                Check.That(Enumerable.Range(0, 12).All(lead => Math.Abs(full[i].MicrovoltValues[lead] - noP[i].MicrovoltValues[lead] - onlyP[i].MicrovoltValues[lead]) <= 2), "retrograde P superposes without deleting QRS/ST/T");
                long phase = i * 4_000_000L % 3_200_000_000;
                if (phase < start || phase >= start + 80_000_000)
                { Check.That(full[i].MicrovoltValues.SequenceEqual(noP[i].MicrovoltValues), "no changes outside retrograde P support"); }
            }
            int peak = Enumerable.Range((int)(start / 4_000_000), 20).MinBy(i => onlyP[i].MicrovoltValues[1]);
            Check.That(onlyP[peak].MicrovoltValues[1] < -150 && onlyP[peak].MicrovoltValues[3] > 100, "both variants retain retrograde inferior/aVR polarity even when hidden by QRS");
            if (pattern == Patterns[2])
            { Check.That(Enumerable.Range(565, 20).Any(i => Math.Abs(noP[i].MicrovoltValues[1]) > 200 && Math.Abs(onlyP[i].MicrovoltValues[1]) > 100), "overlap truly shares the ventricular depolarization interval"); }
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureJunctionalReference.CreateLeadIIBands()).GenerateBefore(6_400_000_000, 1600, 100);
            Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II and projected II share overlapping voltage");
            IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> bands) =>
                ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(6_400_000_000, 1600, 100);
        }
        static bool Unchanged(PhysiologyCycleEvent e) => e.Kind != PhysiologyCycleEventKind.RetrogradeAtrialElectrical && !(e.Kind == PhysiologyCycleEventKind.AtrialMechanical && e.CycleIndex % 4 == 3);
    }

    private static void RetrogradeVariantsRestoreAndQueryWithOriginalOrdinals()
    {
        foreach (var pattern in Patterns.Skip(1))
        {
            var plan = PrematureJunctionalReference.CreatePlan(pattern);
            var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
            for (long begin = 0; begin < 6_300_000_000; begin += 37_000_000)
            {
                long end = begin + 59_000_000;
                var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
                Check.That(RegularPhysiologyTimeline.Restore(new(plan, begin)).AdvanceBefore(end, Math.Max(1, expected.Length)).SequenceEqual(expected), "half-open query keeps simultaneous events and original ordinal");
            }
            long start = PrematureJunctionalReference.RetrogradeOffsetNs(pattern);
            var timeline = RegularPhysiologyTimeline.Restore(new(plan, 2_200_000_000));
            var saved = timeline.CaptureState();
            try { timeline.AdvanceBefore(2_500_000_000, 1); throw new InvalidOperationException("Insufficient event budget accepted."); }
            catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == saved, "atomic rejection through retrograde event and ventricular mechanics"); }
            foreach (long boundary in new[] { start, start + 40_000_000, start + 80_000_000, 3_196_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureJunctionalReference.CreateElectrodes());
                source.GenerateBefore(boundary, 800, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                Check.That(source.GenerateBefore(6_400_000_000, 1600, 100).Zip(restored.GenerateBefore(6_400_000_000, 1600, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore inside P/QRS/ST/T superposition");
            }
            Check.That(RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 3_200_000_000)).AdvanceBefore(long.MaxValue, 30).Any(e => e.Kind == PhysiologyCycleEventKind.RetrogradeAtrialElectrical && e.CycleIndex % 4 == 3), "late query retains retrograde beat identity without epoch replay");
            try { RegularPhysiologyTimeline.Start(plan with { IndependentVentricularPeriodNs = 1_200_000_000 }); throw new InvalidOperationException("Conflicting independent clock accepted."); }
            catch (PhysiologyTimelineException) { }
        }
        try { PrematureJunctionalReference.CreatePlan(AvConductionPattern.FixedPr); throw new InvalidOperationException("Non-junctional pattern accepted."); }
        catch (PhysiologyTimelineException) { }
    }
}
