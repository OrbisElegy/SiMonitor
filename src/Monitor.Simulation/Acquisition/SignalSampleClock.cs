// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

public sealed class SignalSampleClockException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class SignalAcquisitionProfileDescriptor
{
    internal SignalAcquisitionProfileDescriptor(
        string profileId,
        string channelClass,
        uint sampleRateHz,
        long latencyNs,
        string filterPrimitive,
        string quantization,
        string evidenceStatus)
    {
        ProfileId = profileId;
        ChannelClass = channelClass;
        SampleRateHz = sampleRateHz;
        LatencyNs = latencyNs;
        FilterPrimitive = filterPrimitive;
        Quantization = quantization;
        EvidenceStatus = evidenceStatus;
    }

    public string ProfileId { get; }

    public string ChannelClass { get; }

    public uint SampleRateHz { get; }

    public long LatencyNs { get; }

    public string FilterPrimitive { get; }

    public string Quantization { get; }

    public string EvidenceStatus { get; }

    public long SamplePeriodNs => 1_000_000_000 / SampleRateHz;
}

public static class FrozenSignalAcquisitionProfiles
{
    public static SignalAcquisitionProfileDescriptor Get(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return profileId switch
        {
            "AcqECGMonitor250@1" => New(profileId, "ECG-monitor", 250, 40),
            "AcqResp125@1" => New(profileId, "IMP-Resp", 125, 80),
            "AcqPleth125@1" => New(profileId, "Pleth", 125, 2_000),
            "AcqPressure125@1" => New(profileId, "Pressure", 125, 80),
            "AcqCO2_100@1" => New(profileId, "CO2", 100, 2_000),
            _ => throw new SignalSampleClockException(
                "SignalSampleClock.UnknownProfile",
                nameof(profileId)),
        };
    }

    private static SignalAcquisitionProfileDescriptor New(
        string profileId,
        string channelClass,
        uint sampleRateHz,
        int latencyMs)
    {
        if (1_000_000_000 % sampleRateHz != 0)
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.NonIntegralNanosecondPeriod",
                nameof(sampleRateHz));
        }

        return new SignalAcquisitionProfileDescriptor(
            profileId,
            channelClass,
            sampleRateHz,
            latencyMs * 1_000_000L,
            "Identity@1",
            "Int16Normalized",
            "ProjectTeachingProfile");
    }
}

public readonly record struct SignalSampleTick(
    string ProfileId,
    ulong StreamEpoch,
    ulong SampleIndex,
    long SimTimeNs);

public enum FilterStateTransitionPolicy
{
    Reacquire,
    Clear,
    MigrateValidated,
}

public sealed record SignalProfileBoundary(
    string PreviousProfileId,
    string NextProfileId,
    ulong PreviousStreamEpoch,
    ulong NextStreamEpoch,
    long FirstSampleSimTimeNs,
    FilterStateTransitionPolicy FilterStateTransitionPolicy);

public sealed record SignalSampleClockState(
    string ProfileId,
    ulong StreamEpoch,
    long EpochAnchorSimTimeNs,
    ulong NextSampleIndex,
    long NextSampleSimTimeNs,
    long CursorSimTimeNs);

public sealed class SignalSampleClock
{
    private const int MaximumBatchSampleCount = 1_000_000;

    private SignalAcquisitionProfileDescriptor _profile;

    private SignalSampleClock(SignalSampleClockState state)
    {
        _profile = FrozenSignalAcquisitionProfiles.Get(state.ProfileId);
        ValidateState(state, _profile.SamplePeriodNs);
        ProfileId = state.ProfileId;
        StreamEpoch = state.StreamEpoch;
        EpochAnchorSimTimeNs = state.EpochAnchorSimTimeNs;
        NextSampleIndex = state.NextSampleIndex;
        NextSampleSimTimeNs = state.NextSampleSimTimeNs;
        CursorSimTimeNs = state.CursorSimTimeNs;
    }

    public string ProfileId { get; private set; }

    public ulong StreamEpoch { get; private set; }

    public long EpochAnchorSimTimeNs { get; private set; }

    public ulong NextSampleIndex { get; private set; }

    public long NextSampleSimTimeNs { get; private set; }

    public long CursorSimTimeNs { get; private set; }

    public uint SampleRateHz => _profile.SampleRateHz;

    public long SamplePeriodNs => _profile.SamplePeriodNs;

    public static SignalSampleClock Start(
        string profileId,
        ulong streamEpoch,
        long startSimTimeNs)
    {
        if (startSimTimeNs < 0)
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.NegativeSimTime",
                nameof(startSimTimeNs));
        }

        return new SignalSampleClock(new SignalSampleClockState(
            profileId,
            streamEpoch,
            startSimTimeNs,
            0,
            startSimTimeNs,
            startSimTimeNs));
    }

    public static SignalSampleClock Restore(SignalSampleClockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new SignalSampleClock(state);
    }

    public IReadOnlyList<SignalSampleTick> DrainBefore(long exclusiveSimTimeNs)
    {
        if (exclusiveSimTimeNs < CursorSimTimeNs)
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.TimeReversed",
                nameof(exclusiveSimTimeNs));
        }

        if (NextSampleSimTimeNs >= exclusiveSimTimeNs)
        {
            CursorSimTimeNs = exclusiveSimTimeNs;
            return Array.Empty<SignalSampleTick>();
        }

        Int128 countWide = ((Int128)exclusiveSimTimeNs - 1 - NextSampleSimTimeNs) /
            SamplePeriodNs + 1;
        if (countWide > MaximumBatchSampleCount)
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.BatchTooLarge",
                nameof(exclusiveSimTimeNs));
        }

        int count = checked((int)countWide);
        UInt128 nextIndexWide = (UInt128)NextSampleIndex + (uint)count;
        Int128 nextTimeWide = (Int128)NextSampleSimTimeNs +
            (Int128)count * SamplePeriodNs;
        if (nextIndexWide > ulong.MaxValue || nextTimeWide > long.MaxValue)
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.StateOverflow",
                nameof(exclusiveSimTimeNs));
        }

        var samples = new SignalSampleTick[count];
        for (int index = 0; index < count; index++)
        {
            samples[index] = new SignalSampleTick(
                ProfileId,
                StreamEpoch,
                NextSampleIndex + (uint)index,
                NextSampleSimTimeNs + index * SamplePeriodNs);
        }

        NextSampleIndex = (ulong)nextIndexWide;
        NextSampleSimTimeNs = (long)nextTimeWide;
        CursorSimTimeNs = exclusiveSimTimeNs;
        return Array.AsReadOnly(samples);
    }

    public SignalProfileBoundary ApplyProfileBoundary(
        string nextProfileId,
        ulong nextStreamEpoch,
        long boundarySimTimeNs,
        FilterStateTransitionPolicy filterStateTransitionPolicy,
        bool standardEcgRecordActive)
    {
        SignalAcquisitionProfileDescriptor nextProfile =
            FrozenSignalAcquisitionProfiles.Get(nextProfileId);
        if (!Enum.IsDefined(filterStateTransitionPolicy))
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.InvalidFilterTransition",
                nameof(filterStateTransitionPolicy));
        }

        if (boundarySimTimeNs != CursorSimTimeNs)
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.BoundaryNotAtCursor",
                nameof(boundarySimTimeNs));
        }

        if (standardEcgRecordActive)
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.StandardRecordActive",
                nameof(standardEcgRecordActive));
        }

        if (nextStreamEpoch <= StreamEpoch)
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.StreamEpochNotMonotonic",
                nameof(nextStreamEpoch));
        }

        if (StringComparer.Ordinal.Equals(ProfileId, nextProfileId))
        {
            throw new SignalSampleClockException(
                "SignalSampleClock.ProfileUnchanged",
                nameof(nextProfileId));
        }

        SignalProfileBoundary boundary = new(
            ProfileId,
            nextProfileId,
            StreamEpoch,
            nextStreamEpoch,
            boundarySimTimeNs,
            filterStateTransitionPolicy);
        _profile = nextProfile;
        ProfileId = nextProfileId;
        StreamEpoch = nextStreamEpoch;
        EpochAnchorSimTimeNs = boundarySimTimeNs;
        NextSampleIndex = 0;
        NextSampleSimTimeNs = boundarySimTimeNs;
        return boundary;
    }

    public SignalSampleClockState CaptureState() => new(
        ProfileId,
        StreamEpoch,
        EpochAnchorSimTimeNs,
        NextSampleIndex,
        NextSampleSimTimeNs,
        CursorSimTimeNs);

    private static void ValidateState(SignalSampleClockState state, long samplePeriodNs)
    {
        if (state.EpochAnchorSimTimeNs < 0 ||
            state.CursorSimTimeNs < state.EpochAnchorSimTimeNs ||
            state.NextSampleSimTimeNs < state.CursorSimTimeNs)
        {
            throw InvalidState();
        }

        Int128 expectedNextTime = (Int128)state.EpochAnchorSimTimeNs +
            (Int128)state.NextSampleIndex * samplePeriodNs;
        if (expectedNextTime > long.MaxValue ||
            expectedNextTime != state.NextSampleSimTimeNs)
        {
            throw InvalidState();
        }

        if (state.NextSampleIndex == 0)
        {
            if (state.CursorSimTimeNs != state.EpochAnchorSimTimeNs)
            {
                throw InvalidState();
            }
        }
        else if ((Int128)state.NextSampleSimTimeNs - samplePeriodNs >=
            state.CursorSimTimeNs)
        {
            throw InvalidState();
        }

        static SignalSampleClockException InvalidState() => new(
            "SignalSampleClock.InvalidCheckpoint",
            nameof(state));
    }
}
