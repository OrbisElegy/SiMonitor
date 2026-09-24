// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VtCaptureSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(VtCaptureReplacesElectricalAndMechanicalDeadlines), VtCaptureReplacesElectricalAndMechanicalDeadlines),
        new(nameof(VtCaptureUsesConductedContourAndRestoresWithinBeat), VtCaptureUsesConductedContourAndRestoresWithinBeat),
        new(nameof(VtCapturePerfusionTracksEarlierEjectionWithBoundedHistory), VtCapturePerfusionTracksEarlierEjectionWithBoundedHistory),
    ];
    private static void VtCaptureReplacesElectricalAndMechanicalDeadlines()
    {
        var plan = VentricularTachycardiaReference.CreatePlan(true);
        foreach (var invalid in new[] { plan with { IndependentVentricularPeriodNs = 400_000_000 }, plan with { HeartPeriodNs = 600_000_000 }, plan with { ConductionPattern = AvConductionPattern.FixedPr } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Invalid capture grid accepted.");
        }
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(18_000_000_000, 200);
        foreach (var kind in new[] { PhysiologyCycleEventKind.VentricularElectrical, PhysiologyCycleEventKind.VentricularMechanical })
        {
            var beats = events.Where(e => e.Kind == kind).ToArray();
            long offset = kind == PhysiologyCycleEventKind.VentricularElectrical ? 120_000_000 : 200_000_000;
            Check.That(beats.Length == 48 && beats.Select(e => e.SimTimeNs).SequenceEqual(Enumerable.Range(0, 48).Select(i => offset + i * 375_000_000L - (i % 32 == 11 ? 45_000_000 : 0))), "one replacement per group, next ectopic deadline unchanged");
            Check.That(beats[11].SimTimeNs - beats[10].SimTimeNs == 330_000_000 && beats[12].SimTimeNs - beats[11].SimTimeNs == 420_000_000, "shortened then lengthened RR");
        }
        Check.That(events.Any(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical && e.SimTimeNs == 4_000_000_000), "P precedes capture by200ms");
        var timeline = RegularPhysiologyTimeline.Start(plan);
        foreach (long boundary in new long[] { 4_200_000_000, 4_200_000_001, 4_245_000_000, 4_280_000_001, 4_325_000_001, 12_000_000_000, 16_200_000_001, 18_000_000_000 })
        {
            long start = timeline.CaptureState().CursorSimTimeNs;
            var restored = RegularPhysiologyTimeline.Restore(timeline.CaptureState());
            var actual = timeline.AdvanceBefore(boundary, 200);
            Check.That(actual.SequenceEqual(events.Where(e => e.SimTimeNs >= start && e.SimTimeNs < boundary)) && actual.SequenceEqual(restored.AdvanceBefore(boundary, 200)), "half-open boundaries and restore do not duplicate advanced event");
        }
        var gated = plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 11, MechanicalDurationCycles = 1 };
        var mechanical = RegularPhysiologyTimeline.Start(gated).AdvanceBefore(5_000_000_000, 100).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        Check.That(mechanical.All(e => e.CycleIndex != 11) && mechanical.Any(e => e.CycleIndex == 12), "gating selects original cycle ordinals");
        var strided = RegularPhysiologyTimeline.Start(plan with { MechanicalEveryCycles = 3 }).AdvanceBefore(18_000_000_000, 200);
        Check.That(strided.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).SequenceEqual(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical && e.CycleIndex % 3 == 0)), "mechanical stride retains shifted schedule");
        var atomic = RegularPhysiologyTimeline.Start(plan);
        var before = atomic.CaptureState();
        try { atomic.AdvanceBefore(5_000_000_000, 1); throw new InvalidOperationException("Event limit accepted."); }
        catch (PhysiologyTimelineException) { }
        Check.That(before == atomic.CaptureState(), "rejected event budget leaves cursor unchanged");
        long late = 86_400_000_000_000;
        var lateEvents = RegularPhysiologyTimeline.Restore(new(plan, late)).AdvanceBefore(late + 6_000_000_000, 60);
        Check.That(lateEvents.Select(e => (e.SimTimeNs - late, e.Kind)).SequenceEqual(events.Where(e => e.SimTimeNs < 6_000_000_000).Select(e => (e.SimTimeNs, e.Kind))), "late reconstruction indexes directly into repeated group");
    }
    private static void VtCaptureUsesConductedContourAndRestoresWithinBeat()
    {
        var plan = VentricularTachycardiaReference.CreatePlan(true);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(5_600_000_000, 100);
        var capture = VentricularTachycardiaReference.CreateElectrodes(capture: true);
        var normal = TextbookElectrodeReference.CreateElectrodes(timing: VentricularTachycardiaReference.Timing with { QrsDurationNs = 80_000_000 });
        var vt = VentricularTachycardiaReference.CreateElectrodes();
        bool differs = false;
        for (int i = 0; i < capture.Count; i++)
        {
            var a = EventWaveformComposition.Restore(new(capture[i].Bands, events));
            var n = EventWaveformComposition.Restore(new(normal[i].Bands, events));
            var v = EventWaveformComposition.Restore(new(vt[i].Bands, events));
            for (long t = 0; t < 5_600_000_000; t += 4_000_000)
            {
                bool selected = t >= 4_200_000_000 && t < 4_475_000_000;
                Check.That(a.EvaluateAt(t) == (selected ? n : v).EvaluateAt(t), "pure conducted80ms contour replaces VT only in capture slot");
                differs |= selected && a.EvaluateAt(t) != v.EvaluateAt(t);
            }
        }
        Check.That(differs, "capture is not unchanged broad VT");
        foreach (bool fusion in new[] { false, true })
        {
            var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateElectrodes(fusion, true)).GenerateBefore(5_600_000_000, 1400, 200);
            var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateLeadIIBands(fusion, true)).GenerateBefore(5_600_000_000, 1400, 200);
            string scenario = fusion ? "VT capture with fusion" : "VT capture";
            EcgProjectionChecks.RequireMatchingLeadII(full, ii, 1400, scenario);
            foreach (long t in new long[] { 4_200_000_000, 4_240_000_000, 4_280_000_000, 4_475_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateElectrodes(fusion, true));
                source.GenerateBefore(t, 1400, 200);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                var expected = full.Where(sample => sample.Tick.SimTimeNs >= t).ToArray();
                var actual = source.GenerateBefore(5_600_000_000, 1400, 200);
                var recovered = restored.GenerateBefore(5_600_000_000, 1400, 200);
                EcgProjectionChecks.RequireMatchingSamples(expected, actual, scenario);
                EcgProjectionChecks.RequireMatchingSamples(expected, recovered, scenario);
            }
        }
    }
    private static void VtCapturePerfusionTracksEarlierEjectionWithBoundedHistory()
    {
        var plan = VentricularTachycardiaReference.CreatePlan(true);
        var pleth = PlethRunoffSource.Create(plan, VtPerfusionReference.Pleth);
        var ordinaryPlan = VentricularTachycardiaReference.CreatePlan();
        var ordinary = PlethRunoffSource.Create(ordinaryPlan, VtPerfusionReference.Pleth);
        PlethRunoffSource Single(long anchor) => PlethRunoffSource.Create(ordinaryPlan with
        { EpochAnchorSimTimeNs = anchor, VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 }, VtPerfusionReference.Pleth);
        var early = Single(4_080_000_000); var old = Single(4_125_000_000);
        for (long t = 4_160_000_000; t < 5_600_000_000; t += 8_000_000)
            Check.That(Math.Abs(pleth.EvaluateAt(t) - ordinary.EvaluateAt(t) - early.EvaluateAt(t) + old.EvaluateAt(t)) <= 3, "optical output replaces one delayed pulse rather than adding a second ejection");
        Check.That(pleth.MaximumHistoryEvents < 100, "finite optical history at330ms minimum RR");
        foreach (var pressure in new[] { VtPerfusionReference.Arterial, VtPerfusionReference.Pulmonary })
        {
            var source = VascularPressureSource.Create(plan, pressure);
            Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + 329_999_999) / 330_000_000 < 700, "bounded pressure event budget");
            for (long phase = 0; phase < 12_000_000_000; phase += 250_000_000)
            {
                long a = source.EvaluateAt(300_000_000_000 + phase), b = source.EvaluateAt(86_400_000_000_000 + phase);
                Check.That(Math.Abs(a - b) <= FixedPointMath.Q32One && a > 0 && a < short.MaxValue * FixedPointMath.Q32One, "pressure remains bounded and group-periodic at late times");
            }
        }
        _ = VtPerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var resp = new RespirationPlan(1000, 200).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(resp.Bands[1].DurationNs == 330_000_000, "cardiac artifact fits shortest capture RR");
    }
}
