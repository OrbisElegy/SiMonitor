// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class MechanicalTransitionSpecifications
{
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static RegularPhysiologyPlan Plan(ulong? cycles = 1) => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000,
        VentricularMechanicalEnabled: false, MechanicalAfterCycles: cycles);
    public static Specification[] All =>
    [
        new(nameof(MechanicalCutoffUsesVentricularCycleIndices), MechanicalCutoffUsesVentricularCycleIndices),
        new(nameof(MechanicalCutoffPreservesDelayedPulseTails), MechanicalCutoffPreservesDelayedPulseTails),
        new(nameof(MechanicalTransitionRecoversAcrossCutoff), MechanicalTransitionRecoversAcrossCutoff),
        new(nameof(MechanicalScheduleBoundsDefaultsAndAtomicFailure), MechanicalScheduleBoundsDefaultsAndAtomicFailure),
    ];

    private static void MechanicalCutoffUsesVentricularCycleIndices()
    {
        foreach (int ratio in new[] { 1, 3 })
        {
            var plan = Plan(2) with { EpochAnchorSimTimeNs = 17, VentricularConductionRatio = ratio };
            var normal = RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = true, MechanicalAfterCycles = null })
                .AdvanceBefore(8_000_000_000, 100);
            var actual = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100);
            Check.That(actual.SequenceEqual(normal.Where(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical || e.CycleIndex < 2)),
                "only mechanical cycle indices2 onward stop; epoch, conduction, ECG and respiration remain exact");
        }
    }

    private static PhysiologyWaveformGroup Group(RegularPhysiologyPlan plan) => PhysiologyWaveformGroup.Start(Ecg, Pleth, 1, 1, 1, 0, 50,
        [new(plan, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
         new(plan, new(Pleth, "AcqPleth125@1", 1, 1, 0, 1), new PlethPulsePlan(1_080_000_000, 512_000_000, 1000).CreateBands(), 250, 0)]);
    private static short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.SelectMany(bytes =>
        WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == id).Samples).ToArray();

    private static void MechanicalCutoffPreservesDelayedPulseTails()
    {
        var normal = Group(Plan() with { VentricularMechanicalEnabled = true, MechanicalAfterCycles = null }).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var actual = Group(Plan()).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var pulse = Samples(actual, Pleth);
        Check.That(Samples(actual, Ecg).SequenceEqual(Samples(normal, Ecg)), "ECG continues unchanged after mechanical cutoff");
        Check.That(pulse.Take(229).SequenceEqual(Samples(normal, Pleth).Take(229)) &&
            pulse.Skip(165).Take(64).Any(value => value > 0) && pulse.Skip(229).All(value => value == 0),
            "pulse triggered at240ms arrives1320ms, after800ms cutoff, and finishes1832ms without truncation");
        Check.That(Samples(normal, Pleth).Skip(265).Take(64).Any(value => value > 0), "normal control retains the next mechanical pulse");
    }

    private static void MechanicalTransitionRecoversAcrossCutoff()
    {
        var expected = Group(Plan()).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var group = Group(Plan());
        List<byte[]> actual = [];
        for (int step = 1; step <= 40; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(actual.Count == 30 && actual.Count == expected.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "cutoff and post-cutoff delayed tails survive native split recovery");
    }

    private static void MechanicalScheduleBoundsDefaultsAndAtomicFailure()
    {
        var json = JsonSerializer.SerializeToNode(Plan(null))!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.MechanicalAfterCycles));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == Plan(null), "old disabled plans stay immediately disabled");
        foreach (var invalid in new[] { Plan(0), Plan(ulong.MaxValue), Plan() with { VentricularMechanicalEnabled = true },
            Plan() with { CardiacActivity = CardiacActivity.AtrialOnly }, Plan() with { EpochAnchorSimTimeNs = long.MaxValue - 10 },
            Plan(ulong.MaxValue) with { HeartPeriodNs = long.MaxValue / 3, VentricularConductionRatio = 3 } })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "invalid count, target or overflowing boundary rejects");
        }
        var group = Group(Plan());
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(8_000_000_000, 2000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "failed publication cannot partly commit the mechanical cutoff");
    }
}
