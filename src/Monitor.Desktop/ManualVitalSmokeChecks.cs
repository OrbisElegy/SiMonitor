// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class ManualVitalSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            var editor = window.Settings.ManualVitals;
            Require(window.Session.ManualVitals == ManualVitalSigns.Empty, "manual readings start disabled");
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
            Require(window.MonitorView.ManualNumericTexts.Count == 5 &&
                window.MonitorView.ManualNumericTexts.Contains("NIBP 120/80 (93) mmHg") &&
                window.MonitorView.ManualNumericTexts.Contains("Lactate 2.34 mmol/L"), "manual values reach visible monitor");
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
            Require(window.Session.ManualVitals == ManualVitalSigns.Empty, "reset clears manual values");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
