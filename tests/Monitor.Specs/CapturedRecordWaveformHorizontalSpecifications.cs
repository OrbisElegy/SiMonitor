// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static partial class WaveformRecordArchiveSpecifications
{
    private static CapturedRecordBinding FractionalClockRecord()
    {
        const long anchor = 1_000_000_000;
        var acquisition = FillOnceThenHoldStateMachine.Start(
            new("ecg12.standard", "record.ecg12-7", 7, 11, anchor, 200_000_000, 200_000_000,
                BindingSlots().Select(slot => slot.SlotId).ToArray()),
            13, 17, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(), 0, anchor);
        acquisition.Advance(1, anchor + 200_000_000);
        WaveformEnvelope source = WaveformEnvelopeCodec.Decode(Wire(100, 0, sampleRateNumerator: 15));
        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(source with
        {
            StartSimTimeNs = anchor,
            Planes = source.Planes.Select(plane => plane with
            { SampleRateNumerator = 30, SampleRateDenominator = 2 }).ToArray(),
        });
        var archive = WaveformRecordArchive.Create(
            Plan(anchor, anchor + 200_000_000) with { EpochAnchorSimTimeNs = anchor }, [wire]);
        return CapturedRecordBinding.Create(acquisition.CaptureState(), archive, BindingSlots());
    }

    private static CapturedRecordWaveformHorizontalPageDisplay HorizontalPage(CapturedRecordStudyView view,
        CapturedRecordNavigation navigation, int left = 0, int width = 100, bool overlay = false,
        int limit = 100, CancellationToken cancellationToken = default) =>
        view.CaptureWaveformHorizontalPageDisplay(navigation, overlay, left, width,
            new(0, 100, 60, 20, 1), false, limit, cancellationToken);

    private static void WaveformHorizontalUsesFractionalSourceClock()
    {
        CapturedRecordStudyView view = new(FractionalClockRecord(), Ecg12RecordContext.IndependentCapturedRecord,
            "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordWaveformHorizontalPageDisplay page = HorizontalPage(view, navigation, left: 10);
        Check.That(page.Blocks.Single().SampleX.SequenceEqual(new ExactPlotCoordinate[]
            { new(10, 1), new(130, 3), new(230, 3) }) &&
            page.Blocks.Single().Source.Plane.SampleRateDenominator == 2,
            "fractional nanosecond sample instants and nonzero epoch anchors must map without intermediate time rounding");
        CapturedRecordNavigation narrow = view.CreateNavigation(1, 66_666_666, SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(HorizontalPage(view, narrow).Blocks.Single().SampleX.Single() == new ExactPlotCoordinate(200, 3),
            "a one-nanosecond page must retain the exact two-thirds position of its sample");
        narrow.PreviousPage();
        CapturedRecordWaveformHorizontalPageDisplay empty = HorizontalPage(view, narrow);
        Check.That(empty.Content.Content.Study.Admission.MayEnter && empty.Content.Waveform is not null && empty.Blocks.Count == 0,
            "an admitted page between sample instants must have completed empty geometry");
    }

    private static void WaveformHorizontalResizePreservesRawEvidence()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord,
            "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 1, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 10, 1));
        CapturedRecordWaveformHorizontalPageDisplay original = HorizontalPage(view, navigation);
        CapturedRecordWaveformHorizontalPageDisplay resized = HorizontalPage(view, navigation, 7, 250);
        Check.That(original.Blocks.Single().SampleX[0] == new ExactPlotCoordinate(0, 1) &&
            original.Blocks.Single().SampleX[^1] == new ExactPlotCoordinate(98, 1) &&
            resized.Blocks.Single().SampleX[0] == new ExactPlotCoordinate(7, 1) &&
            resized.Blocks.Single().SampleX[^1] == new ExactPlotCoordinate(252, 1) &&
            resized.Blocks.Single().Source.Plane.FirstSampleIndex == 50 &&
            resized.Blocks.Single().Source.Plane.Samples.SequenceEqual(original.Blocks.Single().Source.Plane.Samples) &&
            resized.Blocks.Single().Source.OriginalContentSha256 == original.Blocks.Single().Source.OriginalContentSha256 &&
            ReferenceEquals(pair, view.Measurement.CurrentPair),
            "resize must remap source time into current bounds while preserving raw samples, hash and caliper evidence");
    }

    private static void WaveformHorizontalDenialAndFailurePublishNoGeometry()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.ActiveInstance,
            "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordWaveformHorizontalPageDisplay prior = HorizontalPage(view, navigation, overlay: true);
        CapturedRecordWaveformHorizontalPageDisplay denied = HorizontalPage(view, navigation, width: 0, limit: 0);
        Check.That(denied.Blocks.Count == 0 && denied.Content.Waveform is null && !denied.Content.Content.Study.Admission.MayEnter &&
            Reason(() => HorizontalPage(view, navigation, overlay: true, limit: 99)) == "WaveformRecordArchive.SampleLimitExceeded",
            "safety denial must suppress all geometry and budget failure must reject the entire result");
        bool invalid = false;
        try { HorizontalPage(view, navigation, width: 0, overlay: true); }
        catch (SweepPlotGeometryException) { invalid = true; }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { HorizontalPage(view, navigation, overlay: true, cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(invalid && cancelled && prior.Blocks.Single().SampleX.Count == 100 &&
            HorizontalPage(view, navigation, overlay: true).Blocks.Single().SampleX.SequenceEqual(prior.Blocks.Single().SampleX),
            "layout failure and cancellation must preserve old results and allow fresh recovery");
    }

    private static void WaveformHorizontalRestoresOwnedCoordinates()
    {
        CapturedRecordStudyView view = new(FractionalClockRecord(), Ecg12RecordContext.IndependentCapturedRecord,
            "ecg.slot1", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordWaveformHorizontalPageDisplay prior = HorizontalPage(view, navigation);
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(view.CaptureSession(navigation),
            Ecg12RecordContext.IndependentCapturedRecord, SystemViewCommandAssessmentPolicy.Disabled,
            SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordWaveformHorizontalPageDisplay after = HorizontalPage(restored.View, restored.Navigation);
        bool immutable = false;
        try { ((IList<ExactPlotCoordinate>)after.Blocks.Single().SampleX)[0] = new(99, 1); }
        catch (NotSupportedException) { immutable = true; }
        Check.That(immutable && after.Blocks.Single().SampleX.SequenceEqual(prior.Blocks.Single().SampleX) &&
            after.Blocks.Single().Source.Plane.ChannelId == ChannelIds[10],
            "fresh checkpoint reconstruction must retain selected source and own immutable exact positions");
        ExpectPaginationReason(() => HorizontalPage(restored.View, navigation), "RecordPagination.ForeignNavigation");
    }
}
