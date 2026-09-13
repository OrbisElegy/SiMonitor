// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static partial class WaveformRecordArchiveSpecifications
{
    private static CapturedRecordPathPageDisplay PathPage(CapturedRecordStudyView view,
        CapturedRecordNavigation navigation, CapturedRecordVoltageBinding voltage, CapturedRecordQualityBinding quality,
        bool overlay = false, int limit = 200, CancellationToken token = default) =>
        view.CapturePathPageDisplay(navigation, voltage, quality, overlay, 0, 100,
            new(0, 100, 60, 20, 1), false, 200, limit, token);

    private static void ExpectPathReason(Action action, string reason)
    {
        try { action(); throw new InvalidOperationException("expected path rejection"); }
        catch (CapturedRecordPathException exception) { Check.That(exception.ReasonCode == reason, reason); }
    }

    private static void RecordPathBreaksAtEveryDeniedSample()
    {
        CapturedRecordBinding record = QualityRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordVoltageBinding voltage = new(record, "ecg.slot0", UnitChannel());
        CapturedRecordQualityBinding quality = new(record, "ecg.slot0", QualityRules(), 4);
        CapturedRecordPathPageDisplay page = PathPage(view, navigation, voltage, quality);
        Check.That(page.Segments.Count == 95 && page.Segments[0].StartSampleIndex == 0 && page.Segments[0].EndSampleIndex == 1 &&
            page.Segments[1].StartSampleIndex == 4 && page.Segments[^1].EndSampleIndex == 98 &&
            page.Segments.All(segment => segment.EndSampleIndex - segment.StartSampleIndex == 1) &&
            page.Content.Blocks.Single().Samples.Count == 100,
            "denied samples must break the path without dropping source evidence or bridging the rejected range");
        CapturedRecordQualityBinding none = new(record, "ecg.slot0", QualityRules().Select(rule => rule with { Drawable = false }).ToArray(), 4);
        Check.That(PathPage(view, navigation, voltage, none).Segments.Count == 0,
            "an admitted page with all samples denied has a complete empty path");
    }

    private static void RecordPathConnectsVerifiedBlocksWithoutPageCarryover()
    {
        var acquisition = FillOnceThenHoldStateMachine.Start(
            new("ecg12.standard", "record.ecg12-7", 7, 11, 0, 400_000_000, 400_000_000,
                BindingSlots().Select(slot => slot.SlotId).ToArray()), 13, 17, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(), 0, 0);
        acquisition.Advance(1, 400_000_000);
        var record = CapturedRecordBinding.Create(acquisition.CaptureState(),
            WaveformRecordArchive.Create(Plan(endExclusiveSimTimeNs: 400_000_000), [Wire(100, 0), Wire(101, 200_000_000)]), BindingSlots());
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordVoltageBinding voltage = new(record, "ecg.slot0", UnitChannel());
        CapturedRecordQualityBinding quality = new(record, "ecg.slot0", [new(0, true)], 1);
        CapturedRecordPathPageDisplay whole = PathPage(view, view.CreateNavigation(400_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled), voltage, quality);
        Check.That(whole.Segments.Count == 199 && whole.Segments[99].StartSampleIndex == 99 && whole.Segments[99].EndSampleIndex == 100 &&
            whole.Segments[99].StartBlockSequence == 100 && whole.Segments[99].EndBlockSequence == 101,
            "adjacent samples spanning verified blocks must connect with both block sequence references retained");
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordPathPageDisplay first = PathPage(view, navigation, voltage, quality);
        navigation.NextPage();
        CapturedRecordPathPageDisplay second = PathPage(view, navigation, voltage, quality);
        Check.That(first.Segments.Count == 99 && second.Segments.Count == 99 && second.Segments[0].StartSampleIndex == 100 &&
            second.Segments[0].Start.X == new ExactPlotCoordinate(0, 1),
            "fresh pages must not inherit a predecessor from the prior page or invent boundary-neighbor padding");
    }

    private static void RecordPathBudgetAndSafetyFailuresPreserveEvidence()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.ActiveInstance, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 10, 1));
        CapturedRecordVoltageBinding voltage = new(record, "ecg.slot0", UnitChannel("MILLIVOLT"));
        CapturedRecordQualityBinding quality = new(record, "ecg.slot0", [new(0, true)], 1);
        CapturedRecordPathPageDisplay prior = PathPage(view, navigation, voltage, quality, true, 99);
        ExpectPathReason(() => PathPage(view, navigation, voltage, quality, true, 98), "RecordPath.SegmentLimitExceeded");
        ExpectPathReason(() => PathPage(view, navigation, voltage, quality, true, 0), "RecordPath.InvalidSegmentLimit");
        CapturedRecordPathPageDisplay denied = PathPage(view, navigation, null!, null!, limit: 0);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { PathPage(view, navigation, voltage, quality, true, token: cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && denied.Segments.Count == 0 && ReferenceEquals(pair, view.Measurement.CurrentPair) &&
            prior.Segments[0].Start.Y.Relation == VerticalPlotRelation.AbovePlot &&
            PathPage(view, navigation, voltage, quality, true, 99).Segments.SequenceEqual(prior.Segments),
            "source paths retain unclipped positions; late budget failures and cancellation preserve accepted evidence for retry");
    }

    private static void RecordPathRestoreRebuildsExactOwnedSegments()
    {
        CapturedRecordBinding record = FractionalClockRecord();
        CapturedRecordStudyView view = new(record, Ecg12RecordContext.IndependentCapturedRecord, "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        ResolvedEcgVoltageChannel channel = UnitChannel() with { SampleRateNumerator = 30, SampleRateDenominator = 2 };
        CapturedRecordPathPageDisplay prior = PathPage(view, navigation, new(record, "ecg.slot0", channel), new(record, "ecg.slot0", [new(0, true)], 1));
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(view.CaptureSession(navigation),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordBinding fresh = ReadWaveformPage(restored.View, restored.Navigation).Content.Study.Record!;
        CapturedRecordPathPageDisplay after = PathPage(restored.View, restored.Navigation, new(fresh, "ecg.slot0", channel), new(fresh, "ecg.slot0", [new(0, true)], 1));
        bool immutable = false;
        try { ((IList<RecordWaveformSegment>)after.Segments)[0] = prior.Segments[1]; }
        catch (NotSupportedException) { immutable = true; }
        Check.That(immutable && after.Segments.SequenceEqual(prior.Segments) && after.Segments[0].End.X == new ExactPlotCoordinate(100, 3),
            "restoration must reconstruct immutable segments with original fractional sample positions and fresh declarations");
    }
}
