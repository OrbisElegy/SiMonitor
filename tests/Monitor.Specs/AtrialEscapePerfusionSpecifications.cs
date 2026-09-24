// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AtrialEscapePerfusionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    public static Specification[] All =>
    [
        new(nameof(AtrialEscapePerfusionPreservesSupportsAndBoundsOverlap), AtrialEscapePerfusionPreservesSupportsAndBoundsOverlap),
        new(nameof(AtrialEscapePerfusionIsPeriodicAtLateTimesAndRetainsRunoff), AtrialEscapePerfusionIsPeriodicAtLateTimesAndRetainsRunoff),
        new(nameof(AtrialEscapeVenousAndRespiratoryComponentsRespectConductedClocks), AtrialEscapeVenousAndRespiratoryComponentsRespectConductedClocks),
    ];
    private static void AtrialEscapePerfusionPreservesSupportsAndBoundsOverlap()
    {
        var plan = AtrialEscapeReference.CreatePlan();
        Check.That(AtrialEscapePerfusionReference.Pleth.PulseDurationNs == 512_000_000 && AtrialEscapePerfusionReference.Arterial.Morphology!.DurationNs == 600_000_000 && AtrialEscapePerfusionReference.Pulmonary.Morphology!.DurationNs == 640_000_000, "full morphology supports retained at1200ms RR");
        _ = VascularPressureSource.Create(plan, AtrialEscapePerfusionReference.Arterial);
        _ = VascularPressureSource.Create(plan, AtrialEscapePerfusionReference.Pulmonary);
        _ = AtrialEscapePerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        foreach (var pressure in new[] { AtrialEscapePerfusionReference.Arterial with { Morphology = AtrialEscapePerfusionReference.Arterial.Morphology! with { DurationNs = 1_280_000_000 } }, AtrialEscapePerfusionReference.Pulmonary with { Morphology = AtrialEscapePerfusionReference.Pulmonary.Morphology! with { DurationNs = 1_280_000_000 } } })
        {
            try { VascularPressureSource.Create(plan, pressure); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Oversized atrial escape pressure pulse accepted.");
        }
    }

    private static void AtrialEscapePerfusionIsPeriodicAtLateTimesAndRetainsRunoff()
    {
        var plan = AtrialEscapeReference.CreatePlan();
        var pleth = PlethRunoffSource.Create(plan, AtrialEscapePerfusionReference.Pleth);
        Check.That(pleth.MaximumHistoryEvents < 100 && pleth.SupportNs < 27_000_000_000, "optical history bounded independently of run duration");
        foreach (var pressure in new[] { AtrialEscapePerfusionReference.Arterial, AtrialEscapePerfusionReference.Pulmonary })
        {
            Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + 1_199_999_999) / 1_200_000_000 < 180, "pressure finite history under180 events");
            var source = VascularPressureSource.Create(plan, pressure);
            var values = new List<long>();
            for (long phase = 0; phase < 1_200_000_000; phase += 10_000_000)
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
        var single = PlethRunoffSource.Create(singlePlan, AtrialEscapePerfusionReference.Pleth);
        long time = 1_700_000_000;
        Check.That(Math.Abs(pleth.EvaluateAt(time) - single.EvaluateAt(time) - single.EvaluateAt(time - 1_200_000_000)) <= 3, "overlapping optical pulses retain earlier tails");
        var interrupted = VascularPressureSource.Create(singlePlan, AtrialEscapePerfusionReference.Arterial);
        Check.That(interrupted.EvaluateAt(800_000_000) > AtrialEscapePerfusionReference.Arterial.AsymptoticPressureCentiMmHg * Q, "missing subsequent ejection retains pressure runoff");
    }
    private static void AtrialEscapeVenousAndRespiratoryComponentsRespectConductedClocks()
    {
        var plan = AtrialEscapeReference.CreatePlan();
        var channel = AtrialEscapePerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var bands = channel.Bands;
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 100);
        var atrial = EventWaveformComposition.Restore(new([bands[0]], events));
        var ventricular = EventWaveformComposition.Restore(new(bands.Skip(1).Take(4).ToArray(), events));
        var whole = EventWaveformComposition.Restore(new(bands.Take(5).ToArray(), events));
        Check.That(bands[0].Trigger == PhysiologyCycleEventKind.AtrialMechanical && bands.Skip(1).Take(4).All(b => b.Trigger == PhysiologyCycleEventKind.VentricularMechanical), "CVP components retain distinct atrial and ventricular triggers");
        for (long t = 800_000_000; t < 1_500_000_000; t += 10_000_000)
        {
            Check.That(atrial.EvaluateAt(t) == atrial.EvaluateAt(t + 1_200_000_000), "a wave repeats at atrial period");
            Check.That(ventricular.EvaluateAt(t) == ventricular.EvaluateAt(t + 1_200_000_000), "c/x/v/y repeat at ventricular period including delayed y");
            Check.That(whole.EvaluateAt(t) == atrial.EvaluateAt(t) + ventricular.EvaluateAt(t), "CVP superposition preserves fixed atrioventricular delay without artificial gain");
        }
        var pPrime = EventWaveformComposition.Restore(new(AtrialEscapeReference.CreateLeadIIBands()
            .Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray(), events));
        Check.That(pPrime.EvaluateAt(40_000_000) < 0 && atrial.EvaluateAt(140_000_000) > 0,
            "negative electrical P-prime does not invert mechanical a wave");
        Check.That(atrial.EvaluateAt(300_000_000) == 0 && ventricular.EvaluateAt(300_000_000) > 0 &&
            ventricular.EvaluateAt(140_000_000) == 0, "atrial and ventricular pressure components retain80/240ms trigger separation");
        var resp = new RespirationPlan(1000, 200).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(resp.Bands[1].DurationNs == 1_200_000_000 && resp.Bands[1].Trigger == PhysiologyCycleEventKind.VentricularMechanical, "cardiac artifact fits1200ms conducted cycle");
        var artifact = EventWaveformComposition.Restore(new([resp.Bands[1]], events));
        for (long t = 0; t < 3_000_000_000; t += 4_000_000)
            Check.That(Math.Abs(artifact.EvaluateAt(t)) <= 200 * Q, "artifact cannot exceed reserved single-pulse amplitude");
        var normal = new RespirationPlan(1000, 200).CreateChannel(CompleteAvBlockVentricularReference.CreatePlan(), Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(normal.Bands[1].DurationNs == 800_000_000, "slow escape retains original artifact support");
    }

}
