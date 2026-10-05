// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class PressureTargetSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pressure-target-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        try
        {
            var settings = window.Settings;
            Require(settings.AbpTargetEnabled.IsChecked == true && settings.PaTargetEnabled.IsChecked == true &&
                settings.CvpBaseline.Value == 6 && !settings.AbpPulseGain.IsEnabled && settings.AbpSystolic.IsEnabled && settings.PaDiastolic.IsEnabled,
                "new settings enable normal adult ABP/PA targets and retain CVP baseline 6");
            for (int i = 0; i < 240; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(Near(window.Session.Measurements!.AbpMean.Pulse, 12000, 8000) && Near(window.Session.Measurements.PaMean.Pulse, 2500, 1000),
                "cold startup uses the displayed default targets without an initial Apply");
            settings.AbpTargetEnabled.IsChecked = false;
            settings.PaTargetEnabled.IsChecked = false;
            Require(settings.AbpPulseGain.IsEnabled && !settings.AbpSystolic.IsEnabled && !settings.PaDiastolic.IsEnabled,
                "turning targets off restores pulse factor editing");
            settings.AbpTargetEnabled.IsChecked = true;
            settings.PaTargetEnabled.IsChecked = true;
            Require(!settings.AbpPulseGain.IsEnabled && !settings.PaPulseGain.IsEnabled && settings.AbpSystolic.IsEnabled && settings.PaDiastolic.IsEnabled,
                "a selected target replaces the pulse factor of its channel");
            settings.AbpSystolic.Value = 130;
            settings.AbpDiastolic.Value = 85;
            settings.PaSystolic.Value = 30;
            settings.PaDiastolic.Value = 12;
            settings.CvpBaseline.Value = 12;
            var session = window.Session;
            long time = session.SimulationTimeNs;
            settings.ApplyDelaySeconds.Value = 0;
            settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(ReferenceEquals(session, window.Session) && session.SimulationTimeNs == time && session.PendingSourceTimeNs == time,
                "ABP, PA and CVP changes queue together through the Apply button without restarting");
            for (int i = 0; i < 600; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            var measurements = session.Measurements!;
            Require(Near(measurements.AbpMean.Pulse, 13000, 8500) && Near(measurements.PaMean.Pulse, 3000, 1200),
                "applied targets are measured from the generated ABP and PA waveforms");
            Require(measurements.CvpMean.MeanCentiMmHg is >= 1190 and <= 1210, "combined Apply includes the CVP baseline");

            settings.AbpDiastolic.Value = 128;
            window.ApplySettings();
            Require(ReferenceEquals(session, window.Session) && settings.Status.Text == window.Localization.Get("validation.pressureTargetPulse"),
                "a pulse pressure below 5 mmHg is reported and keeps the running session");
            settings.AbpDiastolic.Value = 85;
            settings.EcgSelection = 23;
            window.ApplySettings();
            Require(ReferenceEquals(session, window.Session) && settings.Status.Text == window.Localization.Get("validation.pressureTargetUnreachable"),
                "unreachable targets for the selected rhythm are reported without applying");
            settings.EcgSelection = 0;

            var reopened = new DesignPreviewWindow(path);
            try
            {
                Require(reopened.Settings.AbpTargetEnabled.IsChecked == true && reopened.Settings.PaTargetEnabled.IsChecked == true &&
                    reopened.Settings.AbpSystolic.Value == 130 && reopened.Settings.AbpDiastolic.Value == 85 &&
                    reopened.Settings.PaSystolic.Value == 30 && reopened.Settings.PaDiastolic.Value == 12 && reopened.Settings.CvpBaseline.Value == 12,
                    "reopening preserves the last accepted targets rather than invalid drafts or startup defaults");
                for (int i = 0; i < 240; i++) { reopened.Session.Advance(50_000_000); }
                Require(Near(reopened.Session.Measurements!.AbpMean.Pulse, 13000, 8500) &&
                    Near(reopened.Session.Measurements.PaMean.Pulse, 3000, 1200), "saved targets configure the reopened sampling source");
            }
            finally { reopened.Close(); }
            var saved = settings.CaptureGenerator();
            Require(saved.Flags["AbpTargetEnabled"] && saved.Numbers["AbpSystolic"] == 130 && saved.Numbers["PaDiastolic"] == 12,
                "targets are part of the saved generator preferences");
            string[] later = ["AbpSystolic", "AbpDiastolic", "PaSystolic", "PaDiastolic", "AbpTargetEnabled", "PaTargetEnabled"];
            var older = saved with
            {
                Numbers = saved.Numbers.Where(pair => !later.Contains(pair.Key)).ToDictionary(),
                Flags = saved.Flags.Where(pair => !later.Contains(pair.Key)).ToDictionary()
            };
            var fresh = new DesignPreviewWindow();
            try
            {
                fresh.Settings.RestoreGenerator(older);
                Require(fresh.Settings.AbpTargetEnabled.IsChecked == false && fresh.Settings.AbpSystolic.Value == 120 && fresh.Settings.PaDiastolic.Value == 10,
                    "preferences saved before targets existed restore with targets off at their defaults");
                bool rejected = false;
                try { fresh.Settings.RestoreGenerator(older with { Numbers = older.Numbers.Where(pair => pair.Key != "CvpBaseline").ToDictionary() }); }
                catch (ArgumentException exception) { rejected = exception.Message == "GeneratorPreferences.FieldsMismatch"; }
                Require(rejected, "fields that always existed remain required");
            }
            finally { fresh.Close(); }
            window.ResetAllSettings();
            for (int i = 0; i < 240; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(window.Settings.AbpTargetEnabled.IsChecked == true && window.Settings.PaTargetEnabled.IsChecked == true &&
                window.Settings.CvpBaseline.Value == 6 && Near(window.Session.Measurements!.AbpMean.Pulse, 12000, 8000) &&
                Near(window.Session.Measurements.PaMean.Pulse, 2500, 1000), "reset restores enabled default pressure targets and CVP baseline");
        }
        finally
        {
            window.Close();
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    private static bool Near(PulsePressureReading? reading, int systolicCentiMmHg, int diastolicCentiMmHg) =>
        reading is { Status: WaveformMeasurementStatus.Valid, SystolicCentiMmHg: { } systolic, DiastolicCentiMmHg: { } diastolic } &&
        Math.Abs(systolic - systolicCentiMmHg) <= 100 && Math.Abs(diastolic - diastolicCentiMmHg) <= 100;

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Pressure targets: " + message); } }
}
