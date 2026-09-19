// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AtrialFibrillationDeficitSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AfDeficitOmitsOnlySelectedInputsAndRetainsRunoff), AfDeficitOmitsOnlySelectedInputsAndRetainsRunoff),
        new(nameof(AfDeficitRestoresAcrossMissingPulseAndRejectsConflicts), AfDeficitRestoresAcrossMissingPulseAndRejectsConflicts),
    ];
    private static PlethRunoffPlan Pleth => new(80_000_000, 512_000_000, 1250,
        UseAtrialFibrillationPerfusion: true, IllustrateAfSystemicPulseDeficit: true);
    private static VascularPressurePlan Pressure => new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000,
        UseAtrialFibrillationPerfusion: true, IllustrateAfSystemicPulseDeficit: true);

    private static void AfDeficitOmitsOnlySelectedInputsAndRetainsRunoff()
    {
        foreach (bool fine in new[] { false, true })
        {
            var plan = AtrialFibrillationReference.CreatePlan(fine);
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(60_000_000_000, 500)
                .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
            var omitted = events.Where((e, i) => i >= 2 && e.SimTimeNs - events[i - 1].SimTimeNs < 600_000_000 &&
                events[i - 1].SimTimeNs - events[i - 2].SimTimeNs >= 900_000_000).ToArray();
            Check.That(omitted.Select(e => e.CycleIndex).SequenceEqual([16UL, 33UL, 69UL]), "explicit long-short teaching selection uses original RR, not every short cycle");
            foreach (var e in events)
            {
                int expected = omitted.Contains(e) ? 0 : AtrialFibrillationPerfusion.GainPermille(plan.ConductionPattern, e.CycleIndex);
                Check.That(AtrialFibrillationPerfusion.GainPermille(plan.ConductionPattern, e.CycleIndex, true) == expected,
                    "only selected input becomes zero; all ECG and mechanical events remain");
            }
            var pulse = PlethRunoffSource.Create(plan, Pleth);
            var original = PlethRunoffSource.Create(plan, Pleth with { IllustrateAfSystemicPulseDeficit = false });
            var pressure = VascularPressureSource.Create(plan, Pressure);
            var originalPressure = VascularPressureSource.Create(plan, Pressure with { IllustrateAfSystemicPulseDeficit = false });
            var isolated = new RegularPhysiologyPlan(0, 800_000_000, 80_000_000, 80_000_000, 160_000_000,
                3_750_000_000, 1_875_000_000, VentricularMechanicalEnabled: false, MechanicalAfterCycles: 1);
            var single = PlethRunoffSource.Create(isolated, Pleth with { UseAtrialFibrillationPerfusion = false, IllustrateAfSystemicPulseDeficit = false });
            for (long t = 0; t < 60_000_000_000; t += 20_000_000)
            {
                long removedPleth = 0;
                double removedPressure = 0;
                foreach (var e in omitted)
                {
                    long age = t - e.SimTimeNs;
                    if (age >= 0)
                    { removedPleth += (long)FixedPointMath.RoundDivideTiesToEven((Int128)single.EvaluateAt(age + 160_000_000) * 400, 1000); }
                    double elapsed = age - Pressure.TransitDelayNs;
                    if (elapsed < 0) { continue; }
                    double input = elapsed <= Pressure.EjectionDurationNs ? 1 - Math.Exp(-elapsed / Pressure.TimeConstantNs) :
                        (1 - Math.Exp(-(double)Pressure.EjectionDurationNs / Pressure.TimeConstantNs)) * Math.Exp(-(elapsed - Pressure.EjectionDurationNs) / Pressure.TimeConstantNs);
                    removedPressure += 12000 * input;
                }
                Check.That(original.EvaluateAt(t) - pulse.EvaluateAt(t) == removedPleth, "only omitted contributions are removed, including their later tails");
                Check.That(Math.Abs((originalPressure.EvaluateAt(t) - pressure.EvaluateAt(t)) / (double)FixedPointMath.Q32One - removedPressure) < 0.00001,
                    "pressure loses only selected independent input, without deleting residual pressure");
            }
            long arrival = omitted[0].SimTimeNs + 80_000_000;
            Check.That(pulse.EvaluateAt(arrival) > 0 && pulse.EvaluateAt(arrival + 160_000_000) < pulse.EvaluateAt(arrival),
                "a missing new pulse still has the prior decaying tail");
            var shaped = VascularPressureSource.Create(plan, Pressure with { Morphology = new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000, MaximumPulseOverlap: 2) });
            Check.That(shaped.EvaluateAt(arrival + 160_000_000) < shaped.EvaluateAt(arrival), "zero gain also removes the contour and does not choose a zero reference input");
        }
    }

    private static void AfDeficitRestoresAcrossMissingPulseAndRejectsConflicts()
    {
        var plan = AtrialFibrillationReference.CreatePlan();
        var id = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var abp = Guid.Parse("33333333-3333-4333-8333-333333333333");
        PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(id, id, 1, 1, 1, 0, 32,
            [new(plan, new(id, "AcqPleth125@1", 1, 1, 0, 1), [], 250, 0, PlethRunoff: Pleth), Pressure.CreateChannel(plan, abp, 0)]);
        var continuous = Group(); var recovered = Group();
        for (int i = 1; i <= 85; i++)
        {
            var left = continuous.AdvanceTo(i * 200_000_000L, 25, 1, 100);
            var right = recovered.AdvanceTo(i * 200_000_000L, 25, 1, 100);
            Check.That(left.Count == right.Count && left.Zip(right).All(p => p.First.SequenceEqual(p.Second)), "wire bytes survive missing input, retained tails and next beat");
            recovered = PhysiologyWaveformGroup.Restore(recovered.CaptureState());
            if (i == 70)
            {
                var state = recovered.CaptureState();
                Reject(() => PhysiologyWaveformGroup.Restore(state with
                {
                    Channels = state.Channels.Select(c => c.Generator.PlethRunoff is null ? c : c with
                    { Generator = c.Generator with { PlethRunoff = Pleth with { IllustrateAfSystemicPulseDeficit = false } } }).ToArray()
                }));
            }
        }
        string before = JsonSerializer.Serialize(recovered.CaptureState());
        Reject(() => recovered.AdvanceTo(19_000_000_000, 250, 1, 100));
        Check.That(before == JsonSerializer.Serialize(recovered.CaptureState()), "failed block budget preserves source state");
        Reject(() => PlethRunoffSource.Create(plan, Pleth with { UseAtrialFibrillationPerfusion = false }));
        Reject(() => VascularPressureSource.Create(plan, Pressure with { UseAtrialFibrillationPerfusion = false }));
        foreach (ulong ordinal in new[] { 0UL, 1UL, ulong.MaxValue })
        { Check.That(AtrialFibrillationPerfusion.GainPermille(plan.ConductionPattern, ordinal, true) is 0 or 400 or 800 or 1000, "startup and endpoint arithmetic cannot underflow"); }
        var late = RegularPhysiologyTimeline.Start(plan).CaptureState() with { CursorSimTimeNs = 3_600_000_000_000 };
        var lateEvents = RegularPhysiologyTimeline.Restore(late).AdvanceBefore(3_620_000_000_000, 100)
            .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        for (int i = 2; i < lateEvents.Length; i++)
        {
            bool missing = lateEvents[i].SimTimeNs - lateEvents[i - 1].SimTimeNs < 600_000_000 && lateEvents[i - 1].SimTimeNs - lateEvents[i - 2].SimTimeNs >= 900_000_000;
            Check.That((AtrialFibrillationPerfusion.GainPermille(plan.ConductionPattern, lateEvents[i].CycleIndex, true) == 0) == missing, "late query uses two original RR intervals without an epoch scan");
        }
        static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "inconsistent source or pending state rejects");
        }
    }
}
