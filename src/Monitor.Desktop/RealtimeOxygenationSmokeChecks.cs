// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal static class RealtimeOxygenationSmokeChecks
{
    internal static void Verify()
    {
        string path = Path.Combine(Path.GetTempPath(), "monitor-oxygenation-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        window.Pause();
        try
        {
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.Oxygenation.Realtime.IsChecked = true;
            window.Settings.Oxygenation.Patient.UseDefaults.IsChecked = false; // Preserve the authored prototype regression scenario.
            window.Settings.Alerts.SpO2Enabled.IsChecked = true;
            window.Settings.Restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Pause();
            var session = window.Session;
            Require(session.Oxygenation is not null && !window.Settings.OpticalTarget.IsEnabled &&
                !window.Settings.OpticalVariation.IsEnabled && window.Settings.OpticalModulation.IsEnabled,
                "Apply selects realtime optical input and disables fixed saturation drafts");
            var saved = new DisplayPreferenceStore(path).Load(out bool rejected);
            Require(!rejected && saved.Generator?.Oxygenation?.Realtime == true, "realtime mode survives preference serialization");
            window.Settings.RestoreGenerator(saved.Generator! with { Oxygenation = null });
            Require(window.Settings.Oxygenation.Realtime.IsChecked != true, "old preferences restore the legacy mode");
            window.Settings.Oxygenation.TidalVolume.Value = null;
            Require(window.Settings.CaptureGenerator().Oxygenation is null, "dormant invalid ventilation drafts do not block saving the legacy source");
            window.Settings.RestoreGenerator(saved.Generator!);
            AdvanceTo(30_000_000_000);
            Require(session.Measurements!.SpO2.SaturationMilliPercent > 96000, "reference ventilation starts with a measured normal value");
            var before = session.Oxygenation;
            window.Settings.Oxygenation.TidalVolume.Value = 0;
            window.Settings.Oxygenation.UpdateVentilation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(ReferenceEquals(session, window.Session) && session.SimulationTimeNs == 30_000_000_000 && session.Oxygenation == before,
                "live button retains the active session and oxygen reservoirs");
            AdvanceTo(180_000_000_000);
            Refresh();
            Require(session.Measurements!.SpO2.Status == WaveformMeasurementStatus.Valid &&
                int.TryParse(window.MonitorView.NumericTexts[1], out int deep) && deep < 70 &&
                window.MonitorView.HighestNotice == MonitorNoticeLevel.Critical &&
                window.Settings.Sound.PublishedAlarm?.Level == MonitorNoticeLevel.Critical,
                "real model desaturation drives deep numeric display, visual alarm and sound request");
            window.Settings.Oxygenation.InspiredOxygen.Value = null;
            before = session.Oxygenation;
            window.Settings.Oxygenation.UpdateVentilation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(session.Oxygenation == before && window.Settings.Status.Text!.Contains("无效", StringComparison.Ordinal),
                "invalid live edits preserve current physiology");
            window.Settings.Oxygenation.InspiredOxygen.Value = 21;
            window.Settings.Oxygenation.TidalVolume.Value = 450;
            window.Settings.Oxygenation.UpdateVentilation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            AdvanceTo(300_000_000_000);
            Refresh();
            Require(session.Measurements!.SpO2.SaturationMilliPercent > 95000 &&
                window.MonitorView.HighestNotice is null && window.Settings.Sound.PublishedAlarm is null,
                "restored ventilation clears the low oxygen alarm after measured recovery");
            Console.WriteLine("PASS: realtime oxygenation controls, preferences, deep measurement and alarm recovery");

            void AdvanceTo(long targetNs)
            {
                while (session.SimulationTimeNs < targetNs) { session.Advance(250_000_000); }
            }
            void Refresh()
            {
                window.MonitorView.RefreshReadings(session.Measurements!);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, window.Settings.Alerts.Timing,
                    measurement: session.Measurements!);
            }
        }
        finally { window.Close(); if (File.Exists(path)) { File.Delete(path); } }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
