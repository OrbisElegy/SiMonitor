// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PhysiologyWaveformGroupSpecifications
{
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Resp = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private const long Q = FixedPointMath.Q32One;
    private static PhysiologyWaveformGroup Start(bool reverse = false)
    {
        RegularPhysiologyPlan plan = new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
        PhysiologyWaveformChannelPlan[] channels =
        [new(plan, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), [new(PhysiologyCycleEventKind.VentricularElectrical, 0, 80_000_000, [0, -200*Q, 1000*Q, -100*Q])], 10, 1),
         new(plan, new(Resp, "AcqResp125@1", 1, 1, 0, 1), [new(PhysiologyCycleEventKind.InspirationStart, 0, 3_750_000_000, [0, 500*Q, 1000*Q, 500*Q])], 10, 3)];
        return PhysiologyWaveformGroup.Start(Ecg, Resp, 1, 7, 1, 0, 32, reverse ? channels.Reverse().ToArray() : channels);
    }
    public static Specification[] All =>
    [
        new(nameof(PhysiologyBlocksWaitForAllNativePlanes), PhysiologyBlocksWaitForAllNativePlanes),
        new(nameof(PhysiologyGroupRecoveryPreservesWire), PhysiologyGroupRecoveryPreservesWire),
        new(nameof(PhysiologyGroupRejectsFailuresAndTampering), PhysiologyGroupRejectsFailuresAndTampering),
    ];

    private static void PhysiologyBlocksWaitForAllNativePlanes()
    {
        var source = Start();
        Check.That(source.AdvanceTo(271_999_999, 68, 1, 100).Count == 0, "Resp last sample must wait until 272ms");
        var block = WaveformEnvelopeCodec.Decode(source.AdvanceTo(272_000_000, 1, 1, 100).Single());
        Check.That(block.StartSimTimeNs == 0 && block.Planes[0].Samples.Count == 50 && block.Planes[1].Samples.Count == 25 &&
            block.Planes[0].Samples.Any(value => value < 0) && block.Planes[1].Samples[^1] > block.Planes[1].Samples[0] &&
            block.Planes[1].QualityRanges.Single() == new WaveformQualityRange(0, 25, 3),
            "event morphology, native rates and explicit quality survive shared block encoding");
    }

    private static void PhysiologyGroupRecoveryPreservesWire()
    {
        var whole = Start();
        var expected = whole.AdvanceTo(4_000_000_000, 1000, 20, 100);
        var split = Start(reverse: true);
        List<byte[]> actual = [];
        for (int batch = 1; batch <= 20; batch++)
        {
            actual.AddRange(split.AdvanceTo(batch * 200_000_000L, 50, 1, 100));
            split = PhysiologyWaveformGroup.Restore(split.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)) && Snapshot(whole) == Snapshot(split),
            "channel order, batch partition and full recovery preserve wire and pending state");
    }

    private static void PhysiologyGroupRejectsFailuresAndTampering()
    {
        var source = Start();
        string before = Snapshot(source);
        Reject(() => source.AdvanceTo(600_000_000, 150, 1, 100), "PhysiologyGroup.BlockLimitExceeded");
        Check.That(Snapshot(source) == before, "late block overflow must roll back every channel");
        _ = source.AdvanceTo(200_000_000, 50, 1, 100);
        var state = source.CaptureState();
        var first = state.Channels[0];
        var pending = first.Delay.PendingSamples.ToArray();
        pending[0] = pending[0] with { NormalizedValue = 1234 };
        Reject(() => PhysiologyWaveformGroup.Restore(state with { Channels = new[] { first with { Delay = first.Delay with { PendingSamples = pending } }, state.Channels[1] } }), "PhysiologyGroup.InvalidCheckpoint");
        var second = state.Channels[1];
        var changed = second.Generator.Timeline with { Plan = second.Generator.Timeline.Plan with { HeartPeriodNs = 900_000_000 } };
        Reject(() => PhysiologyWaveformGroup.Restore(state with { Channels = new[] { first, second with { Generator = second.Generator with { Timeline = changed } } } }), "PhysiologyGroup.InvalidCheckpoint");
        Check.That(Snapshot(source) == Snapshot(PhysiologyWaveformGroup.Restore(state)), "pending samples and shared physiology are revalidated without corrupting valid state");
    }
    private static string Snapshot(PhysiologyWaveformGroup source) => JsonSerializer.Serialize(source.CaptureState());
    private static void Reject(Action action, string reason)
    {
        bool rejected = false;
        try { action(); }
        catch (PhysiologyWaveformGroupException exception) { rejected = exception.ReasonCode == reason; }
        Check.That(rejected, reason);
    }
}
