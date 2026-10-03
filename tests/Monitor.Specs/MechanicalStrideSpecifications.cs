// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Physiology;
using static Monitor.Specs.MechanicalTransitionSpecifications;

namespace Monitor.Specs;

internal static class MechanicalStrideSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(MechanicalStridePreservesElectricalAndOriginalCycleIndices), MechanicalStridePreservesElectricalAndOriginalCycleIndices),
        new(nameof(MechanicalStrideKeepsPulseTransitAndEcgSamples), MechanicalStrideKeepsPulseTransitAndEcgSamples),
        new(nameof(MechanicalStrideBoundsDefaultsAndEventBudget), MechanicalStrideBoundsDefaultsAndEventBudget),
    ];

    private static RegularPhysiologyPlan Regular => Plan(null) with { VentricularMechanicalEnabled = true };

    private static void MechanicalStridePreservesElectricalAndOriginalCycleIndices()
    {
        foreach (int conduction in new[] { 1, 3 })
        {
            foreach (int stride in new[] { 1, 2, 3, 4 })
            {
                var plan = Regular with { EpochAnchorSimTimeNs = 17, VentricularConductionRatio = conduction };
                var normal = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(12_000_000_000, 100);
                var actual = RegularPhysiologyTimeline.Start(plan with { MechanicalEveryCycles = stride }).AdvanceBefore(12_000_000_000, 100);
                Check.That(actual.SequenceEqual(normal.Where(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical || e.CycleIndex % (ulong)stride == 0)),
                    "mechanics retain original divisible cycle indices; electrical/atrial/respiratory events remain identical");
            }
        }
        var schedule = Plan() with { MechanicalDurationCycles = 2, MechanicalEveryCycles = 2 };
        ulong[] resumed = RegularPhysiologyTimeline.Start(schedule).AdvanceBefore(6_000_000_000, 100)
            .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.CycleIndex).ToArray();
        Check.That(resumed.SequenceEqual(new ulong[] { 0, 4, 6 }), "resumption at cycle3 waits for original eligible cycle4 rather than restarting the stride");
    }

    private static void MechanicalStrideKeepsPulseTransitAndEcgSamples()
    {
        var normal = Group(Regular).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var actual = Group(Regular with { MechanicalEveryCycles = 2 }).AdvanceTo(8_000_000_000, 2000, 40, 100);
        short[] pulse = Samples(actual, Pleth);
        Check.That(Samples(actual, Ecg).SequenceEqual(Samples(normal, Ecg)), "native ECG remains exact while fewer mechanical pulses occur");
        Check.That(pulse.Take(229).SequenceEqual(Samples(normal, Pleth).Take(229)) &&
            pulse.Skip(229).Take(136).All(value => value == 0) && pulse.Skip(365).Take(64).Any(value => value > 0),
            "retained delayed pulses finish; skipped cycle has no pulse; next accepted pulse retains transit");
    }

    private static void MechanicalStrideBoundsDefaultsAndEventBudget()
    {
        var json = JsonSerializer.SerializeToNode(Regular)!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.MechanicalEveryCycles));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == Regular, "old plans preserve every mechanical cycle");
        foreach (int invalid in new[] { 0, -1 })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Start(Regular with { MechanicalEveryCycles = invalid }); }
            catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "nonpositive mechanical stride rejects");
        }
        var plan = Regular with { MechanicalEveryCycles = int.MaxValue };
        var expected = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100);
        var timeline = RegularPhysiologyTimeline.Start(plan);
        Check.That(timeline.AdvanceBefore(8_000_000_000, expected.Count).SequenceEqual(expected), "event budget counts selected mechanical events only");
        timeline = RegularPhysiologyTimeline.Start(plan);
        var before = timeline.CaptureState();
        bool limited = false;
        try { timeline.AdvanceBefore(8_000_000_000, expected.Count - 1); }
        catch (PhysiologyTimelineException exception) { limited = exception.ReasonCode == "PhysiologyTimeline.EventLimitExceeded"; }
        Check.That(limited && timeline.CaptureState() == before, "late event budget failure retains the original cursor");
    }
}
