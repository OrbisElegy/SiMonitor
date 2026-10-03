// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Physiology;
using static Monitor.Specs.MechanicalTransitionSpecifications;

namespace Monitor.Specs;

internal static class MechanicalResumptionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(MechanicalResumptionPreservesOriginalIndices), MechanicalResumptionPreservesOriginalIndices),
        new(nameof(ResumedPulsesKeepTransitAndPriorTails), ResumedPulsesKeepTransitAndPriorTails),
        new(nameof(MechanicalDurationBoundsDefaultsAndAtomicFailure), MechanicalDurationBoundsDefaultsAndAtomicFailure),
    ];

    private static void MechanicalResumptionPreservesOriginalIndices()
    {
        foreach (int ratio in new[] { 1, 3 })
        {
            var plan = Plan(1) with { EpochAnchorSimTimeNs = 17, VentricularConductionRatio = ratio, MechanicalDurationCycles = 2 };
            var normal = RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = true, MechanicalAfterCycles = null, MechanicalDurationCycles = null })
                .AdvanceBefore(12_000_000_000, 100);
            var actual = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(12_000_000_000, 100);
            Check.That(actual.SequenceEqual(normal.Where(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical || e.CycleIndex < 1 || e.CycleIndex >= 3)),
                "resumed mechanical cycles keep original identities, epoch and conduction while other events stay unchanged");
        }
    }

    private static void ResumedPulsesKeepTransitAndPriorTails()
    {
        var plan = Plan() with { MechanicalDurationCycles = 1 };
        var actual = Group(plan).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var normal = Group(plan with { VentricularMechanicalEnabled = true, MechanicalAfterCycles = null, MechanicalDurationCycles = null })
            .AdvanceTo(8_000_000_000, 2000, 40, 100);
        short[] pulse = Samples(actual, Pleth);
        short[] reference = Samples(normal, Pleth);
        Check.That(Samples(actual, Ecg).SequenceEqual(Samples(normal, Ecg)), "ECG is byte-identical throughout mechanical loss and recovery");
        Check.That(pulse.Take(229).SequenceEqual(reference.Take(229)) && pulse.Skip(200).Take(29).Any(value => value > 0),
            "old transit-delayed tail survives even beyond the1600ms resumption boundary");
        Check.That(pulse.Skip(229).Take(136).All(value => value == 0) && pulse.Skip(365).SequenceEqual(reference.Skip(365)) &&
            pulse.Skip(365).Take(64).Any(value => value > 0), "resumed event1840ms reaches Pleth2920ms, retaining1080ms transit and future original cycles");
    }

    private static void MechanicalDurationBoundsDefaultsAndAtomicFailure()
    {
        var json = JsonSerializer.SerializeToNode(Plan())!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.MechanicalDurationCycles));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == Plan(), "old cutoff plans keep mechanical suppression permanent");
        foreach (var invalid in new[] { Plan() with { MechanicalDurationCycles = 0 }, Plan(null) with { MechanicalDurationCycles = 1 },
            Plan() with { MechanicalDurationCycles = ulong.MaxValue },
            Plan() with { EpochAnchorSimTimeNs = long.MaxValue - 1_000_000_000, MechanicalDurationCycles = 1 } })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "invalid duration, absent start and overflowing resumption boundary reject");
        }
        var group = Group(Plan() with { MechanicalDurationCycles = 1 });
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(8_000_000_000, 2000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "failed publication cannot partly publish recovered mechanics");
    }
}
