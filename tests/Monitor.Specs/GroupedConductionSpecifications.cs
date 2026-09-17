// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class GroupedConductionSpecifications
{
    private static RegularPhysiologyPlan Plan(int total, int conducted) => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, VentricularConductionRatio: total, ConductedBeatsPerGroup: conducted);
    public static Specification[] All =>
    [
        new(nameof(GroupedConductionKeepsAtriaAndMechanicalPairs), GroupedConductionKeepsAtriaAndMechanicalPairs),
        new(nameof(GroupedSourcesRecoverAndPressureRunsOff), GroupedSourcesRecoverAndPressureRunsOff),
        new(nameof(GroupedConductionRejectsConflictsAndBudgetsAtomically), GroupedConductionRejectsConflictsAndBudgetsAtomically),
    ];
    private static void GroupedConductionKeepsAtriaAndMechanicalPairs()
    {
        foreach (var (total, conducted) in new[] { (3, 2), (4, 3), (5, 2) })
        {
            var plan = Plan(total, conducted);
            var timeline = RegularPhysiologyTimeline.Start(plan);
            long end = total * 3 * plan.HeartPeriodNs;
            var events = timeline.AdvanceBefore(end, 100);
            var reference = RegularPhysiologyTimeline.Start(Plan(1, 1)).AdvanceBefore(end, 100);
            Check.That(events.Where(e => e.Kind is not (PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical)).SequenceEqual(
                reference.Where(e => e.Kind is not (PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical))), "P/atrial mechanics and breathing unchanged");
            var electrical = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            var mechanical = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
            Check.That(electrical.Length == conducted * 3 && mechanical.Length == electrical.Length, "N:M event count");
            foreach (var (e, m) in electrical.Zip(mechanical))
            {
                Check.That(e.CycleIndex % (ulong)total < (ulong)conducted && m.CycleIndex == e.CycleIndex &&
                    e.SimTimeNs == (long)e.CycleIndex * plan.HeartPeriodNs + 160_000_000 && m.SimTimeNs - e.SimTimeNs == 80_000_000,
                    "fixed PR and mechanical lag on original atrial slot IDs");
            }
            var split = RegularPhysiologyTimeline.Start(plan);
            var first = split.AdvanceBefore(conducted * plan.HeartPeriodNs + 100_000_000, 100);
            var restored = RegularPhysiologyTimeline.Restore(split.CaptureState());
            Check.That(first.Concat(restored.AdvanceBefore(end, 100)).SequenceEqual(events), "split inside a blocked slot restores group phase");
        }
    }
    private static void GroupedSourcesRecoverAndPressureRunsOff()
    {
        var plan = Plan(3, 2);
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes());
        var first = source.GenerateBefore(1_700_000_000, 425, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var tail = source.GenerateBefore(4_800_000_000, 775, 100);
        Check.That(tail.Zip(restored.GenerateBefore(4_800_000_000, 775, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "all twelve leads restore grouped phase");
        var atrialOnly = ElectrodeSignalGenerator.Start(Plan(1, 1) with { CardiacActivity = CardiacActivity.AtrialOnly }, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(4_800_000_000, 1200, 100);
        var samples = first.Concat(tail).ToArray();
        Check.That(samples.Skip(400).Take(200).Zip(atrialOnly.Skip(400).Take(200)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "blocked slot preserves P but has no ventricular wave in any lead");
        var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var pressure = VascularPressureSource.Create(plan, pressurePlan);
        Check.That(pressure.EvaluateAt(2_100_000_000) > pressure.EvaluateAt(2_400_000_000) && pressure.EvaluateAt(2_400_000_000) > 0,
            "missing ejection retains passive pressure runoff");
        Check.That(pressure.EvaluateAt(2_900_000_000) > pressure.EvaluateAt(2_700_000_000), "next conducted beat resumes ejection");
    }
    private static void GroupedConductionRejectsConflictsAndBudgetsAtomically()
    {
        var plan = Plan(3, 2);
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(2_400_000_000, 11); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException e) { Check.That(e.ReasonCode == "PhysiologyTimeline.EventLimitExceeded" && timeline.CaptureState() == before, "budget failure atomic"); }
        Check.That(timeline.AdvanceBefore(2_400_000_000, 12).Count == 12, "budget counts actual selected events");
        foreach (var invalid in new[] { plan with { ConductedBeatsPerGroup = 0 }, plan with { ConductedBeatsPerGroup = 3 }, plan with { IndependentVentricularPeriodNs = 1_600_000_000 }, plan with { MechanicalEveryCycles = 2 }, plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 2 }, plan with { CardiacActivity = CardiacActivity.VentricularOnly } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting group accepted.");
        }
        var sparse = Plan(int.MaxValue, 2) with { HeartPeriodNs = 4, VentricularElectricalOffsetNs = 1, AtrialMechanicalOffsetNs = 1, VentricularMechanicalOffsetNs = 2, BreathPeriodNs = long.MaxValue / 2, InspirationDurationNs = 1 };
        var late = RegularPhysiologyTimeline.Restore(new(sparse, (long)(int.MaxValue - 2) * 4));
        var events = late.AdvanceBefore((long)int.MaxValue * 4, 10);
        Check.That(events.Count == 4 && events.All(e => e.Kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical), "long blocked suffix skips without invented ventricular events");
    }
}
