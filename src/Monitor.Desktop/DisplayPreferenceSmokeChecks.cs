// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
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
            Require(store.Load(out bool rejected).Display.Skin == MonitorSkin.FourRows && !rejected, "missing file uses defaults quietly");
            Require(store.Save(new(MonitorDisplayConfiguration.Default(), 0, MeasurementMillimeters: true)) &&
                store.Load(out rejected).MeasurementMillimeters && !rejected, "millimeter units persist");
            var legacyUnits = JsonNode.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8))!.AsObject();
            legacyUnits["Version"] = 10;
            legacyUnits.Remove("MeasurementMillimeters");
            File.WriteAllText(path, legacyUnits.ToJsonString(), new System.Text.UTF8Encoding(false));
            Require(!store.Load(out rejected).MeasurementMillimeters && !rejected, "legacy preferences default to converted units");
            File.Delete(path);
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
                window.Settings.MeasurementUnits.SelectedIndex = 1;
                var alarms = window.Settings.Alerts;
                alarms.HeartRateEnabled.IsChecked = true; alarms.WarningHeartRate.Value = 130.25m;
                alarms.SpO2Enabled.IsChecked = true; alarms.WarningSpO2.Value = 93.5m;
                alarms.HeartRateConfirmation.Fields[6].Value = 1.5m;
                alarms.HeartRateConfirmation.Fields[7].Value = .75m;
                alarms.SpO2Confirmation.Fields[0].Value = .65m;
                alarms.SpO2Confirmation.Fields[1].Value = 1.1m;
                alarms.NoExpirationEnabled.IsChecked = true; alarms.NoExpirationSeconds.Value = 35;
                alarms.NoExpirationTriggerSeconds.Value = 1.125m;
                alarms.NoExpirationRecoverySeconds.Value = 2.25m;
                alarms.NoticeColorEnabled.IsChecked = false;
                alarms.NotificationSettings.Mode.SelectedIndex = 1;
                foreach (var (id, notification) in alarms.NotificationSettings.Editors)
                {
                    notification.LatchUntilAcknowledged.IsChecked = id == "hr-low";
                    notification.SelectedSoundMode = id == "hr-low" ? 2 : 1;
                    notification.RepeatSeconds.Value = 1.125m;
                    notification.ReminderSeconds.Value = 2.25m;
                    notification.ReminderEnabled.IsChecked = id != "hr-low";
                }
                foreach (var d in MeasuredLimitNotice.Descriptors)
                {
                    var editor = alarms.AdditionalLimits.Editors[d.Numeric]; editor.Enabled.IsChecked = true;
                    editor.Confirmation.Fields[4].Value = 1.125m;
                    editor.Confirmation.Fields[5].Value = 2.25m;
                    editor.WarningHigh.Value += 0.25m; editor.CriticalHigh.Value += 0.5m;
                }
                alarms.TestLevel.SelectedIndex = 1;
                var sound = window.Settings.Sound;
                sound.Volume.Value = 73; sound.HeartbeatVolume.Value = 62;
                sound.HeartbeatEnabled.IsChecked = false; sound.BeatSource.SelectedIndex = 2;
                sound.PitchSource.SelectedIndex = 0; sound.PauseSeconds.Value = 77;
                alarms.InfoTone.IsChecked = true; alarms.InfoInterval.Value = 45;
                alarms.NoticeInterval.Value = 12.25m; alarms.WarningInterval.Value = 6.5m;
                alarms.CriticalInterval.Value = 1.75m;
                Require(!File.Exists(path), "draft edits do not persist");
                window.RestartSettings();
                Require(File.Exists(path) && !window.PreferenceNotice.IsVisible, "applied display saves");
                string saved = File.ReadAllText(path);
                var soundSession = window.Session;
                sound.BeatSource.SelectedIndex = -1; window.RestartSettings();
                Require(ReferenceEquals(soundSession, window.Session) && File.ReadAllText(path) == saved,
                    "invalid sound source cannot replace session or saved preferences");
                sound.BeatSource.SelectedIndex = 2;
                var beforeInvalidAlarm = window.Session;
                alarms.WarningHeartRate.Value = 190; window.RestartSettings();
                Require(ReferenceEquals(beforeInvalidAlarm, window.Session) && File.ReadAllText(path) == saved,
                    "invalid enabled alarm ordering preserves session and saved configuration");
                alarms.WarningHeartRate.Value = 130.25m;
                window.Settings.Slots[0].Minimum.Text = "invalid"; var live = window.Session;
                window.RestartSettings();
                Require(ReferenceEquals(live, window.Session) && File.ReadAllText(path) == saved, "invalid application preserves file and live state");
                window.Settings.Slots[0].Minimum.Text = "-2.5";
                window.Settings.PaperLayout.SelectedIndex = -1; window.RestartSettings();
                Require(ReferenceEquals(live, window.Session) && File.ReadAllText(path) == saved, "invalid paper layout rejects before runtime replacement");
                window.Settings.PaperLayout.SelectedIndex = 0;
            }
            finally { window.Close(); }
            var reopened = new DesignPreviewWindow(path); reopened.Show();
            try
            {
                var display = reopened.Session.Display;
                Require(reopened.Settings.Sound.CapturePreferences(reopened.Settings.Alerts) ==
                    new MonitorSoundPreferences(73, 62, false, 2, 0, 77, new(true, 45000, 12250, 6500, 1750)),
                    "restart restores volume, beat modes, pause duration and exact alarm timing");
                Require(!reopened.Settings.Sound.ResumeAlarmAudio.IsEnabled && reopened.Settings.Sound.PublishedAlarm is null,
                    "restoration does not start a pause or publish an alarm");
                var alarms = reopened.Settings.Alerts.CapturePreferences();
                Require(alarms.PlaybackMode == AlarmPlaybackMode.Notifications &&
                    alarms.Notifications.Count == MonitorAlarmPreferences.NotificationConditionIds.Count &&
                    alarms.Notifications.All(e => e.Value == new AlarmNotificationSettings(1125, e.Key != "hr-low", 2250) { SoundMode = e.Key == "hr-low" ? AlarmSoundMode.Continuous : AlarmSoundMode.SingleGroup, LatchingMode = e.Key == "hr-low" ? AlarmLatchingMode.UntilAcknowledged : AlarmLatchingMode.NonLatching }) &&
                    reopened.Settings.Alerts.AlarmLifecycles.SelectMany(j => j.Conditions).All(c => c.Episode is null),
                    "notification configuration round trips without restoring active episodes or hardware audio opt-in");
                Require(alarms.HeartRate.Enabled && alarms.HeartRate.WarningHigh == 130250 &&
                    alarms.SpO2Enabled && alarms.SpO2Warning == 93500 && alarms.NoExpirationEnabled &&
                    alarms.NoExpirationSeconds == 35 && alarms.NoExpirationConfirmation == new BoundaryConfirmationTiming(1125, 2250) && !alarms.NoticeColorEnabled &&
                    alarms.Additional.Values.All(v => v.Enabled) && reopened.Settings.Alerts.TestLevel.SelectedIndex == 0,
                    "restart restores alarm configuration without transient test notices");
                Require(alarms.ConfirmationFor(MonitorNumeric.HeartRate).CriticalHigh == new BoundaryConfirmationTiming(1500, 750) &&
                    alarms.ConfirmationFor(MonitorNumeric.SpO2).CriticalLow == new BoundaryConfirmationTiming(650, 1100),
                    "primary alarm confirmations round trip through settings and storage");
                foreach (var d in MeasuredLimitNotice.Descriptors)
                {
                    Require(alarms.ConfirmationFor(d.Numeric).WarningHigh == new BoundaryConfirmationTiming(1125, 2250),
                        "restart restores exact per-channel confirmation timing");
                    Require(alarms.Additional[d.Numeric].WarningHigh == d.TeachingDefaults.WarningHigh + d.Divisor / 4 &&
                        alarms.Additional[d.Numeric].CriticalHigh == d.TeachingDefaults.CriticalHigh + d.Divisor / 2,
                        "each measurement restores thresholds in its native units");
                }
                Require(display.Skin == MonitorSkin.ThreeRows && display.Slots[0] == new MonitorDisplaySlot(4, false, new(-2.5, 90), 125) &&
                    reopened.Settings.ReadDisplay().Slots.SequenceEqual(display.Slots) && reopened.Settings.PaperLayout.SelectedIndex == 1 && reopened.Settings.MeasurementUnits.SelectedIndex == 1,
                    "restart restores applied configuration in source display and editors, ignoring uncommitted draft");
                Require(reopened.Settings.EcgSelection == 0 && !reopened.Settings.Sound.Muted &&
                    reopened.Settings.OpticalEnabled.IsChecked == false, "display recovery does not restore physiology or audio opt-in");
                reopened.SelectPage(1); Require(reopened.CurrentPaper?.SixRows == true, "saved paper layout is available");
            }
            finally { reopened.Close(); }
            string valid = File.ReadAllText(path);
            var legacy = JsonNode.Parse(valid)!.AsObject(); legacy["Version"] = 1; legacy.Remove("Alarms"); legacy.Remove("Sound"); legacy.Remove("Generator");
            File.WriteAllText(path, legacy.ToJsonString());
            var migrated = store.Load(out rejected);
            Require(!rejected && migrated.Alarms is null && migrated.PaperLayout == 1,
                "version one display configuration remains readable with default alarms");
            var versionTwo = JsonNode.Parse(valid)!.AsObject(); versionTwo["Version"] = 2; versionTwo.Remove("Sound"); versionTwo.Remove("Generator");
            File.WriteAllText(path, versionTwo.ToJsonString());
            Require(store.Load(out rejected).Sound is null && !rejected, "version two retains alarms without requiring sound preferences");
            var versionThree = JsonNode.Parse(valid)!.AsObject(); versionThree["Version"] = 3; versionThree.Remove("Generator");
            File.WriteAllText(path, versionThree.ToJsonString());
            Require(store.Load(out rejected).Generator is null && !rejected, "version three keeps sound without generator inputs");
            var versionFour = JsonNode.Parse(valid)!.AsObject();
            versionFour["Version"] = 4;
            versionFour["Alarms"]!.AsObject().Remove("ConfirmationTimings");
            versionFour["Alarms"]!.AsObject().Remove("PlaybackMode");
            versionFour["Alarms"]!.AsObject().Remove("Notifications");
            File.WriteAllText(path, versionFour.ToJsonString());
            var migratedFour = store.Load(out rejected);
            Require(!rejected && migratedFour.Generator is not null && migratedFour.Alarms!.PlaybackMode == AlarmPlaybackMode.Continuous &&
                migratedFour.Alarms.Notifications.Count == 0 &&
                migratedFour.Alarms!.ConfirmationFor(MonitorNumeric.AbpMean).WarningHigh == new BoundaryConfirmationTiming(10000, 3000),
                "version four retains generator and migrates pressure confirmation defaults");
            var versionFive = JsonNode.Parse(valid)!.AsObject();
            versionFive["Version"] = 5;
            versionFive["Alarms"]!["ConfirmationTimings"]!.AsObject().Remove("HeartRate");
            versionFive["Alarms"]!["ConfirmationTimings"]!.AsObject().Remove("SpO2");
            File.WriteAllText(path, versionFive.ToJsonString());
            var migratedFive = store.Load(out rejected);
            Require(!rejected && migratedFive.Alarms!.HeartRate.Enabled && migratedFive.Alarms.SpO2Enabled &&
                migratedFive.Alarms.ConfirmationFor(MonitorNumeric.HeartRate) == MeasurementConfirmationTiming.DefaultFor(MonitorNumeric.HeartRate) &&
                migratedFive.Alarms.ConfirmationFor(MonitorNumeric.SpO2) == MeasurementConfirmationTiming.DefaultFor(MonitorNumeric.SpO2) &&
                migratedFive.Alarms.ConfirmationFor(MonitorNumeric.PulseRate).WarningHigh == new BoundaryConfirmationTiming(1125, 2250),
                "version five retains additional timing overrides and primary opt-in with immediate defaults");
            var versionSix = JsonNode.Parse(valid)!.AsObject();
            versionSix["Version"] = 6;
            versionSix["Alarms"]!.AsObject().Remove("NoExpirationConfirmation");
            File.WriteAllText(path, versionSix.ToJsonString());
            var migratedSix = store.Load(out rejected);
            Require(!rejected && migratedSix.Alarms is { NoExpirationEnabled: true, NoExpirationSeconds: 35 } &&
                migratedSix.Alarms.NoExpirationConfirmation == new BoundaryConfirmationTiming(0, 0) &&
                migratedSix.Alarms.ConfirmationFor(MonitorNumeric.SpO2).CriticalLow == new BoundaryConfirmationTiming(650, 1100),
                "version six preserves thresholds and numeric confirmation with immediate absence defaults");
            foreach (var edit in new Action<JsonObject>[] {
                d => d.Remove("Generator"),
                d => { d["Version"] = 4; d.Remove("Generator"); },
                d => d.Remove("Sound"),
                d => d["Sound"]!.AsObject().Remove("Volume"),
                d => d["Sound"]!["Volume"] = 101,
                d => d["Sound"]!["BeatSource"] = -1,
                d => d["Sound"]!["PitchSource"] = 2,
                d => d["Sound"]!["PauseSeconds"] = 0,
                d => d["Sound"]!["Timing"] = null,
                d => d["Sound"]!["Timing"]!["CriticalMilliseconds"] = 2001 })
            {
                var corruptSound = JsonNode.Parse(valid)!.AsObject(); edit(corruptSound);
                File.WriteAllText(path, corruptSound.ToJsonString()); store.Load(out rejected);
                Require(rejected, "missing or invalid sound preferences fall back safely");
            }
            foreach (string member in new[] { "Alarms", "HeartRate", "SpO2Enabled", "Additional" })
            {
                var incomplete = JsonNode.Parse(valid)!.AsObject();
                if (member == "Alarms") { incomplete.Remove(member); }
                else { incomplete["Alarms"]!.AsObject().Remove(member); }
                File.WriteAllText(path, incomplete.ToJsonString()); store.Load(out rejected);
                Require(rejected, "missing alarm configuration members are rejected");
            }
            foreach (string invalid in new[] { "{", "null", valid.Replace("\"Version\": 18", "\"Version\": 189"),
                valid.Replace("\"Speed\": 125", "\"Speed\": 0"), valid.Replace("\"Automatic\": false,", ""),
                valid.Replace("\"PaperLayout\": 1", "\"PaperLayout\": 9"), new string(' ', 32769) })
            {
                File.WriteAllText(path, invalid);
                Require(store.Load(out rejected).Display.Skin == MonitorSkin.FourRows && rejected && File.ReadAllText(path) == invalid,
                    "malformed, incompatible and oversized files fall back without overwriting evidence");
            }
            foreach (var edit in new Action<JsonObject>[] {
                a => a["HeartRate"]!["WarningHigh"] = 999999,
                a => a["HeartRate"]!["WarningLow"] = null,
                a => a["SpO2Critical"] = 99000,
                a => a["NoExpirationSeconds"] = 121,
                a => a["Additional"] = new JsonObject(),
                a => a["Additional"] = null })
            {
                var corrupted = JsonNode.Parse(valid)!.AsObject(); edit(corrupted["Alarms"]!.AsObject());
                File.WriteAllText(path, corrupted.ToJsonString()); store.Load(out rejected);
                Require(rejected, "invalid alarm ranges, ordering and channel sets reject safely");
            }
            var damaged = new DesignPreviewWindow(path); damaged.Show();
            try { Require(damaged.PreferenceNotice.IsVisible && damaged.Settings.Status.Text!.Contains("默认值", StringComparison.Ordinal), "load failure is visible"); }
            finally { damaged.Close(); }
            string blocked = Path.Combine(directory, "blocked"); Directory.CreateDirectory(blocked);
            var unsavable = new DesignPreviewWindow(blocked); unsavable.Show();
            try
            {
                var live = unsavable.Session; unsavable.Settings.Skin.SelectedIndex = 0; unsavable.RestartSettings();
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
