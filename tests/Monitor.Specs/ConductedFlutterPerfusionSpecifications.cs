// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ConductedFlutterPerfusionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ConductedFlutterUsesPrecedingIntervals), ConductedFlutterUsesPrecedingIntervals),
        new(nameof(ConductedFlutterPressureIsBoundedAndPeriodic), ConductedFlutterPressureIsBoundedAndPeriodic),
        new(nameof(ConductedFlutterRestoresAndRejectsIncompatiblePlans), ConductedFlutterRestoresAndRejectsIncompatiblePlans)
    ];
    private static void ConductedFlutterUsesPrecedingIntervals()
    {
        var variable = AtrialFlutterReference.CreateVariablePlan();
        Check.That(Enumerable.Range(0, 4).Select(i => ConductedFlutterPerfusion.GainPermille(variable, (ulong)i))
            .SequenceEqual([850, 513, 742, 850]), "variable gains use previous800/400/600ms, not following RR");
        foreach (int ratio in new[] { 2, 3, 4 })
        {
            var plan = AtrialFlutterReference.CreatePlan(ratio);
            Check.That(ConductedFlutterPerfusion.GainPermille(plan, ulong.MaxValue) ==
                CardiacFillingPerfusion.StrokeVolumePermille(ratio * 200_000_000L, 48_000_000, 240_000_000), "fixed ratios share bounded filling model");
        }
    }
    private static void ConductedFlutterPressureIsBoundedAndPeriodic()
    {
        const long q = FixedPointMath.Q32One;
        foreach (var plan in new[] { AtrialFlutterReference.CreatePlan(2), AtrialFlutterReference.CreatePlan(3),
            AtrialFlutterReference.CreatePlan(4), AtrialFlutterReference.CreateVariablePlan() })
        {
            long duration = plan.VentricularConductionRatio * 200_000_000L - 80_000_000;
            foreach (bool pulmonary in new[] { false, true })
            {
                var input = new VascularPressurePlan(pulmonary ? 40_000_000 : 80_000_000,
                    pulmonary ? 200_000_000 : 240_000_000, pulmonary ? 700_000_000 : 2_900_000_000,
                    pulmonary ? 1000 : 8000, pulmonary ? 500 : 1000, pulmonary ? 5000 : 30000,
                    Morphology: new(pulmonary ? VascularPressureMorphologyKind.PulmonaryArtery : VascularPressureMorphologyKind.Arterial,
                        Math.Min(duration, pulmonary ? 640_000_000 : 600_000_000), pulmonary ? 1500 : 4000));
                var old = VascularPressureSource.Create(plan, input);
                var corrected = VascularPressureSource.Create(plan, input with { UseConductedFlutterPerfusion = true });
                var values = new List<double>(); var before = new List<double>();
                for (long phase = 0; phase < 3_600_000_000; phase += 8_000_000)
                {
                    long now = corrected.EvaluateAt(360_000_000_000 + phase);
                    Check.That(Math.Abs(now - corrected.EvaluateAt(86_400_000_000_000 + phase)) <= q,
                        "late evaluation uses bounded history and same cycle ordinal phase");
                    long previous = old.EvaluateAt(360_000_000_000 + phase);
                    if (plan.VentricularConductionRatio == 4) { Check.That(now < previous, "even slow flutter retains abnormal, partial atrial transport rather than a normal atrial kick"); }
                    values.Add(now / (double)q / 100); before.Add(previous / (double)q / 100);
                }
                Console.WriteLine($"Flutter {plan.ConductionPattern}/{plan.VentricularConductionRatio} pulmonary={pulmonary}: old {before.Min():F2}-{before.Max():F2}, new {values.Min():F2}-{values.Max():F2}");
                Check.That(values.Max() - values.Min() > .5 && values.Max() < (pulmonary ? 40 : 165),
                    "reduced input retains pulsatility, without pressure clipping");
                if (!pulmonary && plan.ConductionPattern == AvConductionPattern.AtrialFlutterIllustration && plan.VentricularConductionRatio == 2)
                { Check.That(before.Max() > 220 && values.Max() < 150, "reproduce2:1 excessive pressure and remove full-stroke high-rate input"); }
            }
            var pleth = PlethRunoffSource.Create(plan, new(80_000_000, Math.Min(duration, 512_000_000), 1000, UseConductedFlutterPerfusion: true));
            Check.That(pleth.MaximumHistoryEvents < 100, "flutter optical history stays finite");
            for (long phase = 0; phase < 3_600_000_000; phase += 40_000_000)
            { Check.That(Math.Abs(pleth.EvaluateAt(360_000_000_000 + phase) - pleth.EvaluateAt(86_400_000_000_000 + phase)) <= q, "pleth shares late ordinal gain sequence"); }
        }
    }
    private static void ConductedFlutterRestoresAndRejectsIncompatiblePlans()
    {
        var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.VariableFlutter);
        for (long time = 200_000_000; time <= 8_000_000_000; time += 200_000_000)
        { _ = source.AdvanceTo(time, 50, 1, 100); }
        var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
        for (long time = 8_200_000_000; time <= 12_000_000_000; time += 200_000_000)
        {
            var a = source.AdvanceTo(time, 50, 1, 100); var b = restored.AdvanceTo(time, 50, 1, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.SequenceEqual(p.Second)), "checkpoint restores exact per-beat perfusion and pending samples");
        }
        foreach (var plan in new[] { AtrialFlutterReference.CreatePlan(1), VentricularTachycardiaReference.CreatePlan() })
        {
            bool rejected = false;
            try { PlethRunoffSource.Create(plan, new(0, 320_000_000, 1000, UseConductedFlutterPerfusion: true)); }
            catch (EventWaveformException) { rejected = true; }
            Check.That(rejected, "flutter mode rejects unrelated/one-to-one plan instead of double scaling");
            rejected = false;
            try { VascularPressureSource.Create(plan, new(0, 120_000_000, 700_000_000, 1000, 500, 5000, UseConductedFlutterPerfusion: true)); }
            catch (EventWaveformException) { rejected = true; }
            Check.That(rejected, "pressure rejects unsupported flutter scaling");
        }
    }
}
