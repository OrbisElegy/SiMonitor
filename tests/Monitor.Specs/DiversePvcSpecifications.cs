// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class DiversePvcSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(DiversePvcDistinguishesMorphologyFromCoupling), DiversePvcDistinguishesMorphologyFromCoupling),
        new(nameof(DiversePvcPressureAndRecoveryRetainEightBeatPhase), DiversePvcPressureAndRecoveryRetainEightBeatPhase),
    ];
    private static readonly AvConductionPattern[] Patterns = [AvConductionPattern.PolymorphicPvcIllustration, AvConductionPattern.MultifocalPvcIllustration];

    private static void DiversePvcDistinguishesMorphologyFromCoupling()
    {
        foreach (var pattern in Patterns)
        {
            var plan = PrematureVentricularReference.CreatePlan(pattern);
            long second = pattern == Patterns[0] ? 5_460_000_000 : 5_560_000_000;
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
            Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 160_000_000, 960_000_000, 1_760_000_000, 2_260_000_000, 3_360_000_000, 4_160_000_000, 4_960_000_000, second }), "fixed500 or alternating500/600ms coupling with full compensation");
            Check.That(events.Where(e => e.Kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical).All(e => e.CycleIndex % 4 != 3), "both premature slots have no related atrial event");
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(pattern));
            var samples = source.GenerateBefore(12_800_000_000, 3200, 200);
            var baseline = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(timing: PrematureVentricularReference.Timing)).GenerateBefore(12_800_000_000, 3200, 200);
            for (int i = 0; i < samples.Count; i++)
            {
                long phase = i * 4_000_000L % 6_400_000_000;
                if (!(phase is >= 2_260_000_000 and < 2_740_000_000) && !(phase >= second && phase < second + 500_000_000))
                { Check.That(samples[i].MicrovoltValues.SequenceEqual(baseline[i].MicrovoltValues), "sinus P/QRS/T and baseline unchanged outside both PVC supports"); }
                if (i < 1600) { Check.That(samples[i].MicrovoltValues.SequenceEqual(samples[i + 1600].MicrovoltValues), "8beat morphology and coupling pattern repeats exactly"); }
            }
            int b = (int)(second / 4_000_000);
            for (int lead = 0; lead < 12; lead++)
            {
                for (int k = 0; k <= 4; k++)
                { Check.That(Math.Abs(samples[565 + 8 * k].MicrovoltValues[lead] + samples[b + 9 * k].MicrovoltValues[lead]) <= 1, "B is opposite ventricular vector with180ms support versus A160ms, at matched phase"); }
                for (int k = 0; k < 55; k++)
                { Check.That(Math.Abs(samples[630 + k].MicrovoltValues[lead] + samples[b + 70 + k].MicrovoltValues[lead]) <= 1, "B T polarity follows reversed vector, with delayed onset and unchanged220ms support"); }
            }
            Check.That(samples.Skip(b + 40).Take(5).Any(s => Math.Abs(s.MicrovoltValues[6]) > 20), "B QRS retains terminal support beyond160ms");
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateLeadIIBands(pattern)).GenerateBefore(12_800_000_000, 3200, 200);
            Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "both morphologies share monitor and projected II");
            Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity survives vector change");
        }
    }

    private static void DiversePvcPressureAndRecoveryRetainEightBeatPhase()
    {
        foreach (var pattern in Patterns)
        {
            var plan = PrematureVentricularReference.CreatePlan(pattern);
            long second = pattern == Patterns[0] ? 5_460_000_000 : 5_560_000_000;
            var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
            var pressure = VascularPressureSource.Create(plan, pressurePlan);
            long[] ejections = [240_000_000, 1_040_000_000, 1_840_000_000, 2_340_000_000, 3_440_000_000, 4_240_000_000, 5_040_000_000, second + 80_000_000];
            for (long time = 0; time < 6_400_000_000; time += 17_000_000)
            {
                double t = Math.Max(0, time - 80_000_000), tau = 2_900_000_000;
                double expected = 1000 + 7000 * Math.Exp(-t / tau);
                foreach (long beat in ejections.Where(b => b <= t))
                { expected += 30000 * (1 - Math.Exp(-Math.Min(t - beat, 240_000_000) / tau)) * Math.Exp(-Math.Max(0, t - beat - 240_000_000) / tau); }
                Check.That(Math.Abs((double)pressure.EvaluateAt(time) / FixedPointMath.Q32One - expected) < 0.01, "pressure reflects actual A/B ejection timing, never vector polarity");
            }
            var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(12_800_000_000, 200);
            for (long begin = 0; begin < 12_700_000_000; begin += 53_000_000)
            {
                long end = begin + 79_000_000;
                var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
                Check.That(RegularPhysiologyTimeline.Restore(new(plan, begin)).AdvanceBefore(end, Math.Max(1, expected.Length)).SequenceEqual(expected), "exact half-open windows retain original A/B beat ordinal");
            }
            foreach (long boundary in new[] { 3_196_000_000, second + 72_000_000, second + 400_000_000, 6_396_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureVentricularReference.CreateElectrodes(pattern));
                source.GenerateBefore(boundary, 1600, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                Check.That(source.GenerateBefore(12_800_000_000, 3200, 200).Zip(restored.GenerateBefore(12_800_000_000, 3200, 200)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "checkpoint keeps A/B polarity and duration through group boundary");
            }
            var timeline = RegularPhysiologyTimeline.Restore(new(plan, second));
            var saved = timeline.CaptureState();
            try { timeline.AdvanceBefore(second + 81_000_000, 1); throw new InvalidOperationException("Budget accepted."); }
            catch (PhysiologyTimelineException) { Check.That(saved == timeline.CaptureState(), "atomic rejection at second PVC"); }
            Check.That(RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 6_400_000_000)).AdvanceBefore(long.MaxValue, 100).Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 8 == 7) && pressure.EvaluateAt(long.MaxValue) > 0, "late bounded query preserves second morphology slot");
            try { RegularPhysiologyTimeline.Start(plan with { MechanicalEveryCycles = 2 }); throw new InvalidOperationException("Conflicting mechanical stride accepted."); }
            catch (PhysiologyTimelineException) { }
        }
    }
}
