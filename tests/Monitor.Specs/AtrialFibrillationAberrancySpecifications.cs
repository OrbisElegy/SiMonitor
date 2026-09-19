// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AtrialFibrillationAberrancySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AfAberrancyReplacesOnlySelectedQrsStT), AfAberrancyReplacesOnlySelectedQrsStT),
        new(nameof(AfAberrancyRestoresAndRejectsInvalidSelection), AfAberrancyRestoresAndRejectsInvalidSelection),
    ];
    private static void AfAberrancyReplacesOnlySelectedQrsStT()
    {
        foreach (bool fine in new[] { false, true })
        {
            var plan = AtrialFibrillationReference.CreatePlan(fine);
            var ordinary = AtrialFibrillationReference.CreateElectrodes(fine);
            var mixed = AtrialFibrillationReference.CreateElectrodes(fine, true);
            var wide = RightBundleBlockReference.CreateElectrodes();
            var allWide = ordinary.Select((e, i) => e with
            {
                Bands = e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialFibrillationSegment)
                .Concat(wide[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical)).ToArray()
            }).ToArray();
            var normalSamples = Generate(ordinary); var mixedSamples = Generate(mixed); var wideSamples = Generate(allWide);
            var electrical = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(14_000_000_000, 200)
                .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            Check.That(electrical.Where(e => AtrialFibrillationReference.IsLongShortBeat(e.CycleIndex)).Select(e => e.CycleIndex).SequenceEqual([16UL]), "first selected beat retains original AF ordinal");
            for (int i = 0; i < mixedSamples.Count; i++)
            {
                long t = mixedSamples[i].Tick.SimTimeNs;
                bool selected = t is >= 12_966_000_000 and < 13_366_000_000;
                Check.That(mixedSamples[i].MicrovoltValues.SequenceEqual((selected ? wideSamples : normalSamples)[i].MicrovoltValues),
                    "only selected QRS/ST/T replaced; f and ordinary beats unchanged");
            }
            Check.That(Enumerable.Range(3262, 13).Any(i => Math.Abs(mixedSamples[i].MicrovoltValues[6] - normalSamples[i].MicrovoltValues[6]) > 300),
                "V1 late R-prime persists beyond ordinary80ms QRS");
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AtrialFibrillationReference.CreateLeadIIBands(fine, true))
                .GenerateBefore(14_000_000_000, 3500, 200);
            Check.That(mixedSamples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II shares projected morphology");
            Check.That(mixedSamples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities hold through wide beat");
            IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> e) =>
                ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, e).GenerateBefore(14_000_000_000, 3500, 200);
        }
    }
    private static void AfAberrancyRestoresAndRejectsInvalidSelection()
    {
        var plan = AtrialFibrillationReference.CreatePlan();
        var electrodes = AtrialFibrillationReference.CreateElectrodes(aberrancy: true);
        foreach (long boundary in new[] { 12_964_000_000L, 13_052_000_000, 13_300_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            source.GenerateBefore(boundary, 3500, 200);
            var state = source.CaptureState(); var restored = ElectrodeSignalGenerator.Restore(state);
            var expected = source.GenerateBefore(14_400_000_000, 400, 100);
            var actual = restored.GenerateBefore(14_400_000_000, 400, 100);
            Check.That(expected.Count == actual.Count && expected.Zip(actual).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore preserves selector during wide QRS and T");
            string before = JsonSerializer.Serialize(restored.CaptureState());
            Reject(() => restored.GenerateBefore(16_000_000_000, 1, 1));
            Check.That(before == JsonSerializer.Serialize(restored.CaptureState()), "budget failure leaves all cursors unchanged");
        }
        var sinus = plan with { ConductionPattern = AvConductionPattern.FixedPr };
        Reject(() => ElectrodeSignalGenerator.Start(sinus, "AcqECGMonitor250@1", 1, electrodes));
        Reject(() => PhysiologySignalGenerator.Start(sinus, "AcqECGMonitor250@1", 1, AtrialFibrillationReference.CreateLeadIIBands(aberrancy: true)));
        var band = electrodes[0].Bands.First(b => b.AfBeatSelection is not null);
        foreach (var invalid in new[] { band with { AfBeatSelection = (AtrialFibrillationBeatSelection)99 },
            band with { Trigger = PhysiologyCycleEventKind.VentricularMechanical }, band with { VentricularCycles = new(2, 1) } })
        { Reject(() => EventWaveformComposition.Restore(new([invalid], []))); }
        foreach (ulong ordinal in new[] { 0UL, 1UL, 16UL, 67UL, ulong.MaxValue })
        {
            var eventItem = new PhysiologyCycleEvent(0, PhysiologyCycleEventKind.VentricularElectrical, ordinal);
            var ordinary = EventWaveformComposition.Restore(new([band with { AfBeatSelection = AtrialFibrillationBeatSelection.Ordinary }], [eventItem]));
            var aberrant = EventWaveformComposition.Restore(new([band with { AfBeatSelection = AtrialFibrillationBeatSelection.LongShort }], [eventItem]));
            var whole = EventWaveformComposition.Restore(new([band with { AfBeatSelection = null }], [eventItem]));
            Check.That(ordinary.EvaluateAt(20_000_000) + aberrant.EvaluateAt(20_000_000) == whole.EvaluateAt(20_000_000), "complementary selection partitions even late ordinals without double counting");
        }
        static void Reject(Action action)
        {
            bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid selector or unrelated rhythm rejects");
        }
    }
}
