// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;

namespace Monitor.Simulation.Acquisition;

public sealed class SensorFaultException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class SensorProfileDescriptor
{
    private readonly IReadOnlyList<string> _compatibleCategories;
    private readonly ReadOnlyCollection<string> _failureStates;

    internal SensorProfileDescriptor(
        string profileId,
        string sensorClass,
        string[] compatibleCategories,
        string[] failureStates)
    {
        ProfileId = profileId;
        SensorClass = sensorClass;
        _compatibleCategories = Array.AsReadOnly(compatibleCategories);
        _failureStates = Array.AsReadOnly(failureStates);
    }

    public string ProfileId { get; }

    public string SensorClass { get; }

    public IReadOnlyList<string> CompatibleCategories => _compatibleCategories;

    public IReadOnlyList<string> FailureStates => _failureStates;

    public int FailureStateRank(string failureState)
    {
        ArgumentNullException.ThrowIfNull(failureState);
        for (int index = 0; index < _failureStates.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(_failureStates[index], failureState))
            {
                return index;
            }
        }

        return -1;
    }
}

public static class FrozenSensorProfiles
{
    private static readonly string[] CompatibleCategories =
    [
        "Adult",
        "Pediatric",
        "Neonatal",
    ];

    public static SensorProfileDescriptor Get(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return profileId switch
        {
            "SensorECGGeneric@1" => New(
                profileId,
                "ECGElectrodes",
                "LeadOff",
                "Noise",
                "Disconnected"),
            "SensorSpO2Generic@1" => New(
                profileId,
                "OpticalOximetry",
                "LowPerfusion",
                "Motion",
                "SensorOff",
                "Disconnected"),
            "SensorNIBPCuffGeneric@1" => New(
                profileId,
                "NIBPCuff",
                "LooseCuff",
                "Overpressure",
                "Timeout",
                "Disconnected"),
            "SensorPressureGeneric@1" => New(
                profileId,
                "PressureTransducer",
                "NotZeroed",
                "Overdamped",
                "Underdamped",
                "Disconnected"),
            "SensorCO2Generic@1" => New(
                profileId,
                "SidestreamCO2",
                "Occluded",
                "SampleLineOff",
                "NoBreath",
                "Disconnected"),
            "SensorTempGeneric@1" => New(
                profileId,
                "TemperatureProbe",
                "ProbeOff",
                "OutOfRange",
                "Disconnected"),
            _ => throw new SensorFaultException(
                "SensorFault.UnknownProfile",
                nameof(profileId)),
        };
    }

    private static SensorProfileDescriptor New(
        string profileId,
        string sensorClass,
        params string[] failureStates) => new(
            profileId,
            sensorClass,
            [.. CompatibleCategories],
            failureStates);
}

public enum SensorFaultOperation
{
    Start,
    Stop,
}

public enum SensorFaultOrigin
{
    ScenarioOverride,
    SensorModel,
    LatentPhysiology,
}

public sealed record ActiveSensorFault(
    string FailureState,
    SensorFaultOrigin Origin,
    long ActivatedAtSimTimeNs,
    ulong ActivatedAtRevision);

public sealed record SensorFaultState(
    string ProfileId,
    string SensorInstanceId,
    ulong Revision,
    long CursorSimTimeNs,
    IReadOnlyList<ActiveSensorFault> ActiveFaults);

public sealed record SensorFaultTransition(
    string ProfileId,
    string SensorInstanceId,
    SensorFaultOperation Operation,
    string FailureState,
    SensorFaultOrigin Origin,
    long SimTimeNs,
    ulong PreviousRevision,
    ulong Revision);

public sealed class SensorFaultStateMachine
{
    private readonly List<ActiveSensorFault> _activeFaults;
    private readonly SensorProfileDescriptor _profile;

    private SensorFaultStateMachine(SensorFaultState state)
    {
        _profile = FrozenSensorProfiles.Get(state.ProfileId);
        if (state.ActiveFaults.Count > _profile.FailureStates.Count)
        {
            throw InvalidState();
        }

        ActiveSensorFault[] activeFaults = [.. state.ActiveFaults];
        ValidateState(state with
        {
            ActiveFaults = activeFaults,
        }, _profile);

        ProfileId = state.ProfileId;
        SensorInstanceId = state.SensorInstanceId;
        Revision = state.Revision;
        CursorSimTimeNs = state.CursorSimTimeNs;
        _activeFaults = new List<ActiveSensorFault>(activeFaults);

        static SensorFaultException InvalidState() => new(
            "SensorFault.InvalidCheckpoint",
            nameof(state));
    }

    public string ProfileId { get; }

    public string SensorInstanceId { get; }

    public ulong Revision { get; private set; }

    public long CursorSimTimeNs { get; private set; }

    public IReadOnlyList<ActiveSensorFault> ActiveFaults =>
        Array.AsReadOnly(_activeFaults.ToArray());

    public static SensorFaultStateMachine Start(
        string profileId,
        string sensorInstanceId,
        long startSimTimeNs)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(sensorInstanceId);
        return new SensorFaultStateMachine(new SensorFaultState(
            profileId,
            sensorInstanceId,
            0,
            startSimTimeNs,
            Array.Empty<ActiveSensorFault>()));
    }

    public static SensorFaultStateMachine Restore(SensorFaultState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.ActiveFaults);
        return new SensorFaultStateMachine(state);
    }

    public SensorFaultTransition Apply(
        SensorFaultOperation operation,
        string failureState,
        SensorFaultOrigin origin,
        long simTimeNs,
        ulong expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(failureState);
        if (!Enum.IsDefined(operation))
        {
            throw new SensorFaultException(
                "SensorFault.InvalidOperation",
                nameof(operation));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new SensorFaultException(
                "SensorFault.InvalidOrigin",
                nameof(origin));
        }

        int failureStateRank = _profile.FailureStateRank(failureState);
        if (failureStateRank < 0)
        {
            throw new SensorFaultException(
                "SensorFault.UnsupportedFailureState",
                nameof(failureState));
        }

        if (!OriginAllowed(failureState, origin))
        {
            throw new SensorFaultException(
                "SensorFault.OriginNotAllowed",
                nameof(origin));
        }

        if (expectedRevision != Revision)
        {
            throw new SensorFaultException(
                "SensorFault.RevisionConflict",
                nameof(expectedRevision));
        }

        if (simTimeNs < CursorSimTimeNs)
        {
            throw new SensorFaultException(
                "SensorFault.TimeReversed",
                nameof(simTimeNs));
        }

        if (Revision == ulong.MaxValue)
        {
            throw new SensorFaultException(
                "SensorFault.RevisionOverflow",
                nameof(expectedRevision));
        }

        int activeIndex = FindActiveIndex(failureState);
        if (operation == SensorFaultOperation.Start && activeIndex >= 0)
        {
            throw new SensorFaultException(
                "SensorFault.AlreadyActive",
                nameof(failureState));
        }

        if (operation == SensorFaultOperation.Stop && activeIndex < 0)
        {
            throw new SensorFaultException(
                "SensorFault.NotActive",
                nameof(failureState));
        }

        if (operation == SensorFaultOperation.Stop &&
            _activeFaults[activeIndex].Origin != origin)
        {
            throw new SensorFaultException(
                "SensorFault.OriginMismatch",
                nameof(origin));
        }

        ulong previousRevision = Revision;
        ulong nextRevision = Revision + 1;
        if (operation == SensorFaultOperation.Start)
        {
            ActiveSensorFault activeFault = new(
                failureState,
                origin,
                simTimeNs,
                nextRevision);
            int insertionIndex = 0;
            while (insertionIndex < _activeFaults.Count &&
                _profile.FailureStateRank(_activeFaults[insertionIndex].FailureState) <
                failureStateRank)
            {
                insertionIndex++;
            }

            _activeFaults.Insert(insertionIndex, activeFault);
        }
        else
        {
            _activeFaults.RemoveAt(activeIndex);
        }

        Revision = nextRevision;
        CursorSimTimeNs = simTimeNs;
        return new SensorFaultTransition(
            ProfileId,
            SensorInstanceId,
            operation,
            failureState,
            origin,
            simTimeNs,
            previousRevision,
            nextRevision);
    }

    public SensorFaultState CaptureState() => new(
        ProfileId,
        SensorInstanceId,
        Revision,
        CursorSimTimeNs,
        Array.AsReadOnly(_activeFaults.ToArray()));

    private static bool OriginAllowed(
        string failureState,
        SensorFaultOrigin origin) =>
        StringComparer.Ordinal.Equals(failureState, "LowPerfusion")
            ? origin is SensorFaultOrigin.SensorModel or SensorFaultOrigin.LatentPhysiology
            : origin is SensorFaultOrigin.ScenarioOverride or SensorFaultOrigin.SensorModel;

    private int FindActiveIndex(string failureState)
    {
        for (int index = 0; index < _activeFaults.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(
                _activeFaults[index].FailureState,
                failureState))
            {
                return index;
            }
        }

        return -1;
    }

    private static void ValidateState(
        SensorFaultState state,
        SensorProfileDescriptor profile)
    {
        if (string.IsNullOrWhiteSpace(state.SensorInstanceId) ||
            state.CursorSimTimeNs < 0 ||
            state.ActiveFaults.Count > profile.FailureStates.Count ||
            state.Revision < (ulong)state.ActiveFaults.Count)
        {
            throw InvalidState();
        }

        int previousRank = -1;
        HashSet<ulong> activationRevisions = [];
        foreach (ActiveSensorFault activeFault in state.ActiveFaults)
        {
            if (activeFault is null || string.IsNullOrEmpty(activeFault.FailureState))
            {
                throw InvalidState();
            }

            int rank = profile.FailureStateRank(activeFault.FailureState);
            if (rank <= previousRank ||
                !Enum.IsDefined(activeFault.Origin) ||
                !OriginAllowed(activeFault.FailureState, activeFault.Origin) ||
                activeFault.ActivatedAtSimTimeNs < 0 ||
                activeFault.ActivatedAtSimTimeNs > state.CursorSimTimeNs ||
                activeFault.ActivatedAtRevision == 0 ||
                activeFault.ActivatedAtRevision > state.Revision ||
                !activationRevisions.Add(activeFault.ActivatedAtRevision))
            {
                throw InvalidState();
            }

            previousRank = rank;
        }

        static SensorFaultException InvalidState() => new(
            "SensorFault.InvalidCheckpoint",
            nameof(state));
    }
}
