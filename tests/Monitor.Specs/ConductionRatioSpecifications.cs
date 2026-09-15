// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ConductionRatioSpecifications
{
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static RegularPhysiologyPlan Plan(int ratio = 1) => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000,
        4_000_000_000, 2_000_000_000, VentricularConductionRatio: ratio);
    public static Specification[] All =>
    [
        new(nameof(ConductionPreservesAtriaAndRespiration), ConductionPreservesAtriaAndRespiration),
        new(nameof(UnconductedAtrialCyclesHaveNoVentricularWaves), UnconductedAtrialCyclesHaveNoVentricularWaves),
        new(nameof(ConductedNativeSignalsRecoverTogether), ConductedNativeSignalsRecoverTogether),
        new(nameof(ConductionBoundsDefaultsAndAtomicFailure), ConductionBoundsDefaultsAndAtomicFailure),
    ];

    private static void ConductionPreservesAtriaAndRespiration()
    {
        var normal = RegularPhysiologyTimeline.Start(Plan()).AdvanceBefore(9_600_000_000, 100);
        foreach (int ratio in new[] { 1, 2, 3, 4 })
        {
            var events = RegularPhysiologyTimeline.Start(Plan(ratio)).AdvanceBefore(9_600_000_000, 100);
            Check.That(events.Where(e => e.Kind is not (PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical))
                .SequenceEqual(normal.Where(e => e.Kind is not (PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical))),
                "atrial electrical/mechanical and respiratory events preserve original clocks");
            var electrical = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            var mechanical = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
            Check.That(electrical.Length == 12 / ratio && mechanical.Length == electrical.Length && electrical.Zip(mechanical).All(pair =>
                pair.Second.SimTimeNs - pair.First.SimTimeNs == 80_000_000 && pair.First.CycleIndex == pair.Second.CycleIndex),
                "only conducted ventricular cycles produce paired electrical and mechanical events");
            Check.That(electrical.Select(e => e.SimTimeNs).SequenceEqual(Enumerable.Range(0, 12 / ratio).Select(i => i * ratio * 800_000_000L + 160_000_000)),
                "fixed PR offset and conducted ventricular period are explicit");
        }
    }

    private static PhysiologyWaveformGroup Group(int ratio) => PhysiologyWaveformGroup.Start(Ecg, Pleth, 1, 1, 1, 0, 50,
        [new(Plan(ratio), new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
         new(Plan(ratio), new(Pleth, "AcqPleth125@1", 1, 1, 0, 1), new PlethPulsePlan(80_000_000, 512_000_000, 1000).CreateBands(), 250, 0),
         new RespirationPlan(800).CreateChannel(Plan(ratio), Resp, 0)]);
    private static short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
        .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();

    private static void UnconductedAtrialCyclesHaveNoVentricularWaves()
    {
        var normal = Group(1).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var slowed = Group(2).AdvanceTo(8_000_000_000, 2000, 40, 100);
        short[] ecg = Samples(slowed, Ecg);
        Check.That(ecg.Skip(200).Take(25).Any(value => value > 0) && ecg.Skip(240).Take(20).All(value => value == 0) &&
            Samples(normal, Ecg).Skip(240).Take(20).Any(value => value > 500),
            "unconducted cycle retains a P wave at0.8s but has no QRS at0.96s");
        Check.That(Samples(slowed, Pleth).Skip(140).Take(64).All(value => value == 0) &&
            Samples(normal, Pleth).Skip(140).Take(64).Any(value => value > 0),
            "peripheral pulse is absent after an unconducted atrial cycle");
        Check.That(Samples(normal, Resp).SequenceEqual(Samples(slowed, Resp)), "respiration is independent of conduction ratio");
    }

    private static void ConductedNativeSignalsRecoverTogether()
    {
        var expected = Group(3).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var group = Group(3);
        List<byte[]> actual = [];
        for (int step = 1; step <= 40; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(actual.Count == 30 && expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "conducted ECG and mechanical pulse planes recover together with unchanged native sample clocks");
    }

    private static void ConductionBoundsDefaultsAndAtomicFailure()
    {
        var json = JsonSerializer.SerializeToNode(Plan())!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.VentricularConductionRatio));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == Plan(), "old plans default to1:1 conduction");
        foreach (var plan in new[] { Plan(0), Plan(-1), Plan(int.MaxValue) with { HeartPeriodNs = long.MaxValue / 2 } })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Start(plan); }
            catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "nonpositive or overflowing conducted period rejects");
        }
        var group = Group(2);
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(8_000_000_000, 2000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "failed block publication cannot partly advance conducted channels");
    }
}
