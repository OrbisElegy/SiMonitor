// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class BlockedPrematureAtrialSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(BlockedPacRetainsPPrimeOnTButOmitsVentricularActivation), BlockedPacRetainsPPrimeOnTButOmitsVentricularActivation),
        new(nameof(BlockedPacPressureAndRecoveryRespectTheMissingBeat), BlockedPacPressureAndRecoveryRespectTheMissingBeat),
    ];

    private static void BlockedPacRetainsPPrimeOnTButOmitsVentricularActivation()
    {
        var plan = PrematureAtrialReference.CreatePlan(true);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_200_000_000, 100);
        long[] p = [0, 800_000_000, 1_600_000_000, 2_000_000_000, 3_100_000_000, 3_900_000_000, 4_700_000_000, 5_100_000_000];
        Check.That(events.Where(e => e.Kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.PrematureAtrialElectrical).Select(e => e.SimTimeNs).SequenceEqual(p), "early P-prime persists through blocked beat and sinus reset");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialMechanical).Select(e => e.SimTimeNs).SequenceEqual(p.Select(t => t + 80_000_000)), "blocked atrial impulse retains atrial mechanics");
        foreach (var (kind, offset) in new[] { (PhysiologyCycleEventKind.VentricularElectrical, 160_000_000L), (PhysiologyCycleEventKind.VentricularMechanical, 240_000_000L) })
        {
            Check.That(events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs).SequenceEqual(p.Where((_, i) => i % 4 != 3).Select(t => t + offset)), "no ventricular electrical or mechanical event follows blocked P-prime");
            Check.That(events.Where(e => e.Kind == kind).Select(e => e.CycleIndex).SequenceEqual(new ulong[] { 0, 1, 2, 4, 5, 6 }), "blocked slots do not renumber beats");
        }
        var electrodes = PrematureAtrialReference.CreateElectrodes(true);
        var full = Generate(electrodes);
        var withoutP = Generate(electrodes.Select(e => e with { Bands = e.Bands.Where(b => b.Trigger != PhysiologyCycleEventKind.PrematureAtrialElectrical).ToArray() }).ToArray());
        var onlyP = Generate(electrodes.Select(e => e with { Bands = e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.PrematureAtrialElectrical).ToArray() }).ToArray());
        Check.That(Enumerable.Range(500, 20).Any(i => withoutP[i].MicrovoltValues[1] > 100 && onlyP[i].MicrovoltValues[1] < -150), "P-prime overlaps preceding positive T, rather than appearing only on a flat baseline");
        Check.That(Enumerable.Range(500, 20).All(i => Enumerable.Range(0, 12).All(lead =>
            Math.Abs(full[i].MicrovoltValues[lead] - withoutP[i].MicrovoltValues[lead] - onlyP[i].MicrovoltValues[lead]) <= 2)), "P-prime and old T superpose across leads without replacing T");
        Check.That(full.Skip(520).Take(255).All(s => s.MicrovoltValues.All(v => v == 0)), "no new QRS/T after old T finishes, until resumed sinus P");
        Check.That(full.Skip(815).Take(20).Max(s => s.MicrovoltValues[1]) > 500, "sinus QRS resumes after1500ms RR");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureAtrialReference.CreateLeadIIBands(true)).GenerateBefore(6_200_000_000, 1550, 100);
        Check.That(full.Zip(monitor).All(pair => Math.Abs(pair.First.MicrovoltValues[1] - pair.Second.NormalizedValue) <= 1), "monitor II shares blocked P-prime/T superposition and pause");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb projection identity retained");
        IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> bands) =>
            ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(6_200_000_000, 1550, 100);
    }

    private static void BlockedPacPressureAndRecoveryRespectTheMissingBeat()
    {
        var plan = PrematureAtrialReference.CreatePlan(true);
        var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_200_000_000, 100);
        for (long begin = 0; begin < 6_100_000_000; begin += 37_000_000)
        {
            long end = begin + 59_000_000;
            var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
            Check.That(RegularPhysiologyTimeline.Restore(new(plan, begin)).AdvanceBefore(end, Math.Max(1, expected.Length)).SequenceEqual(expected), "exact window/budget ignores blocked ventricular candidates");
        }
        var timeline = RegularPhysiologyTimeline.Restore(new(plan, 2_000_000_000));
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(2_240_000_001, 1); throw new InvalidOperationException("Event limit accepted."); }
        catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == before, "event budget failure atomic"); }
        Check.That(timeline.AdvanceBefore(2_240_000_001, 2).Count == 2, "only P-prime and atrial contraction consume budget");
        foreach (long boundary in new[] { 2_000_000_000L, 2_040_000_000, 2_080_000_000, 3_096_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureAtrialReference.CreateElectrodes(true));
            source.GenerateBefore(boundary, 800, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(3_600_000_000, 900, 100).Zip(restored.GenerateBefore(3_600_000_000, 900, 100))
                .All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore through P/T overlap and missing beat");
        }
        var pressure = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var vascular = VascularPressureSource.Create(plan, pressure);
        long[] beats = [240_000_000, 1_040_000_000, 1_840_000_000, 3_340_000_000, 4_140_000_000, 4_940_000_000];
        for (long time = 0; time < 6_200_000_000; time += 13_000_000)
        {
            double t = Math.Max(0, time - 80_000_000), tau = 2_900_000_000;
            double expected = 1000 + 7000 * Math.Exp(-t / tau);
            foreach (long beat in beats.Where(b => b <= t))
            { expected += 30000 * (1 - Math.Exp(-Math.Min(t - beat, 240_000_000) / tau)) * Math.Exp(-Math.Max(0, t - beat - 240_000_000) / tau); }
            Check.That(Math.Abs((double)vascular.EvaluateAt(time) / FixedPointMath.Q32One - expected) < 0.01, "pressure contains no hidden ectopic ejection, against independent analytic reference");
        }
        Check.That(vascular.EvaluateAt(3_300_000_000) < vascular.EvaluateAt(2_400_000_000), "pressure runs off in the ventricular pause");
        Check.That(RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 2_000_000_000)).AdvanceBefore(long.MaxValue, 30).Count > 0 && vascular.EvaluateAt(long.MaxValue) > 0, "late indexed event and pressure queries stay bounded");
        foreach (var invalid in new[] { plan with { VentricularConductionRatio = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 }, plan with { MechanicalEveryCycles = 2 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting blocked PAC plan accepted.");
        }
    }
}
