// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Specs;

internal static class PeriodicWaveformPipelineSpecifications
{
    private static readonly Guid ChannelId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static PeriodicWaveformPipeline Start(int capacity = 10) => PeriodicWaveformPipeline.Start(
        new("AcqECGMonitor250@1", 7, 0, 0, 0x2000000000000000,
            [0, 3 * FixedPointMath.Q32One, 0, -3 * FixedPointMath.Q32One]),
        ChannelId, InstanceId, 3, 11, 100, new(ChannelId, "AcqECGMonitor250@1", 1, 1, 0, 1), capacity, 0x80000000);

    public static Specification[] All =>
    [
        new(nameof(StreamReleasesWholeBlocksAtAcquisitionDeadline), StreamReleasesWholeBlocksAtAcquisitionDeadline),
        new(nameof(StreamBatchingAndRecoveryPreserveWireBytes), StreamBatchingAndRecoveryPreserveWireBytes),
        new(nameof(StreamLateFailuresPreserveAllComponentState), StreamLateFailuresPreserveAllComponentState),
        new(nameof(StreamRestoreRejectsCrossComponentAndSampleTampering), StreamRestoreRejectsCrossComponentAndSampleTampering),
    ];

    private static void StreamReleasesWholeBlocksAtAcquisitionDeadline()
    {
        PeriodicWaveformPipeline stream = Start();
        Check.That(stream.AdvanceTo(235_999_999, 59, 1).Count == 0,
            "a block must wait for its final sample's 40ms acquisition delay");
        WaveformEnvelope block = WaveformEnvelopeCodec.Decode(stream.AdvanceTo(236_000_000, 1, 1).Single());
        Check.That(block.StartSimTimeNs == 0 && block.BlockSequence == 100 && block.DurationNs == 200_000_000 &&
            block.Planes.Single().Samples.Count == 50 && block.Planes.Single().Samples[1] == 2 &&
            block.Planes.Single().QualityRanges.Single() == new WaveformQualityRange(0, 50, 0x80000000) &&
            stream.AdvanceTo(236_000_000, 1, 1).Count == 0,
            "inclusive availability releases one verified block while repeated time never replays it");
        WaveformEnvelope next = WaveformEnvelopeCodec.Decode(stream.AdvanceTo(436_000_000, 50, 1).Single());
        Check.That(next.BlockSequence == 101 && next.StartSimTimeNs == 200_000_000 && next.Planes.Single().FirstSampleIndex == 50,
            "continuous advancement must preserve block and sample frontiers");
    }

    private static void StreamBatchingAndRecoveryPreserveWireBytes()
    {
        PeriodicWaveformPipeline whole = Start();
        IReadOnlyList<byte[]> expected = whole.AdvanceTo(1_000_000_000, 250, 4);
        PeriodicWaveformPipeline split = Start();
        List<byte[]> actual = [.. split.AdvanceTo(100_000_001, 26, 1)];
        var restored = PeriodicWaveformPipeline.Restore(split.CaptureState());
        actual.AddRange(restored.AdvanceTo(236_000_000, 33, 1));
        actual.AddRange(restored.AdvanceTo(1_000_000_000, 191, 4));
        Check.That(expected.Count == 4 && actual.Count == expected.Count && actual.Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)) &&
            Snapshot(restored) == Snapshot(whole),
            "batch boundaries and recovery must preserve byte-identical waveform output and all pending component state");
        string before = Snapshot(restored);
        actual[0][0] ^= 0xff;
        Check.That(Snapshot(restored) == before, "caller mutation of returned wire cannot change the stream checkpoint");
    }

    private static void StreamLateFailuresPreserveAllComponentState()
    {
        PeriodicWaveformPipeline stream = Start();
        string before = Snapshot(stream);
        Reject(() => stream.AdvanceTo(600_000_000, 150, 1), "PeriodicPipeline.BlockLimitExceeded");
        Reject(() => stream.AdvanceTo(600_000_000, 150, 0), "PeriodicPipeline.InvalidBlockLimit");
        bool sampleLimit = false;
        try { stream.AdvanceTo(600_000_000, 149, 3); }
        catch (PeriodicSignalGeneratorException) { sampleLimit = true; }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { stream.AdvanceTo(600_000_000, 150, 3, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(sampleLimit && cancelled && Snapshot(stream) == before && stream.AdvanceTo(600_000_000, 150, 2).Count == 2,
            "late output overflow and cancellation must roll back generator, delay and assembler together");
        PeriodicWaveformPipeline small = Start(1);
        string initial = Snapshot(small);
        bool capacity = false;
        try { small.AdvanceTo(8_000_000, 2, 1); }
        catch (SignalAcquisitionDelayException) { capacity = true; }
        Check.That(capacity && Snapshot(small) == initial && small.AdvanceTo(4_000_000, 1, 1).Count == 0,
            "delay queue failure cannot consume generated samples and must allow a smaller valid retry");
    }

    private static void StreamRestoreRejectsCrossComponentAndSampleTampering()
    {
        PeriodicWaveformPipeline stream = Start();
        _ = stream.AdvanceTo(100_000_000, 25, 1);
        PeriodicWaveformPipelineState state = stream.CaptureState();
        Reject(() => PeriodicWaveformPipeline.Restore(state with
        { Delay = state.Delay with { ReleaseCursorSimTimeNs = 100_000_001 } }), "PeriodicPipeline.InvalidCheckpoint");
        DelayedSignalSample[] delayed = state.Delay.PendingSamples.ToArray();
        delayed[0] = delayed[0] with { NormalizedValue = 99 };
        Reject(() => PeriodicWaveformPipeline.Restore(state with
        { Delay = state.Delay with { PendingSamples = delayed } }), "PeriodicPipeline.InvalidCheckpoint");
        WaveformBlockPlaneState plane = state.Assembler.Planes.Single();
        DelayedSignalSample[] buffered = plane.PendingSamples.ToArray();
        buffered[0] = buffered[0] with { QualityFlags = 0 };
        Reject(() => PeriodicWaveformPipeline.Restore(state with
        { Assembler = state.Assembler with { Planes = new[] { plane with { PendingSamples = buffered } } } }), "PeriodicPipeline.InvalidCheckpoint");
        Reject(() => PeriodicWaveformPipeline.Restore(state with { QualityFlags = 0 }), "PeriodicPipeline.InvalidCheckpoint");
        Check.That(Snapshot(PeriodicWaveformPipeline.Restore(state)) == Snapshot(stream),
            "valid checkpoint restoration must still succeed after rejecting mismatched frontiers and forged pending sample evidence");
    }

    private static string Snapshot(PeriodicWaveformPipeline stream) => JsonSerializer.Serialize(stream.CaptureState());

    private static void Reject(Action action, string reason)
    {
        try { action(); throw new InvalidOperationException("expected stream rejection"); }
        catch (PeriodicWaveformPipelineException exception) { Check.That(exception.ReasonCode == reason, reason); }
    }
}
