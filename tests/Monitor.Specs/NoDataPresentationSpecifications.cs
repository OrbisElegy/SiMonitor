// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Specs;

internal static class NoDataPresentationSpecifications
{
    private static readonly Guid HrSource =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Spo2Source =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static Specification[] All =>
    [
        new(nameof(AvailableDataPassesThroughWithoutSafetySubstitution),
            AvailableDataPassesThroughWithoutSafetySubstitution),
        new(nameof(NoDataProjectsOneAtomicSilentSafetyState),
            NoDataProjectsOneAtomicSilentSafetyState),
        new(nameof(StaleRetentionAndRecoveryExpireAtExactBoundary),
            StaleRetentionAndRecoveryExpireAtExactBoundary),
        new(nameof(VerifiedContinuationRestoresSourceDrivenPresentation),
            VerifiedContinuationRestoresSourceDrivenPresentation),
        new(nameof(InvalidPolicyCheckpointAndTimeFailClosed),
            InvalidPolicyCheckpointAndTimeFailClosed),
    ];

    private static void AvailableDataPassesThroughWithoutSafetySubstitution()
    {
        DataContinuityStateMachine continuity = StartContinuity();
        var presentation =
            NoDataPresentationStateMachine.Start(
                Policies(),
                continuity.CaptureState());

        NoDataSafetyProjection projection = presentation.CaptureProjection();

        Check.That(
            projection.DataAvailability == DataAvailability.Authoritative &&
            projection.LiveTrace == LiveTracePresentation.FollowSource &&
            projection.TraceMeaning == TraceSignalMeaning.FollowSource &&
            projection.LiveTraceClock == LiveTraceClock.SourcePlayhead &&
            projection.Numerics.All(static item =>
                item.Quality == NumericNoDataQuality.FollowSource &&
                item.ValuePresentation == NumericValuePresentation.FollowSource) &&
            projection.PatientAlarms == PatientAlarmSuspension.FollowSource &&
            projection.PhysiologyEvents == PhysiologyEventPresentation.FollowSource &&
            projection.PhysiologyAudio ==
                PhysiologyAudioPresentation.FollowAlarmPolicy,
            "available data must retain the authoritative source projections");
    }

    private static void NoDataProjectsOneAtomicSilentSafetyState()
    {
        DataContinuityStateMachine continuity = StartContinuity();
        var presentation =
            NoDataPresentationStateMachine.Start(
                Policies(),
                continuity.CaptureState());
        DataContinuityState noData = continuity.Disconnect(
            hasBufferedData: false,
            authorityMonotonicNs: 10);

        NoDataSafetyProjection projection = presentation.Synchronize(noData);
        NumericNoDataProjection hr = Numeric(projection, "HR-ECG");
        NumericNoDataProjection spo2 = Numeric(projection, "SpO2");

        Check.That(
            projection.LiveTrace == LiveTracePresentation.NoDataSweep &&
            projection.TraceMeaning == TraceSignalMeaning.SignalUnavailable &&
            projection.LiveTraceClock == LiveTraceClock.PresentationClock &&
            hr.Quality == NumericNoDataQuality.Stale &&
            hr.ValuePresentation == NumericValuePresentation.PreserveLastValue &&
            spo2.Quality == NumericNoDataQuality.Disconnected &&
            spo2.ValuePresentation == NumericValuePresentation.UnavailableMarker &&
            projection.PatientAlarms ==
                PatientAlarmSuspension.SuspendedUnknown &&
            projection.PhysiologyEvents == PhysiologyEventPresentation.Suppress &&
            projection.PhysiologyAudio == PhysiologyAudioPresentation.Silent &&
            !projection.ClearLiveTraceImmediately &&
            projection.PreservePinnedHistory &&
            projection.PreserveCalibrationGutter &&
            projection.ShowConnectivityExplanation,
            "NoData must suppress patient semantics while preserving or hiding numerics by policy");
    }

    private static void StaleRetentionAndRecoveryExpireAtExactBoundary()
    {
        DataContinuityStateMachine continuity = StartContinuity();
        DataContinuityState noData = continuity.Disconnect(false, 100);
        var presentation =
            NoDataPresentationStateMachine.Start(Policies(), noData);

        NumericNoDataProjection before = Numeric(
            presentation.Advance(2_000_000_099),
            "HR-ECG");
        NoDataPresentationState checkpoint = presentation.CaptureState();
        var restored = NoDataPresentationStateMachine.Restore(Policies(), checkpoint);
        Check.That(restored.CaptureState() == checkpoint &&
            Numeric(restored.CaptureProjection(), "HR-ECG") == before,
            "restoration preserves the exact stale projection and authority clock");
        NumericNoDataProjection atBoundary = Numeric(
            presentation.Advance(2_000_000_100),
            "HR-ECG");

        Check.That(
            Numeric(restored.Advance(2_000_000_100), "HR-ECG") == atBoundary &&
            restored.CaptureState() == presentation.CaptureState() &&
            before.Quality == NumericNoDataQuality.Stale &&
            before.ValuePresentation ==
                NumericValuePresentation.PreserveLastValue &&
            atBoundary.Quality == NumericNoDataQuality.Disconnected &&
            atBoundary.ValuePresentation ==
                NumericValuePresentation.UnavailableMarker,
            "resolved stale retention must expire exactly without synthesizing a zero value");
    }

    private static void VerifiedContinuationRestoresSourceDrivenPresentation()
    {
        DataContinuityStateMachine continuity = StartContinuity();
        DataContinuityState noData = continuity.Disconnect(false, 10);
        var presentation =
            NoDataPresentationStateMachine.Start(Policies(), noData);
        _ = presentation.Advance(20);

        DataContinuityState continued = continuity.RecordShadowVerification(
            matched: true,
            authorityMonotonicNs: 20);
        NoDataSafetyProjection projection = presentation.Synchronize(continued);

        Check.That(
            projection.DataAvailability == DataAvailability.LocalContinuation &&
            projection.LiveTrace == LiveTracePresentation.FollowSource &&
            projection.Numerics.All(static numeric =>
                numeric.Quality == NumericNoDataQuality.FollowSource) &&
            projection.PatientAlarms == PatientAlarmSuspension.FollowSource &&
            projection.PhysiologyEvents == PhysiologyEventPresentation.FollowSource &&
            projection.PhysiologyAudio ==
                PhysiologyAudioPresentation.FollowAlarmPolicy &&
            !projection.ShowConnectivityExplanation,
            "verified provisional continuation may restore source-driven patient projections");
    }

    private static void InvalidPolicyCheckpointAndTimeFailClosed()
    {
        DataContinuityStateMachine continuity = StartContinuity();
        var presentation =
            NoDataPresentationStateMachine.Start(
                Policies(),
                continuity.CaptureState());
        NoDataPresentationState before = presentation.CaptureState();

        NumericNoDataPolicy duplicate = new("HR-ECG", HrSource, 1);
        Check.That(
            Reason(() => NoDataPresentationStateMachine.Start(
                [duplicate, duplicate],
                continuity.CaptureState())) ==
                "NoDataPresentation.InvalidConfiguration",
            "duplicate measurement identities must reject");

        NoDataPresentationState corrupt = before with
        {
            PresentationAuthorityMonotonicNs = -1,
        };
        Check.That(
            Reason(() => NoDataPresentationStateMachine.Restore(
                Policies(),
                corrupt)) == "NoDataPresentation.InvalidCheckpoint",
            "a presentation checkpoint cannot predate continuity");
        Check.That(
            Reason(() => presentation.Advance(-1)) ==
                "NoDataPresentation.TimeReversed" &&
            presentation.CaptureState() == before,
            "a reversed presentation clock must reject without mutation");
    }

    private static IReadOnlyList<NumericNoDataPolicy> Policies() =>
    [
        new NumericNoDataPolicy("SpO2", Spo2Source, 0),
        new NumericNoDataPolicy("HR-ECG", HrSource, 2_000_000_000),
    ];

    private static NumericNoDataProjection Numeric(
        NoDataSafetyProjection projection,
        string parameterId) => projection.Numerics.Single(item =>
            StringComparer.Ordinal.Equals(item.ParameterId, parameterId));

    private static DataContinuityStateMachine StartContinuity() =>
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
        catch (NoDataPresentationException exception)
        {
            return exception.ReasonCode;
        }
    }
}
