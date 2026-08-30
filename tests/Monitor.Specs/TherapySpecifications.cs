// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Authority;
using Monitor.Domain.Therapy;

namespace Monitor.Specs;

internal static class TherapySpecifications
{
    private static readonly Guid InteractionId =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    public static Specification[] All =>
    [
        new(nameof(AsynchronousHoldDeliversOnce), AsynchronousHoldDeliversOnce),
        new(nameof(SynchronizedHoldWaitsForQrs), SynchronizedHoldWaitsForQrs),
        new(nameof(CancellationBeatsSameTimeQrs), CancellationBeatsSameTimeQrs),
        new(nameof(StaleInteractionIsRejectedAtomically), StaleInteractionIsRejectedAtomically),
        new(nameof(EpochChangeCancelsLease), EpochChangeCancelsLease),
        new(nameof(RecoveryDisarmsAndCancelsLease), RecoveryDisarmsAndCancelsLease),
    ];

    private static void AsynchronousHoldDeliversOnce()
    {
        TherapyController controller = ReadyController(DefibrillationMode.ManualAsynchronous);
        Press(controller);
        SafetyInput held = Input(1_100, SafetyInputKind.HoldThresholdElapsed, 1);
        Check.That(controller.ApplySafetyInputs([held]).IsAccepted, "hold must be accepted");
        Check.That(controller.State.DeliveredCount == 1, "one shock must be delivered");
        Check.That(controller.State.Energy == EnergyState.Idle, "energy must return idle");

        Check.That(controller.ApplySafetyInputs([held with { LocalSequence = 2 }]).IsAccepted,
            "duplicate signal is harmless after delivery");
        Check.That(controller.State.DeliveredCount == 1, "delivery must be idempotent");
    }

    private static void SynchronizedHoldWaitsForQrs()
    {
        TherapyController controller = ReadyController(DefibrillationMode.ManualSynchronized);
        Press(controller);
        controller.ApplySafetyInputs([Input(1_100, SafetyInputKind.HoldThresholdElapsed, 1)]);
        Check.That(controller.State.Request == ShockRequestState.WaitingNextQrs, "must wait for QRS");
        Check.That(controller.State.DeliveredCount == 0, "hold must not deliver synchronized shock");

        controller.ApplySafetyInputs([Input(1_120, SafetyInputKind.ValidQrs, 2)]);
        Check.That(controller.State.DeliveredCount == 1, "qualified QRS must deliver once");
    }

    private static void CancellationBeatsSameTimeQrs()
    {
        TherapyController controller = ReadyController(DefibrillationMode.ManualSynchronized);
        Press(controller);
        controller.ApplySafetyInputs([Input(1_100, SafetyInputKind.HoldThresholdElapsed, 1)]);

        controller.ApplySafetyInputs(
        [
            Input(1_200, SafetyInputKind.ValidQrs, 3),
            Input(1_200, SafetyInputKind.ShockReleased, 2),
        ]);

        Check.That(controller.State.DeliveredCount == 0, "same-time release must beat QRS");
        Check.That(controller.State.Energy == EnergyState.Idle,
            "release must record disarm then return idle");
    }

    private static void StaleInteractionIsRejectedAtomically()
    {
        TherapyController controller = ReadyController(DefibrillationMode.ManualSynchronized);
        Press(controller);
        ulong revisionBefore = controller.State.Revision;
        var staleInteraction = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        SafetyInput release = Input(1_050, SafetyInputKind.ShockReleased, 1) with
        {
            InteractionId = staleInteraction,
        };
        Check.That(!controller.ApplySafetyInputs([release]).IsAccepted, "stale interaction must reject");
        Check.That(controller.State.Revision == revisionBefore, "rejected batch must not partially commit");
        Check.That(controller.State.Lease?.State == MomentaryLeaseState.Pressed,
            "active lease must survive");
    }

    private static void EpochChangeCancelsLease()
    {
        TherapyController controller = ReadyController(DefibrillationMode.ManualSynchronized);
        Press(controller);
        ulong oldEpoch = controller.State.AuthorityEpoch;
        SafetyInput epochChange = Input(1_050, SafetyInputKind.AuthorityEpochChanged, 1) with
        {
            AuthorityEpoch = oldEpoch + 1,
        };
        controller.ApplySafetyInputs([epochChange]);
        Check.That(controller.State.AuthorityEpoch == oldEpoch + 1, "epoch must advance");
        Check.That(controller.State.Lease?.State == MomentaryLeaseState.Cancelled, "lease must cancel");
    }

    private static void RecoveryDisarmsAndCancelsLease()
    {
        TherapyController controller = ReadyController(DefibrillationMode.ManualAsynchronous);
        Press(controller);
        controller.ApplySafetyInputs([Input(1_050, SafetyInputKind.AuthorityRecovered, 1)]);
        Check.That(controller.State.Energy == EnergyState.Idle, "recovery must disarm then return idle");
        Check.That(controller.State.DeliveredCount == 0,
            "recovery must never restore or deliver input");
    }

    private static TherapyController ReadyController(DefibrillationMode mode)
    {
        TherapyController controller = new(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), 7, mode);
        Check.That(controller.BeginCharge(
            0,
            Guid.Parse("10000000-0000-4000-8000-000000000001")).IsAccepted,
            "charge must start");
        Check.That(controller.MarkChargeReady(
            500,
            Guid.Parse("10000000-0000-4000-8000-000000000002")).IsAccepted,
            "charge must become ready");
        return controller;
    }

    private static void Press(TherapyController controller)
    {
        Check.That(controller.PressShock(
            InteractionId,
            "student-01",
            controller.State.AuthorityEpoch,
            1_000,
            100,
            2_000,
            Guid.Parse("10000000-0000-4000-8000-000000000003")).IsAccepted,
            "press must be accepted");
    }

    private static SafetyInput Input(long time, SafetyInputKind kind, ulong sequence) =>
        new(time, kind, $"input-{sequence:D4}", sequence,
            Guid.Parse($"20000000-0000-4000-8000-{sequence:D12}"), InteractionId);
}

