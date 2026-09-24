// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SinusArrhythmiaSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SinusArrhythmiaMovesConductedEventsTogether), SinusArrhythmiaMovesConductedEventsTogether),
        new(nameof(SinusArrhythmiaPreservesMorphologyAndRecovery), SinusArrhythmiaPreservesMorphologyAndRecovery),
        new(nameof(SinusArrhythmiaBoundsLateLookupAndRejectsConflicts), SinusArrhythmiaBoundsLateLookupAndRejectsConflicts),
    ];
    private static void SinusArrhythmiaMovesConductedEventsTogether()
    {
        var events = RegularPhysiologyTimeline.Start(SinusArrhythmiaReference.CreatePlan()).AdvanceBefore(6_400_000_000, 100);
        long[] pTimes = [0, 800_000_000, 1_800_000_000, 2_400_000_000, 3_200_000_000, 4_000_000_000, 5_000_000_000, 5_600_000_000];
        foreach (var (kind, offset) in new[] { (PhysiologyCycleEventKind.AtrialElectrical, 0L), (PhysiologyCycleEventKind.AtrialMechanical, 80_000_000L), (PhysiologyCycleEventKind.VentricularElectrical, 160_000_000L), (PhysiologyCycleEventKind.VentricularMechanical, 240_000_000L) })
        {
            var selected = events.Where(e => e.Kind == kind).ToArray();
            Check.That(selected.Select(e => e.SimTimeNs).SequenceEqual(pTimes.Select(t => t + offset)) && selected.Select(e => e.CycleIndex).SequenceEqual(Enumerable.Range(0, 8).Select(i => (ulong)i)), "all cardiac events share indexed irregular cycles and fixed delays");
        }
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.InspirationStart).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 0, 3_750_000_000 }), "breathing clock remains independent");
    }
    private static void SinusArrhythmiaPreservesMorphologyAndRecovery()
    {
        var plan = SinusArrhythmiaReference.CreatePlan();
        var electrodes = SinusArrhythmiaReference.CreateElectrodes();
        var normal = TextbookElectrodeReference.CreateElectrodes(timing: SinusArrhythmiaReference.Timing);
        Check.That(electrodes.Zip(normal).All(p => p.First.Bands.Zip(p.Second.Bands).All(b => b.First.TableQ32.SequenceEqual(b.Second.TableQ32))), "sinus P and normal QRS/T remain unchanged");
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(6_400_000_000, 1600, 100);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SinusArrhythmiaReference.CreateLeadIIBands()).GenerateBefore(6_400_000_000, 1600, 100);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII shares electrode projection");
        foreach (long boundary in new long[] { 158_000_000, 800_000_000, 1_798_000_000, 1_960_000_000, 2_400_000_000, 3_200_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes); source.GenerateBefore(boundary, 1600, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(6_400_000_000, 1600, 100); var b = restored.GenerateBefore(6_400_000_000, 1600, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "irregular phase survives recovery");
        }
    }
    private static void SinusArrhythmiaBoundsLateLookupAndRejectsConflicts()
    {
        var plan = SinusArrhythmiaReference.CreatePlan();
        long late = 86_400_000_000_000;
        var near = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_200_000_000, 100).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        var far = RegularPhysiologyTimeline.Restore(new(plan, late)).AdvanceBefore(late + 3_200_000_000, 100).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        Check.That(far.Select(e => e.SimTimeNs - late).SequenceEqual(near.Select(e => e.SimTimeNs)), "late lookup preserves phase without epoch scan");
        var source = RegularPhysiologyTimeline.Start(plan); var before = source.CaptureState();
        try { source.AdvanceBefore(3_200_000_000, 1); throw new InvalidOperationException("Event limit accepted."); } catch (PhysiologyTimelineException) { }
        Check.That(source.CaptureState() == before, "event rejection atomic");
        Check.That(source.AdvanceBefore(160_000_000, 100).All(e => e.Kind != PhysiologyCycleEventKind.VentricularElectrical), "half-open QRS boundary");
        Check.That(source.AdvanceBefore(160_000_001, 100).Single().Kind == PhysiologyCycleEventKind.VentricularElectrical, "QRS occurs at exact boundary");
        foreach (var bad in new[] { plan with { HeartPeriodNs = 600_000_000 }, plan with { VentricularConductionRatio = 2 }, plan with { MechanicalEveryCycles = 2 }, plan with { IndependentVentricularPeriodNs = 800_000_000 }, plan with { MechanicalAfterCycles = 2 } })
        {
            try { RegularPhysiologyTimeline.Start(bad); } catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Unsupported sinus arrhythmia configuration accepted.");
        }
    }
}
