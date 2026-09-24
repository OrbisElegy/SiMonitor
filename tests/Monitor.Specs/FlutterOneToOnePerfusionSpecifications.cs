// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class FlutterOneToOnePerfusionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    public static Specification[] All =>
    [
        new(nameof(FlutterSuppressesNormalVenousAAndKeepsVentricularPhase), FlutterSuppressesNormalVenousAAndKeepsVentricularPhase),
        new(nameof(FlutterOneToOnePerfusionPreservesSupportsAndBoundsOverlap), FlutterOneToOnePerfusionPreservesSupportsAndBoundsOverlap),
        new(nameof(FlutterOneToOnePerfusionIsPeriodicAtLateTimesAndRetainsRunoff), FlutterOneToOnePerfusionIsPeriodicAtLateTimesAndRetainsRunoff),
    ];
    private static void FlutterSuppressesNormalVenousAAndKeepsVentricularPhase()
    {
        var plan = AtrialFlutterReference.CreatePlan(1);
        var channel = FlutterOneToOnePerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        var aOnly = channel.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialMechanical).ToArray();
        var atrial = PhysiologySignalGenerator.Start(plan, "AcqPressure125@1", 1, aOnly).GenerateBefore(2_000_000_000, 250, 100);
        Check.That(atrial.All(s => s.NormalizedValue == 0), "no invented normal atrial contraction at flutter rate");
        var full = PhysiologySignalGenerator.Start(plan, "AcqPressure125@1", 1, channel.Bands).GenerateBefore(2_000_000_000, 250, 100);
        var withoutA = PhysiologySignalGenerator.Start(plan, "AcqPressure125@1", 1, channel.Bands.Where(b => b.Trigger != PhysiologyCycleEventKind.AtrialMechanical).ToArray()).GenerateBefore(2_000_000_000, 250, 100);
        Check.That(full.SequenceEqual(withoutA) && full.Select(s => s.NormalizedValue).Distinct().Count() > 10, "CVP retains ventricular and respiratory modulation only");
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_000_000_000, 100);
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 160_000_000, 360_000_000, 560_000_000, 760_000_000, 960_000_000 }), "one ejection per conducted flutter cycle");
    }
    private static void FlutterOneToOnePerfusionPreservesSupportsAndBoundsOverlap()
    {
        var plan = AtrialFlutterReference.CreatePlan(1);
        Check.That(FlutterOneToOnePerfusionReference.Pleth.PulseDurationNs == 512_000_000 && FlutterOneToOnePerfusionReference.Arterial.Morphology!.DurationNs == 600_000_000 && FlutterOneToOnePerfusionReference.Pulmonary.Morphology!.DurationNs == 640_000_000, "full morphology supports retained above200ms RR");
        _ = VascularPressureSource.Create(plan, FlutterOneToOnePerfusionReference.Arterial);
        _ = VascularPressureSource.Create(plan, FlutterOneToOnePerfusionReference.Pulmonary);
        _ = FlutterOneToOnePerfusionReference.Venous.CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        foreach (var pressure in new[] { FlutterOneToOnePerfusionReference.Arterial with { Morphology = FlutterOneToOnePerfusionReference.Arterial.Morphology! with { MaximumPulseOverlap = 2 } }, FlutterOneToOnePerfusionReference.Pulmonary with { Morphology = FlutterOneToOnePerfusionReference.Pulmonary.Morphology! with { MaximumPulseOverlap = 3 } } })
        {
            try { VascularPressureSource.Create(plan, pressure); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Insufficient fast flutter pressure overlap accepted.");
        }
        try { (FlutterOneToOnePerfusionReference.Venous with { MaximumComponentOverlap = 1 }).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0); }
        catch (EventWaveformException e) when (e.ReasonCode == "Cvp.InvalidPlan") { return; }
        throw new InvalidOperationException("Insufficient fast flutter venous overlap accepted.");
    }
    private static void FlutterOneToOnePerfusionIsPeriodicAtLateTimesAndRetainsRunoff()
    {
        var plan = AtrialFlutterReference.CreatePlan(1);
        var pleth = PlethRunoffSource.Create(plan, FlutterOneToOnePerfusionReference.Pleth);
        Check.That(pleth.MaximumHistoryEvents < 150 && pleth.SupportNs < 27_000_000_000, "optical history bounded independently of run duration");
        foreach (var pressure in new[] { FlutterOneToOnePerfusionReference.Arterial, FlutterOneToOnePerfusionReference.Pulmonary })
        {
            Check.That((pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs + 199_999_999) / 200_000_000 < 1000, "pressure finite history under1000 events");
            var source = VascularPressureSource.Create(plan, pressure);
            var values = new List<long>();
            for (long phase = 0; phase < 200_000_000; phase += 10_000_000)
            {
                long a = source.EvaluateAt(200_000_000_000 + phase);
                long b = source.EvaluateAt(86_400_000_000_000 + phase);
                Check.That(Math.Abs(a - b) <= Q && a > pressure.AsymptoticPressureCentiMmHg * Q && a < short.MaxValue * Q, "pressure remains periodic and unsaturated after one day");
                values.Add(a);
                Check.That(Math.Abs(pleth.EvaluateAt(200_000_000_000 + phase) - pleth.EvaluateAt(86_400_000_000_000 + phase)) <= Q, "pleth evaluation at late ordinal is phase-equivalent");
            }
            Check.That(values.Max() - values.Min() > Q, "fast pressure pulses retain modulation");
        }
        var singlePlan = plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 };
        var single = PlethRunoffSource.Create(singlePlan, FlutterOneToOnePerfusionReference.Pleth);
        long time = 800_000_000;
        Check.That(Math.Abs(pleth.EvaluateAt(time) - single.EvaluateAt(time) - single.EvaluateAt(time - 200_000_000) - single.EvaluateAt(time - 400_000_000)) <= 3, "overlapping optical pulses retain earlier tails");
        var interrupted = VascularPressureSource.Create(singlePlan, FlutterOneToOnePerfusionReference.Arterial);
        Check.That(interrupted.EvaluateAt(800_000_000) > FlutterOneToOnePerfusionReference.Arterial.AsymptoticPressureCentiMmHg * Q, "missing subsequent ejection retains pressure runoff");
    }
}
