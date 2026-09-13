// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using System.Xml.Linq;
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static partial class WaveformRecordArchiveSpecifications
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
        new(nameof(WaveformHorizontalUsesFractionalSourceClock), WaveformHorizontalUsesFractionalSourceClock),
        new(nameof(WaveformHorizontalResizePreservesRawEvidence), WaveformHorizontalResizePreservesRawEvidence),
        new(nameof(WaveformHorizontalDenialAndFailurePublishNoGeometry), WaveformHorizontalDenialAndFailurePublishNoGeometry),
        new(nameof(WaveformHorizontalRestoresOwnedCoordinates), WaveformHorizontalRestoresOwnedCoordinates),
        new(nameof(WaveformPageFollowsCurrentPageAndMappedSlot), WaveformPageFollowsCurrentPageAndMappedSlot),
        new(nameof(WaveformPageRechecksSafetyIndependentlyOfCommands), WaveformPageRechecksSafetyIndependentlyOfCommands),
        new(nameof(WaveformPageFailurePreservesStudyEvidence), WaveformPageFailurePreservesStudyEvidence),
        new(nameof(WaveformPageRestoresWithFreshBindingAndCurrentAdmission), WaveformPageRestoresWithFreshBindingAndCurrentAdmission),
        new(nameof(ChannelReadClipsSamplesAndQualityAcrossBlocks), ChannelReadClipsSamplesAndQualityAcrossBlocks),
        new(nameof(ChannelReadUsesExactHalfOpenSampleTimes), ChannelReadUsesExactHalfOpenSampleTimes),
        new(nameof(ChannelReadRejectsWithoutChangingArchive), ChannelReadRejectsWithoutChangingArchive),
        new(nameof(ChannelReadRestoresAndOwnsOutput), ChannelReadRestoresAndOwnsOutput),
        new(nameof(SvgWithdrawRemovesPublicationWithoutChangingEvidence), SvgWithdrawRemovesPublicationWithoutChangingEvidence),
        new(nameof(SvgWithdrawRetainsExplicitGestureRollbackAndRecovery), SvgWithdrawRetainsExplicitGestureRollbackAndRecovery),

        new(nameof(SvgPublicationReportsCoherentAdmissionStatus), SvgPublicationReportsCoherentAdmissionStatus),
        new(nameof(SvgPublicationDistinguishesCancellationFromFailure), SvgPublicationDistinguishesCancellationFromFailure),

        new(nameof(SvgPublicationClearsOnAdmissionLossAndRecovers), SvgPublicationClearsOnAdmissionLossAndRecovers),
        new(nameof(SvgPublicationFailureAndCancellationWithdrawCurrent), SvgPublicationFailureAndCancellationWithdrawCurrent),
        new(nameof(SvgPublicationRefreshKeepsActiveGestureEvidence), SvgPublicationRefreshKeepsActiveGestureEvidence),

        new(nameof(SvgRefreshPublishesCurrentEvidenceAndPermissions), SvgRefreshPublishesCurrentEvidenceAndPermissions),
        new(nameof(SvgRefreshDuringDragKeepsRetainedGesture), SvgRefreshDuringDragKeepsRetainedGesture),
        new(nameof(SvgRefreshFailurePreservesCurrentEvidence), SvgRefreshFailurePreservesCurrentEvidence),

        new(nameof(HiddenSvgRequiresRefreshBeforeEnabledInput), HiddenSvgRequiresRefreshBeforeEnabledInput),
        new(nameof(RestoredDisabledSvgCannotEnableEmptyPlacementWithoutRefresh), RestoredDisabledSvgCannotEnableEmptyPlacementWithoutRefresh),

        new(nameof(SvgClearRemovesOverlayAndSupersedesOldDrag), SvgClearRemovesOverlayAndSupersedesOldDrag),
        new(nameof(SvgClearChecksCurrentSafetyAndPolicy), SvgClearChecksCurrentSafetyAndPolicy),
        new(nameof(SvgClearRestoresEmptyStateWithoutOldPermissions), SvgClearRestoresEmptyStateWithoutOldPermissions),

        new(nameof(SvgDragKeepsActualScaleOffsetThroughRelease), SvgDragKeepsActualScaleOffsetThroughRelease),
        new(nameof(SvgDragRejectsLayoutChangeAndAllowsRollback), SvgDragRejectsLayoutChangeAndAllowsRollback),
        new(nameof(SvgDragRejectsReplacementAndRetriesInvalidRelease), SvgDragRejectsReplacementAndRetriesInvalidRelease),

        new(nameof(SvgHitUsesActualScaleWithoutTranslatingRadius), SvgHitUsesActualScaleWithoutTranslatingRadius),
        new(nameof(SvgHitPreservesAmbiguityAndRejectsStaleEvidence), SvgHitPreservesAmbiguityAndRejectsStaleEvidence),
        new(nameof(SvgHitChecksCurrentPolicyAndInvalidRadius), SvgHitChecksCurrentPolicyAndInvalidRadius),

        new(nameof(SvgPairPlacementCreatesExactRestorableEvidence), SvgPairPlacementCreatesExactRestorableEvidence),
        new(nameof(SvgPairPlacementFailureAllowsRetry), SvgPairPlacementFailureAllowsRetry),
        new(nameof(SvgPairPlacementRechecksSafetyAndPolicy), SvgPairPlacementRechecksSafetyAndPolicy),

        new(nameof(SvgInputRejectsReplacedAndRestoredPairIdentity), SvgInputRejectsReplacedAndRestoredPairIdentity),
        new(nameof(SvgInputRequiresRefreshAfterSuccessButAllowsFailedRetry), SvgInputRequiresRefreshAfterSuccessButAllowsFailedRetry),

        new(nameof(SvgInputUsesRenderedScaleAndCurrentOrigin), SvgInputUsesRenderedScaleAndCurrentOrigin),
        new(nameof(SvgInputRejectsStalePageAndZoom), SvgInputRejectsStalePageAndZoom),
        new(nameof(SvgInputChecksCurrentMeasurementPolicy), SvgInputChecksCurrentMeasurementPolicy),

        new(nameof(ZoomedMovePreservesOtherEndpointAndSupersedesDrag), ZoomedMovePreservesOtherEndpointAndSupersedesDrag),
        new(nameof(ZoomedMoveRejectsCrossingAndCurrentPolicy), ZoomedMoveRejectsCrossingAndCurrentPolicy),
        new(nameof(ZoomedMoveUsesRestoredPageAndScale), ZoomedMoveUsesRestoredPageAndScale),

        new(nameof(ZoomedDragSurvivesEquivalentZoomSet), ZoomedDragSurvivesEquivalentZoomSet),
        new(nameof(ZoomedSvgReportsActualSerializedMapping), ZoomedSvgReportsActualSerializedMapping),
        new(nameof(ZoomedSvgRejectsClippedPageGeometry), ZoomedSvgRejectsClippedPageGeometry),
        new(nameof(ZoomedSvgRejectsRoundedZeroDimensions), ZoomedSvgRejectsRoundedZeroDimensions),

        new(nameof(ZoomedSvgScalesSeparateScreenLayers), ZoomedSvgScalesSeparateScreenLayers),
        new(nameof(ZoomedSvgSuppressesDeniedAndMissingLayers), ZoomedSvgSuppressesDeniedAndMissingLayers),
        new(nameof(ZoomedSvgRestoresAndCancelsWithoutMutation), ZoomedSvgRestoresAndCancelsWithoutMutation),

        new(nameof(ZoomedDragPreservesOffsetAndFinalRelease), ZoomedDragPreservesOffsetAndFinalRelease),
        new(nameof(ZoomedDragRejectsZoomRoundTripAndResize), ZoomedDragRejectsZoomRoundTripAndResize),
        new(nameof(ZoomedDragFailureKeepsPreviewAndCurrentPolicy), ZoomedDragFailureKeepsPreviewAndCurrentPolicy),

        new(nameof(ZoomedHitPreservesScreenRadius), ZoomedHitPreservesScreenRadius),
        new(nameof(ZoomedHitReportsAmbiguityAndRejectsInvalidInputs), ZoomedHitReportsAmbiguityAndRejectsInvalidInputs),
        new(nameof(ZoomedHitUsesRestoredCurrentPolicy), ZoomedHitUsesRestoredCurrentPolicy),

        new(nameof(ZoomedPlacementUsesCurrentExactScale), ZoomedPlacementUsesCurrentExactScale),
        new(nameof(ZoomedPlacementFailurePreservesPair), ZoomedPlacementFailurePreservesPair),
        new(nameof(ZoomedPlacementRestoresAndChecksMeasurementPolicy), ZoomedPlacementRestoresAndChecksMeasurementPolicy),

        new(nameof(ZoomedPoliciesUpdateIndependentGates), ZoomedPoliciesUpdateIndependentGates),
        new(nameof(ZoomedPoliciesRejectPartialUpdates), ZoomedPoliciesRejectPartialUpdates),
        new(nameof(ZoomedPoliciesEnableRestoredSession), ZoomedPoliciesEnableRestoredSession),

        new(nameof(ZoomedDisplayCombinesCurrentSelectionWithoutChangingEvidence), ZoomedDisplayCombinesCurrentSelectionWithoutChangingEvidence),
        new(nameof(ZoomedDisplaySuppressesDeniedContent), ZoomedDisplaySuppressesDeniedContent),
        new(nameof(ZoomedDisplayRestoresAndRejectsInvalidLayout), ZoomedDisplayRestoresAndRejectsInvalidLayout),

        new(nameof(ZoomedSessionRestoresDataAndCurrentPermissions), ZoomedSessionRestoresDataAndCurrentPermissions),
        new(nameof(ZoomedSessionRejectsInvalidComponentsAtomically), ZoomedSessionRejectsInvalidComponentsAtomically),
        new(nameof(ZoomedSessionPreservesFitIntentAndBindingChecks), ZoomedSessionPreservesFitIntentAndBindingChecks),

        new(nameof(PointerReleaseCommitsFinalPositionAndRestores), PointerReleaseCommitsFinalPositionAndRestores),
        new(nameof(PointerReleaseFailurePreservesPreviewForRetry), PointerReleaseFailurePreservesPreviewForRetry),
        new(nameof(PointerReleaseChecksCurrentPolicyAndPage), PointerReleaseChecksCurrentPolicyAndPage),

        new(nameof(PointerDragPreservesGrabOffsetWithoutInitialJump), PointerDragPreservesGrabOffsetWithoutInitialJump),
        new(nameof(PointerDragKeepsFractionalAnchorExact), PointerDragKeepsFractionalAnchorExact),
        new(nameof(PointerDragRejectsInvalidTargetsWithoutLosingPreview), PointerDragRejectsInvalidTargetsWithoutLosingPreview),
        new(nameof(CurrentPageHitBeginsOnlySelectedEndpointDrag), CurrentPageHitBeginsOnlySelectedEndpointDrag),
        new(nameof(CurrentPageHitRejectsMissingAndAmbiguousTargets), CurrentPageHitRejectsMissingAndAmbiguousTargets),
        new(nameof(CurrentPageHitChecksAdmissionBindingAndPolicy), CurrentPageHitChecksAdmissionBindingAndPolicy),
        new(nameof(CurrentPageHitDragRetainsNavigationFence), CurrentPageHitDragRetainsNavigationFence),
        new(nameof(CursorHitTestUsesExactCircularDistance), CursorHitTestUsesExactCircularDistance),
        new(nameof(CursorHitTestReportsAmbiguityAndHiddenEndpoints), CursorHitTestReportsAmbiguityAndHiddenEndpoints),
        new(nameof(CursorHitTestGatesInvalidInputsAndRestores), CursorHitTestGatesInvalidInputsAndRestores),
        new(nameof(StudyCursorSvgRendersExactVisibleMarkersSeparately), StudyCursorSvgRendersExactVisibleMarkersSeparately),
        new(nameof(StudyCursorSvgOmitsHiddenAndLockedMarkers), StudyCursorSvgOmitsHiddenAndLockedMarkers),
        new(nameof(StudyCursorSvgValidatesStylesAndRecovers), StudyCursorSvgValidatesStylesAndRecovers),
        new(nameof(StudyCursorSvgRestoresAndCancelsWithoutMutation), StudyCursorSvgRestoresAndCancelsWithoutMutation),
        new(nameof(StudySvgLayersRenderCurrentAdmittedGrid), StudySvgLayersRenderCurrentAdmittedGrid),
        new(nameof(StudySvgLayersSuppressUnavailableGrid), StudySvgLayersSuppressUnavailableGrid),
        new(nameof(StudySvgLayersRejectStylesWithoutChangingSession), StudySvgLayersRejectStylesWithoutChangingSession),
        new(nameof(StudySvgLayersRestoreAndCancel), StudySvgLayersRestoreAndCancel),
        new(nameof(StudyGridCancellationPreservesSessionAndAllowsRetry), StudyGridCancellationPreservesSessionAndAllowsRetry),
        new(nameof(StudyPaperGridUsesCurrentCalibratedViewport), StudyPaperGridUsesCurrentCalibratedViewport),
        new(nameof(StudyPaperGridSuppressesDarkAndDeniedOutput), StudyPaperGridSuppressesDarkAndDeniedOutput),
        new(nameof(StudyPaperGridFailurePreservesAcceptedState), StudyPaperGridFailurePreservesAcceptedState),
        new(nameof(StudyPaperGridRebuildsAfterSessionRestore), StudyPaperGridRebuildsAfterSessionRestore),
        new(nameof(ThemedPolicyUpdateChangesAllCurrentGates), ThemedPolicyUpdateChangesAllCurrentGates),
        new(nameof(ThemedPolicyUpdateRejectsEveryPartialUpdate), ThemedPolicyUpdateRejectsEveryPartialUpdate),
        new(nameof(ThemedPolicyUpdateWorksAfterSessionRestore), ThemedPolicyUpdateWorksAfterSessionRestore),
        new(nameof(ThemedSessionRestoresCoherentDisplayState), ThemedSessionRestoresCoherentDisplayState),
        new(nameof(ThemedSessionUsesCurrentPermissions), ThemedSessionUsesCurrentPermissions),
        new(nameof(ThemedSessionRejectsInvalidComponentsWithoutMutation), ThemedSessionRejectsInvalidComponentsWithoutMutation),
        new(nameof(StudyThemeSwitchPreservesContentAndGesture), StudyThemeSwitchPreservesContentAndGesture),
        new(nameof(StudyThemeDisplayKeepsCommandPoliciesIndependent), StudyThemeDisplayKeepsCommandPoliciesIndependent),
        new(nameof(StudyThemeDisplayRestoresWithoutBypassingAdmission), StudyThemeDisplayRestoresWithoutBypassingAdmission),
        new(nameof(StudyPolicyUpdatePublishesBothCommandGroups), StudyPolicyUpdatePublishesBothCommandGroups),
        new(nameof(StudyPolicyUpdateRejectsPartialAndForeignChanges), StudyPolicyUpdateRejectsPartialAndForeignChanges),
        new(nameof(StudyPolicyUpdateAppliesToRestoredSelectionAndGesture), StudyPolicyUpdateAppliesToRestoredSelectionAndGesture),
        new(nameof(StudySessionRestoresPageLeadAndCursorValues), StudySessionRestoresPageLeadAndCursorValues),
        new(nameof(StudySessionRestoresEmptyLockedView), StudySessionRestoresEmptyLockedView),
        new(nameof(StudySessionRejectsIncompleteAndForeignState), StudySessionRejectsIncompleteAndForeignState),
        new(nameof(StudySessionUsesCurrentMeasurementPolicy), StudySessionUsesCurrentMeasurementPolicy),
        new(nameof(StudyDragRejectsPageRoundTripBetweenEvents), StudyDragRejectsPageRoundTripBetweenEvents),
        new(nameof(StudyDragSurvivesNoOpAndRejectedNavigation), StudyDragSurvivesNoOpAndRejectedNavigation),
        new(nameof(StudyDragCanRestartAfterPageRoundTripRollback), StudyDragCanRestartAfterPageRoundTripRollback),
        new(nameof(StudyDragCommitsCurrentPagePreview), StudyDragCommitsCurrentPagePreview),
        new(nameof(StudyDragRejectsObservedPageChange), StudyDragRejectsObservedPageChange),
        new(nameof(StudyDragRejectsChangedLead), StudyDragRejectsChangedLead),
        new(nameof(StudyDragSafetyLossAllowsPolicyCheckedRollback), StudyDragSafetyLossAllowsPolicyCheckedRollback),
        new(nameof(MeasurementDragRejectsVerticallyHiddenEndpoints), MeasurementDragRejectsVerticallyHiddenEndpoints),
        new(nameof(MeasurementDragAcceptsExactVerticalEdges), MeasurementDragAcceptsExactVerticalEdges),
        new(nameof(MeasurementHiddenCursorRecoversAfterGainChangeAndRestore), MeasurementHiddenCursorRecoversAfterGainChangeAndRestore),
        new(nameof(CurrentPageEditingUsesNavigationTimeRange), CurrentPageEditingUsesNavigationTimeRange),
        new(nameof(CurrentPageEditingRespectsIndependentPolicies), CurrentPageEditingRespectsIndependentPolicies),
        new(nameof(CurrentPageEditingDenialPreservesSelection), CurrentPageEditingDenialPreservesSelection),
        new(nameof(CurrentPageEditingRecoversAndCheckpointsData), CurrentPageEditingRecoversAndCheckpointsData),
        new(nameof(NavigationCommandDisplayTracksPageBoundaries), NavigationCommandDisplayTracksPageBoundaries),
        new(nameof(NavigationCommandDisplayCannotAuthorizeStaleActions), NavigationCommandDisplayCannotAuthorizeStaleActions),
        new(nameof(NavigationCommandDisplayRestoresCurrentPolicy), NavigationCommandDisplayRestoresCurrentPolicy),
        new(nameof(StudyDisplayIncludesAndSuppressesNavigationCommands), StudyDisplayIncludesAndSuppressesNavigationCommands),
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

    private static void SvgWithdrawRemovesPublicationWithoutChangingEvidence()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgPresentation presentation = new(view, navigation, theme, zoom);
        presentation.Withdraw();
        Check.That(presentation.Current is null && presentation.Publication.Status == CapturedRecordSvgStatus.Withdrawn, "withdraw is safe before first render");
        CapturedRecordSvgInputSession ready = presentation.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        presentation.Withdraw();
        CapturedRecordSvgPublication withdrawn = presentation.Publication;
        presentation.Withdraw();
        Check.That(ReferenceEquals(presentation.Publication, withdrawn) && withdrawn.ReasonCode == "SvgPresentation.Withdrawn" && withdrawn.Input is null &&
            ReferenceEquals(view.Measurement.CurrentPair, pair) && ready.Display.CursorOverlaySvg is not null, "repeated withdrawal is idempotent and does not erase data or retained snapshots");
        CapturedRecordSvgInputSession reopened = presentation.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(presentation.Publication.Status == CapturedRecordSvgStatus.Ready && !ReferenceEquals(reopened, ready), "reopening renders a fresh publication");
    }

    private static void SvgWithdrawRetainsExplicitGestureRollbackAndRecovery()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgPresentation presentation = new(view, navigation, theme, zoom);
        CapturedRecordSvgInputSession input = presentation.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        CapturedRecordSvgDrag drag = input.BeginDrag(false, layout, screen, new(50, 1), new(40, 1), new(0, 1), new(0, 1), new(1, 1));
        CapturedRecordCursorPair preview = drag.PreviewPointer(false, layout, screen, new(75, 1), new(30, 1), new(0, 1), new(0, 1));
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        presentation.Withdraw();
        Check.That(presentation.Current is null && ReferenceEquals(view.Measurement.CurrentPair, preview), "withdraw does not implicitly commit or roll back active preview");
        Check.That(MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.CourseLocked", "retained gesture rollback still checks current policy");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(drag.Cancel().Second.Value == pair.Second.Value && presentation.Current is null, "explicit rollback cannot republish a hidden view");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == pair.Second.Value, "withdrawn session retains restorable evidence");
    }

    private static void SvgPublicationReportsCoherentAdmissionStatus()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgPresentation presentation = new(view, navigation, theme, zoom);
        Check.That(presentation.Publication.Status == CapturedRecordSvgStatus.NotRendered && presentation.Current is null, "initial state explains absent picture");
        CapturedRecordSvgInputSession ready = presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        CapturedRecordSvgPublication snapshot = presentation.Publication;
        Check.That(snapshot.Status == CapturedRecordSvgStatus.Ready && ReferenceEquals(snapshot.Input, ready), "status and input publish in one snapshot");
        try { presentation.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false); }
        catch (Ecg12ViewAdmissionException) { }
        Check.That(presentation.Publication.Status == CapturedRecordSvgStatus.Denied && presentation.Publication.ReasonCode == "Ecg12Admission.SafetyOverlayUnavailable" &&
            presentation.Current is null && snapshot.Status == CapturedRecordSvgStatus.Ready && ReferenceEquals(snapshot.Input, ready), "denial preserves its reason without mutating retained status snapshot");
        presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(presentation.Publication.Status == CapturedRecordSvgStatus.Ready && presentation.Publication.ReasonCode == "SvgPresentation.Ready", "recovery clears old denial reason");
    }

    private static void SvgPublicationDistinguishesCancellationFromFailure()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgPresentation presentation = new(view, navigation, theme, zoom);
        try { presentation.Refresh(true, layout, screen with { PageWidth = 99 }, SvgStudyStyle(), SvgCursorStyle(), false); }
        catch (Ecg12ZoomSelectionException) { }
        Check.That(presentation.Publication.Status == CapturedRecordSvgStatus.Failed && presentation.Publication.ReasonCode == "SvgPresentation.RenderFailed" &&
            presentation.Current is null, "render exception publishes failure with no input");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false, cancellation.Token); }
        catch (OperationCanceledException) { }
        Check.That(presentation.Publication.Status == CapturedRecordSvgStatus.Cancelled && presentation.Publication.ReasonCode == "SvgPresentation.Cancelled" &&
            presentation.Current is null, "requested cancellation is distinct from render failure");
        presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(presentation.Publication.Status == CapturedRecordSvgStatus.Ready && presentation.Current is not null && view.Measurement.CurrentPair is null,
            "retry replaces failure state without inventing measurements");
    }

    private static void SvgPublicationClearsOnAdmissionLossAndRecovers()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgPresentation presentation = new(view, navigation, theme, zoom);
        CapturedRecordSvgInputSession initial = presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(ReferenceEquals(presentation.Current, initial), "successful refresh publishes one coherent display and input slot");
        try
        {
            presentation.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
            throw new InvalidOperationException("denied screen published");
        }
        catch (Ecg12ViewAdmissionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Admission.SafetyOverlayUnavailable", "admission failure reason propagates"); }
        Check.That(presentation.Current is null && ReferenceEquals(view.Measurement.CurrentPair, pair), "admission loss withdraws current display/input without deleting evidence");
        CapturedRecordSvgInputSession recovered = presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(ReferenceEquals(presentation.Current, recovered) && !ReferenceEquals(recovered, initial), "recovered admission publishes a fresh session");
    }

    private static void SvgPublicationFailureAndCancellationWithdrawCurrent()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgPresentation presentation = new(view, navigation, theme, zoom);
        CapturedRecordSvgInputSession initial = presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        try
        {
            presentation.Refresh(true, layout, screen with { PageWidth = 99 }, SvgStudyStyle(), SvgCursorStyle(), false);
            throw new InvalidOperationException("clipped render published");
        }
        catch (Ecg12ZoomSelectionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Zoom.PlotOutsidePage", "render error propagates without partial publication"); }
        Check.That(presentation.Current is null, "failed rendering cannot leave previous current picture");
        presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false, cancellation.Token);
            throw new InvalidOperationException("cancelled render published");
        }
        catch (OperationCanceledException) { }
        Check.That(presentation.Current is null && ReferenceEquals(view.Measurement.CurrentPair, pair) && initial.Display.CursorOverlaySvg is not null,
            "cancel withdraws current slot while retained immutable snapshots and data remain untouched");
    }

    private static void SvgPublicationRefreshKeepsActiveGestureEvidence()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgPresentation presentation = new(view, navigation, theme, zoom);
        CapturedRecordSvgInputSession initial = presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        CapturedRecordSvgDrag drag = initial.BeginDrag(true, layout, screen, new(50, 1), new(40, 1), new(0, 1), new(0, 1), new(1, 1));
        drag.PreviewPointer(true, layout, screen, new(60, 1), new(40, 1), new(0, 1), new(0, 1));
        presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        CapturedRecordCursorPair released = drag.CommitPointer(true, layout, screen, new(75, 1), new(30, 1), new(0, 1), new(0, 1));
        presentation.Refresh(true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(presentation.Current!.HitTest(true, layout, screen, new(75, 1), new(30, 1), new(0, 1), new(0, 1), new(1, 1)) == RecordCursorHits.Second &&
            pair.Second.Value.DataTimeNs == 50_000_000, "publication redraw preserves retained gesture and publishes final evidence");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == released.Second.Value, "published edits remain restorable independently of screen slot");
    }

    private static void SvgRefreshPublishesCurrentEvidenceAndPermissions()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        input.MoveCursor(false, layout, screen, RecordCursorEnd.Second, new(75, 1), new(30, 1), new(0, 1), new(0, 1));
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordSvgInputSession hidden = input.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(hidden.Display.CursorOverlaySvg is null && input.Display.CursorOverlaySvg is not null, "refresh publishes new permission-controlled output without changing old immutable display");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgInputSession current = hidden.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(current.HitTest(false, layout, screen, new(75, 1), new(30, 1), new(0, 1), new(0, 1), new(1, 1)) == RecordCursorHits.Second &&
            pair.Second.Value.DataTimeNs == 50_000_000, "refresh binds edited current evidence and enabled permission");
    }

    private static void SvgRefreshDuringDragKeepsRetainedGesture()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        CapturedRecordSvgDrag drag = input.BeginDrag(false, layout, screen, new(50, 1), new(40, 1), new(0, 1), new(0, 1), new(1, 1));
        drag.PreviewPointer(false, layout, screen, new(60, 1), new(40, 1), new(0, 1), new(0, 1));
        CapturedRecordSvgInputSession preview = input.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(preview.Display.CursorOverlaySvg is not null, "preview redraw returns current overlay");
        CapturedRecordCursorPair released = drag.CommitPointer(false, layout, screen, new(75, 1), new(30, 1), new(0, 1), new(0, 1));
        CapturedRecordSvgInputSession final = preview.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(released.Second.Value == new EcgManualCursor(75_000_000, 1500, 1) &&
            final.HitTest(false, layout, screen, new(75, 1), new(30, 1), new(0, 1), new(0, 1), new(1, 1)) == RecordCursorHits.Second &&
            pair.Second.Value.DataTimeNs == 50_000_000, "redraw leaves active gesture intact and final refresh binds release evidence");
    }

    private static void SvgRefreshFailurePreservesCurrentEvidence()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            input.Refresh(false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false, cancellation.Token);
            throw new InvalidOperationException("cancelled refresh accepted");
        }
        catch (OperationCanceledException) { }
        try
        {
            input.Refresh(false, layout, screen with { PageWidth = 99 }, SvgStudyStyle(), SvgCursorStyle(), false);
            throw new InvalidOperationException("clipped refresh accepted");
        }
        catch (Ecg12ZoomSelectionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Zoom.PlotOutsidePage", "invalid refresh fails before returning replacement"); }
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair) &&
            input.HitTest(false, layout, screen, new(50, 1), new(40, 1), new(0, 1), new(0, 1), new(1, 1)) == RecordCursorHits.Second,
            "failed refresh preserves state and still-current original input");
    }

    private static void HiddenSvgRequiresRefreshBeforeEnabledInput()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            view.Measurement.UpdatePolicy(policy);
            CapturedRecordSvgInputSession hidden = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
            Check.That(hidden.Display.CursorOverlaySvg is null, "disabled measurement picture omits manual markers");
            view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
            Check.That(MeasurementReason(() => hidden.HitTest(false, layout, screen, new(50, 1), new(40, 1), new(0, 1), new(0, 1), new(2, 1))) == "RecordMeasurement.StaleRenderedView" &&
                MeasurementReason(() => hidden.ClearPair(false, layout, screen)) == "RecordMeasurement.StaleRenderedView" &&
                MeasurementReason(() => hidden.BeginDrag(false, layout, screen, new(50, 1), new(40, 1), new(0, 1), new(0, 1), new(2, 1))) == "RecordMeasurement.StaleRenderedView" &&
                ReferenceEquals(view.Measurement.CurrentPair, pair), "enabling policy alone cannot activate invisible targets or clear from old picture");
        }
        CapturedRecordSvgInputSession refreshed = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(refreshed.Display.CursorOverlaySvg is not null && refreshed.HitTest(false, layout, screen,
            new(50, 1), new(40, 1), new(0, 1), new(0, 1), new(2, 1)) == RecordCursorHits.Second, "fresh enabled picture permits visible cursor input");
    }

    private static void RestoredDisabledSvgCannotEnableEmptyPlacementWithoutRefresh()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(view.CaptureZoomedSession(navigation, theme, zoom),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.Enabled, true, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgInputSession hidden = new(restored.Content.Study.View, restored.Content.Study.Navigation, restored.Content.Theme,
            restored.Zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(MeasurementReason(() => hidden.ClearPair(false, layout, screen)) == "RecordMeasurement.Disabled", "still-disabled picture uses current permission rejection");
        restored.Content.Study.View.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(MeasurementReason(() => hidden.PlacePair(false, layout, screen, new(0, 1), new(60, 1), new(50, 1), new(40, 1), new(0, 1), new(0, 1))) ==
            "RecordMeasurement.StaleRenderedView" && restored.Content.Study.View.Measurement.CurrentPair is null, "restored disabled empty display cannot create cursors after policy change without redraw");
        CapturedRecordSvgInputSession refreshed = new(restored.Content.Study.View, restored.Content.Study.Navigation, restored.Content.Theme,
            restored.Zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(refreshed.PlacePair(false, layout, screen, new(0, 1), new(60, 1), new(50, 1), new(40, 1), new(0, 1), new(0, 1)).Second.Value.DataTimeNs == 50_000_000,
            "fresh enabled empty picture accepts first placement");
    }

    private static void SvgClearRemovesOverlayAndSupersedesOldDrag()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        CapturedRecordSvgDrag drag = input.BeginDrag(true, layout, screen, new(50, 1), new(40, 1), new(0, 1), new(0, 1), new(2, 1));
        input.ClearPair(true, layout, screen);
        Check.That(view.Measurement.CurrentPair is null && MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragSuperseded",
            "clear removes active evidence and old drag cannot restore deleted cursors");
        Check.That(MeasurementReason(() => input.ClearPair(true, layout, screen)) == "RecordMeasurement.StaleRenderedView",
            "old populated display cannot submit another clear after deletion");
        CapturedRecordSvgInputSession refreshed = new(view, navigation, theme, zoom, true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(refreshed.Display.CursorOverlaySvg is null && pair.Second.Value.DataTimeNs == 50_000_000, "fresh display omits overlay while old immutable evidence stays intact");
        refreshed.ClearPair(true, layout, screen);
        Check.That(view.Measurement.CurrentPair is null, "clearing an already empty current picture is harmless");
    }

    private static void SvgClearChecksCurrentSafetyAndPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        try
        {
            input.ClearPair(false, layout, screen);
            throw new InvalidOperationException("unsafe clear accepted");
        }
        catch (Ecg12ViewAdmissionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Admission.SafetyOverlayUnavailable", "clear checks current safety admission"); }
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => input.ClearPair(true, layout, screen)) == "RecordMeasurement.CourseLocked" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "denied clear preserves active pair and old input remains nonmutating");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        input.ClearPair(true, layout, screen);
        Check.That(view.Measurement.CurrentPair is null, "valid retry after current permission recovery succeeds");
    }

    private static void SvgClearRestoresEmptyStateWithoutOldPermissions()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        input.ClearPair(true, layout, screen);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(view.CaptureZoomedSession(navigation, theme, zoom),
            Ecg12RecordContext.ActiveInstance, SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.Disabled, false, SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(restored.Content.Study.View.Measurement.CurrentPair is null && pair.First.Value.DataTimeNs == 0, "empty selection survives restoration without mutating prior evidence");
        CapturedRecordSvgInputSession current = new(restored.Content.Study.View, restored.Content.Study.Navigation, restored.Content.Theme, restored.Zoom,
            true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(MeasurementReason(() => current.ClearPair(true, layout, screen)) == "RecordMeasurement.Disabled", "even empty clear requires current measurement permission after restore");
    }

    private static void SvgDragKeepsActualScaleOffsetThroughRelease()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        ExactPlotCoordinate ox = new(-7, 3), oy = new(11, 7);
        CapturedRecordSvgDrag drag = input.BeginDrag(false, layout, screen, actual.ForwardAt(new(52, 1), ox),
            actual.ForwardAt(new(42, 1), oy), ox, oy, actual.Forward(new(3, 1)));
        Check.That(drag.PreviewPointer(false, layout, screen, actual.ForwardAt(new(52, 1), ox), actual.ForwardAt(new(42, 1), oy), ox, oy).Second.Value == pair.Second.Value,
            "actual SVG edge press preserves grab offset without jumping");
        drag.PreviewPointer(false, layout, screen, actual.ForwardAt(new(62, 1), ox), actual.ForwardAt(new(42, 1), oy), ox, oy);
        CapturedRecordCursorPair released = drag.CommitPointer(false, layout, screen, actual.ForwardAt(new(77, 1), ox), actual.ForwardAt(new(32, 1), oy), ox, oy);
        Check.That(released.Second.Value == new EcgManualCursor(75_000_000, 1500, 1) && MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragFinished",
            "successive preview and final release use actual mapping and close gesture");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == released.Second.Value, "SVG release retains restorable data evidence");
    }

    private static void SvgDragRejectsLayoutChangeAndAllowsRollback()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        ExactPlotCoordinate ox = new(-7, 3), oy = new(11, 7);
        CapturedRecordSvgDrag drag = input.BeginDrag(false, layout, screen, actual.ForwardAt(new(52, 1), ox),
            actual.ForwardAt(new(42, 1), oy), ox, oy, actual.Forward(new(3, 1)));
        drag.PreviewPointer(false, layout, screen, actual.ForwardAt(new(62, 1), ox), actual.ForwardAt(new(42, 1), oy), ox, oy);
        Check.That(MeasurementReason(() => drag.CommitPointer(false, layout, screen with { AvailableWidth = 99 }, new(0, 1), new(0, 1), ox, oy)) == "RecordMeasurement.StaleRenderedView" &&
            MeasurementReason(() => drag.PreviewPointer(false, layout, screen, new(0, 1), new(0, 1), ox, oy)) == "RecordMeasurement.StaleRenderedView",
            "observed rendered layout mismatch remains latched");
        Check.That(drag.Cancel().Second.Value == pair.Second.Value, "stale geometry does not prevent policy-checked rollback");
    }

    private static void SvgDragRejectsReplacementAndRetriesInvalidRelease()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        ExactPlotCoordinate ox = new(-7, 3), oy = new(11, 7);
        CapturedRecordSvgDrag drag = input.BeginDrag(false, layout, screen, actual.ForwardAt(new(52, 1), ox),
            actual.ForwardAt(new(42, 1), oy), ox, oy, actual.Forward(new(3, 1)));
        CapturedRecordCursorPair accepted = drag.PreviewPointer(false, layout, screen, actual.ForwardAt(new(62, 1), ox), actual.ForwardAt(new(42, 1), oy), ox, oy);
        Check.That(MeasurementReason(() => drag.CommitPointer(false, layout, screen, actual.ForwardAt(new(102, 1), ox), actual.ForwardAt(new(32, 1), oy), ox, oy)) == "RecordMeasurement.InvalidPoint" &&
            ReferenceEquals(view.Measurement.CurrentPair, accepted), "invalid adjusted release target preserves prior preview");
        drag.PreviewPointer(false, layout, screen, actual.ForwardAt(new(72, 1), ox), actual.ForwardAt(new(42, 1), oy), ox, oy);
        CapturedRecordCursorPair replacement = view.Measurement.ReplacePair(pair.First.Value, pair.Second.Value);
        Check.That(MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragSuperseded" && ReferenceEquals(view.Measurement.CurrentPair, replacement),
            "external replacement cannot be overwritten by old SVG gesture");
    }

    private static void SvgHitUsesActualScaleWithoutTranslatingRadius()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        ExactPlotCoordinate ox = new(-7, 3), oy = new(11, 7);
        Check.That(input.HitTest(false, layout, screen, actual.ForwardAt(new(53, 1), ox), actual.ForwardAt(new(44, 1), oy),
            ox, oy, actual.Forward(new(5, 1))) == RecordCursorHits.Second, "translated screen point and scaled radius preserve exact inclusive circle boundary");
        Check.That(input.HitTest(false, layout, screen, actual.ForwardAt(new(53, 1), ox), actual.ForwardAt(new(44, 1), oy),
            ox, oy, actual.Forward(new(49, 10))) == RecordCursorHits.None && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "radius is scaled but never translated and query does not consume current picture");
    }

    private static void SvgHitPreservesAmbiguityAndRejectsStaleEvidence()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        view.Measurement.ReplacePair(pair.Second.Value, pair.Second.Value);
        Check.That(MeasurementReason(() => input.HitTest(false, layout, screen, actual.Forward(new(50, 1)), actual.Forward(new(40, 1)),
            new(0, 1), new(0, 1), new(1, 1))) == "RecordMeasurement.StaleRenderedView", "hit testing rejects obsolete cursor picture after replacement");
        CapturedRecordSvgInputSession fresh = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(fresh.HitTest(false, layout, screen, actual.Forward(new(50, 1)), actual.Forward(new(40, 1)),
            new(0, 1), new(0, 1), new(1, 1)) == (RecordCursorHits.First | RecordCursorHits.Second), "fresh overlapping endpoints retain ambiguity without tie breaking");
    }

    private static void SvgHitChecksCurrentPolicyAndInvalidRadius()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        Check.That(MeasurementReason(() => input.HitTest(false, layout, screen, actual.Forward(new(50, 1)), actual.Forward(new(40, 1)),
            new(0, 1), new(0, 1), new(1, 0))) == "RecordMeasurement.InvalidHitRadius", "malformed screen radius rejects before inverse conversion");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => input.HitTest(false, layout, screen, actual.Forward(new(50, 1)), actual.Forward(new(40, 1)),
            new(0, 1), new(0, 1), new(1, 1))) == "RecordMeasurement.CourseLocked" && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "current measurement policy suppresses hit results from formerly enabled picture");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(input.HitTest(false, layout, screen, actual.Forward(new(50, 1)), actual.Forward(new(40, 1)),
            new(0, 1), new(0, 1), new(1, 1)) == RecordCursorHits.Second, "nonmutating rejection keeps current evidence queryable after policy recovery");
    }

    private static void SvgPairPlacementCreatesExactRestorableEvidence()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        ExactPlotCoordinate ox = new(-7, 3), oy = new(11, 7);
        CapturedRecordCursorPair pair = input.PlacePair(true, layout, screen,
            actual.ForwardAt(new(0, 1), ox), actual.ForwardAt(new(60, 1), oy),
            actual.ForwardAt(new(75, 1), ox), actual.ForwardAt(new(30, 1), oy), ox, oy);
        Check.That(pair.First.Value == new EcgManualCursor(100_000_000, 0, 1) && pair.Second.Value == new EcgManualCursor(175_000_000, 1500, 1),
            "empty SVG picture accepts exact pair through actual scale and current window origin");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.First.Value == pair.First.Value && restored.Second.Value == pair.Second.Value,
            "created pair persists as data evidence rather than screen coordinates");
        Check.That(MeasurementReason(() => input.PlacePair(true, layout, screen, new(0, 1), new(0, 1), new(1, 1), new(1, 1), ox, oy)) ==
            "RecordMeasurement.StaleRenderedView", "successful pair creation invalidates old empty picture");
    }

    private static void SvgPairPlacementFailureAllowsRetry()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        ExactPlotCoordinate ox = new(-7, 3), oy = new(11, 7);
        Check.That(MeasurementReason(() => input.PlacePair(true, layout, screen,
            actual.ForwardAt(new(0, 1), ox), actual.ForwardAt(new(60, 1), oy),
            actual.ForwardAt(new(100, 1), ox), actual.ForwardAt(new(30, 1), oy), ox, oy)) == "RecordMeasurement.InvalidPoint" &&
            view.Measurement.CurrentPair is null, "invalid second endpoint publishes no first endpoint");
        CapturedRecordCursorPair pair = input.PlacePair(true, layout, screen,
            actual.ForwardAt(new(0, 1), ox), actual.ForwardAt(new(60, 1), oy),
            actual.ForwardAt(new(50, 1), ox), actual.ForwardAt(new(40, 1), oy), ox, oy);
        Check.That(pair.Second.Value == new EcgManualCursor(150_000_000, 1000, 1), "nonmutating failure leaves empty picture available for valid retry");
    }

    private static void SvgPairPlacementRechecksSafetyAndPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, true, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        ExactPlotCoordinate ox = new(-7, 3), oy = new(11, 7);
        try
        {
            input.PlacePair(false, layout, screen, actual.ForwardAt(new(0, 1), ox), actual.ForwardAt(new(60, 1), oy),
                actual.ForwardAt(new(50, 1), ox), actual.ForwardAt(new(40, 1), oy), ox, oy);
            throw new InvalidOperationException("missing safety overlay accepted");
        }
        catch (Ecg12ViewAdmissionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Admission.SafetyOverlayUnavailable", "creation rechecks current overlay admission"); }
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(MeasurementReason(() => input.PlacePair(true, layout, screen,
            actual.ForwardAt(new(0, 1), ox), actual.ForwardAt(new(60, 1), oy),
            actual.ForwardAt(new(50, 1), ox), actual.ForwardAt(new(40, 1), oy), ox, oy)) == "RecordMeasurement.Disabled" &&
            view.Measurement.CurrentPair is null, "stale enabled picture cannot authorize creation after policy disable");
    }

    private static void SvgInputRejectsReplacedAndRestoredPairIdentity()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        view.Measurement.ClearPair();
        CapturedRecordCursorPair replacement = view.Measurement.ReplacePair(pair.First.Value, pair.Second.Value);
        Check.That(MeasurementReason(() => input.MoveCursor(false, layout, screen, RecordCursorEnd.Second,
            new(75, 1), new(30, 1), new(0, 1), new(0, 1))) == "RecordMeasurement.StaleRenderedView" &&
            ReferenceEquals(view.Measurement.CurrentPair, replacement), "clear and equal-value replacement cannot make an old picture current again");
        CapturedRecordSvgInputSession refreshed = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        CapturedRecordCursorPair moved = refreshed.MoveCursor(false, layout, screen, RecordCursorEnd.Second,
            new(75, 1), new(30, 1), new(0, 1), new(0, 1));
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == moved.Second.Value && moved.Second.Value.DataTimeNs == 75_000_000, "fresh render accepts replacement and publishes restorable evidence");
    }

    private static void SvgInputRequiresRefreshAfterSuccessButAllowsFailedRetry()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(100, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(MeasurementReason(() => input.MoveCursor(false, layout, screen, RecordCursorEnd.Second,
            new(100, 1), new(30, 1), new(0, 1), new(0, 1))) == "RecordMeasurement.InvalidPoint" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "failed input preserves rendered pair and does not consume session");
        CapturedRecordCursorPair accepted = input.MoveCursor(false, layout, screen, RecordCursorEnd.Second,
            new(75, 1), new(30, 1), new(0, 1), new(0, 1));
        Check.That(MeasurementReason(() => input.MoveCursor(false, layout, screen, RecordCursorEnd.Second,
            new(80, 1), new(30, 1), new(0, 1), new(0, 1))) == "RecordMeasurement.StaleRenderedView" &&
            ReferenceEquals(view.Measurement.CurrentPair, accepted), "successful edit requires a new displayed snapshot before another command");
    }

    private static void SvgInputUsesRenderedScaleAndCurrentOrigin()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        ExactPlotCoordinate originX = new(-17, 7), originY = new(23, 11);
        CapturedRecordCursorPair moved = input.MoveCursor(false, layout, screen, RecordCursorEnd.Second,
            actual.ForwardAt(new(75, 1), originX), actual.ForwardAt(new(30, 1), originY), originX, originY);
        Check.That(moved.Second.Value == new EcgManualCursor(75_000_000, 1500, 1) && ReferenceEquals(moved.First, pair.First),
            "rendered input reverses serialized scale and current scrolled origin without double zoom");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == moved.Second.Value, "rendered input publishes restorable data evidence");
    }

    private static void SvgInputRejectsStalePageAndZoom()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        navigation.NextPage();
        navigation.PreviousPage();
        Check.That(MeasurementReason(() => input.MoveCursor(false, layout, screen, RecordCursorEnd.Second, new(25, 1), new(10, 1), new(0, 1), new(0, 1))) ==
            "RecordMeasurement.StaleRenderedView" && ReferenceEquals(view.Measurement.CurrentPair, pair), "page round trip rejects old rendered input before mutation");
        CapturedRecordSvgInputSession current = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1));
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 1, 3));
        Check.That(MeasurementReason(() => current.MoveCursor(false, layout, screen, RecordCursorEnd.Second, new(25, 1), new(10, 1), new(0, 1), new(0, 1))) ==
            "RecordMeasurement.StaleRenderedView" && ReferenceEquals(view.Measurement.CurrentPair, pair), "zoom round trip cannot reuse old SVG mapping");
    }

    private static void SvgInputChecksCurrentMeasurementPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordSvgLayout layout = SvgStudyLayout() with { PlotLeftPixels = 0, PlotWidthPixels = 100 };
        RecordScreenZoomLayout screen = new(101, 100, 100, 100);
        CapturedRecordSvgInputSession input = new(view, navigation, theme, zoom, false, layout, screen, SvgStudyStyle(), SvgCursorStyle(), false);
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Ecg12ScreenTransform actual = input.Display.RenderedTransform!;
        Check.That(MeasurementReason(() => input.MoveCursor(false, layout, screen, RecordCursorEnd.Second,
            actual.Forward(new(75, 1)), actual.Forward(new(30, 1)), new(0, 1), new(0, 1))) == "RecordMeasurement.CourseLocked" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "rendered enabled state cannot authorize input after policy lock");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(MeasurementReason(() => input.MoveCursor(false, layout, screen with { AvailableWidth = 99 }, RecordCursorEnd.Second,
            new(25, 1), new(10, 1), new(0, 1), new(0, 1))) == "RecordMeasurement.StaleRenderedView" &&
            MeasurementReason(() => input.MoveCursor(false, layout, screen, RecordCursorEnd.Second, new(25, 1), new(10, 1), new(0, 1), new(0, 1))) == "RecordMeasurement.StaleRenderedView",
            "observed layout mismatch stays stale after original size returns");
    }

    private static void ZoomedMovePreservesOtherEndpointAndSupersedesDrag()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(100_000_000, 0, 1), new(150_000_000, 1000, 1));
        CapturedRecordZoomedDrag drag = view.BeginCursorDragOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout, new(100, 1), new(80, 1), new(2, 1));
        zoom.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordCursorPair moved = view.MoveCursorOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            RecordCursorEnd.Second, new(150, 1), new(60, 1));
        Check.That(ReferenceEquals(moved.First, pair.First) && moved.Second.Value == new EcgManualCursor(175_000_000, 1500, 1),
            "single endpoint moves under current scale independently of zoom selection lock");
        Check.That(MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragSuperseded" && ReferenceEquals(view.Measurement.CurrentPair, moved),
            "old drag cannot overwrite a newer direct move");
    }

    private static void ZoomedMoveRejectsCrossingAndCurrentPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(100_000_000, 0, 1), new(150_000_000, 1000, 1));
        try
        {
            view.MoveCursorOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout, RecordCursorEnd.First, new(150, 1), new(60, 1));
            throw new InvalidOperationException("crossing accepted");
        }
        catch (EcgManualMeasurementException exception)
        { Check.That(exception.ReasonCode == "ManualMeasurement.TimeReversed", "direct move cannot swap cursor order"); }
        Check.That(MeasurementReason(() => view.MoveCursorOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            RecordCursorEnd.Second, new(200, 1), new(60, 1))) == "RecordMeasurement.InvalidPoint" && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "crossing and exclusive right edge leave both endpoints unchanged");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(MeasurementReason(() => view.MoveCursorOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            RecordCursorEnd.Second, new(150, 1), new(60, 1))) == "RecordMeasurement.Disabled" && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "current measurement policy blocks direct input");
    }

    private static void ZoomedMoveUsesRestoredPageAndScale()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(100_000_000, 0, 1), new(150_000_000, 1000, 1));
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(view.CaptureZoomedSession(navigation, theme, zoom),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.CourseLocked,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true, SystemViewCommandAssessmentPolicy.Enabled);
        restored.Zoom.Select(new(Ecg12ZoomMode.FitPage, 1, 1));
        CapturedRecordCursorPair moved = restored.Content.Study.View.MoveCursorOnZoomedPage(restored.Content.Study.Navigation,
            restored.Zoom, false, 0, 100, scale, layout, RecordCursorEnd.Second, new(75, 2), new(15, 1));
        Check.That(moved.Second.Value == new EcgManualCursor(175_000_000, 1500, 1) && pair.Second.Value.DataTimeNs == 150_000_000,
            "restored current page and changed fit scale determine exact data without changing original session");
    }

    private static void ZoomedDragSurvivesEquivalentZoomSet()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 100, 100);
        view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordZoomedDrag drag = view.BeginCursorDragOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(104, 1), new(84, 1), new(6, 1));
        CapturedRecordCursorPair accepted = drag.PreviewPointer(false, 0, 100, scale, layout, new(124, 1), new(84, 1));
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 4, 2));
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, accepted), "repeated zoom set does not change preview evidence");
        CapturedRecordCursorPair released = drag.CommitPointer(false, 0, 100, scale, layout, new(154, 1), new(64, 1));
        Check.That(released.Second.Value == new EcgManualCursor(75_000_000, 1500, 1), "equivalent scale set preserves anchored drag through release");
    }

    private static void ZoomedSvgReportsActualSerializedMapping()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 1, 3), SystemViewCommandAssessmentPolicy.Enabled);
        view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        ZoomedCapturedRecordSvgScreenLayers rendered = CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, true,
            SvgStudyLayout(), new(101, 100, 100, 100), SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(rendered.Transform!.Factor == new ExactPlotCoordinate(1, 3) &&
            rendered.RenderedTransform!.Factor == new ExactPlotCoordinate(33_333_333, 100_000_000),
            "actual meet mapping uses smaller serialized height ratio rather than original exact intent");
        ExactPlotCoordinate screenX = rendered.RenderedTransform!.Forward(new(15, 1));
        Check.That(rendered.RenderedTransform.Inverse(screenX) == new ExactPlotCoordinate(15, 1), "input inverse matches serialized SVG mapping");
    }

    private static void ZoomedSvgRejectsClippedPageGeometry()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        foreach (RecordScreenZoomLayout screen in new RecordScreenZoomLayout[] { new(19, 100, 100, 100), new(20, 99, 100, 100) })
        {
            try
            {
                CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, true, SvgStudyLayout(), screen,
                    SvgStudyStyle(), SvgCursorStyle(), false);
                throw new InvalidOperationException("clipped page accepted");
            }
            catch (Ecg12ZoomSelectionException exception)
            { Check.That(exception.ReasonCode == "Ecg12Zoom.PlotOutsidePage", "page must contain full logical plot on both axes"); }
        }
        ZoomedCapturedRecordSvgScreenLayers exact = CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, true,
            SvgStudyLayout(), new(20, 100, 100, 100), SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(exact.GridSvg is not null && ReferenceEquals(view.Measurement.CurrentPair, pair), "exact page edge fits and rejected rendering preserves session");
    }

    private static void ZoomedSvgRejectsRoundedZeroDimensions()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 1, uint.MaxValue));
        try
        {
            CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, true, SvgStudyLayout(), new(100, 100, 100, 100),
                SvgStudyStyle(), SvgCursorStyle(), false);
            throw new InvalidOperationException("zero rounded SVG size accepted");
        }
        catch (Ecg12ZoomSelectionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Zoom.UnrepresentableSvgSize", "positive exact size cannot serialize to invisible zero viewport"); }
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair), "failed render does not alter exact measurement evidence");
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 1, 100_000_000));
        Ecg12ThemeSelection restoredTheme = Ecg12ThemeSelection.Restore(theme.CaptureState(), SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection restoredZoom = Ecg12ZoomSelection.Restore(zoom.CaptureState(), SystemViewCommandAssessmentPolicy.Disabled);
        ZoomedCapturedRecordSvgScreenLayers smallest = CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, restoredTheme, restoredZoom, true,
            SvgStudyLayout(), new(100, 100, 100, 100), SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That((string?)XElement.Parse(smallest.GridSvg!).Attribute("width") == "0.000001", "representable tiny viewport remains accepted after zoom restore");
    }

    private static void ZoomedSvgScalesSeparateScreenLayers()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        RecordScreenZoomLayout screen = new(100, 100, 50, 50);
        ZoomedCapturedRecordSvgScreenLayers rendered = CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, true,
            SvgStudyLayout(), screen, SvgStudyStyle(), SvgCursorStyle(), false);
        XElement grid = XElement.Parse(rendered.GridSvg!);
        XElement cursors = XElement.Parse(rendered.CursorOverlaySvg!);
        Check.That((string?)grid.Attribute("width") == "150" && (string?)grid.Attribute("height") == "150" &&
            (string?)grid.Attribute("viewBox") == "0 0 100 100" && (string?)cursors.Attribute("width") == "150",
            "grid and cursor layers share a single outer page viewport scale");
        Check.That(!rendered.GridSvg!.Contains("data-cursor", StringComparison.Ordinal) && rendered.CursorOverlaySvg!.Contains("data-cursor", StringComparison.Ordinal) &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "manual overlay stays separate and rendering preserves exact evidence");
        zoom.Select(new(Ecg12ZoomMode.FitPage, 1, 1));
        ZoomedCapturedRecordSvgScreenLayers fit = CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, true,
            SvgStudyLayout(), screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That((string?)XElement.Parse(fit.GridSvg!).Attribute("width") == "50" && fit.Transform!.Factor == new ExactPlotCoordinate(1, 2),
            "fresh fit selection drives actual SVG viewport dimensions");
    }

    private static void ZoomedSvgSuppressesDeniedAndMissingLayers()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        RecordScreenZoomLayout screen = new(100, 100, 50, 50);
        ZoomedCapturedRecordSvgScreenLayers denied = CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, false,
            SvgStudyLayout(), new(0, 0, 0, 0), SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(denied.GridSvg is null && denied.CursorOverlaySvg is null && denied.Zoom is null && denied.Transform is null,
            "admission denial suppresses scaled output before unused screen validation");
        theme.Select(Ecg12Theme.MonitorDarkGreen);
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        ZoomedCapturedRecordSvgScreenLayers hidden = CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, true,
            SvgStudyLayout(), screen, SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(hidden.GridSvg is null && hidden.CursorOverlaySvg is null && hidden.Transform is not null && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "missing dark grid and locked overlay do not produce empty SVG layers");
    }

    private static void ZoomedSvgRestoresAndCancelsWithoutMutation()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        RecordScreenZoomLayout screen = new(100, 100, 50, 50);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(view.CaptureZoomedSession(navigation, theme, zoom),
            Ecg12RecordContext.ActiveInstance, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled,
            SystemViewCommandAssessmentPolicy.Enabled, true, SystemViewCommandAssessmentPolicy.Disabled);
        ZoomedCapturedRecordSvgScreenLayers rendered = CapturedRecordSvgLayers.RenderZoomedScreen(restored.Content.Study.View,
            restored.Content.Study.Navigation, restored.Content.Theme, restored.Zoom, true, SvgStudyLayout(), screen,
            SvgStudyStyle(), SvgCursorStyle(), false);
        Check.That(!rendered.Zoom!.CanSelect && rendered.Transform!.Factor == new ExactPlotCoordinate(3, 2), "restored selected zoom renders under current disabled selection permission");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom, true, SvgStudyLayout(), screen,
                SvgStudyStyle(), SvgCursorStyle(), false, cancellation.Token);
            throw new InvalidOperationException("cancelled rendering succeeded");
        }
        catch (OperationCanceledException) { }
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair), "cancelled scaled render publishes no result and preserves session");
    }

    private static void ZoomedDragPreservesOffsetAndFinalRelease()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 100, 100);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordZoomedDrag drag = view.BeginCursorDragOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(104, 1), new(84, 1), new(6, 1));
        Check.That(drag.PreviewPointer(false, 0, 100, scale, layout, new(104, 1), new(84, 1)).Second.Value == pair.Second.Value, "scaled edge press does not jump cursor");
        CapturedRecordCursorPair released = drag.CommitPointer(false, 0, 100, scale, layout, new(154, 1), new(64, 1));
        Check.That(released.Second.Value == new EcgManualCursor(75_000_000, 1500, 1) &&
            MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragFinished", "final screen position uses grab offset and closes gesture");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == released.Second.Value, "only final data evidence survives restore");
    }

    private static void ZoomedDragRejectsZoomRoundTripAndResize()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 100, 100);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordZoomedDrag drag = view.BeginCursorDragOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(104, 1), new(84, 1), new(6, 1));
        zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1));
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 2, 1));
        Check.That(MeasurementReason(() => drag.CommitPointer(false, 0, 100, scale, layout, new(154, 1), new(64, 1))) == "RecordMeasurement.DragLayoutChanged" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "zoom round trip between events invalidates original selection identity");
        drag.Cancel();
        CapturedRecordZoomedDrag resized = view.BeginCursorDragOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout, new(104, 1), new(84, 1), new(6, 1));
        Check.That(MeasurementReason(() => resized.PreviewPointer(false, 0, 100, scale, layout with { AvailableWidth = 99 }, new(104, 1), new(84, 1))) == "RecordMeasurement.DragLayoutChanged" &&
            MeasurementReason(() => resized.PreviewPointer(false, 0, 100, scale, layout, new(104, 1), new(84, 1))) == "RecordMeasurement.DragLayoutChanged",
            "observed size mismatch stays latched after returning to original layout");
        Check.That(resized.Cancel().Second.Value == pair.Second.Value, "layout rejection allows original-value rollback");
    }

    private static void ZoomedDragFailureKeepsPreviewAndCurrentPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 100, 100);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordZoomedDrag drag = view.BeginCursorDragOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(104, 1), new(84, 1), new(6, 1));
        CapturedRecordCursorPair accepted = drag.PreviewPointer(false, 0, 100, scale, layout, new(124, 1), new(84, 1));
        Check.That(MeasurementReason(() => drag.CommitPointer(false, 0, 100, scale, layout, new(204, 1), new(64, 1))) == "RecordMeasurement.InvalidPoint" &&
            ReferenceEquals(view.Measurement.CurrentPair, accepted), "invalid final adjusted target preserves accepted preview");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => drag.CommitPointer(false, 0, 100, scale, layout, new(154, 1), new(64, 1))) == "RecordMeasurement.CourseLocked",
            "current measurement lock rejects release");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        zoom.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(drag.Cancel().Second.Value == pair.Second.Value, "zoom permission update alone does not invalidate measurement rollback");
    }

    private static void ZoomedHitPreservesScreenRadius()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        Check.That(view.HitTestOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout, new(103, 1), new(84, 1), new(5, 1)) == RecordCursorHits.Second,
            "screen-space 3-4-5 radius boundary is inclusive after zoom inversion");
        Check.That(view.HitTestOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout, new(103, 1), new(84, 1), new(49, 10)) == RecordCursorHits.None,
            "radius is transformed with coordinates rather than growing with zoom");
        zoom.Select(new(Ecg12ZoomMode.FitPage, 1, 1));
        Check.That(view.HitTestOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout, new(28, 1), new(24, 1), new(5, 1)) == RecordCursorHits.Second &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "current fit scale preserves screen tolerance and never changes evidence");
    }

    private static void ZoomedHitReportsAmbiguityAndRejectsInvalidInputs()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordCursorPair overlap = view.Measurement.ReplacePair(new(50_000_000, 1000, 1), new(50_000_000, 1000, 1));
        Check.That(view.HitTestOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout, new(100, 1), new(80, 1), new(1, 1)) ==
            (RecordCursorHits.First | RecordCursorHits.Second), "overlapping scaled markers retain ambiguity");
        Check.That(MeasurementReason(() => view.HitTestOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(100, 1), new(80, 1), new(0, 1))) == "RecordMeasurement.InvalidHitRadius" &&
            MeasurementReason(() => view.HitTestOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(200, 1), new(80, 1), new(1, 1))) == "RecordMeasurement.InvalidPoint" &&
            ReferenceEquals(view.Measurement.CurrentPair, overlap), "invalid radius and exclusive scaled right edge preserve pair");
        Check.That(pair.Second.Value.DataTimeNs == 50_000_000, "old immutable cursor evidence remains unchanged");
    }

    private static void ZoomedHitUsesRestoredCurrentPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(view.CaptureZoomedSession(navigation, theme, zoom),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.CourseLocked,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true, SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(restored.Content.Study.View.HitTestOnZoomedPage(restored.Content.Study.Navigation, restored.Zoom, false, 0, 100, scale, layout,
            new(100, 1), new(80, 1), new(1, 1)) == RecordCursorHits.Second, "zoom and pagination locks do not block enabled hit testing");
        restored.Content.Study.View.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => restored.Content.Study.View.HitTestOnZoomedPage(restored.Content.Study.Navigation, restored.Zoom,
            false, 0, 100, scale, layout, new(100, 1), new(80, 1), new(1, 1))) == "RecordMeasurement.CourseLocked" &&
            restored.Content.Study.View.Measurement.CurrentPair!.Second.Value == pair.Second.Value,
            "current measurement lock suppresses hit results without changing restored evidence");
    }

    private static void ZoomedPlacementUsesCurrentExactScale()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        zoom.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordCursorPair pair = view.PlacePairOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(0, 1), new(90, 1), new(75, 1), new(60, 1));
        Check.That(pair.First.Value == new EcgManualCursor(100_000_000, 0, 1) && pair.Second.Value == new EcgManualCursor(150_000_000, 1000, 1),
            "locked zoom selection does not block independently enabled measurement at current exact scale");
        zoom.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        zoom.Select(new(Ecg12ZoomMode.FitPage, 1, 1));
        CapturedRecordCursorPair fit = view.PlacePairOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(0, 1), new(30, 1), new(25, 1), new(20, 1));
        Check.That(fit.Second.Value == pair.Second.Value, "placement resolves current fit geometry rather than stale explicit zoom");
    }

    private static void ZoomedPlacementFailurePreservesPair()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        CapturedRecordCursorPair original = view.Measurement.ReplacePair(new(100_000_000, 0, 1), new(150_000_000, 1000, 1));
        Check.That(MeasurementReason(() => view.PlacePairOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(15, 1), new(90, 1), new(150, 1), new(60, 1))) == "RecordMeasurement.InvalidPoint" &&
            ReferenceEquals(view.Measurement.CurrentPair, original), "invalid second endpoint at exclusive right edge does not publish first endpoint");
        Check.That(MeasurementReason(() => view.PlacePairOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(0, 1), new(90, 1), new(1, 1), new(60, 1))) == "RecordMeasurement.UnrepresentableTime" &&
            ReferenceEquals(view.Measurement.CurrentPair, original), "inverse scale never rounds fractional nanoseconds");
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectPaginationReason(() => view.PlacePairOnZoomedPage(foreign, zoom, false, 0, 100, scale, layout,
            new(0, 1), new(90, 1), new(75, 1), new(60, 1)), "RecordPagination.ForeignNavigation");
    }

    private static void ZoomedPlacementRestoresAndChecksMeasurementPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        RecordScreenZoomLayout layout = new(100, 100, 50, 50);
        CapturedRecordCursorPair pair = view.PlacePairOnZoomedPage(navigation, zoom, false, 0, 100, scale, layout,
            new(0, 1), new(90, 1), new(75, 1), new(60, 1));
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(view.CaptureZoomedSession(navigation, theme, zoom),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.CourseLocked,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true, SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordCursorPair next = restored.Content.Study.View.PlacePairOnZoomedPage(restored.Content.Study.Navigation, restored.Zoom,
            false, 0, 100, scale, layout, new(0, 1), new(90, 1), new(225, 2), new(45, 1));
        Check.That(next.Second.Value == new EcgManualCursor(175_000_000, 1500, 1) && pair.Second.Value.DataTimeNs == 150_000_000,
            "restored zoom maps fractional screen coordinates independently of pagination and zoom locks");
        restored.Content.Study.View.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(MeasurementReason(() => restored.Content.Study.View.PlacePairOnZoomedPage(restored.Content.Study.Navigation, restored.Zoom,
            false, 0, 100, scale, layout, new(0, 1), new(90, 1), new(75, 1), new(60, 1))) == "RecordMeasurement.Disabled" &&
            ReferenceEquals(restored.Content.Study.View.Measurement.CurrentPair, next), "current measurement policy rejects stale placement without mutation");
    }

    private static void ZoomedPoliciesUpdateIndependentGates()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        view.UpdateZoomedCommandPolicies(navigation, theme, zoom, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Enabled, false,
            SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(navigation.CurrentPolicy == SystemViewCommandAssessmentPolicy.Disabled && view.Measurement.CurrentPolicy == SystemViewCommandAssessmentPolicy.CourseLocked &&
            !theme.CaptureDisplay().CanSelect && zoom.CaptureDisplay().Policy == SystemViewCommandAssessmentPolicy.CourseLocked &&
            ReferenceEquals(view.Measurement.CurrentPair, pair) && zoom.Selection == new Ecg12ZoomState(Ecg12ZoomMode.ExplicitScale, 3, 2),
            "grouped policy changes preserve evidence and independently govern all four commands");
        try { zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1)); throw new InvalidOperationException("locked zoom accepted"); }
        catch (Ecg12ZoomSelectionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Zoom.CourseLocked", "current grouped policy blocks stale zoom action"); }
    }

    private static void ZoomedPoliciesRejectPartialUpdates()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        for (int invalidIndex = 0; invalidIndex < 4; invalidIndex++)
        {
            SystemViewCommandAssessmentPolicy[] policies = [SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.Disabled,
                SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.Disabled];
            policies[invalidIndex] = (SystemViewCommandAssessmentPolicy)99;
            ExpectPaginationReason(() => view.UpdateZoomedCommandPolicies(navigation, theme, zoom, policies[0], policies[1], policies[2], false, policies[3]), "RecordStudy.InvalidPolicy");
            Check.That(navigation.CurrentPolicy == SystemViewCommandAssessmentPolicy.Enabled && view.Measurement.CurrentPolicy == SystemViewCommandAssessmentPolicy.Enabled &&
                theme.CaptureDisplay().CanSelect && zoom.CaptureDisplay().CanSelect, "each invalid policy leaves every group and local theme permission unchanged");
        }
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectPaginationReason(() => view.UpdateZoomedCommandPolicies(foreign, theme, zoom, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.Disabled, false, SystemViewCommandAssessmentPolicy.Disabled), "RecordPagination.ForeignNavigation");
        Check.That(foreign.CurrentPolicy == SystemViewCommandAssessmentPolicy.Enabled && theme.CaptureDisplay().CanSelect && zoom.CaptureDisplay().CanSelect,
            "foreign navigation cannot partially change supplied selectors");
    }

    private static void ZoomedPoliciesEnableRestoredSession()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(view.CaptureZoomedSession(navigation, theme, zoom),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.Disabled, false, SystemViewCommandAssessmentPolicy.Disabled);
        restored.Content.Study.View.UpdateZoomedCommandPolicies(restored.Content.Study.Navigation, restored.Content.Theme, restored.Zoom,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true,
            SystemViewCommandAssessmentPolicy.Enabled);
        restored.Content.Study.Navigation.NextPage();
        restored.Content.Theme.Select(Ecg12Theme.MonitorDarkGreen);
        restored.Zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1));
        restored.Content.Study.View.Measurement.ReplacePair(new(100_000_000, 0, 1), new(150_000_000, 1000, 1));
        Check.That(restored.Content.Study.Navigation.CurrentPage.PageIndex == 1 && restored.Zoom.Selection.Mode == Ecg12ZoomMode.ActualSize &&
            navigation.CurrentPage.PageIndex == 0 && zoom.Selection.Mode == Ecg12ZoomMode.ExplicitScale && view.Measurement.CurrentPair is null,
            "trusted update enables restored commands without affecting original session");
    }

    private static void ZoomedDisplayCombinesCurrentSelectionWithoutChangingEvidence()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        ZoomedCapturedRecordPageDisplay first = view.CaptureZoomedPageDisplay(navigation, theme, zoom, true, 0, 100, scale, new(100, 100, 200, 200), false);
        zoom.Select(new(Ecg12ZoomMode.FitPage, 1, 1));
        zoom.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        ZoomedCapturedRecordPageDisplay current = view.CaptureZoomedPageDisplay(navigation, theme, zoom, true, 0, 100, scale, new(100, 100, 50, 75), false);
        Check.That(first.Transform!.Factor == new ExactPlotCoordinate(3, 2) && current.Transform!.Factor == new ExactPlotCoordinate(1, 2) &&
            current.Zoom!.Policy == SystemViewCommandAssessmentPolicy.CourseLocked && !current.Zoom.CanSelect,
            "display snapshots retain their exact transform and current selection gate");
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair) && current.Content.Content.Study.Measurement!.Second!.X.WholePixels == 50 &&
            current.Transform!.Forward(new(50, 1)) == new ExactPlotCoordinate(25, 1), "content remains logical and is transformed exactly once by renderer");
    }

    private static void ZoomedDisplaySuppressesDeniedContent()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        ZoomedCapturedRecordPageDisplay denied = view.CaptureZoomedPageDisplay(navigation, theme, zoom, false, 0, 100, scale, new(0, 0, 0, 0), false);
        Check.That(!denied.Content.Content.Study.Admission.MayEnter && denied.Zoom is null && denied.Transform is null &&
            denied.Content.Theme is null && denied.Content.Content.Study.Record is null && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "lost safety admission suppresses all display content before unused zoom geometry is evaluated");
        ZoomedCapturedRecordPageDisplay resumed = view.CaptureZoomedPageDisplay(navigation, theme, zoom, true, 0, 100, scale, new(100, 100, 100, 100), false);
        Check.That(resumed.Transform is not null && resumed.Content.Content.Study.Measurement!.Second is not null, "fresh admission restores current display without losing evidence");
    }

    private static void ZoomedDisplayRestoresAndRejectsInvalidLayout()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        try
        {
            view.CaptureZoomedPageDisplay(navigation, theme, zoom, true, 0, 100, scale, new(100, 100, 0, 100), false);
            throw new InvalidOperationException("invalid display geometry accepted");
        }
        catch (Ecg12ZoomSelectionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Zoom.InvalidGeometry", "admitted invalid geometry rejects whole display"); }
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair), "failed capture does not mutate measurement");
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(view.CaptureZoomedSession(navigation, theme, zoom),
            Ecg12RecordContext.ActiveInstance, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled,
            SystemViewCommandAssessmentPolicy.Enabled, true, SystemViewCommandAssessmentPolicy.Disabled);
        ZoomedCapturedRecordPageDisplay display = restored.Content.Study.View.CaptureZoomedPageDisplay(restored.Content.Study.Navigation,
            restored.Content.Theme, restored.Zoom, true, 0, 100, scale, new(100, 100, 75, 75), false);
        Check.That(display.Transform!.Factor == new ExactPlotCoordinate(3, 2) && !display.Zoom!.CanSelect &&
            restored.Content.Study.View.Measurement.CurrentPair!.Second.Value == pair.Second.Value, "restore displays saved explicit scale under current disabled selection policy");
    }

    private static void ZoomedSessionRestoresDataAndCurrentPermissions()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 6, 4), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(100_000_000, 1000, 1), new(150_000_000, 1500, 1));
        ZoomedCapturedRecordStudySessionState state = view.CaptureZoomedSession(navigation, theme, zoom);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(state,
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.CourseLocked,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Disabled, false,
            SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(restored.Zoom.Selection == new Ecg12ZoomState(Ecg12ZoomMode.ExplicitScale, 3, 2) && !restored.Zoom.CaptureDisplay().CanSelect &&
            !restored.Content.Theme.CaptureDisplay().CanSelect && !restored.Content.Study.Navigation.CaptureDisplay().Select.IsEnabled,
            "restored independent command permissions come from current inputs");
        Check.That(restored.Content.Study.Navigation.CurrentPage.PageIndex == 1 && restored.Content.Study.View.Measurement.Slot.SlotId == "ecg.slot1" &&
            restored.Content.Study.View.Measurement.CurrentPair!.Second.Value == pair.Second.Value &&
            !ReferenceEquals(restored.Content.Study.View.Measurement.CurrentPair!.Second, pair.Second),
            "zoom restore retains page, lead and data coordinates with fresh cursor ownership");
        zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1));
        Check.That(state.Zoom.Numerator == 3 && restored.Zoom.Selection.Numerator == 3, "later source zoom changes cannot mutate checkpoint or restored state");
    }

    private static void ZoomedSessionRejectsInvalidComponentsAtomically()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 6, 4), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(100_000_000, 1000, 1), new(150_000_000, 1500, 1));
        ZoomedCapturedRecordStudySessionState state = view.CaptureZoomedSession(navigation, theme, zoom);
        try
        {
            CapturedRecordStudySession.RestoreZoomed(state with { Zoom = new(Ecg12ZoomMode.ExplicitScale, 1, 0) },
                Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Enabled,
                SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true, SystemViewCommandAssessmentPolicy.Enabled);
            throw new InvalidOperationException("malformed zoom restored");
        }
        catch (Ecg12ZoomSelectionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Zoom.InvalidSelection", "invalid zoom checkpoint rejects"); }
        ExpectPaginationReason(() => CapturedRecordStudySession.RestoreZoomed(state with
        { Content = state.Content with { Study = state.Content.Study with { SlotId = "missing" } } },
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Enabled,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true,
            SystemViewCommandAssessmentPolicy.Enabled), "RecordStudy.InvalidCheckpoint");
        Check.That(MeasurementReason(() => CapturedRecordStudySession.RestoreZoomed(state,
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Enabled,
            SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Enabled, true,
            SystemViewCommandAssessmentPolicy.Enabled)) == "RecordMeasurement.CourseLocked" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair) && zoom.Selection == state.Zoom && theme.Theme == state.Content.Theme.Theme,
            "failure in later components leaves source session untouched and cannot bypass measurement lock");
    }

    private static void ZoomedSessionPreservesFitIntentAndBindingChecks()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 6, 4), SystemViewCommandAssessmentPolicy.Enabled);
        zoom.Select(new(Ecg12ZoomMode.FitPage, 1, 1));
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        ZoomedCapturedRecordStudySessionState state = view.CaptureZoomedSession(navigation, theme, zoom);
        RestoredZoomedRecordStudySession restored = CapturedRecordStudySession.RestoreZoomed(state,
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.Disabled, false,
            SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(restored.Zoom.Selection.Mode == Ecg12ZoomMode.FitPage && restored.Content.Study.View.Measurement.CurrentPair is null,
            "empty locked session restores fit intent without storing stale pixel geometry");
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectPaginationReason(() => view.CaptureZoomedSession(foreign, theme, zoom), "RecordPagination.ForeignNavigation");
    }

    private static void PointerReleaseCommitsFinalPositionAndRestores()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(52, 1), new(42, 1), new(3, 1));
        drag.PreviewPointer(false, 0, 100, scale, new(62, 1), new(42, 1));
        CapturedRecordCursorPair released = drag.CommitPointer(false, 0, 100, scale, new(77, 1), new(32, 1));
        Check.That(released.Second.Value == new EcgManualCursor(75_000_000, 1500, 1), "release uses its own final position and original grab offset");
        Check.That(MeasurementReason(() => drag.CommitPointer(false, 0, 100, scale, new(82, 1), new(32, 1))) == "RecordMeasurement.DragFinished" &&
            MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragFinished" && ReferenceEquals(view.Measurement.CurrentPair, released),
            "release closes gesture and cannot be replayed or rolled back");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == released.Second.Value, "checkpoint retains final release evidence");
    }

    private static void PointerReleaseFailurePreservesPreviewForRetry()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(52, 1), new(42, 1), new(3, 1));
        CapturedRecordCursorPair accepted = drag.PreviewPointer(false, 0, 100, scale, new(62, 1), new(42, 1));
        Check.That(MeasurementReason(() => drag.CommitPointer(false, 0, 100, scale, new(102, 1), new(32, 1))) == "RecordMeasurement.InvalidPoint" &&
            MeasurementReason(() => drag.CommitPointer(false, 0, 100, scale, new(217, 3), new(32, 1))) == "RecordMeasurement.UnrepresentableTime" &&
            ReferenceEquals(view.Measurement.CurrentPair, accepted), "exclusive right edge and fractional nanosecond reject before mutation or completion");
        Check.That(drag.CommitPointer(false, 0, 100, scale, new(77, 1), new(32, 1)).Second.Value.DataTimeNs == 75_000_000,
            "failed release leaves gesture available for valid retry");
    }

    private static void PointerReleaseChecksCurrentPolicyAndPage()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(52, 1), new(42, 1), new(3, 1));
        CapturedRecordCursorPair accepted = drag.PreviewPointer(false, 0, 100, scale, new(62, 1), new(42, 1));
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => drag.CommitPointer(false, 0, 100, scale, new(77, 1), new(32, 1))) == "RecordMeasurement.CourseLocked" &&
            ReferenceEquals(view.Measurement.CurrentPair, accepted), "release rechecks current policy before moving endpoint");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        navigation.NextPage();
        navigation.PreviousPage();
        Check.That(MeasurementReason(() => drag.CommitPointer(false, 0, 100, scale, new(77, 1), new(32, 1))) == "RecordMeasurement.DragLayoutChanged" &&
            ReferenceEquals(view.Measurement.CurrentPair, accepted), "page round trip invalidates release before mutation");
        Check.That(drag.Cancel().Second.Value == new EcgManualCursor(50_000_000, 1000, 1), "rejected release still permits original-value rollback");
    }

    private static void PointerDragPreservesGrabOffsetWithoutInitialJump()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(52, 1), new(42, 1), new(3, 1));
        Check.That(drag.PreviewPointer(false, 0, 100, scale, new(52, 1), new(42, 1)).Second.Value == pair.Second.Value,
            "stationary pointer at marker edge does not jump cursor center");
        CapturedRecordCursorPair moved = drag.PreviewPointer(false, 0, 100, scale, new(77, 1), new(32, 1));
        Check.That(moved.Second.Value == new EcgManualCursor(75_000_000, 1500, 1) && ReferenceEquals(drag.Commit(false, 0, 100, scale), moved),
            "pointer displacement translates endpoint by the same exact displacement");
    }

    private static void PointerDragKeepsFractionalAnchorExact()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(151, 3), new(161, 4), new(1, 1));
        CapturedRecordCursorPair moved = drag.PreviewPointer(false, 0, 100, scale, new(226, 3), new(121, 4));
        Check.That(moved.Second.Value == new EcgManualCursor(75_000_000, 1500, 1), "fractional grab offset cancels exactly before time/amplitude conversion");
        drag.Commit(false, 0, 100, scale);
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == moved.Second.Value, "only resulting data values are checkpointed, not pointer anchors");
    }

    private static void PointerDragRejectsInvalidTargetsWithoutLosingPreview()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair original = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag direct = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        Check.That(MeasurementReason(() => direct.PreviewPointer(false, 0, 100, scale, new(50, 1), new(40, 1))) == "RecordMeasurement.NoPointerAnchor",
            "target-coordinate gestures cannot infer a missing pointer anchor");
        CapturedRecordStudyDrag drag = view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(52, 1), new(42, 1), new(3, 1));
        CapturedRecordCursorPair accepted = drag.PreviewPointer(false, 0, 100, scale, new(77, 1), new(32, 1));
        Check.That(MeasurementReason(() => drag.PreviewPointer(false, 0, 100, scale, new(102, 1), new(32, 1))) == "RecordMeasurement.InvalidPoint" &&
            MeasurementReason(() => drag.PreviewPointer(false, 0, 100, scale, new(1, 0), new(32, 1))) == "RecordMeasurement.InvalidPoint" &&
            ReferenceEquals(view.Measurement.CurrentPair, accepted), "invalid adjusted target and pointer fraction preserve prior preview");
        Check.That(drag.Cancel().Second.Value == original.Second.Value, "failed pointer move still permits exact rollback");
    }

    private static void CurrentPageHitBeginsOnlySelectedEndpointDrag()
    {
        CapturedRecordNavigation original = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = CapturedRecordNavigation.Restore(original.CaptureState(), SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        Check.That(view.HitTestOnCurrentPage(navigation, false, 0, 100, scale, new(50, 1), new(40, 1), new(2, 1)) == RecordCursorHits.Second,
            "hit query uses the restored current page independently of pagination lock");
        CapturedRecordStudyDrag drag = view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(50, 1), new(40, 1), new(2, 1));
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair), "pointer-down does not move cursor evidence");
        drag.Preview(false, 0, 100, scale, new(75, 1), new(30, 1));
        CapturedRecordCursorPair committed = drag.Commit(false, 0, 100, scale);
        Check.That(ReferenceEquals(committed.First, pair.First) && committed.Second.Value == new EcgManualCursor(175_000_000, 1500, 1),
            "unique hit selects the second endpoint for the existing drag lifecycle");
    }

    private static void CurrentPageHitRejectsMissingAndAmbiguousTargets()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(MeasurementReason(() => view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(2, 1))) == "RecordMeasurement.NoCursorHit",
            "empty pair starts no gesture");
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(0, 0, 1));
        Check.That(MeasurementReason(() => view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(2, 1))) == "RecordMeasurement.AmbiguousCursorHit" &&
            MeasurementReason(() => view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(50, 1), new(60, 1), new(2, 1))) == "RecordMeasurement.NoCursorHit" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "ambiguous and missed targets preserve selection without tie breaking");
    }

    private static void CurrentPageHitChecksAdmissionBindingAndPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 0, 1));
        try
        {
            view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(2, 1));
            throw new InvalidOperationException("denied hit gesture accepted");
        }
        catch (Ecg12ViewAdmissionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Admission.SafetyOverlayUnavailable", "pointer-down requires current safety admission"); }
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectPaginationReason(() => view.HitTestOnCurrentPage(foreign, true, 0, 100, scale, new(0, 1), new(60, 1), new(2, 1)), "RecordPagination.ForeignNavigation");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => view.BeginCursorDragAtPoint(navigation, true, 0, 100, scale, new(0, 1), new(60, 1), new(2, 1))) == "RecordMeasurement.CourseLocked" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "course-locked input cannot disclose/select targets or change values");
    }

    private static void CurrentPageHitDragRetainsNavigationFence()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDragAtPoint(navigation, false, 0, 100, scale, new(50, 1), new(40, 1), new(2, 1));
        drag.Preview(false, 0, 100, scale, new(75, 1), new(30, 1));
        navigation.NextPage();
        navigation.PreviousPage();
        Check.That(MeasurementReason(() => drag.Commit(false, 0, 100, scale)) == "RecordMeasurement.DragLayoutChanged" && drag.Cancel().Second.Value == pair.Second.Value,
            "pointer-selected gesture retains page round-trip rejection and exact rollback");
    }

    private static void CursorHitTestUsesExactCircularDistance()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair pair = measurement.CurrentPair!;
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(measurement.HitTestCursors(new(3, 1), new(64, 1), new(5, 1), page, scale) == RecordCursorHits.First &&
            measurement.HitTestCursors(new(3, 1), new(64_001, 1000), new(5, 1), page, scale) == RecordCursorHits.None,
            "exact closed circular boundary accepts 3-4-5 distance but rejects fractional excursion");
        Check.That(measurement.HitTestCursors(new(151, 3), new(60, 1), new(1, 2), page, scale) == RecordCursorHits.Second &&
            ReferenceEquals(measurement.CurrentPair, pair), "hit testing accepts subpixel coordinates without inverse-time quantization or mutation");
    }

    private static void CursorHitTestReportsAmbiguityAndHiddenEndpoints()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        measurement.ReplacePair(new(0, 0, 1), new(0, 0, 1));
        Check.That(measurement.HitTestCursors(new(0, 1), new(60, 1), new(1, 1), page, scale) == (RecordCursorHits.First | RecordCursorHits.Second),
            "coincident endpoints report both hits without implicit tie breaking");
        measurement.ReplacePair(new(0, 4000, 1), new(100_000_000, 0, 1));
        Check.That(measurement.HitTestCursors(new(0, 1), new(0, 1), new(100, 1), new(0, 100_000_000, 0, 100), scale) == RecordCursorHits.None,
            "large radius cannot select vertically hidden or off-page endpoints");
        measurement.ClearPair();
        Check.That(measurement.HitTestCursors(new(0, 1), new(60, 1), new(1, 1), page, scale) == RecordCursorHits.None, "empty selection has no hit targets");
    }

    private static void CursorHitTestGatesInvalidInputsAndRestores()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair pair = measurement.CurrentPair!;
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        Check.That(MeasurementReason(() => measurement.HitTestCursors(new(0, 1), new(60, 1), new(0, 1), page, scale)) == "RecordMeasurement.InvalidHitRadius" &&
            MeasurementReason(() => measurement.HitTestCursors(new(100, 1), new(60, 1), new(1, 1), page, scale)) == "RecordMeasurement.InvalidPoint" &&
            ReferenceEquals(measurement.CurrentPair, pair), "invalid radius and exclusive right-edge pointer leave data unchanged");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Measurement.HitTestCursors(new(50, 1), new(60, 1), new(1, 1), page, scale) == RecordCursorHits.Second, "fresh ownership rebuilds the same geometric hit");
        measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => measurement.HitTestCursors(new(0, 1), new(60, 1), new(1, 1), page, scale)) == "RecordMeasurement.CourseLocked",
            "locked measurements expose no endpoint hits");
    }

    private static EcgManualCursorSvgStyle SvgCursorStyle() => new("#0055ff", "#ff5500", 1000, 2000);

    private static void StudyCursorSvgRendersExactVisibleMarkersSeparately()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(50_000_000, 0, 1), new(100_000_000, 1000, 1));
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        CapturedRecordSvgScreenLayers result = CapturedRecordSvgLayers.RenderScreen(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true);
        XElement root = XElement.Parse(result.CursorOverlaySvg!);
        XElement[] groups = root.Elements().ToArray();
        Check.That(groups.Length == 2 && (string?)groups[0].Attribute("data-cursor") == "first" &&
            (string?)groups[0].Elements().Last().Attribute("cx") == "12.5" && (string?)groups[1].Elements().Last().Attribute("cy") == "40" &&
            (string?)root.Attribute("overflow") == "hidden", "screen markers use exact projected positions and clipped crosshairs");
        Check.That(!result.Content.GridSvg!.Contains("manual-measurement", StringComparison.Ordinal) && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "manual overlay remains separate from grid SVG and changes no data");
    }

    private static void StudyCursorSvgOmitsHiddenAndLockedMarkers()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        view.Measurement.ReplacePair(new(0, 4000, 1), new(100_000_000, 0, 1));
        Check.That(CapturedRecordSvgLayers.RenderScreen(view, navigation, theme, true, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true).CursorOverlaySvg is null,
            "above-plot first cursor and next-page second cursor emit no markers");
        navigation.NextPage();
        CapturedRecordSvgScreenLayers one = CapturedRecordSvgLayers.RenderScreen(view, navigation, theme, true, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true);
        Check.That(XElement.Parse(one.CursorOverlaySvg!).Elements().Count() == 1 && one.Content.GridSvg is null,
            "dark theme can display the one visible manual cursor without a paper grid");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(CapturedRecordSvgLayers.RenderScreen(view, navigation, theme, true, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true).CursorOverlaySvg is null &&
            CapturedRecordSvgLayers.RenderScreen(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true).CursorOverlaySvg is null,
            "locked or denied displays disclose no cursor overlay");
    }

    private static void StudyCursorSvgValidatesStylesAndRecovers()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 3000, 1), new(100_000_000, -2000, 1));
        foreach (EcgManualCursorSvgStyle invalid in new[] { SvgCursorStyle() with { RadiusMilliPixels = 0 }, SvgCursorStyle() with { FirstColor = "url(#external)" } })
        {
            Check.That(MeasurementReason(() => CapturedRecordSvgLayers.RenderScreen(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), invalid, true)) ==
                "RecordMeasurement.InvalidSvgStyle", "visible markers reject invalid styles before returning screen layers");
        }
        CapturedRecordSvgScreenLayers recovered = CapturedRecordSvgLayers.RenderScreen(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true);
        Check.That(XElement.Parse(recovered.CursorOverlaySvg!).Elements().Count() == 2 && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "closed vertical edges render after failure without modifying selected evidence");
    }

    private static void StudyCursorSvgRestoresAndCancelsWithoutMutation()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        string? before = CapturedRecordSvgLayers.RenderScreen(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true).CursorOverlaySvg;
        RestoredThemedRecordStudySession restored = CapturedRecordStudySession.RestoreThemed(view.CaptureThemedSession(navigation, theme), Ecg12RecordContext.IndependentCapturedRecord,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            CapturedRecordSvgLayers.RenderScreen(restored.Study.View, restored.Study.Navigation, restored.Theme, false, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true, cancellation.Token);
            throw new InvalidOperationException("cancelled cursor rendering accepted");
        }
        catch (OperationCanceledException exception)
        { Check.That(exception.CancellationToken == cancellation.Token, "screen cancellation preserves caller token"); }
        Check.That(CapturedRecordSvgLayers.RenderScreen(restored.Study.View, restored.Study.Navigation, restored.Theme, false, SvgStudyLayout(), SvgStudyStyle(), SvgCursorStyle(), true).CursorOverlaySvg == before,
            "restored manual coordinates rebuild identical screen-only markers after cancellation");
    }

    private static CapturedRecordSvgLayout SvgStudyLayout() => new(10, 10, new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 10, 60, 55);
    private static EcgPaperGridSvgStyle SvgStudyStyle() => new("#f0cccc", "#cc9999", 500, 1000);

    private static void StudySvgLayersRenderCurrentAdmittedGrid()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        CapturedRecordSvgLayersResult result = CapturedRecordSvgLayers.Render(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), true);
        Check.That(result.GridSvg == EcgPaperGridSvg.Render(result.Display.GridPlan!, 55, SvgStudyStyle()) && result.Display.GridLines.Count == 55 &&
            result.Display.Content.Content.Page == navigation.CurrentPage, "fresh current page geometry and rendered grid are published together without rebuilding the grid");
    }

    private static void StudySvgLayersSuppressUnavailableGrid()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        CapturedRecordSvgLayersResult denied = CapturedRecordSvgLayers.Render(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), false);
        Check.That(denied.GridSvg is null && denied.Display.Content.Content.Study.Record is null, "denied current admission emits no SVG");
        theme.Select(Ecg12Theme.MonitorDarkGreen);
        CapturedRecordSvgLayersResult dark = CapturedRecordSvgLayers.Render(view, navigation, theme, true, SvgStudyLayout(), SvgStudyStyle() with { MinorColor = "invalid" }, false);
        Check.That(dark.GridSvg is null && dark.Display.Content.Content.Study.Record is not null, "dark display omits grid and unused grid style");
    }

    private static void StudySvgLayersRejectStylesWithoutChangingSession()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.CourseLocked, false);
        try
        {
            CapturedRecordSvgLayers.Render(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle() with { MajorStrokeMilliPixels = 0 }, true);
            throw new InvalidOperationException("invalid current grid style accepted");
        }
        catch (EcgPaperGridException exception)
        { Check.That(exception.ReasonCode == "PaperGrid.InvalidSvgStyle", "fresh layer rendering validates styles"); }
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair) && CapturedRecordSvgLayers.Render(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), true).GridSvg is not null,
            "render failure changes no selection and permits fresh retry");
    }

    private static void StudySvgLayersRestoreAndCancel()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.CourseLocked, false);
        RestoredThemedRecordStudySession restored = CapturedRecordStudySession.RestoreThemed(view.CaptureThemedSession(navigation, theme),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked, false);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            CapturedRecordSvgLayers.Render(restored.Study.View, restored.Study.Navigation, restored.Theme, false, SvgStudyLayout(), SvgStudyStyle(), false, cancellation.Token);
            throw new InvalidOperationException("cancelled layers accepted");
        }
        catch (OperationCanceledException exception)
        { Check.That(exception.CancellationToken == cancellation.Token, "layer rendering forwards caller cancellation"); }
        Check.That(CapturedRecordSvgLayers.Render(view, navigation, theme, false, SvgStudyLayout(), SvgStudyStyle(), false).GridSvg ==
            CapturedRecordSvgLayers.Render(restored.Study.View, restored.Study.Navigation, restored.Theme, false, SvgStudyLayout(), SvgStudyStyle(), false).GridSvg,
            "restored current state produces equivalent SVG after cancellation");
    }

    private static void StudyGridCancellationPreservesSessionAndAllowsRetry()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        foreach (Ecg12Theme selected in Enum.GetValues<Ecg12Theme>())
        {
            theme.Select(selected);
            foreach (bool admitted in new[] { false, true })
            {
                try
                {
                    view.CaptureGridPageDisplay(navigation, theme, admitted, 0, 10, new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 0, 60, 55, true, cancellation.Token);
                    throw new InvalidOperationException("cancelled study composition accepted");
                }
                catch (OperationCanceledException exception)
                { Check.That(exception.CancellationToken == cancellation.Token, "cancellation applies to paper, dark and denied composition"); }
            }
        }
        theme.Select(Ecg12Theme.PaperGridBlack);
        GridCapturedRecordPageDisplay recovered = view.CaptureGridPageDisplay(navigation, theme, true, 0, 10,
            new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 0, 60, 55, true);
        Check.That(recovered.GridLines.Count == 55 && ReferenceEquals(view.Measurement.CurrentPair, pair) && navigation.CurrentPage.PageIndex == 0,
            "fresh composition succeeds after cancellation without losing session evidence");
    }

    private static void StudyPaperGridUsesCurrentCalibratedViewport()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        GridCapturedRecordPageDisplay display = view.CaptureGridPageDisplay(navigation, theme, false, 10, 10,
            new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 10, 60, 55, true);
        Check.That(display.GridPlan!.MinorSpacingNumerator == 2 && display.GridLines.Count == 55 &&
            display.GridLines[0] == new EcgPaperGridLine(true, new(10, 1), true) &&
            display.Content.Content.Viewport!.PlotLeftPixels == display.GridPlan.LeftPixels && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "paper theme composes calibrated current viewport geometry with unchanged cursor evidence");
        Check.That(((ICollection<EcgPaperGridLine>)display.GridLines).IsReadOnly, "published grid remains immutable");
    }

    private static void StudyPaperGridSuppressesDarkAndDeniedOutput()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        GridCapturedRecordPageDisplay dark = view.CaptureGridPageDisplay(navigation, theme, true, 10, 10,
            new(0, 100, 60, 20, 1), new(0, 0, 0, 0), 10, 60, 0, false);
        Check.That(dark.GridPlan is null && dark.GridLines.Count == 0 && dark.Content.Theme!.Theme == Ecg12Theme.MonitorDarkGreen,
            "dark theme omits grid and does not consume unused paper-only inputs");
        theme.Select(Ecg12Theme.PaperGridBlack);
        GridCapturedRecordPageDisplay denied = view.CaptureGridPageDisplay(navigation, theme, false, 0, 0,
            new(0, 100, 60, 20, 1), new(0, 0, 0, 0), 0, 0, 0, false);
        Check.That(denied.GridPlan is null && denied.GridLines.Count == 0 && denied.Content.Theme is null && denied.Content.Content.Page is null,
            "safety denial produces no grid or page metadata without requiring usable layout");
    }

    private static void StudyPaperGridFailurePreservesAcceptedState()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1000, 1));
        foreach (bool inconsistent in new[] { false, true })
        {
            try
            {
                view.CaptureGridPageDisplay(navigation, theme, false, 10, inconsistent ? 20 : 10,
                    new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 10, 60, 54, true);
                throw new InvalidOperationException("invalid paper display accepted");
            }
            catch (EcgPaperGridException exception)
            { Check.That(exception.ReasonCode == (inconsistent ? "PaperGrid.InconsistentAxisScale" : "PaperGrid.LineLimitExceeded"), "grid failures preserve component reason codes"); }
        }
        GridCapturedRecordPageDisplay recovered = view.CaptureGridPageDisplay(navigation, theme, false, 10, 10,
            new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 10, 60, 55, true);
        Check.That(recovered.GridLines.Count == 55 && ReferenceEquals(view.Measurement.CurrentPair, pair) && navigation.CurrentPage.PageIndex == 0,
            "valid retry publishes a complete display without losing accepted state");
    }

    private static void StudyPaperGridRebuildsAfterSessionRestore()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.CourseLocked, false);
        RestoredThemedRecordStudySession restored = CapturedRecordStudySession.RestoreThemed(view.CaptureThemedSession(navigation, theme),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.CourseLocked, false);
        GridCapturedRecordPageDisplay before = view.CaptureGridPageDisplay(navigation, theme, false, 10, 10,
            new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 10, 60, 55, false);
        GridCapturedRecordPageDisplay after = restored.Study.View.CaptureGridPageDisplay(restored.Study.Navigation, restored.Theme, false, 10, 10,
            new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 10, 60, 55, false);
        Check.That(before.GridPlan == after.GridPlan && before.GridLines.SequenceEqual(after.GridLines) && !after.Content.Theme!.CanSelect,
            "restoration rebuilds geometry from current scales without persisting grid or old permission");
    }

    private static void ThemedPolicyUpdateChangesAllCurrentGates()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        view.UpdateThemedCommandPolicies(navigation, theme, SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked,
            SystemViewCommandAssessmentPolicy.Enabled, false);
        ThemedCapturedRecordPageDisplay display = view.CaptureThemedPageDisplay(navigation, theme, false, 0, 100, new(0, 100, 60, 20, 1), true);
        Check.That(display.Content.Navigation!.Policy == SystemViewCommandAssessmentPolicy.Disabled &&
            display.Content.Study.Measurement!.ReasonCode == "RecordMeasurement.CourseLocked" && display.Theme!.ReasonCode == "Ecg12Theme.LocalSelectionNotAllowed" &&
            ReferenceEquals(view.Measurement.CurrentPair, pair) && theme.Theme == Ecg12Theme.PaperGridBlack,
            "grouped update applies every current gate without changing theme, page or cursor values");
        ExpectPaginationReason(() => navigation.NextPage(), "RecordPagination.Disabled");
        Check.That(MeasurementReason(() => view.Measurement.ClearPair()) == "RecordMeasurement.CourseLocked", "caliper execution observes grouped update");
    }

    private static void ThemedPolicyUpdateRejectsEveryPartialUpdate()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, false);
        Ecg12ThemeDisplay before = theme.CaptureDisplay();
        SystemViewCommandAssessmentPolicy invalid = (SystemViewCommandAssessmentPolicy)99;
        ExpectPaginationReason(() => view.UpdateThemedCommandPolicies(navigation, theme, invalid, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true),
            "RecordStudy.InvalidPolicy");
        ExpectPaginationReason(() => view.UpdateThemedCommandPolicies(navigation, theme, SystemViewCommandAssessmentPolicy.Enabled, invalid, SystemViewCommandAssessmentPolicy.Enabled, true),
            "RecordStudy.InvalidPolicy");
        ExpectPaginationReason(() => view.UpdateThemedCommandPolicies(navigation, theme, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, invalid, true),
            "RecordStudy.InvalidPolicy");
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Disabled);
        ExpectPaginationReason(() => view.UpdateThemedCommandPolicies(foreign, theme, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true),
            "RecordPagination.ForeignNavigation");
        Check.That(navigation.CurrentPolicy == SystemViewCommandAssessmentPolicy.CourseLocked && view.Measurement.CurrentPolicy == SystemViewCommandAssessmentPolicy.Disabled &&
            theme.CaptureDisplay() == before && foreign.CurrentPolicy == SystemViewCommandAssessmentPolicy.Disabled, "every rejected update preserves all gates including local permission");
    }

    private static void ThemedPolicyUpdateWorksAfterSessionRestore()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        RestoredThemedRecordStudySession restored = CapturedRecordStudySession.RestoreThemed(view.CaptureThemedSession(navigation, theme),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.CourseLocked, false);
        restored.Study.View.UpdateThemedCommandPolicies(restored.Study.Navigation, restored.Theme, SystemViewCommandAssessmentPolicy.Enabled,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true);
        restored.Study.Navigation.NextPage();
        restored.Theme.Select(Ecg12Theme.PaperGridBlack);
        CapturedRecordCursorPair pair = restored.Study.View.PlacePairOnCurrentPage(restored.Study.Navigation, false, 0, 100, new(0, 100, 60, 20, 1),
            new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        Check.That(restored.Theme.Theme == Ecg12Theme.PaperGridBlack && pair.First.Value.DataTimeNs == 100_000_000 &&
            navigation.CurrentPage.PageIndex == 0 && theme.Theme == Ecg12Theme.MonitorDarkGreen, "fresh grouped permission enables all restored commands without changing original objects");
    }

    private static void ThemedSessionRestoresCoherentDisplayState()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(100_000_000, 0, 1), new(150_000_000, 1000, 1));
        ThemedCapturedRecordStudySessionState state = view.CaptureThemedSession(navigation, theme);
        theme.Select(Ecg12Theme.MonitorDarkGreen);
        navigation.PreviousPage();
        view.SelectMeasurementSlot("ecg.slot0");
        RestoredThemedRecordStudySession restored = CapturedRecordStudySession.RestoreThemed(state, Ecg12RecordContext.IndependentCapturedRecord,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true);
        ThemedCapturedRecordPageDisplay display = restored.Study.View.CaptureThemedPageDisplay(restored.Study.Navigation, restored.Theme, false,
            0, 100, new(0, 100, 60, 20, 1), true);
        Check.That(display.Theme!.Theme == Ecg12Theme.PaperGridBlack && display.Content.Page!.PageIndex == 1 &&
            display.Content.Study.MeasurementSlot!.SlotId == "ecg.slot1" && restored.Study.View.Measurement.CurrentPair!.Second.Value == pair.Second.Value,
            "combined checkpoint retains captured theme, page, lead and manual data independently of later source edits");
    }

    private static void ThemedSessionUsesCurrentPermissions()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        ThemedCapturedRecordStudySessionState state = view.CaptureThemedSession(navigation, theme);
        RestoredThemedRecordStudySession restored = CapturedRecordStudySession.RestoreThemed(state, Ecg12RecordContext.ActiveInstance,
            SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.Enabled, false);
        Check.That(restored.Study.Navigation.CurrentPolicy == SystemViewCommandAssessmentPolicy.CourseLocked &&
            restored.Study.View.Measurement.CurrentPolicy == SystemViewCommandAssessmentPolicy.Disabled &&
            restored.Theme.CaptureDisplay().ReasonCode == "Ecg12Theme.LocalSelectionNotAllowed", "all current command gates are explicit restore inputs");
        ExpectPaginationReason(() => restored.Study.Navigation.NextPage(), "RecordPagination.CourseLocked");
        Check.That(restored.Study.View.CaptureThemedPageDisplay(restored.Study.Navigation, restored.Theme, false, 0, 100, new(0, 100, 60, 20, 1), false).Theme is null,
            "restored theme does not bypass current context admission");
    }

    private static void ThemedSessionRejectsInvalidComponentsWithoutMutation()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.CourseLocked, false);
        ThemedCapturedRecordStudySessionState state = view.CaptureThemedSession(navigation, theme);
        try
        {
            CapturedRecordStudySession.RestoreThemed(state with { Theme = new((Ecg12Theme)99) }, Ecg12RecordContext.IndependentCapturedRecord,
                SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, true);
            throw new InvalidOperationException("invalid saved theme accepted");
        }
        catch (Ecg12ThemeSelectionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Theme.InvalidTheme", "invalid theme rejects combined restoration"); }
        ExpectPaginationReason(() => CapturedRecordStudySession.RestoreThemed(state with { Study = state.Study with { SlotId = "unknown" } },
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled,
            SystemViewCommandAssessmentPolicy.Enabled, true), "RecordStudy.InvalidCheckpoint");
        RestoredThemedRecordStudySession recovered = CapturedRecordStudySession.RestoreThemed(state, Ecg12RecordContext.IndependentCapturedRecord,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.CourseLocked, false);
        Check.That(recovered.Theme.Theme == theme.Theme && navigation.CurrentPage.PageIndex == 0 && view.Measurement.CurrentPair is null,
            "invalid components publish no combined session and leave source state available for a valid retry");
    }

    private static void StudyThemeSwitchPreservesContentAndGesture()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        ThemedCapturedRecordPageDisplay before = view.CaptureThemedPageDisplay(navigation, theme, false, 0, 100, scale, true);
        theme.Select(Ecg12Theme.PaperGridBlack);
        ThemedCapturedRecordPageDisplay after = view.CaptureThemedPageDisplay(navigation, theme, false, 0, 100, scale, true);
        Check.That(before.Theme!.Theme == Ecg12Theme.MonitorDarkGreen && after.Theme!.Theme == Ecg12Theme.PaperGridBlack &&
            before.Content == after.Content && ReferenceEquals(view.Measurement.CurrentPair, pair),
            "theme change modifies only theme display while preserving record, page, viewport and manual results");
        Check.That(ReferenceEquals(drag.Commit(false, 0, 100, scale), pair), "theme change does not invalidate unchanged gesture geometry");
    }

    private static void StudyThemeDisplayKeepsCommandPoliciesIndependent()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.CourseLocked);
        Ecg12ThemeSelection theme = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        ThemedCapturedRecordPageDisplay display = view.CaptureThemedPageDisplay(navigation, theme, false, 0, 100, scale, true);
        Check.That(display.Theme!.CanSelect && !display.Content.Navigation!.Next.IsEnabled &&
            display.Content.Study.Measurement!.ReasonCode == "RecordMeasurement.CourseLocked", "separate course command policies are not conflated");
        theme.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked, true);
        ThemedCapturedRecordPageDisplay locked = view.CaptureThemedPageDisplay(navigation, theme, false, 0, 100, scale, true);
        Check.That(!locked.Theme!.CanSelect && locked.Theme.ReasonCode == "Ecg12Theme.CourseLocked" && locked.Content == display.Content,
            "locking theme selection retains the selected theme and record display");
    }

    private static void StudyThemeDisplayRestoresWithoutBypassingAdmission()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ThemeSelection theme = Ecg12ThemeSelection.Restore(new(Ecg12Theme.PaperGridBlack), SystemViewCommandAssessmentPolicy.Enabled, false);
        ThemedCapturedRecordPageDisplay denied = view.CaptureThemedPageDisplay(navigation, theme, false, 0, 0, new(0, 100, 60, 20, 1), true);
        Check.That(denied.Theme is null && denied.Content.Page is null && denied.Content.Study.Record is null, "denied admission suppresses theme chrome and record content");
        try
        {
            view.CaptureThemedPageDisplay(navigation, theme, true, 0, 0, new(0, 100, 60, 20, 1), true);
            throw new InvalidOperationException("invalid themed layout accepted");
        }
        catch (SweepPlotGeometryException exception)
        { Check.That(exception.ReasonCode == "SweepGeometry.InvalidPlotBounds", "theme cannot bypass current page validation"); }
        ThemedCapturedRecordPageDisplay recovered = view.CaptureThemedPageDisplay(navigation, theme, true, 0, 100, new(0, 100, 60, 20, 1), true);
        Check.That(recovered.Theme!.Theme == Ecg12Theme.PaperGridBlack && !recovered.Theme.CanSelect && recovered.Content.Page!.PageIndex == 1,
            "fresh valid display retains restored theme and current local permission after failure");
    }

    private static void StudyPolicyUpdatePublishesBothCommandGroups()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        view.UpdateCommandPolicies(navigation, SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordPageDisplay display = view.CapturePageDisplay(navigation, false, 0, 100, new(0, 100, 60, 20, 1), true);
        Check.That(display.Navigation!.Policy == SystemViewCommandAssessmentPolicy.Disabled && !display.Navigation.Next.IsEnabled &&
            display.Study.Measurement!.ReasonCode == "RecordMeasurement.CourseLocked" && display.Study.Measurement.Measurement is null &&
            ReferenceEquals(view.Measurement.CurrentPair, pair), "complete update changes both command displays without destroying selected evidence");
        ExpectPaginationReason(() => navigation.NextPage(), "RecordPagination.Disabled");
        Check.That(MeasurementReason(() => view.Measurement.ClearPair()) == "RecordMeasurement.CourseLocked", "measurement command sees the same update");
        view.UpdateCommandPolicies(navigation, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(navigation.NextPage().PageIndex == 1 && view.Measurement.Calculate(pair.First, pair.Second, true).AmplitudeChangeMillivolts == new EcgMeasurementRatio(1, 1),
            "explicit complete unlock restores both groups independently of retained data");
    }

    private static void StudyPolicyUpdateRejectsPartialAndForeignChanges()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        ExpectPaginationReason(() => view.UpdateCommandPolicies(navigation, SystemViewCommandAssessmentPolicy.Enabled, (SystemViewCommandAssessmentPolicy)99),
            "RecordStudy.InvalidPolicy");
        ExpectPaginationReason(() => view.UpdateCommandPolicies(navigation, (SystemViewCommandAssessmentPolicy)99, SystemViewCommandAssessmentPolicy.Enabled),
            "RecordStudy.InvalidPolicy");
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Disabled);
        ExpectPaginationReason(() => view.UpdateCommandPolicies(foreign, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled),
            "RecordPagination.ForeignNavigation");
        Check.That(navigation.CurrentPolicy == SystemViewCommandAssessmentPolicy.CourseLocked && view.Measurement.CurrentPolicy == SystemViewCommandAssessmentPolicy.Disabled &&
            foreign.CurrentPolicy == SystemViewCommandAssessmentPolicy.Disabled, "invalid second policy, first policy and foreign binding leave all prior policies intact");
    }

    private static void StudyPolicyUpdateAppliesToRestoredSelectionAndGesture()
    {
        CapturedRecordNavigation original = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView originalView = original.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        originalView.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(originalView.CaptureSession(original), Ecg12RecordContext.IndependentCapturedRecord,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordStudyDrag drag = restored.View.BeginCursorDrag(restored.Navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        restored.View.UpdateCommandPolicies(restored.Navigation, SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(MeasurementReason(() => drag.Commit(false, 0, 100, scale)) == "RecordMeasurement.Disabled", "already-issued gesture observes the updated measurement policy");
        restored.View.UpdateCommandPolicies(restored.Navigation, SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Enabled);
        drag.Cancel();
        restored.View.SelectMeasurementSlot("ecg.slot1");
        restored.View.UpdateCommandPolicies(restored.Navigation, SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(restored.View.Measurement.Slot.SlotId == "ecg.slot1" && restored.View.Measurement.CurrentPolicy == SystemViewCommandAssessmentPolicy.CourseLocked &&
            restored.Navigation.CurrentPolicy == SystemViewCommandAssessmentPolicy.Enabled, "update targets current selected lead after restoration and replacement");
    }

    private static void StudySessionRestoresPageLeadAndCursorValues()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair original = view.Measurement.ReplacePair(new(100_000_000, 0, 1), new(150_000_000, 1000, 1));
        CapturedRecordStudySessionState checkpoint = view.CaptureSession(navigation);
        view.SelectMeasurementSlot("ecg.slot0");
        navigation.PreviousPage();
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(checkpoint, Ecg12RecordContext.ActiveInstance,
            SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordPageDisplay display = restored.View.CapturePageDisplay(restored.Navigation, true, 0, 200, new(0, 100, 60, 40, 1), true);
        Check.That(display.Page!.PageIndex == 1 && display.Study.MeasurementSlot!.SlotId == "ecg.slot1" &&
            restored.View.Measurement.CurrentPair!.Second.Value == original.Second.Value && display.Navigation!.Policy == SystemViewCommandAssessmentPolicy.CourseLocked,
            "one restored record binds the saved page, lead and data values under current policies");
        Check.That(!restored.View.CapturePageDisplay(restored.Navigation, false, 0, 100, new(0, 100, 60, 20, 1), false).Study.Admission.MayEnter,
            "restored current context requires current overlay capability");
        Check.That(MeasurementReason(() => restored.View.Measurement.Calculate(original.First, original.Second, true)) == "RecordMeasurement.ForeignCursor",
            "restoration issues fresh measurement ownership");
    }

    private static void StudySessionRestoresEmptyLockedView()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordStudySessionState checkpoint = view.CaptureSession(navigation);
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(checkpoint, Ecg12RecordContext.IndependentCapturedRecord,
            SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(checkpoint.First is null && checkpoint.Second is null && restored.View.Measurement.CurrentPair is null &&
            restored.View.Measurement.CurrentPolicy == SystemViewCommandAssessmentPolicy.Disabled,
            "empty selection restores without inventing cursors while retaining explicitly supplied policy");
        ExpectPaginationReason(() => restored.Navigation.NextPage(), "RecordPagination.CourseLocked");
    }

    private static void StudySessionRejectsIncompleteAndForeignState()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudySessionState checkpoint = view.CaptureSession(navigation);
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectPaginationReason(() => view.CaptureSession(foreign), "RecordPagination.ForeignNavigation");
        foreach (CapturedRecordStudySessionState invalid in new[]
        {
            checkpoint with { First = new(0, 0, 1) },
            checkpoint with { SlotId = "unknown" },
            checkpoint with { Navigation = checkpoint.Navigation with { PageIndex = 2 } },
            checkpoint with { First = new(100_000_000, 0, 1), Second = new(0, 0, 1) },
        })
        {
            ExpectPaginationReason(() => CapturedRecordStudySession.Restore(invalid, Ecg12RecordContext.IndependentCapturedRecord,
                SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled), "RecordStudy.InvalidCheckpoint");
        }
        Check.That(view.Measurement.CurrentPair is null && navigation.CurrentPage.PageIndex == 0, "invalid session trials cannot alter the original session");
    }

    private static void StudySessionUsesCurrentMeasurementPolicy()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        view.Measurement.ReplacePair(new(0, 0, 1), new(50_000_000, 1000, 1));
        CapturedRecordStudySessionState checkpoint = view.CaptureSession(navigation);
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => view.CaptureSession(navigation)) == "RecordMeasurement.CourseLocked" &&
            MeasurementReason(() => CapturedRecordStudySession.Restore(checkpoint, Ecg12RecordContext.IndependentCapturedRecord,
                SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Disabled)) == "RecordMeasurement.Disabled",
            "session capture and populated restore preserve existing caliper permission gates");
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(checkpoint, Ecg12RecordContext.IndependentCapturedRecord,
            SystemViewCommandAssessmentPolicy.Enabled, SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.View.Measurement.CurrentPair!.Second.Value == checkpoint.Second, "fresh authorized restore remains possible after denied attempt");
    }

    private static void StudyDragRejectsPageRoundTripBetweenEvents()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair initial = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        CapturedRecordPage initialPage = navigation.CurrentPage;
        navigation.NextPage();
        navigation.PreviousPage();
        Check.That(navigation.CurrentPage == initialPage && !ReferenceEquals(navigation.CurrentPage, initialPage),
            "equal page values after navigation retain distinct local page instances");
        Check.That(MeasurementReason(() => drag.Preview(false, 0, 100, scale, new(75, 1), new(40, 1))) == "RecordMeasurement.DragLayoutChanged" &&
            MeasurementReason(() => drag.Commit(false, 0, 100, scale)) == "RecordMeasurement.DragLayoutChanged" &&
            ReferenceEquals(view.Measurement.CurrentPair, initial), "unobserved round trip rejects both preview and completion without changing evidence");
    }

    private static void StudyDragSurvivesNoOpAndRejectedNavigation()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        CapturedRecordPage page = navigation.CurrentPage;
        Check.That(ReferenceEquals(navigation.SelectPage(0), page), "same-page command preserves instance identity after policy validation");
        ExpectPaginationReason(() => navigation.PreviousPage(), "RecordPagination.NoPreviousPage");
        ExpectPaginationReason(() => navigation.SelectPage(99), "RecordPagination.PageOutsideRecord");
        navigation.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        ExpectPaginationReason(() => navigation.SelectPage(0), "RecordPagination.CourseLocked");
        Check.That(ReferenceEquals(navigation.CurrentPage, page), "failed navigation and policy changes preserve page identity");
        CapturedRecordCursorPair preview = drag.Preview(false, 0, 100, scale, new(75, 1), new(40, 1));
        Check.That(ReferenceEquals(drag.Commit(false, 0, 100, scale), preview), "unmoved page permits separately enabled caliper gesture");
    }

    private static void StudyDragCanRestartAfterPageRoundTripRollback()
    {
        CapturedRecordNavigation originalNavigation = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = CapturedRecordNavigation.Restore(originalNavigation.CaptureState(), SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair initial = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag old = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        old.Preview(false, 0, 100, scale, new(75, 1), new(30, 1));
        navigation.NextPage();
        navigation.PreviousPage();
        Check.That(old.Cancel().Second.Value == initial.Second.Value, "rollback remains possible after a round trip on restored navigation");
        CapturedRecordStudyDrag fresh = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        fresh.Preview(false, 0, 100, scale, new(75, 1), new(30, 1));
        Check.That(fresh.Commit(false, 0, 100, scale).Second.Value.DataTimeNs == 75_000_000,
            "fresh gesture binds the current page instance after rollback");
    }

    private static void StudyDragCommitsCurrentPagePreview()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 1, SystemViewCommandAssessmentPolicy.CourseLocked);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        CapturedRecordCursorPair preview = drag.Preview(false, 0, 100, scale, new(75, 1), new(30, 1));
        Check.That(ReferenceEquals(drag.Commit(false, 0, 100, scale), preview) && preview.Second.Value == new EcgManualCursor(175_000_000, 1500, 1),
            "view gesture uses current page and independent measurement policy");
        Check.That(MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragFinished", "committed view gesture cannot roll back");
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == preview.Second.Value, "committed gesture checkpoints only data evidence");
    }

    private static void StudyDragRejectsObservedPageChange()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair original = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        CapturedRecordCursorPair preview = drag.Preview(false, 0, 100, scale, new(75, 1), new(40, 1));
        navigation.NextPage();
        Check.That(MeasurementReason(() => drag.Commit(false, 0, 100, scale)) == "RecordMeasurement.DragLayoutChanged" &&
            ReferenceEquals(view.Measurement.CurrentPair, preview), "release reads the current navigation page and preserves rejected preview");
        navigation.PreviousPage();
        Check.That(MeasurementReason(() => drag.Preview(false, 0, 100, scale, new(75, 1), new(40, 1))) == "RecordMeasurement.DragLayoutChanged" &&
            drag.Cancel().Second.Value == original.Second.Value, "observed page change remains latched until rollback");
    }

    private static void StudyDragRejectsChangedLead()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDrag(navigation, false, 0, 100, scale, RecordCursorEnd.Second);
        view.SelectMeasurementSlot("ecg.slot1");
        Check.That(MeasurementReason(() => drag.Preview(false, 0, 100, scale, new(75, 1), new(40, 1))) == "RecordMeasurement.DragSuperseded" &&
            MeasurementReason(() => drag.Commit(false, 0, 100, scale)) == "RecordMeasurement.DragSuperseded" &&
            MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.DragSuperseded" && view.Measurement.CurrentPair is null,
            "all view gesture operations reject after lead replacement without resurrecting old data");
    }

    private static void StudyDragSafetyLossAllowsPolicyCheckedRollback()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair original = view.PlacePairOnCurrentPage(navigation, true, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        CapturedRecordStudyDrag drag = view.BeginCursorDrag(navigation, true, 0, 100, scale, RecordCursorEnd.Second);
        CapturedRecordCursorPair preview = drag.Preview(true, 0, 100, scale, new(75, 1), new(30, 1));
        try { drag.Commit(false, 0, 100, scale); throw new InvalidOperationException("unsafe commit accepted"); }
        catch (Ecg12ViewAdmissionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Admission.SafetyOverlayUnavailable", "gesture release checks current safety capability"); }
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(MeasurementReason(() => drag.Cancel()) == "RecordMeasurement.CourseLocked" && ReferenceEquals(view.Measurement.CurrentPair, preview),
            "rollback still obeys current caliper policy");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(drag.Cancel().Second.Value == original.Second.Value, "permitted rollback needs no visible viewport");
    }

    private static void MeasurementDragRejectsVerticallyHiddenEndpoints()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        // Fractional excursions must not be rounded onto the visible edge.
        CapturedRecordCursorPair pair = measurement.ReplacePair(new(0, 6_000_001, 2000), new(100_000_000, -4_000_001, 2000));
        Check.That(MeasurementReason(() => { _ = new CapturedRecordDrag(measurement, RecordCursorEnd.First, page, scale); }) ==
            "RecordMeasurement.DragCursorNotVisible" &&
            MeasurementReason(() => { _ = new CapturedRecordDrag(measurement, RecordCursorEnd.Second, page, scale); }) ==
            "RecordMeasurement.DragCursorNotVisible" && ReferenceEquals(measurement.CurrentPair, pair),
            "exactly off-plot endpoints reject drag start without snapping or changing values");
        Check.That(measurement.ProjectCursor(pair.First, page, scale)!.Y.Relation == VerticalPlotRelation.AbovePlot &&
            measurement.ProjectCursor(pair.Second, page, scale)!.Y.Relation == VerticalPlotRelation.BelowPlot,
            "projection still retains unclamped evidence for hidden cursors");
    }

    private static void MeasurementDragAcceptsExactVerticalEdges()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        measurement.ReplacePair(new(0, 3000, 1), new(100_000_000, -2000, 1));
        CapturedRecordDrag top = new(measurement, RecordCursorEnd.First, page, scale);
        top.Preview(new(0, 1), new(0, 1), page, scale);
        top.Commit(page, scale);
        CapturedRecordDrag bottom = new(measurement, RecordCursorEnd.Second, page, scale);
        bottom.Preview(new(50, 1), new(100, 1), page, scale);
        CapturedRecordCursorPair committed = bottom.Commit(page, scale);
        Check.That(committed.First.Value.MicrovoltsNumerator == 3000 && committed.Second.Value.MicrovoltsNumerator == -2000,
            "both closed vertical edges accept drag start and exact pointer round trips");
    }

    private static void MeasurementHiddenCursorRecoversAfterGainChangeAndRestore()
    {
        CapturedRecordMeasurement measurement = EditableMeasurement();
        CapturedRecordCursorPair original = measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 4000, 1));
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        RecordCursorViewport page = new(0, 200_000_000, 0, 100);
        Check.That(MeasurementReason(() => { _ = new CapturedRecordDrag(restored.Measurement, RecordCursorEnd.Second, page, new(0, 100, 60, 20, 1)); }) ==
            "RecordMeasurement.DragCursorNotVisible", "restore preserves hidden amplitude rather than clamping it");
        EcgVerticalScale reducedGain = new(0, 100, 60, 10, 1);
        CapturedRecordDrag visible = new(restored.Measurement, RecordCursorEnd.Second, page, reducedGain);
        visible.Preview(new(75, 1), new(30, 1), page, reducedGain);
        Check.That(visible.Cancel().Second.Value == original.Second.Value,
            "a fresh gesture at a visible gain can cancel back to exact original data");
    }

    private static void CurrentPageEditingUsesNavigationTimeRange()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair first = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        navigation.NextPage();
        CapturedRecordCursorPair moved = view.MoveCursorOnCurrentPage(navigation, false, 0, 100, scale, RecordCursorEnd.Second, new(50, 1), new(40, 1));
        Check.That(first.Second.Value.DataTimeNs == 50_000_000 && moved.Second.Value.DataTimeNs == 150_000_000 &&
            ReferenceEquals(first.First, moved.First), "identical pixels on a new page resolve through current navigation and preserve other endpoint");
        CapturedRecordCursorPair replaced = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        Check.That(replaced.First.Value.DataTimeNs == 100_000_000 && replaced.Second.Value.MicrovoltsNumerator == 1000,
            "new pair uses current page time and calibrated manual voltage");
    }

    private static void CurrentPageEditingRespectsIndependentPolicies()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.CourseLocked);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            view.Measurement.UpdatePolicy(policy);
            Check.That(MeasurementReason(() => view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1))) == $"RecordMeasurement.{policy}" &&
                MeasurementReason(() => view.MoveCursorOnCurrentPage(navigation, false, 0, 100, scale, RecordCursorEnd.Second, new(75, 1), new(40, 1))) == $"RecordMeasurement.{policy}" &&
                ReferenceEquals(view.Measurement.CurrentPair, pair), "caliper policy gates both edits independently of locked pagination");
        }
    }

    private static void CurrentPageEditingDenialPreservesSelection()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair pair = view.PlacePairOnCurrentPage(navigation, true, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        try
        {
            view.MoveCursorOnCurrentPage(navigation, false, 0, 100, scale, RecordCursorEnd.Second, new(75, 1), new(40, 1));
            throw new InvalidOperationException("denied study edit accepted");
        }
        catch (Ecg12ViewAdmissionException exception)
        { Check.That(exception.ReasonCode == "Ecg12Admission.SafetyOverlayUnavailable", "current safety capability gates edits"); }
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectPaginationReason(() => view.PlacePairOnCurrentPage(foreign, true, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1)),
            "RecordPagination.ForeignNavigation");
        Check.That(ReferenceEquals(view.Measurement.CurrentPair, pair), "denied and foreign edits preserve active selection");
    }

    private static void CurrentPageEditingRecoversAndCheckpointsData()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        EcgVerticalScale scale = new(0, 100, 60, 20, 1);
        CapturedRecordCursorPair initial = view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(50, 1), new(40, 1));
        Check.That(MeasurementReason(() => view.PlacePairOnCurrentPage(navigation, false, 0, 100, scale, new(0, 1), new(60, 1), new(100, 1), new(40, 1))) ==
            "RecordMeasurement.InvalidPoint" && ReferenceEquals(initial, view.Measurement.CurrentPair), "invalid second point cannot partially replace current-page selection");
        CapturedRecordCursorPair moved = view.MoveCursorOnCurrentPage(navigation, false, 0, 100, scale, RecordCursorEnd.Second, new(75, 1), new(40, 1));
        RestoredRecordMeasurement restored = CapturedRecordMeasurement.Restore(view.Measurement.CaptureCheckpoint(), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.Second.Value == moved.Second.Value && restored.Second.Value.DataTimeNs == 175_000_000,
            "recovered editing persists absolute record time rather than page-relative pixels");
    }

    private static void NavigationCommandDisplayTracksPageBoundaries()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 75_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigationDisplay first = navigation.CaptureDisplay();
        Check.That(!first.Previous.IsEnabled && first.Previous.ReasonCode == "RecordPagination.NoPreviousPage" && first.Next.IsEnabled && first.Select.IsEnabled,
            "first-page chrome disables only previous");
        navigation.NextPage();
        CapturedRecordNavigationDisplay middle = navigation.CaptureDisplay();
        Check.That(middle.Previous.IsEnabled && middle.Next.IsEnabled && middle.Select.IsEnabled, "middle page enables both directions");
        navigation.NextPage();
        CapturedRecordNavigationDisplay last = navigation.CaptureDisplay();
        Check.That(last.Previous.IsEnabled && !last.Next.IsEnabled && last.Next.ReasonCode == "RecordPagination.NoNextPage" && first.Page.PageIndex == 0,
            "last-page chrome and immutable old snapshot retain their respective pages");
        CapturedRecordNavigationDisplay single = new CapturedRecordNavigation(MeasurementRecord(), long.MaxValue, 0, SystemViewCommandAssessmentPolicy.Enabled).CaptureDisplay();
        Check.That(!single.Previous.IsEnabled && !single.Next.IsEnabled && single.Select.IsEnabled, "single page has no directional navigation but permits valid explicit selection");
    }

    private static void NavigationCommandDisplayCannotAuthorizeStaleActions()
    {
        CapturedRecordNavigation navigation = new(MeasurementRecord(), 75_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigationDisplay stale = navigation.CaptureDisplay();
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            navigation.UpdatePolicy(policy);
            CapturedRecordNavigationDisplay current = navigation.CaptureDisplay();
            Check.That(current.Policy == policy && !current.Previous.IsEnabled && !current.Next.IsEnabled && !current.Select.IsEnabled &&
                current.Previous.ReasonCode == $"RecordPagination.{policy}" && current.Previous == current.Next && current.Next == current.Select,
                "course restriction supplies consistent status and reason for every command");
            ExpectPaginationReason(() => navigation.NextPage(), current.Next.ReasonCode);
            Check.That(stale.Next.IsEnabled && navigation.CurrentPage.PageIndex == 1, "old enabled snapshot cannot authorize a new command");
        }
    }

    private static void NavigationCommandDisplayRestoresCurrentPolicy()
    {
        CapturedRecordNavigation original = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation restored = CapturedRecordNavigation.Restore(original.CaptureState(), SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(restored.CaptureDisplay().Previous.ReasonCode == "RecordPagination.CourseLocked", "restored current policy takes precedence over page boundary reason");
        CapturedRecordNavigationDisplay locked = restored.CaptureDisplay();
        ExpectPaginationReason(() => restored.UpdatePolicy((SystemViewCommandAssessmentPolicy)99), "RecordPagination.InvalidPolicy");
        Check.That(restored.CaptureDisplay() == locked, "invalid update preserves complete command display");
        restored.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(restored.CaptureDisplay() == original.CaptureDisplay(), "unlock recomputes boundary availability from restored page");
    }

    private static void StudyDisplayIncludesAndSuppressesNavigationCommands()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordPageDisplay display = view.CapturePageDisplay(navigation, true, 0, 100, new(0, 100, 60, 20, 1), false);
        Check.That(display.Navigation == navigation.CaptureDisplay() && display.Page == display.Navigation!.Page &&
            display.Viewport!.StartDataTimeNs == display.Page!.StartDataTimeNs, "page, commands and viewport share the captured navigation state");
        CapturedRecordPageDisplay denied = view.CapturePageDisplay(navigation, false, 0, 100, new(0, 100, 60, 20, 1), false);
        Check.That(denied.Navigation is null && denied.Page is null && denied.Viewport is null, "admission denial does not expose navigation chrome");
    }

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
