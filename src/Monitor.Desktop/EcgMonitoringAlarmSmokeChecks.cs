// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class EcgMonitoringAlarmSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            Require(alerts.EcgMonitoringEnabled.IsChecked == true && alerts.CapturePreferences().EcgMonitoringEnabled,
                "default skin enables detector alarms and captures their preference");
            alerts.NotificationSettings.Editors["ecg-asystole"].LatchUntilAcknowledged.IsChecked = true;
            var empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
            var snapshot = empty with
            {
                EcgMonitoring = new(WaveformMeasurementStatus.Valid, false, EcgMonitoringConditions.Asystole, 0, null,
                    new(WaveformMeasurementStatus.Valid, -125, WaveformMeasurementStatus.Valid, 420, 480, 30))
            };
            void Read()
            {
                window.MonitorView.RefreshReadings(snapshot);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot,
                    notices: window.MonitorView.ActiveNotices);
            }
            Read();
            Require(window.MonitorView.ActiveNotices.Any(n => n.Id == "ecg-asystole" && n.Level == MonitorNoticeLevel.Critical) &&
                window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical, "confirmed detector result reaches the default banner and sound request");
            Require(window.MonitorView.NumericBlocks[1].Text == "ST -0.125 mV\nQT 420 / QTc 480 ms", "ST has voltage units and QT intervals retain measured milliseconds");
            snapshot = snapshot with { SampleTimeNs = 200_000_000, EcgMonitoring = snapshot.EcgMonitoring with { ActiveConditions = EcgMonitoringConditions.None } };
            Read();
            Require(window.MonitorView.ActiveNotices.Any(n => n.Id == MonitorAlertSettings.RetainedNoticePrefix + "ecg-asystole" && !n.Audible) &&
                window.Settings.Sound.PublishedAlarm is null, "unacknowledged recovery is retained silently");
            alerts.EcgMonitoringEnabled.IsChecked = false;
            Read();
            Require(!window.MonitorView.ActiveNotices.Any(n => n.Id.Contains("ecg-asystole", StringComparison.Ordinal)), "disabling clears retained ECG attention");
            alerts.EcgMonitoringEnabled.IsChecked = true;
            snapshot = snapshot with
            {
                SampleTimeNs = 400_000_000,
                EcgMonitoring = snapshot.EcgMonitoring with
                { ActiveConditions = EcgMonitoringConditions.VentricularTachycardia | EcgMonitoringConditions.PvcsPerMinuteHigh },
                EcgRhythm = new(WaveformMeasurementStatus.Valid, true, true, null)
            };
            Read();
            Require(window.MonitorView.ActiveNotices.Any(n => n.Id == "ecg-vt") &&
                !window.MonitorView.ActiveNotices.Any(n => n.Id is "ecg-pvc-rate" or "ecg-af" or "ecg-irregular") &&
                window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical,
                "default banner and sound honor the red arrhythmia priority chain");
            snapshot = snapshot with
            {
                SampleTimeNs = 600_000_000,
                EcgMonitoring = snapshot.EcgMonitoring with { ActiveConditions = EcgMonitoringConditions.None }
            };
            Read();
            Require(window.MonitorView.ActiveNotices.Any(n => n.Id == "ecg-af") &&
                !window.MonitorView.ActiveNotices.Any(n => n.Id == "ecg-irregular") &&
                window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Warning,
                "AF blocks the redundant irregular-rhythm banner when the red alarm ends");
            alerts.EcgMonitoringEnabled.IsChecked = false;
            var preferences = alerts.CapturePreferences();
            alerts.EcgMonitoringEnabled.IsChecked = true;
            alerts.RestorePreferences(preferences);
            Require(alerts.EcgMonitoringEnabled.IsChecked == false, "ECG group switch restores from preferences");
            foreach (var skin in Enum.GetValues<MonitorSkin>())
            {
                var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(skin), true);
                var localization = new DesktopLocalization("zh-CN");
                var view = new LiveMonitorView(new LiveMonitorTrace(session), localization);
                var host = new Window { Content = view, Width = 1180, Height = 800 };
                host.Show();
                try
                {
                    view.RefreshReadings(snapshot);
                    DesktopViewportSmokeChecks.Layout(host, 1180, 800);
                    var readout = view.NumericBlocks[1];
                    Require(readout.IsVisible && readout.Bounds.Width <= 166 && readout.Bounds.Height > 0,
                        "ST/QT readout fits the numeric column for " + skin + ": " + readout.Bounds);
                    localization.Select("en");
                    Require(AutomationProperties.GetName(readout) == "Measured ST, QT and QTc" && readout.Text!.Contains("-0.125", StringComparison.Ordinal),
                        "paused readout keeps measured values while its accessible label changes language");
                    view.RefreshReadings(snapshot with
                    {
                        EcgMonitoring = snapshot.EcgMonitoring with
                        {
                            Repolarization = new(WaveformMeasurementStatus.Valid, 0, WaveformMeasurementStatus.Uncountable, 420, 480, 30)
                        }
                    });
                    Require(readout.Text == "ST 0.000 mV\nQT -?- / QTc -?- ms", "zero ST remains a valid measurement while invalid QT does not leak a stale value");
                    view.RefreshReadings(snapshot with { EcgMonitoring = snapshot.EcgMonitoring with { Learning = true } });
                    Require(readout.Text == "ST --- mV\nQT --- / QTc --- ms", "learning hides stale repolarization measurements");
                }
                finally { host.Close(); }
            }
            alerts.EcgMonitoringEnabled.IsChecked = true;
            alerts.Reset();
            var live = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent }, MonitorDisplayConfiguration.Default(), true);
            var liveView = new LiveMonitorView(new LiveMonitorTrace(live))
            {
                AdditionalNotices = sample => alerts.Notices(sample, live.DetectedMonitoringEvents, live.DetectedRhythmEvents)
            };
            for (int frame = 0; frame < 1200; frame++) { live.Advance(16_666_667); liveView.Refresh(); }
            Require(liveView.ActiveNotices.Any(n => n.Id == "ecg-asystole"), "sampled absent cardiac activity reaches the banner at ordinary frame cadence");
        }
        finally { window.Close(); }
    }

    internal static void VerifyRepolarizationRecovery()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.VtPreset, MonitorDisplayConfiguration.Default(), true);
        var view = new LiveMonitorView(new LiveMonitorTrace(session));
        var host = new Window { Content = view, Width = 1180, Height = 800 };
        host.Show();
        try
        {
            void AdvanceTo(long targetNs)
            {
                while (session.SimulationTimeNs < targetNs) { session.Advance(200_000_000); view.Refresh(); }
            }
            AdvanceTo(30_000_000_000);
            Require(view.NumericBlocks[1].Text!.Contains("-?-", StringComparison.Ordinal), "abnormal startup exposes unavailable repolarization");
            session.ScheduleSource(new(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true), 0);
            AdvanceTo(75_000_000_000);
            Require(session.Measurements!.EcgMonitoring is
            { LastBeat.Label: EcgBeatLabel.Normal, Repolarization.StStatus: WaveformMeasurementStatus.Valid, Repolarization.QtStatus: WaveformMeasurementStatus.Valid } &&
                !view.NumericBlocks[1].Text!.Contains("-?-", StringComparison.Ordinal) && !view.NumericBlocks[1].Text!.Contains("---", StringComparison.Ordinal),
                "continuing the same live monitor with sinus rhythm restores actual ST/QT numbers without restart");
        }
        finally { host.Close(); }
    }

    internal static void VerifyLongQtRonT()
    {
        var window = new DesignPreviewWindow();
        var alerts = window.Settings.Alerts;
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.PrematureVentricular with
        { ConductionPattern = AvConductionPattern.RonTLongQtPvcIllustration }, MonitorDisplayConfiguration.Default(), true);
        var view = new LiveMonitorView(new LiveMonitorTrace(session))
        {
            AdditionalNotices = sample => alerts.Notices(sample, session.DetectedMonitoringEvents, session.DetectedRhythmEvents)
        };
        window.Show();
        try
        {
            bool announced = false;
            for (int frame = 0; frame < 2400; frame++)
            {
                session.Advance(16_666_667);
                view.Refresh();
                if (!view.ActiveNotices.Any(n => n.Id == "ecg-ron-t")) { continue; }
                announced = true;
                Require(!view.ActiveNotices.Any(n => n.Id == "ecg-pvc-rate"), "R-on-T suppresses the lower PVC-frequency notice");
            }
            Require(announced, "acquired long-QT R-on-T preset reaches the default skin at ordinary frame cadence");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("ECG monitoring UI: " + message); }
    }
}
