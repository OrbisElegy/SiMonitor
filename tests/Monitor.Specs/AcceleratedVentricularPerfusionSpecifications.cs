// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AcceleratedVentricularPerfusionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    public static Specification[] All =>
    [
        new(nameof(AcceleratedVentricularPerfusionPreservesSupportsAndBoundsOverlap), AcceleratedVentricularPerfusionPreservesSupportsAndBoundsOverlap),
        new(nameof(AcceleratedVentricularPerfusionIsPeriodicAtLateTimesAndRetainsRunoff), AcceleratedVentricularPerfusionIsPeriodicAtLateTimesAndRetainsRunoff),
        new(nameof(AcceleratedVentricularVenousAndRespiratoryComponentsRespectIndependentClocks), AcceleratedVentricularVenousAndRespiratoryComponentsRespectIndependentClocks),
    ];
    private static void AcceleratedVentricularPerfusionPreservesSupportsAndBoundsOverlap()
    {
        var plan = AcceleratedVentricularReference.CreatePlan();
        Check.That(FixedPerfusionPresets.AcceleratedVentricular.Pleth.PulseDurationNs == 512_000_000 && FixedPerfusionPresets.AcceleratedVentricular.Arterial.Morphology!.DurationNs == 600_000_000 && FixedPerfusionPresets.AcceleratedVentricular.Pulmonary.Morphology!.DurationNs == 640_000_000, "full morphology supports retained within750ms RR");
        _ = VascularPressureSource.Create(plan, FixedPerfusionPresets.AcceleratedVentricular.Arterial);
        _ = VascularPressureSource.Create(plan, FixedPerfusionPresets.AcceleratedVentricular.Pulmonary);
        _ = FixedPerfusionPresets.AcceleratedVentricular.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        foreach (var pressure in new[] { FixedPerfusionPresets.AcceleratedVentricular.Arterial with { Morphology = FixedPerfusionPresets.AcceleratedVentricular.Arterial.Morphology! with { DurationNs = 760_000_000 } }, FixedPerfusionPresets.AcceleratedVentricular.Pulmonary with { Morphology = FixedPerfusionPresets.AcceleratedVentricular.Pulmonary.Morphology! with { DurationNs = 760_000_000 } } })
        {
            try { VascularPressureSource.Create(plan, pressure); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Oversized AIVR pressure pulse accepted.");
        }
    }

    private static void AcceleratedVentricularPerfusionIsPeriodicAtLateTimesAndRetainsRunoff()
    {
        var plan = AcceleratedVentricularReference.CreatePlan();
        var pleth = PlethRunoffSource.Create(plan, FixedPerfusionPresets.AcceleratedVentricular.Pleth);
        Check.That(pleth.MaximumHistoryEvents < 100 && pleth.SupportNs < 27_000_000_000, "optical history bounded independently of run duration");
        foreach (var pressure in new[] { FixedPerfusionPresets.AcceleratedVentricular.Arterial, FixedPerfusionPresets.AcceleratedVentricular.Pulmonary })
        {
            Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + 749_999_999) / 750_000_000 < 350, "pressure finite history under350 events");
            var source = VascularPressureSource.Create(plan, pressure);
            var values = new List<long>();
            for (long phase = 0; phase < 750_000_000; phase += 10_000_000)
            {
                long a = source.EvaluateAt(300_000_000_000 + phase);
                long b = source.EvaluateAt(86_400_000_000_000 + phase);
                Check.That(Math.Abs(a - b) <= Q && a > pressure.AsymptoticPressureCentiMmHg * Q && a < short.MaxValue * Q, "pressure remains periodic and unsaturated after one day");
                values.Add(a);
                Check.That(Math.Abs(pleth.EvaluateAt(300_000_000_000 + phase) - pleth.EvaluateAt(86_400_000_000_000 + phase)) <= Q, "pleth evaluation at late ordinal is phase-equivalent");
            }
            Check.That(values.Max() - values.Min() > Q, "pressure pulses retain modulation");
        }
        var singlePlan = plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 };
        var single = PlethRunoffSource.Create(singlePlan, FixedPerfusionPresets.AcceleratedVentricular.Pleth with { UseCardiacFillingPerfusion = false });
        long time = 1_100_000_000;
        long expected = (long)FixedPointMath.RoundDivideTiesToEven((Int128)single.EvaluateAt(time) * 969, 1000) +
            (long)FixedPointMath.RoundDivideTiesToEven((Int128)single.EvaluateAt(time - 750_000_000) * 868, 1000);
        Check.That(Math.Abs(pleth.EvaluateAt(time) - expected) <= 3, "overlapping optical pulses retain tails with each beat's filling gain");
        var interrupted = VascularPressureSource.Create(singlePlan, FixedPerfusionPresets.AcceleratedVentricular.Arterial);
        Check.That(interrupted.EvaluateAt(800_000_000) > FixedPerfusionPresets.AcceleratedVentricular.Arterial.AsymptoticPressureCentiMmHg * Q, "missing subsequent ejection retains pressure runoff");
    }
    private static void AcceleratedVentricularVenousAndRespiratoryComponentsRespectIndependentClocks()
    {
        var plan = AcceleratedVentricularReference.CreatePlan();
        var channel = FixedPerfusionPresets.AcceleratedVentricular.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var bands = channel.Bands;
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 100);
        var atrial = EventWaveformComposition.Restore(new([bands[0]], events));
        var ventricular = EventWaveformComposition.Restore(new(bands.Skip(1).Take(4).ToArray(), events));
        var whole = EventWaveformComposition.Restore(new(bands.Take(5).ToArray(), events));
        Check.That(bands[0].Trigger == PhysiologyCycleEventKind.AtrialMechanical && bands.Skip(1).Take(4).All(b => b.Trigger == PhysiologyCycleEventKind.VentricularMechanical), "CVP atrial and ventricular triggers remain independent");
        for (long t = 800_000_000; t < 1_500_000_000; t += 10_000_000)
        {
            Check.That(atrial.EvaluateAt(t) == atrial.EvaluateAt(t + 800_000_000), "a wave repeats at atrial period");
            Check.That(ventricular.EvaluateAt(t) == ventricular.EvaluateAt(t + 750_000_000), "c/x/v/y repeat at ventricular period including delayed y");
            Check.That(whole.EvaluateAt(t) == atrial.EvaluateAt(t) + ventricular.EvaluateAt(t), "CVP superposition preserves phase drift without artificial cannon a gain");
        }
        Check.That(atrial.EvaluateAt(140_000_000) > 0 && atrial.EvaluateAt(515_000_000) == 0, "a wave is not retriggered by each ventricle");
        var resp = new RespirationPlan(1000, 200).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(resp.Bands[1].DurationNs == 750_000_000 && resp.Bands[1].Trigger == PhysiologyCycleEventKind.VentricularMechanical, "cardiac artifact fits independent750ms ventricle");
        var artifact = EventWaveformComposition.Restore(new([resp.Bands[1]], events));
        for (long t = 0; t < 3_000_000_000; t += 4_000_000)
            Check.That(Math.Abs(artifact.EvaluateAt(t)) <= 200 * Q, "artifact cannot exceed reserved single-pulse amplitude");
        var normal = new RespirationPlan(1000, 200).CreateChannel(CompleteAvBlockVentricularReference.CreatePlan(), Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(normal.Bands[1].DurationNs == 800_000_000, "slow escape retains original artifact support");
    }

}
