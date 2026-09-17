// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class JunctionalEscapeSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(JunctionalEscapeHasIndependentClocksAndMechanicalPairs), JunctionalEscapeHasIndependentClocksAndMechanicalPairs),
        new(nameof(JunctionalEscapeRestoresNormalQrsAndRejectsConflicts), JunctionalEscapeRestoresNormalQrsAndRejectsConflicts),
    ];
    private static void JunctionalEscapeHasIndependentClocksAndMechanicalPairs()
    {
        var plan = CompleteAvBlockJunctionalReference.CreatePlan();
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var events = timeline.AdvanceBefore(4_000_000_000, 100);
        var qrs = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
        Check.That(qrs.Select(e => e.SimTimeNs).SequenceEqual(new long[] { 400_000_000, 1_600_000_000, 2_800_000_000 }), "50/min established escape clock, exclusive endpoint");
        Check.That(events.Count(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical) == 5 &&
            qrs.Select(e => e.SimTimeNs % plan.HeartPeriodNs).Distinct().Count() == 2, "75/min P and no fixed PR");
        var changed = RegularPhysiologyTimeline.Start(CompleteAvBlockJunctionalReference.CreatePlan(700_000_000)).AdvanceBefore(4_000_000_000, 100);
        Check.That(changed.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).SequenceEqual(qrs), "atrial rate cannot conduct or reset escape clock");
        Check.That(qrs.Zip(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical)).All(p => p.First.CycleIndex == p.Second.CycleIndex && p.Second.SimTimeNs - p.First.SimTimeNs == 80_000_000), "mechanics follows escape, not P");
        var head = RegularPhysiologyTimeline.Start(plan);
        var first = head.AdvanceBefore(1_600_000_000, 100);
        Check.That(first.Concat(RegularPhysiologyTimeline.Restore(head.CaptureState()).AdvanceBefore(4_000_000_000, 100)).SequenceEqual(events), "restore at coincident P and QRS has no duplicate or omission");
        var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var pressure = VascularPressureSource.Create(plan, pressurePlan);
        Check.That(pressure.EvaluateAt(1_400_000_000) > pressure.EvaluateAt(1_700_000_000) && pressure.EvaluateAt(1_950_000_000) > pressure.EvaluateAt(1_760_000_000), "pressure decays until escape ejection arrives");
        var atrialChangePressure = VascularPressureSource.Create(plan with { HeartPeriodNs = 700_000_000 }, pressurePlan);
        Check.That(Enumerable.Range(0, 100).All(i => pressure.EvaluateAt(i * 40_000_000L) == atrialChangePressure.EvaluateAt(i * 40_000_000L)), "atrial clock does not invent arterial ejections");
    }
    private static void JunctionalEscapeRestoresNormalQrsAndRejectsConflicts()
    {
        var plan = CompleteAvBlockJunctionalReference.CreatePlan();
        var electrodes = CompleteAvBlockJunctionalReference.CreateElectrodes();
        Check.That(electrodes.All(e => e.Bands.Any(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical &&
            b.DelayNs == 0 && b.DurationNs == 80_000_000)), "every electrode retains an 80ms QRS activation band");
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        source.GenerateBefore(1_600_000_000, 400, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var actual = source.GenerateBefore(4_000_000_000, 600, 100);
        Check.That(actual.Zip(restored.GenerateBefore(4_000_000_000, 600, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "all twelve leads recover during AV dissociation");
        var reference = ElectrodeSignalGenerator.Start(plan with { ConductionPattern = AvConductionPattern.FixedPr }, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(4_000_000_000, 1000, 100);
        Check.That(actual.Zip(reference.Skip(400)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "escape uses normal 80ms reference QRS, preserves overlapping atrial waves");
        foreach (long period in new long[] { 1_000_000_000, 1_500_000_000 })
        { _ = CompleteAvBlockJunctionalReference.CreatePlan(800_000_000, period, 0); }
        foreach (var invalid in new[] { plan with { IndependentVentricularPeriodNs = null }, plan with { IndependentVentricularPeriodNs = 999_999_999 }, plan with { IndependentVentricularPeriodNs = 1_500_000_001 }, plan with { HeartPeriodNs = 1_200_000_000 }, plan with { VentricularConductionRatio = 2 }, plan with { CardiacActivity = CardiacActivity.VentricularOnly }, plan with { ConductedBeatsPerGroup = 2 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting junctional escape plan accepted.");
        }
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(4_000_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException e) { Check.That(e.ReasonCode == "PhysiologyTimeline.EventLimitExceeded" && timeline.CaptureState() == before, "escape budget failure is atomic"); }
    }
}
