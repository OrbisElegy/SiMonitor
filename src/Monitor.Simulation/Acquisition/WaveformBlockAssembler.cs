// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

public sealed class WaveformBlockAssemblerException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record WaveformBlockPlaneConfiguration(
    Guid ChannelId,
    string ProfileId,
    int ScaleNumerator,
    uint ScaleDenominator,
    int OffsetNumerator,
    uint OffsetDenominator);

public sealed record WaveformBlockPlaneState(
    WaveformBlockPlaneConfiguration Configuration,
    ulong NextInputSampleIndex,
    IReadOnlyList<DelayedSignalSample> PendingSamples);

public sealed record WaveformBlockAssemblerState(
    Guid SessionId,
    Guid InstanceId,
    ulong TimebaseEpoch,
    ulong StreamEpoch,
    ulong ConfigurationRevision,
    ulong NextBlockSequence,
    long EpochAnchorSimTimeNs,
    long NextBlockStartSimTimeNs,
    int MaximumBufferedBlocks,
    IReadOnlyList<WaveformBlockPlaneState> Planes);

public sealed class WaveformBlockAssembler
{
    public const uint BlockDurationNs = 200_000_000;
    public const int MaximumBufferedBlockCount = 300;

    private readonly List<PlaneBuffer> _planes;

    private WaveformBlockAssembler(WaveformBlockAssemblerState state)
    {
        ValidateState(state);
        SessionId = state.SessionId;
        InstanceId = state.InstanceId;
        TimebaseEpoch = state.TimebaseEpoch;
        StreamEpoch = state.StreamEpoch;
        ConfigurationRevision = state.ConfigurationRevision;
        NextBlockSequence = state.NextBlockSequence;
        EpochAnchorSimTimeNs = state.EpochAnchorSimTimeNs;
        NextBlockStartSimTimeNs = state.NextBlockStartSimTimeNs;
        MaximumBufferedBlocks = state.MaximumBufferedBlocks;
        _planes = new List<PlaneBuffer>(state.Planes.Count);
        foreach (WaveformBlockPlaneState plane in state.Planes)
        {
            _planes.Add(new PlaneBuffer(
                plane.Configuration,
                FrozenSignalAcquisitionProfiles.Get(plane.Configuration.ProfileId),
                plane.NextInputSampleIndex,
                [.. plane.PendingSamples]));
        }
    }

    public Guid SessionId { get; }

    public Guid InstanceId { get; }

    public ulong TimebaseEpoch { get; }

    public ulong StreamEpoch { get; }

    public ulong ConfigurationRevision { get; }

    public ulong NextBlockSequence { get; private set; }

    public long EpochAnchorSimTimeNs { get; }

    public long NextBlockStartSimTimeNs { get; private set; }

    public int MaximumBufferedBlocks { get; }

    public static WaveformBlockAssembler Start(
        Guid sessionId,
        Guid instanceId,
        ulong timebaseEpoch,
        ulong streamEpoch,
        ulong configurationRevision,
        ulong firstBlockSequence,
        long epochAnchorSimTimeNs,
        int maximumBufferedBlocks,
        IReadOnlyList<WaveformBlockPlaneConfiguration> planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if (planes.Count is < 1 or > WaveformEnvelopeCodec.MaximumPlaneCount)
        {
            throw Error("WaveformBlockAssembler.InvalidConfiguration", nameof(planes));
        }

        WaveformBlockPlaneConfiguration[] canonicalPlanes = [.. planes];
        if (canonicalPlanes.Any(static plane => plane is null))
        {
            throw Error("WaveformBlockAssembler.InvalidConfiguration", nameof(planes));
        }

        Array.Sort(canonicalPlanes, static (left, right) => CompareChannelIds(
            left.ChannelId,
            right.ChannelId));
        var planeStates = new WaveformBlockPlaneState[
            canonicalPlanes.Length];
        for (int index = 0; index < canonicalPlanes.Length; index++)
        {
            planeStates[index] = new WaveformBlockPlaneState(
                canonicalPlanes[index],
                0,
                Array.Empty<DelayedSignalSample>());
        }

        return new WaveformBlockAssembler(new WaveformBlockAssemblerState(
            sessionId,
            instanceId,
            timebaseEpoch,
            streamEpoch,
            configurationRevision,
            firstBlockSequence,
            epochAnchorSimTimeNs,
            epochAnchorSimTimeNs,
            maximumBufferedBlocks,
            planeStates));
    }

    public static WaveformBlockAssembler Restore(WaveformBlockAssemblerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Planes);
        return new WaveformBlockAssembler(state);
    }

    public IReadOnlyList<WaveformEnvelope> Push(
        Guid channelId,
        IReadOnlyList<DelayedSignalSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        int planeIndex = FindPlane(channelId);
        if (planeIndex < 0)
        {
            throw Error("WaveformBlockAssembler.UnknownChannel", nameof(channelId));
        }

        if (samples.Count == 0)
        {
            return Array.Empty<WaveformEnvelope>();
        }

        PlaneBuffer target = _planes[planeIndex];
        int capacity = checked(target.SamplesPerBlock * MaximumBufferedBlocks);
        if (samples.Count > capacity - target.PendingSamples.Count)
        {
            throw Error("WaveformBlockAssembler.CapacityExceeded", nameof(samples));
        }

        UInt128 nextInputWide = (UInt128)target.NextInputSampleIndex +
            checked((uint)samples.Count);
        if (nextInputWide > ulong.MaxValue)
        {
            throw Error("WaveformBlockAssembler.StateOverflow", nameof(samples));
        }

        var accepted = new DelayedSignalSample[samples.Count];
        for (int index = 0; index < accepted.Length; index++)
        {
            DelayedSignalSample sample = samples[index];
            ulong expectedIndex = target.NextInputSampleIndex + (uint)index;
            Int128 expectedSourceTime = (Int128)EpochAnchorSimTimeNs +
                (Int128)expectedIndex * target.Profile.SamplePeriodNs;
            Int128 expectedAvailableTime = expectedSourceTime + target.Profile.LatencyNs;
            if (!StringComparer.Ordinal.Equals(sample.ProfileId, target.Configuration.ProfileId) ||
                sample.StreamEpoch != StreamEpoch ||
                sample.SampleIndex != expectedIndex ||
                expectedSourceTime > long.MaxValue ||
                sample.SourceSimTimeNs != expectedSourceTime ||
                expectedAvailableTime > long.MaxValue ||
                sample.AvailableSimTimeNs != expectedAvailableTime)
            {
                throw Error("WaveformBlockAssembler.InputDiscontinuous", nameof(samples));
            }

            accepted[index] = sample;
        }

        int readyBlocks = int.MaxValue;
        for (int index = 0; index < _planes.Count; index++)
        {
            PlaneBuffer plane = _planes[index];
            int pendingCount = plane.PendingSamples.Count +
                (index == planeIndex ? accepted.Length : 0);
            readyBlocks = Math.Min(readyBlocks, pendingCount / plane.SamplesPerBlock);
        }

        UInt128 nextBlockSequenceWide = (UInt128)NextBlockSequence +
            checked((uint)readyBlocks);
        Int128 nextBlockStartWide = (Int128)NextBlockStartSimTimeNs +
            (Int128)readyBlocks * BlockDurationNs;
        if (nextBlockSequenceWide > ulong.MaxValue || nextBlockStartWide > long.MaxValue)
        {
            throw Error("WaveformBlockAssembler.StateOverflow", nameof(samples));
        }

        target.PendingSamples.AddRange(accepted);
        target.NextInputSampleIndex = (ulong)nextInputWide;
        if (readyBlocks == 0)
        {
            return Array.Empty<WaveformEnvelope>();
        }

        var envelopes = new WaveformEnvelope[readyBlocks];
        for (int blockIndex = 0; blockIndex < readyBlocks; blockIndex++)
        {
            var outputPlanes = new WaveformPlane[_planes.Count];
            for (int index = 0; index < _planes.Count; index++)
            {
                PlaneBuffer plane = _planes[index];
                DelayedSignalSample[] blockSamples = plane.PendingSamples
                    .GetRange(0, plane.SamplesPerBlock)
                    .ToArray();
                plane.PendingSamples.RemoveRange(0, plane.SamplesPerBlock);
                short[] values = new short[blockSamples.Length];
                for (int sampleIndex = 0; sampleIndex < blockSamples.Length; sampleIndex++)
                {
                    values[sampleIndex] = blockSamples[sampleIndex].NormalizedValue;
                }

                WaveformQualityRange[] qualityRanges = BuildQualityRanges(blockSamples);
                outputPlanes[index] = new WaveformPlane(
                    plane.Configuration.ChannelId,
                    plane.Profile.SampleRateHz,
                    1,
                    blockSamples[0].SampleIndex,
                    plane.Configuration.ScaleNumerator,
                    plane.Configuration.ScaleDenominator,
                    plane.Configuration.OffsetNumerator,
                    plane.Configuration.OffsetDenominator,
                    qualityRanges.Length == 0
                        ? WaveformQualityEncoding.None
                        : WaveformQualityEncoding.Ranges,
                    Array.AsReadOnly(values),
                    Array.AsReadOnly(qualityRanges));
            }

            envelopes[blockIndex] = new WaveformEnvelope(
                SessionId,
                InstanceId,
                TimebaseEpoch,
                StreamEpoch,
                NextBlockSequence,
                ConfigurationRevision,
                NextBlockStartSimTimeNs,
                BlockDurationNs,
                Array.AsReadOnly(outputPlanes));
            NextBlockSequence++;
            NextBlockStartSimTimeNs += BlockDurationNs;
        }

        return Array.AsReadOnly(envelopes);
    }

    public WaveformBlockAssemblerState CaptureState()
    {
        var planes = new WaveformBlockPlaneState[_planes.Count];
        for (int index = 0; index < _planes.Count; index++)
        {
            PlaneBuffer plane = _planes[index];
            planes[index] = new WaveformBlockPlaneState(
                plane.Configuration,
                plane.NextInputSampleIndex,
                Array.AsReadOnly(plane.PendingSamples.ToArray()));
        }

        return new WaveformBlockAssemblerState(
            SessionId,
            InstanceId,
            TimebaseEpoch,
            StreamEpoch,
            ConfigurationRevision,
            NextBlockSequence,
            EpochAnchorSimTimeNs,
            NextBlockStartSimTimeNs,
            MaximumBufferedBlocks,
            Array.AsReadOnly(planes));
    }

    private static WaveformQualityRange[] BuildQualityRanges(
        DelayedSignalSample[] samples)
    {
        List<WaveformQualityRange> ranges = [];
        int index = 0;
        while (index < samples.Length)
        {
            uint qualityFlags = samples[index].QualityFlags;
            if (qualityFlags == 0)
            {
                index++;
                continue;
            }

            int first = index;
            index++;
            while (index < samples.Length && samples[index].QualityFlags == qualityFlags)
            {
                index++;
            }

            ranges.Add(new WaveformQualityRange(
                checked((uint)first),
                checked((uint)(index - first)),
                qualityFlags));
        }

        return [.. ranges];
    }

    private int FindPlane(Guid channelId)
    {
        for (int index = 0; index < _planes.Count; index++)
        {
            if (_planes[index].Configuration.ChannelId == channelId)
            {
                return index;
            }
        }

        return -1;
    }

    private static void ValidateState(WaveformBlockAssemblerState state)
    {
        if (state.EpochAnchorSimTimeNs < 0 ||
            state.NextBlockStartSimTimeNs < state.EpochAnchorSimTimeNs ||
            (state.NextBlockStartSimTimeNs - state.EpochAnchorSimTimeNs) %
                BlockDurationNs != 0 ||
            state.MaximumBufferedBlocks is < 1 or > MaximumBufferedBlockCount ||
            state.Planes.Count is < 1 or > WaveformEnvelopeCodec.MaximumPlaneCount)
        {
            throw InvalidState();
        }

        ulong blockOrdinal = checked((ulong)(
            (state.NextBlockStartSimTimeNs - state.EpochAnchorSimTimeNs) /
            BlockDurationNs));
        Guid? previousChannelId = null;
        foreach (WaveformBlockPlaneState plane in state.Planes)
        {
            if (plane is null || plane.Configuration is null || plane.PendingSamples is null ||
                string.IsNullOrEmpty(plane.Configuration.ProfileId) ||
                plane.Configuration.ScaleDenominator == 0 ||
                plane.Configuration.OffsetDenominator == 0 ||
                (previousChannelId.HasValue && CompareChannelIds(
                    previousChannelId.Value,
                    plane.Configuration.ChannelId) >= 0))
            {
                throw InvalidState();
            }

            previousChannelId = plane.Configuration.ChannelId;
            SignalAcquisitionProfileDescriptor profile;
            try
            {
                profile = FrozenSignalAcquisitionProfiles.Get(plane.Configuration.ProfileId);
            }
            catch (SignalSampleClockException)
            {
                throw InvalidState();
            }

            int samplesPerBlock = checked((int)(
                (ulong)profile.SampleRateHz * BlockDurationNs / 1_000_000_000));
            if ((ulong)profile.SampleRateHz * BlockDurationNs % 1_000_000_000 != 0 ||
                samplesPerBlock == 0 ||
                plane.PendingSamples.Count >
                    checked(samplesPerBlock * state.MaximumBufferedBlocks))
            {
                throw InvalidState();
            }

            UInt128 expectedFirstIndexWide = (UInt128)blockOrdinal *
                checked((uint)samplesPerBlock);
            if (expectedFirstIndexWide > ulong.MaxValue)
            {
                throw InvalidState();
            }

            ulong expectedFirstIndex = (ulong)expectedFirstIndexWide;
            UInt128 expectedNextIndexWide = (UInt128)expectedFirstIndex +
                checked((uint)plane.PendingSamples.Count);
            if (expectedNextIndexWide > ulong.MaxValue ||
                plane.NextInputSampleIndex != (ulong)expectedNextIndexWide)
            {
                throw InvalidState();
            }

            for (int index = 0; index < plane.PendingSamples.Count; index++)
            {
                DelayedSignalSample sample = plane.PendingSamples[index];
                ulong expectedIndex = expectedFirstIndex + (uint)index;
                Int128 expectedSourceTime = (Int128)state.EpochAnchorSimTimeNs +
                    (Int128)expectedIndex * profile.SamplePeriodNs;
                Int128 expectedAvailableTime = expectedSourceTime + profile.LatencyNs;
                if (!StringComparer.Ordinal.Equals(
                        sample.ProfileId,
                        plane.Configuration.ProfileId) ||
                    sample.StreamEpoch != state.StreamEpoch ||
                    sample.SampleIndex != expectedIndex ||
                    expectedSourceTime > long.MaxValue ||
                    sample.SourceSimTimeNs != expectedSourceTime ||
                    expectedAvailableTime > long.MaxValue ||
                    sample.AvailableSimTimeNs != expectedAvailableTime)
                {
                    throw InvalidState();
                }
            }
        }

        static WaveformBlockAssemblerException InvalidState() => new(
            "WaveformBlockAssembler.InvalidCheckpoint",
            nameof(state));
    }

    private static int CompareChannelIds(Guid left, Guid right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        _ = left.TryWriteBytes(leftBytes, bigEndian: true, out _);
        _ = right.TryWriteBytes(rightBytes, bigEndian: true, out _);
        return leftBytes.SequenceCompareTo(rightBytes);
    }

    private static WaveformBlockAssemblerException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);

    private sealed class PlaneBuffer(
        WaveformBlockPlaneConfiguration configuration,
        SignalAcquisitionProfileDescriptor profile,
        ulong nextInputSampleIndex,
        DelayedSignalSample[] pendingSamples)
    {
        public WaveformBlockPlaneConfiguration Configuration { get; } = configuration;

        public SignalAcquisitionProfileDescriptor Profile { get; } = profile;

        public int SamplesPerBlock { get; } = checked((int)(
            (ulong)profile.SampleRateHz * BlockDurationNs / 1_000_000_000));

        public ulong NextInputSampleIndex { get; set; } = nextInputSampleIndex;

        public List<DelayedSignalSample> PendingSamples { get; } = new(pendingSamples);
    }
}
