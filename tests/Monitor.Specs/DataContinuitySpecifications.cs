// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Specs;

internal static class DataContinuitySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(VerifiedBufferHandsOffToProvisionalContinuation),
            VerifiedBufferHandsOffToProvisionalContinuation),
        new(nameof(UnverifiedOrRejectedShadowCannotContinue),
            UnverifiedOrRejectedShadowCannotContinue),
        new(nameof(ResolvedPolicyStopsAtItsExactBoundary),
            ResolvedPolicyStopsAtItsExactBoundary),
        new(nameof(RelockingRemainsProvisionalUntilExplicitCommit),
            RelockingRemainsProvisionalUntilExplicitCommit),
        new(nameof(DataContinuityCheckpointAndInvalidTransitionsFailClosed),
            DataContinuityCheckpointAndInvalidTransitionsFailClosed),
    ];

    private static void VerifiedBufferHandsOffToProvisionalContinuation()
    {
        DataContinuityStateMachine machine = Start();
        DataContinuityState suspect = machine.ObserveHealthyConnection(
            ConnectionState.Suspect,
            1_000_000_000);
        ConnectivityCriticalBannerProjection suspectBanner = machine.CaptureBanner();
        Check.That(suspect.DataAvailability == DataAvailability.Authoritative &&
            suspect.AuthorityState == AuthorityState.Authoritative &&
            suspectBanner.Visible &&
            suspectBanner.Message == ConnectivityBannerMessage.ConnectionSuspect &&
            suspectBanner.AudioPolicy == ConnectivityTechnicalAudioPolicy.Silent,
            "Suspect must keep authoritative data while showing a silent banner");

        DataContinuityState buffered = machine.Disconnect(
            hasBufferedData: true,
            2_000_000_000);
        Check.That(buffered.ConnectionState == ConnectionState.Disconnected &&
            buffered.DataAvailability == DataAvailability.Buffered &&
            buffered.AuthorityState == AuthorityState.Authoritative &&
            buffered.ShadowVerification == ShadowVerificationState.Pending &&
            machine.CaptureBanner().Message ==
                ConnectivityBannerMessage.PlayingVerifiedBuffer,
            "disconnect must consume verified Host blocks before local generation");

        DataContinuityState verified = machine.RecordShadowVerification(
            matched: true,
            2_500_000_000);
        Check.That(verified.DataAvailability == DataAvailability.Buffered &&
            verified.ShadowVerification == ShadowVerificationState.Verified,
            "shadow verification must not discard remaining authoritative blocks");

        DataContinuityState continued = machine.ExhaustAuthoritativeBuffer(
            3_000_000_000);
        Check.That(continued.DataAvailability == DataAvailability.LocalContinuation &&
            continued.AuthorityState == AuthorityState.Provisional &&
            continued.LocalContinuationStartedAtAuthorityMonotonicNs ==
                3_000_000_000 &&
            machine.CaptureBanner().Message ==
                ConnectivityBannerMessage.LocalContinuationProvisional,
            "only a verified shadow may cross the buffer tail as Provisional");
    }

    private static void UnverifiedOrRejectedShadowCannotContinue()
    {
        DataContinuityStateMachine pending = Start();
        _ = pending.Disconnect(hasBufferedData: true, 1);
        DataContinuityState waiting = pending.ExhaustAuthoritativeBuffer(2);
        Check.That(waiting.DataAvailability == DataAvailability.NoData &&
            waiting.ContinuationStopReason ==
                ContinuationStopReason.AwaitingShadowVerification,
            "buffer exhaustion cannot race ahead of shadow verification");
        Check.That(pending.RecordShadowVerification(matched: true, 3)
                .DataAvailability == DataAvailability.LocalContinuation,
            "a later complete overlap match may start continuation from NoData");

        DataContinuityStateMachine mismatched = Start();
        _ = mismatched.Disconnect(hasBufferedData: true, 10);
        DataContinuityState rejected = mismatched.RecordShadowVerification(
            matched: false,
            11);
        Check.That(rejected.DataAvailability == DataAvailability.Buffered &&
            rejected.AuthorityState == AuthorityState.Authoritative,
            "a mismatch must not invalidate already verified Host buffer content");
        DataContinuityState noData = mismatched.ExhaustAuthoritativeBuffer(12);
        Check.That(noData.DataAvailability == DataAvailability.NoData &&
            noData.AuthorityState == AuthorityState.Provisional &&
            noData.ContinuationStopReason == ContinuationStopReason.ShadowMismatch,
            "a rejected overlap must go to NoData instead of guessing from a seed");

        foreach (LocalContinuationFailure failure in
            Enum.GetValues<LocalContinuationFailure>())
        {
            DataContinuityStateMachine failed = Start();
            _ = failed.Disconnect(hasBufferedData: true, 20);
            DataContinuityState stillBuffered = failed.RecordContinuationFailure(
                failure,
                21);
            ContinuationStopReason expected = failure switch
            {
                LocalContinuationFailure.CapsuleInvalid =>
                    ContinuationStopReason.CapsuleInvalid,
                LocalContinuationFailure.ConsistencyMismatch =>
                    ContinuationStopReason.ConsistencyMismatch,
                LocalContinuationFailure.PerformanceInsufficient =>
                    ContinuationStopReason.PerformanceInsufficient,
                _ => throw new InvalidOperationException("failure enum is undefined"),
            };
            Check.That(stillBuffered.DataAvailability == DataAvailability.Buffered &&
                failed.ExhaustAuthoritativeBuffer(22).ContinuationStopReason == expected,
                "continuation failures must latch through verified buffer exhaustion");
        }
    }

    private static void ResolvedPolicyStopsAtItsExactBoundary()
    {
        var duration = DataContinuityStateMachine.Start(
            new LocalContinuationPolicy(
                LocalContinuationPolicyKind.Duration,
                2_000_000_000),
            0);
        _ = duration.Disconnect(hasBufferedData: false, 0);
        _ = duration.RecordShadowVerification(matched: true, 0);
        Check.That(duration.Advance(1_999_999_999).DataAvailability ==
                DataAvailability.LocalContinuation &&
            duration.Advance(2_000_000_000).DataAvailability ==
                DataAvailability.NoData &&
            duration.CaptureState().NoDataSinceAuthorityMonotonicNs ==
                2_000_000_000 &&
            duration.CaptureState().ContinuationStopReason ==
                ContinuationStopReason.DurationExpired,
            "Duration must expire on the authority monotonic boundary exactly");

        var scenario = DataContinuityStateMachine.Start(
            LocalContinuationPolicy.UntilScenarioEnd,
            0);
        _ = scenario.Disconnect(hasBufferedData: false, 0);
        _ = scenario.RecordShadowVerification(matched: true, 0);
        Check.That(scenario.Advance(9_000_000_000).DataAvailability ==
                DataAvailability.LocalContinuation &&
            scenario.RecordScenarioEnded(9_000_000_001).DataAvailability ==
                DataAvailability.NoData &&
            scenario.CaptureState().ContinuationStopReason ==
                ContinuationStopReason.ScenarioEnded,
            "UntilScenarioEnd must not invent a duration or cross scenario end");

        var disabled = DataContinuityStateMachine.Start(
            LocalContinuationPolicy.Disabled,
            0);
        _ = disabled.Disconnect(hasBufferedData: false, 0);
        DataContinuityState disabledState = disabled.RecordShadowVerification(
            matched: true,
            1);
        Check.That(disabledState.DataAvailability == DataAvailability.NoData &&
            disabledState.ContinuationStopReason ==
                ContinuationStopReason.PolicyDisabled,
            "a resolved Disabled policy must remain NoData even after a match");
    }

    private static void RelockingRemainsProvisionalUntilExplicitCommit()
    {
        DataContinuityStateMachine machine = Start();
        _ = machine.Disconnect(hasBufferedData: false, 1);
        _ = machine.RecordShadowVerification(matched: true, 2);
        DataContinuityState relocking = machine.BeginRelocking(
            hasVerifiedPreroll: true,
            3);
        Check.That(relocking.ConnectionState == ConnectionState.Relocking &&
            relocking.DataAvailability == DataAvailability.Buffered &&
            relocking.AuthorityState == AuthorityState.Provisional &&
            machine.CaptureBanner().Message == ConnectivityBannerMessage.Relocking,
            "verified preroll during Relocking cannot self-promote to authority");
        Check.That(Reason(() => machine.ObserveHealthyConnection(
                ConnectionState.Connected,
                4)) == "DataContinuity.InvalidTransition",
            "a connected callback cannot bypass the future-boundary commit");

        DataContinuityState committed = machine.CompleteRelocking(5);
        Check.That(committed.ConnectionState == ConnectionState.Connected &&
            committed.DataAvailability == DataAvailability.Authoritative &&
            committed.AuthorityState == AuthorityState.Authoritative &&
            !machine.CaptureBanner().Visible,
            "only explicit RecoveryCommit may restore authoritative presentation");

        _ = machine.Disconnect(hasBufferedData: true, 6);
        _ = machine.BeginRelocking(hasVerifiedPreroll: false, 7);
        DataContinuityState failedRelock = machine.Disconnect(
            hasBufferedData: true,
            8);
        Check.That(failedRelock.ConnectionState == ConnectionState.Disconnected &&
            failedRelock.DataAvailability == DataAvailability.Buffered &&
            failedRelock.ShadowVerification == ShadowVerificationState.Pending,
            "a relock transport loss must restart verification without going healthy");
    }

    private static void DataContinuityCheckpointAndInvalidTransitionsFailClosed()
    {
        DataContinuityStateMachine original = Start();
        _ = original.Disconnect(hasBufferedData: false, 1_000_000_000);
        DataContinuityState checkpoint = original.RecordShadowVerification(
            matched: true,
            2_000_000_000);
        var restored = DataContinuityStateMachine.Restore(
            checkpoint);
        Check.That(restored.CaptureState() == checkpoint &&
            restored.Advance(901_999_999_999).DataAvailability ==
                DataAvailability.LocalContinuation &&
            restored.Advance(902_000_000_000).DataAvailability ==
                DataAvailability.NoData,
            "checkpoint restore must preserve the exact continuation expiry");

        DataContinuityState corrupt = checkpoint with
        {
            AuthorityState = AuthorityState.Authoritative,
        };
        Check.That(Reason(() => DataContinuityStateMachine.Restore(corrupt)) ==
            "DataContinuity.InvalidCheckpoint",
            "a checkpoint cannot label local generation as authoritative");
        DataContinuityState falseReason = checkpoint with
        {
            DataAvailability = DataAvailability.NoData,
            LocalContinuationStartedAtAuthorityMonotonicNs = null,
            NoDataSinceAuthorityMonotonicNs = 2_000_000_000,
            ContinuationStopReason = ContinuationStopReason.ShadowMismatch,
        };
        Check.That(Reason(() => DataContinuityStateMachine.Restore(falseReason)) ==
            "DataContinuity.InvalidCheckpoint",
            "checkpoint stop reasons must agree with their verification state");
        Check.That(Reason(() => DataContinuityStateMachine.Start(
                new LocalContinuationPolicy(
                    LocalContinuationPolicyKind.Duration,
                    LocalContinuationPolicy.MaximumDurationNs + 1),
                0)) == "DataContinuity.InvalidPolicy",
            "resolved Duration must remain inside the frozen 0 to 1800 second range");

        DataContinuityState beforeReversal = original.CaptureState();
        Check.That(Reason(() => original.Advance(1_999_999_999)) ==
                "DataContinuity.TimeReversed" &&
            original.CaptureState() == beforeReversal,
            "authority monotonic reversal must reject without partial mutation");
    }

    private static DataContinuityStateMachine Start() =>
        DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration,
            0);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (DataContinuityException exception)
        {
            return exception.ReasonCode;
        }
    }
}
