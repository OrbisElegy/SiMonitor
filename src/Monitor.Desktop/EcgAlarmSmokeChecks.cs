// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;
using Monitor.Simulation.Authoring;

namespace Monitor.Desktop;

internal static class EcgAlarmSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            alerts.HeartRateEnabled.IsChecked = true;
            alerts.WarningLowHeartRate.Value = 50;
            alerts.CriticalLowHeartRate.Value = 40;
            foreach (var (expectedLevel, confirmationMilliseconds) in new[]
            {
                (MonitorNoticeLevel.Warning, 0), (MonitorNoticeLevel.Critical, 0),
                (MonitorNoticeLevel.Warning, 600), (MonitorNoticeLevel.Critical, 600)
            })
            {
                alerts.Reset();
                var boundaryTiming = new BoundaryConfirmationTiming(confirmationMilliseconds, 400);
                alerts.HeartRateConfirmation.Restore(new(boundaryTiming, boundaryTiming, boundaryTiming, boundaryTiming));
                window.Settings.Sound.ResetBeatSource();
                window.Settings.Sound.ResetPitchState();
                alerts.WarningHeartRate.Value = expectedLevel == MonitorNoticeLevel.Warning ? 140 : 120;
                alerts.CriticalHeartRate.Value = expectedLevel == MonitorNoticeLevel.Warning ? 180 : 140;
                var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.VtPreset with { VtTwisting = true },
                    MonitorDisplayConfiguration.Default(), true);
                var view = new LiveMonitorView(new LiveMonitorTrace(session)) { AdditionalNotices = alerts.Notices };
                var actualAudio = new AudioRenderSession();
                var referenceAudio = new AudioRenderSession();
                var actualSequencer = new MonitorAlarmSequencer(actualAudio);
                var referenceSequencer = new MonitorAlarmSequencer(referenceAudio);
                MonitorAlarmSoundRequest? previous = null;
                var referenceRequest = new MonitorAlarmSoundRequest(expectedLevel, (int)window.Settings.Sound.Volume.Value, alerts.Timing);
                float[] actualPcm = new float[480], referencePcm = new float[480];
                int transitions = 0;
                bool audible = false;
                long? firstSampleTimeNs = null;
                while (session.SimulationTimeNs < 140_000_000_000)
                {
                    session.Advance(200_000_000);
                    var snapshot = session.Measurements;
                    if (snapshot is null || snapshot.SampleTimeNs < 20_000_000_000) { continue; }
                    firstSampleTimeNs ??= snapshot.SampleTimeNs;
                    bool confirmed = snapshot.SampleTimeNs - firstSampleTimeNs.Value >= confirmationMilliseconds * 1_000_000L;
                    // This ECG-only fixture has no optical acquisition. Its existing
                    // SpO2 NoData Info remains while physiological confirmation waits.
                    var expectedRequest = confirmed ? referenceRequest : new MonitorAlarmSoundRequest(
                        MonitorNoticeLevel.Info, (int)window.Settings.Sound.Volume.Value, alerts.Timing);
                    view.RefreshReadings(snapshot);
                    view.RefreshNumericHighlights(0);
                    window.Settings.Sound.UpdateAlarm(view.HighestNotice, alerts.Timing, measurement: snapshot);
                    var notice = view.ActiveNotices.SingleOrDefault(n => n.Numeric == MonitorNumeric.HeartRate);
                    var request = window.Settings.Sound.PublishedAlarm;
                    Require(snapshot.HeartRate.Status == WaveformMeasurementStatus.Valid && notice?.Level == (confirmed ? expectedLevel : null) &&
                        request == expectedRequest,
                        $"real rotating QRS confirmation: expected {expectedLevel}, delay {confirmationMilliseconds} ms, time {snapshot.SampleTimeNs}, first {firstSampleTimeNs}, HR {snapshot.HeartRate.MilliBeatsPerMinute}, notice {notice?.Level}, audio {request?.Level}, notices {string.Join("; ", view.ActiveNotices.Select(n => n.Id))}");
                    if (request != previous && request?.Level == expectedLevel) { transitions++; }
                    previous = request;
                    for (int tick = 0; tick < 20; tick++)
                    {
                        actualSequencer.Update(request);
                        referenceSequencer.Update(expectedRequest);
                        Require(actualAudio.TryProduce(480) && referenceAudio.TryProduce(480), "both sequencers produce the next audio slice");
                        actualAudio.Read(actualPcm);
                        referenceAudio.Read(referencePcm);
                        Require(actualPcm.SequenceEqual(referencePcm), "measurement refresh does not restart the continuing alarm sound pattern");
                        if (!confirmed) { Require(actualPcm.All(sample => sample == 0), "default Info stays silent before physiological confirmation"); }
                        audible |= actualPcm.Any(sample => sample != 0);
                    }
                }
                Require(transitions == 1 && audible, "one sustained alarm request produces PCM through the full rotating episode");
            }
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("ECG alarm: " + message); }
    }
}
