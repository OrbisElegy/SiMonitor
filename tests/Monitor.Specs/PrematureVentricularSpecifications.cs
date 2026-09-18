// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PrematureVentricularSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PvcHasLocalWideQrsDiscordantTAndNoRelatedP), PvcHasLocalWideQrsDiscordantTAndNoRelatedP),
        new(nameof(PvcCompensationAndMechanicsUseActualEvents), PvcCompensationAndMechanicsUseActualEvents),
        new(nameof(PvcRecoveryQueriesAndRejectionsAreBounded), PvcRecoveryQueriesAndRejectionsAreBounded),
    ];

    private static void PvcHasLocalWideQrsDiscordantTAndNoRelatedP()
    {
        var plan = PrematureVentricularReference.CreatePlan();
        var full = Generate(PrematureVentricularReference.CreateElectrodes());
        var normal = Generate(TextbookElectrodeReference.CreateElectrodes(timing: PrematureVentricularReference.Timing));
        var wide = Generate(CompleteAvBlockVentricularReference.CreateElectrodes());
        for (int i = 0; i < 1600; i++)
        {
            long phase = i * 4_000_000L % 3_200_000_000;
            var expected = phase is >= 2_260_000_000 and < 2_740_000_000 ? wide[i] : normal[i];
            Check.That(full[i].MicrovoltValues.SequenceEqual(expected.MicrovoltValues), "wide morphology affects only the premature beat, sinus P/QRS/T unchanged");
        }
        Check.That(full.Skip(520).Take(45).All(s => s.MicrovoltValues.All(v => v == 0)), "no related P or P-prime precedes premature QRS");
        Check.That(full.Skip(685).Take(115).All(s => s.MicrovoltValues.All(v => v == 0)), "no hidden conducted beat before resumed sinus P");
        for (int lead = 0; lead < 12; lead++)
        {
            int q = full.Skip(565).Take(40).Select(s => s.MicrovoltValues[lead]).MaxBy(v => Math.Abs(v));
            int t = full.Skip(630).Take(55).Select(s => s.MicrovoltValues[lead]).MaxBy(v => Math.Abs(v));
            if (Math.Abs(q) > 100) { Check.That(Math.Sign(q) == -Math.Sign(t), "authored ventricular T opposes the dominant QRS"); }
        }
        Check.That(full.Skip(585).Take(20).Any(s => Math.Abs(s.MicrovoltValues[6]) > 200), "PVC persists beyond the normal80ms QRS");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateLeadIIBands()).GenerateBefore(6_400_000_000, 1600, 100);
        Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor and projected II share narrow and wide beats");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity preserved");
        IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> bands) => ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(6_400_000_000, 1600, 100);
    }

    private static void PvcCompensationAndMechanicsUseActualEvents()
    {
        var plan = PrematureVentricularReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 0, 800_000_000, 1_600_000_000, 3_200_000_000, 4_000_000_000, 4_800_000_000 }), "authored full compensatory pause with no related sinus P");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 160_000_000, 960_000_000, 1_760_000_000, 2_260_000_000, 3_360_000_000, 4_160_000_000, 4_960_000_000, 5_460_000_000 }), "500+1100ms RR equals two baseline cycles");
        Check.That(events.All(e => e.Kind is not (PhysiologyCycleEventKind.RetrogradeAtrialElectrical or PhysiologyCycleEventKind.PrematureAtrialElectrical) && !(e.Kind == PhysiologyCycleEventKind.AtrialMechanical && e.CycleIndex % 4 == 3)), "no invented ectopic atrial electrical or mechanical activation");
        var junctional = PrematureJunctionalReference.CreatePlan();
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).SequenceEqual(RegularPhysiologyTimeline.Start(junctional).AdvanceBefore(6_400_000_000, 100).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical)), "PVC ejection times follow actual QRS plus80ms");
        var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var a = VascularPressureSource.Create(plan, pressurePlan); var b = VascularPressureSource.Create(junctional, pressurePlan);
        for (long time = 0; time < 6_400_000_000; time += 13_000_000)
        { Check.That(a.EvaluateAt(time) == b.EvaluateAt(time), "same ventricular schedule retains independently tested PJC pressure, no uncalibrated stroke-volume inference"); }
        Check.That(a.EvaluateAt(long.MaxValue) == b.EvaluateAt(long.MaxValue), "late pressure query remains bounded");
        Check.That(RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = false }).AdvanceBefore(6_400_000_000, 100).All(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical), "explicit mechanical disable retains electrical PVC");
    }

    private static void PvcRecoveryQueriesAndRejectionsAreBounded()
    {
        var plan = PrematureVentricularReference.CreatePlan();
        var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
        for (long begin = 0; begin < 6_300_000_000; begin += 37_000_000)
        {
            long end = begin + 59_000_000;
            var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
            Check.That(RegularPhysiologyTimeline.Restore(new(plan, begin)).AdvanceBefore(end, Math.Max(1, expected.Length)).SequenceEqual(expected), "half-open query and exact event budget omit atrial ectopic slot");
        }
        var timeline = RegularPhysiologyTimeline.Restore(new(plan, 2_200_000_000));
        var saved = timeline.CaptureState();
        try { timeline.AdvanceBefore(2_400_000_000, 1); throw new InvalidOperationException("Insufficient budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == saved, "event rejection atomic"); }
        Check.That(timeline.AdvanceBefore(2_400_000_000, 2).Count == 2, "only ventricular electrical and mechanical events consume budget");
        foreach (long boundary in new[] { 2_260_000_000L, 2_348_000_000, 2_600_000_000, 3_196_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes());
            source.GenerateBefore(boundary, 800, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(6_400_000_000, 1600, 100).Zip(restored.GenerateBefore(6_400_000_000, 1600, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "wide QRS/T and full-pause checkpoint recovery");
        }
        Check.That(RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 3_200_000_000)).AdvanceBefore(long.MaxValue, 30).Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 4 == 3), "late original PVC identity without epoch replay");
        foreach (var invalid in new[] { plan with { VentricularConductionRatio = 2 }, plan with { MechanicalEveryCycles = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 }, plan with { CardiacActivity = CardiacActivity.VentricularOnly } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Incompatible PVC plan accepted.");
        }
    }
}
