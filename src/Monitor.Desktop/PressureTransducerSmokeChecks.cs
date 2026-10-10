// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class PressureTransducerSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-pressure-range-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        try
        {
            var editor = window.Settings.PressureTransducers;
            Require(editor.Read() == new PressureTransducerLimits(), "default ranges are -50 mmHg");
            editor.Abp.Value = 20;
            editor.Pa.Value = -2;
            editor.Cvp.Value = -4;
            editor.AbpPulse.Value = 100;
            editor.PaPulse.Value = 80;
            window.Settings.Skin.SelectedIndex = (int)MonitorSkin.SevenRows;
            window.RestartSettings();
            window.Pause();
            Require(window.Session.PressureTransducers == new PressureTransducerLimits(2000, -200, -400, 10000, 8000), "apply reaches the actual pressure readings");
            for (int i = 0; i < 60; i++) { window.Session.Advance(200_000_000); }
            window.MonitorView.RefreshReadings(window.Session.Measurements!);
            Require(window.Session.Measurements!.AbpMean is { Status: WaveformMeasurementStatus.Valid, Pulse.Status: WaveformMeasurementStatus.PoorSignal } &&
                window.MonitorView.NumericBlocks[5].Text == "SYS/DIA ---/---" && window.MonitorView.NumericBlocks[4].Text != "---",
                "weak pulse hides SYS/DIA while retaining mean");
            var alerts = window.Settings.Alerts;
            alerts.AdditionalLimits.Editors[MonitorNumeric.AbpMean].Enabled.IsChecked = true;
            for (int i = 0; i <= 20; i++)
            {
                window.Session.Advance(200_000_000);
                var snapshot = window.Session.Measurements!;
                snapshot = window.Session.PressureTransducers.Apply(snapshot with
                {
                    AbpMean = new(WaveformMeasurementStatus.Valid, 3000, 0, snapshot.SampleTimeNs)
                    { Pulse = new(WaveformMeasurementStatus.Valid, 3500, 2500, snapshot.SampleTimeNs) { LatestAmplitudeCentiMmHg = 1000 } }
                });
                window.MonitorView.RefreshReadings(snapshot);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot);
            }
            Require(window.MonitorView.NumericBlocks[4].Text == "30" && window.MonitorView.NumericBlocks[5].Text == "SYS/DIA ---/---" &&
                window.MonitorView.ActiveNotices.Any(n => n.Id == "abp-mean-low" && n.Level == MonitorNoticeLevel.Critical) &&
                window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical,
                "weak pulsation retains measurable mean, low-pressure banner and sound");
            for (int i = 0; i <= 20; i++)
            {
                window.Session.Advance(200_000_000);
                var snapshot = window.Session.Measurements!;
                snapshot = window.Session.PressureTransducers.Apply(snapshot with
                {
                    AbpMean = new(WaveformMeasurementStatus.Valid, 1000, 0, snapshot.SampleTimeNs)
                    { Pulse = new(WaveformMeasurementStatus.Valid, 1500, 500, snapshot.SampleTimeNs) { LatestAmplitudeCentiMmHg = 1000 } }
                });
                window.MonitorView.RefreshReadings(snapshot);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot);
            }
            Require(window.MonitorView.NumericBlocks[4].Text == "---" &&
                window.MonitorView.ActiveNotices.Any(n => n.Id == "abp-mean-low" && n.Level == MonitorNoticeLevel.Critical) &&
                window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical,
                "below-range dashes retain the low-pressure banner and alarm sound");
            var previous = window.Session;
            editor.Abp.Value = null;
            window.ApplySettings();
            Require(editor.Error.IsVisible && ReferenceEquals(previous, window.Session), "invalid drafts leave the active session unchanged");
            window.Close();
            var restored = new DesignPreviewWindow(path);
            restored.Show();
            try
            {
                Require(restored.Settings.PressureTransducers.Read() == new PressureTransducerLimits(2000, -200, -400, 10000, 8000), "saved ranges reopen independently");
                restored.Localization.Select("en");
                restored.ResetAllSettings();
                Require(restored.Settings.PressureTransducers.Read() == new PressureTransducerLimits(), "reset all restores device defaults");
            }
            finally { restored.Close(); }
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Pressure transducer: " + message); }
    }
}
