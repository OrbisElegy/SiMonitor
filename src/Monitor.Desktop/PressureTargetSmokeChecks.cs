// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class PressureTargetSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            var settings = window.Settings;
            Require(settings.AbpPulseGain.IsEnabled && !settings.AbpSystolic.IsEnabled && !settings.PaDiastolic.IsEnabled,
                "targets start off and leave the pulse factors editable");
            settings.AbpTargetEnabled.IsChecked = true;
            settings.PaTargetEnabled.IsChecked = true;
            Require(!settings.AbpPulseGain.IsEnabled && !settings.PaPulseGain.IsEnabled && settings.AbpSystolic.IsEnabled && settings.PaDiastolic.IsEnabled,
                "a selected target replaces the pulse factor of its channel");
            settings.AbpSystolic.Value = 130;
            settings.AbpDiastolic.Value = 85;
            settings.PaSystolic.Value = 30;
            settings.PaDiastolic.Value = 12;
            window.RestartSettings();
            var session = window.Session;
            for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            var measurements = session.Measurements!;
            Require(Near(measurements.AbpMean.Pulse, 13000, 8500) && Near(measurements.PaMean.Pulse, 3000, 1200),
                "applied targets are measured from the generated ABP and PA waveforms");

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
        }
        finally { window.Close(); }
    }

    private static bool Near(PulsePressureReading? reading, int systolicCentiMmHg, int diastolicCentiMmHg) =>
        reading is { Status: WaveformMeasurementStatus.Valid, SystolicCentiMmHg: { } systolic, DiastolicCentiMmHg: { } diastolic } &&
        Math.Abs(systolic - systolicCentiMmHg) <= 100 && Math.Abs(diastolic - diastolicCentiMmHg) <= 100;

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Pressure targets: " + message); } }
}
