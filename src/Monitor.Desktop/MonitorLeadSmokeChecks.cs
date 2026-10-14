// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class MonitorLeadSmokeChecks
{
    internal static void Verify()
    {
        string path = Path.Combine(Path.GetTempPath(), "monitor-lead-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        try
        {
            for (int index = 0; index < 250; index++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Pause();
            var session = window.Session;
            var timer = window.ActiveTimer;
            long time = session.SimulationTimeNs;
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            void SelectLead(EcgLead lead)
            {
                var flyout = skin.LeadButton.Flyout!;
                flyout.ShowAt(skin.LeadButton);
                window.UpdateLayout();
                var choice = skin.LeadChoices[(int)lead];
                choice.Focus();
                choice.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
                choice.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
                Require(!flyout.IsOpen, "activating a lead closes the picker");
                Require(session.DisplayEcgLead == lead && choice.IsChecked == true && skin.LeadChoices.Count(item => item.IsChecked == true) == 1,
                    "activating a lead leaves exactly one selection matching the displayed ECG");
                flyout.ShowAt(skin.LeadButton);
                window.UpdateLayout();
                Require(choice.IsChecked == true && skin.LeadChoices.Count(item => item.IsChecked == true) == 1,
                    "reopening the picker preserves exactly one selection");
                flyout.Hide();
            }
            Require(skin.LeadChoices.Count == 12 && skin.LeadChoices[1].IsChecked == true, "all twelve leads are offered with II selected");
            var detector = session.Measurements;
            var therapy = window.Defibrillator;
            window.Settings.EcgSelection = 21;
            window.Settings.Slots[0].Speed.SelectedIndex = 2;
            SelectLead(EcgLead.V1);
            Require(session.DisplayEcgLead == EcgLead.V1 && window.MonitorTrace.ChannelName(0) == "ECG · V1" &&
                AutomationProperties.GetName(skin.LeadButton) == "导联 · V1", "selection updates the ECG label and toolbar immediately while paused");
            Require(ReferenceEquals(session, window.Session) && ReferenceEquals(timer, window.ActiveTimer) && session.SimulationTimeNs == time &&
                detector == session.Measurements && ReferenceEquals(therapy, window.Defibrillator), "display selection retains session, measurements and therapy");
            var store = new DisplayPreferenceStore(path);
            var saved = store.Load(out bool rejected);
            Require(!rejected && saved.MonitorEcgLead == EcgLead.V1 && saved.Generator is null &&
                saved.Display.Slots[0].SpeedTenthsMmPerSecond == 250, "quick selection saves no unrelated settings drafts");
            foreach (var lead in Enum.GetValues<EcgLead>()) { SelectLead(lead); }
            SelectLead(EcgLead.V6);
            skin.SetTwelveLeadVisible(true);
            SelectLead(EcgLead.AVR);
            Require(window.MonitorView.TwelveLeadTrace.StripCount == 13 && skin.TwelveLeadVisible,
                "selecting a monitor lead leaves the full twelve-lead layout and long II intact");
            skin.SetTwelveLeadVisible(false);
            Require(window.MonitorTrace.ChannelName(0) == "ECG · aVR", "returning to monitoring uses the selected lead");
            window.Localization.Select("en");
            Require(AutomationProperties.GetName(skin.LeadButton) == "Leads · aVR" && skin.LeadChoices[3].IsChecked == true,
                "language changes preserve selection and translate accessible labels");
            window.Localization.Select("zh-CN");
            skin.ShowTherapy.IsChecked = false;
            DesktopViewportSmokeChecks.Layout(window, 1440, 940);
            skin.LeadButton.Flyout!.ShowAt(skin.LeadButton);
            window.UpdateLayout();
            Directory.CreateDirectory("artifacts");
            using (var image = new RenderTargetBitmap(new PixelSize(1440, 940), new Vector(96, 96)))
            {
                image.Render((Control)window.Content!);
                image.Save("artifacts/monitor-lead-selection.png", PngBitmapEncoderOptions.Default);
            }
            skin.LeadButton.Flyout.Hide();
            window.ApplySettings();
            Require(window.Session.DisplayEcgLead == EcgLead.AVR && ((GenericMonitorSkin)window.MonitorTrace.Skin!).LeadChoices[3].IsChecked == true,
                "applying settings preserves the display lead");
            window.RestartSettings();
            Require(window.Session.DisplayEcgLead == EcgLead.AVR, "explicit restart retains the selected lead");
            window.Pause();
            var restored = new DesignPreviewWindow(path);
            try { Require(restored.Session.DisplayEcgLead == EcgLead.AVR, "reopening restores the saved lead"); }
            finally { restored.Close(); }
            var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            document.Remove("MonitorEcgLead");
            File.WriteAllText(path, document.ToJsonString());
            Require(store.Load(out rejected).MonitorEcgLead == EcgLead.II && !rejected, "older preferences default to II");
            document["MonitorEcgLead"] = 99;
            File.WriteAllText(path, document.ToJsonString());
            Require(store.Load(out rejected).MonitorEcgLead == EcgLead.II && rejected, "invalid saved lead uses validated fallback");
        }
        finally
        {
            window.Close();
            foreach (string file in new[] { path, Path.ChangeExtension(path, ".language.json"), Path.ChangeExtension(path, ".therapy.json") })
            { File.Delete(file); }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Monitor lead: " + message); }
    }
}
