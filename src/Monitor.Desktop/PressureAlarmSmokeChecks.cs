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
            window.RestartSettings();
            Require(ReferenceEquals(previous, window.Session) && alerts.Notices(snapshot).Any(n => n.Numeric == MonitorNumeric.AbpMean && n.Level == MonitorNoticeLevel.Critical),
                "failed Apply retains the running session and confirmed alarm");
            window.Settings.CardiacRateEnabled.IsChecked = false;
            window.RestartSettings();
            window.Pause();
            Require(!ReferenceEquals(previous, window.Session) && !alerts.Notices(snapshot).Any(n => n.Numeric == MonitorNumeric.AbpMean),
                "successful Apply resets the alarm even if the old snapshot is read again");
        }
        finally { window.Close(); }
        VerifyConfigurableTiming();
    }

    private static void VerifyConfigurableTiming()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            var pressure = alerts.AdditionalLimits.Editors[MonitorNumeric.AbpMean];
            var pulse = alerts.AdditionalLimits.Editors[MonitorNumeric.PulseRate];
            pressure.Enabled.IsChecked = true;
            pulse.Enabled.IsChecked = true;
            foreach (var field in pressure.Confirmation.Fields) { field.Value = 0; }
            pulse.Confirmation.Fields[4].Value = .4m;
            pulse.Confirmation.Fields[5].Value = .2m;
            pulse.Confirmation.Fields[6].Value = .6m;
            pulse.Confirmation.Fields[7].Value = .3m;
            long now = 0;
            void Read(int pulseRate = 180001, int meanPressure = 3999)
            {
                var snapshot = LiveWaveformMeasurements.CreateIllustration().Read(0) with
                {
                    SampleTimeNs = now,
                    PulseRate = new(WaveformMeasurementStatus.Valid, pulseRate, now),
                    AbpMean = new(WaveformMeasurementStatus.Valid, meanPressure, 0, now)
                };
                now += 100_000_000;
                window.MonitorView.RefreshReadings(snapshot);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot);
            }
            MonitorNotice? Notice(MonitorNumeric numeric) => window.MonitorView.ActiveNotices.SingleOrDefault(n => n.Numeric == numeric);
            Read();
            Require(Notice(MonitorNumeric.AbpMean)?.Level == MonitorNoticeLevel.Critical && Notice(MonitorNumeric.PulseRate) is null,
                "zero pressure confirmation is immediate while pulse confirmation waits independently");
            for (int i = 0; i < 3; i++) { Read(); }
            Read();
            Require(Notice(MonitorNumeric.PulseRate)?.Level == MonitorNoticeLevel.Warning, "custom pulse warning confirms at 400 ms");
            Read(); Read();
            Require(Notice(MonitorNumeric.PulseRate)?.Level == MonitorNoticeLevel.Critical, "custom pulse Critical confirms at 600 ms");
            pressure.Confirmation.Fields[0].Value = 1;
            Read();
            Require(Notice(MonitorNumeric.AbpMean)?.Level == MonitorNoticeLevel.Warning &&
                Notice(MonitorNumeric.PulseRate)?.Level == MonitorNoticeLevel.Critical,
                "editing pressure clears its Critical evidence without resetting pulse");
            Read(120000, 6000);
            Require(Notice(MonitorNumeric.AbpMean) is null && Notice(MonitorNumeric.PulseRate)?.Level == MonitorNoticeLevel.Critical,
                "zero pressure recovery and delayed pulse recovery remain separate");
            Read(120000, 6000); Read(120000, 6000); Read(120000, 6000);
            Require(Notice(MonitorNumeric.PulseRate) is null && window.Settings.Sound.PublishedAlarm?.Level != MonitorNoticeLevel.Critical,
                "configured recovery clears banner and shared alarm audio");
            for (int i = 0; i < 7; i++) { Read(); }
            pulse.Confirmation.Fields[6].Value = .8m;
            pulse.Confirmation.Fields[6].Value = .6m;
            Read();
            Require(Notice(MonitorNumeric.PulseRate) is null, "editing then reverting a duration between samples resets evidence");
            pulse.Confirmation.Fields[0].Value = null;
            Read();
            Require(Notice(MonitorNumeric.PulseRate) is null && window.MonitorView.ActiveNotices.Any(n => n.Id == "pleth-rate-settings"),
                "incomplete timing is an explicit settings fault, never a physiological alarm");
            bool rejected = false;
            try { alerts.CapturePreferences(); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "invalid timing cannot be captured or saved");
            pulse.Confirmation.Restore(MeasurementConfirmationTiming.DefaultFor(MonitorNumeric.PulseRate));
            Read();
            Require(Notice(MonitorNumeric.PulseRate)?.Level == MonitorNoticeLevel.Critical,
                "restored immediate defaults take effect on fresh evidence");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Pressure alarm: " + message); }
    }
}
