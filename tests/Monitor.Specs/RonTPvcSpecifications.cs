// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RonTPvcSpecifications
{
    public static Specification[] All => [new(nameof(RonTOverlaysOnlyThePrecedingProlongedT), RonTOverlaysOnlyThePrecedingProlongedT)];

    private static void RonTOverlaysOnlyThePrecedingProlongedT()
    {
        const AvConductionPattern pattern = AvConductionPattern.RonTLongQtPvcIllustration;
        var plan = PrematureVentricularReference.CreatePlan(pattern);
        var baselinePlan = PrematureVentricularReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
        Check.That(events.SequenceEqual(RegularPhysiologyTimeline.Start(baselinePlan).AdvanceBefore(6_400_000_000, 100)), "long-QT illustration retains electrical/mechanical schedule and no ectopic P");
        var timing = PrematureVentricularReference.Timing;
        var extended = timing with { RrIntervalNs = 800_000_000, QtIntervalNs = 640_000_000, TDurationNs = 440_000_000 };
        IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> bands) => ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(6_400_000_000, 1600, 100);
        IReadOnlyList<ElectrodeWaveformPlan> OnlyT(EcgCycleTiming t) => TextbookElectrodeReference.CreateElectrodes(timing: t).Select(e => e with
        { Bands = e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical && b.DelayNs > 0).Select(b => b with { VentricularCycles = new(4, 4) }).ToArray() }).ToArray();
        var full = Generate(PrematureVentricularReference.CreateElectrodes(pattern));
        var baseline = Generate(PrematureVentricularReference.CreateElectrodes());
        var oldT = Generate(OnlyT(timing));
        var newT = Generate(OnlyT(extended));
        for (int i = 0; i < full.Count; i++)
        {
            for (int lead = 0; lead < 12; lead++)
            {
                Check.That(Math.Abs(full[i].MicrovoltValues[lead] - (baseline[i].MicrovoltValues[lead] - oldT[i].MicrovoltValues[lead] + newT[i].MicrovoltValues[lead])) <= 2, "replace only preceding T, superpose with PVC without duplicate normal T");
            }
            if (i % 800 is < 485 or >= 600)
            { Check.That(full[i].MicrovoltValues.SequenceEqual(baseline[i].MicrovoltValues), "outside changed T support every lead remains exact"); }
        }
        int peak = Enumerable.Range(565, 40).MaxBy(i => baseline[i].MicrovoltValues[1]);
        Check.That(peak > 565 && peak < 600 && newT[peak].MicrovoltValues[1] > 0 && baseline[peak].MicrovoltValues[1] > 500, $"actual PVC R peak on T: index={peak}, T={newT[peak].MicrovoltValues[1]}, R={baseline[peak].MicrovoltValues[1]}");
        Check.That(full[peak].MicrovoltValues[1] > baseline[peak].MicrovoltValues[1], "overlaid R preserves preceding positive T contribution");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateLeadIIBands(pattern)).GenerateBefore(6_400_000_000, 1600, 100);
        Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II and12 leads use same overlap");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity holds during overlap");
        var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var pressure = VascularPressureSource.Create(plan, pressurePlan);
        var normalPressure = VascularPressureSource.Create(baselinePlan, pressurePlan);
        for (long t = 0; t < 6_400_000_000; t += 17_000_000)
        { Check.That(pressure.EvaluateAt(t) == normalPressure.EvaluateAt(t), "T overlap does not infer altered ejection or VF"); }
        foreach (long boundary in new[] { 1_960_000_000L, 2_260_000_000, 2_324_000_000, 2_400_000_000, 3_200_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(pattern));
            source.GenerateBefore(boundary, 1000, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(6_400_000_000, 1600, 100);
            var b = restored.GenerateBefore(6_400_000_000, 1600, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "recovery keeps old T/new QRS overlap at support boundaries");
        }
        var timeline = RegularPhysiologyTimeline.Restore(new(plan, 2_260_000_000));
        var saved = timeline.CaptureState();
        try { timeline.AdvanceBefore(2_341_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(saved == timeline.CaptureState(), "exact event budget rejected atomically"); }
        Check.That(timeline.AdvanceBefore(2_341_000_000, 2).Count == 2, "one QRS and mechanical event, no triggered VT/VF");
        Check.That(RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 3_200_000_000)).AdvanceBefore(long.MaxValue, 40).Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 4 == 3), "late indexed query retains ectopic slot");
        foreach (var invalid in new[] { plan with { VentricularConductionRatio = 2 }, plan with { HeartPeriodNs = 600_000_000 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting R-on-T plan accepted.");
        }
    }
}
