// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Continuity;

public sealed class ConnectionHealthException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum ConnectionState
{
    Connected,
    Suspect,
    Disconnected,
    Relocking,
}

public enum SamplePlaneProgressHealth
{
    Healthy,
    SamplePlaneStalled,
    Disconnected,
}

public enum ImmediateTransportFailure
{
    WebSocketClose,
    IoError,
    TlsFailure,
}

public enum PlaneProgressUpdateStatus
{
    Accepted,
    IgnoredDuplicateOrStale,
    IgnoredDiscontinuous,
    RequiresResync,
}

public sealed record RealtimeConnectionHealthProfile(
    ulong HeartbeatIntervalNs,
    ulong SuspectAfterNs,
    ulong DisconnectedAfterNs,
    ulong RequiredPlaneStallNs)
{
    public static RealtimeConnectionHealthProfile Default { get; } = new(
        1_000_000_000,
        3_000_000_000,
        5_000_000_000,
        1_000_000_000);
}

public sealed record RequiredPlaneProgressState(
    Guid PlaneId,
    ulong? StreamEpoch,
    ulong? LastBlockSequence,
    ulong LastProgressRunningTimeNs,
    SamplePlaneProgressHealth Health);

public sealed record ConnectionHealthState(
    RealtimeConnectionHealthProfile Profile,
    long LastAuthorityMonotonicNs,
    long LastServerActivityAtNs,
    ulong RunningTimeNs,
    bool SessionRunning,
    ConnectionState ConnectionState,
    ImmediateTransportFailure? LastImmediateFailure,
    IReadOnlyList<RequiredPlaneProgressState> RequiredPlanes);

public sealed class ConnectionHealthStateMachine
{
    private readonly List<PlaneTracker> _requiredPlanes;
    private long _lastAuthorityMonotonicNs;
    private long _lastServerActivityAtNs;
    private ulong _runningTimeNs;
    private bool _sessionRunning;
    private ConnectionState _connectionState;
    private ImmediateTransportFailure? _lastImmediateFailure;

    private ConnectionHealthStateMachine(ConnectionHealthState state)
    {
        ValidateState(state);
        Profile = state.Profile;
        _lastAuthorityMonotonicNs = state.LastAuthorityMonotonicNs;
        _lastServerActivityAtNs = state.LastServerActivityAtNs;
        _runningTimeNs = state.RunningTimeNs;
        _sessionRunning = state.SessionRunning;
        _connectionState = state.ConnectionState;
        _lastImmediateFailure = state.LastImmediateFailure;
        _requiredPlanes = state.RequiredPlanes
            .Select(static plane => new PlaneTracker(
                plane.PlaneId,
                plane.StreamEpoch,
                plane.LastBlockSequence,
                plane.LastProgressRunningTimeNs,
                plane.Health))
            .ToList();
    }

    public RealtimeConnectionHealthProfile Profile { get; }

    public long LastAuthorityMonotonicNs => _lastAuthorityMonotonicNs;

    public ulong RunningTimeNs => _runningTimeNs;

    public bool SessionRunning => _sessionRunning;

    public ConnectionState ConnectionState => _connectionState;

    public ulong RequiredPlaneDisconnectedAfterNs =>
        checked(Profile.RequiredPlaneStallNs * 2);

    public static ConnectionHealthStateMachine Start(
        RealtimeConnectionHealthProfile profile,
        long startAuthorityMonotonicNs,
        bool sessionRunning,
        IReadOnlyList<Guid> requiredPlaneIds)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(requiredPlaneIds);
        ValidateProfile(profile, nameof(profile));
        if (startAuthorityMonotonicNs < 0 ||
            requiredPlaneIds.Count is < 1 or > 128 ||
            requiredPlaneIds.Any(static id => id == Guid.Empty))
        {
            throw Error("ConnectionHealth.InvalidConfiguration", nameof(requiredPlaneIds));
        }

        Guid[] canonicalIds = [.. requiredPlaneIds];
        Array.Sort(canonicalIds, ComparePlaneIds);
        for (int index = 1; index < canonicalIds.Length; index++)
        {
            if (canonicalIds[index - 1] == canonicalIds[index])
            {
                throw Error("ConnectionHealth.InvalidConfiguration", nameof(requiredPlaneIds));
            }
        }

        RequiredPlaneProgressState[] planes = canonicalIds
            .Select(static id => new RequiredPlaneProgressState(
                id,
                null,
                null,
                0,
                SamplePlaneProgressHealth.Healthy))
            .ToArray();
        return new ConnectionHealthStateMachine(new ConnectionHealthState(
            profile,
            startAuthorityMonotonicNs,
            startAuthorityMonotonicNs,
            0,
            sessionRunning,
            ConnectionState.Connected,
            null,
            Array.AsReadOnly(planes)));
    }

    public static ConnectionHealthStateMachine Restore(ConnectionHealthState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Profile);
        ArgumentNullException.ThrowIfNull(state.RequiredPlanes);
        return new ConnectionHealthStateMachine(state);
    }

    public ConnectionHealthState Advance(long authorityMonotonicNs, bool sessionRunning)
    {
        MoveTo(authorityMonotonicNs);
        _sessionRunning = sessionRunning;
        EvaluateConnectionSilence();
        return CaptureState();
    }

    public ConnectionHealthState RecordServerActivity(long authorityMonotonicNs)
    {
        MoveTo(authorityMonotonicNs);
        EvaluateConnectionSilence();
        if (_connectionState is ConnectionState.Connected or ConnectionState.Suspect)
        {
            _lastServerActivityAtNs = authorityMonotonicNs;
            _connectionState = ConnectionState.Connected;
            _lastImmediateFailure = null;
        }

        return CaptureState();
    }

    public ConnectionHealthState RecordImmediateFailure(
        ImmediateTransportFailure failure,
        long authorityMonotonicNs)
    {
        if (!Enum.IsDefined(failure))
        {
            throw Error("ConnectionHealth.InvalidFailure", nameof(failure));
        }

        MoveTo(authorityMonotonicNs);
        _connectionState = ConnectionState.Disconnected;
        _lastImmediateFailure = failure;
        return CaptureState();
    }

    public PlaneProgressUpdateStatus RecordPlaneProgress(
        Guid planeId,
        ulong streamEpoch,
        ulong blockSequence,
        long authorityMonotonicNs)
    {
        int planeIndex = FindPlane(planeId);
        if (planeIndex < 0)
        {
            throw Error("ConnectionHealth.UnknownPlane", nameof(planeId));
        }

        MoveTo(authorityMonotonicNs);
        EvaluateConnectionSilence();
        if (_connectionState is ConnectionState.Disconnected or ConnectionState.Relocking)
        {
            return PlaneProgressUpdateStatus.RequiresResync;
        }

        PlaneTracker plane = _requiredPlanes[planeIndex];
        if (plane.Health == SamplePlaneProgressHealth.Disconnected)
        {
            return PlaneProgressUpdateStatus.RequiresResync;
        }

        if (plane.StreamEpoch is not null)
        {
            ulong currentEpoch = plane.StreamEpoch.Value;
            ulong lastBlockSequence = plane.LastBlockSequence!.Value;
            if (streamEpoch == currentEpoch && blockSequence <= lastBlockSequence)
            {
                return PlaneProgressUpdateStatus.IgnoredDuplicateOrStale;
            }

            if (streamEpoch != currentEpoch ||
                lastBlockSequence == ulong.MaxValue ||
                blockSequence != lastBlockSequence + 1)
            {
                return PlaneProgressUpdateStatus.IgnoredDiscontinuous;
            }
        }

        plane.StreamEpoch = streamEpoch;
        plane.LastBlockSequence = blockSequence;
        plane.LastProgressRunningTimeNs = _runningTimeNs;
        plane.Health = SamplePlaneProgressHealth.Healthy;
        RecordActivityWithoutMoving(authorityMonotonicNs);
        return PlaneProgressUpdateStatus.Accepted;
    }

    public ConnectionHealthState ResetPlaneAfterResync(
        Guid planeId,
        ulong streamEpoch,
        ulong lastBlockSequence,
        long authorityMonotonicNs)
    {
        int planeIndex = FindPlane(planeId);
        if (planeIndex < 0)
        {
            throw Error("ConnectionHealth.UnknownPlane", nameof(planeId));
        }

        MoveTo(authorityMonotonicNs);
        PlaneTracker plane = _requiredPlanes[planeIndex];
        plane.StreamEpoch = streamEpoch;
        plane.LastBlockSequence = lastBlockSequence;
        plane.LastProgressRunningTimeNs = _runningTimeNs;
        plane.Health = SamplePlaneProgressHealth.Healthy;
        return CaptureState();
    }

    public ConnectionHealthState BeginRelocking(long authorityMonotonicNs)
    {
        if (_connectionState != ConnectionState.Disconnected &&
            (_connectionState is not (ConnectionState.Connected or ConnectionState.Suspect) ||
                authorityMonotonicNs < _lastServerActivityAtNs ||
                (ulong)(authorityMonotonicNs - _lastServerActivityAtNs) <
                    Profile.DisconnectedAfterNs))
        {
            throw Error("ConnectionHealth.InvalidTransition", nameof(authorityMonotonicNs));
        }

        MoveTo(authorityMonotonicNs);
        EvaluateConnectionSilence();
        _connectionState = ConnectionState.Relocking;
        return CaptureState();
    }

    public ConnectionHealthState CompleteRelocking(long authorityMonotonicNs)
    {
        if (_connectionState != ConnectionState.Relocking)
        {
            throw Error("ConnectionHealth.InvalidTransition", nameof(authorityMonotonicNs));
        }

        MoveTo(authorityMonotonicNs);
        _connectionState = ConnectionState.Connected;
        _lastServerActivityAtNs = authorityMonotonicNs;
        _lastImmediateFailure = null;
        return CaptureState();
    }

    public ConnectionHealthState CaptureState()
    {
        RequiredPlaneProgressState[] planes = _requiredPlanes
            .Select(static plane => new RequiredPlaneProgressState(
                plane.PlaneId,
                plane.StreamEpoch,
                plane.LastBlockSequence,
                plane.LastProgressRunningTimeNs,
                plane.Health))
            .ToArray();
        return new ConnectionHealthState(
            Profile,
            _lastAuthorityMonotonicNs,
            _lastServerActivityAtNs,
            _runningTimeNs,
            _sessionRunning,
            _connectionState,
            _lastImmediateFailure,
            Array.AsReadOnly(planes));
    }

    private void MoveTo(long authorityMonotonicNs)
    {
        if (authorityMonotonicNs < _lastAuthorityMonotonicNs)
        {
            throw Error("ConnectionHealth.TimeReversed", nameof(authorityMonotonicNs));
        }

        ulong elapsed = checked((ulong)(
            authorityMonotonicNs - _lastAuthorityMonotonicNs));
        if (_sessionRunning)
        {
            UInt128 runningWide = (UInt128)_runningTimeNs + elapsed;
            if (runningWide > ulong.MaxValue)
            {
                throw Error("ConnectionHealth.StateOverflow", nameof(authorityMonotonicNs));
            }

            _runningTimeNs = (ulong)runningWide;
        }

        _lastAuthorityMonotonicNs = authorityMonotonicNs;
        EvaluatePlanes();
    }

    private void EvaluateConnectionSilence()
    {
        if (_connectionState is not (ConnectionState.Connected or ConnectionState.Suspect))
        {
            return;
        }

        ulong silence = checked((ulong)(
            _lastAuthorityMonotonicNs - _lastServerActivityAtNs));
        if (silence >= Profile.DisconnectedAfterNs)
        {
            _connectionState = ConnectionState.Disconnected;
        }
        else if (silence >= Profile.SuspectAfterNs)
        {
            _connectionState = ConnectionState.Suspect;
        }
        else
        {
            _connectionState = ConnectionState.Connected;
        }
    }

    private void EvaluatePlanes()
    {
        ulong disconnectedAfterNs = RequiredPlaneDisconnectedAfterNs;
        foreach (PlaneTracker plane in _requiredPlanes)
        {
            if (plane.Health == SamplePlaneProgressHealth.Disconnected)
            {
                continue;
            }

            ulong age = _runningTimeNs - plane.LastProgressRunningTimeNs;
            plane.Health = age >= disconnectedAfterNs
                ? SamplePlaneProgressHealth.Disconnected
                : age >= Profile.RequiredPlaneStallNs
                    ? SamplePlaneProgressHealth.SamplePlaneStalled
                    : SamplePlaneProgressHealth.Healthy;
        }
    }

    private void RecordActivityWithoutMoving(long authorityMonotonicNs)
    {
        if (_connectionState is ConnectionState.Connected or ConnectionState.Suspect)
        {
            _lastServerActivityAtNs = authorityMonotonicNs;
            _connectionState = ConnectionState.Connected;
            _lastImmediateFailure = null;
        }
    }

    private int FindPlane(Guid planeId)
    {
        for (int index = 0; index < _requiredPlanes.Count; index++)
        {
            if (_requiredPlanes[index].PlaneId == planeId)
            {
                return index;
            }
        }

        return -1;
    }

    private static void ValidateState(ConnectionHealthState state)
    {
        ValidateProfile(state.Profile, nameof(state));
        if (state.LastAuthorityMonotonicNs < 0 ||
            state.LastServerActivityAtNs < 0 ||
            state.LastServerActivityAtNs > state.LastAuthorityMonotonicNs ||
            state.RunningTimeNs > (ulong)state.LastAuthorityMonotonicNs ||
            !Enum.IsDefined(state.ConnectionState) ||
            (state.LastImmediateFailure is not null &&
                !Enum.IsDefined(state.LastImmediateFailure.Value)) ||
            state.RequiredPlanes.Count is < 1 or > 128)
        {
            throw InvalidCheckpoint(nameof(state));
        }

        ulong silence = checked((ulong)(
            state.LastAuthorityMonotonicNs - state.LastServerActivityAtNs));
        if ((state.ConnectionState == ConnectionState.Connected &&
                silence >= state.Profile.SuspectAfterNs) ||
            (state.ConnectionState == ConnectionState.Suspect &&
                (silence < state.Profile.SuspectAfterNs ||
                    silence >= state.Profile.DisconnectedAfterNs)) ||
            (state.ConnectionState == ConnectionState.Disconnected &&
                state.LastImmediateFailure is null &&
                silence < state.Profile.DisconnectedAfterNs) ||
            (state.ConnectionState is ConnectionState.Connected or ConnectionState.Suspect &&
                state.LastImmediateFailure is not null))
        {
            throw InvalidCheckpoint(nameof(state));
        }

        ulong planeDisconnectedAfter = checked(state.Profile.RequiredPlaneStallNs * 2);
        Guid? previousId = null;
        foreach (RequiredPlaneProgressState plane in state.RequiredPlanes)
        {
            if (plane is null ||
                plane.PlaneId == Guid.Empty ||
                (previousId is not null && ComparePlaneIds(previousId.Value, plane.PlaneId) >= 0) ||
                plane.LastProgressRunningTimeNs > state.RunningTimeNs ||
                (plane.StreamEpoch is null) != (plane.LastBlockSequence is null) ||
                !Enum.IsDefined(plane.Health))
            {
                throw InvalidCheckpoint(nameof(state));
            }

            ulong age = state.RunningTimeNs - plane.LastProgressRunningTimeNs;
            SamplePlaneProgressHealth expected = age >= planeDisconnectedAfter
                ? SamplePlaneProgressHealth.Disconnected
                : age >= state.Profile.RequiredPlaneStallNs
                    ? SamplePlaneProgressHealth.SamplePlaneStalled
                    : SamplePlaneProgressHealth.Healthy;
            if (plane.Health != expected)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            previousId = plane.PlaneId;
        }
    }

    private static void ValidateProfile(
        RealtimeConnectionHealthProfile profile,
        string parameterName)
    {
        UInt128 twiceHeartbeat = (UInt128)profile.HeartbeatIntervalNs * 2;
        UInt128 twicePlaneStall = (UInt128)profile.RequiredPlaneStallNs * 2;
        if (profile.HeartbeatIntervalNs == 0 ||
            profile.SuspectAfterNs < profile.HeartbeatIntervalNs ||
            profile.DisconnectedAfterNs <= profile.SuspectAfterNs ||
            profile.DisconnectedAfterNs < twiceHeartbeat ||
            profile.DisconnectedAfterNs > long.MaxValue ||
            profile.RequiredPlaneStallNs == 0 ||
            twicePlaneStall > long.MaxValue)
        {
            throw Error("ConnectionHealth.InvalidProfile", parameterName);
        }
    }

    private static int ComparePlaneIds(Guid left, Guid right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        _ = left.TryWriteBytes(leftBytes, bigEndian: true, out _);
        _ = right.TryWriteBytes(rightBytes, bigEndian: true, out _);
        return leftBytes.SequenceCompareTo(rightBytes);
    }

    private static ConnectionHealthException InvalidCheckpoint(string parameterName) =>
        Error("ConnectionHealth.InvalidCheckpoint", parameterName);

    private static ConnectionHealthException Error(string reasonCode, string parameterName) =>
        new(reasonCode, parameterName);

    private sealed class PlaneTracker(
        Guid planeId,
        ulong? streamEpoch,
        ulong? lastBlockSequence,
        ulong lastProgressRunningTimeNs,
        SamplePlaneProgressHealth health)
    {
        public Guid PlaneId { get; } = planeId;

        public ulong? StreamEpoch { get; set; } = streamEpoch;

        public ulong? LastBlockSequence { get; set; } = lastBlockSequence;

        public ulong LastProgressRunningTimeNs { get; set; } =
            lastProgressRunningTimeNs;

        public SamplePlaneProgressHealth Health { get; set; } = health;
    }
}
