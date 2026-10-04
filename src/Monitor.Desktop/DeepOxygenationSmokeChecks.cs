// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Media;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class DeepOxygenationSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.OpticalTarget.Value = 0;
            window.Settings.OpticalVariation.Value = 0;
            window.Settings.Alerts.SpO2Enabled.IsChecked = true;
            window.RestartSettings();
            window.Pause();
            Require(window.Settings.OpticalTarget.Minimum == 0 && window.Settings.OpticalTarget.Value == 0,
                "editor accepts physical zero without a75% floor");
            while (window.Session.SimulationTimeNs < 12_000_000_000) { window.Session.Advance(200_000_000); }
            window.MonitorView.RefreshReadings(window.Session.Measurements!);
            Require(int.TryParse(window.MonitorView.NumericTexts[1], out int zeroTargetReading) && zeroTargetReading is >= 0 and <= 4 &&
                window.Session.Measurements!.SpO2.Status == WaveformMeasurementStatus.Valid && window.MonitorView.HighestNotice == MonitorNoticeLevel.Critical,
                "the real Apply path measures a numerical deep value from a zero target and raises the enabled low alarm");
            var saved = window.Settings.CaptureGenerator();
            window.Settings.OpticalTarget.Value = 98;
            window.Settings.RestoreGenerator(saved);
            Require(window.Settings.OpticalTarget.Value == 0, "editor preferences retain a deep target");

            var trace = new SampledArterialOxygenation(new(0, 1_000_000_000,
                Enumerable.Range(0, 49).Select(t => t < 32 ? 40000 : t < 40 ? 40000 + (t - 32) * 7250 : 98000).ToArray()));
            var configuration = PhysiologyIllustrationConfiguration.Default with
            { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 15, MechanicalDurationCycles = 15 };
            var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true, oxygenation: trace);
            var view = new LiveMonitorView(new LiveMonitorTrace(session)) { AdditionalNotices = window.Settings.Alerts.Notices };
            bool deep = false, lost = false, recoveredLow = false, normal = false;
            while (session.SimulationTimeNs < 48_000_000_000)
            {
                session.Advance(200_000_000);
                var snapshot = session.Measurements!;
                view.RefreshReadings(snapshot);
                view.RefreshNumericHighlights(0);
                window.Settings.Sound.UpdateAlarm(view.HighestNotice, window.Settings.Alerts.Timing, measurement: snapshot);
                long time = session.SimulationTimeNs;
                if (time is >= 8_000_000_000 and < 14_000_000_000 || time is >= 30_000_000_000 and < 34_000_000_000)
                {
                    Require(snapshot.SpO2.Status == WaveformMeasurementStatus.Valid && snapshot.SpO2.SaturationMilliPercent < 45000 &&
                        int.TryParse(view.NumericTexts[1], out int numeric) && numeric < 45 &&
                        view.HighestNotice == MonitorNoticeLevel.Critical && window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical &&
                        window.Settings.Sound.BeatPitchPercent == 70 &&
                        (LiveMonitorView.NumericBackground(view.NumericBlocks[2]) as ISolidColorBrush)?.Color == Color.Parse("#ffb51f2c"),
                        "deep paired optics drive number, critical banner, red highlight and sound request");
                    deep = true;
                    recoveredLow |= time >= 30_000_000_000;
                }
                if (time is >= 18_000_000_000 and < 26_000_000_000)
                {
                    Require(view.NumericTexts[1] == "---" && snapshot.SpO2.Status == WaveformMeasurementStatus.PoorSignal &&
                        view.ActiveNotices.Any(n => n.Text == "SpO₂信号质量不足") &&
                        !view.ActiveNotices.Any(n => n.Id == "spo2-low") && window.Settings.Sound.PublishedAlarm?.Level != MonitorNoticeLevel.Critical,
                        "actual pulse loss replaces the low number and alarm with an explicit signal-quality notice");
                    lost = true;
                }
                if (time >= 46_000_000_000)
                {
                    Require(view.NumericTexts[1] == "98" && !view.ActiveNotices.Any(n => n.Id == "spo2-low") &&
                        window.Settings.Sound.PublishedAlarm?.Level != MonitorNoticeLevel.Critical &&
                        (LiveMonitorView.NumericBackground(view.NumericBlocks[2]) as ISolidColorBrush)?.Color == Colors.Transparent,
                        "measured normal recovery clears the banner, highlight and critical sound request together");
                    normal = true;
                }
            }
            Require(deep && lost && recoveredLow && normal, "all native deep-oxygenation stages executed");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Deep oxygenation: " + message); }
    }
}
