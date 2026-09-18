// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentricularCoupletSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(CoupletHasTwoConsecutiveWideComplexesAndNoInterveningP), CoupletHasTwoConsecutiveWideComplexesAndNoInterveningP),
        new(nameof(PolymorphicCoupletChangesOnlyTheSecondElectricalMorphology), PolymorphicCoupletChangesOnlyTheSecondElectricalMorphology),
        new(nameof(CoupletPressureAndRecoveryPreserveBothMechanicalBeats), CoupletPressureAndRecoveryPreserveBothMechanicalBeats),
    ];
    private const AvConductionPattern Pattern = AvConductionPattern.VentricularCoupletIllustration;

    private static void CoupletHasTwoConsecutiveWideComplexesAndNoInterveningP()
    {
        var plan = PrematureVentricularReference.CreatePlan(Pattern);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100);
        long[] qrs = [160_000_000, 960_000_000, 1_760_000_000, 2_260_000_000, 2_760_000_000, 4_160_000_000, 4_960_000_000, 5_760_000_000, 6_260_000_000, 6_760_000_000];
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(qrs), "two consecutive PVCs500ms apart followed by authored1400ms pause");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 0, 800_000_000, 1_600_000_000, 4_000_000_000, 4_800_000_000, 5_600_000_000 }), "no intervening sinus P or extra third ventricular run beat");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialMechanical).All(e => e.CycleIndex % 5 < 3), "both ectopic slots omit atrial mechanics");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(qrs.Select(t => t + 80_000_000)), "two independent ventricular mechanical events, not one merged pulse");
        var full = Generate(PrematureVentricularReference.CreateElectrodes(Pattern));
        var normal = Generate(TextbookElectrodeReference.CreateElectrodes(timing: PrematureVentricularReference.Timing));
        var wide = Generate(CompleteAvBlockVentricularReference.CreateElectrodes());
        for (int i = 0; i < full.Count; i++)
        {
            long phase = i * 4_000_000L % 4_000_000_000;
            var expected = phase is >= 2_260_000_000 and < 2_740_000_000 or >= 2_760_000_000 and < 3_240_000_000 ? wide[i] : normal[i];
            Check.That(full[i].MicrovoltValues.SequenceEqual(expected.MicrovoltValues), "mask24 selects exactly two PVCs while mask7 retains sinus morphology");
            if (i < 1000) { Check.That(full[i].MicrovoltValues.SequenceEqual(full[i + 1000].MicrovoltValues), "five-beat phase repeats through full cycle"); }
        }
        Check.That(Enumerable.Range(0, 120).All(i => full[565 + i].MicrovoltValues.SequenceEqual(full[690 + i].MicrovoltValues)), "both complete QRS/T supports are identical and untruncated");
        Check.That(full.Skip(685).Take(5).All(s => s.MicrovoltValues.All(v => v == 0)), "first QT finishes before next PVC rather than leaving a spurious repolarization tail");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateLeadIIBands(Pattern)).GenerateBefore(8_000_000_000, 2000, 100);
        Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor/projected II share both wide complexes");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity preserved");
        IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> bands) => ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(8_000_000_000, 2000, 100);
    }

    private static void PolymorphicCoupletChangesOnlyTheSecondElectricalMorphology()
    {
        var pattern = AvConductionPattern.PolymorphicVentricularCoupletIllustration;
        var plan = PrematureVentricularReference.CreatePlan(pattern);
        var monoPlan = PrematureVentricularReference.CreatePlan(Pattern);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100);
        Check.That(events.SequenceEqual(RegularPhysiologyTimeline.Start(monoPlan).AdvanceBefore(8_000_000_000, 100)), "morphology must not move electrical/mechanical events or insert atrial events");
        IReadOnlyList<ElectrodeSignalSample> Generate(AvConductionPattern mode) => ElectrodeSignalGenerator.Start(PrematureVentricularReference.CreatePlan(mode), "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(mode)).GenerateBefore(8_000_000_000, 2000, 100);
        var full = Generate(pattern);
        var mono = Generate(Pattern);
        var alternating = Generate(AvConductionPattern.PolymorphicPvcIllustration);
        for (int i = 0; i < full.Count; i++)
        {
            int phase = i % 1000;
            var expected = phase is >= 690 and < 815 ? alternating[1365 + phase - 690] : mono[i];
            Check.That(full[i].MicrovoltValues.SequenceEqual(expected.MicrovoltValues), "only second PVC uses existing opposite180ms/500ms vector, without residual first morphology");
        }
        Check.That(full.Skip(690).Take(125).Zip(mono.Skip(690).Take(125)).Any(p => !p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "second morphology differs from monomorphic couplet");
        Check.That(full.Skip(685).Take(5).All(s => s.MicrovoltValues.All(v => v == 0)), "first QT still ends before second PVC");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateLeadIIBands(pattern)).GenerateBefore(8_000_000_000, 2000, 100);
        Check.That(full.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "both demo ECG sources share paired morphology");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity preserved with second vector");
        var pressureConfig = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var pressure = VascularPressureSource.Create(plan, pressureConfig);
        var monoPressure = VascularPressureSource.Create(monoPlan, pressureConfig);
        for (long time = 0; time < 8_000_000_000; time += 17_000_000)
        { Check.That(pressure.EvaluateAt(time) == monoPressure.EvaluateAt(time), "electrical polarity does not invert ejection or alter pressure"); }
        foreach (long boundary in new[] { 2_700_000_000L, 2_800_000_000, 3_100_000_000, 3_248_000_000, 3_996_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(pattern));
            source.GenerateBefore(boundary, 1000, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(8_000_000_000, 2000, 100);
            var b = restored.GenerateBefore(8_000_000_000, 2000, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore preserves first/second morphology masks and longer second QT tail");
        }
    }

    private static void CoupletPressureAndRecoveryPreserveBothMechanicalBeats()
    {
        foreach (var pattern in new[] { Pattern, AvConductionPattern.PolymorphicVentricularCoupletIllustration })
        {
            var plan = PrematureVentricularReference.CreatePlan(pattern);
            var pressure = VascularPressureSource.Create(plan, new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000));
            long[] mechanical = [240_000_000, 1_040_000_000, 1_840_000_000, 2_340_000_000, 2_840_000_000, 4_240_000_000, 5_040_000_000, 5_840_000_000, 6_340_000_000, 6_840_000_000];
            for (long time = 0; time < 8_000_000_000; time += 17_000_000)
            {
                double t = Math.Max(0, time - 80_000_000), tau = 2_900_000_000;
                double expected = 1000 + 7000 * Math.Exp(-t / tau);
                foreach (long beat in mechanical.Where(b => b <= t))
                { expected += 30000 * (1 - Math.Exp(-Math.Min(t - beat, 240_000_000) / tau)) * Math.Exp(-Math.Max(0, t - beat - 240_000_000) / tau); }
                Check.That(Math.Abs((double)pressure.EvaluateAt(time) / FixedPointMath.Q32One - expected) < 0.01, "independent ejection sum includes both PVC mechanical events and post-pair runoff");
            }
            var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100);
            for (long begin = 0; begin < 7_900_000_000; begin += 37_000_000)
            {
                long end = begin + 59_000_000;
                var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
                Check.That(RegularPhysiologyTimeline.Restore(new(plan, begin)).AdvanceBefore(end, Math.Max(1, expected.Length)).SequenceEqual(expected), "exact half-open query preserves both original PVC ordinals");
            }
            foreach (long boundary in new[] { 2_700_000_000L, 2_748_000_000, 2_848_000_000, 3_100_000_000, 3_996_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(pattern));
                source.GenerateBefore(boundary, 1000, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                Check.That(source.GenerateBefore(8_000_000_000, 2000, 100).Zip(restored.GenerateBefore(8_000_000_000, 2000, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore between and within both wide QRS/T supports and at group boundary");
            }
            var timeline = RegularPhysiologyTimeline.Restore(new(plan, 2_260_000_000));
            var saved = timeline.CaptureState();
            try { timeline.AdvanceBefore(2_841_000_000, 3); throw new InvalidOperationException("Budget accepted."); }
            catch (PhysiologyTimelineException) { Check.That(saved == timeline.CaptureState(), "paired four-event budget rejection atomic"); }
            Check.That(timeline.AdvanceBefore(2_841_000_000, 4).Count == 4, "two QRS and two mechanical events consume exact budget");
            var late = RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 4_000_000_000)).AdvanceBefore(long.MaxValue, 40);
            Check.That(new ulong[] { 3, 4 }.All(slot => late.Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 5 == slot)) && pressure.EvaluateAt(long.MaxValue) > 0, "late query retains both couplet slots without epoch replay");
            Check.That(RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = false }).AdvanceBefore(8_000_000_000, 100).All(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical), "explicit mechanical disable covers both PVCs");
            foreach (var invalid in new[] { plan with { VentricularConductionRatio = 2 }, plan with { MechanicalEveryCycles = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 } })
            {
                try { RegularPhysiologyTimeline.Start(invalid); }
                catch (PhysiologyTimelineException) { continue; }
                throw new InvalidOperationException("Conflicting couplet plan accepted.");
            }
        }
    }
}
