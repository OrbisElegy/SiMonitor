// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class NoExpirationAlarmSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            alerts.NoExpirationEnabled.IsChecked = true;
            alerts.NoExpirationSeconds.Value = 5;
            alerts.NoExpirationTriggerSeconds.Value = .4m;
            alerts.NoExpirationRecoverySeconds.Value = .3m;
            var snapshot = LiveWaveformMeasurements.CreateIllustration().Read(0);
            long nowNs = 5_000_000_000;
            void Read(bool expiration = false, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid)
            {
                snapshot = snapshot with
                {
                    SampleTimeNs = nowNs,
                    HeartRate = new(WaveformMeasurementStatus.Valid, 180001, nowNs),
                    Capnography = snapshot.Capnography with
                    {
                        Activity = new(status, 0, expiration ? nowNs : null, nowNs),
                        RespirationsMilliPerMinute = new(WaveformMeasurementStatus.Stale, null, null)
                    }
                };
                nowNs += 100_000_000;
                window.MonitorView.RefreshReadings(snapshot);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot);
            }
            MonitorNotice? Absence() => window.MonitorView.ActiveNotices.SingleOrDefault(n => n.Id == "co2-no-expiration");
            void Confirm()
            {
                for (int i = 0; i < 5; i++) { Read(); }
                Require(Absence() is { Level: MonitorNoticeLevel.Critical, Numeric: MonitorNumeric.Co2RespirationRate },
                    "continuous sample activity confirms despite missing numeric respiratory rate");
            }
            for (int i = 0; i < 4; i++) { Read(); Require(Absence() is null, "extra trigger does not fire early"); }
            Read();
            Require(Absence() is not null && window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical,
                "confirmed absence reaches banner and sound arbitration");
            var groups = alerts.AdditionalLimits.Editors[MonitorNumeric.EtCo2].Confirmation.Groups;
            var request = window.Settings.Sound.PublishedAlarm;
            groups.SelectedIndex = 3; groups.SelectedIndex = 0; groups.SelectedIndex = 3;
            Read();
            Require(Absence() is not null && window.Settings.Sound.PublishedAlarm == request, "navigation preserves confirmation and sound");
            Read(expiration: true); Read(expiration: true); Read(expiration: true);
            Require(Absence() is not null, "accepted expiration starts recovery without clearing early");
            Read(expiration: true);
            Require(Absence() is null && window.Settings.Sound.PublishedAlarm?.Level != MonitorNoticeLevel.Critical,
                "confirmed recovery clears banner and sound together");
            Confirm();
            alerts.HeartRateEnabled.IsChecked = true;
            alerts.NoExpirationTriggerSeconds.Value = .5m; alerts.NoExpirationTriggerSeconds.Value = .4m;
            Read();
            Require(Absence() is null && window.MonitorView.ActiveNotices.Any(n => n.Numeric == MonitorNumeric.HeartRate && n.Level == MonitorNoticeLevel.Critical),
                "edit and revert resets only the absence alarm");
            Confirm();
            alerts.NoExpirationEnabled.IsChecked = false; alerts.NoExpirationEnabled.IsChecked = true;
            Read(); Require(Absence() is null, "toggle and revert between samples discards evidence");
            Confirm();
            alerts.NoExpirationSeconds.Value = 6; alerts.NoExpirationSeconds.Value = 5;
            Read(); Require(Absence() is null, "detection-window edit also resets only this condition");
            Confirm();
            Read(status: WaveformMeasurementStatus.PoorSignal);
            Require(Absence() is null, "poor quality clears physiological projection");
            Confirm();
            var saved = alerts.CapturePreferences();
            bool rejected = false;
            try { alerts.RestorePreferences(saved with { NoExpirationConfirmation = new(-1, 0) }); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && alerts.Notices(snapshot).Any(n => n.Id == "co2-no-expiration"), "invalid restoration is atomic");
            alerts.RestorePreferences(saved);
            Read(); Require(Absence() is null, "restoration does not restore pending or active state");
            Confirm();
            var previous = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.RateSeed.Text = "invalid";
            window.RestartSettings();
            Require(ReferenceEquals(previous, window.Session) && alerts.Notices(snapshot).Any(n => n.Id == "co2-no-expiration"),
                "failed restart preserves live confirmation");
            window.Settings.CardiacRateEnabled.IsChecked = false;
            window.RestartSettings(); window.Pause();
            Require(!ReferenceEquals(previous, window.Session) && !alerts.Notices(snapshot).Any(n => n.Id == "co2-no-expiration"),
                "successful restart clears confirmation");
            foreach (decimal? invalid in new decimal?[] { null, .0001m })
            {
                alerts.NoExpirationTriggerSeconds.Value = invalid;
                Read();
                Require(Absence() is null && window.MonitorView.ActiveNotices.Any(n => n is { Id: "co2-absence-settings", Audible: false }),
                    "invalid confirmation reports a silent settings fault");
                var page = (Control)((TabItem)groups.Items[3]!).Content!;
                Require(page.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.IsVisible && t.Text?.Contains("最多三位小数", StringComparison.Ordinal) == true),
                    "invalid draft has an immediate in-page correction message");
                bool captureRejected = false;
                try { alerts.CapturePreferences(); } catch (ArgumentException) { captureRejected = true; }
                Require(captureRejected, "invalid draft cannot be saved");
            }
            var absencePage = (Control)((TabItem)groups.Items[3]!).Content!;
            var reset = absencePage.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "恢复默认确认时间"));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(alerts.CapturePreferences().NoExpirationConfirmation == new BoundaryConfirmationTiming(0, 0) &&
                alerts.NoExpirationSeconds.Value == 5 && alerts.NoExpirationEnabled.IsChecked == true,
                "default confirmation reset preserves detection interval and opt-in");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("No-expiration alarm: " + message); } }
}
