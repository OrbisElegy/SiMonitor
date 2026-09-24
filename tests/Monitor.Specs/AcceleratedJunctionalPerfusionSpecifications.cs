// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AcceleratedJunctionalPerfusionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    public static Specification[] All =>
    [
        new(nameof(AcceleratedJunctionalPerfusionPreservesSupportsAndBoundsOverlap), AcceleratedJunctionalPerfusionPreservesSupportsAndBoundsOverlap),
        new(nameof(AcceleratedJunctionalPerfusionIsPeriodicAtLateTimesAndRetainsRunoff), AcceleratedJunctionalPerfusionIsPeriodicAtLateTimesAndRetainsRunoff),
        new(nameof(AcceleratedJunctionalVenousAndRespiratoryComponentsRespectIndependentClocks), AcceleratedJunctionalVenousAndRespiratoryComponentsRespectIndependentClocks),
    ];
    private static void AcceleratedJunctionalPerfusionPreservesSupportsAndBoundsOverlap()
    {
        var plan = AcceleratedJunctionalReference.CreatePlan();
        Check.That(FixedPerfusionPresets.PulmonaryOverlap.Pleth.PulseDurationNs == 512_000_000 && FixedPerfusionPresets.PulmonaryOverlap.Arterial.Morphology!.DurationNs == 600_000_000 && FixedPerfusionPresets.PulmonaryOverlap.Pulmonary.Morphology!.DurationNs == 640_000_000, "full morphology supports retained at600ms RR");
        _ = VascularPressureSource.Create(plan, FixedPerfusionPresets.PulmonaryOverlap.Arterial);
        _ = VascularPressureSource.Create(plan, FixedPerfusionPresets.PulmonaryOverlap.Pulmonary);
        _ = FixedPerfusionPresets.PulmonaryOverlap.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        foreach (var pressure in new[] { FixedPerfusionPresets.PulmonaryOverlap.Arterial with { Morphology = FixedPerfusionPresets.PulmonaryOverlap.Arterial.Morphology! with { DurationNs = 760_000_000 } }, FixedPerfusionPresets.PulmonaryOverlap.Pulmonary with { Morphology = FixedPerfusionPresets.PulmonaryOverlap.Pulmonary.Morphology! with { MaximumPulseOverlap = 1 } } })
        {
            try { VascularPressureSource.Create(plan, pressure); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Oversized AJR pressure pulse accepted.");
        }
    }

    private static void AcceleratedJunctionalPerfusionIsPeriodicAtLateTimesAndRetainsRunoff()
    {
        var plan = AcceleratedJunctionalReference.CreatePlan();
        var pleth = PlethRunoffSource.Create(plan, FixedPerfusionPresets.PulmonaryOverlap.Pleth);
        Check.That(pleth.MaximumHistoryEvents < 100 && pleth.SupportNs < 27_000_000_000, "optical history bounded independently of run duration");
        foreach (var pressure in new[] { FixedPerfusionPresets.PulmonaryOverlap.Arterial, FixedPerfusionPresets.PulmonaryOverlap.Pulmonary })
        {
            Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + 599_999_999) / 600_000_000 < 350, "pressure finite history under350 events");
            var source = VascularPressureSource.Create(plan, pressure);
            var values = new List<long>();
            for (long phase = 0; phase < 600_000_000; phase += 10_000_000)
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
        var single = PlethRunoffSource.Create(singlePlan, FixedPerfusionPresets.PulmonaryOverlap.Pleth);
        long time = 1_100_000_000;
        Check.That(Math.Abs(pleth.EvaluateAt(time) - single.EvaluateAt(time) - single.EvaluateAt(time - 600_000_000)) <= 3, "overlapping optical pulses retain earlier tails");
        var interrupted = VascularPressureSource.Create(singlePlan, FixedPerfusionPresets.PulmonaryOverlap.Arterial);
        Check.That(interrupted.EvaluateAt(800_000_000) > FixedPerfusionPresets.PulmonaryOverlap.Arterial.AsymptoticPressureCentiMmHg * Q, "missing subsequent ejection retains pressure runoff");
    }
    private static void AcceleratedJunctionalVenousAndRespiratoryComponentsRespectIndependentClocks()
    {
        var plan = AcceleratedJunctionalReference.CreatePlan();
        var channel = FixedPerfusionPresets.PulmonaryOverlap.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var bands = channel.Bands;
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 100);
        var atrial = EventWaveformComposition.Restore(new([bands[0]], events));
        var ventricular = EventWaveformComposition.Restore(new(bands.Skip(1).Take(4).ToArray(), events));
        var whole = EventWaveformComposition.Restore(new(bands.Take(5).ToArray(), events));
        Check.That(bands[0].Trigger == PhysiologyCycleEventKind.AtrialMechanical && bands.Skip(1).Take(4).All(b => b.Trigger == PhysiologyCycleEventKind.VentricularMechanical), "CVP atrial and ventricular triggers remain independent");
        for (long t = 800_000_000; t < 1_500_000_000; t += 10_000_000)
        {
            Check.That(atrial.EvaluateAt(t) == atrial.EvaluateAt(t + 800_000_000), "a wave repeats at atrial period");
            Check.That(ventricular.EvaluateAt(t) == ventricular.EvaluateAt(t + 600_000_000), "c/x/v/y repeat at ventricular period including delayed y");
            Check.That(whole.EvaluateAt(t) == atrial.EvaluateAt(t) + ventricular.EvaluateAt(t), "CVP superposition preserves phase drift without artificial cannon a gain");
        }
        Check.That(atrial.EvaluateAt(140_000_000) > 0 && atrial.EvaluateAt(515_000_000) == 0, "a wave is not retriggered by each ventricle");
        var resp = new RespirationPlan(1000, 200).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(resp.Bands[1].DurationNs == 600_000_000 && resp.Bands[1].Trigger == PhysiologyCycleEventKind.VentricularMechanical, "cardiac artifact fits independent600ms ventricle");
        var artifact = EventWaveformComposition.Restore(new([resp.Bands[1]], events));
        for (long t = 0; t < 3_000_000_000; t += 4_000_000)
            Check.That(Math.Abs(artifact.EvaluateAt(t)) <= 200 * Q, "artifact cannot exceed reserved single-pulse amplitude");
        var normal = new RespirationPlan(1000, 200).CreateChannel(CompleteAvBlockVentricularReference.CreatePlan(), Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(normal.Bands[1].DurationNs == 800_000_000, "slow escape retains original artifact support");
    }

}
