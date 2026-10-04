// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class AlarmNotificationSmokeChecks
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
            var journal = alerts.AlarmLifecycles.Single(j => j.Conditions.Any(s => s.ConditionId == "hr-high"));
            journal.ConfigureNotifications("hr-high", new(1000, 500));
            var empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
            void Read(long milliseconds, int heartRate)
            {
                var snapshot = empty with
                {
                    SampleTimeNs = milliseconds * 1_000_000,
                    HeartRate = new(WaveformMeasurementStatus.Valid, heartRate, milliseconds * 1_000_000)
                };
                window.MonitorView.RefreshReadings(snapshot);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot);
            }
            AlarmNotificationDecision Last() => journal.NotificationRecords[^1].Decision;
            Read(0, 130000);
            Require(Last().Kind == AlarmNotificationKind.FirstOccurrence && window.MonitorView.ActiveNotices.Any(n => n.Id == "hr-high"),
                "confirmed desktop condition produces first notification intent");
            Read(100, 100000); Read(200, 130000);
            var episode = Last().Episode;
            Require(Last().Kind == AlarmNotificationKind.RepeatSuppressed && journal.Conditions.Single(s => s.ConditionId == "hr-high").Episode == episode &&
                window.MonitorView.ActiveNotices.Any(n => n.Id == "hr-high" && n.Level == MonitorNoticeLevel.Warning),
                "notification suppression never removes a real episode or its visual warning");
            Read(300, 180001);
            Require(Last().Kind == AlarmNotificationKind.SeverityEscalation && Last().Episode == episode &&
                window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical, "escalation intent and existing sound priority remain consistent");
            var transitions = journal.Transitions;
            Read(600, 180001); Read(799, 180001);
            Require(Last().Kind == AlarmNotificationKind.SeverityEscalation, "reminder does not precede its independent period");
            Read(800, 180001);
            Require(Last().Kind == AlarmNotificationKind.Reminder && journal.Transitions.SequenceEqual(transitions),
                "ongoing reminders do not fabricate new physiological transitions");
            var records = journal.NotificationRecords;
            var sound = window.Settings.Sound.PublishedAlarm;
            Read(800, 180001);
            Require(journal.NotificationRecords.SequenceEqual(records) && window.Settings.Sound.PublishedAlarm == sound,
                "repeated rendering cannot duplicate intent or restart continuous sound");
            alerts.WarningHeartRate.Value = 121; alerts.WarningHeartRate.Value = 120;
            Read(900, 180001);
            Require(Last().Kind == AlarmNotificationKind.FirstOccurrence && Last().Episode != episode,
                "configuration interruption discards stale repeat suppression");
            Require(journal.NotificationPolicyFor("hr-high") == new AlarmNotificationPolicy(1000, 500),
                "runtime reset retains policy while discarding timing evidence");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Alarm notification: " + message); } }
}
