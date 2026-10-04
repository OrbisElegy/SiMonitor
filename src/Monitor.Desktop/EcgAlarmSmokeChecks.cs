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
            foreach (var expectedLevel in new[] { MonitorNoticeLevel.Warning, MonitorNoticeLevel.Critical })
            {
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
                while (session.SimulationTimeNs < 140_000_000_000)
                {
                    session.Advance(200_000_000);
                    var snapshot = session.Measurements;
                    if (snapshot is null || snapshot.SampleTimeNs < 20_000_000_000) { continue; }
                    view.RefreshReadings(snapshot);
                    view.RefreshNumericHighlights(0);
                    window.Settings.Sound.UpdateAlarm(view.HighestNotice, alerts.Timing, measurement: snapshot);
                    var notice = view.ActiveNotices.SingleOrDefault(n => n.Numeric == MonitorNumeric.HeartRate);
                    var request = window.Settings.Sound.PublishedAlarm;
                    Require(snapshot.HeartRate.Status == WaveformMeasurementStatus.Valid && notice?.Level == expectedLevel &&
                        request == referenceRequest, "real rotating QRS drives a stable 140 bpm banner and audio priority");
                    if (request != previous) { transitions++; }
                    previous = request;
                    for (int tick = 0; tick < 20; tick++)
                    {
                        actualSequencer.Update(request);
                        referenceSequencer.Update(referenceRequest);
                        Require(actualAudio.TryProduce(480) && referenceAudio.TryProduce(480), "both sequencers produce the next audio slice");
                        actualAudio.Read(actualPcm);
                        referenceAudio.Read(referencePcm);
                        Require(actualPcm.SequenceEqual(referencePcm), "measurement refresh does not restart the continuing alarm sound pattern");
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
