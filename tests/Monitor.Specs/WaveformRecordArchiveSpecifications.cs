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
