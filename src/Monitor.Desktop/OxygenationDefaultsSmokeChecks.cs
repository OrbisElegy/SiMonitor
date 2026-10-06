// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Measurements;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class OxygenationDefaultsSmokeChecks
{
    internal static void Verify()
    {
        string path = Path.Combine(Path.GetTempPath(), "monitor-baselines-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        window.Pause();
        try
        {
            var settings = window.Settings.Oxygenation;
            var patient = settings.Patient;
            Require(patient.UseDefaults.IsChecked == true && settings.DemandMultiplier.Value == 1,
                "a new editor selects central baselines and no extra demand");
            window.Settings.OpticalEnabled.IsChecked = true;
            settings.Realtime.IsChecked = true;
            patient.Sex.SelectedIndex = 1;
            patient.Age.Value = 60;
            patient.PatientHeight.Value = 165;
            patient.Weight.Value = 60;
            var expected = OxygenationPatientDefaults.Resolve(new(60, OxygenationReferenceSex.Female, 165, 60));
            Require(patient.Hemoglobin.Value == 13.66m && !patient.Hemoglobin.IsEnabled &&
                patient.Frc.Value == decimal.Round(expected.Frc.Value, 3) &&
                patient.BasalDemand.Value == decimal.Round(expected.BasalOxygenDemand.Value, 3),
                "age, sex and body size update automatic central values");
            patient.OverrideHemoglobin.IsChecked = true;
            patient.Hemoglobin.Value = 10;
            patient.Age.Value = 70;
            patient.Sex.SelectedIndex = 0;
            Require(patient.Hemoglobin.Value == 10 && patient.Read()!.Resolved.Hemoglobin.Origin == OxygenationBaselineOrigin.Explicit,
                "demographic edits retain a per-field explicit Hb");
            patient.OverrideHemoglobin.IsChecked = false;
            Require(patient.Hemoglobin.Value == 14.70m, "clearing the override restores the age-bin central Hb");
            patient.OverrideFrc.IsChecked = true;
            patient.Frc.Value = 2400;
            var applied = patient.Read()!;
            SettingsApplySmokeChecks.ApplySynchronously(window, restart: true);
            window.Pause();
            var session = window.Session;
            Require(session.OxygenationParameters == applied.Resolved.Parameters, "Apply uses the full unrounded patient baseline");
            var saved = new DisplayPreferenceStore(path).Load(out bool rejected);
            Require(!rejected && !window.PreferenceNotice.IsVisible && saved.Generator?.Oxygenation?.Patient == applied,
                "the actual bounded preference store preserves inputs, overrides, exact values and sources");
            var reopened = new DesignPreviewWindow(path);
            try
            {
                Require(!reopened.PreferenceNotice.IsVisible && reopened.Session.OxygenationParameters == session.OxygenationParameters &&
                    reopened.Settings.Oxygenation.Patient.Read() == applied && reopened.Session.SimulationTimeNs == 0,
                    "reopening reconstructs the baseline and starts a new scene");
            }
            finally { reopened.Close(); }
            for (int step = 0; step < 64; step++) { session.Advance(250_000_000); }
            Require(session.Measurements!.SpO2.Status == WaveformMeasurementStatus.Valid, "demographic baselines feed actual optical measurement");
            var before = session.Oxygenation!.Value;
            var readings = session.Measurements;
            patient.Weight.Value = 65; // A baseline draft is deliberately left unapplied.
            settings.DemandMultiplier.Value = 2;
            settings.UpdateVentilation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(ReferenceEquals(session, window.Session) && session.Oxygenation == before && session.Measurements == readings &&
                session.OxygenationParameters == applied.Resolved.Parameters,
                "live demand retains stores, optical windows and the applied baseline despite demographic drafts");
            session.Advance(250_000_000);
            var after = session.Oxygenation!.Value;
            decimal consumed = after.Reservoirs.ConsumedOxygenMl - before.Reservoirs.ConsumedOxygenMl;
            decimal seconds = (after.SourceSimTimeNs - before.SourceSimTimeNs) / 1_000_000_000m;
            Require(after.OxygenDemandMultiplier == 2 &&
                Math.Abs(consumed - seconds * applied.Resolved.Parameters.OxygenDemandMlStpdPerMinute * 2 / 60) < .000000001m,
                "the teacher multiplier scales actual consumption against the running basal value");
            before = after;
            settings.DemandMultiplier.Value = null;
            settings.TidalVolume.Value = 0;
            settings.UpdateVentilation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(session.Oxygenation == before && window.Settings.Status.Text!.Contains("无效", StringComparison.Ordinal),
                "invalid live demand rejects the complete ventilation and demand edit");
            settings.DemandMultiplier.Value = 2;
            patient.Age.Value = 19;
            SettingsApplySmokeChecks.ApplySynchronously(window, restart: true);
            Require(ReferenceEquals(session, window.Session) && window.Settings.Status.Text!.Contains("Hb", StringComparison.Ordinal),
                "unsupported central Hb requires an explicit input and preserves the current session");
            window.Settings.RestoreGenerator(saved.Generator!);
            settings.DemandMultiplier.Value = 2;
            SettingsApplySmokeChecks.ApplySynchronously(window, restart: true);
            window.Pause();
            saved = new DisplayPreferenceStore(path).Load(out rejected);
            Require(!rejected && saved.Generator!.Oxygenation!.OxygenDemandMultiplier == 2 &&
                saved.Generator.Oxygenation.Patient == applied && window.Session.Oxygenation!.Value.OxygenDemandMultiplier == 2,
                "baseline and multiplier persist independently when the complete scene is applied");
            Console.WriteLine("PASS: demographic defaults, explicit baselines, persistence and live teacher demand");
        }
        finally
        {
            window.Close();
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
