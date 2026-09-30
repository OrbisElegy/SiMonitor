// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal static class DisplayPreferenceSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-display-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "display.json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            Require(store.Load(out bool rejected).Display.Skin == MonitorSkin.FiveRows && !rejected, "missing file uses defaults quietly");
            var window = new DesignPreviewWindow(path); window.Show();
            try
            {
                window.Settings.Skin.SelectedIndex = 0;
                window.Settings.Slots[0].Channel.SelectedIndex = 4;
                window.Settings.Slots[0].Auto.IsChecked = false;
                window.Settings.Slots[0].Minimum.Text = "-2.5";
                window.Settings.Slots[0].Maximum.Text = "90";
                window.Settings.Slots[0].Speed.SelectedIndex = 0;
                window.Settings.PaperLayout.SelectedIndex = 1;
                Require(!File.Exists(path), "draft edits do not persist");
                window.ApplySettings();
                Require(File.Exists(path) && !window.PreferenceNotice.IsVisible, "applied display saves");
                string saved = File.ReadAllText(path);
                window.Settings.Slots[0].Minimum.Text = "invalid"; var live = window.Session;
                window.ApplySettings();
                Require(ReferenceEquals(live, window.Session) && File.ReadAllText(path) == saved, "invalid application preserves file and live state");
                window.Settings.Slots[0].Minimum.Text = "-2.5";
                window.Settings.PaperLayout.SelectedIndex = -1; window.ApplySettings();
                Require(ReferenceEquals(live, window.Session) && File.ReadAllText(path) == saved, "invalid paper layout rejects before runtime replacement");
                window.Settings.PaperLayout.SelectedIndex = 0;
            }
            finally { window.Close(); }
            var reopened = new DesignPreviewWindow(path); reopened.Show();
            try
            {
                var display = reopened.Session.Display;
                Require(display.Skin == MonitorSkin.ThreeRows && display.Slots[0] == new MonitorDisplaySlot(4, false, new(-2.5, 90), 125) &&
                    reopened.Settings.ReadDisplay().Slots.SequenceEqual(display.Slots) && reopened.Settings.PaperLayout.SelectedIndex == 1,
                    "restart restores applied configuration in source display and editors, ignoring uncommitted draft");
                Require(reopened.Settings.EcgSelection == 0 && reopened.Settings.Sound.AlarmEnabled.IsChecked == false &&
                    reopened.Settings.OpticalEnabled.IsChecked == false, "display recovery does not restore physiology or audio opt-in");
                reopened.SelectPage(1); Require(reopened.CurrentPaper?.SixRows == true, "saved paper layout is available");
            }
            finally { reopened.Close(); }
            string valid = File.ReadAllText(path);
            foreach (string invalid in new[] { "{", "null", valid.Replace("\"Version\": 1", "\"Version\": 2"),
                valid.Replace("\"Speed\": 125", "\"Speed\": 0"), valid.Replace("\"Automatic\": false,", ""),
                valid.Replace("\"PaperLayout\": 1", "\"PaperLayout\": 9"), new string(' ', 32769) })
            {
                File.WriteAllText(path, invalid);
                Require(store.Load(out rejected).Display.Skin == MonitorSkin.FiveRows && rejected && File.ReadAllText(path) == invalid,
                    "malformed, incompatible and oversized files fall back without overwriting evidence");
            }
            var damaged = new DesignPreviewWindow(path); damaged.Show();
            try { Require(damaged.PreferenceNotice.IsVisible && damaged.Settings.Status.Text!.Contains("默认值", StringComparison.Ordinal), "load failure is visible"); }
            finally { damaged.Close(); }
            string blocked = Path.Combine(directory, "blocked"); Directory.CreateDirectory(blocked);
            var unsavable = new DesignPreviewWindow(blocked); unsavable.Show();
            try
            {
                var live = unsavable.Session; unsavable.Settings.Skin.SelectedIndex = 0; unsavable.ApplySettings();
                Require(!ReferenceEquals(live, unsavable.Session) && unsavable.Session.Display.Skin == MonitorSkin.ThreeRows &&
                    unsavable.PreferenceNotice.IsVisible && unsavable.Settings.Status.Text!.Contains("保存失败", StringComparison.Ordinal),
                    "save failure retains applied runtime and reports lack of persistence");
            }
            finally { unsavable.Close(); }
            Require(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "failed writes clean their temporary files");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } }
}
