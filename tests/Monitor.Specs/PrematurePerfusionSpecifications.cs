// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PrematurePerfusionSpecifications
{
    public static Specification[] All => [new(nameof(BeatPerfusionSharesGainsWithoutInventingEjection), BeatPerfusionSharesGainsWithoutInventingEjection)];
    private static void BeatPerfusionSharesGainsWithoutInventingEjection()
    {
        foreach (var mode in Enum.GetValues<AvConductionPattern>().Where(PrematureVentricularReference.IsPattern))
        {
            var physiology = PrematureVentricularReference.CreatePlan(mode);
            int count = PrematureVentricularReference.BeatsPerGroup(mode);
            bool shortCoupled = mode == AvConductionPattern.ShortCoupledRonTPvcIllustration;
            Check.That(PrematureBeatPerfusion.GainPermille(mode, 0) == 800, "no phantom recovery at startup");
            Check.That(PrematureBeatPerfusion.GainPermille(mode, (ulong)count - 1) == (shortCoupled ? 0 : 200), "ectopic authored output");
            Check.That(PrematureBeatPerfusion.GainPermille(mode, (ulong)count) == (mode == AvConductionPattern.InterpolatedPvcIllustration ? 800 : 1000), "recovery enhancement explicitly absent for interpolated example");
            var plan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000, UsePrematureBeatPerfusion: true);
            var pressure = VascularPressureSource.Create(physiology, plan);
            var events = RegularPhysiologyTimeline.Start(physiology).AdvanceBefore(8_000_000_000, 200).Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
            for (long t = 0; t < 8_000_000_000; t += 19_000_000)
            {
                double time = Math.Max(0, t - 80_000_000), tau = 2_900_000_000;
                double expected = 1000 + 7000 * Math.Exp(-time / tau);
                foreach (var beat in events.Where(e => e.SimTimeNs <= time))
                {
                    ulong slot = beat.CycleIndex % (ulong)count;
                    bool ectopic = count == 5 ? slot >= 3 : count == 8 ? slot % 4 == 3 : slot == (ulong)count - 1;
                    double gain = ectopic ? shortCoupled ? 0 : 0.2 : beat.CycleIndex > 0 && mode != AvConductionPattern.InterpolatedPvcIllustration && (slot == 0 || count == 8 && slot == 4) ? 1 : 0.8;
                    double age = time - beat.SimTimeNs;
                    expected += 30000 * gain * (1 - Math.Exp(-Math.Min(age, 240_000_000) / tau)) * Math.Exp(-Math.Max(0, age - 240_000_000) / tau);
                }
                Check.That(Math.Abs((double)pressure.EvaluateAt(t) / FixedPointMath.Q32One - expected) < 0.01, "independent beat-weighted pressure sum including absent ectopic input");
            }
            var band = new PlethPulsePlan(80_000_000, 512_000_000, 1250).CreateBands()[0] with { EjectionIllustration = mode };
            foreach (var beat in events.Take(2 * count))
            {
                var weighted = EventWaveformComposition.Restore(new([band], [beat]));
                var reference = EventWaveformComposition.Restore(new([band with { EjectionIllustration = null }], [beat]));
                long t = beat.SimTimeNs + 200_000_000;
                long expected = (long)FixedPointMath.RoundDivideTiesToEven((Int128)reference.EvaluateAt(t) * PrematureBeatPerfusion.GainPermille(mode, beat.CycleIndex), 1000);
                Check.That(weighted.EvaluateAt(t) == expected, "Pleth uses same beat strength and retains normal support");
            }
            var source = PhysiologySignalGenerator.Start(physiology, "AcqPressure125@1", 1, [], plan);
            source.GenerateBefore(2_056_000_000, 1000, 200);
            var restored = PhysiologySignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(8_000_000_000, 1000, 200).SequenceEqual(restored.GenerateBefore(8_000_000_000, 1000, 200)), "pressure recovery preserves strength phase");
            var shaped = VascularPressureSource.Create(physiology, plan with { Morphology = new(VascularPressureMorphologyKind.Arterial, shortCoupled ? 600_000_000 : 420_000_000, 4000) });
            Check.That(shaped.EvaluateAt(long.MaxValue) >= 0, "bounded remote shaped reconstruction");
        }
        var ordinary = PrematureVentricularReference.CreatePlan();
        var shortPlan = PrematureVentricularReference.CreatePlan(AvConductionPattern.ShortCoupledRonTPvcIllustration);
        try { VascularPressureSource.Create(shortPlan, new(80_000_000, 801_000_000, 2_900_000_000, 8000, 1000, 30000, UsePrematureBeatPerfusion: true)); throw new InvalidOperationException("Unsafe support accepted."); }
        catch (EventWaveformException) { }
        var wrongBand = new PlethPulsePlan(80_000_000, 512_000_000, 1000).CreateBands()[0] with { EjectionIllustration = AvConductionPattern.ShortCoupledRonTPvcIllustration };
        try { PhysiologySignalGenerator.Start(ordinary, "AcqPleth125@1", 1, [wrongBand]); throw new InvalidOperationException("Mismatched beat phase accepted."); }
        catch (PhysiologySignalException) { }
        try { EventWaveformComposition.Restore(new([wrongBand with { Trigger = PhysiologyCycleEventKind.VentricularElectrical }], [])); throw new InvalidOperationException("Electrical waveform gained perfusion weighting."); }
        catch (EventWaveformException) { }
    }
}
