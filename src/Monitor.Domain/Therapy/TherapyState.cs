// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Therapy;

public enum DefibrillationAttemptState
{
    Idle,
    Charging,
    Charged,
    DischargeRequested,
    AwaitingSync,
    Delivered,
    Disarmed,
    Expired,
    Failed,
}

public enum EnergyState
{
    Idle,
    Charging,
    Ready,
    Disarmed,
    Failed,
}

public enum ShockRequestState
{
    None,
    WaitingNextQrs,
    Cancelled,
}

public enum MomentaryLeaseState
{
    Pressed,
    Held,
    Released,
    Cancelled,
    Expired,
}

public enum DefibrillationMode
{
    ManualAsynchronous,
    ManualSynchronized,
}

public sealed record MomentaryTherapyLease(
    Guid InteractionId,
    string ClientId,
    Guid InstanceId,
    ulong AuthorityEpoch,
    ulong GrantedStateRevision,
    long PressedAtSafetyNs,
    long HoldThresholdNs,
    long ExpiresAtSafetyNs,
    ulong RenewSequence,
    MomentaryLeaseState State);

public sealed record TherapyState(
    ulong Revision,
    ulong AuthorityEpoch,
    DefibrillationMode Mode,
    DefibrillationAttemptState Attempt,
    EnergyState Energy,
    ShockRequestState Request,
    MomentaryTherapyLease? Lease,
    ulong DeliveredCount)
{
    public static TherapyState Initial(ulong authorityEpoch, DefibrillationMode mode) =>
        new(0, authorityEpoch, mode, DefibrillationAttemptState.Idle,
            EnergyState.Idle, ShockRequestState.None, null, 0);
}

public sealed record TherapyFact(string EventType, long SafetyTimeNs, Guid CorrelationId);

public sealed record TherapyTransition(TherapyState State, IReadOnlyList<TherapyFact> Facts);

