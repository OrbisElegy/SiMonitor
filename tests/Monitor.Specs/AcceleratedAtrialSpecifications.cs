// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AcceleratedAtrialSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AcceleratedAtrialRhythmReplacesEverySinusPWithoutPrematurity), AcceleratedAtrialRhythmReplacesEverySinusPWithoutPrematurity),
        new(nameof(AcceleratedAtrialRhythmPreservesConductionProjectionAndRecovery), AcceleratedAtrialRhythmPreservesConductionProjectionAndRecovery),
    ];
    private static void AcceleratedAtrialRhythmReplacesEverySinusPWithoutPrematurity()
    {
        var plan = AcceleratedAtrialReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 100);
        foreach (var (kind, offset) in new[] {
            (PhysiologyCycleEventKind.AtrialElectrical, 0L), (PhysiologyCycleEventKind.AtrialMechanical, 80_000_000L),
            (PhysiologyCycleEventKind.VentricularElectrical, 160_000_000L), (PhysiologyCycleEventKind.VentricularMechanical, 240_000_000L) })
            Check.That(events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs)
                .SequenceEqual(Enumerable.Range(0, 5).Select(i => offset + i * 600_000_000L)), "one event per regular cycle, fixed conducted PR and mechanical delay");
        Check.That(!events.Any(e => e.Kind == PhysiologyCycleEventKind.PrematureAtrialElectrical), "no PAC event, coupling interval or pause in established rhythm");
        var electrodes = AcceleratedAtrialReference.CreateElectrodes();
        var sinus = TextbookElectrodeReference.CreateElectrodes(timing: AcceleratedAtrialReference.Timing);
        var pac = PrematureAtrialReference.CreateElectrodes();
        for (int i = 0; i < electrodes.Count; i++)
        {
            Check.That(electrodes[i].Bands.Count == 3, "replace P, do not append an additional atrial source");
            var p = electrodes[i].Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical);
            Check.That(p.DurationNs == 80_000_000 && p.TableQ32.SequenceEqual(pac[i].Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.PrematureAtrialElectrical).TableQ32), "shared authored ectopic atrial contour");
            Check.That(electrodes[i].Bands.Skip(1).Zip(sinus[i].Bands.Skip(1)).All(pair => pair.First.DurationNs == pair.Second.DurationNs && pair.First.TableQ32.SequenceEqual(pair.Second.TableQ32)), "QRS and repolarization keep normal morphology");
        }
        var pOnly = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
            AcceleratedAtrialReference.CreateLeadIIBands().Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray())
            .GenerateBefore(3_000_000_000, 750, 100);
        for (int beat = 0; beat < 5; beat++)
        {
            short[] samples = pOnly.Skip(beat * 150).Take(150).Select(s => s.NormalizedValue).ToArray();
            Check.That(samples.Take(20).Min() < -100 && samples.Skip(20).All(v => v == 0), "inverted leadII P-prime once per cycle, no residual sinus P");
        }
    }
    private static void AcceleratedAtrialRhythmPreservesConductionProjectionAndRecovery()
    {
        var plan = AcceleratedAtrialReference.CreatePlan();
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AcceleratedAtrialReference.CreateElectrodes()).GenerateBefore(3_000_000_000, 750, 100);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AcceleratedAtrialReference.CreateLeadIIBands()).GenerateBefore(3_000_000_000, 750, 100);
        Check.That(full.Count == 750 && full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII projection agrees");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities preserved");
        foreach (long boundary in new long[] { 0, 42_000_000, 80_000_000, 160_000_000, 200_000_000, 558_000_000, 600_000_000, 1_160_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AcceleratedAtrialReference.CreateElectrodes());
            source.GenerateBefore(boundary, 750, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(3_000_000_000, 750, 100); var b = restored.GenerateBefore(3_000_000_000, 750, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "recovery inside P/QRS/T retains cycle phase");
        }
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(3_000_000_000, 1); throw new InvalidOperationException("Event budget accepted."); }
        catch (PhysiologyTimelineException) { }
        Check.That(timeline.CaptureState() == before, "event budget rejection is atomic");
        Check.That(timeline.AdvanceBefore(160_000_000, 100).All(e => e.Kind != PhysiologyCycleEventKind.VentricularElectrical), "QRS excluded at half-open boundary");
        Check.That(timeline.AdvanceBefore(160_000_001, 100).Single().Kind == PhysiologyCycleEventKind.VentricularElectrical, "QRS occurs exactly160ms after P-prime");
    }
}
