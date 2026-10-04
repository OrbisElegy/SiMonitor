// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Specs;

internal static class NoExpirationConfirmationSpecifications
{
    private const long MillisecondNs = 1_000_000;
    public static Specification[] All =>
    [
        new(nameof(AbsenceSeparatesDetectionAndConfirmation), AbsenceSeparatesDetectionAndConfirmation),
        new(nameof(AbsenceRecoveryRequiresUninterruptedEvidence), AbsenceRecoveryRequiresUninterruptedEvidence),
        new(nameof(AbsenceConfirmationUsesAcquisitionAndResetsFaults), AbsenceConfirmationUsesAcquisitionAndResetsFaults),
        new(nameof(AbsenceConfigurationChangesAndInvalidInputAreAtomic), AbsenceConfigurationChangesAndInvalidInputAreAtomic),
        new(nameof(AbsencePreferencesMigrateAndRejectCorruption), AbsencePreferencesMigrateAndRejectCorruption)
    ];

    private static CapnographyActivity Activity(long sampleMilliseconds, long? expirationMilliseconds = null) =>
        new(WaveformMeasurementStatus.Valid, 0, expirationMilliseconds * MillisecondNs, sampleMilliseconds * MillisecondNs);

    private static void AbsenceSeparatesDetectionAndConfirmation()
    {
        foreach (int delay in new[] { 0, 1, 750, 600000 })
        {
            var filter = new ConfirmedNoExpirationNotice();
            var timing = new BoundaryConfirmationTiming(delay, delay);
            MonitorNotice? At(long time, long? expiration = null) => filter.Evaluate(true, 5, time * MillisecondNs, Activity(time, expiration), timing);
            Check.That(At(4999) is null, "detection window cannot be shortened by confirmation");
            for (int time = 5000; time < 5000 + delay; time += 100)
            { Check.That(At(time) is null, "additional confirmation follows the detection window"); }
            if (delay > 0) { Check.That(At(5000 + delay - 1) is null, "one millisecond before trigger"); }
            var active = At(5000 + delay);
            Check.That(active is { Id: "co2-no-expiration", Level: MonitorNoticeLevel.Critical, Numeric: MonitorNumeric.Co2RespirationRate },
                "exact configured trigger preserves the legacy notice");
            long recoveryStart = 5001 + delay;
            for (int elapsed = 0; elapsed < delay; elapsed += 100)
            { Check.That(At(recoveryStart + elapsed, recoveryStart + elapsed) == active, "continued accepted expirations confirm recovery"); }
            if (delay > 0)
            { Check.That(At(recoveryStart + delay - 1, recoveryStart + delay - 1) == active, "not before exact recovery"); }
            Check.That(At(recoveryStart + delay, recoveryStart + delay) is null, "exact configured recovery including zero");
        }
        var immediate = new ConfirmedNoExpirationNotice();
        foreach (long time in new long[] { 0, 4999, 5000, 5100 })
        { Check.That(immediate.Evaluate(true, 5, time * MillisecondNs, Activity(time)) == NoExpirationNotice.Evaluate(true, 5, time * MillisecondNs, Activity(time)), "default retains raw behavior"); }
    }

    private static void AbsenceRecoveryRequiresUninterruptedEvidence()
    {
        var filter = new ConfirmedNoExpirationNotice();
        var timing = new BoundaryConfirmationTiming(400, 5500);
        MonitorNotice? At(long time, long? expiration = null) => filter.Evaluate(true, 5, time * MillisecondNs, Activity(time, expiration), timing);
        At(5000); At(5200);
        Check.That(At(5300, 5300) is null, "expiration interrupts pending trigger");
        for (int time = 10300; time < 10700; time += 100) { Check.That(At(time, 5300) is null, "fresh trigger waits again"); }
        Check.That(At(10700, 5300) is not null, "fresh trigger completes");
        for (int time = 10800; time <= 16000; time += 100)
        { Check.That(At(time, 10800) is not null, "absence returning at five seconds cancels the longer recovery"); }
        for (int time = 16100; time < 21600; time += 100)
        { Check.That(At(time, time) is not null, "second recovery starts from new accepted expiration evidence"); }
        Check.That(At(21600, 21600) is null, "full uninterrupted second recovery clears");
    }

    private static void AbsenceConfirmationUsesAcquisitionAndResetsFaults()
    {
        var timing = new BoundaryConfirmationTiming(400, 300);
        var filter = new ConfirmedNoExpirationNotice();
        MonitorNotice? At(long sample, long? observation = null, CapnographyActivity? activity = null) =>
            filter.Evaluate(true, 5, (observation ?? sample) * MillisecondNs, activity ?? Activity(sample), timing);
        At(5000);
        Check.That(At(5000, 5400) is null, "presentation clock cannot advance trigger confirmation");
        Check.That(At(5200, 5500) is null, "only acquired evidence accumulates");
        Check.That(At(5400, 5500) is not null, "sample clock reaches exact trigger");
        Check.That(At(5400, 5901) is null, "stale activity clears projection");
        Check.That(At(5901) is null && At(6101) is null && At(6301) is not null, "sample gap restarts evidence");
        Check.That(At(6301, activity: Activity(6301, 6301)) is not null &&
            At(6301, 6701, Activity(6301, 6301)) is not null, "repeated reads cannot advance recovery either");
        foreach (var invalid in Enum.GetValues<WaveformMeasurementStatus>().Where(s => s != WaveformMeasurementStatus.Valid)
            .Select(s => Activity(6500) with { Status = s }).Concat(new[]
            {
                Activity(6500) with { ContinuousUsableSinceNs = null }, Activity(6500) with { ContinuousUsableSinceNs = -1 },
                Activity(6500) with { ContinuousUsableSinceNs = 6501 * MillisecondNs }, Activity(6500) with { LastSampleNs = null },
                Activity(6500) with { LastSampleNs = 6501 * MillisecondNs }, Activity(6500) with { LastExpirationNs = -1 },
                Activity(6500) with { LastExpirationNs = 6501 * MillisecondNs }
            }))
        {
            filter.Reset(); At(6000); At(6200); At(6400);
            Check.That(At(6500, activity: invalid) is null && At(6600) is null, "invalid activity discards active and pending confirmation");
        }
        filter.Reset(); At(6000); At(6200);
        Check.That(At(6400, activity: Activity(6400) with { ContinuousUsableSinceNs = MillisecondNs }) is null,
            "changed usable segment cannot borrow confirmation across a missed quality interruption");
        filter.Reset(); At(6000); At(6200);
        Check.That(At(6100) is null && At(6300) is null && At(6500) is not null, "acquisition rollback restarts evidence");
        Check.That(filter.Evaluate(true, 5, 6600 * MillisecondNs, null, timing) is null && At(6700) is null, "missing activity clears evidence");
        filter.Reset(); At(6000); At(6200);
        filter.Reset(); Check.That(At(6400) is null, "session reset discards pending state");
    }

    private static void AbsenceConfigurationChangesAndInvalidInputAreAtomic()
    {
        var filter = new ConfirmedNoExpirationNotice();
        var timing = new BoundaryConfirmationTiming(400, 300);
        MonitorNotice? At(long time, bool enabled = true, int? delay = 5, BoundaryConfirmationTiming? selected = null) =>
            filter.Evaluate(enabled, delay, time * MillisecondNs, Activity(time), selected ?? timing);
        At(6000);
        foreach (var invalid in new[] { new BoundaryConfirmationTiming(-1, 0), new(0, -1), new(600001, 0), new(0, 600001) })
        {
            bool rejected = false;
            try { At(6200, selected: invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid timing rejects before state mutation");
        }
        Check.That(At(6400) is not null, "invalid timing did not reset pending evidence");
        Check.That(At(6500, delay: null) is { Level: MonitorNoticeLevel.Info, Audible: false } && At(6600) is null,
            "invalid detection settings are silent faults and clear old evidence");
        At(6800); Check.That(At(7000) is not null, "fresh confirmation after fault");
        Check.That(At(7100, enabled: false) is null && At(7200) is null, "disable and reenable reset");
        At(7400); Check.That(At(7600, delay: 6) is null, "detection window edit resets even if both conditions are already met");
        At(7800, delay: 6);
        Check.That(At(8000, delay: 6, selected: new(600, 300)) is null, "confirmation edit resets old evidence");
    }

    private static void AbsencePreferencesMigrateAndRejectCorruption()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-absence-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "preferences.json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            var alarms = MonitorAlarmPreferences.Default with
            {
                NoExpirationEnabled = true,
                NoExpirationSeconds = 35,
                NoExpirationConfirmation = new(1125, 2250),
                ConfirmationTimings = new Dictionary<MonitorNumeric, MeasurementConfirmationTiming>
                { [MonitorNumeric.Co2RespirationRate] = new(new(1, 2), new(3, 4), new(5, 6), new(7, 8)) }
            };
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, alarms)), "save separate condition timing");
            string valid = File.ReadAllText(path);
            var loaded = store.Load(out bool rejected);
            Check.That(!rejected && loaded.Alarms!.NoExpirationConfirmation == alarms.NoExpirationConfirmation &&
                loaded.Alarms.ConfirmationFor(MonitorNumeric.Co2RespirationRate) == alarms.ConfirmationFor(MonitorNumeric.Co2RespirationRate),
                "condition timing and numeric respiratory-rate timing are independent");
            foreach (int version in new[] { 2, 3, 5, 6 })
            {
                var legacy = JsonNode.Parse(valid)!.AsObject(); legacy["Version"] = version;
                legacy["Alarms"]!.AsObject().Remove("NoExpirationConfirmation");
                File.WriteAllText(path, legacy.ToJsonString());
                var migrated = store.Load(out rejected);
                Check.That(!rejected && migrated.Alarms is { NoExpirationEnabled: true, NoExpirationSeconds: 35 } &&
                    migrated.Alarms.NoExpirationConfirmation == new BoundaryConfirmationTiming(0, 0) &&
                    migrated.Alarms.ConfirmationFor(MonitorNumeric.Co2RespirationRate) == alarms.ConfirmationFor(MonitorNumeric.Co2RespirationRate),
                    "old files retain opt-in, detection interval and numeric overrides with immediate absence defaults");
            }
            foreach (var edit in new Action<JsonObject>[]
            {
                a => a["NoExpirationConfirmation"] = null,
                a => a["NoExpirationConfirmation"]!["TriggerMilliseconds"] = -1,
                a => a["NoExpirationConfirmation"]!["RecoveryMilliseconds"] = 600001,
                a => a["NoExpirationConfirmation"]!["TriggerMilliseconds"] = 1.25,
                a => a["NoExpirationConfirmation"]!.AsObject().Remove("RecoveryMilliseconds")
            })
            {
                var corrupt = JsonNode.Parse(valid)!.AsObject(); edit(corrupt["Alarms"]!.AsObject());
                string evidence = corrupt.ToJsonString(); File.WriteAllText(path, evidence);
                store.Load(out rejected);
                Check.That(rejected && File.ReadAllText(path) == evidence, "corrupt condition timing rejects without overwriting evidence");
            }
            File.WriteAllText(path, valid);
            bool saveRejected = false;
            try { store.Save(new(MonitorDisplayConfiguration.Default(), 0, alarms with { NoExpirationConfirmation = null! })); }
            catch (ArgumentException) { saveRejected = true; }
            Check.That(saveRejected && File.ReadAllText(path) == valid, "invalid save preserves stored configuration");
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }
}
