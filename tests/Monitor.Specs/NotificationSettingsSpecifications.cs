// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Specs;

internal static class NotificationSettingsSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(NotificationSettingsValidateAllConditions), NotificationSettingsValidateAllConditions),
        new(nameof(NotificationPreferencesRoundTripAndMigrate), NotificationPreferencesRoundTripAndMigrate)
    ];

    private static void NotificationSettingsValidateAllConditions()
    {
        var owners = Enum.GetValues<MonitorNumeric>().Select(n => new ConfirmedLimitNotice(n).Lifecycle)
            .Append(new ConfirmedNoExpirationNotice().Lifecycle).Append(new EcgAlarmNotices().Lifecycle).ToArray();
        Check.That(MonitorAlarmPreferences.NotificationConditionIds.SequenceEqual(owners.SelectMany(j => j.Conditions)
            .Select(c => c.ConditionId).Order(StringComparer.Ordinal)), "saved condition registry matches all live owners exactly");
        Check.That(AlarmNotificationSettings.Default.ToPolicy() == AlarmNotificationPolicy.Default &&
            new AlarmNotificationSettings(3600000, false, 12345).ToPolicy() == new AlarmNotificationPolicy(3600000, 0) &&
            new AlarmNotificationSettings(0, true, 1).ToPolicy() == new AlarmNotificationPolicy(0, 1),
            "reminder enablement is independent from its retained positive interval");
        Check.That(new AlarmNotificationSettings(1000, true, 500) { SoundMode = AlarmSoundMode.Continuous }.ToPolicy() ==
            new AlarmNotificationPolicy { SoundDuration = AlarmSoundDuration.Continuous } &&
            (AlarmNotificationSettings.Default with { SoundMode = AlarmSoundMode.SingleGroup }).ToPolicy(AlarmPlaybackMode.Continuous).SoundDuration == AlarmSoundDuration.SingleGroup,
            "explicit duration overrides the global default; long sound has no short-repeat timeout or periodic intent");
        foreach (var invalid in new[]
        {
            new AlarmNotificationSettings(-1, false, 30000), new(3600001, true, 30000),
            new(0, true, 0), new(0, false, 0), new(0, false, 3600001), new(0, false, 30000) { SoundMode = (AlarmSoundMode)3 }
        })
        {
            bool rejected = false;
            try { invalid.ToPolicy(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid enabled and dormant values cannot become runtime policies");
        }
        foreach (var invalid in new[]
        {
            MonitorAlarmPreferences.Default with { PlaybackMode = (AlarmPlaybackMode)99 },
            MonitorAlarmPreferences.Default with { Notifications = null! },
            MonitorAlarmPreferences.Default with { Notifications = new Dictionary<string, AlarmNotificationSettings> { ["spo2-high"] = AlarmNotificationSettings.Default } },
            MonitorAlarmPreferences.Default with { Notifications = new Dictionary<string, AlarmNotificationSettings> { ["hr-high"] = null! } }
        })
        {
            bool rejected = false;
            try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "unknown conditions and malformed notification configuration reject atomically");
        }
    }

    private static void NotificationPreferencesRoundTripAndMigrate()
    {
        string directory = Path.Combine(Path.GetTempPath(), "notification-preferences-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "preferences.json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            var settings = MonitorAlarmPreferences.NotificationConditionIds.Select((id, index) =>
                (Id: id, Value: new AlarmNotificationSettings(index * 123, index % 2 == 0, index + 1) { SoundMode = (AlarmSoundMode)(index % 3), LatchingMode = (AlarmLatchingMode)(index % 2) }))
                .ToDictionary(e => e.Id, e => e.Value);
            var alarms = MonitorAlarmPreferences.Default with { PlaybackMode = AlarmPlaybackMode.Notifications, Notifications = settings, EcgMonitoringEnabled = false };
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, alarms)), "save notification configuration");
            string valid = File.ReadAllText(path);
            var loaded = store.Load(out bool rejected);
            Check.That(!rejected && JsonNode.Parse(valid)!["Version"]!.GetValue<int>() == 12 &&
                loaded.Alarms!.PlaybackMode == AlarmPlaybackMode.Notifications && !loaded.Alarms.EcgMonitoringEnabled &&
                settings.All(e => loaded.Alarms.NotificationFor(e.Key) == e.Value) &&
                !valid.Contains("Occurrence", StringComparison.Ordinal) && !valid.Contains("NotificationSequence", StringComparison.Ordinal),
                "all notification settings round trip without episodes, cursors or requests");
            var versionEleven = JsonNode.Parse(valid)!.AsObject();
            versionEleven["Version"] = 11;
            versionEleven["Alarms"]!.AsObject().Remove("EcgMonitoringEnabled");
            foreach (var descriptor in EcgAlarmNotices.Descriptors)
            { versionEleven["Alarms"]!["Notifications"]!.AsObject().Remove(descriptor.Id); }
            File.WriteAllText(path, versionEleven.ToJsonString());
            var migratedEleven = store.Load(out rejected);
            Check.That(!rejected && migratedEleven.Alarms!.EcgMonitoringEnabled && !migratedEleven.Alarms.HeartRate.Enabled &&
                EcgAlarmNotices.Descriptors.All(d => migratedEleven.Alarms.NotificationFor(d.Id) == d.DefaultNotification),
                "old preferences enable the new ECG group with its defaults while preserving existing HR settings");
            var versionNine = JsonNode.Parse(valid)!.AsObject();
            versionNine["Version"] = 9;
            foreach (var item in versionNine["Alarms"]!["Notifications"]!.AsObject()) { item.Value!.AsObject().Remove("LatchingMode"); }
            File.WriteAllText(path, versionNine.ToJsonString());
            var migratedNine = store.Load(out rejected);
            Check.That(!rejected && migratedNine.Alarms!.Notifications.All(e => e.Value.LatchingMode == AlarmLatchingMode.NonLatching),
                "version nine retains non-latching defaults without restoring acknowledgement state");
            foreach (var mode in Enum.GetValues<AlarmPlaybackMode>())
            {
                var old = JsonNode.Parse(valid)!.AsObject();
                old["Version"] = 8;
                old["Alarms"]!["PlaybackMode"] = (int)mode;
                foreach (var item in old["Alarms"]!["Notifications"]!.AsObject()) { item.Value!.AsObject().Remove("SoundMode"); }
                File.WriteAllText(path, old.ToJsonString());
                var migrated = store.Load(out rejected);
                Check.That(!rejected && migrated.Alarms!.PlaybackMode == mode && migrated.Alarms.Notifications.All(e =>
                    e.Value.SoundMode == AlarmSoundMode.Inherit && e.Value.RepeatSuppressionMilliseconds == settings[e.Key].RepeatSuppressionMilliseconds &&
                    e.Value.ToPolicy(mode).SoundDuration == (mode == AlarmPlaybackMode.Continuous ? AlarmSoundDuration.Continuous : AlarmSoundDuration.SingleGroup)),
                    "version eight retains both previous global modes and stored notification intervals");
            }
            foreach (int version in new[] { 1, 2, 3, 5, 6, 7 })
            {
                var legacy = JsonNode.Parse(valid)!.AsObject();
                legacy["Version"] = version;
                legacy["Alarms"]!.AsObject().Remove("PlaybackMode");
                legacy["Alarms"]!.AsObject().Remove("Notifications");
                File.WriteAllText(path, legacy.ToJsonString());
                var migrated = store.Load(out rejected);
                Check.That(!rejected && migrated.Alarms!.PlaybackMode == AlarmPlaybackMode.Continuous &&
                    migrated.Alarms.Notifications.Count == 0 &&
                    MonitorAlarmPreferences.NotificationConditionIds.All(id => migrated.Alarms.NotificationFor(id) == MonitorAlarmPreferences.DefaultNotificationFor(id)) &&
                    !migrated.Alarms.HeartRate.Enabled, "legacy files keep continuous sound, default policies and alarm opt-in");
            }
            foreach (var edit in new Action<JsonObject>[]
            {
                a => a["PlaybackMode"] = 2,
                a => a["Notifications"]!["hr-high"]!["LatchingMode"] = 99,
                a => a["Notifications"]!["hr-high"]!["SoundMode"] = 3,
                a => a["Notifications"] = null,
                a => a["Notifications"]!["unknown"] = JsonNode.Parse("{\"RepeatSuppressionMilliseconds\":0,\"ReminderEnabled\":false,\"ReminderMilliseconds\":30000}"),
                a => a["Notifications"]!["hr-high"] = null,
                a => a["Notifications"]!["hr-high"]!["ReminderMilliseconds"] = 0,
                a => a["Notifications"]!["hr-high"]!["RepeatSuppressionMilliseconds"] = 1.5,
                a => a["Notifications"]!["hr-high"]!.AsObject().Remove("ReminderEnabled")
            })
            {
                var invalid = JsonNode.Parse(valid)!.AsObject(); edit(invalid["Alarms"]!.AsObject());
                File.WriteAllText(path, invalid.ToJsonString());
                store.Load(out rejected);
                Check.That(rejected, "malformed notification preferences reject before restoration");
            }
            File.WriteAllText(path, valid);
            bool refused = false;
            try { store.Save(new(MonitorDisplayConfiguration.Default(), 0, alarms with { PlaybackMode = (AlarmPlaybackMode)99 })); }
            catch (ArgumentException) { refused = true; }
            Check.That(refused && File.ReadAllText(path) == valid, "invalid save preserves the previous file");
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }
}
