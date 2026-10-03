// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EctopicAtrialSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(EctopicAtrialRhythmsReplaceSinusPWithoutPrematurity), EctopicAtrialRhythmsReplaceSinusPWithoutPrematurity),
        new(nameof(EctopicAtrialRhythmsPreserveConductionAndProjection), EctopicAtrialRhythmsPreserveConductionAndProjection),
    ];

    private sealed record RhythmCase(string Name, long PeriodNs, RegularPhysiologyPlan Plan, EcgCycleTiming Timing,
        IReadOnlyList<ElectrodeWaveformPlan> Electrodes, IReadOnlyList<EventWaveformBand> LeadII);

    private static RhythmCase[] Cases =>
    [
        new("accelerated atrial", 600_000_000, AcceleratedAtrialReference.CreatePlan(), AcceleratedAtrialReference.Timing,
            AcceleratedAtrialReference.CreateElectrodes(), AcceleratedAtrialReference.CreateLeadIIBands()),
        new("atrial escape", 1_200_000_000, AtrialEscapeReference.CreatePlan(), AtrialEscapeReference.Timing,
            AtrialEscapeReference.CreateElectrodes(), AtrialEscapeReference.CreateLeadIIBands()),
    ];

    private static void EctopicAtrialRhythmsReplaceSinusPWithoutPrematurity()
    {
        var pac = PrematureAtrialReference.CreateElectrodes();
        foreach (var item in Cases)
        {
            long endNs = item.PeriodNs * 5;
            int samplesPerCycle = checked((int)(item.PeriodNs / 4_000_000));
            var events = RegularPhysiologyTimeline.Start(item.Plan).AdvanceBefore(endNs, 100);
            foreach (var (kind, offsetNs) in new[]
            {
                (PhysiologyCycleEventKind.AtrialElectrical, 0L), (PhysiologyCycleEventKind.AtrialMechanical, 80_000_000L),
                (PhysiologyCycleEventKind.VentricularElectrical, 160_000_000L), (PhysiologyCycleEventKind.VentricularMechanical, 240_000_000L)
            })
                Check.That(events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs)
                    .SequenceEqual(Enumerable.Range(0, 5).Select(i => offsetNs + i * item.PeriodNs)),
                    $"{item.Name}: one event per regular cycle, fixed conducted PR and mechanical delay");
            Check.That(!events.Any(e => e.Kind == PhysiologyCycleEventKind.PrematureAtrialElectrical),
                $"{item.Name}: no PAC event, coupling interval or pause in established rhythm");
            var sinus = TextbookElectrodeReference.CreateElectrodes(timing: item.Timing);
            for (int i = 0; i < item.Electrodes.Count; i++)
            {
                Check.That(item.Electrodes[i].Bands.Count == 3, $"{item.Name}: replace P without appending another atrial source");
                var p = item.Electrodes[i].Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical);
                Check.That(p.DurationNs == 80_000_000 && p.TableQ32.SequenceEqual(pac[i].Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.PrematureAtrialElectrical).TableQ32),
                    $"{item.Name}: shared authored ectopic atrial contour");
                Check.That(item.Electrodes[i].Bands.Skip(1).Zip(sinus[i].Bands.Skip(1)).All(pair =>
                    pair.First.DurationNs == pair.Second.DurationNs && pair.First.TableQ32.SequenceEqual(pair.Second.TableQ32)),
                    $"{item.Name}: QRS and repolarization retain normal morphology");
            }
            var pOnly = PhysiologySignalGenerator.Start(item.Plan, "AcqECGMonitor250@1", 1,
                item.LeadII.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray())
                .GenerateBefore(endNs, samplesPerCycle * 5, 100);
            for (int beat = 0; beat < 5; beat++)
            {
                short[] samples = pOnly.Skip(beat * samplesPerCycle).Take(samplesPerCycle).Select(s => s.NormalizedValue).ToArray();
                Check.That(samples.Take(20).Min() < -100 && samples.Skip(20).All(v => v == 0),
                    $"{item.Name}: inverted lead II P-prime once per cycle, no residual sinus P");
            }
        }
    }

    private static void EctopicAtrialRhythmsPreserveConductionAndProjection()
    {
        foreach (var item in Cases)
        {
            long endNs = item.PeriodNs * 5;
            int sampleCount = checked((int)(endNs / 4_000_000));
            var full = ElectrodeSignalGenerator.Start(item.Plan, "AcqECGMonitor250@1", 1, item.Electrodes).GenerateBefore(endNs, sampleCount, 100);
            var ii = PhysiologySignalGenerator.Start(item.Plan, "AcqECGMonitor250@1", 1, item.LeadII).GenerateBefore(endNs, sampleCount, 100);
            Check.That(full.Count == sampleCount && full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1),
                $"{item.Name}: monitor II projection agrees");
            Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1),
                $"{item.Name}: limb identities preserved");
            var timeline = RegularPhysiologyTimeline.Start(item.Plan);
            var before = timeline.CaptureState();
            try { timeline.AdvanceBefore(endNs, 1); throw new InvalidOperationException($"{item.Name}: event budget accepted."); }
            catch (PhysiologyTimelineException) { }
            Check.That(timeline.CaptureState() == before, $"{item.Name}: event budget rejection is atomic");
            Check.That(timeline.AdvanceBefore(160_000_000, 100).All(e => e.Kind != PhysiologyCycleEventKind.VentricularElectrical),
                $"{item.Name}: QRS excluded at half-open boundary");
            Check.That(timeline.AdvanceBefore(160_000_001, 100).Single().Kind == PhysiologyCycleEventKind.VentricularElectrical,
                $"{item.Name}: QRS occurs exactly 160 ms after P-prime");
        }
    }
}
