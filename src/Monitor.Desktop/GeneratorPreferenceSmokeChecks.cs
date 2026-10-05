// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class GeneratorPreferenceSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-generator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            foreach (int selection in new[] { 0, 107, 137, 2 })
            {
                var window = new DesignPreviewWindow();
                MonitorGeneratorPreferences expected;
                LocalMonitorPreviewSession original;
                string?[] appliedParameters;
                try
                {
                    Configure(window.Settings, selection);
                    window.RestartSettings(); original = window.Session;
                    appliedParameters = [window.Settings.AppliedEcgParameters.Text, window.Settings.AppliedRespirationParameters.Text, window.Settings.AppliedEjectionParameters.Text];
                    expected = window.Settings.CaptureGenerator();
                    // Use the real persistence Apply path with the same complete editor snapshot.
                    var writer = new DesignPreviewWindow(path);
                    try
                    {
                        writer.Settings.RestoreGenerator(expected); writer.RestartSettings();
                        Require(!writer.PreferenceNotice.IsVisible && File.Exists(path), "configured generator saves");
                        writer.Settings.HeartRate.Value = 99; // unapplied draft must not replace saved settings
                    }
                    finally { writer.Close(); }
                }
                finally { window.Close(); }
                string bytes = File.ReadAllText(path);
                var reopened = new DesignPreviewWindow(path);
                try
                {
                    var actual = reopened.Settings.CaptureGenerator();
                    Require(appliedParameters.SequenceEqual(new[] { reopened.Settings.AppliedEcgParameters.Text,
                        reopened.Settings.AppliedRespirationParameters.Text, reopened.Settings.AppliedEjectionParameters.Text }),
                        "startup overview describes the restored applied configuration");
                    Require(!reopened.PreferenceNotice.IsVisible && actual.Ecg == expected.Ecg && actual.EcgName == expected.EcgName &&
                        actual.Respiration == expected.Respiration && actual.Ejection == expected.Ejection && actual.Seed == expected.Seed &&
                        actual.Numbers.OrderBy(p => p.Key).SequenceEqual(expected.Numbers.OrderBy(p => p.Key)) &&
                        actual.Flags.OrderBy(p => p.Key).SequenceEqual(expected.Flags.OrderBy(p => p.Key)) &&
                        actual.Choices.OrderBy(p => p.Key).SequenceEqual(expected.Choices.OrderBy(p => p.Key)), "complete editor round trip");
                    Require(reopened.Session.SimulationTimeNs == 0 && File.ReadAllText(path) == bytes,
                        "restore starts at zero without writing the source file");
                    for (int step = 0; step < 64; step++)
                    { original.Advance(250_000_000); reopened.Session.Advance(250_000_000); }
                    for (int channel = 0; channel < 7; channel++)
                    { Require(original.Samples(channel, 0, long.MaxValue).SequenceEqual(reopened.Session.Samples(channel, 0, long.MaxValue)), "restored acquired samples match all seven channels"); }
                    Require(original.Measurements == reopened.Session.Measurements, "optical and other sample-derived measurements reproduce");
                }
                finally { reopened.Close(); }
            }
            string valid = File.ReadAllText(path);
            foreach (var edit in new Action<JsonObject>[] {
                g => g["EcgName"] = "unknown template",
                g => g["Numbers"]!.AsObject().Remove("RespiratoryRate"),
                g => g["Numbers"]!["RespiratoryRate"] = 600,
                g => g["Ejection"] = 2,
                g => { g["Respiration"] = 1; g["Numbers"]!["EtCo2Variation"] = 1; } })
            {
                var document = JsonNode.Parse(valid)!.AsObject(); edit(document["Generator"]!.AsObject());
                string broken = document.ToJsonString(); File.WriteAllText(path, broken);
                var rejected = new DesignPreviewWindow(path);
                try
                {
                    Require(rejected.PreferenceNotice.IsVisible && rejected.Settings.EcgSelection == 0 &&
                        rejected.Session.SimulationTimeNs == 0 && File.ReadAllText(path) == broken,
                        "unknown, incomplete, out-of-range or incompatible generators fall back without overwriting evidence");
                }
                finally { rejected.Close(); }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Configure(DesignPreviewSettings settings, int selection)
    {
        settings.EcgSelection = selection; settings.RespirationSelection = selection == 2 ? 1 : 0;
        settings.EjectionSelection = selection == 2 ? 1 : 0;
        settings.OpticalEnabled.IsChecked = true; settings.OpticalTarget.Value = 96.5m;
        settings.OpticalVariation.Value = 2; settings.OpticalModulation.Value = .8m;
        settings.RateSeed.Text = new string('a', 64);
        settings.RespiratoryRate.Value = 18; settings.InspirationPercent.Value = 40;
        settings.AbpTargetEnabled.IsChecked = false; settings.PaTargetEnabled.IsChecked = false;
        settings.CvpBaseline.Value = 8.5m; settings.AbpPulseGain.Value = 1.2m;
        settings.PaPulseGain.Value = .8m; settings.RespSignalAmplitude.Value = -750;
        settings.RespCardiacArtifact.Value = 30; settings.Co2TransportDelay.Value = 300;
        settings.Co2DispersionStep.Value = 50; settings.Co2Rise.Value = 300;
        if (selection == 0)
        {
            settings.CardiacRateEnabled.IsChecked = true; settings.HeartRate.Value = 90;
            settings.RateVariation.Value = 5; settings.EtCo2Variation.Value = 2;
        }
        if (selection == 107)
        { settings.TContourParameters.Crossing.Value = 35; settings.TContourParameters.SecondPeak.Value = 180; }
        if (selection == 137)
        {
            var editor = settings.InfarctionParameters;
            editor.ComponentsEnabled.IsChecked = true; editor.SeparateRegions.IsChecked = true;
            editor.IschemiaRegion.SelectedIndex = 7; editor.InjuryRegion.SelectedIndex = 8; editor.NecrosisRegion.SelectedIndex = 9;
            editor.ReferenceT.IsChecked = false; editor.TPeak.Value = -450;
            editor.JPoint.Value = 150; editor.StEnd.Value = 200; editor.StArch.Value = 80;
            editor.Necrosis.SelectedIndex = 1; editor.QrsWeight.Value = 60; editor.Delay.Value = 20;
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } }
}
