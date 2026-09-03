// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

public sealed class WaveformRecordArchiveException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record WaveformRecordArchivePlan(
    string GroupId,
    string RecordRef,
    ulong SweepEpoch,
    Guid SessionId,
    Guid InstanceId,
    ulong TimebaseEpoch,
    long EpochAnchorSimTimeNs,
    ulong StreamEpoch,
    ulong ConfigurationRevision,
    long RecordStartSimTimeNs,
    long RecordEndExclusiveSimTimeNs,
    IReadOnlyList<Guid> ChannelIds);

public sealed record ArchivedWaveformBlock(
    ulong BlockSequence,
    long StartSimTimeNs,
    string ContentSha256,
    byte[] RawEnvelope);

public sealed record WaveformRecordArchiveState(
    WaveformRecordArchivePlan Plan,
    IReadOnlyList<byte[]> RawEnvelopes);

public sealed class WaveformRecordArchive
{
    public const int StandardEcgChannelCount = 12;
    public const int MaximumRecordBlockCount =
        WaveformBlockRing.HostRetentionBlockCount;

    private readonly WaveformRecordArchivePlan _plan;
    private readonly StoredBlock[] _blocks;

    private WaveformRecordArchive(
        WaveformRecordArchivePlan plan,
        IReadOnlyList<byte[]> rawEnvelopes)
    {
        _plan = ValidateAndCopyPlan(plan, nameof(plan));
        _blocks = ValidateAndCopyBlocks(
            _plan,
            rawEnvelopes,
            nameof(rawEnvelopes));
    }

    public string RecordRef => _plan.RecordRef;

    public int BlockCount => _blocks.Length;

    public long RecordStartSimTimeNs => _plan.RecordStartSimTimeNs;

    public long RecordEndExclusiveSimTimeNs =>
        _plan.RecordEndExclusiveSimTimeNs;

    public static WaveformRecordArchive Create(
        WaveformRecordArchivePlan plan,
        IReadOnlyList<byte[]> rawEnvelopes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rawEnvelopes);
        return new WaveformRecordArchive(plan, rawEnvelopes);
    }

    public static WaveformRecordArchive Restore(
        WaveformRecordArchiveState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            ArgumentNullException.ThrowIfNull(state.Plan);
            ArgumentNullException.ThrowIfNull(state.RawEnvelopes);
            return new WaveformRecordArchive(state.Plan, state.RawEnvelopes);
        }
        catch (WaveformRecordArchiveException exception)
            when (exception.ReasonCode !=
                "WaveformRecordArchive.InvalidCheckpoint")
        {
            throw Error(
                "WaveformRecordArchive.InvalidCheckpoint",
                nameof(state));
        }
        catch (WaveformEnvelopeException)
        {
            throw Error(
                "WaveformRecordArchive.InvalidCheckpoint",
                nameof(state));
        }
        catch (OverflowException)
        {
            throw Error(
                "WaveformRecordArchive.InvalidCheckpoint",
                nameof(state));
        }
    }

    public IReadOnlyList<ArchivedWaveformBlock> ReadBlocks()
    {
        ArchivedWaveformBlock[] blocks = new ArchivedWaveformBlock[
            _blocks.Length];
        for (int index = 0; index < blocks.Length; index++)
        {
            StoredBlock block = _blocks[index];
            blocks[index] = new ArchivedWaveformBlock(
                block.BlockSequence,
                block.StartSimTimeNs,
                block.ContentSha256,
                [.. block.RawEnvelope]);
        }

        return Array.AsReadOnly(blocks);
    }

    public WaveformRecordArchiveState CaptureState()
    {
        byte[][] envelopes = new byte[_blocks.Length][];
        for (int index = 0; index < envelopes.Length; index++)
        {
            envelopes[index] = [.. _blocks[index].RawEnvelope];
        }

        return new WaveformRecordArchiveState(
            CopyPlan(_plan),
            Array.AsReadOnly(envelopes));
    }

    private static StoredBlock[] ValidateAndCopyBlocks(
        WaveformRecordArchivePlan plan,
        IReadOnlyList<byte[]> rawEnvelopes,
        string parameterName)
    {
        if (rawEnvelopes.Count is < 1 or > MaximumRecordBlockCount)
        {
            throw Error(
                "WaveformRecordArchive.RangeNotCovered",
                parameterName);
        }

        StoredBlock[] blocks = new StoredBlock[rawEnvelopes.Count];
        ChannelCursor[]? channelCursors = null;
        ulong previousSequence = 0;
        long previousStart = 0;
        for (int index = 0; index < blocks.Length; index++)
        {
            byte[] source = rawEnvelopes[index] ??
                throw Error(
                    "WaveformRecordArchive.InvalidEnvelope",
                    parameterName);
            byte[] copied = [.. source];
            WaveformEnvelope envelope;
            try
            {
                envelope = WaveformEnvelopeCodec.Decode(copied);
            }
            catch (WaveformEnvelopeException)
            {
                throw Error(
                    "WaveformRecordArchive.InvalidEnvelope",
                    parameterName);
            }

            ValidateEnvelopeIdentity(plan, envelope, parameterName);
            if (index > 0 &&
                (previousSequence == ulong.MaxValue ||
                    envelope.BlockSequence != previousSequence + 1 ||
                    previousStart >
                        long.MaxValue - WaveformBlockAssembler.BlockDurationNs ||
                    envelope.StartSimTimeNs != previousStart +
                        WaveformBlockAssembler.BlockDurationNs))
            {
                throw Error(
                    "WaveformRecordArchive.BlockDiscontinuous",
                    parameterName);
            }

            channelCursors = ValidateChannels(
                plan,
                envelope,
                channelCursors,
                parameterName);
            blocks[index] = new StoredBlock(
                envelope.BlockSequence,
                envelope.StartSimTimeNs,
                Convert.ToHexStringLower(copied.AsSpan(
                    WaveformEnvelopeCodec.ContentSha256Offset,
                    32)),
                copied);
            previousSequence = envelope.BlockSequence;
            previousStart = envelope.StartSimTimeNs;
        }

        ValidateRangeCoverage(plan, blocks, parameterName);
        return blocks;
    }

    private static void ValidateEnvelopeIdentity(
        WaveformRecordArchivePlan plan,
        WaveformEnvelope envelope,
        string parameterName)
    {
        if (envelope.SessionId != plan.SessionId ||
            envelope.InstanceId != plan.InstanceId ||
            envelope.TimebaseEpoch != plan.TimebaseEpoch ||
            envelope.StreamEpoch != plan.StreamEpoch)
        {
            throw Error(
                "WaveformRecordArchive.IdentityMismatch",
                parameterName);
        }

        if (envelope.ConfigurationRevision != plan.ConfigurationRevision)
        {
            throw Error(
                "WaveformRecordArchive.ConfigurationChanged",
                parameterName);
        }

        if (envelope.DurationNs != WaveformBlockAssembler.BlockDurationNs ||
            envelope.StartSimTimeNs < 0)
        {
            throw Error(
                "WaveformRecordArchive.InvalidEnvelope",
                parameterName);
        }
    }

    private static ChannelCursor[] ValidateChannels(
        WaveformRecordArchivePlan plan,
        WaveformEnvelope envelope,
        ChannelCursor[]? previous,
        string parameterName)
    {
        Dictionary<Guid, WaveformPlane> planes = envelope.Planes.ToDictionary(
            static plane => plane.ChannelId);
        WaveformPlane[] required = new WaveformPlane[
            StandardEcgChannelCount];
        for (int index = 0; index < required.Length; index++)
        {
            if (!planes.TryGetValue(plan.ChannelIds[index], out WaveformPlane? plane))
            {
                throw Error(
                    "WaveformRecordArchive.ChannelSetIncomplete",
                    parameterName);
            }

            required[index] = plane;
        }

        WaveformPlane reference = required[0];
        ChannelCursor[] next = new ChannelCursor[required.Length];
        for (int index = 0; index < required.Length; index++)
        {
            WaveformPlane plane = required[index];
            bool synchronized =
                plane.SampleRateNumerator == reference.SampleRateNumerator &&
                plane.SampleRateDenominator == reference.SampleRateDenominator &&
                plane.FirstSampleIndex == reference.FirstSampleIndex &&
                HasExactBlockSampleCount(plane, envelope.DurationNs) &&
                IsAlignedToBlockStart(
                    plane,
                    plan.EpochAnchorSimTimeNs,
                    envelope.StartSimTimeNs);
            if (!synchronized)
            {
                throw Error(
                    "WaveformRecordArchive.ChannelGridChanged",
                    parameterName);
            }

            if (previous is not null &&
                (!previous[index].MatchesShape(plane) ||
                    previous[index].NextSampleIndex != plane.FirstSampleIndex))
            {
                throw Error(
                    "WaveformRecordArchive.ChannelGridChanged",
                    parameterName);
            }

            UInt128 nextSampleIndex =
                (UInt128)plane.FirstSampleIndex +
                checked((uint)plane.Samples.Count);
            if (nextSampleIndex > ulong.MaxValue)
            {
                throw Error(
                    "WaveformRecordArchive.ChannelGridChanged",
                    parameterName);
            }

            next[index] = ChannelCursor.From(
                plane,
                (ulong)nextSampleIndex);
        }

        return next;
    }

    private static bool HasExactBlockSampleCount(
        WaveformPlane plane,
        uint durationNs)
    {
        if (plane.Samples.Count == 0)
        {
            return false;
        }

        UInt128 representedDuration =
            (UInt128)checked((uint)plane.Samples.Count) *
            plane.SampleRateDenominator *
            1_000_000_000U;
        UInt128 expectedDuration =
            (UInt128)durationNs * plane.SampleRateNumerator;
        return representedDuration == expectedDuration;
    }

    private static bool IsAlignedToBlockStart(
        WaveformPlane plane,
        long epochAnchorSimTimeNs,
        long blockStartSimTimeNs)
    {
        if (blockStartSimTimeNs < epochAnchorSimTimeNs)
        {
            return false;
        }

        ulong elapsed = checked((ulong)(
            blockStartSimTimeNs - epochAnchorSimTimeNs));
        UInt128 timePosition =
            (UInt128)elapsed * plane.SampleRateNumerator;
        UInt128 samplePosition =
            (UInt128)plane.FirstSampleIndex *
            plane.SampleRateDenominator *
            1_000_000_000U;
        return timePosition == samplePosition;
    }

    private static void ValidateRangeCoverage(
        WaveformRecordArchivePlan plan,
        IReadOnlyList<StoredBlock> blocks,
        string parameterName)
    {
        StoredBlock first = blocks[0];
        StoredBlock last = blocks[^1];
        long firstEnd = AddBlockDuration(first.StartSimTimeNs, parameterName);
        long lastEnd = AddBlockDuration(last.StartSimTimeNs, parameterName);
        bool minimalEnd = blocks.Count == 1 ||
            AddBlockDuration(blocks[^2].StartSimTimeNs, parameterName) <
                plan.RecordEndExclusiveSimTimeNs;
        if (first.StartSimTimeNs > plan.RecordStartSimTimeNs ||
            firstEnd <= plan.RecordStartSimTimeNs ||
            last.StartSimTimeNs >= plan.RecordEndExclusiveSimTimeNs ||
            lastEnd < plan.RecordEndExclusiveSimTimeNs ||
            !minimalEnd)
        {
            throw Error(
                "WaveformRecordArchive.RangeNotCovered",
                parameterName);
        }
    }

    private static WaveformRecordArchivePlan ValidateAndCopyPlan(
        WaveformRecordArchivePlan plan,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.ChannelIds is null)
        {
            throw Error("WaveformRecordArchive.InvalidPlan", parameterName);
        }

        WaveformRecordArchivePlan copied = CopyPlan(plan);
        if (!IsStableId(copied.GroupId) ||
            !IsStableId(copied.RecordRef) ||
            copied.SessionId == Guid.Empty ||
            copied.InstanceId == Guid.Empty ||
            copied.EpochAnchorSimTimeNs < 0 ||
            copied.EpochAnchorSimTimeNs > copied.RecordStartSimTimeNs ||
            copied.RecordStartSimTimeNs < 0 ||
            copied.RecordEndExclusiveSimTimeNs <=
                copied.RecordStartSimTimeNs ||
            copied.ChannelIds.Count != StandardEcgChannelCount ||
            copied.ChannelIds.Any(static channelId => channelId == Guid.Empty) ||
            copied.ChannelIds.Distinct().Count() != StandardEcgChannelCount)
        {
            throw Error("WaveformRecordArchive.InvalidPlan", parameterName);
        }

        return copied;
    }

    private static WaveformRecordArchivePlan CopyPlan(
        WaveformRecordArchivePlan plan) => plan with
        {
            ChannelIds = Array.AsReadOnly(plan.ChannelIds.ToArray()),
        };

    private static long AddBlockDuration(long start, string parameterName)
    {
        if (start > long.MaxValue - WaveformBlockAssembler.BlockDurationNs)
        {
            throw Error(
                "WaveformRecordArchive.InvalidEnvelope",
                parameterName);
        }

        return start + WaveformBlockAssembler.BlockDurationNs;
    }

    private static bool IsStableId(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > 128 ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        return value.All(static character =>
            IsAsciiLetter(character) ||
            char.IsAsciiDigit(character) ||
            character is '.' or '_' or ':' or '@' or '/' or '-');
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static WaveformRecordArchiveException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);

    private sealed record StoredBlock(
        ulong BlockSequence,
        long StartSimTimeNs,
        string ContentSha256,
        byte[] RawEnvelope);

    private sealed record ChannelCursor(
        uint SampleRateNumerator,
        uint SampleRateDenominator,
        int ScaleNumerator,
        uint ScaleDenominator,
        int OffsetNumerator,
        uint OffsetDenominator,
        ulong NextSampleIndex)
    {
        public static ChannelCursor From(
            WaveformPlane plane,
            ulong nextSampleIndex) => new(
            plane.SampleRateNumerator,
            plane.SampleRateDenominator,
            plane.ScaleNumerator,
            plane.ScaleDenominator,
            plane.OffsetNumerator,
            plane.OffsetDenominator,
            nextSampleIndex);

        public bool MatchesShape(WaveformPlane plane) =>
            SampleRateNumerator == plane.SampleRateNumerator &&
            SampleRateDenominator == plane.SampleRateDenominator &&
            ScaleNumerator == plane.ScaleNumerator &&
            ScaleDenominator == plane.ScaleDenominator &&
            OffsetNumerator == plane.OffsetNumerator &&
            OffsetDenominator == plane.OffsetDenominator;
    }
}
