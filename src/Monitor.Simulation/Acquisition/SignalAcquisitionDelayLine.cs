// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

public sealed class SignalAcquisitionDelayException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public readonly record struct DelayedSignalSample(
    string ProfileId,
    ulong StreamEpoch,
    ulong SampleIndex,
    long SourceSimTimeNs,
    long AvailableSimTimeNs,
    short NormalizedValue,
    uint QualityFlags);

public sealed record SignalAcquisitionDelayState(
    string ProfileId,
    ulong StreamEpoch,
    long EpochAnchorSimTimeNs,
    ulong NextInputSampleIndex,
    long ReleaseCursorSimTimeNs,
    int Capacity,
    IReadOnlyList<DelayedSignalSample> PendingSamples);

public sealed class SignalAcquisitionDelayLine
{
    public const int MaximumCapacity = 1_000_000;

    private readonly List<DelayedSignalSample> _pendingSamples;
    private readonly SignalAcquisitionProfileDescriptor _profile;

    private SignalAcquisitionDelayLine(SignalAcquisitionDelayState state)
    {
        if (state.PendingSamples.Count > MaximumCapacity)
        {
            throw InvalidState();
        }

        DelayedSignalSample[] pendingSamples = [.. state.PendingSamples];
        _profile = FrozenSignalAcquisitionProfiles.Get(state.ProfileId);
        ValidateState(state with
        {
            PendingSamples = pendingSamples,
        }, _profile);

        ProfileId = state.ProfileId;
        StreamEpoch = state.StreamEpoch;
        EpochAnchorSimTimeNs = state.EpochAnchorSimTimeNs;
        NextInputSampleIndex = state.NextInputSampleIndex;
        ReleaseCursorSimTimeNs = state.ReleaseCursorSimTimeNs;
        Capacity = state.Capacity;
        _pendingSamples = new List<DelayedSignalSample>(pendingSamples);

        static SignalAcquisitionDelayException InvalidState() => new(
            "SignalAcquisitionDelay.InvalidCheckpoint",
            nameof(state));
    }

    public string ProfileId { get; }

    public ulong StreamEpoch { get; }

    public long EpochAnchorSimTimeNs { get; }

    public ulong NextInputSampleIndex { get; private set; }

    public long ReleaseCursorSimTimeNs { get; private set; }

    public int Capacity { get; }

    public int PendingCount => _pendingSamples.Count;

    public long LatencyNs => _profile.LatencyNs;

    public static SignalAcquisitionDelayLine Start(
        string profileId,
        ulong streamEpoch,
        long epochAnchorSimTimeNs,
        int capacity)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return new SignalAcquisitionDelayLine(new SignalAcquisitionDelayState(
            profileId,
            streamEpoch,
            epochAnchorSimTimeNs,
            0,
            epochAnchorSimTimeNs,
            capacity,
            Array.Empty<DelayedSignalSample>()));
    }

    public static SignalAcquisitionDelayLine Restore(SignalAcquisitionDelayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.PendingSamples);
        return new SignalAcquisitionDelayLine(state);
    }

    public void Enqueue(
        SignalSampleTick tick,
        short normalizedValue,
        uint qualityFlags = 0)
    {
        if (!StringComparer.Ordinal.Equals(tick.ProfileId, ProfileId))
        {
            throw new SignalAcquisitionDelayException(
                "SignalAcquisitionDelay.ProfileMismatch",
                nameof(tick));
        }

        if (tick.StreamEpoch != StreamEpoch)
        {
            throw new SignalAcquisitionDelayException(
                "SignalAcquisitionDelay.StreamEpochMismatch",
                nameof(tick));
        }

        if (tick.SampleIndex != NextInputSampleIndex)
        {
            throw new SignalAcquisitionDelayException(
                "SignalAcquisitionDelay.InputIndexDiscontinuous",
                nameof(tick));
        }

        Int128 expectedSourceTime = (Int128)EpochAnchorSimTimeNs +
            (Int128)tick.SampleIndex * _profile.SamplePeriodNs;
        if (expectedSourceTime > long.MaxValue ||
            expectedSourceTime != tick.SimTimeNs)
        {
            throw new SignalAcquisitionDelayException(
                "SignalAcquisitionDelay.InputTimeDiscontinuous",
                nameof(tick));
        }

        Int128 availableTimeWide = expectedSourceTime + _profile.LatencyNs;
        if (availableTimeWide > long.MaxValue || tick.SampleIndex == ulong.MaxValue)
        {
            throw new SignalAcquisitionDelayException(
                "SignalAcquisitionDelay.StateOverflow",
                nameof(tick));
        }

        if (availableTimeWide <= ReleaseCursorSimTimeNs)
        {
            throw new SignalAcquisitionDelayException(
                "SignalAcquisitionDelay.InputArrivedTooLate",
                nameof(tick));
        }

        if (_pendingSamples.Count == Capacity)
        {
            throw new SignalAcquisitionDelayException(
                "SignalAcquisitionDelay.CapacityExceeded",
                nameof(tick));
        }

        _pendingSamples.Add(new DelayedSignalSample(
            ProfileId,
            StreamEpoch,
            tick.SampleIndex,
            tick.SimTimeNs,
            (long)availableTimeWide,
            normalizedValue,
            qualityFlags));
        NextInputSampleIndex++;
    }

    public IReadOnlyList<DelayedSignalSample> DrainAvailable(long simTimeNs)
    {
        if (simTimeNs < ReleaseCursorSimTimeNs)
        {
            throw new SignalAcquisitionDelayException(
                "SignalAcquisitionDelay.TimeReversed",
                nameof(simTimeNs));
        }

        int count = 0;
        while (count < _pendingSamples.Count &&
            _pendingSamples[count].AvailableSimTimeNs <= simTimeNs)
        {
            count++;
        }

        if (count == 0)
        {
            ReleaseCursorSimTimeNs = simTimeNs;
            return Array.Empty<DelayedSignalSample>();
        }

        DelayedSignalSample[] available = _pendingSamples.GetRange(0, count).ToArray();
        _pendingSamples.RemoveRange(0, count);
        ReleaseCursorSimTimeNs = simTimeNs;
        return Array.AsReadOnly(available);
    }

    public SignalAcquisitionDelayState CaptureState() => new(
        ProfileId,
        StreamEpoch,
        EpochAnchorSimTimeNs,
        NextInputSampleIndex,
        ReleaseCursorSimTimeNs,
        Capacity,
        Array.AsReadOnly(_pendingSamples.ToArray()));

    private static void ValidateState(
        SignalAcquisitionDelayState state,
        SignalAcquisitionProfileDescriptor profile)
    {
        if (state.EpochAnchorSimTimeNs < 0 ||
            state.ReleaseCursorSimTimeNs < state.EpochAnchorSimTimeNs ||
            state.Capacity is < 1 or > MaximumCapacity ||
            state.PendingSamples.Count > state.Capacity)
        {
            throw InvalidState();
        }

        ulong pendingCount = checked((ulong)state.PendingSamples.Count);
        if (state.NextInputSampleIndex < pendingCount)
        {
            throw InvalidState();
        }

        ulong firstPendingIndex = state.NextInputSampleIndex - pendingCount;
        if (firstPendingIndex > 0)
        {
            UInt128 previousIndex = (UInt128)firstPendingIndex - 1;
            Int128 previousAvailableTime = (Int128)state.EpochAnchorSimTimeNs +
                (Int128)previousIndex * profile.SamplePeriodNs + profile.LatencyNs;
            if (previousAvailableTime > state.ReleaseCursorSimTimeNs)
            {
                throw InvalidState();
            }
        }

        for (int index = 0; index < state.PendingSamples.Count; index++)
        {
            DelayedSignalSample sample = state.PendingSamples[index];
            ulong expectedIndex = firstPendingIndex + (uint)index;
            Int128 expectedSourceTime = (Int128)state.EpochAnchorSimTimeNs +
                (Int128)expectedIndex * profile.SamplePeriodNs;
            Int128 expectedAvailableTime = expectedSourceTime + profile.LatencyNs;
            if (!StringComparer.Ordinal.Equals(sample.ProfileId, state.ProfileId) ||
                sample.StreamEpoch != state.StreamEpoch ||
                sample.SampleIndex != expectedIndex ||
                expectedSourceTime > long.MaxValue ||
                sample.SourceSimTimeNs != expectedSourceTime ||
                expectedAvailableTime > long.MaxValue ||
                sample.AvailableSimTimeNs != expectedAvailableTime ||
                sample.AvailableSimTimeNs <= state.ReleaseCursorSimTimeNs)
            {
                throw InvalidState();
            }
        }

        static SignalAcquisitionDelayException InvalidState() => new(
            "SignalAcquisitionDelay.InvalidCheckpoint",
            nameof(state));
    }
}
