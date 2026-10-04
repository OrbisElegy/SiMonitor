// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;

namespace Monitor.Desktop;

internal static class NotificationSoundSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow(); window.Show(); window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            var sound = window.Settings.Sound;
            alerts.HeartRateEnabled.IsChecked = true;
            var journal = alerts.AlarmLifecycles.Single(j => j.Conditions.Any(s => s.ConditionId == "hr-high"));
            journal.ConfigureNotifications("hr-high", new(1000, 500));
            sound.UseNotificationPlayback(alerts.AlarmLifecycles);
            var empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
            void Read(long milliseconds, int heartRate)
            {
                var snapshot = empty with
                {
                    SampleTimeNs = milliseconds * 1_000_000,
                    HeartRate = new(WaveformMeasurementStatus.Valid, heartRate, milliseconds * 1_000_000)
                };
                window.MonitorView.RefreshReadings(snapshot);
                sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot, notices: window.MonitorView.ActiveNotices);
            }
            Read(0, 130000);
            var first = sound.PublishedAlarm;
            Require(first is { NotificationSequence: > 0, Level: MonitorNoticeLevel.Warning }, "explicit notification playback publishes a single-group request");
            Read(0, 130000);
            Require(sound.PublishedAlarm == first, "duplicate sample keeps the same sound identity");
            Read(100, 100000); Require(sound.PublishedAlarm is null, "recovery cancels the current sound");
            Read(200, 130000);
            Require(sound.PublishedAlarm is null && window.MonitorView.ActiveNotices.Any(n => n.Id == "hr-high"),
                "real recurrence remains visible while its sound is suppressed");
            Read(300, 180001);
            Require(sound.PublishedAlarm is { NotificationSequence: > 1, Level: MonitorNoticeLevel.Critical }, "higher severity bypasses suppression at the actual playback boundary");
            sound.StartAudioPause(60);
            Require(sound.PublishedAlarm is null, "operational audio pause cancels a single-group request");
            Read(600, 180001); Read(800, 180001);
            Require(sound.PublishedAlarm is null, "new policy reminders cannot defeat operational pause");
            sound.ResumeAlarmAudio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var resumed = sound.PublishedAlarm;
            Require(resumed is { Level: MonitorNoticeLevel.Critical, NotificationSequence: > 2 }, "explicit resume uses fresh current identity");
            sound.RefreshAudioPause();
            Require(sound.PublishedAlarm == resumed, "countdown refresh does not replay the group");
            sound.Volume.Value = 0;
            Require(sound.PublishedAlarm is null, "mute cancels the group");
            sound.Volume.Value = 50;
            Require(sound.PublishedAlarm!.NotificationSequence > resumed!.NotificationSequence, "unmute does not reuse a cancelled identity");
            sound.PauseMonitor();
            sound.RefreshAudioPause();
            Require(sound.PublishedAlarm is null, "pause expiry cannot restart a paused simulation");
            Read(900, 100000);
            Require(sound.PublishedAlarm is null, "simulation resume cannot resurrect a recovered event");
            sound.UseNotificationPlayback(null);
            Read(1000, 180001);
            Require(sound.PublishedAlarm is { Level: MonitorNoticeLevel.Critical, NotificationSequence: 0 }, "leaving notification playback restores the unchanged continuous mode");
        }
        finally { window.Close(); }
    }

    internal static void VerifyMixedNotices()
    {
        var window = new DesignPreviewWindow(); window.Show();
        try
        {
            var alerts = window.Settings.Alerts;
            var sound = window.Settings.Sound;
            sound.UseNotificationPlayback(alerts.AlarmLifecycles);
            alerts.InfoTone.IsChecked = true;
            foreach (int level in new[] { 1, 2, 3, 4 })
            {
                alerts.TestLevel.SelectedIndex = level;
                // Numeric/notice acquisition refreshes every 200 ms.
                window.Pulse(window.ActiveTimer, 200_000_000);
                Require(sound.PublishedAlarm is { NotificationSequence: 0 } request && request.Level == (MonitorNoticeLevel)(level - 1),
                    $"production tick carries explicit test notices into mixed continuous playback: test={level}, request={sound.PublishedAlarm}, time={window.Session.SimulationTimeNs}, notices={string.Join(",", window.MonitorView.ActiveNotices.Select(n => n.Id))}, status={window.Settings.Status.Text}");
                var published = sound.PublishedAlarm;
                window.Pulse(window.ActiveTimer, 50_000_000);
                Require(sound.PublishedAlarm == published, "banner refresh cannot restart continuous test sound");
            }
            sound.StartAudioPause(60);
            Require(sound.PublishedAlarm is null, "mixed test sound respects operational pause");
            alerts.TestLevel.SelectedIndex = 0;
            alerts.InfoTone.IsChecked = false;
            window.Pulse(window.ActiveTimer, 200_000_000);
            sound.ResumeAlarmAudio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(sound.PublishedAlarm is null, "removed test and default silent Info cannot replay on resume");
        }
        finally { window.Close(); }

        var result = SoundPreviewResult.Unavailable;
        var panel = new SoundSettingsPanel((_, _) => Task.FromResult(result));
        try
        {
            var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
            var snapshot = LiveWaveformMeasurements.CreateIllustration().Read(0) with
            { HeartRate = new(WaveformMeasurementStatus.Valid, 180001, 0) };
            var patient = filter.Evaluate(MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true }, snapshot)!;
            panel.UseNotificationPlayback([filter.Lifecycle]);
            panel.PreviewAsync().GetAwaiter().GetResult();
            var fault = panel.OutputNotice!;
            panel.UpdateAlarm(patient.Level, new(), measurement: snapshot, notices: [patient, fault]);
            Require(panel.PublishedAlarm is { NotificationSequence: > 0, Level: MonitorNoticeLevel.Critical } && panel.OutputNotice == fault,
                "routing a patient notification does not claim device recovery or sound the output fault");
            result = SoundPreviewResult.Stopped;
            panel.PreviewAsync().GetAwaiter().GetResult();
            Require(panel.OutputNotice == fault, "cancelled output retry retains fault in notification mode");
            filter.Reset();
            panel.UpdateAlarm(null, new(InfoTone: true), notices: [fault]);
            Require(panel.PublishedAlarm is null && panel.OutputNotice == fault, "output fault alone stays silent even with Info sound enabled");
            result = SoundPreviewResult.Completed;
            panel.PreviewAsync().GetAwaiter().GetResult();
            Require(panel.OutputNotice is null, "successful output completion retains the existing fault recovery contract");
        }
        finally { panel.Close(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Notification sound: " + message); } }
}
