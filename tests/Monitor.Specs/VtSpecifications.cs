// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VtSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(VtSeparatesFasterVentriclesFromSlowerAtria), VtSeparatesFasterVentriclesFromSlowerAtria),
        new(nameof(VtPreservesMonomorphicQrsAndRestoresIndependentPhases), VtPreservesMonomorphicQrsAndRestoresIndependentPhases),
    ];
    private static void VtSeparatesFasterVentriclesFromSlowerAtria()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 100);
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).Select(e => e.SimTimeNs)
            .SequenceEqual(new long[] { 0, 800_000_000, 1_600_000_000, 2_400_000_000 }), "independent75bpm atria");
        foreach (var kind in new[] { PhysiologyCycleEventKind.VentricularElectrical, PhysiologyCycleEventKind.VentricularMechanical })
        {
            long offset = kind == PhysiologyCycleEventKind.VentricularElectrical ? 120_000_000 : 200_000_000;
            Check.That(events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs)
                .SequenceEqual(Enumerable.Range(0, 8).Select(i => offset + i * 375_000_000L)), "independent160bpm ventricular grid");
        }
        foreach (var invalid in new[] { plan with { HeartPeriodNs = 375_000_000 }, plan with { IndependentVentricularPeriodNs = 400_000_000 },
            plan with { ConductionPattern = AvConductionPattern.FixedPr }, plan with { ConductionPattern = AvConductionPattern.CompleteAvBlockVentricularIllustration },
            plan with { ConductionPattern = AvConductionPattern.CompleteAvBlockJunctionalIllustration }, plan with { VentricularConductionRatio = 2 },
            plan with { CardiacActivity = CardiacActivity.VentricularOnly }, plan with { VentricularElectricalOffsetNs = 0 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException e) when (e.ReasonCode == "PhysiologyTimeline.InvalidState") { continue; }
            throw new InvalidOperationException("Invalid VT grid accepted.");
        }
        var timeline = RegularPhysiologyTimeline.Start(plan);
        timeline.AdvanceBefore(800_000_000, 100);
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(3_000_000_000, 1); throw new InvalidOperationException("Event limit accepted."); }
        catch (PhysiologyTimelineException) { }
        Check.That(timeline.CaptureState() == before, "event budget rejection is atomic");
        Check.That(timeline.AdvanceBefore(3_000_000_000, 100).SequenceEqual(RegularPhysiologyTimeline.Restore(before).AdvanceBefore(3_000_000_000, 100)), "timeline recovery preserves both clocks");
    }
    private static void VtPreservesMonomorphicQrsAndRestoresIndependentPhases()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var electrodes = VentricularTachycardiaReference.CreateElectrodes();
        var escape = CompleteAvBlockVentricularReference.CreateElectrodes();
        for (int i = 0; i < electrodes.Count; i++)
        {
            Check.That(electrodes[i].Bands[0].TableQ32.SequenceEqual(escape[i].Bands[0].TableQ32), "normal atrial morphology retained");
            Check.That(electrodes[i].Bands[1].DurationNs == 160_000_000 && electrodes[i].Bands[1].TableQ32.SequenceEqual(escape[i].Bands[1].TableQ32), "one authored broad ventricular contour");
            Check.That(electrodes[i].Bands[2].DelayNs + electrodes[i].Bands[2].DurationNs == 275_000_000, "QT support ends before next ventricular beat");
        }
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(3_000_000_000, 750, 200);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateLeadIIBands()).GenerateBefore(3_000_000_000, 750, 200);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared monitorII projection");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
        var pBands = VentricularTachycardiaReference.CreateLeadIIBands().Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
        var p = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, pBands).GenerateBefore(3_000_000_000, 750, 200);
        Check.That(p.Where((_, i) => i % 200 >= 25).All(s => s.NormalizedValue == 0) && p.Take(25).Any(s => s.NormalizedValue > 50), "P follows atrial clock, not each QRS");
        // P onset relative to ventricular grid drifts by50ms each atrial cycle.
        Check.That(new long[] { 800_000_000, 1_600_000_000, 2_400_000_000 }.Select(t => (t - 120_000_000) % 375_000_000).Distinct().Count() == 3, "no fixed PR");
        foreach (long boundary in new long[] { 0, 118_000_000, 278_000_000, 394_000_000, 494_000_000, 798_000_000, 870_000_000, 1_600_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            source.GenerateBefore(boundary, 750, 200);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(3_000_000_000, 750, 200); var b = restored.GenerateBefore(3_000_000_000, 750, 200);
            Check.That(a.Count == b.Count && a.Zip(b).All(pair => pair.First.Tick == pair.Second.Tick && pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)), "restore across P/QRS overlap and independent boundaries");
        }
    }
}
