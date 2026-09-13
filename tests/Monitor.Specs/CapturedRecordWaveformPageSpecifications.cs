// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static partial class WaveformRecordArchiveSpecifications
{
    private static CapturedRecordWaveformPageDisplay ReadWaveformPage(CapturedRecordStudyView view,
        CapturedRecordNavigation navigation, bool overlay = false, int limit = 100,
        CancellationToken cancellationToken = default) => view.CaptureWaveformPageDisplay(
            navigation, overlay, 0, 100, new(0, 100, 60, 20, 1), false, limit, cancellationToken);

    private static void WaveformPageFollowsCurrentPageAndMappedSlot()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord,
            "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(75_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(50_000_000, 0, 1), new(100_000_000, 1000, 1));
        CapturedRecordWaveformPageDisplay first = ReadWaveformPage(view, navigation);
        navigation.NextPage();
        CapturedRecordWaveformPageDisplay second = ReadWaveformPage(view, navigation);
        Check.That(first.Waveform!.ChannelId == ChannelIds[11] && first.Waveform.Blocks.Single().Plane.Samples.Count == 38 &&
            first.Waveform.Blocks.Single().Plane.Samples.All(sample => sample == 111) &&
            second.Waveform!.StartSimTimeNs == 75_000_000 && second.Waveform.EndExclusiveSimTimeNs == 150_000_000 &&
            second.Waveform.Blocks.Single().Plane.FirstSampleIndex == 38 &&
            second.Waveform.Blocks.Single().Plane.Samples.Count == 37 && ReferenceEquals(pair, view.Measurement.CurrentPair),
            "raw samples must follow exact current pages and explicit slot mapping without moving calipers");
        view.SelectMeasurementSlot("ecg.slot1");
        navigation.NextPage();
        CapturedRecordWaveformPageDisplay last = ReadWaveformPage(view, navigation);
        Check.That(last.Content.Page!.EndExclusiveDataTimeNs == 200_000_000 &&
            last.Content.Study.MeasurementSlot!.ChannelId == last.Waveform!.ChannelId &&
            last.Waveform.ChannelId == ChannelIds[10] && last.Waveform.Blocks.Single().Plane.Samples.Count == 25 &&
            last.Waveform.Blocks.Single().Plane.Samples.All(sample => sample == 110) &&
            first.Content.Page!.PageIndex == 0 && first.Waveform.ChannelId == ChannelIds[11],
            "lead changes and short final pages must rebuild coherent content while old snapshots remain unchanged");
    }

    private static void WaveformPageRechecksSafetyIndependentlyOfCommands()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.ActiveInstance,
            "ecg.slot0", SystemViewCommandAssessmentPolicy.Disabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.CourseLocked);
        CapturedRecordWaveformPageDisplay admitted = ReadWaveformPage(view, navigation, true);
        CapturedRecordWaveformPageDisplay denied = view.CaptureWaveformPageDisplay(navigation,
            false, 0, 0, null!, false, 0);
        Check.That(admitted.Waveform!.Blocks.Single().Plane.Samples.Count == 50 &&
            admitted.Content.Study.Measurement!.ReasonCode == "RecordMeasurement.Disabled" &&
            !admitted.Content.Navigation!.Next.IsEnabled && !denied.Content.Study.Admission.MayEnter &&
            denied.Waveform is null && denied.Content.Page is null && denied.Content.Study.Record is null &&
            denied.Content.Study.MeasurementSlot is null,
            "command locks must not hide an admitted record; safety denial must suppress raw samples and metadata before unused layout/budget checks");
        view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(ReadWaveformPage(view, navigation, true).Waveform is not null,
            "restoring current overlay capability must allow fresh record reads with locked calipers");
    }

    private static void WaveformPageFailurePreservesStudyEvidence()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord,
            "ecg.slot0", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordCursorPair pair = view.Measurement.ReplacePair(new(0, 0, 1), new(100_000_000, 1, 1));
        CapturedRecordPage page = navigation.CurrentPage;
        CapturedRecordWaveformPageDisplay before = ReadWaveformPage(view, navigation);
        Check.That(Reason(() => ReadWaveformPage(view, navigation, limit: 49)) == "WaveformRecordArchive.SampleLimitExceeded" &&
            Reason(() => ReadWaveformPage(view, navigation, limit: 0)) == "WaveformRecordArchive.InvalidSampleLimit",
            "waveform budgets must reject rather than returning a truncated page");
        CapturedRecordNavigation foreign = new(MeasurementRecord(), 100_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        ExpectPaginationReason(() => ReadWaveformPage(view, foreign), "RecordPagination.ForeignNavigation");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { ReadWaveformPage(view, navigation, cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && ReferenceEquals(pair, view.Measurement.CurrentPair) && ReferenceEquals(page, navigation.CurrentPage) &&
            before.Waveform!.Blocks.Single().Plane.Samples.SequenceEqual(
                ReadWaveformPage(view, navigation, limit: 50).Waveform!.Blocks.Single().Plane.Samples),
            "failed and cancelled captures must preserve prior snapshots, page identity and cursor evidence for retry");
    }

    private static void WaveformPageRestoresWithFreshBindingAndCurrentAdmission()
    {
        CapturedRecordStudyView view = new(MeasurementRecord(), Ecg12RecordContext.IndependentCapturedRecord,
            "ecg.slot2", SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordNavigation navigation = view.CreateNavigation(75_000_000, 2, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordWaveformPageDisplay before = ReadWaveformPage(view, navigation);
        CapturedRecordStudySessionState state = view.CaptureSession(navigation);
        RestoredRecordStudySession restored = CapturedRecordStudySession.Restore(state, Ecg12RecordContext.ActiveInstance,
            SystemViewCommandAssessmentPolicy.CourseLocked, SystemViewCommandAssessmentPolicy.Disabled);
        state.Navigation.Record.Archive.RawEnvelopes[0][0] ^= 0xff;
        Check.That(ReadWaveformPage(restored.View, restored.Navigation).Waveform is null,
            "restored data must not restore former independent-record admission");
        CapturedRecordWaveformPageDisplay after = ReadWaveformPage(restored.View, restored.Navigation, true);
        Check.That(after.Content.Page == before.Content.Page && after.Waveform!.ChannelId == ChannelIds[9] &&
            after.Waveform.Blocks.Single().Plane.Samples.SequenceEqual(before.Waveform!.Blocks.Single().Plane.Samples) &&
            after.Waveform.Blocks.Single().OriginalContentSha256 == before.Waveform.Blocks.Single().OriginalContentSha256,
            "revalidated restored bindings must reconstruct the selected lead/page with current policies and owned wire evidence");
        ExpectPaginationReason(() => ReadWaveformPage(restored.View, navigation, true), "RecordPagination.ForeignNavigation");
    }
}
