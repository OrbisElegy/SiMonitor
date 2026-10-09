// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Authority;
using Monitor.Domain.Therapy;

namespace Monitor.Application.Therapy;

// The host supplies a monotonic safety clock, interaction IDs and acquired QRS times.
// Runtime energy and leases are deliberately absent from persisted preferences.
public sealed class ManualDefibrillator
{
    public const long HoldDurationNs = 500_000_000;
    public const long SyncTimeoutNs = 10_000_000_000;
    private readonly Guid _instanceId;
    private TherapyController _controller;
    private long _lastSafetyNs;
    private long _chargeStartedNs;
    private long _readyAtNs;
    private long _awaitingAfterSimTimeNs;
    private ulong _deliverySequence;
    public DefibrillatorConfiguration Configuration { get; }
    public TherapyState State => _controller.State;
    public int EnergyJoules { get; private set; }
    public int ChargeProgressPermille { get; private set; }

    public ManualDefibrillator(Guid instanceId, DefibrillatorConfiguration configuration, int energyJoules)
    {
        Configuration = configuration.Snapshot();
        EnergyJoules = Configuration.NearestEnergy(energyJoules);
        _instanceId = instanceId;
        _controller = new(instanceId, 1, DefibrillationMode.ManualAsynchronous);
    }

    public void SelectEnergy(int energyJoules, long safetyTimeNs)
    {
        if (!Configuration.EnergyStepsJoules.Contains(energyJoules))
        { throw new ArgumentOutOfRangeException(nameof(energyJoules)); }
        if (EnergyJoules == energyJoules) { return; }
        Disarm(safetyTimeNs);
        EnergyJoules = energyJoules;
    }

    public void SetSynchronized(bool enabled, long safetyTimeNs)
    {
        var mode = enabled ? DefibrillationMode.ManualSynchronized : DefibrillationMode.ManualAsynchronous;
        if (State.Mode == mode) { return; }
        Disarm(safetyTimeNs);
        _controller = new(_instanceId, 1, mode);
    }

    public bool BeginCharge(long safetyTimeNs)
    {
        AdvanceClock(safetyTimeNs);
        if (!_controller.BeginCharge(safetyTimeNs, _instanceId).IsAccepted) { return false; }
        _chargeStartedNs = safetyTimeNs;
        ChargeProgressPermille = 0;
        return true;
    }

    public bool PressShock(Guid interactionId, long safetyTimeNs)
    {
        AdvanceClock(safetyTimeNs);
        if (State.Energy == EnergyState.Ready && safetyTimeNs >= _readyAtNs + Configuration.AutoDisarmSeconds * 1_000_000_000L)
        { Disarm(safetyTimeNs); return false; }
        return _controller.PressShock(interactionId, "local-monitor", 1, safetyTimeNs,
            HoldDurationNs, checked(safetyTimeNs + SyncTimeoutNs), _instanceId).IsAccepted;
    }

    public void ReleaseShock(long safetyTimeNs)
    {
        AdvanceClock(safetyTimeNs);
        if (State.Lease?.State is MomentaryLeaseState.Pressed or MomentaryLeaseState.Held)
        { Input(SafetyInputKind.ShockReleased, safetyTimeNs); ChargeProgressPermille = 0; }
    }

    public void Disarm(long safetyTimeNs)
    {
        AdvanceClock(safetyTimeNs);
        Input(SafetyInputKind.EnergyDisarmed, safetyTimeNs);
        ChargeProgressPermille = 0;
    }

    public DeliveredElectricalShock? Tick(long safetyTimeNs, long simulationTimeNs, IEnumerable<long> qrsPeakTimesNs)
    {
        ArgumentNullException.ThrowIfNull(qrsPeakTimesNs);
        ArgumentOutOfRangeException.ThrowIfNegative(simulationTimeNs);
        AdvanceClock(safetyTimeNs);
        if (State.Energy == EnergyState.Charging)
        {
            long durationNs = Configuration.ChargeDurationMilliseconds * 1_000_000L;
            ChargeProgressPermille = (int)(Math.Min(durationNs, safetyTimeNs - _chargeStartedNs) * 1000 / durationNs);
            if (ChargeProgressPermille == 1000)
            {
                _controller.MarkChargeReady(safetyTimeNs, _instanceId);
                _readyAtNs = _chargeStartedNs + durationNs;
            }
        }
        if (State.Energy != EnergyState.Ready) { return null; }
        if (safetyTimeNs >= _readyAtNs + Configuration.AutoDisarmSeconds * 1_000_000_000L)
        { Disarm(safetyTimeNs); return null; }
        var lease = State.Lease;
        if (lease?.State is not (MomentaryLeaseState.Pressed or MomentaryLeaseState.Held)) { return null; }
        if (safetyTimeNs >= lease.ExpiresAtSafetyNs)
        { Input(SafetyInputKind.LeaseExpired, safetyTimeNs); ChargeProgressPermille = 0; return null; }
        if (lease.State == MomentaryLeaseState.Pressed && safetyTimeNs >= lease.PressedAtSafetyNs + HoldDurationNs)
        {
            Input(SafetyInputKind.HoldThresholdElapsed, safetyTimeNs);
            _awaitingAfterSimTimeNs = simulationTimeNs;
        }
        else if (State.Attempt == DefibrillationAttemptState.AwaitingSync &&
            qrsPeakTimesNs.Any(peak => peak > _awaitingAfterSimTimeNs && peak <= simulationTimeNs))
        { Input(SafetyInputKind.ValidQrs, safetyTimeNs); }
        if (State.Attempt != DefibrillationAttemptState.Delivered) { return null; }
        ChargeProgressPermille = 0;
        return new(++_deliverySequence, simulationTimeNs, Configuration.Waveform, State.Mode, EnergyJoules);
    }

    private void AdvanceClock(long safetyTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(safetyTimeNs, _lastSafetyNs);
        _lastSafetyNs = safetyTimeNs;
    }

    private void Input(SafetyInputKind kind, long safetyTimeNs)
    {
        var result = _controller.ApplySafetyInputs([new(safetyTimeNs, kind, "local-monitor", 0, _instanceId, State.Lease?.InteractionId)]);
        if (!result.IsAccepted) { throw new InvalidOperationException(result.Rejection!.ReasonCode); }
    }
}
