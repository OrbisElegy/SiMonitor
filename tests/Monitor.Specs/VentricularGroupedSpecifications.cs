// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentricularGroupedSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(GroupedPvcKeepsMorphologyAndCompensation), GroupedPvcKeepsMorphologyAndCompensation),
        new(nameof(GroupedPvcPressureRecoveryAndBudgetsFollowEachBeat), GroupedPvcPressureRecoveryAndBudgetsFollowEachBeat),
    ];
    private static readonly (AvConductionPattern Pattern, int Count)[] Variants =
        [(AvConductionPattern.VentricularBigeminyIllustration, 2), (AvConductionPattern.VentricularTrigeminyIllustration, 3)];

    private static void GroupedPvcKeepsMorphologyAndCompensation()
    {
        foreach (var (pattern, count) in Variants)
        {
            var plan = PrematureVentricularReference.CreatePlan(pattern);
            long duration = count * 800_000_000L;
            long premature = (count - 2) * 800_000_000L + 660_000_000;
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(2 * duration, 100);
            long[] expectedQrs = Enumerable.Range(0, 2).SelectMany(g => Enumerable.Range(0, count).Select(s => g * duration + (s == count - 1 ? premature : s * 800_000_000L + 160_000_000))).ToArray();
            Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(expectedQrs), "one or two sinus beats followed by premature QRS,500ms coupling/1100ms pause");
            var atrial = events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
            Check.That(atrial.Length == 2 * (count - 1) && atrial.All(e => e.CycleIndex % (ulong)count != (ulong)count - 1), "no atrial event in PVC slot and original ordinals retained");
            Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(expectedQrs.Select(t => t + 80_000_000)), "every actual ventricular event has one mechanical event");
            var full = Generate(PrematureVentricularReference.CreateElectrodes(pattern));
            var normal = Generate(TextbookElectrodeReference.CreateElectrodes(timing: PrematureVentricularReference.Timing));
            var wide = Generate(CompleteAvBlockVentricularReference.CreateElectrodes());
            for (int i = 0; i < full.Count; i++)
            {
                long phase = i * 4_000_000L % duration;
                var expected = phase >= premature && phase < premature + 480_000_000 ? wide[i] : normal[i];
                Check.That(full[i].MicrovoltValues.SequenceEqual(expected.MicrovoltValues), "repeated wide beat masks follow the new group size without affecting sinus complexes");
            }
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateLeadIIBands(pattern)).GenerateBefore(2 * duration, 1600, 100);
            Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "grouped monitor II matches projected II");
            IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> bands) => ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(2 * duration, 1600, 100);
        }
    }

    private static void GroupedPvcPressureRecoveryAndBudgetsFollowEachBeat()
    {
        foreach (var (pattern, count) in Variants)
        {
            var plan = PrematureVentricularReference.CreatePlan(pattern);
            long duration = count * 800_000_000L;
            var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000));
            long[] beats = Enumerable.Range(0, 4).SelectMany(g => Enumerable.Range(0, count).Select(s => g * duration + (s == count - 1 ? (count - 2) * 800_000_000L + 740_000_000 : s * 800_000_000L + 240_000_000))).ToArray();
            for (long time = 0; time < 4 * duration; time += 17_000_000)
            {
                double t = Math.Max(0, time - 80_000_000), tau = 2_900_000_000;
                double expected = 1000 + 7000 * Math.Exp(-t / tau);
                foreach (long beat in beats.Where(b => b <= t))
                { expected += 30000 * (1 - Math.Exp(-Math.Min(t - beat, 240_000_000) / tau)) * Math.Exp(-Math.Max(0, t - beat - 240_000_000) / tau); }
                Check.That(Math.Abs((double)pressure.EvaluateAt(time) / FixedPointMath.Q32One - expected) < 0.01, "independent pressure sum retains all ectopic ejections and compensatory gaps");
            }
            var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(2 * duration, 100);
            for (long begin = 0; begin < 2 * duration - 59_000_000; begin += 37_000_000)
            {
                long end = begin + 59_000_000;
                var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
                Check.That(RegularPhysiologyTimeline.Restore(new(plan, begin)).AdvanceBefore(end, Math.Max(1, expected.Length)).SequenceEqual(expected), "exact bounded windows preserve repeated beat indices");
            }
            long qrs = (count - 2) * 800_000_000L + 660_000_000;
            var timeline = RegularPhysiologyTimeline.Restore(new(plan, qrs));
            var saved = timeline.CaptureState();
            try { timeline.AdvanceBefore(qrs + 81_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
            catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == saved, "atomic budget rejection"); }
            foreach (long boundary in new[] { qrs + 88_000_000, qrs + 360_000_000, duration - 4_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(pattern));
                source.GenerateBefore(boundary, 800, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                Check.That(source.GenerateBefore(2 * duration, 1600, 100).Zip(restored.GenerateBefore(2 * duration, 1600, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore wide QRS/T and group wrap with correct mask phase");
            }
            Check.That(RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - duration)).AdvanceBefore(long.MaxValue, 30).Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % (ulong)count == (ulong)count - 1) && pressure.EvaluateAt(long.MaxValue) > 0, "late source and pressure queries stay bounded");
            try { RegularPhysiologyTimeline.Start(plan with { VentricularConductionRatio = 2 }); throw new InvalidOperationException("Conflicting AV ratio accepted."); }
            catch (PhysiologyTimelineException) { }
        }
        try { PrematureVentricularReference.CreatePlan(AvConductionPattern.FixedPr); throw new InvalidOperationException("Non-PVC group accepted."); }
        catch (PhysiologyTimelineException) { }
    }
}
