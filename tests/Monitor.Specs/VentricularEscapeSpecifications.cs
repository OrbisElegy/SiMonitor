// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentricularEscapeSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(VentricularEscapeHasWideDistinctQrsAndDiscordantT), VentricularEscapeHasWideDistinctQrsAndDiscordantT),
        new(nameof(VentricularEscapeClocksRestoreAndValidate), VentricularEscapeClocksRestoreAndValidate),
    ];
    private static void VentricularEscapeHasWideDistinctQrsAndDiscordantT()
    {
        var plan = CompleteAvBlockVentricularReference.CreatePlan();
        var electrodes = CompleteAvBlockVentricularReference.CreateElectrodes();
        Check.That(electrodes.All(e => e.Bands[1].DurationNs == 160_000_000), "160ms ventricular activation in every electrode");
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(2_000_000_000, 500, 100);
        short[] Lead(EcgLead lead) => samples.Select(s => s.MicrovoltValues[(int)lead]).ToArray();
        short[] ii = Lead(EcgLead.II); short[] v1 = Lead(EcgLead.V1); short[] v5 = Lead(EcgLead.V5);
        Check.That(ii[111] > ii[117] && ii[125] > ii[117] && ii[132] > 200, "broad two-peak QRS has a notch and late activation");
        Check.That(v1.Skip(110).Take(25).Min() < -900 && v5.Skip(100).Take(40).Max() > 1000, "authored rS and broad R differ across chest leads");
        Check.That(ii.Skip(175).Take(20).Min() < -200 && v1.Skip(175).Take(20).Max() > 200 && v5.Skip(175).Take(20).Min() < -200, "secondary T opposes dominant QRS in selected leads");
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[(int)EcgLead.I] + s.MicrovoltValues[(int)EcgLead.III] - s.MicrovoltValues[(int)EcgLead.II]) <= 1), "limb projection identity survives wide morphology");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, CompleteAvBlockVentricularReference.CreateLeadIIBands()).GenerateBefore(2_000_000_000, 500, 100);
        Check.That(monitor.Zip(ii).All(p => Math.Abs(p.First.NormalizedValue - p.Second) <= 1), "monitor ECG and twelve-lead II share morphology");
        var stretched = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(timing: CompleteAvBlockVentricularReference.Timing)).GenerateBefore(2_000_000_000, 500, 100);
        Check.That(!samples.Skip(100).Take(40).Zip(stretched.Skip(100).Take(40)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "ectopic QRS is not just stretched normal activation");
        Check.That(samples.Take(25).Zip(stretched.Take(25)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "atrial morphology remains unchanged");
    }
    private static void VentricularEscapeClocksRestoreAndValidate()
    {
        var plan = CompleteAvBlockVentricularReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_000_000_000, 100);
        var qrs = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
        Check.That(qrs.Select(e => e.SimTimeNs).SequenceEqual(new long[] { 400_000_000, 2_400_000_000, 4_400_000_000 }), "30/min independent ventricular escape");
        var changed = RegularPhysiologyTimeline.Start(plan with { HeartPeriodNs = 700_000_000 }).AdvanceBefore(6_000_000_000, 100);
        Check.That(changed.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).SequenceEqual(qrs), "P does not trigger or reset escape");
        Check.That(qrs.Zip(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical)).All(p => p.Second.CycleIndex == p.First.CycleIndex && p.Second.SimTimeNs - p.First.SimTimeNs == 80_000_000), "mechanical lag stays attached to escape");
        var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000));
        Check.That(pressure.EvaluateAt(1_600_000_000) > pressure.EvaluateAt(2_500_000_000) && pressure.EvaluateAt(2_750_000_000) > pressure.EvaluateAt(2_560_000_000), "pressure runs off between slow escape ejections");
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, CompleteAvBlockVentricularReference.CreateElectrodes());
        source.GenerateBefore(2_480_000_000, 620, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var tail = source.GenerateBefore(4_000_000_000, 380, 100);
        Check.That(tail.Zip(restored.GenerateBefore(4_000_000_000, 380, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore inside wide QRS preserves all leads");
        foreach (long period in new long[] { 1_500_000_000, 3_000_000_000 })
        { _ = CompleteAvBlockVentricularReference.CreatePlan(800_000_000, period, 0); }
        foreach (var invalid in new[] { plan with { IndependentVentricularPeriodNs = null }, plan with { IndependentVentricularPeriodNs = 1_499_999_999 }, plan with { IndependentVentricularPeriodNs = 3_000_000_001 }, plan with { HeartPeriodNs = 2_000_000_000 }, plan with { CardiacActivity = CardiacActivity.VentricularOnly }, plan with { VentricularConductionRatio = 2 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Invalid ventricular escape accepted.");
        }
        var timeline = RegularPhysiologyTimeline.Restore(new(plan, 2_400_000_000));
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(2_480_000_001, 1); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(before == timeline.CaptureState(), "budget failure at coincident atrial/ventricular events is atomic"); }
        Check.That(timeline.AdvanceBefore(2_480_000_001, 4).Count == 4, "exact coincident event boundary budget");
    }
}
