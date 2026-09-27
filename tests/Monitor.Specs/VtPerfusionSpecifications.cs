// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VtPerfusionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    public static Specification[] All =>
    [
        new(nameof(VtFillingLimitsPressureWithoutChangingOtherRhythms), VtFillingLimitsPressureWithoutChangingOtherRhythms),
        new(nameof(VtPerfusionPreservesSupportsAndBoundsOverlap), VtPerfusionPreservesSupportsAndBoundsOverlap),
        new(nameof(VtPerfusionIsPeriodicAtLateTimesAndRetainsRunoff), VtPerfusionIsPeriodicAtLateTimesAndRetainsRunoff),
        new(nameof(VtVenousAndRespiratoryComponentsRespectIndependentClocks), VtVenousAndRespiratoryComponentsRespectIndependentClocks),
    ];
    private static void VtFillingLimitsPressureWithoutChangingOtherRhythms()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var corrected = VascularPressureSource.Create(plan, VtPerfusionReference.Arterial);
        var old = VascularPressureSource.Create(plan, VtPerfusionReference.Arterial with
        { EjectionEquilibriumCentiMmHg = 30000, Morphology = VtPerfusionReference.Arterial.Morphology! with { PulseHeightCentiMmHg = 4000 } });
        var before = new List<double>(); var after = new List<double>();
        for (long t = 300_000_000_000; t < 300_375_000_000; t += 1_000_000)
        { before.Add(old.EvaluateAt(t) / (double)Q / 100); after.Add(corrected.EvaluateAt(t) / (double)Q / 100); }
        Console.WriteLine($"VT steady ABP old {before.Min():F2}-{before.Max():F2}, filling-limited {after.Min():F2}-{after.Max():F2} mmHg");
        Check.That(before.Max() > 220 && after.Max() < 160 && after.Min() > 60 && after.Max() - after.Min() > 5,
            "fixed VT no longer pumps full reference volume at fast rate");
        Check.That(FillingLimitedEjection.StrokeVolumePermille(375_000_000) > SvtPerfusionReference.StrokeVolumePermille(300_000_000),
            "VT uses its own filling interval rather than SVT gain");
        Check.That(FixedPerfusionPresets.SinglePulse.Arterial.EjectionEquilibriumCentiMmHg == 30000,
            "unrelated preset inputs remain unchanged");
        foreach (long invalid in new[] { -1L, 240_000_000L, 800_000_001L })
        {
            bool rejected = false;
            try { FillingLimitedEjection.StrokeVolumePermille(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check.That(rejected, "unsupported filling intervals reject rather than inventing zero-flow extrapolation");
        }
    }
    private static void VtPerfusionPreservesSupportsAndBoundsOverlap()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        Check.That(VtPerfusionReference.Pleth.PulseDurationNs == 512_000_000 && VtPerfusionReference.Arterial.Morphology!.DurationNs == 600_000_000 && VtPerfusionReference.Pulmonary.Morphology!.DurationNs == 640_000_000, "full morphology supports retained above375ms RR");
        _ = VascularPressureSource.Create(plan, VtPerfusionReference.Arterial);
        _ = VascularPressureSource.Create(plan, VtPerfusionReference.Pulmonary);
        _ = VtPerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        foreach (var pressure in new[] { VtPerfusionReference.Arterial with { Morphology = VtPerfusionReference.Arterial.Morphology! with { MaximumPulseOverlap = 1 } }, VtPerfusionReference.Pulmonary with { Morphology = VtPerfusionReference.Pulmonary.Morphology! with { MaximumPulseOverlap = 1 } } })
        {
            try { VascularPressureSource.Create(plan, pressure); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Insufficient VT pressure overlap accepted.");
        }
    }

    private static void VtPerfusionIsPeriodicAtLateTimesAndRetainsRunoff()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var pleth = PlethRunoffSource.Create(plan, VtPerfusionReference.Pleth);
        Check.That(pleth.MaximumHistoryEvents < 100 && pleth.SupportNs < 27_000_000_000, "optical history bounded independently of run duration");
        foreach (var pressure in new[] { VtPerfusionReference.Arterial, VtPerfusionReference.Pulmonary })
        {
            Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + 374_999_999) / 375_000_000 < 700, "pressure finite history under700 events");
            var source = VascularPressureSource.Create(plan, pressure);
            var values = new List<long>();
            for (long phase = 0; phase < 375_000_000; phase += 10_000_000)
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
        var single = PlethRunoffSource.Create(singlePlan, VtPerfusionReference.Pleth);
        long time = 1_100_000_000;
        Check.That(Math.Abs(pleth.EvaluateAt(time) - single.EvaluateAt(time) - single.EvaluateAt(time - 375_000_000) - single.EvaluateAt(time - 750_000_000)) <= 3, "overlapping optical pulses retain earlier tails");
        var interrupted = VascularPressureSource.Create(singlePlan, VtPerfusionReference.Arterial);
        Check.That(interrupted.EvaluateAt(800_000_000) > VtPerfusionReference.Arterial.AsymptoticPressureCentiMmHg * Q, "missing subsequent ejection retains pressure runoff");
    }
    private static void VtVenousAndRespiratoryComponentsRespectIndependentClocks()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var channel = VtPerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var bands = channel.Bands;
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 100);
        var atrial = EventWaveformComposition.Restore(new([bands[0]], events));
        var ventricular = EventWaveformComposition.Restore(new(bands.Skip(1).Take(4).ToArray(), events));
        var whole = EventWaveformComposition.Restore(new(bands.Take(5).ToArray(), events));
        Check.That(bands[0].Trigger == PhysiologyCycleEventKind.AtrialMechanical && bands.Skip(1).Take(4).All(b => b.Trigger == PhysiologyCycleEventKind.VentricularMechanical), "CVP atrial and ventricular triggers remain independent");
        for (long t = 800_000_000; t < 1_500_000_000; t += 10_000_000)
        {
            Check.That(atrial.EvaluateAt(t) == atrial.EvaluateAt(t + 800_000_000), "a wave repeats at atrial period");
            Check.That(ventricular.EvaluateAt(t) == ventricular.EvaluateAt(t + 375_000_000), "c/x/v/y repeat at ventricular period including delayed y");
            Check.That(whole.EvaluateAt(t) == atrial.EvaluateAt(t) + ventricular.EvaluateAt(t), "CVP superposition preserves phase drift without artificial cannon a gain");
        }
        Check.That(atrial.EvaluateAt(140_000_000) > 0 && atrial.EvaluateAt(515_000_000) == 0, "a wave is not retriggered by each ventricle");
        var resp = new RespirationPlan(1000, 200).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(resp.Bands[1].DurationNs == 375_000_000 && resp.Bands[1].Trigger == PhysiologyCycleEventKind.VentricularMechanical, "cardiac artifact fits fast independent ventricle");
        var artifact = EventWaveformComposition.Restore(new([resp.Bands[1]], events));
        for (long t = 0; t < 3_000_000_000; t += 4_000_000)
            Check.That(Math.Abs(artifact.EvaluateAt(t)) <= 200 * Q, "artifact cannot exceed reserved single-pulse amplitude");
        var normal = new RespirationPlan(1000, 200).CreateChannel(CompleteAvBlockVentricularReference.CreatePlan(), Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        Check.That(normal.Bands[1].DurationNs == 800_000_000, "slow escape retains original artifact support");
    }

}
