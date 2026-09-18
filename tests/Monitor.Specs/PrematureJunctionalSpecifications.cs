// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PrematureJunctionalSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PjcHasRetrogradePShortPrAndCompleteCompensation), PjcHasRetrogradePShortPrAndCompleteCompensation),
        new(nameof(PjcPressureAndRecoveryUseActualMechanicalTimes), PjcPressureAndRecoveryUseActualMechanicalTimes),
        new(nameof(PjcBoundedQueriesAndRejectionAreAtomic), PjcBoundedQueriesAndRejectionAreAtomic),
    ];

    private static void PjcHasRetrogradePShortPrAndCompleteCompensation()
    {
        var plan = PrematureJunctionalReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
        long[] p = [0, 800_000_000, 1_600_000_000, 3_200_000_000, 4_000_000_000, 4_800_000_000];
        long[] qrs = [160_000_000, 960_000_000, 1_760_000_000, 2_260_000_000, 3_360_000_000, 4_160_000_000, 4_960_000_000, 5_460_000_000];
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).Select(e => e.SimTimeNs).SequenceEqual(p), "no sinus P before ectopic QRS or at the omitted sinus slot");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.RetrogradeAtrialElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 2_180_000_000, 5_380_000_000 }), "P-prime precedes premature QRS by80ms rather than PAC PR160");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(qrs), "500ms coupling plus1100ms pause equals two800ms RR");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(qrs.Select(t => t + 80_000_000)), "mechanical events follow actual ventricular activation");
        Check.That(events.Any(e => e.Kind == PhysiologyCycleEventKind.AtrialMechanical && e.SimTimeNs == 2_260_000_000), "retrograde atrial mechanics follows P-prime, not the old PAC atrial slot");
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureJunctionalReference.CreateElectrodes()).GenerateBefore(6_400_000_000, 1600, 100);
        for (int lead = 0; lead < 12; lead++)
        {
            Check.That(Enumerable.Range(0, 80).All(i => full[565 + i].MicrovoltValues[lead] == full[40 + i].MicrovoltValues[lead]), "ectopic QRS/T has the same shape and duration as sinus QRS/T in every lead");
        }
        Check.That(full.Skip(525).Take(20).All(s => s.MicrovoltValues.All(v => v == 0)), "no early PAC P-prime at2100ms");
        foreach (int lead in new[] { 1, 2, 5 })
        { Check.That(full.Skip(545).Take(20).Min(s => s.MicrovoltValues[lead]) < -100, "retrograde inferior P-prime is negative"); }
        Check.That(full.Skip(545).Take(20).Max(s => s.MicrovoltValues[3]) > 100, "retrograde aVR P-prime positive");
        Check.That(full.Skip(645).Take(155).All(s => s.MicrovoltValues.All(v => v == 0)), "no hidden sinus beat until3200ms");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureJunctionalReference.CreateLeadIIBands()).GenerateBefore(6_400_000_000, 1600, 100);
        Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "12lead and monitor II share retrograde P and ventricular shapes");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity retained");
    }

    private static void PjcPressureAndRecoveryUseActualMechanicalTimes()
    {
        var plan = PrematureJunctionalReference.CreatePlan();
        var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000));
        long[] beats = [240_000_000, 1_040_000_000, 1_840_000_000, 2_340_000_000, 3_440_000_000, 4_240_000_000, 5_040_000_000, 5_540_000_000];
        for (long time = 0; time < 6_400_000_000; time += 13_000_000)
        {
            double t = Math.Max(0, time - 80_000_000), tau = 2_900_000_000;
            double expected = 1000 + 7000 * Math.Exp(-t / tau);
            foreach (long beat in beats.Where(b => b <= t))
            { expected += 30000 * (1 - Math.Exp(-Math.Min(t - beat, 240_000_000) / tau)) * Math.Exp(-Math.Max(0, t - beat - 240_000_000) / tau); }
            Check.That(Math.Abs((double)pressure.EvaluateAt(time) / FixedPointMath.Q32One - expected) < 0.01, "independent pressure sum uses the full compensatory pause");
        }
        foreach (long boundary in new[] { 2_180_000_000L, 2_224_000_000, 2_260_000_000, 2_400_000_000, 3_196_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureJunctionalReference.CreateElectrodes());
            source.GenerateBefore(boundary, 800, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(6_400_000_000, 1600, 100).Zip(restored.GenerateBefore(6_400_000_000, 1600, 100))
                .All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore through retrograde P, QRS, T and group boundary");
        }
        Check.That(RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = false }).AdvanceBefore(6_400_000_000, 100).All(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical), "explicit uncoupling suppresses mechanics without changing electrical schedule");
    }

    private static void PjcBoundedQueriesAndRejectionAreAtomic()
    {
        var plan = PrematureJunctionalReference.CreatePlan();
        var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
        for (long begin = 0; begin < 6_300_000_000; begin += 37_000_000)
        {
            long end = begin + 59_000_000;
            var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
            Check.That(RegularPhysiologyTimeline.Restore(new(plan, begin)).AdvanceBefore(end, Math.Max(1, expected.Length)).SequenceEqual(expected), "indexed half-open windows preserve original slots and simultaneous atrial mechanics/QRS");
        }
        var timeline = RegularPhysiologyTimeline.Restore(new(plan, 2_180_000_000));
        var state = timeline.CaptureState();
        try { timeline.AdvanceBefore(2_260_000_001, 2); throw new InvalidOperationException("Insufficient budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == state, "event budget rejection atomic"); }
        Check.That(timeline.AdvanceBefore(2_260_000_001, 3).Count == 3, "P-prime, QRS and atrial mechanical consume exact budget");
        var late = RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 2_000_000_000)).AdvanceBefore(long.MaxValue, 30);
        Check.That(late.Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex > 1_000_000), "late event query does not replay from epoch or renumber");
        foreach (var invalid in new[] { plan with { VentricularConductionRatio = 2 }, plan with { MechanicalEveryCycles = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 }, plan with { AtrialMechanicalOffsetNs = 40_000_000 }, plan with { CardiacActivity = CardiacActivity.VentricularOnly } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Incompatible PJC configuration accepted.");
        }
    }
}
