// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ShortCoupledPvcSpecifications
{
    public static Specification[] All => [new(nameof(ShortCoupledPvcPreservesTOverlapAndBoundsMechanicalSources), ShortCoupledPvcPreservesTOverlapAndBoundsMechanicalSources)];
    private static void ShortCoupledPvcPreservesTOverlapAndBoundsMechanicalSources()
    {
        const AvConductionPattern mode = AvConductionPattern.ShortCoupledRonTPvcIllustration;
        var plan = PrematureVentricularReference.CreatePlan(mode);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
        long[] qrs = [160_000_000, 960_000_000, 1_760_000_000, 1_960_000_000, 3_360_000_000, 4_160_000_000, 4_960_000_000, 5_160_000_000];
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(qrs), "coupling200ms and pause1400ms retain original3200ms group");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(qrs.Select(t => t + 80_000_000)), "each electrical beat has authored delayed mechanical event");
        Check.That(events.Where(e => e.Kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical).All(e => e.CycleIndex % 4 != 3), "ectopic slot does not invent atrial events");
        IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> bands) => ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(6_400_000_000, 1600, 100);
        IReadOnlyList<ElectrodeWaveformPlan> Select(IReadOnlyList<ElectrodeWaveformPlan> bands, ulong mask, bool includeP) => bands.Select(e => e with
        { Bands = e.Bands.Where(b => includeP || b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).Select(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical ? b with { VentricularCycles = new(4, mask) } : b).ToArray() }).ToArray();
        var full = Generate(PrematureVentricularReference.CreateElectrodes(mode));
        var normal = Generate(Select(TextbookElectrodeReference.CreateElectrodes(timing: PrematureVentricularReference.Timing), 7, true));
        var pvc = Generate(Select(CompleteAvBlockVentricularReference.CreateElectrodes(), 8, false));
        for (int i = 0; i < full.Count; i++)
        {
            for (int lead = 0; lead < 12; lead++)
            { Check.That(Math.Abs(full[i].MicrovoltValues[lead] - normal[i].MicrovoltValues[lead] - pvc[i].MicrovoltValues[lead]) <= 2, "ordinary sinus QT320 and PVC sum without shortening old T or extending it"); }
        }
        int peak = Enumerable.Range(490, 40).MaxBy(i => pvc[i].MicrovoltValues[1]);
        Check.That(peak == 515 && normal[peak].MicrovoltValues[1] > 0 && pvc[peak].MicrovoltValues[1] > 500, "R peak2060ms occurs on preceding T ending2080ms");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateLeadIIBands(mode)).GenerateBefore(6_400_000_000, 1600, 100);
        Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared monitor II");
        var pressure = VascularPressureSource.Create(plan, new(80_000_000, 160_000_000, 2_900_000_000, 8000, 1000, 30000));
        for (long t = 0; t < 6_400_000_000; t += 17_000_000)
        {
            double time = Math.Max(0, t - 80_000_000), tau = 2_900_000_000;
            double expected = 1000 + 7000 * Math.Exp(-time / tau);
            foreach (long beat in qrs.Select(x => x + 80_000_000).Where(x => x <= time))
            { expected += 30000 * (1 - Math.Exp(-Math.Min(time - beat, 160_000_000) / tau)) * Math.Exp(-Math.Max(0, time - beat - 160_000_000) / tau); }
            Check.That(Math.Abs((double)pressure.EvaluateAt(t) / FixedPointMath.Q32One - expected) < 0.01, "short-interval pressure includes separate ejections and passive runoff");
        }
        try { VascularPressureSource.Create(plan, new(80_000_000, 201_000_000, 2_900_000_000, 8000, 1000, 30000)); throw new InvalidOperationException("Oversized ejection accepted."); }
        catch (EventWaveformException) { }
        foreach (long boundary in new[] { 1_960_000_000L, 2_060_000_000, 2_080_000_000, 2_440_000_000, 3_200_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(mode));
            source.GenerateBefore(boundary, 1000, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(6_400_000_000, 1600, 100); var b = restored.GenerateBefore(6_400_000_000, 1600, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "checkpoint within T/QRS overlap and PVC repolarization tail");
        }
        var timeline = RegularPhysiologyTimeline.Restore(new(plan, 1_960_000_000)); var saved = timeline.CaptureState();
        try { timeline.AdvanceBefore(2_041_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(saved == timeline.CaptureState(), "failed short-pair budget atomic"); }
        Check.That(timeline.AdvanceBefore(2_041_000_000, 2).Count == 2, "exact short-pair budget");
        Check.That(RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 3_200_000_000)).AdvanceBefore(long.MaxValue, 40).Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 4 == 3), "bounded late recovery");
        foreach (var invalid in new[] { plan with { VentricularConductionRatio = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting short-coupled plan accepted.");
        }
    }
}
