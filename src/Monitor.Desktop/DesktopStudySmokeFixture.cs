// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Desktop;

// Test-only data: constructed exclusively by --smoke-test, never normal startup.
internal static class DesktopStudySmokeFixture
{
    public static EcgPaperGridSvgStyle GridStyle => new("#f0cccc", "#cc9999", 500, 1000);
    public static EcgManualCursorSvgStyle CursorStyle => new("#0055ff", "#ff5500", 1000, 2000);

    public static CapturedRecordSvgPublication Create(bool hideMeasurement = false, bool firstOutsidePlot = false)
    {
        Guid session = Guid.Parse("11111111-1111-4111-8111-111111111111");
        Guid instance = Guid.Parse("22222222-2222-4222-8222-222222222222");
        Guid[] channels = Enumerable.Range(1, 12)
            .Select(i => Guid.Parse($"30000000-0000-4000-8000-{i:x12}")).ToArray();
        string[] slots = Enumerable.Range(0, 12).Select(i => $"ecg.slot{i}").ToArray();
        FillOnceThenHoldStateMachine acquisition = FillOnceThenHoldStateMachine.Start(
            new("ecg12.standard", "record.smoke", 7, 11, 0, 200_000_000, 200_000_000, slots),
            13, 17, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(), 0, 0);
        acquisition.Advance(1, 200_000_000);
        WaveformPlane[] planes = channels.Select(channel => new WaveformPlane(channel,
            500, 1, 0, 1, 1, 0, 1, WaveformQualityEncoding.None,
            Array.AsReadOnly(new short[100]), Array.Empty<WaveformQualityRange>())).ToArray();
        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(new(session, instance, 3, 7, 100, 11,
            0, WaveformBlockAssembler.BlockDurationNs, Array.AsReadOnly(planes)));
        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            new("ecg12.standard", "record.smoke", 7, session, instance, 3, 0, 7, 11,
                0, 200_000_000, channels), [wire]);
        CapturedRecordBinding binding = CapturedRecordBinding.Create(acquisition.CaptureState(), archive,
            slots.Select((slot, i) => new RecordSlotBinding(slot, channels[i])).ToArray());
        CapturedRecordNavigation navigation = new(binding, 200_000_000, 0, SystemViewCommandAssessmentPolicy.Enabled);
        CapturedRecordStudyView view = navigation.CreateStudyView(Ecg12RecordContext.IndependentCapturedRecord,
            slots[0], SystemViewCommandAssessmentPolicy.Enabled);
        view.Measurement.ReplacePair(new(50_000_000, firstOutsidePlot ? 10000 : 125, 1), new(150_000_000, 75, 1));
        if (hideMeasurement) { view.Measurement.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled); }
        CapturedRecordSvgPresentation presentation = new(view, navigation,
            new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true),
            new(new(Ecg12ZoomMode.ExplicitScale, 2, 1), SystemViewCommandAssessmentPolicy.Enabled));
        presentation.Refresh(false, new(0, 100, new(0, 60, 50, 200, 1), new(25, 1, 10, 1), 0, 0, 100),
            new(100, 60, 200, 120), GridStyle, CursorStyle, false);
        return presentation.Publication;
    }
}
