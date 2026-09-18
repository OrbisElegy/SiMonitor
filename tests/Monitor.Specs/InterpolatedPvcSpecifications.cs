// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class InterpolatedPvcSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(InterpolatedPvcRetainsSinusClockAndOverlappingP), InterpolatedPvcRetainsSinusClockAndOverlappingP),
        new(nameof(InterpolatedPvcPressureRecoveryAndBudgetsIncludeExtraBeat), InterpolatedPvcPressureRecoveryAndBudgetsIncludeExtraBeat),
    ];
    private const AvConductionPattern Pattern = AvConductionPattern.InterpolatedPvcIllustration;

    private static void InterpolatedPvcRetainsSinusClockAndOverlappingP()
    {
        var plan = PrematureVentricularReference.CreatePlan(Pattern);
        var regular = plan with { ConductionPattern = AvConductionPattern.FixedPr };
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_000_000_000, 100);
        var sinus = RegularPhysiologyTimeline.Start(regular).AdvanceBefore(6_000_000_000, 100);
        foreach (var kind in new[] { PhysiologyCycleEventKind.AtrialElectrical, PhysiologyCycleEventKind.AtrialMechanical })
        { Check.That(events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs).SequenceEqual(sinus.Where(e => e.Kind == kind).Select(e => e.SimTimeNs)), "sinus P and atrial mechanics keep the original1000ms grid"); }
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 160_000_000, 1_160_000_000, 2_160_000_000, 2_660_000_000, 3_160_000_000, 4_160_000_000, 5_160_000_000, 5_660_000_000 }), "PVC inserts within original RR:500+500=1000ms, next sinus QRS not delayed");
        Check.That(events.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) == sinus.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) + 2, "two added ventricular beats, no replaced sinus beats");
        var electrodes = PrematureVentricularReference.CreateElectrodes(Pattern);
        var full = Generate(electrodes);
        var withoutAtrial = Generate(electrodes.Select(e => e with { Bands = e.Bands.Where(b => b.Trigger != PhysiologyCycleEventKind.AtrialElectrical).ToArray() }).ToArray());
        var onlyAtrial = Generate(electrodes.Select(e => e with { Bands = e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray() }).ToArray());
        var normal = ElectrodeSignalGenerator.Start(regular, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(timing: PrematureVentricularReference.Timing)).GenerateBefore(6_000_000_000, 1500, 100);
        for (int i = 0; i < full.Count; i++)
        {
            long t = i * 4_000_000L;
            if (!(t is >= 2_660_000_000 and < 3_140_000_000) && !(t is >= 5_660_000_000 and < 6_140_000_000))
            { Check.That(full[i].MicrovoltValues.SequenceEqual(normal[i].MicrovoltValues), "outside inserted PVC support the complete sinus ECG is unchanged"); }
            Check.That(Enumerable.Range(0, 12).All(lead => Math.Abs(full[i].MicrovoltValues[lead] - withoutAtrial[i].MicrovoltValues[lead] - onlyAtrial[i].MicrovoltValues[lead]) <= 2), "next sinus P and old PVC T add without either being deleted");
        }
        Check.That(Enumerable.Range(750, 25).Any(i => Math.Abs(withoutAtrial[i].MicrovoltValues[1]) > 100 && onlyAtrial[i].MicrovoltValues[1] > 100), "next sinus P genuinely overlaps PVC repolarization");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateLeadIIBands(Pattern)).GenerateBefore(6_000_000_000, 1500, 100);
        Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor/projected II share inserted QRS and P/T overlay");
        IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> bands) => ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(6_000_000_000, 1500, 100);
    }

    private static void InterpolatedPvcPressureRecoveryAndBudgetsIncludeExtraBeat()
    {
        var plan = PrematureVentricularReference.CreatePlan(Pattern);
        var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000));
        long[] mechanical = [240_000_000, 1_240_000_000, 2_240_000_000, 2_740_000_000, 3_240_000_000, 4_240_000_000, 5_240_000_000, 5_740_000_000];
        var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_000_000_000, 100);
        Check.That(all.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(mechanical), "each original and inserted QRS gets exactly one mechanical event");
        for (long time = 0; time < 6_000_000_000; time += 17_000_000)
        {
            double t = Math.Max(0, time - 80_000_000), tau = 2_900_000_000;
            double expected = 1000 + 7000 * Math.Exp(-t / tau);
            foreach (long beat in mechanical.Where(b => b <= t))
            { expected += 30000 * (1 - Math.Exp(-Math.Min(t - beat, 240_000_000) / tau)) * Math.Exp(-Math.Max(0, t - beat - 240_000_000) / tau); }
            Check.That(Math.Abs((double)pressure.EvaluateAt(time) / FixedPointMath.Q32One - expected) < 0.01, "independent pressure sum includes extra ejection without skipping next sinus beat");
        }
        for (long begin = 0; begin < 5_900_000_000; begin += 37_000_000)
        {
            long end = begin + 59_000_000;
            var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
            Check.That(RegularPhysiologyTimeline.Restore(new(plan, begin)).AdvanceBefore(end, Math.Max(1, expected.Length)).SequenceEqual(expected), "half-open query preserves sinus and inserted beat ordinals");
        }
        foreach (long boundary in new[] { 2_700_000_000L, 2_996_000_000, 3_048_000_000, 3_160_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(Pattern));
            source.GenerateBefore(boundary, 800, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(6_000_000_000, 1500, 100).Zip(restored.GenerateBefore(6_000_000_000, 1500, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore through group wrap and P/T overlap before resumed sinus QRS");
        }
        var timeline = RegularPhysiologyTimeline.Restore(new(plan, 2_660_000_000));
        var saved = timeline.CaptureState();
        try { timeline.AdvanceBefore(2_741_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(saved == timeline.CaptureState(), "inserted-beat event budget failure is atomic"); }
        Check.That(RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 3_000_000_000)).AdvanceBefore(long.MaxValue, 30).Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 4 == 3) && pressure.EvaluateAt(long.MaxValue) > 0, "bounded late query retains inserted beat identity");
        foreach (var invalid in new[] { plan with { HeartPeriodNs = 800_000_000 }, plan with { VentricularConductionRatio = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting interpolation plan accepted.");
        }
    }
}
