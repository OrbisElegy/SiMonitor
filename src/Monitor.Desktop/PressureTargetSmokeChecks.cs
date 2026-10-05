// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class PressureTargetSmokeChecks
{
    internal static void VerifyDriftApply()
    {
        string path = Path.Combine(Path.GetTempPath(), "pressure-drift-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new DesignPreviewWindow(path);
        var control = new DesignPreviewWindow();
        window.Show();
        control.Show();
        try
        {
            var session = window.Session;
            foreach (var current in new[] { window, control })
            {
                current.SelectPage(2);
                current.Settings.Tabs.SelectedIndex = 5;
                current.Settings.SectionPages[5].SelectedSection = 3;
                current.Settings.RateSeed.Text = new string('a', 64);
                current.Settings.AbpVariation.Value = 10;
                current.Settings.PaVariation.Value = 3;
                current.Settings.ApplyDelaySeconds.Value = 0;
                current.Settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 600; i++) { current.Pulse(current.ActiveTimer, 50_000_000); }
            }
            Require(ReferenceEquals(session, window.Session), "enabling both drifts through Apply continues the same session");
            var settings = window.Settings;
            long boundary = session.SimulationTimeNs;
            window.Pause();
            settings.AbpVariation.Value = 5;
            settings.ApplyDelaySeconds.Value = 3;
            settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Pulse(window.ActiveTimer, 250_000_000);
            Require(session.SimulationTimeNs == boundary && session.PendingSourceTimeNs == boundary + 3_000_000_000,
                "drift changes preserve pause and wait on simulation time");
            settings.AbpVariation.Value = 10;
            settings.ApplyDelaySeconds.Value = 0;
            settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            settings.AbpVariation.Value = null;
            settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(session.PendingSourceTimeNs == boundary && settings.Status.Text!.Contains("未应用", StringComparison.Ordinal),
                "an empty amplitude cannot replace the accepted pending source");
            settings.AbpVariation.Value = 10;
            window.Start();
            for (int i = 0; i < 240; i++)
            {
                window.Pulse(window.ActiveTimer, 50_000_000);
                control.Pulse(control.ActiveTimer, 50_000_000);
            }
            for (int channel = 0; channel < 7; channel++)
            {
                var actual = session.Samples(channel, boundary, boundary + 10_000_000_000).ToArray();
                var expected = control.Session.Samples(channel, boundary, boundary + 10_000_000_000).ToArray();
                Require(actual.Length > 0 && actual.SequenceEqual(expected),
                    "repeated Apply of the same drift leaves every channel identical to uninterrupted playback");
            }
            var reopened = new DesignPreviewWindow(path);
            try
            {
                Require(reopened.Settings.AbpVariation.Value == 10 && reopened.Settings.PaVariation.Value == 3 &&
                    reopened.Settings.RateSeed.Text == new string('a', 64), "reopening retains accepted amplitudes and seed, not an invalid draft");
            }
            finally { reopened.Close(); }
            settings.AbpVariation.Value = 0;
            settings.PaVariation.Value = 0;
            settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 600; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(ReferenceEquals(session, window.Session) && Near(session.Measurements!.AbpMean.Pulse, 12000, 8000) &&
                Near(session.Measurements.PaMean.Pulse, 2500, 1000), "zero amplitudes disable drift without restarting");
            window.ResetAllSettings();
            Require(window.Settings.AbpVariation.Value == 0 && window.Settings.PaVariation.Value == 0, "reset leaves both drifts off");
        }
        finally
        {
            window.Close();
            control.Close();
            File.Delete(path);
        }
    }

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
            settings.AbpVariation.Value = 10;
            window.RestartSettings();
            var diastolic = new List<int>();
            for (int i = 0; i < 1200; i++)
            {
                window.Pulse(window.ActiveTimer, 50_000_000);
                if (i % 40 == 39 && window.Session.Measurements!.AbpMean.Pulse is { DiastolicCentiMmHg: { } value }) { diastolic.Add(value); }
            }
            Require(diastolic.Count > 20 && diastolic.Max() - diastolic.Min() >= 400, "a random drift moves the measured ABP around its targets");
            settings.AbpVariation.Value = 20;
            settings.AbpSystolic.Value = 55;
            settings.AbpDiastolic.Value = 21;
            session = window.Session;
            window.ApplySettings();
            Require(ReferenceEquals(session, window.Session) && settings.Status.Text == window.Localization.Get("validation.pressureVariation"),
                "a drift too large for the pressure level is reported without applying");
            settings.AbpSystolic.Value = 130;
            settings.AbpDiastolic.Value = 85;
            settings.AbpVariation.Value = 10;

            var saved = settings.CaptureGenerator();
            Require(saved.Flags["AbpTargetEnabled"] && saved.Numbers["AbpSystolic"] == 130 && saved.Numbers["PaDiastolic"] == 12 && saved.Numbers["AbpVariation"] == 10,
                "targets and drift are part of the saved generator preferences");
            string[] later = ["AbpSystolic", "AbpDiastolic", "PaSystolic", "PaDiastolic", "AbpTargetEnabled", "PaTargetEnabled", "AbpVariation", "PaVariation"];
            var older = saved with
            {
                Numbers = saved.Numbers.Where(pair => !later.Contains(pair.Key)).ToDictionary(),
                Flags = saved.Flags.Where(pair => !later.Contains(pair.Key)).ToDictionary()
            };
            var fresh = new DesignPreviewWindow();
            try
            {
                fresh.Settings.RestoreGenerator(older);
                Require(fresh.Settings.AbpTargetEnabled.IsChecked == false && fresh.Settings.AbpSystolic.Value == 120 && fresh.Settings.PaDiastolic.Value == 10 &&
                    fresh.Settings.AbpVariation.Value == 0,
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
