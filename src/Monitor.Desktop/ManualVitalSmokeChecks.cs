// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class ManualVitalSmokeChecks
{
    private static readonly bool[] Custom2Visibility = [false, false, true, false];
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            var editor = window.Settings.ManualVitals;
            var defaults = new ManualVitalSigns(new(120, 80, 93));
            Require(window.Session.ManualVitals == defaults, "NIBP targets start enabled and other manual items remain disabled");
            editor.NibpEnabled.IsChecked = true;
            editor.TemperatureEnabled.IsChecked = true;
            editor.CustomEnabled[0].IsChecked = true;
            editor.Names[0].Text = "Lactate";
            editor.Units[0].Text = "mmol/L";
            editor.Values[0].Value = 2.34m;
            editor.CustomEnabled[1].IsChecked = true;
            editor.Names[1].Text = "ICP";
            editor.Units[1].Text = "mmHg";
            editor.Values[1].Value = 12;
            window.RestartSettings();
            Require(window.Session.ManualVitals.Custom1?.Value == 2.34m && window.Session.ManualVitals.Custom2?.Name == "ICP",
                "both custom slots reach session");
            window.MonitorView.Refresh();
            Require(window.MonitorView.ManualNumericTexts.Count == 4 &&
                window.MonitorView.ManualNumericTexts.Any(text => text.StartsWith("NIBP --- / ---", StringComparison.Ordinal)) &&
                window.MonitorView.ManualNumericTexts.Contains("Lactate 2.34 mmol/L"), "manual values reach visible monitor");
            VerifySkinCapabilities(window);
            var prior = window.Session;
            editor.Mean.Value = 121;
            Require(editor.ErrorKey() is not null, "invalid draft has inline error");
            window.RestartSettings();
            Require(ReferenceEquals(prior, window.Session), "invalid draft preserves live session");
            editor.Mean.Value = 93;
            editor.Names[0].Text = " ";
            window.RestartSettings();
            Require(ReferenceEquals(prior, window.Session), "empty custom name rejected");
            editor.CustomEnabled[0].IsChecked = false;
            window.RestartSettings();
            Require(window.Session.ManualVitals.Custom1 is null && window.Session.ManualVitals.Custom2 is not null,
                "disabled invalid draft ignored without disturbing second slot");
            window.ResetAllSettings();
            Require(window.Session.ManualVitals == defaults, "reset restores NIBP targets and clears other manual values");
        }
        finally { window.Close(); }
    }

    private static void VerifySkinCapabilities(DesignPreviewWindow window)
    {
        window.Pause();
        var session = window.Session;
        var values = session.ManualVitals;
        string[] readings = window.MonitorView.ManualNumericTexts.ToArray();
        foreach (var package in new[] { MonitorSkinPackage.BedsideMonitor, MonitorSkinPackage.DefibrillatorEnclosure })
        {
            window.ChangeMonitorSkin(package);
            window.MonitorView.Refresh();
            DesktopViewportSmokeChecks.Layout(window, 960, 640);
            Require(window.MonitorView.ManualTiles.Take(3).All(tile => !tile.IsVisible && tile.Reading.Length == 0) &&
                window.MonitorView.ManualTiles[3].IsVisible && window.MonitorView.ManualNumericTexts[3].Contains("--- / ---", StringComparison.Ordinal),
                "skins without extra display areas show only NIBP even when every manual vital is enabled");
            if (package.Bedside)
            {
                var extras = window.MonitorView.ManualTiles[0].Parent!;
                Require(extras is Avalonia.Controls.Control { IsVisible: false } &&
                    extras.Parent!.Parent!.Parent is BedsideMonitorArea area && area.RowDefinitions[1].Height.Value == 118,
                    "hidden extras do not expand the bedside footer or consume waveform space");
            }
            Require(ReferenceEquals(session, window.Session) && session.ManualVitals == values &&
                window.Settings.ManualVitals.TemperatureEnabled.IsChecked == true &&
                window.Settings.ManualVitals.CustomEnabled.All(toggle => toggle.IsChecked == true),
                "skin display capabilities preserve the session and enabled user settings");
        }
        window.ChangeMonitorSkin(SkinPackageCodecSmokeChecks.WithManualVitals(MonitorSkinPackage.Generic, ["custom2"]));
        Require(window.MonitorView.ManualTiles.Select(tile => tile.IsVisible).SequenceEqual(Custom2Visibility) &&
            window.MonitorView.ManualNumericTexts[2] == readings[2] && window.MonitorView.ManualNumericTexts[3] == "",
            "an imported skin independently controls each manual display, including NIBP");
        window.ChangeMonitorSkin(SkinPackageCodecSmokeChecks.WithManualVitals(MonitorSkinPackage.Generic, []));
        Require(window.MonitorView.ManualTiles.All(tile => !tile.IsVisible && tile.Reading.Length == 0),
            "skins may disable every manual display");
        window.ChangeMonitorSkin(MonitorSkinPackage.Generic);
        Require(window.MonitorView.ManualTiles.All(tile => tile.IsVisible) && window.MonitorView.ManualNumericTexts.SequenceEqual(readings),
            "returning to a supporting skin restores all enabled readings without reapplying settings");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
