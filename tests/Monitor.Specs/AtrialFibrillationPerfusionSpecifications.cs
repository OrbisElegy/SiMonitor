// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AtrialFibrillationPerfusionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FibrillationPerfusionFollowsOriginalRrAndIndependentSums), FibrillationPerfusionFollowsOriginalRrAndIndependentSums),
        new(nameof(FibrillationPerfusionRestoresAndRejectsConflicts), FibrillationPerfusionRestoresAndRejectsConflicts),
    ];
    private static VascularPressurePlan Pressure => new(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000, UseAtrialFibrillationPerfusion: true);
    private static PlethRunoffPlan Pleth => new(80_000_000, 512_000_000, 1250, UseAtrialFibrillationPerfusion: true);
    private static int Gain(PhysiologyCycleEvent[] events, int i) => i == 0 ? 800 :
        events[i].SimTimeNs - events[i - 1].SimTimeNs < 600_000_000 ? 400 : events[i].SimTimeNs - events[i - 1].SimTimeNs < 900_000_000 ? 800 : 1000;

    private static void FibrillationPerfusionFollowsOriginalRrAndIndependentSums()
    {
        var plan = AtrialFibrillationReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(60_000_000_000, 500)
            .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        int[] gains = events.Select((e, i) => Gain(events, i)).ToArray();
        Check.That(gains.Distinct().Order().SequenceEqual([400, 800, 1000]), "authored schedule exercises weak, reference and stronger input");
        foreach (bool fine in new[] { false, true })
        {
            var physiology = AtrialFibrillationReference.CreatePlan(fine);
            for (int i = 0; i < events.Length; i++)
            { Check.That(AtrialFibrillationPerfusion.GainPermille(physiology.ConductionPattern, events[i].CycleIndex) == gains[i], "gain follows actual prior RR and coarse/fine share perfusion"); }
            var source = PlethRunoffSource.Create(physiology, Pleth);
            // Independent regular single-ejection basis, shifted to actual AF events.
            var singlePlan = new RegularPhysiologyPlan(0, 800_000_000, 80_000_000, 80_000_000, 160_000_000,
                3_750_000_000, 1_875_000_000, VentricularMechanicalEnabled: false, MechanicalAfterCycles: 1);
            var basis = PlethRunoffSource.Create(singlePlan, Pleth with { UseAtrialFibrillationPerfusion = false });
            var pressure = VascularPressureSource.Create(physiology, Pressure);
            for (long t = 0; t < 60_000_000_000; t += 20_000_000)
            {
                long expectedPleth = 0;
                double sourceTime = Math.Max(0, t - Pressure.TransitDelayNs);
                double expectedPressure = 1000 + 7000 * Math.Exp(-sourceTime / Pressure.TimeConstantNs);
                for (int i = 0; i < events.Length; i++)
                {
                    long age = t - events[i].SimTimeNs;
                    if (age >= 0)
                    { expectedPleth += (long)FixedPointMath.RoundDivideTiesToEven((Int128)basis.EvaluateAt(age + 160_000_000) * gains[i], 1000); }
                    double elapsed = sourceTime - events[i].SimTimeNs;
                    if (elapsed < 0) { continue; }
                    double input = elapsed <= Pressure.EjectionDurationNs ? 1 - Math.Exp(-elapsed / Pressure.TimeConstantNs) :
                        (1 - Math.Exp(-(double)Pressure.EjectionDurationNs / Pressure.TimeConstantNs)) * Math.Exp(-(elapsed - Pressure.EjectionDurationNs) / Pressure.TimeConstantNs);
                    expectedPressure += 30000 * gains[i] / 1000.0 * input;
                }
                Check.That(source.EvaluateAt(t) == expectedPleth, "weighted Pleth pulses superpose with old runoff and full nominal duration");
                Check.That(Math.Abs(pressure.EvaluateAt(t) / (double)FixedPointMath.Q32One - expectedPressure) < 0.00001,
                    "independent weighted RC inputs agree without resetting residual pressure");
            }
        }
        var absent = PlethRunoffSource.Create(plan with { VentricularMechanicalEnabled = false }, Pleth);
        Check.That(absent.EvaluateAt(60_000_000_000) == 0, "RR weights do not invent ejection");
        var late = RegularPhysiologyTimeline.Start(plan).CaptureState() with { CursorSimTimeNs = 3_600_000_000_000 };
        var lateEvents = RegularPhysiologyTimeline.Restore(late).AdvanceBefore(3_610_000_000_000, 100)
            .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        for (int i = 1; i < lateEvents.Length; i++)
        { Check.That(AtrialFibrillationPerfusion.GainPermille(plan.ConductionPattern, lateEvents[i].CycleIndex) == Gain(lateEvents, i), "late queries keep original beat identity and preceding RR"); }
    }

    private static void FibrillationPerfusionRestoresAndRejectsConflicts()
    {
        var plan = AtrialFibrillationReference.CreatePlan();
        var plethId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var pressureId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(plethId, plethId, 1, 1, 1, 0, 32,
            [new(plan, new(plethId, "AcqPleth125@1", 1, 1, 0, 1), [], 250, 0, PlethRunoff: Pleth),
             (Pressure with { Morphology = new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000, MaximumPulseOverlap: 2) }).CreateChannel(plan, pressureId, 0)]);
        var whole = Group().AdvanceTo(6_000_000_000, 750, 30, 200);
        var group = Group();
        List<byte[]> split = [];
        for (int i = 1; i <= 30; i++)
        {
            split.AddRange(group.AdvanceTo(i * 200_000_000L, 25, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(whole.Count == split.Count && whole.Zip(split).All(p => p.First.SequenceEqual(p.Second)), "weighted pressure and Pleth preserve pending samples and wire bytes");
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool rejected = false;
        try { group.AdvanceTo(8_000_000_000, 250, 1, 100); } catch (PhysiologyWaveformGroupException) { rejected = true; }
        Check.That(rejected && before == JsonSerializer.Serialize(group.CaptureState()), "late failure is atomic");
        var state = group.CaptureState();
        Reject(() => PhysiologyWaveformGroup.Restore(state with
        {
            Channels = state.Channels.Select(c => c.Generator.PlethRunoff is null ? c : c with
            { Generator = c.Generator with { PlethRunoff = Pleth with { UseAtrialFibrillationPerfusion = false } } }).ToArray()
        }));
        Reject(() => VascularPressureSource.Create(plan, Pressure with { UsePrematureBeatPerfusion = true }));
        Reject(() => PlethRunoffSource.Create(plan, Pleth with { UsePrematureBeatPerfusion = true }));
        var sinus = plan with { ConductionPattern = AvConductionPattern.FixedPr };
        Reject(() => VascularPressureSource.Create(sinus, Pressure));
        Reject(() => PlethRunoffSource.Create(sinus, Pleth));
        Reject(() => AtrialFibrillationPerfusion.GainPermille(AvConductionPattern.FixedPr, 0));
        Check.That(AtrialFibrillationPerfusion.GainPermille(plan.ConductionPattern, ulong.MaxValue) is 400 or 800 or 1000,
            "original ordinal arithmetic remains bounded at ulong endpoint");
        static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid mode, double weighting or tampered pending values reject");
        }
    }
}
