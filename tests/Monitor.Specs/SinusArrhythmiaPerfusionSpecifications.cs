// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SinusArrhythmiaPerfusionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    public static Specification[] All =>
    [
        new(nameof(SinusArrhythmiaPerfusionPreservesSupportsAndBoundsOverlap), SinusArrhythmiaPerfusionPreservesSupportsAndBoundsOverlap),
        new(nameof(SinusArrhythmiaPerfusionIsPeriodicAtLateTimesAndRetainsRunoff), SinusArrhythmiaPerfusionIsPeriodicAtLateTimesAndRetainsRunoff),
        new(nameof(SinusArrhythmiaVenousAndRespiratoryComponentsRespectConductedClocks), SinusArrhythmiaVenousAndRespiratoryComponentsRespectConductedClocks),
    ];
    private static void SinusArrhythmiaPerfusionPreservesSupportsAndBoundsOverlap()
    {
        var plan = SinusArrhythmiaReference.CreatePlan();
        Check.That(FixedPerfusionPresets.PulmonaryOverlap.Pleth.PulseDurationNs == 512_000_000 && FixedPerfusionPresets.PulmonaryOverlap.Arterial.Morphology!.DurationNs == 600_000_000 && FixedPerfusionPresets.PulmonaryOverlap.Pulmonary.Morphology!.DurationNs == 640_000_000, "full morphology supports retained at600ms RR");
        _ = VascularPressureSource.Create(plan, FixedPerfusionPresets.PulmonaryOverlap.Arterial);
        _ = VascularPressureSource.Create(plan, FixedPerfusionPresets.PulmonaryOverlap.Pulmonary);
        _ = FixedPerfusionPresets.PulmonaryOverlap.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        foreach (var pressure in new[] { FixedPerfusionPresets.PulmonaryOverlap.Arterial with { Morphology = FixedPerfusionPresets.PulmonaryOverlap.Arterial.Morphology! with { DurationNs = 760_000_000 } }, FixedPerfusionPresets.PulmonaryOverlap.Pulmonary with { Morphology = FixedPerfusionPresets.PulmonaryOverlap.Pulmonary.Morphology! with { MaximumPulseOverlap = 1 } } })
        {
            try { VascularPressureSource.Create(plan, pressure); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Oversized sinus arrhythmia pressure pulse accepted.");
        }
    }

    private static void SinusArrhythmiaPerfusionIsPeriodicAtLateTimesAndRetainsRunoff()
    {
        var plan = SinusArrhythmiaReference.CreatePlan();
        var pleth = PlethRunoffSource.Create(plan, FixedPerfusionPresets.PulmonaryOverlap.Pleth);
        Check.That(pleth.MaximumHistoryEvents < 100 && pleth.SupportNs < 27_000_000_000, "optical history bounded independently of run duration");
        foreach (var pressure in new[] { FixedPerfusionPresets.PulmonaryOverlap.Arterial, FixedPerfusionPresets.PulmonaryOverlap.Pulmonary })
        {
            Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + 599_999_999) / 600_000_000 < 350, "pressure finite history under350 events");
            var source = VascularPressureSource.Create(plan, pressure);
            var values = new List<long>();
            for (long phase = 0; phase < 3_200_000_000; phase += 10_000_000)
            {
                long a = source.EvaluateAt(320_000_000_000 + phase);
                long b = source.EvaluateAt(86_400_000_000_000 + phase);
                Check.That(Math.Abs(a - b) <= Q && a > pressure.AsymptoticPressureCentiMmHg * Q && a < short.MaxValue * Q, "pressure remains periodic and unsaturated after one day");
                values.Add(a);
                Check.That(Math.Abs(pleth.EvaluateAt(320_000_000_000 + phase) - pleth.EvaluateAt(86_400_000_000_000 + phase)) <= Q, "pleth evaluation at late ordinal is phase-equivalent");
            }
            Check.That(values.Max() - values.Min() > Q, "pressure pulses retain modulation");
        }
        // The first two ejections are800ms apart, followed by1000ms.
        // At1.9s only those two optical pulses have begun. Evaluate each
        // contribution against an isolated regular event without replaying history.
        var isolatedPlan = new RegularPhysiologyPlan(0, 800_000_000, 160_000_000,
            80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000,
            VentricularMechanicalEnabled: false, MechanicalAfterCycles: 1);
        var isolated = PlethRunoffSource.Create(isolatedPlan, FixedPerfusionPresets.PulmonaryOverlap.Pleth);
        long time = 1_900_000_000;
        Check.That(Math.Abs(pleth.EvaluateAt(time) - isolated.EvaluateAt(time) - isolated.EvaluateAt(time - 800_000_000)) <= 3,
            "optical runoff preserves both irregularly spaced pulse tails");
        var pressureSource = VascularPressureSource.Create(plan, FixedPerfusionPresets.PulmonaryOverlap.Arterial);
        Check.That(pressureSource.EvaluateAt(1_800_000_000) > pressureSource.EvaluateAt(2_000_000_000) &&
            pressureSource.EvaluateAt(2_000_000_000) > FixedPerfusionPresets.PulmonaryOverlap.Arterial.AsymptoticPressureCentiMmHg * Q,
            "long RR pressure falls continuously without resetting to its floor");
    }
    private static void SinusArrhythmiaVenousAndRespiratoryComponentsRespectConductedClocks()
    {
        var plan = SinusArrhythmiaReference.CreatePlan();
        var channel = FixedPerfusionPresets.PulmonaryOverlap.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var bands = channel.Bands;
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(7_000_000_000, 100);
        var atrial = EventWaveformComposition.Restore(new([bands[0]], events));
        var ventricular = EventWaveformComposition.Restore(new(bands.Skip(1).Take(4).ToArray(), events));
        var whole = EventWaveformComposition.Restore(new(bands.Take(5).ToArray(), events));
        Check.That(bands[0].Trigger == PhysiologyCycleEventKind.AtrialMechanical && bands.Skip(1).Take(4).All(b => b.Trigger == PhysiologyCycleEventKind.VentricularMechanical), "CVP components retain distinct atrial and ventricular triggers");
        for (long t = 800_000_000; t < 1_500_000_000; t += 10_000_000)
        {
            Check.That(atrial.EvaluateAt(t) == atrial.EvaluateAt(t + 3_200_000_000), "a wave repeats at atrial period");
            Check.That(ventricular.EvaluateAt(t) == ventricular.EvaluateAt(t + 3_200_000_000), "c/x/v/y repeat at ventricular period including delayed y");
            Check.That(whole.EvaluateAt(t) == atrial.EvaluateAt(t) + ventricular.EvaluateAt(t), "CVP superposition preserves fixed atrioventricular delay without artificial gain");
        }
        Check.That(atrial.EvaluateAt(300_000_000) == 0 && ventricular.EvaluateAt(300_000_000) > 0 &&
            ventricular.EvaluateAt(140_000_000) == 0, "atrial and ventricular pressure components retain80/240ms trigger separation");
        var resp = new RespirationPlan(1000, 200).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(resp.Bands[1].DurationNs == 600_000_000 && resp.Bands[1].Trigger == PhysiologyCycleEventKind.VentricularMechanical, "cardiac artifact fits600ms conducted cycle");
        var artifact = EventWaveformComposition.Restore(new([resp.Bands[1]], events));
        for (long t = 0; t < 6_400_000_000; t += 4_000_000)
            Check.That(Math.Abs(artifact.EvaluateAt(t)) <= 200 * Q, "artifact cannot exceed reserved single-pulse amplitude");
        var normal = new RespirationPlan(1000, 200).CreateChannel(CompleteAvBlockVentricularReference.CreatePlan(), Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(normal.Bands[1].DurationNs == 800_000_000, "slow escape retains original artifact support");
    }

}
