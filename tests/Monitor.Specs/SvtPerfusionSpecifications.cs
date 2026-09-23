// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SvtPerfusionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    public static Specification[] All =>
    [
        new(nameof(SvtPerfusionPreservesSupportsAndBoundsOverlap), SvtPerfusionPreservesSupportsAndBoundsOverlap),
        new(nameof(SvtPerfusionIsPeriodicAtLateTimesAndRetainsRunoff), SvtPerfusionIsPeriodicAtLateTimesAndRetainsRunoff),
    ];
    private static void SvtPerfusionPreservesSupportsAndBoundsOverlap()
    {
        var plan = SupraventricularTachycardiaReference.CreatePlan();
        Check.That(SvtPerfusionReference.Pleth.PulseDurationNs == 512_000_000 && SvtPerfusionReference.Arterial.Morphology!.DurationNs == 600_000_000 && SvtPerfusionReference.Pulmonary.Morphology!.DurationNs == 640_000_000, "full morphology supports retained above300ms RR");
        _ = VascularPressureSource.Create(plan, SvtPerfusionReference.Arterial);
        _ = VascularPressureSource.Create(plan, SvtPerfusionReference.Pulmonary);
        _ = SvtPerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        foreach (var pressure in new[] { SvtPerfusionReference.Arterial with { Morphology = SvtPerfusionReference.Arterial.Morphology! with { MaximumPulseOverlap = 1 } }, SvtPerfusionReference.Pulmonary with { Morphology = SvtPerfusionReference.Pulmonary.Morphology! with { MaximumPulseOverlap = 2 } } })
        {
            try { VascularPressureSource.Create(plan, pressure); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Insufficient SVT pressure overlap accepted.");
        }
        try { (SvtPerfusionReference.Venous with { MaximumComponentOverlap = 1 }).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0); }
        catch (EventWaveformException e) when (e.ReasonCode == "Cvp.InvalidPlan") { return; }
        throw new InvalidOperationException("Insufficient SVT venous overlap accepted.");
    }
    private static void SvtPerfusionIsPeriodicAtLateTimesAndRetainsRunoff()
    {
        var plan = SupraventricularTachycardiaReference.CreatePlan();
        var pleth = PlethRunoffSource.Create(plan, SvtPerfusionReference.Pleth);
        Check.That(pleth.MaximumHistoryEvents < 100 && pleth.SupportNs < 27_000_000_000, "optical history bounded independently of run duration");
        foreach (var pressure in new[] { SvtPerfusionReference.Arterial, SvtPerfusionReference.Pulmonary })
        {
            Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + 299_999_999) / 300_000_000 < 700, "pressure finite history under700 events");
            var source = VascularPressureSource.Create(plan, pressure);
            var values = new List<long>();
            for (long phase = 0; phase < 300_000_000; phase += 10_000_000)
            {
                long a = source.EvaluateAt(300_000_000_000 + phase);
                long b = source.EvaluateAt(86_400_000_000_000 + phase);
                Check.That(Math.Abs(a - b) <= Q && a > pressure.AsymptoticPressureCentiMmHg * Q && a < short.MaxValue * Q, "pressure remains periodic and unsaturated after one day");
                values.Add(a);
                Check.That(Math.Abs(pleth.EvaluateAt(300_000_000_000 + phase) - pleth.EvaluateAt(86_400_000_000_000 + phase)) <= Q, "pleth evaluation at late ordinal is phase-equivalent");
            }
            Check.That(values.Max() - values.Min() > Q, "fast pressure pulses retain modulation");
        }
        var singlePlan = plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 };
        var single = PlethRunoffSource.Create(singlePlan, SvtPerfusionReference.Pleth);
        long time = 800_000_000;
        Check.That(Math.Abs(pleth.EvaluateAt(time) - single.EvaluateAt(time) - single.EvaluateAt(time - 300_000_000) - single.EvaluateAt(time - 600_000_000)) <= 3, "overlapping optical pulses retain earlier tails");
        var interrupted = VascularPressureSource.Create(singlePlan, SvtPerfusionReference.Arterial);
        Check.That(interrupted.EvaluateAt(800_000_000) > SvtPerfusionReference.Arterial.AsymptoticPressureCentiMmHg * Q, "missing subsequent ejection retains pressure runoff");
    }
}
