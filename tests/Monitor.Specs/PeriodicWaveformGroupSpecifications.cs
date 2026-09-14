// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Specs;

internal static class PeriodicWaveformGroupSpecifications
{
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static PeriodicWaveformChannelPlan Plan(Guid id, string profile, int capacity, uint flags) => new(
        new(profile, 7, 0, 0, 0x2000000000000000, [0, 3 * FixedPointMath.Q32One, 0, -3 * FixedPointMath.Q32One]),
        new(id, profile, 1, 1, 0, 1), capacity, flags);
    private static PeriodicWaveformGroup Start(bool reverse = false, int capacity = 16)
    {
        PeriodicWaveformChannelPlan[] channels = [Plan(Ecg, "AcqECGMonitor250@1", 10, 1), Plan(Pleth, "AcqPleth125@1", 250, 3)];
        return PeriodicWaveformGroup.Start(Ecg, Pleth, 3, 11, 100, capacity, reverse ? channels.Reverse().ToArray() : channels);
    }
    public static Specification[] All =>
    [
        new(nameof(GroupWaitsForSlowPlaneWithoutResampling), GroupWaitsForSlowPlaneWithoutResampling),
        new(nameof(GroupOrderBatchesAndRestorePreserveWire), GroupOrderBatchesAndRestorePreserveWire),
        new(nameof(GroupFailuresRollBackEveryChannel), GroupFailuresRollBackEveryChannel),
        new(nameof(GroupCheckpointRejectsMixedClocksAndTamperedSamples), GroupCheckpointRejectsMixedClocksAndTamperedSamples),
    ];

    private static void GroupWaitsForSlowPlaneWithoutResampling()
    {
        PeriodicWaveformGroup group = Start();
        Check.That(group.AdvanceTo(2_191_999_999, 550, 1).Count == 0,
            "complete blocks must wait for the slowest plane's last sample availability");
        WaveformEnvelope block = WaveformEnvelopeCodec.Decode(group.AdvanceTo(2_192_000_000, 1, 1).Single());
        Check.That(block.BlockSequence == 100 && block.StartSimTimeNs == 0 && block.DurationNs == 200_000_000 &&
            block.Planes.Count == 2 && block.Planes[0].ChannelId == Ecg && block.Planes[0].Samples.Count == 50 &&
            block.Planes[1].ChannelId == Pleth && block.Planes[1].Samples.Count == 25 &&
            block.Planes[0].SampleRateNumerator == 250 && block.Planes[1].SampleRateNumerator == 125 &&
            block.Planes[0].QualityRanges.Single() == new WaveformQualityRange(0, 50, 1) &&
            block.Planes[1].QualityRanges.Single() == new WaveformQualityRange(0, 25, 3) &&
            group.AdvanceTo(2_192_000_000, 1, 1).Count == 0,
            "shared envelopes preserve native sample counts, canonical channel order, source time and independent quality declarations");
    }

    private static void GroupOrderBatchesAndRestorePreserveWire()
    {
        PeriodicWaveformGroup whole = Start();
        IReadOnlyList<byte[]> expected = whole.AdvanceTo(2_400_000_000, 600, 2);
        PeriodicWaveformGroup split = Start(reverse: true);
        _ = split.AdvanceTo(100_000_000, 25, 1);
        var restored = PeriodicWaveformGroup.Restore(split.CaptureState());
        List<byte[]> actual = [.. restored.AdvanceTo(2_192_000_000, 523, 1)];
        actual.AddRange(restored.AdvanceTo(2_400_000_000, 52, 1));
        Check.That(expected.Count == 2 && actual.Count == 2 && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)) &&
            Snapshot(whole) == Snapshot(restored),
            "caller channel order, batch partitions and recovery must preserve byte-identical output and pending evidence");
        string before = Snapshot(restored);
        actual[0][0] ^= 0xff;
        Check.That(Snapshot(restored) == before, "returned wire arrays must not alias pending group state");
    }

    private static void GroupFailuresRollBackEveryChannel()
    {
        PeriodicWaveformGroup group = Start();
        string before = Snapshot(group);
        Reject(() => group.AdvanceTo(2_400_000_000, 600, 1), "PeriodicGroup.BlockLimitExceeded");
        Reject(() => group.AdvanceTo(2_400_000_000, 600, 0), "PeriodicGroup.InvalidBlockLimit");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { group.AdvanceTo(2_400_000_000, 600, 2, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && Snapshot(group) == before && group.AdvanceTo(2_400_000_000, 600, 2).Count == 2,
            "late output overflow and cancellation must not advance any source or shared assembler state");
        PeriodicWaveformGroup bounded = Start(capacity: 1);
        string initial = Snapshot(bounded);
        bool full = false;
        try { bounded.AdvanceTo(400_000_000, 100, 1); }
        catch (WaveformBlockAssemblerException) { full = true; }
        Check.That(full && Snapshot(bounded) == initial && bounded.AdvanceTo(40_000_000, 10, 1).Count == 0,
            "fast-plane backlog limits must reject atomically and allow smaller valid advances");
    }

    private static void GroupCheckpointRejectsMixedClocksAndTamperedSamples()
    {
        PeriodicWaveformGroup group = Start();
        _ = group.AdvanceTo(100_000_000, 25, 1);
        PeriodicWaveformGroupState state = group.CaptureState();
        PeriodicWaveformChannelState slow = state.Channels[1];
        PeriodicWaveformChannelState shifted = slow with
        {
            Generator = slow.Generator with { Clock = slow.Generator.Clock with { CursorSimTimeNs = 100_000_001 } },
            Delay = slow.Delay with { ReleaseCursorSimTimeNs = 100_000_001 },
        };
        Reject(() => PeriodicWaveformGroup.Restore(state with { Channels = new[] { state.Channels[0], shifted } }), "PeriodicGroup.InvalidCheckpoint");
        Reject(() => PeriodicWaveformGroup.Restore(state with { Channels = state.Channels.Reverse().ToArray() }), "PeriodicGroup.InvalidCheckpoint");
        DelayedSignalSample[] pending = slow.Delay.PendingSamples.ToArray();
        pending[0] = pending[0] with { NormalizedValue = 99 };
        Reject(() => PeriodicWaveformGroup.Restore(state with
        { Channels = new[] { state.Channels[0], slow with { Delay = slow.Delay with { PendingSamples = pending } } } }), "PeriodicGroup.InvalidCheckpoint");
        Check.That(Snapshot(PeriodicWaveformGroup.Restore(state)) == Snapshot(group),
            "canonical, cross-clock and sample-content rejection must leave valid checkpoint recovery available");
    }

    private static string Snapshot(PeriodicWaveformGroup group) => JsonSerializer.Serialize(group.CaptureState());
    private static void Reject(Action action, string reason)
    {
        try { action(); throw new InvalidOperationException("expected group rejection"); }
        catch (PeriodicWaveformGroupException exception) { Check.That(exception.ReasonCode == reason, reason); }
    }
}
