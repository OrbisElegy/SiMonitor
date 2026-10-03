// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Media;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class PressureAlarmSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            var editor = alerts.AdditionalLimits.Editors[MonitorNumeric.AbpMean];
            editor.Enabled.IsChecked = true;
            while (window.Session.SimulationTimeNs < 12_000_000_000) { window.Session.Advance(200_000_000); }
            var snapshot = window.Session.Measurements!;
            void Read(int value, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid)
            {
                window.Session.Advance(200_000_000);
                snapshot = window.Session.Measurements!;
                snapshot = snapshot with { AbpMean = new(status, value, 0, snapshot.SampleTimeNs) };
                window.MonitorView.RefreshReadings(snapshot);
                window.MonitorView.RefreshNumericHighlights(0);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot);
            }
            MonitorNotice? PressureNotice() => window.MonitorView.ActiveNotices.SingleOrDefault(n => n.Numeric == MonitorNumeric.AbpMean);
            void Confirm()
            {
                for (int i = 0; i <= 20; i++) { Read(3999); }
                Require(PressureNotice()?.Level == MonitorNoticeLevel.Critical, "continuous low pressure reaches Critical in the live view");
            }
            for (int i = 0; i < 20; i++)
            {
                Read(3999);
                Require(PressureNotice() is null && window.Settings.Sound.PublishedAlarm?.Level != MonitorNoticeLevel.Critical,
                    "pending pressure produces neither a physiological banner nor Critical audio");
            }
            Read(3999);
            var request = window.Settings.Sound.PublishedAlarm;
            Require(request?.Level == MonitorNoticeLevel.Critical, "confirmed pressure reaches the shared sound request");
            var number = window.MonitorView.NumericBlocks.Single(b => AutomationProperties.GetName(b) == "ABP 平均压   mmHg");
            for (int i = 0; i < 100; i++)
            {
                Read(i % 2 == 0 ? 4001 : 3999);
                Require(PressureNotice()?.Level == MonitorNoticeLevel.Critical && window.Settings.Sound.PublishedAlarm == request &&
                    (LiveMonitorView.NumericBackground(number) as ISolidColorBrush)?.Color == Color.Parse("#ffb51f2c"),
                    "boundary chatter preserves banner, red numeric highlight and sequencer request");
            }
            for (int i = 0; i < 15; i++)
            {
                Read(6000);
                Require(PressureNotice()?.Level == MonitorNoticeLevel.Critical, "display and audio retain confirmed state during recovery");
            }
            Read(6000);
            Require(PressureNotice() is null && window.Settings.Sound.PublishedAlarm?.Level != MonitorNoticeLevel.Critical &&
                (LiveMonitorView.NumericBackground(number) as ISolidColorBrush)?.Color == Colors.Transparent,
                "stable recovery clears pressure banner, highlight and Critical sound together");
            Confirm();
            Read(3999, WaveformMeasurementStatus.PoorSignal);
            Require(PressureNotice() is null, "quality failure clears immediately through the real presentation path");
            Confirm();
            editor.Enabled.IsChecked = false;
            editor.Enabled.IsChecked = true;
            Read(3999);
            Require(PressureNotice() is null, "toggle off/on between samples clears accumulated state");
            Confirm();
            editor.CriticalLow.Value = 39;
            editor.CriticalLow.Value = 40;
            Read(3999);
            Require(PressureNotice() is null, "editing a threshold and reverting before the next read still resets evidence");
            Confirm();
            alerts.RestorePreferences(alerts.CapturePreferences());
            Read(3999);
            Require(PressureNotice() is null, "restoring the same preferences never restores an episode");
            Confirm();
            var previous = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.RateSeed.Text = "invalid";
            window.ApplySettings();
            Require(ReferenceEquals(previous, window.Session) && alerts.Notices(snapshot).Any(n => n.Numeric == MonitorNumeric.AbpMean && n.Level == MonitorNoticeLevel.Critical),
                "failed Apply retains the running session and confirmed alarm");
            window.Settings.CardiacRateEnabled.IsChecked = false;
            window.ApplySettings();
            window.Pause();
            Require(!ReferenceEquals(previous, window.Session) && !alerts.Notices(snapshot).Any(n => n.Numeric == MonitorNumeric.AbpMean),
                "successful Apply resets the alarm even if the old snapshot is read again");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Pressure alarm: " + message); }
    }
}
