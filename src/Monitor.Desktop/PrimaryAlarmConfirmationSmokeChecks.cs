// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Media;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class PrimaryAlarmConfirmationSmokeChecks
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
            alerts.SpO2Enabled.IsChecked = true;
            alerts.HeartRateConfirmation.Restore(new(new(600, 300), new(400, 200), new(400, 200), new(600, 300)));
            alerts.SpO2Confirmation.Restore(new(new(400, 300), new(200, 200), new(0, 0), new(0, 0)));
            Require(alerts.HeartRateConfirmation.Fields.Length == 8 && alerts.SpO2Confirmation.Fields.Length == 4,
                "SpO2 only exposes its supported low boundaries");
            var snapshot = LiveWaveformMeasurements.CreateIllustration().Read(0);
            long now = 0;
            void Read(int heartRate = 180001, int saturation = 84999, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid)
            {
                snapshot = snapshot with
                {
                    SampleTimeNs = now,
                    HeartRate = new(status, heartRate, now),
                    SpO2 = new(status, saturation, null, now)
                };
                now += 100_000_000;
                window.MonitorView.RefreshReadings(snapshot);
                window.MonitorView.RefreshNumericHighlights(0);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot);
            }
            MonitorNotice? Notice(MonitorNumeric numeric) => window.MonitorView.ActiveNotices.SingleOrDefault(n => n.Numeric == numeric);
            void Confirm()
            {
                for (int i = 0; i < 7; i++) { Read(); }
                Require(Notice(MonitorNumeric.HeartRate)?.Level == MonitorNoticeLevel.Critical &&
                    Notice(MonitorNumeric.SpO2)?.Level == MonitorNoticeLevel.Critical, "primary confirmations finish");
            }
            Read(); Read();
            Require(Notice(MonitorNumeric.HeartRate) is null && Notice(MonitorNumeric.SpO2) is null,
                "unconfirmed primary values do not publish physiological banners");
            Read();
            Require(Notice(MonitorNumeric.SpO2)?.Level == MonitorNoticeLevel.Warning && Notice(MonitorNumeric.HeartRate) is null,
                "SpO2 Warning confirms independently at 200 ms");
            Read(); Read();
            Require(Notice(MonitorNumeric.SpO2)?.Level == MonitorNoticeLevel.Critical && Notice(MonitorNumeric.HeartRate)?.Level == MonitorNoticeLevel.Warning,
                "independent severity timing is visible at 400 ms");
            Read(); Read();
            Require(Notice(MonitorNumeric.HeartRate)?.Level == MonitorNoticeLevel.Critical && window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical &&
                (LiveMonitorView.NumericBackground(window.MonitorView.NumericBlocks[2]) as ISolidColorBrush)?.Color == Color.Parse("#ffb51f2c"),
                "confirmed primary alarms agree with numeric highlight and sound priority");
            var request = window.Settings.Sound.PublishedAlarm;
            alerts.HeartRateConfirmation.Groups.SelectedIndex = 1;
            alerts.HeartRateConfirmation.Groups.SelectedIndex = 2;
            alerts.HeartRateConfirmation.Groups.SelectedIndex = 0;
            alerts.SpO2Confirmation.Groups.SelectedIndex = 2;
            Read();
            Require(Notice(MonitorNumeric.HeartRate)?.Level == MonitorNoticeLevel.Critical && Notice(MonitorNumeric.SpO2)?.Level == MonitorNoticeLevel.Critical &&
                window.Settings.Sound.PublishedAlarm == request, "switching alarm subgroups preserves active confirmation and sound");
            for (int i = 0; i < 10; i++) { Read(i % 2 == 0 ? 179999 : 180001, i % 2 == 0 ? 85001 : 84999); }
            Require(Notice(MonitorNumeric.HeartRate)?.Level == MonitorNoticeLevel.Critical && Notice(MonitorNumeric.SpO2)?.Level == MonitorNoticeLevel.Critical &&
                window.Settings.Sound.PublishedAlarm == request, "short recovery chatter retains both Critical requests");
            alerts.HeartRateConfirmation.Fields[6].Value = .8m;
            alerts.HeartRateConfirmation.Fields[6].Value = .6m;
            Read();
            Require(Notice(MonitorNumeric.HeartRate) is null && Notice(MonitorNumeric.SpO2)?.Level == MonitorNoticeLevel.Critical,
                "editing and reverting HR timing clears only HR evidence");
            Confirm();
            alerts.SpO2Enabled.IsChecked = false;
            alerts.SpO2Enabled.IsChecked = true;
            Read();
            Require(Notice(MonitorNumeric.SpO2) is null && Notice(MonitorNumeric.HeartRate)?.Level == MonitorNoticeLevel.Critical,
                "SpO2 toggled between samples starts fresh without resetting HR");
            Confirm();
            Read(120000, 92000);
            Read(120000, 92000);
            Read(120000, 92000);
            Require(Notice(MonitorNumeric.HeartRate)?.Level == MonitorNoticeLevel.Critical && Notice(MonitorNumeric.SpO2)?.Level == MonitorNoticeLevel.Critical,
                "equal thresholds start recovery, with Critical still waiting");
            Read(120000, 92000);
            Require(Notice(MonitorNumeric.HeartRate) is null && Notice(MonitorNumeric.SpO2) is null &&
                window.Settings.Sound.PublishedAlarm?.Level != MonitorNoticeLevel.Critical, "confirmed recovery clears primary sound and banners together");
            Confirm();
            Read(status: WaveformMeasurementStatus.PoorSignal);
            Require(Notice(MonitorNumeric.HeartRate) is null && Notice(MonitorNumeric.SpO2) is null, "invalid primary evidence is never treated as low physiology");
            Confirm();
            var saved = alerts.CapturePreferences();
            var invalidTimings = saved.ConfirmationTimings.ToDictionary(entry => entry.Key, entry => entry.Value);
            invalidTimings[MonitorNumeric.SpO2] = saved.ConfirmationFor(MonitorNumeric.SpO2) with { WarningHigh = new(1, 0) };
            bool rejected = false;
            try { alerts.RestorePreferences(saved with { ConfirmationTimings = invalidTimings }); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && alerts.Notices(snapshot).Count(n => n.Level == MonitorNoticeLevel.Critical) == 2,
                "invalid restore rejects before changing active primary alarms");
            alerts.RestorePreferences(saved);
            Read();
            Require(Notice(MonitorNumeric.HeartRate) is null && Notice(MonitorNumeric.SpO2) is null, "restoring identical preferences never restores active evidence");
            Confirm();
            var previous = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.RateSeed.Text = "invalid";
            window.RestartSettings();
            Require(ReferenceEquals(previous, window.Session) && alerts.Notices(snapshot).Count(n => n.Level == MonitorNoticeLevel.Critical) == 2,
                "failed restart preserves primary evidence");
            window.Settings.CardiacRateEnabled.IsChecked = false;
            window.RestartSettings();
            window.Pause();
            Require(!ReferenceEquals(previous, window.Session) && !alerts.Notices(snapshot).Any(n => n.Level == MonitorNoticeLevel.Critical),
                "successful restart clears primary timers even when the previous snapshot is reread");
            alerts.SpO2Confirmation.Fields[0].Value = null;
            Read();
            Require(Notice(MonitorNumeric.SpO2) is null && window.MonitorView.ActiveNotices.Any(n => n.Id == "spo2-settings"),
                "invalid SpO2 duration reports a settings fault");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Primary alarm confirmation: " + message); }
    }
}
