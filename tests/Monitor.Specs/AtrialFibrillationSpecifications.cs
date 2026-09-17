// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AtrialFibrillationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FibrillationSharesIrregularElectricalAndMechanicalSchedule), FibrillationSharesIrregularElectricalAndMechanicalSchedule),
        new(nameof(FibrillationHasVariableFAndCoarseFineRecovery), FibrillationHasVariableFAndCoarseFineRecovery),
        new(nameof(FibrillationRejectsConflictsAndPreservesEventBoundaries), FibrillationRejectsConflictsAndPreservesEventBoundaries),
    ];
    private static void FibrillationSharesIrregularElectricalAndMechanicalSchedule()
    {
        var plan = AtrialFibrillationReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_000_000_000, 100);
        Check.That(events.All(e => e.Kind is not (PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical)), "no normal P or coordinated atrial mechanical events");
        var electrical = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
        Check.That(electrical.Select(e => e.SimTimeNs / 1_000_000).SequenceEqual(new long[] { 80, 1212, 2005, 2660, 3519, 4237, 4935, 5932 }), "stable authored irregular timestamps");
        Check.That(electrical.Zip(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical)).All(p => p.Second.CycleIndex == p.First.CycleIndex && p.Second.SimTimeNs - p.First.SimTimeNs == 80_000_000), "electrical and mechanical jitter cannot diverge");
        var longer = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(120_000_000_000, 1000).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
        long[] rr = longer.Zip(longer.Skip(1)).Select(p => p.Second.SimTimeNs - p.First.SimTimeNs).ToArray();
        Check.That(rr.Distinct().Count() > 50 && rr.All(value => value is >= 440_000_000 and <= 1_160_000_000), "irregular RR stays within pulse support bounds");
        var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000));
        foreach (var beat in electrical.Skip(1))
        {
            long onset = beat.SimTimeNs + 160_000_000;
            Check.That(pressure.EvaluateAt(onset - 1) < pressure.EvaluateAt(onset - 100_000_000) &&
                pressure.EvaluateAt(onset + 200_000_000) > pressure.EvaluateAt(onset), "indexed pressure waits for each irregular mechanical beat plus transit");
        }
        var fine = RegularPhysiologyTimeline.Start(AtrialFibrillationReference.CreatePlan(true)).AdvanceBefore(6_000_000_000, 100);
        Check.That(events.SequenceEqual(fine), "coarse/fine changes morphology only");
        var noMechanics = RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = false }).AdvanceBefore(6_000_000_000, 100);
        Check.That(noMechanics.All(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical) && noMechanics.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) == electrical.Length, "perfusion can be disabled independently");
    }
    private static void FibrillationHasVariableFAndCoarseFineRecovery()
    {
        var plan = AtrialFibrillationReference.CreatePlan();
        var electrodes = AtrialFibrillationReference.CreateElectrodes();
        var fine = AtrialFibrillationReference.CreateElectrodes(true);
        Check.That(electrodes.All(e => e.Bands[0].Trigger == PhysiologyCycleEventKind.AtrialFibrillationSegment && e.Bands[1].DurationNs == 80_000_000), "replace P and retain narrow QRS");
        Check.That(electrodes.Zip(fine).All(p => p.First.Bands[1].TableQ32.SequenceEqual(p.Second.Bands[1].TableQ32) && p.First.Bands[2].TableQ32.SequenceEqual(p.Second.Bands[2].TableQ32)), "coarse/fine leaves QRS and T unchanged");
        var atrial = electrodes.Select(e => e with { Bands = [e.Bands[0]] }).ToArray();
        var f = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, atrial).GenerateBefore(4_000_000_000, 1000, 100);
        short[] v1 = f.Select(s => s.MicrovoltValues[(int)EcgLead.V1]).ToArray();
        int[] peaks = Enumerable.Range(1, v1.Length - 2).Where(i => v1[i] > 0 && v1[i] >= v1[i - 1] && v1[i] > v1[i + 1]).ToArray();
        Check.That(peaks.Select(i => v1[i]).Distinct().Count() > 10 && peaks.Zip(peaks.Skip(1)).Select(p => p.Second - p.First).Distinct().Count() > 5, "f varies in amplitude and spacing");
        Check.That(v1.Max() > 150 && f.Max(s => Math.Abs(s.MicrovoltValues[1])) < v1.Max() / 2, "V1 carries prominent f");
        var fineF = ElectrodeSignalGenerator.Start(AtrialFibrillationReference.CreatePlan(true), "AcqECGMonitor250@1", 1,
            fine.Select(e => e with { Bands = [e.Bands[0]] }).ToArray()).GenerateBefore(4_000_000_000, 1000, 100);
        Check.That(f.Zip(fineF).All(p => Math.Abs(p.First.MicrovoltValues[6] * 3 - p.Second.MicrovoltValues[6] * 10) <= 7), "fine f is thirty percent with acquisition rounding");
        var complete = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(4_000_000_000, 1000, 100);
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AtrialFibrillationReference.CreateLeadIIBands()).GenerateBefore(4_000_000_000, 1000, 100);
        Check.That(complete.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor lead II shares f, QRS and T with electrode projection");
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        source.GenerateBefore(AtrialFibrillationReference.SegmentDurationNs - 4_000_000, 4095, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        long end = AtrialFibrillationReference.SegmentDurationNs + 800_000_000;
        var tail = source.GenerateBefore(end, 201, 100);
        Check.That(tail.Zip(restored.GenerateBefore(end, 201, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore across f tile boundary preserves irregular rhythm and all leads");
        Check.That(tail.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities preserved");
    }
    private static void FibrillationRejectsConflictsAndPreservesEventBoundaries()
    {
        var plan = AtrialFibrillationReference.CreatePlan();
        var full = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(4_000_000_000, 100);
        foreach (long boundary in new long[] { 1_212_000_000, 1_212_000_001, 1_292_000_000, 2_005_000_000 })
        {
            var timeline = RegularPhysiologyTimeline.Start(plan);
            var head = timeline.AdvanceBefore(boundary, 100);
            Check.That(head.Concat(RegularPhysiologyTimeline.Restore(timeline.CaptureState()).AdvanceBefore(4_000_000_000, 100)).SequenceEqual(full), "half-open irregular event partition");
        }
        var budget = RegularPhysiologyTimeline.Restore(new(plan, 1_200_000_000));
        var before = budget.CaptureState();
        try { budget.AdvanceBefore(1_300_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(before == budget.CaptureState(), "budget failure atomic"); }
        Check.That(budget.AdvanceBefore(1_300_000_000, 2).Count == 2, "exact delayed E/M budget");
        var reference = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(32_000_000_000, 200);
        for (long start = 0; start < 31_000_000_000; start += 41_000_000)
        {
            long end = start + 59_000_000;
            var expected = reference.Where(e => e.SimTimeNs >= start && e.SimTimeNs < end).ToArray();
            var actual = RegularPhysiologyTimeline.Restore(new(plan, start)).AdvanceBefore(end, Math.Max(1, expected.Length));
            Check.That(expected.SequenceEqual(actual), "tight budgets count emitted events, not jitter-window candidates");
        }
        var late = RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 2_000_000_000)).AdvanceBefore(long.MaxValue, 20);
        Check.That(late.Count is > 0 and < 20 && late.All(e => e.SimTimeNs >= long.MaxValue - 2_000_000_000), "direct indexed lookup near maximum timestamp without epoch scan");
        foreach (var invalid in new[] { plan with { HeartPeriodNs = 600_000_000 }, plan with { VentricularConductionRatio = 2 }, plan with { MechanicalEveryCycles = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 }, plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 2 }, plan with { CardiacActivity = CardiacActivity.AtrialOnly } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting AF configuration accepted.");
        }
    }
}
