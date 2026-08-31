// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformBlockAssemblerSpecifications
{
    private static readonly Guid EcgChannel =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RespChannel =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid PlethChannel =
        Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid PressureChannel =
        Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Co2Channel =
        Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly int[] FrozenSampleCounts = [50, 25, 25, 25, 20];

    public static Specification[] All =>
    [
        new(nameof(FrozenProfilesAssembleOneExactSharedBlock),
            FrozenProfilesAssembleOneExactSharedBlock),
        new(nameof(SlowPlanesReleaseBufferedBlocksWithoutChangingSourceTime),
            SlowPlanesReleaseBufferedBlocksWithoutChangingSourceTime),
        new(nameof(InvalidAndOverCapacityInputRejectAtomically),
            InvalidAndOverCapacityInputRejectAtomically),
        new(nameof(AssemblerCheckpointRestoresBufferedPlanes),
            AssemblerCheckpointRestoresBufferedPlanes),
    ];

    private static void FrozenProfilesAssembleOneExactSharedBlock()
    {
        WaveformBlockPlaneConfiguration[] configurations = AllConfigurations();
        WaveformBlockAssembler assembler = CreateAssembler(configurations, 12, 7);
        Dictionary<Guid, DelayedSignalSample[]> batches = new()
        {
            [EcgChannel] = Produce(
                "AcqECGMonitor250@1",
                4,
                200_000_000,
                index => index is 2 or 3 ? 5U : index == 5 ? 7U : 0U),
            [RespChannel] = Produce("AcqResp125@1", 4, 200_000_000),
            [PlethChannel] = Produce("AcqPleth125@1", 4, 200_000_000),
            [PressureChannel] = Produce("AcqPressure125@1", 4, 200_000_000),
            [Co2Channel] = Produce("AcqCO2_100@1", 4, 200_000_000),
        };

        List<WaveformEnvelope> emitted = [];
        foreach (WaveformBlockPlaneConfiguration configuration in configurations.Reverse())
        {
            emitted.AddRange(assembler.Push(
                configuration.ChannelId,
                batches[configuration.ChannelId]));
        }

        WaveformEnvelope envelope = emitted.Single();
        Check.That(envelope.BlockSequence == 7 &&
            envelope.StreamEpoch == 4 &&
            envelope.StartSimTimeNs == 0 &&
            envelope.DurationNs == WaveformBlockAssembler.BlockDurationNs,
            "all planes must share the same frozen 200 ms source window");
        Check.That(envelope.Planes.Select(plane => plane.Samples.Count).SequenceEqual(
            FrozenSampleCounts),
            "each frozen acquisition rate must retain its exact independent sample count");
        WaveformPlane ecg = envelope.Planes[0];
        Check.That(ecg.FirstSampleIndex == 0 &&
            ecg.QualityEncoding == WaveformQualityEncoding.Ranges &&
            ecg.QualityRanges.SequenceEqual(new[]
            {
                new WaveformQualityRange(2, 2, 5),
                new WaveformQualityRange(5, 1, 7),
            }),
            "non-zero sample quality flags must form sparse canonical ranges");

        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(envelope);
        WaveformEnvelope decoded = WaveformEnvelopeCodec.Decode(wire);
        Check.That(decoded.Planes.Select(plane => plane.Samples.Count).SequenceEqual(
            FrozenSampleCounts),
            "an assembled block must cross the raw envelope boundary without resampling");
    }

    private static void SlowPlanesReleaseBufferedBlocksWithoutChangingSourceTime()
    {
        WaveformBlockAssembler assembler = CreateAssembler(
            [Configuration(EcgChannel, "AcqECGMonitor250@1"),
             Configuration(PlethChannel, "AcqPleth125@1")],
            4,
            10);
        Check.That(assembler.Push(
            EcgChannel,
            Produce("AcqECGMonitor250@1", 4, 600_000_000)).Count == 0,
            "a fast plane must wait while the required slow plane is unavailable");

        IReadOnlyList<WaveformEnvelope> blocks = assembler.Push(
            PlethChannel,
            Produce("AcqPleth125@1", 4, 600_000_000));
        Check.That(blocks.Count == 3 &&
            blocks.Select(block => block.BlockSequence).SequenceEqual(
                new ulong[] { 10, 11, 12 }) &&
            blocks.Select(block => block.StartSimTimeNs).SequenceEqual(
                new long[] { 0, 200_000_000, 400_000_000 }),
            "the slow plane must atomically release every complete buffered source window");
        Check.That(blocks.Select(block => block.Planes[0].FirstSampleIndex).SequenceEqual(
                new ulong[] { 0, 50, 100 }) &&
            blocks.Select(block => block.Planes[1].FirstSampleIndex).SequenceEqual(
                new ulong[] { 0, 25, 50 }),
            "buffering must preserve each plane's independent sample index grid");
    }

    private static void InvalidAndOverCapacityInputRejectAtomically()
    {
        WaveformBlockAssembler assembler = CreateAssembler(
            [Configuration(EcgChannel, "AcqECGMonitor250@1"),
             Configuration(PlethChannel, "AcqPleth125@1")],
            1,
            0);
        WaveformBlockAssemblerState empty = assembler.CaptureState();
        Check.That(Reason(() => assembler.Push(
            EcgChannel,
            Produce("AcqECGMonitor250@1", 4, 400_000_000))) ==
            "WaveformBlockAssembler.CapacityExceeded" &&
            Equivalent(assembler.CaptureState(), empty),
            "a fast plane cannot exceed the configured block backlog");
        Check.That(Reason(() => assembler.Push(Guid.Empty, new DelayedSignalSample[1])) ==
            "WaveformBlockAssembler.UnknownChannel" &&
            Equivalent(assembler.CaptureState(), empty),
            "an unregistered channel must reject without mutation");

        _ = assembler.Push(
            EcgChannel,
            Produce("AcqECGMonitor250@1", 4, 200_000_000));
        WaveformBlockAssemblerState waiting = assembler.CaptureState();
        Check.That(Reason(() => assembler.Push(
            PlethChannel,
            Produce("AcqResp125@1", 4, 200_000_000))) ==
            "WaveformBlockAssembler.InputDiscontinuous" &&
            Equivalent(assembler.CaptureState(), waiting),
            "a plane cannot accept another profile's delayed samples");
    }

    private static void AssemblerCheckpointRestoresBufferedPlanes()
    {
        WaveformBlockAssembler original = CreateAssembler(
            [Configuration(EcgChannel, "AcqECGMonitor250@1"),
             Configuration(PlethChannel, "AcqPleth125@1")],
            4,
            20);
        DelayedSignalSample[] ecg = Produce("AcqECGMonitor250@1", 4, 400_000_000);
        DelayedSignalSample[] pleth = Produce("AcqPleth125@1", 4, 400_000_000);
        _ = original.Push(EcgChannel, ecg);
        _ = original.Push(PlethChannel, pleth[..25]);
        WaveformBlockAssemblerState checkpoint = original.CaptureState();
        var restored = WaveformBlockAssembler.Restore(checkpoint);

        WaveformEnvelope expected = original.Push(PlethChannel, pleth[25..]).Single();
        WaveformEnvelope actual = restored.Push(PlethChannel, pleth[25..]).Single();
        Check.That(WaveformEnvelopeCodec.EncodeRaw(actual).SequenceEqual(
                WaveformEnvelopeCodec.EncodeRaw(expected)) &&
            Equivalent(restored.CaptureState(), original.CaptureState()),
            "restoring a partially buffered block must reproduce future wire bytes and state");

        WaveformBlockPlaneState corruptPlane = checkpoint.Planes[0] with
        {
            PendingSamples =
            [
                checkpoint.Planes[0].PendingSamples[0] with
                {
                    SourceSimTimeNs = 1,
                },
                .. checkpoint.Planes[0].PendingSamples.Skip(1),
            ],
        };
        WaveformBlockAssemblerState corrupt = checkpoint with
        {
            Planes = [corruptPlane, .. checkpoint.Planes.Skip(1)],
        };
        Check.That(Reason(() => WaveformBlockAssembler.Restore(corrupt)) ==
            "WaveformBlockAssembler.InvalidCheckpoint",
            "a checkpoint cannot alter a buffered sample's exact source grid");
    }

    private static WaveformBlockAssembler CreateAssembler(
        IReadOnlyList<WaveformBlockPlaneConfiguration> configurations,
        int maximumBufferedBlocks,
        ulong firstBlockSequence) => WaveformBlockAssembler.Start(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            2,
            4,
            6,
            firstBlockSequence,
            0,
            maximumBufferedBlocks,
            configurations);

    private static WaveformBlockPlaneConfiguration[] AllConfigurations() =>
    [
        Configuration(EcgChannel, "AcqECGMonitor250@1"),
        Configuration(RespChannel, "AcqResp125@1"),
        Configuration(PlethChannel, "AcqPleth125@1"),
        Configuration(PressureChannel, "AcqPressure125@1"),
        Configuration(Co2Channel, "AcqCO2_100@1"),
    ];

    private static WaveformBlockPlaneConfiguration Configuration(
        Guid channelId,
        string profileId) => new(channelId, profileId, 1, 1, 0, 1);

    private static DelayedSignalSample[] Produce(
        string profileId,
        ulong streamEpoch,
        long exclusiveSimTimeNs,
        Func<ulong, uint>? quality = null)
    {
        var clock = SignalSampleClock.Start(profileId, streamEpoch, 0);
        var delay = SignalAcquisitionDelayLine.Start(
            profileId,
            streamEpoch,
            0,
            1_000);
        foreach (SignalSampleTick tick in clock.DrainBefore(exclusiveSimTimeNs))
        {
            delay.Enqueue(
                tick,
                checked((short)tick.SampleIndex),
                quality?.Invoke(tick.SampleIndex) ?? 0);
        }

        long latency = FrozenSignalAcquisitionProfiles.Get(profileId).LatencyNs;
        return [.. delay.DrainAvailable(exclusiveSimTimeNs + latency)];
    }

    private static bool Equivalent(
        WaveformBlockAssemblerState left,
        WaveformBlockAssemblerState right) =>
        left.SessionId == right.SessionId &&
        left.InstanceId == right.InstanceId &&
        left.TimebaseEpoch == right.TimebaseEpoch &&
        left.StreamEpoch == right.StreamEpoch &&
        left.ConfigurationRevision == right.ConfigurationRevision &&
        left.NextBlockSequence == right.NextBlockSequence &&
        left.EpochAnchorSimTimeNs == right.EpochAnchorSimTimeNs &&
        left.NextBlockStartSimTimeNs == right.NextBlockStartSimTimeNs &&
        left.MaximumBufferedBlocks == right.MaximumBufferedBlocks &&
        left.Planes.Count == right.Planes.Count &&
        left.Planes.Zip(right.Planes).All(pair =>
            pair.First.Configuration == pair.Second.Configuration &&
            pair.First.NextInputSampleIndex == pair.Second.NextInputSampleIndex &&
            pair.First.PendingSamples.SequenceEqual(pair.Second.PendingSamples));

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (WaveformBlockAssemblerException exception)
        {
            return exception.ReasonCode;
        }
    }
}
