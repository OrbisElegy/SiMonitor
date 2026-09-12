// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformRecordArchiveSpecifications
{
    private static readonly Guid SessionId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid[] ChannelIds =
    [
        Guid.Parse("30000000-0000-4000-8000-000000000001"),
        Guid.Parse("30000000-0000-4000-8000-000000000002"),
        Guid.Parse("30000000-0000-4000-8000-000000000003"),
        Guid.Parse("30000000-0000-4000-8000-000000000004"),
        Guid.Parse("30000000-0000-4000-8000-000000000005"),
        Guid.Parse("30000000-0000-4000-8000-000000000006"),
        Guid.Parse("30000000-0000-4000-8000-000000000007"),
        Guid.Parse("30000000-0000-4000-8000-000000000008"),
        Guid.Parse("30000000-0000-4000-8000-000000000009"),
        Guid.Parse("30000000-0000-4000-8000-00000000000a"),
        Guid.Parse("30000000-0000-4000-8000-00000000000b"),
        Guid.Parse("30000000-0000-4000-8000-00000000000c"),
    ];

    public static Specification[] All =>
    [
        new(nameof(NavigatedDisplayUsesCurrentPage), NavigatedDisplayUsesCurrentPage),
        new(nameof(NavigatedDisplayRejectsForeignRecord), NavigatedDisplayRejectsForeignRecord),
        new(nameof(NavigatedDisplaySuppressesDeniedPage), NavigatedDisplaySuppressesDeniedPage),
        new(nameof(NavigatedDisplayRebindsRestoredNavigation), NavigatedDisplayRebindsRestoredNavigation),
        new(nameof(RecordNavigationMovesWithoutChangingMeasurements), RecordNavigationMovesWithoutChangingMeasurements),
        new(nameof(RecordNavigationBoundariesPreservePage), RecordNavigationBoundariesPreservePage),
        new(nameof(RecordNavigationGatesEveryCommand), RecordNavigationGatesEveryCommand),
        new(nameof(RecordNavigationRestoresWithCurrentPolicy), RecordNavigationRestoresWithCurrentPolicy),
        new(nameof(RecordPagesPartitionWithoutGapsOrEmptyTail), RecordPagesPartitionWithoutGapsOrEmptyTail),
        new(nameof(RecordPagesRejectInvalidRequestsWithoutMutation), RecordPagesRejectInvalidRequestsWithoutMutation),
        new(nameof(RecordPagesPreserveCursorEvidenceAcrossDisplay), RecordPagesPreserveCursorEvidenceAcrossDisplay),
        new(nameof(RecordPagesRestoreAndHandleExtremeDurations), RecordPagesRestoreAndHandleExtremeDurations),
        new(nameof(StudySlotSelectionStartsWithIndependentCursors), StudySlotSelectionStartsWithIndependentCursors),
        new(nameof(StudySlotSelectionPreservesCurrentPolicy), StudySlotSelectionPreservesCurrentPolicy),
        new(nameof(StudySlotSelectionFailureAndReselectionPreserveState), StudySlotSelectionFailureAndReselectionPreserveState),
        new(nameof(StudySlotSelectionIsolatesOldGesturesAndRestoresValues), StudySlotSelectionIsolatesOldGesturesAndRestoresValues),
        new(nameof(StudyViewSuppressesContentWhenOverlayIsLost), StudyViewSuppressesContentWhenOverlayIsLost),
        new(nameof(StudyViewSeparatesAdmissionAndMeasurementPolicy), StudyViewSeparatesAdmissionAndMeasurementPolicy),
        new(nameof(StudyViewCompositionFailurePreservesRecordAndSelection), StudyViewCompositionFailurePreservesRecordAndSelection),
        new(nameof(StudyViewRestoredRecordRequiresCurrentAdmission), StudyViewRestoredRecordRequiresCurrentAdmission),
        new(nameof(MeasurementPointPairCreatesAndRestoresSelection), MeasurementPointPairCreatesAndRestoresSelection),
        new(nameof(MeasurementPointPairFailurePreservesSelection), MeasurementPointPairFailurePreservesSelection),
        new(nameof(MeasurementPointPairPreservesOrderAndEqualTime), MeasurementPointPairPreservesOrderAndEqualTime),
        new(nameof(MeasurementPointPairEnforcesCurrentPolicy), MeasurementPointPairEnforcesCurrentPolicy),
        new(nameof(MeasurementDragLayoutChangeLatchesUntilCancel), MeasurementDragLayoutChangeLatchesUntilCancel),
        new(nameof(MeasurementDragChecksLayoutAtCommit), MeasurementDragChecksLayoutAtCommit),
        new(nameof(MeasurementDragAcceptsEquivalentGainRatios), MeasurementDragAcceptsEquivalentGainRatios),
        new(nameof(MeasurementDragStartRequiresVisibleValidatedPage), MeasurementDragStartRequiresVisibleValidatedPage),
        new(nameof(MeasurementDragCancelRestoresInitialValues), MeasurementDragCancelRestoresInitialValues),
        new(nameof(MeasurementDragCommitClosesGesture), MeasurementDragCommitClosesGesture),
        new(nameof(MeasurementOldGestureCannotOverwriteReplacement), MeasurementOldGestureCannotOverwriteReplacement),
        new(nameof(MeasurementGestureFailurePreservesPreview), MeasurementGestureFailurePreservesPreview),
        new(nameof(MeasurementDisplayCombinesPagePositionsAndResults), MeasurementDisplayCombinesPagePositionsAndResults),
        new(nameof(MeasurementDisplaySuppressesLockedAndClearedResults), MeasurementDisplaySuppressesLockedAndClearedResults),
        new(nameof(MeasurementDisplayFailurePreservesSelection), MeasurementDisplayFailurePreservesSelection),
        new(nameof(MeasurementDisplayRebuildsAfterRestore), MeasurementDisplayRebuildsAfterRestore),
        new(nameof(MeasurementClearRemovesActivePairAndAllowsReplacement), MeasurementClearRemovesActivePairAndAllowsReplacement),
        new(nameof(MeasurementClearObeysPolicyAndPreservesCheckpoints), MeasurementClearObeysPolicyAndPreservesCheckpoints),
        new(nameof(MeasurementPairReplacementIsAtomic), MeasurementPairReplacementIsAtomic),
        new(nameof(MeasurementDragCommitsOnlyOrderedPairs), MeasurementDragCommitsOnlyOrderedPairs),
        new(nameof(MeasurementDragRejectsMissingInvalidAndLockedEntry), MeasurementDragRejectsMissingInvalidAndLockedEntry),
        new(nameof(MeasurementActivePairRestoresForFurtherEditing), MeasurementActivePairRestoresForFurtherEditing),
        new(nameof(MeasurementPointerMapsPageTimeAndVoltage), MeasurementPointerMapsPageTimeAndVoltage),
        new(nameof(MeasurementPointerRejectsEdgesAndUnrepresentableValues), MeasurementPointerRejectsEdgesAndUnrepresentableValues),
        new(nameof(MeasurementPointerRoundTripsFractionalEvidence), MeasurementPointerRoundTripsFractionalEvidence),
        new(nameof(MeasurementPointerPolicyAndFailurePreserveExistingPair), MeasurementPointerPolicyAndFailurePreserveExistingPair),
        new(nameof(MeasurementPagesOwnSharedBoundaryOnce), MeasurementPagesOwnSharedBoundaryOnce),
        new(nameof(MeasurementPageZoomPreservesDataValues), MeasurementPageZoomPreservesDataValues),
        new(nameof(MeasurementPagesValidateEvenHiddenCursors), MeasurementPagesValidateEvenHiddenCursors),
        new(nameof(MeasurementPagesReprojectRestoredValues), MeasurementPagesReprojectRestoredValues),
        new(nameof(MeasurementProjectionPreservesValuesAcrossResize), MeasurementProjectionPreservesValuesAcrossResize),
        new(nameof(MeasurementProjectionRetainsFractionalAndOutsideCoordinates), MeasurementProjectionRetainsFractionalAndOutsideCoordinates),
        new(nameof(MeasurementProjectionEnforcesPolicyOwnershipAndBounds), MeasurementProjectionEnforcesPolicyOwnershipAndBounds),
        new(nameof(MeasurementProjectionRestoresFromDataEvidence), MeasurementProjectionRestoresFromDataEvidence),
        new(nameof(MeasurementCheckpointReissuesOwnedCursorPair), MeasurementCheckpointReissuesOwnedCursorPair),
        new(nameof(MeasurementCheckpointRejectsTamperedValues), MeasurementCheckpointRejectsTamperedValues),
        new(nameof(MeasurementCheckpointUsesCurrentCoursePolicy), MeasurementCheckpointUsesCurrentCoursePolicy),
        new(nameof(MeasurementCheckpointRejectsForeignCaptureAndOwnsRecord), MeasurementCheckpointRejectsForeignCaptureAndOwnsRecord),
        new(nameof(MeasurementSlotResolvesExplicitChannelMapping), MeasurementSlotResolvesExplicitChannelMapping),
        new(nameof(MeasurementSlotRejectsUnknownIdentity), MeasurementSlotRejectsUnknownIdentity),
        new(nameof(MeasurementSlotPreventsCrossLeadCursorMixing), MeasurementSlotPreventsCrossLeadCursorMixing),
        new(nameof(MeasurementSlotSurvivesInputMutationAndRecordRestore), MeasurementSlotSurvivesInputMutationAndRecordRestore),
        new(nameof(MeasurementPolicyRejectsDisabledAndLockedEntry), MeasurementPolicyRejectsDisabledAndLockedEntry),
        new(nameof(MeasurementPolicyRevokesExistingCursorCalculation), MeasurementPolicyRevokesExistingCursorCalculation),
        new(nameof(InvalidMeasurementPolicyPreservesAcceptedState), InvalidMeasurementPolicyPreservesAcceptedState),
        new(nameof(RestoredMeasurementUsesExplicitCurrentPolicy), RestoredMeasurementUsesExplicitCurrentPolicy),
        new(nameof(RecordMeasurementUsesBoundCursorValues), RecordMeasurementUsesBoundCursorValues),
        new(nameof(RecordMeasurementEnforcesHalfOpenRange), RecordMeasurementEnforcesHalfOpenRange),
        new(nameof(RecordMeasurementRejectsForeignCursorsWithoutMutation), RecordMeasurementRejectsForeignCursorsWithoutMutation),
        new(nameof(RestoredRecordMeasurementRequiresFreshCursors), RestoredRecordMeasurementRequiresFreshCursors),
        new(nameof(CompletedRecordBindsExplicitSlots), CompletedRecordBindsExplicitSlots),
        new(nameof(RecordBindingRejectsIncompleteAndMismatchedInputs), RecordBindingRejectsIncompleteAndMismatchedInputs),
        new(nameof(RecordBindingCheckpointIsDefensive), RecordBindingCheckpointIsDefensive),
        new(nameof(StandardRecordArchivesFiftySharedBlocks),
            StandardRecordArchivesFiftySharedBlocks),
        new(nameof(ArchivedRecordSurvivesLiveRingEviction),
            ArchivedRecordSurvivesLiveRingEviction),
        new(nameof(UnalignedRangeUsesOnlyItsMinimalCover),
            UnalignedRangeUsesOnlyItsMinimalCover),
        new(nameof(IdentityConfigurationAndChannelSetFailClosed),
            IdentityConfigurationAndChannelSetFailClosed),
        new(nameof(EveryLeadKeepsOneSampleGridAndScale),
            EveryLeadKeepsOneSampleGridAndScale),
        new(nameof(DiscontinuousBlocksCannotFormARecord),
            DiscontinuousBlocksCannotFormARecord),
        new(nameof(ArchiveCheckpointAndReadsAreDefensive),
            ArchiveCheckpointAndReadsAreDefensive),
    ];

    private static CapturedRecordBinding MeasurementRecord() => CapturedRecordBinding.Create(
        BindingPresentation().CaptureState(), BindingArchive(), BindingSlots());

    private static void NavigatedDisplayUsesCurrentPage()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(50_000_000, 0, 1), new(100_000_000, 1000, 1));
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordPageDisplay first = view.CapturePageDisplay(navigation, false, 0, 100, scale, true);
        navigation.NextPage();
        navigation.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordPageDisplay second = view.CapturePageDisplay(navigation, false, 0, 100, scale, true);
        Check.That(first.Page!.PageIndex == 0 && second.Page!.PageIndex == 1 && second.Viewport!.StartDataTimeNs == 100_000_000 &&
            first.Study.Measurement!.First is not null && first.Study.Measurement.Second is null &&
            second.Study.Measurement!.First is null && second.Study.Measurement.Second is not null &&
            first.Study.Measurement.Measurement == second.Study.Measurement.Measurement && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "current navigation page determines visibility without changing evidence, including locked page display");
    }

    private static void NavigatedDisplayRejectsForeignRecord()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectPaginationReason(() => view.CapturePageDisplay(foreign, false, 0, 100, new(0, 100, 60, 20, 1), true),
            "RecordPagination.ForeignNavigation");
        Check.That(view.Measurement.CurrentPair is null && foreign.CurrentPage.PageIndex == 0,
            "even equivalent record bytes require explicit shared binding and rejection changes neither state");
    }

    private static void NavigatedDisplaySuppressesDeniedPage()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordPageDisplay denied = view.CapturePageDisplay(navigation, false, 0, 0, new(0, 100, 60, 20, 1), true);
        Check.That(!denied.Study.Admission.MayEnter && denied.Page is null && denied.Viewport is null && denied.Study.Record is null,
            "denied admission suppresses page metadata without depending on a usable layout");
        try
        {
            view.CapturePageDisplay(navigation, true, 0, 0, new(0, 100, 60, 20, 1), true);
            throw new InvalidOperationException("invalid empty-display layout accepted");
        }
        catch (SweepPlotGeometryException exception)
        { Check.That(exception.ReasonCode == "SweepGeometry.InvalidPlotBounds", "empty or disabled calipers do not bypass page layout validation"); }
        CapturedRecordPageDisplay recovered = view.CapturePageDisplay(navigation, true, 0, 100, new(0, 100, 60, 20, 1), true);
        Check.That(recovered.Page!.PageIndex == 0 && recovered.Study.Measurement!.ReasonCode == "RecordMeasurement.Disabled",
            "valid layout recovers without navigating and keeps caliper policy distinct");
    }

    private static void NavigatedDisplayRebindsRestoredNavigation()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 75_000_000, 2, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation restored = CapturedRecordNavigation.Restore(navigation.CaptureState(), SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordStudyView view = restored.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordPageDisplay display = view.CapturePageDisplay(restored, false, 10, 100, new(0, 100, 60, 20, 1), false);
        Check.That(display.Page == navigation.CurrentPage && display.Viewport == new RecordCursorViewport(150_000_000, 200_000_000, 10, 100) &&
            display.Study.MeasurementSlot!.SlotId == "ecg.slot1", "restored navigation creates a view bound to its revalidated record");
        ExpectPaginationReason(() => view.CapturePageDisplay(navigation, false, 10, 100, new(0, 100, 60, 20, 1), false),
            "RecordPagination.ForeignNavigation");
    }

    private static void RecordNavigationMovesWithoutChangingMeasurements()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordNavigation navigation = view.CreateNavigation(75_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(navigation.NextPage().PageIndex == 1 && navigation.NextPage().EndExclusiveDataTimeNs == 200_000_000 &&
            navigation.PreviousPage().PageIndex == 1 && navigation.SelectPage(0).PageIndex == 0,
            "next, previous and explicit page selection share one bounded record");
        Check.That(view.Measurement.CurrentPolicy == SystemViewCommandAssessmentPolicy.CourseLocked && view.Measurement.CurrentPair is null,
            "pagination policy is independent of caliper policy");
    }

    private static void RecordNavigationBoundariesPreservePage()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordPage first = navigation.CurrentPage;
        ExpectPaginationReason(() => navigation.PreviousPage(), "RecordPagination.NoPreviousPage");
        ExpectPaginationReason(() => navigation.SelectPage(ulong.MaxValue), "RecordPagination.PageOutsideRecord");
        Check.That(ReferenceEquals(navigation.CurrentPage, first), "boundary and invalid page failures preserve the current page");
        CapturedRecordPage last = navigation.NextPage();
        ExpectPaginationReason(() => navigation.NextPage(), "RecordPagination.NoNextPage");
        Check.That(ReferenceEquals(navigation.CurrentPage, last) && navigation.PreviousPage() == first,
            "last page never wraps and navigation recovers after rejection");
    }

    private static void RecordNavigationGatesEveryCommand()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 75_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordPage initial = navigation.CurrentPage;
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            navigation.UpdatePolicy(policy);
            ExpectPaginationReason(() => navigation.NextPage(), $"RecordPagination.{policy}");
            ExpectPaginationReason(() => navigation.PreviousPage(), $"RecordPagination.{policy}");
            ExpectPaginationReason(() => navigation.SelectPage(1), $"RecordPagination.{policy}");
            ExpectPaginationReason(() => navigation.UpdatePolicy((SystemViewCommandAssessmentPolicy)99), "RecordPagination.InvalidPolicy");
            Check.That(navigation.CurrentPolicy == policy && ReferenceEquals(navigation.CurrentPage, initial),
                "all entry paths including same-page selection respect current policy and invalid policy cannot unlock");
        }
        navigation.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(navigation.NextPage().PageIndex == 2, "explicit enabled policy allows subsequent navigation");
    }

    private static void RecordNavigationRestoresWithCurrentPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 75_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        navigation.SelectPage(2);
        CapturedRecordNavigationState checkpoint = navigation.CaptureState();
        CapturedRecordNavigation restored = CapturedRecordNavigation.Restore(checkpoint, SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(restored.CurrentPage == navigation.CurrentPage, "restore retains the validated selected page");
        ExpectPaginationReason(() => restored.PreviousPage(), "RecordPagination.CourseLocked");
        ExpectPaginationReason(() => CapturedRecordNavigation.Restore(checkpoint with { PageIndex = 3 }, SystemViewCommandAssessmentPolicy.Enabled),
            "RecordPagination.InvalidCheckpoint");
        ExpectPaginationReason(() => CapturedRecordNavigation.Restore(checkpoint with { PageDurationNs = 0 }, SystemViewCommandAssessmentPolicy.Enabled),
            "RecordPagination.InvalidCheckpoint");
        Check.That(restored.CurrentPage.PageIndex == 2, "invalid restoration cannot mutate the accepted navigation instance");
    }

    private static void RecordPagesPartitionWithoutGapsOrEmptyTail()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordPage first = CapturedRecordPagination.Resolve(record, 75_000_000, 0);
        CapturedRecordPage second = CapturedRecordPagination.Resolve(record, 75_000_000, 1);
        CapturedRecordPage last = CapturedRecordPagination.Resolve(record, 75_000_000, 2);
        Check.That(first == new CapturedRecordPage(0, 3, 0, 75_000_000) &&
            second == new CapturedRecordPage(1, 3, 75_000_000, 150_000_000) &&
            last == new CapturedRecordPage(2, 3, 150_000_000, 200_000_000), "half-open pages exactly partition record with a short final page");
        Check.That(CapturedRecordPagination.Resolve(record, 100_000_000, 1) == new CapturedRecordPage(1, 2, 100_000_000, 200_000_000),
            "exactly divisible duration creates no empty extra page");
    }

    private static void RecordPagesRejectInvalidRequestsWithoutMutation()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        foreach (long invalid in new[] { 0L, -1L, long.MinValue })
        { ExpectPaginationReason(() => view.ResolvePage(invalid, 0), "RecordPagination.InvalidPageDuration"); }
        ExpectPaginationReason(() => view.ResolvePage(100_000_000, 2), "RecordPagination.PageOutsideRecord");
        ExpectPaginationReason(() => view.ResolvePage(1, ulong.MaxValue), "RecordPagination.PageOutsideRecord");
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair) && view.ResolvePage(100_000_000, 0).PageCount == 2,
            "failed page queries neither edit measurement nor poison subsequent queries");
    }

    private static void RecordPagesPreserveCursorEvidenceAcrossDisplay()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(50_000_000, 0, 1), new(100_000_000, 1000, 1));
        CapturedRecordPage first = view.ResolvePage(100_000_000, 0), second = view.ResolvePage(100_000_000, 1);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordStudyDisplay a = view.CaptureDisplay(false, new(first.StartDataTimeNs, first.EndExclusiveDataTimeNs, 0, 100), scale, true);
        CapturedRecordStudyDisplay b = view.CaptureDisplay(false, new(second.StartDataTimeNs, second.EndExclusiveDataTimeNs, 0, 100), scale, true);
        Check.That(a.Measurement!.First is not null && a.Measurement.Second is null && b.Measurement!.First is null && b.Measurement.Second is not null &&
            a.Measurement.Measurement == b.Measurement.Measurement && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "shared page boundary owns cursor once while elapsed time and voltage remain unchanged");
    }

    private static void RecordPagesRestoreAndHandleExtremeDurations()
    {
        CapturedRecordBinding original = MeasurementRecord();
        CapturedRecordBinding restored = CapturedRecordBinding.Restore(original.CaptureState());
        Check.That(CapturedRecordPagination.Resolve(restored, 75_000_000, 2) == CapturedRecordPagination.Resolve(original, 75_000_000, 2),
            "restored verified record produces the same page ranges");
        Check.That(CapturedRecordPagination.Resolve(restored, long.MaxValue, 0) == new CapturedRecordPage(0, 1, 0, 200_000_000) &&
            CapturedRecordPagination.Resolve(restored, 1, 199_999_999) == new CapturedRecordPage(199_999_999, 200_000_000, 199_999_999, 200_000_000),
            "oversized and one-nanosecond pages use bounded arithmetic without materializing page collections");
    }

    private static void ExpectPaginationReason(Action action, string expected)
    {
        try { action(); }
        catch (CapturedRecordPaginationException exception)
        {
            Check.That(exception.ReasonCode == expected, "pagination rejects with stable reason");
            return;
        }
        throw new InvalidOperationException("invalid pagination request accepted");
    }

    private static void StudySlotSelectionStartsWithIndependentCursors()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair old = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        view.SelectMeasurementSlot("ecg.slot1");
        CapturedRecordStudyDisplay display = view.CaptureDisplay(true, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1), true);
        Check.That(view.Measurement.CurrentPair is null && display.MeasurementSlot == new RecordSlotBinding("ecg.slot1", ChannelIds[10]) &&
            display.Measurement!.ReasonCode == "RecordMeasurement.NoCursorPair", "selection resolves verified mapping and never transfers another lead's voltage");
        Check.That(MeasurementReason(() => view.Measurement.Calculate(old.First, old.Second, true)) == "RecordMeasurement.ForeignCursor",
            "old lead handles cannot measure the selected lead");
        Check.That(view.CaptureDisplay(false, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1), true).MeasurementSlot is null,
            "denied admission also suppresses selected slot metadata");
    }

    private static void StudySlotSelectionPreservesCurrentPolicy()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Disabled })
        {
            view.Measurement.UpdatePolicy(policy);
            view.SelectMeasurementSlot(view.Measurement.Slot.SlotId == "ecg.slot0" ? "ecg.slot1" : "ecg.slot0");
            Check.That(view.Measurement.CurrentPolicy == policy && MeasurementReason(() => view.Measurement.ReplacePair(new(0, 0, 1), new(1, 0, 1))) ==
                $"RecordMeasurement.{policy}", "switching leads does not re-enable course-locked or disabled calipers");
        }
    }

    private static void StudySlotSelectionFailureAndReselectionPreserveState()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordMeasurement initial = view.Measurement;
        CapturedRecordCursorPair pair = initial.ReplacePair(new(0, 0, 1), new(100_000_000, 0, 1));
        view.SelectMeasurementSlot("ecg.slot0");
        Check.That(ReferenceEquals(view.Measurement, initial) && ReferenceEquals(view.Measurement.CurrentPair, pair), "same-slot selection preserves gestures and values");
        Check.That(MeasurementReason(() => view.SelectMeasurementSlot("ECG.slot1")) == "RecordMeasurement.UnknownSlot" &&
            ReferenceEquals(view.Measurement, initial) && ReferenceEquals(view.Measurement.CurrentPair, pair), "invalid exact slot leaves selection unchanged");
    }

    private static void StudySlotSelectionIsolatesOldGesturesAndRestoresValues()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 0, 1));
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordDrag oldDrag = new(view.Measurement, RecordCursorEnd.Second, page, scale);
        view.SelectMeasurementSlot("ecg.slot1");
        CapturedRecordCursorPair selected = view.Measurement.ReplacePair(new(0, 0, 1), new(150_000_000, 500, 1));
        oldDrag.Preview(new(75, 1), new(40, 1), page, scale);
        oldDrag.Cancel();
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, selected), "detached old gesture cannot overwrite current lead");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Measurement.Slot == view.Measurement.Slot && restored.Second.Value == selected.Second.Value,
            "selected lead checkpoint retains its own channel and cursor values");
        view.SelectMeasurementSlot("ecg.slot0");
        Check.That(view.Measurement.CurrentPair is null, "returning to a lead does not resurrect detached selections");
    }

    private static void StudyViewSuppressesContentWhenOverlayIsLost()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordStudyDisplay initial = view.CaptureDisplay(true, page, scale, true);
        CapturedRecordStudyDisplay denied = view.CaptureDisplay(false, page, scale, true);
        Check.That(initial.Admission.MayEnter && ReferenceEquals(initial.Record, record) && initial.Measurement!.Measurement is not null &&
            !denied.Admission.MayEnter && denied.Record is null && denied.Measurement is null,
            "lost overlay publishes a complete denial without record or cursor content");
        CapturedRecordStudyDisplay recovered = view.CaptureDisplay(true, page, scale, true);
        Check.That(ReferenceEquals(recovered.Record, record) && ReferenceEquals(view.Measurement.CurrentPair, pair) &&
            recovered.Measurement == initial.Measurement, "capability recovery recomposes unchanged record-time evidence");
    }

    private static void StudyViewSeparatesAdmissionAndMeasurementPolicy()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord,
            "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordStudyDisplay display = view.CaptureDisplay(false, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1), true);
        Check.That(display.Admission.MayEnter && !display.Admission.InheritPatientAlarmAggregate && display.Record is not null &&
            display.Measurement!.ReasonCode == "RecordMeasurement.CourseLocked" && display.Measurement.First is null &&
            display.Measurement.Second is null && display.Measurement.Measurement is null,
            "independent record remains viewable while locked calipers disclose no geometry or results");
    }

    private static void StudyViewCompositionFailurePreservesRecordAndSelection()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        RecordCursorViewport invalid = new(0, 0, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(MeasurementReason(() => view.CaptureDisplay(true, invalid, scale, true)) == "RecordMeasurement.InvalidViewport" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "composition validates before returning a display and never edits evidence");
        CapturedRecordStudyDisplay denied = view.CaptureDisplay(false, invalid, scale, true);
        Check.That(!denied.Admission.MayEnter && denied.Record is null && denied.Measurement is null,
            "safety denial needs no successful measurement layout");
        Check.That(ReferenceEquals(view.CaptureDisplay(true, new(0, 200_000_000, 0, 100), scale, true).Record, record),
            "fresh valid composition recovers after a layout failure");
    }

    private static void StudyViewRestoredRecordRequiresCurrentAdmission()
    {
        CapturedRecordBinding restored = CapturedRecordBinding.Restore(MeasurementRecord().CaptureState());
        CapturedRecordStudyView view = new(restored, Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordStudyDisplay denied = view.CaptureDisplay(false, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1), false);
        Check.That(!denied.Admission.MayEnter && denied.Record is null, "restored record bytes grant no persisted overlay capability");
        try
        {
            _ = new CapturedRecordStudyView(restored, (Ecg12RecordContext)99, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
            throw new InvalidOperationException("invalid context accepted");
        }
        catch (Ecg12ViewAdmissionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Admission.InvalidContext", "construction validates explicit context"); }
    }

    private static void MeasurementPointPairCreatesAndRestoresSelection()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = measurement.ReplacePairFromPoints(new(25, 1), new(60, 1), new(75, 1), new(40, 1),
            new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        Check.That(ReferenceEquals(measurement.CurrentPair, pair) && pair.First.Value == new EcgManualCursor(50_000_000, 0, 1) &&
            pair.Second.Value == new EcgManualCursor(150_000_000, 1000, 1), "two manual points initialize calibrated data selection");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        RecordMeasurementDisplay display = restored.Measurement.CaptureDisplay(new(0, 200_000_000, 0, 200), new(0, 100, 60, 40, 1), true);
        Check.That(restored.First.Value == pair.First.Value && restored.Second.Value == pair.Second.Value &&
            display.Measurement!.ElapsedMilliseconds == new EcgMeasurementRatio(100, 1) &&
            display.Measurement.AmplitudeChangeMillivolts == new EcgMeasurementRatio(1, 1), "restore and changed display preserve data differences");
    }

    private static void MeasurementPointPairFailurePreservesSelection()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair original = measurement.CurrentPair!;
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(MeasurementReason(() => measurement.ReplacePairFromPoints(new(25, 1), new(60, 1), new(100, 1), new(40, 1), page, scale)) ==
            "RecordMeasurement.InvalidPoint" && ReferenceEquals(measurement.CurrentPair, original), "exclusive right edge rejects second point without partial publication");
        Check.That(MeasurementReason(() => measurement.ReplacePairFromPoints(new(0, 1), new(60, 1), new(1, 3), new(40, 1), page, scale)) ==
            "RecordMeasurement.UnrepresentableTime" && ReferenceEquals(measurement.CurrentPair, original), "nonintegral time is not silently rounded");
        measurement.ClearPair();
        Check.That(MeasurementReason(() => measurement.ReplacePairFromPoints(new(-1, 1), new(60, 1), new(50, 1), new(40, 1), page, scale)) ==
            "RecordMeasurement.InvalidPoint" && measurement.CurrentPair is null, "invalid first placement leaves an empty selection empty");
    }

    private static void MeasurementPointPairPreservesOrderAndEqualTime()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair original = measurement.CurrentPair!;
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        try
        {
            measurement.ReplacePairFromPoints(new(75, 1), new(60, 1), new(25, 1), new(40, 1), page, scale);
            throw new InvalidOperationException("reversed point pair accepted");
        }
        catch (EcgManualMeasurementException exception)
        { Check.That(exception.ReasonCode == "ManualMeasurement.TimeReversed", "manual endpoint order is never silently swapped"); }
        Check.That(ReferenceEquals(measurement.CurrentPair, original), "order failure preserves original pair");
        CapturedRecordCursorPair pair = measurement.ReplacePairFromPoints(new(0, 1), new(0, 1), new(0, 1), new(100, 1), page, scale);
        EcgManualMeasurementResult result = measurement.Calculate(pair.First, pair.Second, true);
        Check.That(result.ElapsedMilliseconds == new EcgMeasurementRatio(0, 1) && result.AuxiliaryRatePerMinute is null &&
            result.AmplitudeChangeMillivolts == new EcgMeasurementRatio(-5, 1), "equal time and closed vertical edges support amplitude-only placement");
    }

    private static void MeasurementPointPairEnforcesCurrentPolicy()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair original = measurement.CurrentPair!;
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            measurement.UpdatePolicy(policy);
            Check.That(MeasurementReason(() => measurement.ReplacePairFromPoints(new(0, 1), new(60, 1), new(50, 1), new(40, 1),
                new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1))) == $"RecordMeasurement.{policy}" &&
                ReferenceEquals(measurement.CurrentPair, original), "manual placement has no disabled-policy bypass");
        }
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair replacement = measurement.ReplacePairFromPoints(new(0, 1), new(60, 1), new(50, 1), new(40, 1),
            new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        Check.That(ReferenceEquals(measurement.CurrentPair, replacement) && !ReferenceEquals(original, replacement), "current enabled policy permits complete replacement");
    }

    private static void MeasurementDragLayoutChangeLatchesUntilCancel()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair original = measurement.CurrentPair!;
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordDrag drag = new(measurement, RecordCursorEnd.Second, page, scale);
        CapturedRecordCursorPair preview = drag.Preview(new(75, 1), new(40, 1), page, scale);
        Check.That(MeasurementReason(() => drag.Preview(new(75, 1), new(40, 1), page with { PlotWidthPixels = 200 }, scale)) == "RecordMeasurement.DragLayoutChanged" &&
            ReferenceEquals(measurement.CurrentPair, preview) && MeasurementReason(() => drag.Commit(page, scale)) == "RecordMeasurement.DragLayoutChanged",
            "layout mismatch preserves preview and cannot be bypassed by reverting layout arguments");
        Check.That(drag.Cancel().Second.Value == original.Second.Value, "cancel restores data independently of changed display layout");
    }

    private static void MeasurementDragChecksLayoutAtCommit()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordDrag drag = new(measurement, RecordCursorEnd.Second, page, scale);
        Check.That(MeasurementReason(() => drag.Commit(page, scale with { ZeroBaselinePixels = 61 })) == "RecordMeasurement.DragLayoutChanged",
            "release checks current layout even when no move event followed a gain/baseline change");
        drag.Cancel();
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value.DataTimeNs == 100_000_000, "cancelled layout change persists original data only");
    }

    private static void MeasurementDragAcceptsEquivalentGainRatios()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        CapturedRecordDrag drag = new(measurement, RecordCursorEnd.Second, page, new(0, 100, 60, 20, 1));
        EcgVerticalScale equivalent = new(0, 100, 60, 40, 2);
        CapturedRecordCursorPair preview = drag.Preview(new(75, 1), new(40, 1), page, equivalent);
        Check.That(ReferenceEquals(drag.Commit(page, equivalent), preview) && preview.Second.Value.MicrovoltsNumerator == 1000,
            "exactly equivalent gain fractions do not falsely invalidate a gesture");
    }

    private static void MeasurementDragStartRequiresVisibleValidatedPage()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair original = measurement.CurrentPair!;
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(MeasurementReason(() => { _ = new CapturedRecordDrag(measurement, RecordCursorEnd.Second, new(0, 100_000_000, 0, 100), scale); }) ==
            "RecordMeasurement.DragCursorNotVisible" &&
            MeasurementReason(() => { _ = new CapturedRecordDrag(measurement, RecordCursorEnd.First, new(0, 0, 0, 100), scale); }) ==
                "RecordMeasurement.InvalidViewport" && ReferenceEquals(measurement.CurrentPair, original),
            "start rejects off-page endpoints and invalid layouts without changing selection");
    }

    private static CapturedRecordMeasurement EditableMeasurement()
    {
        CapturedRecordMeasurement result = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        result.ReplacePair(new(0, 0, 1), new(100_000_000, 0, 1));
        return result;
    }

    private static void MeasurementDragCancelRestoresInitialValues()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair initial = measurement.CurrentPair!;
        CapturedRecordDrag drag = new(measurement, RecordCursorEnd.Second, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        drag.Preview(new(75, 1), new(40, 1), new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        CapturedRecordCursorPair restored = drag.Cancel();
        Check.That(restored.First.Value == initial.First.Value && restored.Second.Value == initial.Second.Value &&
            ReferenceEquals(measurement.CurrentPair, restored) && MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragFinished",
            "cancel restores pre-gesture data exactly and ends the gesture");
        RestoredRecordMeasurement replay = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(replay.Second.Value == initial.Second.Value, "checkpoint after cancel contains restored values");
    }

    private static void MeasurementDragCommitClosesGesture()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordDrag drag = new(measurement, RecordCursorEnd.Second, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        CapturedRecordCursorPair preview = drag.Preview(new(75, 1), new(40, 1), new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        Check.That(ReferenceEquals(drag.Commit(new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1)), preview) && preview.Second.Value == new EcgManualCursor(150_000_000, 1000, 1) &&
            MeasurementReason(() => drag.Commit(new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1))) == "RecordMeasurement.DragFinished" &&
            MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragFinished",
            "commit keeps the final preview and closed gestures cannot commit or roll it back again");
    }

    private static void MeasurementOldGestureCannotOverwriteReplacement()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordDrag drag = new(measurement, RecordCursorEnd.Second, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        CapturedRecordCursorPair replacement = measurement.ReplacePair(new(1, 0, 1), new(2, 0, 1));
        Check.That(MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragSuperseded" &&
            MeasurementReason(() => drag.Commit(new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1))) == "RecordMeasurement.DragSuperseded" &&
            ReferenceEquals(measurement.CurrentPair, replacement), "old gesture cannot undo a replacement");
        CapturedRecordDrag cleared = new(measurement, RecordCursorEnd.First, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        measurement.ClearPair();
        Check.That(MeasurementReason(() => cleared.Cancel()) == "RecordMeasurement.DragSuperseded" && measurement.CurrentPair is null,
            "old gesture cannot resurrect a cleared pair");
    }

    private static void MeasurementGestureFailurePreservesPreview()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordDrag drag = new(measurement, RecordCursorEnd.Second, new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        CapturedRecordCursorPair accepted = measurement.CurrentPair!;
        Check.That(MeasurementReason(() => drag.Preview(new(100, 1), new(60, 1), new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1))) ==
            "RecordMeasurement.InvalidPoint" && ReferenceEquals(measurement.CurrentPair, accepted), "invalid preview preserves gesture state");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => drag.Commit(new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1))) == "RecordMeasurement.CourseLocked" &&
            MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.CourseLocked", "current policy applies to gesture completion");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(drag.Cancel().Second.Value == accepted.Second.Value, "failed completion does not consume the gesture");
    }

    private static void MeasurementDisplayCombinesPagePositionsAndResults()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        measurement.ReplacePair(new(50_000_000, 0, 1), new(150_000_000, 1000, 1));
        RecordCursorViewport page = new(100_000_000, 200_000_000, 30, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordMeasurementDisplay display = measurement.CaptureDisplay(page, scale, true);
        Check.That(display.ReasonCode == "RecordMeasurement.Ready" && display.First is null && display.Second!.X.WholePixels == 80 &&
            display.Measurement!.ElapsedMilliseconds == new EcgMeasurementRatio(100, 1) &&
            display.Measurement.AuxiliaryRatePerMinute == new EcgMeasurementRatio(600, 1) &&
            measurement.CaptureDisplay(page, scale, false).Measurement!.AuxiliaryRatePerMinute is null,
            "one composition combines page visibility and full data measurement with explicit auxiliary-rate permission");
    }

    private static void MeasurementDisplaySuppressesLockedAndClearedResults()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        measurement.ReplacePair(new(0, 0, 1), new(1, 1, 1));
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            measurement.UpdatePolicy(policy);
            RecordMeasurementDisplay hidden = measurement.CaptureDisplay(page, scale, true);
            Check.That(hidden.First is null && hidden.Second is null && hidden.Measurement is null &&
                hidden.ReasonCode == (policy == SystemViewCommandAssessmentPolicy.Disabled ? "RecordMeasurement.Disabled" : "RecordMeasurement.CourseLocked"),
                "current denied policy removes both geometry and derived results");
        }
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        measurement.ClearPair();
        Check.That(measurement.CaptureDisplay(page, scale, true) == new RecordMeasurementDisplay("RecordMeasurement.NoCursorPair", null, null, null),
            "cleared selection produces an explicit empty display instead of cached results");
    }

    private static void MeasurementDisplayFailurePreservesSelection()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordCursorViewport valid = new(0, 200_000_000, 0, 100);
        RecordMeasurementDisplay before = measurement.CaptureDisplay(valid, scale, false);
        Check.That(MeasurementReason(() => measurement.CaptureDisplay(valid with { EndExclusiveDataTimeNs = 0 }, scale, true)) == "RecordMeasurement.InvalidViewport" &&
            ReferenceEquals(measurement.CurrentPair, pair) && measurement.CaptureDisplay(valid, scale, false) == before,
            "failed composition publishes no partial result and preserves accepted selection");
    }

    private static void MeasurementDisplayRebuildsAfterRestore()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        measurement.ReplacePair(new(0, 1000, 3), new(123456789, -1000, 3));
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        RecordCursorViewport page = new(100_000_000, 200_000_000, 30, 333);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordMeasurementDisplay before = measurement.CaptureDisplay(page, scale, true), after = restored.Measurement.CaptureDisplay(page, scale, true);
        Check.That(before.Measurement == after.Measurement && before.First is null && after.First is null &&
            before.Second!.X == after.Second!.X && before.Second.Y == after.Second.Y,
            "restored data evidence regenerates matching display without persisted derived values");
    }

    private static void MeasurementClearRemovesActivePairAndAllowsReplacement()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair previous = measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 0, 1));
        measurement.ClearPair();
        measurement.ClearPair();
        Check.That(measurement.CurrentPair is null && MeasurementReason(() => measurement.CaptureCheckpoint()) == "RecordMeasurement.NoCursorPair" &&
            MeasurementReason(() => measurement.MoveCursor(RecordCursorEnd.Second, new(75, 1), new(60, 1),
                new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1))) == "RecordMeasurement.NoCursorPair",
            "idempotent clear removes active editing and checkpoint state");
        CapturedRecordCursorPair next = measurement.ReplacePair(new(1, 0, 1), new(2, 0, 1));
        Check.That(ReferenceEquals(measurement.CurrentPair, next) && previous.First.Value.DataTimeNs == 0,
            "replacement after clear is independent of previously issued immutable values");
    }

    private static void MeasurementClearObeysPolicyAndPreservesCheckpoints()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = measurement.ReplacePair(new(0, 0, 1), new(1, 1, 1));
        CapturedRecordMeasurementCheckpoint checkpoint = measurement.CaptureCheckpoint();
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => measurement.ClearPair()) == "RecordMeasurement.CourseLocked" && ReferenceEquals(measurement.CurrentPair, pair),
            "course lock denies clear without deleting accepted values");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        measurement.ClearPair();
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(checkpoint, SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(measurement.CurrentPair is null && restored.First.Value == pair.First.Value && restored.Second.Value == pair.Second.Value,
            "clearing local selection does not mutate an earlier independently captured checkpoint");
    }

    private static void MeasurementPairReplacementIsAtomic()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair accepted = measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        Check.That(MeasurementReason(() => measurement.ReplacePair(new(1, 0, 1), new(200_000_000, 0, 1))) == "RecordMeasurement.CursorOutsideRecord" &&
            ReferenceEquals(measurement.CurrentPair, accepted), "late second-cursor rejection cannot publish half a replacement");
        CapturedRecordCursorPair replacement = measurement.ReplacePair(new(1, 0, 1), new(2, 0, 1));
        Check.That(ReferenceEquals(measurement.CurrentPair, replacement) && accepted.First.Value.DataTimeNs == 0,
            "complete replacement commits once and leaves previous immutable pair unchanged");
    }

    private static void MeasurementDragCommitsOnlyOrderedPairs()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair initial = measurement.ReplacePair(new(50_000_000, 0, 1), new(150_000_000, 1000, 1));
        RecordCursorViewport viewport = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        try { _ = measurement.MoveCursor(RecordCursorEnd.First, new(90, 1), new(60, 1), viewport, scale); throw new InvalidOperationException("crossed cursor accepted"); }
        catch (EcgManualMeasurementException exception) { Check.That(exception.ReasonCode == "ManualMeasurement.TimeReversed", "crossing rejects without swapping endpoints"); }
        Check.That(ReferenceEquals(measurement.CurrentPair, initial), "rejected drag preserves the previous pair");
        CapturedRecordCursorPair moved = measurement.MoveCursor(RecordCursorEnd.First, new(75, 1), new(40, 1), viewport, scale);
        Check.That(ReferenceEquals(moved.Second, initial.Second) && moved.First.Value == moved.Second.Value &&
            measurement.Calculate(moved.First, moved.Second, true).AuxiliaryRatePerMinute is null,
            "moving one endpoint preserves the other and equal times produce no auxiliary rate");
    }

    private static void MeasurementDragRejectsMissingInvalidAndLockedEntry()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        RecordCursorViewport viewport = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(MeasurementReason(() => measurement.MoveCursor(RecordCursorEnd.First, new(0, 1), new(60, 1), viewport, scale)) == "RecordMeasurement.NoCursorPair" &&
            MeasurementReason(() => measurement.CaptureCheckpoint()) == "RecordMeasurement.NoCursorPair",
            "editing and capture require an initialized pair");
        CapturedRecordCursorPair pair = measurement.ReplacePair(new(0, 0, 1), new(1, 0, 1));
        Check.That(MeasurementReason(() => measurement.MoveCursor((RecordCursorEnd)999, new(0, 1), new(60, 1), viewport, scale)) == "RecordMeasurement.InvalidCursorEnd",
            "invalid endpoint rejects explicitly");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => measurement.MoveCursor(RecordCursorEnd.Second, new(0, 1), new(60, 1), viewport, scale)) == "RecordMeasurement.CourseLocked" &&
            ReferenceEquals(pair, measurement.CurrentPair), "locking prevents editing without mutating the saved pair");
    }

    private static void MeasurementActivePairRestoresForFurtherEditing()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 0, 1));
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(ReferenceEquals(restored.Measurement.CurrentPair!.First, restored.First) && restored.Second.Value == pair.Second.Value,
            "restore initializes a complete active pair with fresh owned handles");
        CapturedRecordCursorPair moved = restored.Measurement.MoveCursor(RecordCursorEnd.Second, new(75, 1), new(60, 1),
            new(0, 200_000_000, 0, 100), new(0, 100, 60, 20, 1));
        Check.That(moved.Second.Value.DataTimeNs == 150_000_000 && measurement.CurrentPair!.Second.Value.DataTimeNs == 100_000_000,
            "restored editing is independent from the original measurement context");
    }

    private static void MeasurementPointerMapsPageTimeAndVoltage()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        RecordCursorViewport viewport = new(100_000_000, 200_000_000, 30, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursor cursor = measurement.CreateCursorFromPoint(new(80, 1), new(40, 1), viewport, scale);
        Check.That(cursor.Value == new EcgManualCursor(150_000_000, 1000, 1) && cursor.Slot.ChannelId == ChannelIds[11],
            "manual point maps through page offset and declared gain into an owned lead cursor");
        Check.That(measurement.CreateCursorFromPoint(new(30, 1), new(100, 1), viewport, scale).Value == new EcgManualCursor(100_000_000, -2000, 1),
            "inclusive left and bottom geometry preserve signed amplitude");
    }

    private static void MeasurementPointerRejectsEdgesAndUnrepresentableValues()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        RecordCursorViewport viewport = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        ExactPlotCoordinate[] invalid = [null!, new(100, 1), new(-1, 1), new(0, 0)];
        foreach (ExactPlotCoordinate x in invalid)
        {
            Check.That(MeasurementReason(() => measurement.CreateCursorFromPoint(x, new(60, 1), viewport, scale)) == "RecordMeasurement.InvalidPoint",
                "invalid rational positions and exclusive right edge reject");
        }
        Check.That(MeasurementReason(() => measurement.CreateCursorFromPoint(new(1, 3), new(60, 1), viewport, scale)) == "RecordMeasurement.UnrepresentableTime" &&
            MeasurementReason(() => measurement.CreateCursorFromPoint(new(0, 1), new(0, 1), viewport,
                new(0, int.MaxValue, int.MaxValue, 1, uint.MaxValue))) == "RecordMeasurement.UnrepresentableAmplitude",
            "unrepresentable time or voltage rejects without rounding or overflow");
    }

    private static void MeasurementPointerRoundTripsFractionalEvidence()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor source = measurement.CreateCursor(new(123456789, -1000, 3));
        RecordCursorViewport viewport = new(100_000_000, 200_000_000, 30, 333);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        ProjectedRecordCursor projected = measurement.ProjectCursor(source, viewport, scale)!;
        ExactPlotCoordinate x = new((BigInteger)projected.X.WholePixels * projected.X.FractionDenominator + projected.X.FractionNumerator,
            projected.X.FractionDenominator);
        CapturedRecordCursor mapped = measurement.CreateCursorFromPoint(x,
            new((BigInteger)projected.Y.PixelNumerator, (BigInteger)projected.Y.PixelDenominator), viewport, scale);
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(source, mapped), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(mapped.Value == source.Value && restored.Second.Value == source.Value,
            "fractional forward/inverse mapping and checkpoint restore preserve exact manual data");
    }

    private static void MeasurementPointerPolicyAndFailurePreserveExistingPair()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(0, 0, 1));
        RecordCursorViewport viewport = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(MeasurementReason(() => measurement.CreateCursorFromPoint(new(0, 1), new(101, 1), viewport, scale)) == "RecordMeasurement.InvalidPoint",
            "off-plot pointer amplitude rejects instead of clamping");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => measurement.CreateCursorFromPoint(new(0, 1), new(60, 1), viewport, scale)) == "RecordMeasurement.CourseLocked",
            "pointer entry cannot bypass current course policy");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(measurement.Calculate(cursor, cursor, false).ElapsedMilliseconds == new EcgMeasurementRatio(0, 1),
            "failed pointer entry leaves existing cursors untouched");
    }

    private static void MeasurementPagesOwnSharedBoundaryOnce()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor boundary = measurement.CreateCursor(new(100_000_000, 0, 1));
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(measurement.ProjectCursor(boundary, new(0, 100_000_000, 30, 500), scale) is null &&
            measurement.ProjectCursor(boundary, new(100_000_000, 200_000_000, 30, 500), scale)!.X.WholePixels == 30,
            "shared page boundary appears only at the next page's inclusive left edge");
        Check.That(measurement.ProjectCursor(measurement.CreateCursor(new(99_999_999, 0, 1)), new(100_000_000, 200_000_000, 30, 500), scale) is null,
            "off-page cursors are omitted instead of clamped to page edges");
    }

    private static void MeasurementPageZoomPreservesDataValues()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = measurement.CreateCursor(new(50_000_000, 0, 1)), second = measurement.CreateCursor(new(150_000_000, 1000, 1));
        EcgManualMeasurementResult before = measurement.Calculate(first, second, true);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        ProjectedRecordCursor full = measurement.ProjectCursor(second, 30, 100, scale);
        ProjectedRecordCursor page = measurement.ProjectCursor(second, new(100_000_000, 200_000_000, 30, 100), scale)!;
        Check.That(full.X.WholePixels == 105 && page.X.WholePixels == 80 && full.Y == page.Y &&
            ReferenceEquals(page.Cursor, second) && measurement.Calculate(first, second, true) == before,
            "subrange projection changes only layout, not source values or measurements spanning pages");
    }

    private static void MeasurementPagesValidateEvenHiddenCursors()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(0, 0, 1));
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordCursorViewport[] invalid = [null!, new(-1, 100, 0, 100), new(1, 1, 0, 100), new(100, 99, 0, 100), new(0, 200_000_001, 0, 100)];
        foreach (RecordCursorViewport viewport in invalid)
        {
            Check.That(MeasurementReason(() => measurement.ProjectCursor(cursor, viewport, scale)) == "RecordMeasurement.InvalidViewport",
                "invalid record subranges reject before output");
        }
        try { _ = measurement.ProjectCursor(cursor, new(1, 100, 0, 0), scale); throw new InvalidOperationException("invalid hidden layout accepted"); }
        catch (SweepPlotGeometryException exception) { Check.That(exception.ReasonCode == "SweepGeometry.InvalidPlotBounds", "hidden cursors do not bypass layout validation"); }
        Check.That(measurement.ProjectCursor(cursor, new(0, 100, 0, 100), scale)!.X.WholePixels == 0, "rejected projection leaves cursor unchanged");
    }

    private static void MeasurementPagesReprojectRestoredValues()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(123456789, 1000, 3));
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(cursor, cursor), SystemViewCommandAssessmentPolicy.Enabled);
        RecordCursorViewport viewport = new(100_000_000, 200_000_000, 30, 333);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        ProjectedRecordCursor expected = measurement.ProjectCursor(cursor, viewport, scale)!;
        ProjectedRecordCursor actual = restored.Measurement.ProjectCursor(restored.First, viewport, scale)!;
        Check.That(expected.X == actual.X && expected.Y == actual.Y, "restored data values reproject without saved page pixels");
        restored.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(MeasurementReason(() => restored.Measurement.ProjectCursor(restored.First, viewport, scale)) == "RecordMeasurement.Disabled",
            "page projection retains current course policy enforcement");
    }

    private static void MeasurementProjectionPreservesValuesAcrossResize()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 0, 1)), second = measurement.CreateCursor(new(100_000_000, 1000, 1));
        EcgManualMeasurementResult before = measurement.Calculate(first, second, true);
        ProjectedRecordCursor narrow = measurement.ProjectCursor(second, 30, 100, new(0, 100, 60, 20, 1));
        ProjectedRecordCursor wide = measurement.ProjectCursor(second, 40, 200, new(0, 200, 120, 40, 1));
        Check.That(narrow.X.WholePixels == 80 && narrow.Y.PixelNumerator == 40 &&
            wide.X.WholePixels == 140 && wide.Y.PixelNumerator == 80 && ReferenceEquals(wide.Cursor, second) &&
            measurement.Calculate(first, second, true) == before,
            "layout and gain alter only projected pixels while preserving data cursors and exact measurement");
    }

    private static void MeasurementProjectionRetainsFractionalAndOutsideCoordinates()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(1, 1000, 3));
        ProjectedRecordCursor point = measurement.ProjectCursor(cursor, 0, 100, new(0, 100, 60, 20, 1));
        Check.That(point.X.WholePixels == 0 && point.X.FractionNumerator == 100 && point.X.FractionDenominator == 200_000_000 &&
            point.Y.PixelNumerator == 160 && point.Y.PixelDenominator == 3,
            "subpixel time and rational voltage remain exact at projection");
        ProjectedRecordCursor above = measurement.ProjectCursor(measurement.CreateCursor(new(199_999_999, 10000, 1)),
            0, 100, new(0, 100, 60, 20, 1));
        Check.That(above.X.WholePixels == 99 && above.Y.Relation == VerticalPlotRelation.AbovePlot && above.Y.PixelNumerator == -140,
            "last included time remains left of the right edge and outside amplitudes are not clamped");
    }

    private static void MeasurementProjectionEnforcesPolicyOwnershipAndBounds()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordMeasurement other = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(0, 0, 1));
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(MeasurementReason(() => measurement.ProjectCursor(other.CreateCursor(cursor.Value), 0, 100, scale)) == "RecordMeasurement.ForeignCursor",
            "projection cannot expose foreign cursor geometry");
        try { _ = measurement.ProjectCursor(cursor, 0, 0, scale); throw new InvalidOperationException("zero viewport accepted"); }
        catch (SweepPlotGeometryException exception) { Check.That(exception.ReasonCode == "SweepGeometry.InvalidPlotBounds", "invalid viewport rejects"); }
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => measurement.ProjectCursor(cursor, 0, 100, scale)) == "RecordMeasurement.CourseLocked",
            "course locking covers the projection entry point too");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(measurement.ProjectCursor(cursor, 0, 100, scale).X.WholePixels == 0, "failed projections preserve accepted evidence");
    }

    private static void MeasurementProjectionRestoresFromDataEvidence()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 0, 1)), second = measurement.CreateCursor(new(123456789, -1000, 3));
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(first, second), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        ProjectedRecordCursor expected = measurement.ProjectCursor(second, 30, 500, scale);
        ProjectedRecordCursor actual = restored.Measurement.ProjectCursor(restored.Second, 30, 500, scale);
        Check.That(expected.X == actual.X && expected.Y == actual.Y && actual.Cursor.Slot == second.Slot,
            "restore rebuilds identical display coordinates from record values without persisting pixels");
    }

    private static void MeasurementCheckpointReissuesOwnedCursorPair()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 1000, 3)), second = measurement.CreateCursor(new(199_999_999, -1000, 3));
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(first, second), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Measurement.Calculate(restored.First, restored.Second, true) == measurement.Calculate(first, second, true) &&
            restored.First.Slot == first.Slot && MeasurementReason(() => restored.Measurement.Calculate(first, second, true)) == "RecordMeasurement.ForeignCursor",
            "restore rebuilds verified record and exact cursor values with fresh ownership");
    }

    private static void MeasurementCheckpointRejectsTamperedValues()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 0, 1)), second = measurement.CreateCursor(new(1, 1, 1));
        CapturedRecordMeasurementCheckpoint checkpoint = measurement.CaptureCheckpoint(first, second);
        CapturedRecordMeasurementCheckpoint[] invalid = [null!, checkpoint with { SlotId = "unknown" },
            checkpoint with { First = second.Value, Second = first.Value }, checkpoint with { Second = new(200_000_000, 0, 1) },
            checkpoint with { First = new(0, 0, 0) }, checkpoint with { Record = null! }];
        foreach (CapturedRecordMeasurementCheckpoint state in invalid)
        {
            Check.That(MeasurementReason(() => CapturedRecordMeasurement.Restore(state, SystemViewCommandAssessmentPolicy.Enabled)) ==
                "RecordMeasurement.InvalidCheckpoint", "invalid restored identity, range and values reject before a pair is published");
        }
        Check.That(measurement.Calculate(first, second, false).ElapsedMilliseconds == new EcgMeasurementRatio(1, 1_000_000),
            "failed restores leave the original pair unchanged");
    }

    private static void MeasurementCheckpointUsesCurrentCoursePolicy()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(0, 0, 1));
        CapturedRecordMeasurementCheckpoint state = measurement.CaptureCheckpoint(cursor, cursor);
        Check.That(MeasurementReason(() => CapturedRecordMeasurement.Restore(state, SystemViewCommandAssessmentPolicy.CourseLocked)) == "RecordMeasurement.CourseLocked" &&
            MeasurementReason(() => CapturedRecordMeasurement.Restore(state, SystemViewCommandAssessmentPolicy.Disabled)) == "RecordMeasurement.Disabled" &&
            MeasurementReason(() => CapturedRecordMeasurement.Restore(state, (SystemViewCommandAssessmentPolicy)999)) == "RecordMeasurement.InvalidPolicy",
            "checkpoint cannot restore past course permission or accept an invalid current policy");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => measurement.CaptureCheckpoint(cursor, cursor)) == "RecordMeasurement.CourseLocked",
            "locked measurement cannot export a pair through the checkpoint path");
    }

    private static void MeasurementCheckpointRejectsForeignCaptureAndOwnsRecord()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordMeasurement measurement = new(record, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordMeasurement other = new(record, "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(0, 0, 1));
        Check.That(MeasurementReason(() => measurement.CaptureCheckpoint(cursor, other.CreateCursor(cursor.Value))) == "RecordMeasurement.ForeignCursor",
            "capture cannot persist foreign lead handles");
        CapturedRecordMeasurementCheckpoint state = measurement.CaptureCheckpoint(cursor, cursor);
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(state, SystemViewCommandAssessmentPolicy.Enabled);
        RecordSlotBinding[] changed = state.Record.Slots.ToArray();
        changed[0] = new("ecg.slot0", Guid.Empty);
        Check.That(MeasurementReason(() => CapturedRecordMeasurement.Restore(state with { Record = state.Record with { Slots = changed } },
            SystemViewCommandAssessmentPolicy.Enabled)) == "RecordMeasurement.InvalidCheckpoint" &&
            restored.First.Slot.ChannelId == ChannelIds[11], "record restore revalidates mapping and previously restored pair remains owned");
    }

    private static void MeasurementSlotResolvesExplicitChannelMapping()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(0, 0, 1));
        Check.That(measurement.Slot.SlotId == "ecg.slot0" && measurement.Slot.ChannelId == ChannelIds[11] &&
            cursor.Slot == measurement.Slot, "measurement resolves the declared reversed slot mapping rather than channel array position");
    }

    private static void MeasurementSlotRejectsUnknownIdentity()
    {
        CapturedRecordBinding record = MeasurementRecord();
        string[] invalid = [null!, "", "ecg.slot12", "ECG.SLOT0", "ecg.slot0 "];
        foreach (string slot in invalid)
        {
            Check.That(MeasurementReason(() => { _ = new CapturedRecordMeasurement(record, slot, SystemViewCommandAssessmentPolicy.Enabled); }) ==
                "RecordMeasurement.UnknownSlot", "missing or nonmatching slot identities reject without guessing a lead");
        }
    }

    private static void MeasurementSlotPreventsCrossLeadCursorMixing()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordMeasurement firstLead = new(record, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordMeasurement secondLead = new(record, "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = firstLead.CreateCursor(new(0, 0, 1)), second = firstLead.CreateCursor(new(1, 1, 1));
        EcgManualMeasurementResult accepted = firstLead.Calculate(first, second, false);
        Check.That(MeasurementReason(() => firstLead.Calculate(first, secondLead.CreateCursor(second.Value), true)) ==
            "RecordMeasurement.ForeignCursor" && firstLead.Calculate(first, second, false) == accepted,
            "another lead's cursor cannot alter an accepted same-lead calculation");
    }

    private static void MeasurementSlotSurvivesInputMutationAndRecordRestore()
    {
        RecordSlotBinding[] slots = BindingSlots();
        CapturedRecordBinding record = CapturedRecordBinding.Create(BindingPresentation().CaptureState(), BindingArchive(), slots);
        CapturedRecordMeasurement measurement = new(record, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        slots[0] = new("ecg.slot0", Guid.Empty);
        CapturedRecordMeasurement restored = new(CapturedRecordBinding.Restore(record.CaptureState()), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(measurement.Slot.ChannelId == ChannelIds[11] && restored.Slot == measurement.Slot &&
            restored.CreateCursor(new(0, 1000, 3)).Slot == measurement.Slot,
            "caller array mutation and record restore preserve the verified channel mapping");
    }

    private static string MeasurementReason(Action action)
    {
        try { action(); }
        catch (CapturedRecordMeasurementException exception) { return exception.ReasonCode; }
        throw new InvalidOperationException("measurement unexpectedly accepted");
    }

    private static void MeasurementPolicyRejectsDisabledAndLockedEntry()
    {
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", policy);
            string expected = policy == SystemViewCommandAssessmentPolicy.Disabled ? "RecordMeasurement.Disabled" : "RecordMeasurement.CourseLocked";
            Check.That(MeasurementReason(() => measurement.CreateCursor(new(0, 0, 1))) == expected &&
                MeasurementReason(() => measurement.Calculate(null!, null!, true)) == expected,
                "both cursor entry and calculation enforce resolved policy before accepting values");
        }
    }

    private static void MeasurementPolicyRevokesExistingCursorCalculation()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 0, 1)), second = measurement.CreateCursor(new(1, 1, 1));
        EcgManualMeasurementResult accepted = measurement.Calculate(first, second, false);
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => measurement.Calculate(first, second, true)) == "RecordMeasurement.CourseLocked",
            "previously issued cursors cannot bypass a newly locked course policy");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(measurement.Calculate(first, second, false) == accepted, "explicit re-enable preserves cursor values without recomputing source data");
    }

    private static void InvalidMeasurementPolicyPreservesAcceptedState()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordMeasurement measurement = new(record, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(MeasurementReason(() => measurement.UpdatePolicy((SystemViewCommandAssessmentPolicy)999)) == "RecordMeasurement.InvalidPolicy" &&
            MeasurementReason(() => measurement.CreateCursor(new(0, 0, 1))) == "RecordMeasurement.Disabled" &&
            MeasurementReason(() => { _ = new CapturedRecordMeasurement(record, "ecg.slot0", (SystemViewCommandAssessmentPolicy)999); }) == "RecordMeasurement.InvalidPolicy",
            "invalid updates and construction fail closed without relaxing accepted policy");
    }

    private static void RestoredMeasurementUsesExplicitCurrentPolicy()
    {
        CapturedRecordBinding record = CapturedRecordBinding.Restore(MeasurementRecord().CaptureState());
        CapturedRecordMeasurement measurement = new(record, "ecg.slot0", SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => measurement.CreateCursor(new(0, 0, 1))) == "RecordMeasurement.CourseLocked",
            "restoring a record does not restore an old enabled policy implicitly");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor cursor = measurement.CreateCursor(new(0, 0, 1));
        Check.That(measurement.Calculate(cursor, cursor, true).AuxiliaryRatePerMinute is null,
            "policy enable retains the domain zero-interval rule");
    }

    private static void RecordMeasurementUsesBoundCursorValues()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 0, 1));
        CapturedRecordCursor second = measurement.CreateCursor(new(100_000_000, 1000, 1));
        EcgManualMeasurementResult result = measurement.Calculate(first, second, true);
        Check.That(result.ElapsedMilliseconds == new EcgMeasurementRatio(100, 1) &&
            result.AmplitudeChangeMillivolts == new EcgMeasurementRatio(1, 1) &&
            result.AuxiliaryRatePerMinute == new EcgMeasurementRatio(600, 1) &&
            measurement.Calculate(first, second, false).AuxiliaryRatePerMinute is null,
            "bound manual values preserve exact calculation and explicit auxiliary-rate permission");
    }

    private static void RecordMeasurementEnforcesHalfOpenRange()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord(), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(measurement.CreateCursor(new(0, 0, 1)).Value.DataTimeNs == 0 &&
            measurement.CreateCursor(new(199_999_999, 0, 1)).Value.DataTimeNs == 199_999_999,
            "record start and last included nanosecond accept manual cursors");
        foreach (long time in new long[] { 200_000_000, long.MaxValue })
        {
            try { _ = measurement.CreateCursor(new(time, 0, 1)); throw new InvalidOperationException("outside cursor accepted"); }
            catch (CapturedRecordMeasurementException exception) { Check.That(exception.ReasonCode == "RecordMeasurement.CursorOutsideRecord", "exclusive end and later times reject"); }
        }
        try { _ = measurement.CreateCursor(new(0, 1, 0)); throw new InvalidOperationException("invalid voltage accepted"); }
        catch (EcgManualMeasurementException exception) { Check.That(exception.ReasonCode == "ManualMeasurement.InvalidCursor", "bound cursors still validate voltage"); }
    }

    private static void RecordMeasurementRejectsForeignCursorsWithoutMutation()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordMeasurement measurement = new(record, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled), other = new(record, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 0, 1)), second = measurement.CreateCursor(new(1, 1, 1));
        EcgManualMeasurementResult accepted = measurement.Calculate(first, second, false);
        CapturedRecordCursor[] foreign = [null!, other.CreateCursor(second.Value)];
        foreach (CapturedRecordCursor cursor in foreign)
        {
            try { _ = measurement.Calculate(first, cursor, true); throw new InvalidOperationException("foreign cursor accepted"); }
            catch (CapturedRecordMeasurementException exception) { Check.That(exception.ReasonCode == "RecordMeasurement.ForeignCursor", "foreign ownership rejects even with equal record identity"); }
        }
        Check.That(measurement.Calculate(first, second, false) == accepted, "rejected foreign values leave issued cursors unchanged");
    }

    private static void RestoredRecordMeasurementRequiresFreshCursors()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordMeasurement original = new(record, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursor first = original.CreateCursor(new(0, 1000, 3)), second = original.CreateCursor(new(199_999_999, -1000, 3));
        CapturedRecordMeasurement restored = new(CapturedRecordBinding.Restore(record.CaptureState()), "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        try { _ = restored.Calculate(first, second, true); throw new InvalidOperationException("old handles accepted"); }
        catch (CapturedRecordMeasurementException exception) { Check.That(exception.ReasonCode == "RecordMeasurement.ForeignCursor", "restored ownership rejects old handles"); }
        Check.That(restored.Calculate(restored.CreateCursor(first.Value), restored.CreateCursor(second.Value), true) ==
            original.Calculate(first, second, true), "explicit reissue revalidates values and recreates exact measurement after record restore");
    }

    private static FillOnceThenHoldStateMachine BindingPresentation(long playhead = 200_000_000)
    {
        FillOnceThenHoldStateMachine machine = FillOnceThenHoldStateMachine.Start(
            new FillOnceThenHoldPlan("ecg12.standard", "record.ecg12-7", 7, 11,
                0, 200_000_000, 200_000_000,
                Enumerable.Range(0, 12).Select(index => $"ecg.slot{index}").ToArray()),
            13, 17, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(),
            0, 0);
        machine.Advance(1, playhead);
        return machine;
    }

    private static RecordSlotBinding[] BindingSlots() => Enumerable.Range(0, 12)
        .Select(index => new RecordSlotBinding($"ecg.slot{index}", ChannelIds[11 - index])).ToArray();

    private static WaveformRecordArchive BindingArchive() => WaveformRecordArchive.Create(
        Plan(endExclusiveSimTimeNs: 200_000_000), [Wire(100, 0)]);

    private static void CompletedRecordBindsExplicitSlots()
    {
        foreach (long playhead in new long[] { 200_000_000, 900_000_000 })
        {
            FillOnceThenHoldStateMachine machine = BindingPresentation(playhead);
            CapturedRecordBinding binding = CapturedRecordBinding.Create(
                machine.CaptureState(), BindingArchive(), BindingSlots());
            machine.Advance(2, 1_000_000_000);
            Check.That(binding.Slots[0].ChannelId == ChannelIds[11] &&
                binding.CapturePinnedRecordRange().EndExclusiveDataSimTimeNs == 200_000_000 &&
                binding.CaptureProjection().TransientReplayPolicy == TransientReplayPolicy.Suppress &&
                binding.ReadBlocks().Count == 1,
                "exact and skipped completion bind explicit channels, never positional channel order");
        }
    }

    private static void RecordBindingRejectsIncompleteAndMismatchedInputs()
    {
        FillOnceThenHoldStateMachine machine = BindingPresentation();
        FillOnceThenHoldState state = machine.CaptureState();
        WaveformRecordArchive archive = BindingArchive();
        RecordSlotBinding[] slots = BindingSlots();
        Check.That(BindingReason(() => CapturedRecordBinding.Create(
            BindingPresentation(199_999_999).CaptureState(), archive, slots)) ==
            "CapturedRecordBinding.RecordNotCaptured", "one nanosecond before completion cannot bind");
        foreach (FillOnceThenHoldPlan plan in new[]
        {
            state.Plan with { GroupId = "other" }, state.Plan with { RecordRef = "other" },
            state.Plan with { SweepEpoch = 8 }, state.Plan with { RecordStartDataSimTimeNs = 1, RecordDurationNs = 199_999_999 },
            state.Plan with { RecordDurationNs = 199_999_999 },
        })
        {
            Check.That(BindingReason(() => CapturedRecordBinding.Create(state with { Plan = plan }, archive, slots)) ==
                "CapturedRecordBinding.IdentityMismatch", "every pinned identity must match");
        }

        foreach (RecordSlotBinding[] invalid in new[]
        {
            slots.Take(11).ToArray(), slots.Reverse().ToArray(),
            slots.Select((slot, index) => index == 11 ? slot with { ChannelId = slots[0].ChannelId } : slot).ToArray(),
            slots.Select((slot, index) => index == 0 ? slot with { ChannelId = Guid.Empty } : slot).ToArray(),
            slots.Select((slot, index) => index == 0 ? null! : slot).ToArray(),
        })
        {
            Check.That(BindingReason(() => CapturedRecordBinding.Create(state, archive, invalid)) ==
                "CapturedRecordBinding.InvalidSlots", "partial, reordered, duplicated and unknown mappings reject");
        }

        Check.That(machine.CaptureProjection().SweepRevision == state.SweepRevision &&
            CapturedRecordBinding.Create(state, archive, slots).Slots.Count == 12,
            "failed publication leaves source state usable for an exact retry");
    }

    private static void RecordBindingCheckpointIsDefensive()
    {
        RecordSlotBinding[] slots = BindingSlots();
        CapturedRecordBinding binding = CapturedRecordBinding.Create(
            BindingPresentation().CaptureState(), BindingArchive(), slots);
        slots[0] = slots[0] with { ChannelId = Guid.Empty };
        CapturedRecordBindingState state = binding.CaptureState();
        CapturedRecordBinding restored = CapturedRecordBinding.Restore(state);
        state.Archive.RawEnvelopes[0][0] ^= 1;
        Check.That(binding.Slots[0].ChannelId == ChannelIds[11] &&
            Equivalent(binding.ReadBlocks(), restored.ReadBlocks()) &&
            BindingReason(() => CapturedRecordBinding.Restore(state)) == "CapturedRecordBinding.InvalidCheckpoint" &&
            BindingReason(() => CapturedRecordBinding.Restore(binding.CaptureState() with { Slots = slots })) ==
                "CapturedRecordBinding.InvalidCheckpoint",
            "restore revalidates mapping and bytes without exposing accepted state to caller mutation");
    }

    private static string? BindingReason(Action action)
    {
        try { action(); return null; }
        catch (CapturedRecordBindingException exception) { return exception.ReasonCode; }
    }

    private static void StandardRecordArchivesFiftySharedBlocks()
    {
        byte[][] wires = StandardWires();
        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            Plan(),
            wires);

        IReadOnlyList<ArchivedWaveformBlock> blocks = archive.ReadBlocks();
        string firstContentHash = Convert.ToHexStringLower(wires[0].AsSpan(
            WaveformEnvelopeCodec.ContentSha256Offset,
            32));
        Check.That(
            archive.RecordRef == "record.ecg12-7" &&
            archive.RecordStartSimTimeNs == 0 &&
            archive.RecordEndExclusiveSimTimeNs == 10_000_000_000 &&
            archive.BlockCount == 50 &&
            blocks[0].BlockSequence == 100 &&
            blocks[0].StartSimTimeNs == 0 &&
            blocks[0].ContentSha256 == firstContentHash &&
            blocks[^1].BlockSequence == 149 &&
            blocks[^1].StartSimTimeNs == 9_800_000_000,
            "one standard record must retain fifty exact shared 200 ms blocks");
    }

    private static void ArchivedRecordSurvivesLiveRingEviction()
    {
        byte[][] wires = StandardWires();
        WaveformBlockRing live = WaveformBlockRing.Start(
            SessionId,
            InstanceId,
            timebaseEpoch: 3,
            streamEpoch: 7,
            firstBlockSequence: 100,
            firstBlockStartSimTimeNs: 0,
            WaveformBlockRing.ClientHistoryBlockCount);
        foreach (byte[] wire in wires)
        {
            _ = live.Append(wire);
        }

        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            Plan(),
            live.CaptureState().RetainedRawBlocks);
        byte[] archivedFirst = archive.ReadBlocks()[0].RawEnvelope;
        for (ulong sequence = 150; sequence < 202; sequence++)
        {
            long start = checked((long)(sequence - 100) * 200_000_000);
            _ = live.Append(Wire(sequence, start));
        }

        Check.That(
            live.OldestBlockSequence == 152 &&
            live.ReadFrom(100, 1).Status ==
                WaveformBlockReplayStatus.RecoverySnapshotRequired &&
            archive.BlockCount == 50 &&
            archive.ReadBlocks()[0].RawEnvelope.SequenceEqual(archivedFirst),
            "pinned record bytes must outlive eviction from the Live ring");
    }

    private static void UnalignedRangeUsesOnlyItsMinimalCover()
    {
        WaveformRecordArchivePlan plan = Plan(
            startSimTimeNs: 100_000_000,
            endExclusiveSimTimeNs: 550_000_000);
        byte[][] exact =
        [
            Wire(100, 0),
            Wire(101, 200_000_000),
            Wire(102, 400_000_000),
        ];

        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            plan,
            exact);
        Check.That(
            archive.BlockCount == 3 &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                exact.Take(2).ToArray())) ==
                "WaveformRecordArchive.RangeNotCovered" &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [.. exact, Wire(103, 600_000_000)])) ==
                "WaveformRecordArchive.RangeNotCovered",
            "an unaligned interval must retain only its minimal covering blocks");
    }

    private static void IdentityConfigurationAndChannelSetFailClosed()
    {
        WaveformRecordArchivePlan shortPlan = Plan(
            endExclusiveSimTimeNs: 200_000_000);
        byte[] wrongIdentity = Wire(
            100,
            0,
            sessionId: Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
        byte[] wrongConfiguration = Wire(
            100,
            0,
            configurationRevision: 12);
        byte[] missingLead = Wire(100, 0, channelCount: 11);
        Guid[] duplicateChannels = ChannelIds.ToArray();
        duplicateChannels[^1] = duplicateChannels[0];
        byte[] corrupt = Wire(100, 0);
        corrupt[^1] ^= 1;

        Check.That(
            Reason(() => WaveformRecordArchive.Create(
                shortPlan,
                [wrongIdentity])) ==
                "WaveformRecordArchive.IdentityMismatch" &&
            Reason(() => WaveformRecordArchive.Create(
                shortPlan,
                [wrongConfiguration])) ==
                "WaveformRecordArchive.ConfigurationChanged" &&
            Reason(() => WaveformRecordArchive.Create(
                shortPlan,
                [missingLead])) ==
                "WaveformRecordArchive.ChannelSetIncomplete" &&
            Reason(() => WaveformRecordArchive.Create(
                Plan(
                    endExclusiveSimTimeNs: 200_000_000,
                    channelIds: duplicateChannels),
                [Wire(100, 0)])) ==
                "WaveformRecordArchive.InvalidPlan" &&
            Reason(() => WaveformRecordArchive.Create(
                shortPlan,
                [corrupt])) == "WaveformRecordArchive.InvalidEnvelope",
            "record creation must bind identity, configuration and all leads");
    }

    private static void EveryLeadKeepsOneSampleGridAndScale()
    {
        WaveformRecordArchivePlan plan = Plan(
            endExclusiveSimTimeNs: 400_000_000);
        byte[] first = Wire(100, 0);
        byte[] sampleGap = Wire(
            101,
            200_000_000,
            firstSampleIndex: 101);
        byte[] rateChange = Wire(
            101,
            200_000_000,
            sampleRateNumerator: 250,
            firstSampleIndex: 100);
        byte[] scaleChange = Wire(
            101,
            200_000_000,
            scaleNumerator: 2);

        Check.That(
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, sampleGap])) ==
                "WaveformRecordArchive.ChannelGridChanged" &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, rateChange])) ==
                "WaveformRecordArchive.ChannelGridChanged" &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, scaleChange])) ==
                "WaveformRecordArchive.ChannelGridChanged",
            "all twelve leads must retain one sample frontier, rate and scale");
    }

    private static void DiscontinuousBlocksCannotFormARecord()
    {
        WaveformRecordArchivePlan plan = Plan(
            endExclusiveSimTimeNs: 400_000_000);
        byte[] first = Wire(100, 0);
        byte[] sequenceGap = Wire(102, 200_000_000);
        byte[] timeGap = Wire(101, 400_000_000);

        Check.That(
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, sequenceGap])) ==
                "WaveformRecordArchive.BlockDiscontinuous" &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, timeGap])) ==
                "WaveformRecordArchive.BlockDiscontinuous",
            "missing sequence or source-time slots cannot form a record");
    }

    private static void ArchiveCheckpointAndReadsAreDefensive()
    {
        byte[][] source = StandardWires();
        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            Plan(),
            source);
        byte expected = archive.ReadBlocks()[0].RawEnvelope[0];
        source[0][0] ^= 0xff;
        IReadOnlyList<ArchivedWaveformBlock> firstRead = archive.ReadBlocks();
        firstRead[0].RawEnvelope[0] ^= 0xff;

        WaveformRecordArchiveState checkpoint = archive.CaptureState();
        WaveformRecordArchive restored = WaveformRecordArchive.Restore(checkpoint);
        byte[][] corrupt = checkpoint.RawEnvelopes
            .Select(static wire => wire.ToArray())
            .ToArray();
        corrupt[0][^1] ^= 1;
        WaveformRecordArchiveState corruptState = checkpoint with
        {
            RawEnvelopes = corrupt,
        };

        Check.That(
            archive.ReadBlocks()[0].RawEnvelope[0] == expected &&
            Equivalent(archive.ReadBlocks(), restored.ReadBlocks()) &&
            Reason(() => WaveformRecordArchive.Restore(corruptState)) ==
                "WaveformRecordArchive.InvalidCheckpoint",
            "source, read and checkpoint mutation must not alter archived bytes");
    }

    private static WaveformRecordArchivePlan Plan(
        long startSimTimeNs = 0,
        long endExclusiveSimTimeNs = 10_000_000_000,
        IReadOnlyList<Guid>? channelIds = null) => new(
        "ecg12.standard",
        "record.ecg12-7",
        SweepEpoch: 7,
        SessionId,
        InstanceId,
        TimebaseEpoch: 3,
        EpochAnchorSimTimeNs: 0,
        StreamEpoch: 7,
        ConfigurationRevision: 11,
        startSimTimeNs,
        endExclusiveSimTimeNs,
        channelIds ?? Array.AsReadOnly(ChannelIds.ToArray()));

    private static byte[][] StandardWires() => Enumerable
        .Range(0, 50)
        .Select(index => Wire(
            100 + checked((ulong)index),
            index * 200_000_000L))
        .ToArray();

    private static byte[] Wire(
        ulong sequence,
        long startSimTimeNs,
        Guid? sessionId = null,
        ulong configurationRevision = 11,
        int channelCount = 12,
        uint sampleRateNumerator = 500,
        ulong? firstSampleIndex = null,
        int scaleNumerator = 1)
    {
        int sampleCount = checked((int)(sampleRateNumerator / 5));
        ulong firstIndex = firstSampleIndex ?? checked((ulong)(
            (UInt128)checked((ulong)startSimTimeNs) *
            sampleRateNumerator /
            1_000_000_000U));
        WaveformPlane[] planes = ChannelIds
            .Take(channelCount)
            .Select((channelId, channelIndex) => new WaveformPlane(
                channelId,
                sampleRateNumerator,
                1,
                firstIndex,
                scaleNumerator,
                1,
                0,
                1,
                WaveformQualityEncoding.None,
                Array.AsReadOnly(Enumerable
                    .Repeat(checked((short)(sequence +
                        (ulong)channelIndex)), sampleCount)
                    .ToArray()),
                Array.Empty<WaveformQualityRange>()))
            .ToArray();
        return WaveformEnvelopeCodec.EncodeRaw(new WaveformEnvelope(
            sessionId ?? SessionId,
            InstanceId,
            TimebaseEpoch: 3,
            StreamEpoch: 7,
            sequence,
            configurationRevision,
            startSimTimeNs,
            WaveformBlockAssembler.BlockDurationNs,
            Array.AsReadOnly(planes)));
    }

    private static bool Equivalent(
        IReadOnlyList<ArchivedWaveformBlock> left,
        IReadOnlyList<ArchivedWaveformBlock> right) =>
        left.Count == right.Count &&
        left.Zip(right).All(pair =>
            pair.First.BlockSequence == pair.Second.BlockSequence &&
            pair.First.StartSimTimeNs == pair.Second.StartSimTimeNs &&
            pair.First.ContentSha256 == pair.Second.ContentSha256 &&
            pair.First.RawEnvelope.SequenceEqual(pair.Second.RawEnvelope));

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (WaveformRecordArchiveException exception)
        {
            return exception.ReasonCode;
        }
    }
}
