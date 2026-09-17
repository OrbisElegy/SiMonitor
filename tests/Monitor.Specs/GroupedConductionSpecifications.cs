// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class GroupedConductionSpecifications
{
    private static RegularPhysiologyPlan Plan(int total, int conducted) => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, VentricularConductionRatio: total, ConductedBeatsPerGroup: conducted);
    public static Specification[] All =>
    [
        new(nameof(MobitzTwoKeepsPrConstantAcrossDroppedBeats), MobitzTwoKeepsPrConstantAcrossDroppedBeats),
        new(nameof(GroupedConductionKeepsAtriaAndMechanicalPairs), GroupedConductionKeepsAtriaAndMechanicalPairs),
        new(nameof(GroupedSourcesRecoverAndPressureRunsOff), GroupedSourcesRecoverAndPressureRunsOff),
        new(nameof(GroupedConductionRejectsConflictsAndBudgetsAtomically), GroupedConductionRejectsConflictsAndBudgetsAtomically),
        new(nameof(AdditionalWenckebachRatiosKeepSharedTiming), AdditionalWenckebachRatiosKeepSharedTiming),
        new(nameof(WenckebachProgressesResetsAndRestores), WenckebachProgressesResetsAndRestores),
        new(nameof(WenckebachRejectsConflictsAndHonorsBoundaries), WenckebachRejectsConflictsAndHonorsBoundaries),
    ];
    private static void MobitzTwoKeepsPrConstantAcrossDroppedBeats()
    {
        foreach (var (size, pattern) in new[] { (3, AvConductionPattern.MobitzTwoThreeToTwoIllustration), (4, AvConductionPattern.MobitzTwoFourToThreeIllustration) })
        {
            var reference = Plan(size, size - 1);
            var plan = reference with { ConductionPattern = pattern };
            long end = size * 3 * 800_000_000L;
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(end, 100);
            Check.That(events.SequenceEqual(RegularPhysiologyTimeline.Start(reference).AdvanceBefore(end, 100)), "named Mobitz II retains constant PR and fixed dropped slots");
            var beats = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            Check.That(beats.Length == 3 * (size - 1) && beats.All(e => e.SimTimeNs - (long)e.CycleIndex * 800_000_000 == 160_000_000), "all conducted beats including post-pause retain PR160");
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes());
            source.GenerateBefore(1_000_000_000, 250, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(end, 3000, 100).Zip(restored.GenerateBefore(end, 3000, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "named narrow QRS source recovers across dropped beat");
            var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
            var pressure = VascularPressureSource.Create(plan, pressurePlan);
            var expected = VascularPressureSource.Create(reference, pressurePlan);
            for (long time = 0; time < end; time += 100_000_000)
            { Check.That(pressure.EvaluateAt(time) == expected.EvaluateAt(time), "named pattern shares grouped mechanical and runoff schedule"); }
            foreach (var invalid in new[] { plan with { ConductedBeatsPerGroup = 1 }, plan with { VentricularElectricalOffsetNs = 200_000_000 }, plan with { VentricularMechanicalOffsetNs = 280_000_000 }, plan with { CardiacActivity = CardiacActivity.VentricularOnly } })
            {
                try { RegularPhysiologyTimeline.Start(invalid); }
                catch (PhysiologyTimelineException) { continue; }
                throw new InvalidOperationException("Conflicting Mobitz II preset accepted.");
            }
            var timeline = RegularPhysiologyTimeline.Start(plan);
            var state = timeline.CaptureState();
            try { timeline.AdvanceBefore(end, 1); throw new InvalidOperationException("Budget accepted."); }
            catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == state, "failed event budget atomic"); }
        }
    }
    private static void AdditionalWenckebachRatiosKeepSharedTiming()
    {
        foreach (var (size, pattern) in new[] { (3, AvConductionPattern.WenckebachThreeToTwoIllustration), (5, AvConductionPattern.WenckebachFiveToFourIllustration) })
        {
            var plan = Plan(size, size - 1) with { ConductionPattern = pattern };
            long[] delays = [0, 80_000_000, 120_000_000, 140_000_000];
            long end = 3 * size * plan.HeartPeriodNs;
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(end, 100);
            var electrical = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            long[] expected = Enumerable.Range(0, size * 3).Where(i => i % size < size - 1)
                .Select(i => i * plan.HeartPeriodNs + 160_000_000 + delays[i % size]).ToArray();
            Check.That(electrical.Select(e => e.SimTimeNs).SequenceEqual(expected), "progressive PR, last P dropped and first PR restored");
            Check.That(events.Count(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical) == size * 3, "atrial rhythm remains regular through dropped beats");
            Check.That(electrical.Zip(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical)).All(p => p.First.CycleIndex == p.Second.CycleIndex && p.Second.SimTimeNs - p.First.SimTimeNs == 80_000_000), "shared electrical/mechanical sequence");
            foreach (var beat in electrical)
            {
                var timeline = RegularPhysiologyTimeline.Start(plan);
                var head = timeline.AdvanceBefore(beat.SimTimeNs, 100);
                Check.That(head.Concat(RegularPhysiologyTimeline.Restore(timeline.CaptureState()).AdvanceBefore(end, 100)).SequenceEqual(events), "half-open boundary recovery for every conducted beat");
            }
            var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000));
            foreach (var beat in electrical.Skip(1))
            {
                long onset = beat.SimTimeNs + 160_000_000;
                Check.That(pressure.EvaluateAt(onset - 1) < pressure.EvaluateAt(onset - 50_000_000) && pressure.EvaluateAt(onset + 200_000_000) > pressure.EvaluateAt(onset), "pressure follows shifted ejection plus transit");
            }
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes());
            source.GenerateBefore(1_000_000_000, 250, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(end, 3000, 100).Zip(restored.GenerateBefore(end, 3000, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "all leads recover across dropped beat and group reset");
            foreach (var invalid in new[] { plan with { ConductedBeatsPerGroup = 1 }, plan with { VentricularConductionRatio = 4 }, plan with { HeartPeriodNs = 240_000_000 + delays[size - 2] }, plan with { IndependentVentricularPeriodNs = 1_600_000_000 }, plan with { MechanicalEveryCycles = 2 } })
            {
                try { RegularPhysiologyTimeline.Start(invalid); }
                catch (PhysiologyTimelineException) { continue; }
                throw new InvalidOperationException("Conflicting Wenckebach input accepted.");
            }
            var budget = RegularPhysiologyTimeline.Restore(new(plan, 1_000_000_000));
            var before = budget.CaptureState();
            try { budget.AdvanceBefore(1_200_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
            catch (PhysiologyTimelineException) { Check.That(budget.CaptureState() == before, "failed budget leaves phase unchanged"); }
            Check.That(budget.AdvanceBefore(1_200_000_000, 2).Count == 2, "exact event count");
            var late = RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 2_000_000_000)).AdvanceBefore(long.MaxValue, 20);
            Check.That(late.All(e => e.SimTimeNs >= long.MaxValue - 2_000_000_000), "indexed late query avoids origin replay");
        }
    }
    private static void WenckebachProgressesResetsAndRestores()
    {
        var plan = Plan(4, 3) with { ConductionPattern = AvConductionPattern.WenckebachFourToThreeIllustration };
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
        var electrical = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
        Check.That(electrical.Select(e => e.SimTimeNs).SequenceEqual(new long[] { 160_000_000, 1_040_000_000, 1_880_000_000, 3_360_000_000, 4_240_000_000, 5_080_000_000 }), "PR 160/240/280 ms, dropped fourth P and reset");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).Select(e => e.SimTimeNs).SequenceEqual(Enumerable.Range(0, 8).Select(i => i * 800_000_000L)), "P remains regular");
        Check.That(electrical.Zip(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical)).All(p => p.Second.CycleIndex == p.First.CycleIndex && p.Second.SimTimeNs - p.First.SimTimeNs == 80_000_000), "mechanics shares progressive delay");
        foreach (long boundary in new long[] { 1_040_000_000, 1_040_000_001, 1_880_000_000, 2_700_000_000 })
        {
            var timeline = RegularPhysiologyTimeline.Start(plan);
            var head = timeline.AdvanceBefore(boundary, 100);
            Check.That(head.Concat(RegularPhysiologyTimeline.Restore(timeline.CaptureState()).AdvanceBefore(6_400_000_000, 100)).SequenceEqual(events), "split at delayed event/drop restores exact phase");
        }
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes());
        source.GenerateBefore(1_000_000_000, 250, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var tail = source.GenerateBefore(3_200_000_000, 550, 100);
        Check.That(tail.Zip(restored.GenerateBefore(3_200_000_000, 550, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "twelve-lead samples restore before shifted QRS");
        var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var pressure = VascularPressureSource.Create(plan, pressurePlan);
        Check.That(pressure.EvaluateAt(1_180_000_000) < pressure.EvaluateAt(1_100_000_000) && pressure.EvaluateAt(1_400_000_000) > pressure.EvaluateAt(1_200_000_000), "pressure waits for delayed mechanical event plus transit");
        Check.That(pressure.EvaluateAt(2_700_000_000) > pressure.EvaluateAt(3_200_000_000), "dropped beat has passive pressure runoff");
    }
    private static void WenckebachRejectsConflictsAndHonorsBoundaries()
    {
        var plan = Plan(4, 3) with { ConductionPattern = AvConductionPattern.WenckebachFourToThreeIllustration };
        foreach (var invalid in new[] { plan with { ConductedBeatsPerGroup = 2 }, plan with { VentricularConductionRatio = 5 }, plan with { ConductionPattern = (AvConductionPattern)99 }, plan with { HeartPeriodNs = 360_000_000 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Invalid Wenckebach configuration accepted.");
        }
        var timeline = RegularPhysiologyTimeline.Restore(new(plan, 1_000_000_000));
        var state = timeline.CaptureState();
        try { timeline.AdvanceBefore(1_200_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException e) { Check.That(e.ReasonCode == "PhysiologyTimeline.EventLimitExceeded" && timeline.CaptureState() == state, "delayed event budget failure atomic"); }
        Check.That(timeline.AdvanceBefore(1_200_000_000, 2).Count == 2, "count actual delayed electrical and mechanical events");
        var latePlan = plan with { EpochAnchorSimTimeNs = long.MaxValue - 2_000_000_000 };
        var late = RegularPhysiologyTimeline.Start(latePlan).AdvanceBefore(long.MaxValue, 100);
        Check.That(late.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) == 3 && late.All(e => e.SimTimeNs >= latePlan.EpochAnchorSimTimeNs), "Int128 group arithmetic near maximum timestamp");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        state = timeline.CaptureState();
        try { timeline.AdvanceBefore(3_200_000_000, 100, cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
        catch (OperationCanceledException) { Check.That(timeline.CaptureState() == state, "cancel is atomic"); }
    }
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
