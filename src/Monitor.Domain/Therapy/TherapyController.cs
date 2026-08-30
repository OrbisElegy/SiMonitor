// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Authority;
using Monitor.Domain.Common;

namespace Monitor.Domain.Therapy;

public sealed class TherapyController
{
    private readonly Guid _instanceId;
    private TherapyState _state;

    public TherapyController(Guid instanceId, ulong authorityEpoch, DefibrillationMode mode)
    {
        if (instanceId == Guid.Empty)
        {
            throw new ArgumentException("The instance ID must not be empty.", nameof(instanceId));
        }

        _instanceId = instanceId;
        _state = TherapyState.Initial(authorityEpoch, mode);
    }

    public TherapyState State => _state;

    public DomainResult<TherapyTransition> BeginCharge(long safetyTimeNs, Guid correlationId)
    {
        if (_state.Energy != EnergyState.Idle)
        {
            return Reject("therapy.energy.not_idle", "energy_state", correlationId);
        }

        return Accept(
            _state with
            {
                Revision = _state.Revision + 1,
                Attempt = DefibrillationAttemptState.Charging,
                Energy = EnergyState.Charging,
                Request = ShockRequestState.None,
                Lease = null,
            },
            new TherapyFact("ChargeStarted", safetyTimeNs, correlationId));
    }

    public DomainResult<TherapyTransition> MarkChargeReady(long safetyTimeNs, Guid correlationId)
    {
        if (_state.Energy != EnergyState.Charging)
        {
            return Reject("therapy.charge.not_charging", "energy_state", correlationId);
        }

        return Accept(
            _state with
            {
                Revision = _state.Revision + 1,
                Attempt = DefibrillationAttemptState.Charged,
                Energy = EnergyState.Ready,
            },
            new TherapyFact("ChargeReady", safetyTimeNs, correlationId));
    }

    public DomainResult<TherapyTransition> PressShock(
        Guid interactionId,
        string clientId,
        ulong authorityEpoch,
        long safetyTimeNs,
        long holdThresholdNs,
        long expiresAtSafetyNs,
        Guid correlationId)
    {
        if (_state.Energy != EnergyState.Ready || _state.Attempt != DefibrillationAttemptState.Charged)
        {
            return Reject("therapy.shock.not_ready", "energy_state", correlationId);
        }

        if (interactionId == Guid.Empty || string.IsNullOrWhiteSpace(clientId))
        {
            return Reject("therapy.lease.identity_invalid", "interaction_id", correlationId);
        }

        if (authorityEpoch != _state.AuthorityEpoch)
        {
            return Reject("authority.epoch.stale", "authority_epoch", correlationId);
        }

        if (holdThresholdNs <= 0 || expiresAtSafetyNs <= safetyTimeNs ||
            safetyTimeNs > long.MaxValue - holdThresholdNs ||
            safetyTimeNs + holdThresholdNs > expiresAtSafetyNs)
        {
            return Reject("therapy.lease.time_order", "hold_threshold_ns", correlationId);
        }

        ulong nextRevision = _state.Revision + 1;
        MomentaryTherapyLease lease = new(
            interactionId,
            clientId,
            _instanceId,
            authorityEpoch,
            nextRevision,
            safetyTimeNs,
            holdThresholdNs,
            expiresAtSafetyNs,
            0,
            MomentaryLeaseState.Pressed);

        return Accept(
            _state with
            {
                Revision = nextRevision,
                Attempt = DefibrillationAttemptState.DischargeRequested,
                Lease = lease,
            },
            new TherapyFact("ShockInteractionPressed", safetyTimeNs, correlationId));
    }

    public DomainResult<TherapyTransition> ApplySafetyInputs(IEnumerable<SafetyInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        TherapyState stateBeforeBatch = _state;
        List<TherapyFact> facts = [];

        foreach (SafetyInput input in inputs.Order(SafetyInputComparer.Instance))
        {
            DomainResult<TherapyTransition> result = ApplySafetyInput(input);
            if (!result.IsAccepted)
            {
                _state = stateBeforeBatch;
                return result;
            }

            facts.AddRange(result.Value!.Facts);
        }

        return DomainResult.Accept(new TherapyTransition(_state, facts));
    }

    private DomainResult<TherapyTransition> ApplySafetyInput(SafetyInput input) => input.Kind switch
    {
        SafetyInputKind.HoldThresholdElapsed => HandleHoldThreshold(input),
        SafetyInputKind.ValidQrs => HandleValidQrs(input),
        SafetyInputKind.LeaseRenewed => RenewLease(input),
        SafetyInputKind.AuthorityEpochChanged => ChangeEpoch(input),
        SafetyInputKind.LeaseExpired => Cancel(input, MomentaryLeaseState.Expired,
            DefibrillationAttemptState.Expired, "ShockLeaseExpired", true),
        SafetyInputKind.DeviceFaulted => Fail(input),
        SafetyInputKind.ShockReleased => Cancel(input, MomentaryLeaseState.Released,
            DefibrillationAttemptState.Disarmed, "ShockInteractionReleased", true),
        SafetyInputKind.PermissionRevoked or
        SafetyInputKind.EnergyDisarmed or
        SafetyInputKind.SessionPaused or
        SafetyInputKind.SessionEnded or
        SafetyInputKind.AuthorityRecovered => Cancel(input, MomentaryLeaseState.Cancelled,
            DefibrillationAttemptState.Disarmed, "ShockRequestCancelled"),
        SafetyInputKind.ShockCancelled => Cancel(input, MomentaryLeaseState.Cancelled,
            DefibrillationAttemptState.Disarmed, "ShockRequestCancelled", true),
        _ => Reject("therapy.input.unsupported", "kind", input.CorrelationId),
    };

    private DomainResult<TherapyTransition> HandleHoldThreshold(SafetyInput input)
    {
        if (_state.Attempt == DefibrillationAttemptState.Delivered)
        {
            return AcceptWithoutChange();
        }

        MomentaryTherapyLease? lease = ActiveLease(input, out DomainRejection? rejection);
        if (lease is null)
        {
            return DomainResult.Reject<TherapyTransition>(rejection!);
        }

        if (input.SafetyTimeNs < lease.PressedAtSafetyNs + lease.HoldThresholdNs)
        {
            return Reject("therapy.lease.hold_too_short", "safety_time_ns", input.CorrelationId);
        }

        if (input.SafetyTimeNs >= lease.ExpiresAtSafetyNs)
        {
            return Cancel(input, MomentaryLeaseState.Expired,
                DefibrillationAttemptState.Expired, "ShockLeaseExpired", true);
        }

        MomentaryTherapyLease held = lease with { State = MomentaryLeaseState.Held };
        if (_state.Mode == DefibrillationMode.ManualAsynchronous)
        {
            return Deliver(input, held);
        }

        return Accept(
            _state with
            {
                Revision = _state.Revision + 1,
                Attempt = DefibrillationAttemptState.AwaitingSync,
                Request = ShockRequestState.WaitingNextQrs,
                Lease = held,
            },
            new TherapyFact("ShockInteractionHeld", input.SafetyTimeNs, input.CorrelationId));
    }

    private DomainResult<TherapyTransition> HandleValidQrs(SafetyInput input)
    {
        if (_state.Mode != DefibrillationMode.ManualSynchronized ||
            _state.Attempt != DefibrillationAttemptState.AwaitingSync ||
            _state.Request != ShockRequestState.WaitingNextQrs)
        {
            return AcceptWithoutChange();
        }

        MomentaryTherapyLease? lease = ActiveLease(input, out DomainRejection? rejection);
        if (lease is null)
        {
            return DomainResult.Reject<TherapyTransition>(rejection!);
        }

        if (lease.State != MomentaryLeaseState.Held)
        {
            return Reject("therapy.lease.not_held", "lease.state", input.CorrelationId);
        }

        if (input.SafetyTimeNs >= lease.ExpiresAtSafetyNs)
        {
            return Cancel(input, MomentaryLeaseState.Expired,
                DefibrillationAttemptState.Expired, "ShockLeaseExpired", true);
        }

        return Deliver(input, lease);
    }

    private DomainResult<TherapyTransition> Deliver(SafetyInput input, MomentaryTherapyLease lease)
    {
        if (_state.Energy != EnergyState.Ready)
        {
            return Reject("therapy.shock.not_ready", "energy_state", input.CorrelationId);
        }

        MomentaryTherapyLease completed = lease with { State = MomentaryLeaseState.Released };
        return Accept(
            _state with
            {
                Revision = _state.Revision + 1,
                Attempt = DefibrillationAttemptState.Delivered,
                Energy = EnergyState.Idle,
                Request = ShockRequestState.None,
                Lease = completed,
                DeliveredCount = _state.DeliveredCount + 1,
            },
            new TherapyFact("ShockDelivered", input.SafetyTimeNs, input.CorrelationId));
    }

    private DomainResult<TherapyTransition> RenewLease(SafetyInput input)
    {
        MomentaryTherapyLease? lease = ActiveLease(input, out DomainRejection? rejection);
        if (lease is null)
        {
            return DomainResult.Reject<TherapyTransition>(rejection!);
        }

        if (input.SafetyTimeNs >= lease.ExpiresAtSafetyNs)
        {
            return Cancel(input, MomentaryLeaseState.Expired,
                DefibrillationAttemptState.Expired, "ShockLeaseExpired", true);
        }

        _state = _state with { Lease = lease with { RenewSequence = lease.RenewSequence + 1 } };
        return DomainResult.Accept(new TherapyTransition(_state, []));
    }

    private DomainResult<TherapyTransition> ChangeEpoch(SafetyInput input)
    {
        if (input.AuthorityEpoch is null || input.AuthorityEpoch <= _state.AuthorityEpoch)
        {
            return Reject("authority.epoch.not_monotonic", "authority_epoch", input.CorrelationId);
        }

        DomainResult<TherapyTransition> cancelled = Cancel(
            input,
            MomentaryLeaseState.Cancelled,
            DefibrillationAttemptState.Disarmed,
            "ShockRequestCancelled");
        if (!cancelled.IsAccepted)
        {
            return cancelled;
        }

        bool cancellationChangedState = cancelled.Value!.Facts.Count > 0;
        _state = _state with
        {
            AuthorityEpoch = input.AuthorityEpoch.Value,
            Revision = cancellationChangedState ? _state.Revision : _state.Revision + 1,
        };
        List<TherapyFact> facts = [.. cancelled.Value.Facts];
        facts.Add(new TherapyFact("AuthorityEpochChanged", input.SafetyTimeNs, input.CorrelationId));
        return DomainResult.Accept(new TherapyTransition(_state, facts));
    }

    private DomainResult<TherapyTransition> Fail(SafetyInput input)
    {
        MomentaryTherapyLease? lease = _state.Lease;
        if (lease is not null && lease.State is MomentaryLeaseState.Pressed or MomentaryLeaseState.Held)
        {
            lease = lease with { State = MomentaryLeaseState.Cancelled };
        }

        return Accept(
            _state with
            {
                Revision = _state.Revision + 1,
                Attempt = DefibrillationAttemptState.Failed,
                Energy = EnergyState.Failed,
                Request = ShockRequestState.None,
                Lease = lease,
            },
            new TherapyFact("DefibrillatorFailed", input.SafetyTimeNs, input.CorrelationId));
    }

    private DomainResult<TherapyTransition> Cancel(
        SafetyInput input,
        MomentaryLeaseState leaseState,
        DefibrillationAttemptState attemptState,
        string factType,
        bool requireInteractionMatch = false)
    {
        bool activeLease = _state.Lease?.State is MomentaryLeaseState.Pressed or MomentaryLeaseState.Held;
        bool activeEnergy = _state.Energy is EnergyState.Charging or EnergyState.Ready;
        if (requireInteractionMatch && activeLease && input.InteractionId != _state.Lease!.InteractionId)
        {
            return Reject("therapy.lease.interaction_stale", "interaction_id", input.CorrelationId);
        }

        if (!activeLease && !activeEnergy)
        {
            return AcceptWithoutChange();
        }

        MomentaryTherapyLease? lease = activeLease ? _state.Lease! with { State = leaseState } : _state.Lease;
        TherapyState state = _state with
        {
            Revision = _state.Revision + 1,
            Attempt = attemptState,
            Energy = EnergyState.Idle,
            Request = ShockRequestState.None,
            Lease = lease,
        };
        _state = state;
        TherapyFact[] facts =
        [
            new(factType, input.SafetyTimeNs, input.CorrelationId),
            new("DisarmRecorded", input.SafetyTimeNs, input.CorrelationId),
        ];
        return DomainResult.Accept(new TherapyTransition(state, facts));
    }

    private MomentaryTherapyLease? ActiveLease(SafetyInput input, out DomainRejection? rejection)
    {
        MomentaryTherapyLease? lease = _state.Lease;
        if (lease is null || lease.State is not (MomentaryLeaseState.Pressed or MomentaryLeaseState.Held))
        {
            rejection = NewRejection("therapy.lease.not_active", "lease.state", input.CorrelationId);
            return null;
        }

        if (lease.AuthorityEpoch != _state.AuthorityEpoch || lease.InstanceId != _instanceId)
        {
            rejection = NewRejection("therapy.lease.scope_stale", "lease.authority_epoch", input.CorrelationId);
            return null;
        }

        if (input.InteractionId != lease.InteractionId)
        {
            rejection = NewRejection("therapy.lease.interaction_stale", "interaction_id", input.CorrelationId);
            return null;
        }

        rejection = null;
        return lease;
    }

    private DomainResult<TherapyTransition> Accept(TherapyState state, params TherapyFact[] facts)
    {
        _state = state;
        return DomainResult.Accept(new TherapyTransition(state, facts));
    }

    private DomainResult<TherapyTransition> AcceptWithoutChange() =>
        DomainResult.Accept(new TherapyTransition(_state, []));

    private DomainResult<TherapyTransition> Reject(
        string reasonCode,
        string fieldPath,
        Guid correlationId) => DomainResult.Reject<TherapyTransition>(
            NewRejection(reasonCode, fieldPath, correlationId));

    private DomainRejection NewRejection(string reasonCode, string fieldPath, Guid correlationId) =>
        new(reasonCode, $"therapy:{_instanceId:D}", fieldPath, _state.Revision, false,
            $"rejection.{reasonCode}", correlationId);
}
