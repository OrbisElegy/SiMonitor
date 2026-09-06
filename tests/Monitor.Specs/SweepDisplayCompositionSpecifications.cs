// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepDisplayCompositionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(MissingFrameStillProjectsCurrentSafety), MissingFrameStillProjectsCurrentSafety),
        new(nameof(AuthorityAndPresentationClocksRemainIndependent), AuthorityAndPresentationClocksRemainIndependent),
        new(nameof(PinnedViewsPreserveHistoryAndExposeLiveDisconnection), PinnedViewsPreserveHistoryAndExposeLiveDisconnection),
        new(nameof(CompositionRestoreOwnsPoliciesAndPreservesPublication), CompositionRestoreOwnsPoliciesAndPreservesPublication),
    ];

    private static NumericNoDataPolicy Policy => new("HR", Guid.Parse("11111111-1111-4111-8111-111111111111"), 10);

    private static SweepStateProjectionStateMachine Start() => SweepStateProjectionStateMachine.Start(
        new("ecg", 4, 5, 0, 10, 2, 12), 1, 1, SessionRunState.Running,
        DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0);

    private static void Disconnect(SweepStateProjectionStateMachine machine) =>
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState)
            .Disconnect(false, 1), machine.CaptureState().LastPresentationNs, 0);

    private static SweepDisplaySnapshot Compose(SweepStateProjectionState state, long authorityNs = 1,
        PublishedSweepFrame? frame = null) => SweepDisplayComposition.Compose(state, authorityNs, [Policy], 0, 10, 0, 10, frame);

    private static PublishedSweepFrame Publish(SweepStateProjectionState state) => SweepFramePublication.Restore(1, 1,
        new(new(state, 0, 10, 0, 10, null), Array.Empty<SweepPathSample>())).CapturePublished()!;

    private static void MissingFrameStillProjectsCurrentSafety()
    {
        SweepStateProjectionStateMachine machine = Start();
        PublishedSweepFrame old = Publish(machine.CaptureState());
        Disconnect(machine);
        machine.Advance(5, 0);
        foreach (PublishedSweepFrame? candidate in new[] { null, old })
        {
            SweepDisplaySnapshot result = Compose(machine.CaptureState(), frame: candidate);
            Check.That(result.SourceFrame.Frame is null && result.Connectivity.Visible &&
                result.Connectivity.Message == ConnectivityBannerMessage.NoData &&
                result.LiveSafety.PatientAlarms == PatientAlarmSuspension.SuspendedUnknown &&
                result.LiveSafety.PhysiologyAudio == PhysiologyAudioPresentation.Silent,
                "missing or stale source frames cannot suppress current disconnect safety");
            Check.That(result.CurrentRegions.Regions.Any(region => region.Kind == SweepTraceRegionKind.NoDataBaseline) &&
                result.CurrentRegions.Regions.Any(region => region.Kind == SweepTraceRegionKind.RetainSourceTrace) &&
                !result.LiveSafety.ClearLiveTraceImmediately && result.LiveSafety.PreserveCalibrationGutter,
                "partial NoData coverage remains explicit without authorizing full clearing");
        }
    }

    private static void AuthorityAndPresentationClocksRemainIndependent()
    {
        SweepStateProjectionStateMachine machine = Start();
        Disconnect(machine);
        SweepDisplaySnapshot before = Compose(machine.CaptureState(), 10);
        SweepDisplaySnapshot expiry = Compose(machine.CaptureState(), 11);
        Check.That(before.LiveSafety.Numerics[0].ValuePresentation == NumericValuePresentation.PreserveLastValue &&
            expiry.LiveSafety.Numerics[0].ValuePresentation == NumericValuePresentation.UnavailableMarker &&
            before.CurrentRegions.Regions.SequenceEqual(expiry.CurrentRegions.Regions),
            "authority-time retention expires exactly without advancing the scan");
        machine.Advance(10, 0);
        SweepDisplaySnapshot swept = Compose(machine.CaptureState(), 10);
        Check.That(swept.LiveSafety.Numerics[0] == before.LiveSafety.Numerics[0] &&
            swept.CurrentRegions.Regions.All(region => region.Kind != SweepTraceRegionKind.RetainSourceTrace),
            "one presentation cycle erases retained Live regions without changing numeric age");
    }

    private static void PinnedViewsPreserveHistoryAndExposeLiveDisconnection()
    {
        foreach (bool review in new[] { false, true })
        {
            SweepStateProjectionStateMachine machine = Start();
            if (review) { machine.EnterReview("record.one", 17, 0, 0); }
            else { machine.EnterFrozen(0, 0); }
            Disconnect(machine);
            PublishedSweepFrame pinned = Publish(machine.CaptureState());
            machine.Advance(20, 0);
            SweepDisplaySnapshot result = Compose(machine.CaptureState(), 11, pinned);
            Check.That(result.SourceFrame.Frame == pinned.Frame && result.Connectivity.Visible &&
                result.CurrentRegions.Regions.Single().Kind == SweepTraceRegionKind.PinnedHistory &&
                result.LiveSafety.PreservePinnedHistory &&
                result.Presentation.PlayheadDataSimTimeNs == (review ? 17 : 0),
                "pinned original range coexists with current Live NoData status");
            Check.That(!review || result.Presentation.TransientReplayPolicy == TransientReplayPolicy.Suppress,
                "historical replay remains suppressed independently of background patient state");
        }
    }

    private static void CompositionRestoreOwnsPoliciesAndPreservesPublication()
    {
        SweepStateProjectionStateMachine machine = Start();
        Disconnect(machine);
        SweepStateProjectionState checkpoint = machine.CaptureState();
        PublishedSweepFrame frame = Publish(checkpoint);
        NumericNoDataPolicy[] policies = [Policy];
        SweepDisplaySnapshot first = SweepDisplayComposition.Compose(checkpoint, 10, policies, 0, 10, 0, 10, frame);
        policies[0] = Policy with { StaleRetentionNs = 0 };
        SweepDisplaySnapshot restored = Compose(SweepStateProjectionStateMachine.Restore(checkpoint).CaptureState(), 10,
            Publish(frame.Checkpoint.Frame.Presentation));
        Check.That(first.LiveSafety.Numerics.SequenceEqual(restored.LiveSafety.Numerics) &&
            first.CurrentRegions.Regions.SequenceEqual(restored.CurrentRegions.Regions) &&
            first.SourceFrame.ReasonCode == restored.SourceFrame.ReasonCode,
            "recomposition after restore is deterministic and caller policy mutation cannot alter output");
        try
        {
            _ = Compose(checkpoint, 0, frame);
            throw new InvalidOperationException("reversed authority clock accepted");
        }
        catch (NoDataPresentationException exception)
        {
            Check.That(exception.ReasonCode == "NoDataPresentation.TimeReversed", "clock validation has a stable reason");
        }
        Check.That(machine.CaptureState() == checkpoint && ReferenceEquals(Compose(checkpoint, 10, frame).SourceFrame.Frame, frame.Frame),
            "failed composition leaves both patient checkpoint and completed frame intact");
    }
}
