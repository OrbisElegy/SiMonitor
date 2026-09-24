// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SinusArrestSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SinusArrestRetainsBoundedPerfusionThroughPause), SinusArrestRetainsBoundedPerfusionThroughPause),
        new(nameof(SinusArrestMovesConductedEventsTogether), SinusArrestMovesConductedEventsTogether),
        new(nameof(SinusArrestPreservesMorphologyAndRecovery), SinusArrestPreservesMorphologyAndRecovery),
        new(nameof(SinusArrestBoundsLateLookupAndRejectsConflicts), SinusArrestBoundsLateLookupAndRejectsConflicts),
    ];
    private static void SinusArrestMovesConductedEventsTogether()
    {
        var events = RegularPhysiologyTimeline.Start(SinusArrestReference.CreatePlan()).AdvanceBefore(8_800_000_000, 100);
        long[] pTimes = [0, 800_000_000, 1_600_000_000, 3_600_000_000, 4_400_000_000, 5_200_000_000, 6_000_000_000, 8_000_000_000];
        foreach (var (kind, offset) in new[] { (PhysiologyCycleEventKind.AtrialElectrical, 0L), (PhysiologyCycleEventKind.AtrialMechanical, 80_000_000L), (PhysiologyCycleEventKind.VentricularElectrical, 160_000_000L), (PhysiologyCycleEventKind.VentricularMechanical, 240_000_000L) })
        {
            var selected = events.Where(e => e.Kind == kind).ToArray();
            Check.That(selected.Select(e => e.SimTimeNs).SequenceEqual(pTimes.Select(t => t + offset)) && selected.Select(e => e.CycleIndex).SequenceEqual(Enumerable.Range(0, 8).Select(i => (ulong)i)), "all cardiac events share indexed irregular cycles and fixed delays");
        }
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.InspirationStart).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 0, 3_750_000_000, 7_500_000_000 }), "breathing clock remains independent");
    }
    private static void SinusArrestPreservesMorphologyAndRecovery()
    {
        var plan = SinusArrestReference.CreatePlan();
        var electrodes = SinusArrestReference.CreateElectrodes();
        var normal = TextbookElectrodeReference.CreateElectrodes(timing: SinusArrestReference.Timing);
        Check.That(electrodes.Zip(normal).All(p => p.First.Bands.Zip(p.Second.Bands).All(b => b.First.TableQ32.SequenceEqual(b.Second.TableQ32))), "sinus P and normal QRS/T remain unchanged");
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(8_800_000_000, 2200, 100);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SinusArrestReference.CreateLeadIIBands()).GenerateBefore(8_800_000_000, 2200, 100);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII shares electrode projection");
        Check.That(full.Skip(550).Take(350)
            .All(p => p.MicrovoltValues.All(v => v == 0)), "pause removes P and QRS-T across all twelve leads");
        foreach (long boundary in new long[] { 158_000_000, 800_000_000, 1_758_000_000, 2_800_000_000, 3_600_000_000, 4_400_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes); source.GenerateBefore(boundary, 2200, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(8_800_000_000, 2200, 100); var b = restored.GenerateBefore(8_800_000_000, 2200, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "irregular phase survives recovery");
        }
    }
    private static void SinusArrestBoundsLateLookupAndRejectsConflicts()
    {
        var plan = SinusArrestReference.CreatePlan();
        long late = 88_000_000_000_000;
        var near = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(4_400_000_000, 100).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        var far = RegularPhysiologyTimeline.Restore(new(plan, late)).AdvanceBefore(late + 4_400_000_000, 100).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        Check.That(far.Select(e => e.SimTimeNs - late).SequenceEqual(near.Select(e => e.SimTimeNs)), "late lookup preserves phase without epoch scan");
        var source = RegularPhysiologyTimeline.Start(plan); var before = source.CaptureState();
        try { source.AdvanceBefore(4_400_000_000, 1); throw new InvalidOperationException("Event limit accepted."); } catch (PhysiologyTimelineException) { }
        Check.That(source.CaptureState() == before, "event rejection atomic");
        Check.That(source.AdvanceBefore(160_000_000, 100).All(e => e.Kind != PhysiologyCycleEventKind.VentricularElectrical), "half-open QRS boundary");
        Check.That(source.AdvanceBefore(160_000_001, 100).Single().Kind == PhysiologyCycleEventKind.VentricularElectrical, "QRS occurs at exact boundary");
        foreach (var bad in new[] { plan with { HeartPeriodNs = 600_000_000 }, plan with { VentricularConductionRatio = 2 }, plan with { MechanicalEveryCycles = 2 }, plan with { IndependentVentricularPeriodNs = 800_000_000 }, plan with { MechanicalAfterCycles = 2 } })
        {
            try { RegularPhysiologyTimeline.Start(bad); } catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Unsupported sinus arrest configuration accepted.");
        }
    }
    private static void SinusArrestRetainsBoundedPerfusionThroughPause()
    {
        var plan = SinusArrestReference.CreatePlan();
        var pleth = PlethRunoffSource.Create(plan, SinusArrestPerfusionReference.Pleth);
        var isolatedPlan = plan with
        {
            ConductionPattern = AvConductionPattern.FixedPr,
            VentricularMechanicalEnabled = false,
            MechanicalAfterCycles = 1
        };
        var isolated = PlethRunoffSource.Create(isolatedPlan, SinusArrestPerfusionReference.Pleth);
        long expected = new long[] { 0, 800_000_000, 1_600_000_000 }
            .Sum(offset => isolated.EvaluateAt(3_500_000_000 - offset));
        Check.That(Math.Abs(pleth.EvaluateAt(3_500_000_000) - expected) <= 3 && expected > 0,
            "pause retains all earlier optical tails without new ejection");
        Check.That(pleth.MaximumHistoryEvents < 100, "history bound excludes elapsed runtime");
        foreach (var pressure in new[] { SinusArrestPerfusionReference.Arterial, SinusArrestPerfusionReference.Pulmonary })
        {
            var source = VascularPressureSource.Create(plan, pressure);
            Check.That(source.EvaluateAt(2_800_000_000) > source.EvaluateAt(3_500_000_000) &&
                source.EvaluateAt(3_500_000_000) > pressure.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One,
                "pressure falls during missing ejections without an artificial floor reset");
            for (long phase = 0; phase < 4_400_000_000; phase += 20_000_000)
                Check.That(Math.Abs(source.EvaluateAt(440_000_000_000 + phase) -
                    source.EvaluateAt(88_000_000_000_000 + phase)) <= FixedPointMath.Q32One,
                    "late pressure lookup matches settled pause phase");
        }
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(4_400_000_000, 100);
        var cvp = SinusArrestPerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var mechanical = EventWaveformComposition.Restore(new(cvp.Bands.Take(5).ToArray(), events));
        var resp = new RespirationPlan(1000, 200).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var artifact = EventWaveformComposition.Restore(new([resp.Bands[1]], events));
        for (long t = 2_800_000_000; t < 3_600_000_000; t += 4_000_000)
            Check.That(mechanical.EvaluateAt(t) == 0 && artifact.EvaluateAt(t) == 0,
                "pause has no residual CVP cardiac components or invented Resp cardiac oscillation");
    }

}
