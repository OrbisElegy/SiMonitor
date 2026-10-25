// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class DisplayPreferenceSpecifications
{
    private static readonly string[] EcgFlagNames =
        ["customTiming", "customP", "customTPeak", "fusionEnabled", "uEnabled", "afAberrancy", "afPulseDeficit"];

    public static Specification[] All =>
    [
        new(nameof(CompleteDisplayPreferencesSurviveSaveAndMigration), CompleteDisplayPreferencesSurviveSaveAndMigration),
        new(nameof(OversizedDisplaySavePreservesExistingPreferences), OversizedDisplaySavePreservesExistingPreferences),
        new(nameof(DisplayPreferenceReadLimitIncludesTheBoundary), DisplayPreferenceReadLimitIncludesTheBoundary),
    ];

    private static void CompleteDisplayPreferencesSurviveSaveAndMigration() => WithDirectory(directory =>
    {
        string path = Path.Combine(directory, "display.json");
        var store = new DisplayPreferenceStore(path);
        var preferences = CompletePreferences();
        Check.That(store.Save(preferences) && new FileInfo(path).Length > 32768,
            "all registered alarm and therapy settings plus generator inputs save beyond the former limit");
        var restored = store.Load(out bool rejected);
        Check.That(!rejected && JsonSerializer.Serialize(restored.Generator) == JsonSerializer.Serialize(preferences.Generator) &&
            JsonSerializer.Serialize(restored.Alarms) == JsonSerializer.Serialize(preferences.Alarms),
            "successful save preserves the complete generator and alarm settings on reload");

        var legacy = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))!;
        legacy["Version"] = 17;
        legacy["Generator"]!.AsObject().Remove("AedElectricalConversions");
        File.WriteAllText(path, legacy.ToJsonString(), new UTF8Encoding(false));
        var migrated = store.Load(out rejected);
        Check.That(!rejected && store.Save(migrated), "legacy manual conversion rules expand into independently saved AED rules");
        restored = store.Load(out rejected);
        Check.That(!rejected && restored.Generator!.AedElectricalConversions.Count == EcgElectricalTherapy.Descriptors.Count &&
            JsonSerializer.Serialize(restored.Generator) == JsonSerializer.Serialize(preferences.Generator),
            "migration expansion remains readable after publishing the current preference format");
    });

    private static void OversizedDisplaySavePreservesExistingPreferences() => WithDirectory(directory =>
    {
        string path = Path.Combine(directory, "display.json");
        var store = new DisplayPreferenceStore(path);
        var preferences = CompletePreferences();
        Check.That(store.Save(preferences), "initial complete preferences save");
        byte[] before = File.ReadAllBytes(path);
        var oversized = preferences with
        {
            Generator = preferences.Generator! with
            {
                Numbers = new Dictionary<string, decimal?> { [new string('x', 65536)] = 1 }
            }
        };
        oversized.Generator.Validate();
        Check.That(!store.Save(oversized) && File.ReadAllBytes(path).SequenceEqual(before) &&
            Directory.EnumerateFiles(directory).Count() == 1,
            "oversized serialized input reports save failure without replacing the previous file or leaving temporary output");
        _ = store.Load(out bool rejected);
        Check.That(!rejected, "failed oversized save leaves the previous preferences readable");
    });

    private static void DisplayPreferenceReadLimitIncludesTheBoundary() => WithDirectory(directory =>
    {
        string path = Path.Combine(directory, "display.json");
        var store = new DisplayPreferenceStore(path);
        Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0)), "boundary fixture saves");
        byte[] original = File.ReadAllBytes(path);
        byte[] atLimit = Enumerable.Repeat((byte)' ', 65536).ToArray();
        original.CopyTo(atLimit, 0);
        File.WriteAllBytes(path, atLimit);
        _ = store.Load(out bool rejected);
        Check.That(!rejected, "a valid preference document exactly at the byte limit loads");
        byte[] oversized = [.. atLimit, (byte)' '];
        File.WriteAllBytes(path, oversized);
        _ = store.Load(out rejected);
        Check.That(rejected && File.ReadAllBytes(path).SequenceEqual(oversized),
            "one byte beyond the read limit is rejected and retained for recovery");
    });

    private static DisplayPreferences CompletePreferences()
    {
        var alarms = MonitorAlarmPreferences.Default with
        {
            Notifications = MonitorAlarmPreferences.NotificationConditionIds.ToDictionary(id => id, MonitorAlarmPreferences.DefaultNotificationFor),
            ConfirmationTimings = MeasuredLimitNotice.Descriptors.Prepend(MeasuredLimitNotice.HeartRateDescriptor)
                .ToDictionary(descriptor => descriptor.Numeric, descriptor => MeasurementConfirmationTiming.DefaultFor(descriptor.Numeric))
        };
        var numbers = new Dictionary<string, decimal?>
        {
            ["pDuration"] = 100,
            ["prInterval"] = 160,
            ["qrsDuration"] = 80,
            ["tDuration"] = 180,
            ["qtc"] = 400,
            ["pEarly"] = 100,
            ["pLate"] = -100,
            ["jPoint"] = 0,
            ["stEnd"] = 0,
            ["stArch"] = 0,
            ["tPeakPosition"] = 50,
            ["fusionJ"] = 200,
            ["fusionPeak"] = 500,
            ["fusionPosition"] = 50,
            ["uDelay"] = 30,
            ["uDuration"] = 120,
            ["escapePeriod"] = 1200,
            ["escapeOffset"] = 400
        };
        for (int lead = 1; lead <= 6; lead++)
        {
            numbers["tScale" + lead] = 1;
            numbers["uAmplitude" + lead] = 0;
        }
        var flags = EcgFlagNames
            .Concat(Enumerable.Range(1, 6).Select(lead => "fusionLead" + lead)).ToDictionary(key => key, _ => false);
        var profile = OxygenationPatientProfile.Default;
        var overrides = new OxygenationBaselineOverrides();
        var generator = new MonitorGeneratorPreferences(0, "窦性参考", 0, 0, new string('0', 63) + "1",
            new Dictionary<string, decimal?> { ["HeartRate"] = 75, ["RespiratoryRate"] = 16 },
            new Dictionary<string, bool> { ["OpticalEnabled"] = true }, new Dictionary<string, int>())
        {
            ElectricalConversions = EcgElectricalTherapy.Descriptors.ToDictionary(descriptor => descriptor.TemplateId, _ => ElectricalConversionSettings.Default),
            AedElectricalConversions = EcgElectricalTherapy.Descriptors.ToDictionary(descriptor => descriptor.TemplateId, _ => ElectricalConversionSettings.Default),
            PacingPermissions = EcgPacingPermissions.TemplateIds.ToDictionary(id => id, _ => true),
            EcgAdvanced = new(numbers, flags, new Dictionary<string, int> { ["qtMethod"] = 0, ["limbPlacement"] = 0 }),
            Oxygenation = new(true, 450000, 150000, 210000, true)
            {
                Patient = new(profile, overrides, OxygenationPatientDefaults.Resolve(profile, overrides))
            }
        };
        return new(MonitorDisplayConfiguration.Default(), 0, Alarms: alarms, Generator: generator);
    }

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-display-specs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
