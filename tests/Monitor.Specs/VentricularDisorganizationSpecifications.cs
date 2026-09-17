// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentricularDisorganizationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(DisorganizedVentriclesHaveNoEffectiveEjection), DisorganizedVentriclesHaveNoEffectiveEjection),
        new(nameof(FlutterAndFibrillationPreserveShapeAndRecovery), FlutterAndFibrillationPreserveShapeAndRecovery),
        new(nameof(DisorganizedModesRejectConflictsAtomically), DisorganizedModesRejectConflictsAtomically),
    ];
    private static AvConductionPattern[] Patterns => [AvConductionPattern.VentricularFlutterIllustration, AvConductionPattern.VentricularFibrillationCoarseIllustration, AvConductionPattern.VentricularFibrillationFineIllustration];
    private static void DisorganizedVentriclesHaveNoEffectiveEjection()
    {
        foreach (var pattern in Patterns)
        {
            var plan = VentricularDisorganizationReference.CreatePlan(pattern);
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(20_000_000_000, 100);
            Check.That(events.All(e => e.Kind is PhysiologyCycleEventKind.VentricularDisorganizationSegment or PhysiologyCycleEventKind.InspirationStart or PhysiologyCycleEventKind.ExpirationStart), "no organized atrial/ventricular events");
            var segments = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularDisorganizationSegment).ToArray();
            long duration = VentricularDisorganizationReference.SegmentDurationNs(pattern);
            Check.That(segments.Length > 1 && segments.Select((e, i) => e.SimTimeNs == i * duration).All(v => v), "continuous source segments without gaps");
            var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000));
            Check.That(pressure.EvaluateAt(0) > pressure.EvaluateAt(1_000_000_000) && pressure.EvaluateAt(1_000_000_000) > pressure.EvaluateAt(6_000_000_000) && pressure.EvaluateAt(6_000_000_000) > pressure.EvaluateAt(20_000_000_000), "pressure decays without ejection, not immediate zero");
        }
    }
    private static void FlutterAndFibrillationPreserveShapeAndRecovery()
    {
        List<int[]> leads = [];
        foreach (var pattern in Patterns)
        {
            var plan = VentricularDisorganizationReference.CreatePlan(pattern);
            var electrodes = VentricularDisorganizationReference.CreateElectrodes(pattern);
            Check.That(electrodes.All(e => e.Bands.Count == 1 && e.Bands[0].Trigger == PhysiologyCycleEventKind.VentricularDisorganizationSegment), "no residual P/QRS/T bands");
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            var samples = source.GenerateBefore(4_000_000_000, 1000, 100);
            leads.Add(samples.Select(s => (int)s.MicrovoltValues[1]).ToArray());
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularDisorganizationReference.CreateLeadIIBands(pattern)).GenerateBefore(4_000_000_000, 1000, 100);
            Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II shares projected electrode source");
            Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities preserved");
            source.GenerateBefore(16_380_000_000, 3095, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var tail = source.GenerateBefore(17_200_000_000, 205, 100);
            Check.That(tail.Zip(restored.GenerateBefore(17_200_000_000, 205, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "tile-boundary checkpoint recovery");
        }
        Check.That(leads[0].Max() > 990 && leads[0].Min() < -990 && leads[0].Take(875).SequenceEqual(leads[0].Skip(125)), "large regular flutter at 240/min, two periods equal 125 samples");
        int[] peaks = Enumerable.Range(1, 998).Where(i => leads[1][i] > 0 && leads[1][i] >= leads[1][i - 1] && leads[1][i] > leads[1][i + 1]).ToArray();
        Check.That(peaks.Select(i => leads[1][i]).Distinct().Count() > 10 && peaks.Zip(peaks.Skip(1)).Select(p => p.Second - p.First).Distinct().Count() > 5, "VF varies in amplitude and spacing");
        Check.That(leads[1].Zip(leads[2]).All(p => Math.Abs(p.First - 5 * p.Second) <= 3), "fine VF scales to one fifth with acquisition rounding");
    }
    private static void DisorganizedModesRejectConflictsAtomically()
    {
        foreach (var pattern in Patterns)
        {
            var plan = VentricularDisorganizationReference.CreatePlan(pattern);
            foreach (var invalid in new[] { plan with { VentricularMechanicalEnabled = true }, plan with { CardiacActivity = CardiacActivity.AtrialAndVentricular }, plan with { MechanicalAfterCycles = 2 }, plan with { MechanicalEveryCycles = 2 }, plan with { VentricularConductionRatio = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 } })
            {
                try { RegularPhysiologyTimeline.Start(invalid); }
                catch (PhysiologyTimelineException) { continue; }
                throw new InvalidOperationException("Conflicting disorganized rhythm accepted.");
            }
            var timeline = RegularPhysiologyTimeline.Start(plan);
            var before = timeline.CaptureState();
            try { timeline.AdvanceBefore(20_000_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
            catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == before, "event budget failure atomic"); }
            long boundary = VentricularDisorganizationReference.SegmentDurationNs(pattern);
            var head = timeline.AdvanceBefore(boundary, 100);
            var tail = RegularPhysiologyTimeline.Restore(timeline.CaptureState()).AdvanceBefore(20_000_000_000, 100);
            Check.That(head.Concat(tail).SequenceEqual(RegularPhysiologyTimeline.Start(plan).AdvanceBefore(20_000_000_000, 100)), "half-open boundary emits segment exactly once");
            var late = RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 1_000_000_000)).AdvanceBefore(long.MaxValue, 10);
            Check.That(late.All(e => e.SimTimeNs >= long.MaxValue - 1_000_000_000), "indexed near-max query without origin replay");
        }
    }
}
