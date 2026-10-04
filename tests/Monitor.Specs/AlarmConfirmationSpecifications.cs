// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Specs;

internal static class AlarmConfirmationSpecifications
{
    private const long MillisecondNs = 1_000_000;
    private static readonly LiveMeasurementSnapshot Empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
    public static Specification[] All =>
    [
        new(nameof(ConfigurableBoundariesConfirmAndRecoverExactly), ConfigurableBoundariesConfirmAndRecoverExactly),
        new(nameof(ConfigurableConfirmationPreservesIndependentEvidence), ConfigurableConfirmationPreservesIndependentEvidence),
        new(nameof(ConfigurableConfirmationRejectsInvalidTimingAtomically), ConfigurableConfirmationRejectsInvalidTimingAtomically),
        new(nameof(ConfigurableConfirmationResetsInterruptedEvidence), ConfigurableConfirmationResetsInterruptedEvidence),
        new(nameof(SpO2ConfirmationPreservesLowerBoundarySemantics), SpO2ConfirmationPreservesLowerBoundarySemantics),
        new(nameof(PrimaryConfirmationRejectsUnusableEvidence), PrimaryConfirmationRejectsUnusableEvidence),
        new(nameof(AlarmConfirmationPreferencesMigrateAndRoundTrip), AlarmConfirmationPreferencesMigrateAndRoundTrip)
    ];

    private static LiveMeasurementSnapshot Snapshot(MonitorNumeric numeric, long milliseconds, int value,
        WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid)
    {
        var s = Empty with { SampleTimeNs = milliseconds * MillisecondNs };
        return numeric switch
        {
            MonitorNumeric.HeartRate => s with { HeartRate = new(status, value, null) },
            MonitorNumeric.SpO2 => s with { SpO2 = new(status, value, null, s.SampleTimeNs) },
            MonitorNumeric.RespirationRate => s with { ImpedanceRespiration = new(status, value, 0) },
            MonitorNumeric.PulseRate => s with { PulseRate = new(status, value, 0) },
            MonitorNumeric.EtCo2 => s with { Capnography = s.Capnography with { EndTidalCentiMmHg = new(status, value, 0) } },
            MonitorNumeric.Co2RespirationRate => s with { Capnography = s.Capnography with { RespirationsMilliPerMinute = new(status, value, 0) } },
            MonitorNumeric.AbpMean => s with { AbpMean = new(status, value, 0, s.SampleTimeNs) },
            MonitorNumeric.PaMean => s with { PaMean = new(status, value, 0, s.SampleTimeNs) },
            MonitorNumeric.CvpMean => s with { CvpMean = new(status, value, 0, s.SampleTimeNs) },
            _ => throw new ArgumentOutOfRangeException(nameof(numeric))
        };
    }

    private static void ConfigurableBoundariesConfirmAndRecoverExactly()
    {
        foreach (var d in MeasuredLimitNotice.Descriptors.Append(MeasuredLimitNotice.HeartRateDescriptor))
            foreach (bool low in new[] { true, false })
                foreach (bool critical in new[] { true, false })
                    foreach (int trigger in new[] { 0, 1, 750 })
                        foreach (int recovery in new[] { 0, 1, 450 })
                        {
                            var filter = new ConfirmedLimitNotice(d.Numeric);
                            var limits = d.TeachingDefaults with { Enabled = true };
                            var slow = new BoundaryConfirmationTiming(600000, 600000);
                            var selected = new BoundaryConfirmationTiming(trigger, recovery);
                            var timing = new MeasurementConfirmationTiming(
                                low && critical ? selected : slow, low && !critical ? selected : slow,
                                !low && !critical ? selected : slow, !low && critical ? selected : slow);
                            int boundary = (low ? critical ? limits.CriticalLow : limits.WarningLow :
                                critical ? limits.CriticalHigh : limits.WarningHigh)!.Value;
                            int breached = boundary + (low ? -1 : 1);
                            MonitorNotice? At(long time, int value) => filter.Evaluate(limits, Snapshot(d.Numeric, time, value), timing);
                            for (int time = 0; time < trigger; time += 100)
                            { Check.That(At(time, breached) is null, "only the selected boundary may confirm"); }
                            if (trigger > 0) { Check.That(At(trigger - 1, breached) is null, "not before trigger boundary"); }
                            var notice = At(trigger, breached);
                            Check.That(notice?.Level == (critical ? MonitorNoticeLevel.Critical : MonitorNoticeLevel.Warning) &&
                                notice.Numeric == d.Numeric, "exact trigger including zero and millisecond precision");
                            for (int time = 0; time < recovery; time += 100)
                            { Check.That(At(trigger + 1 + time, boundary) == notice, "equality starts recovery without clearing early"); }
                            if (recovery > 0)
                            { Check.That(At(trigger + recovery, boundary) == notice, "one millisecond before recovery"); }
                            Check.That(At(trigger + 1 + recovery, boundary) is null, "exact recovery including zero");
                        }
    }

    private static void ConfigurableConfirmationPreservesIndependentEvidence()
    {
        var numeric = MonitorNumeric.PulseRate;
        var limits = MeasuredLimitNotice.Describe(numeric).TeachingDefaults with { Enabled = true };
        var timing = new MeasurementConfirmationTiming(new(0, 0), new(0, 0), new(400, 200), new(600, 300));
        var filter = new ConfirmedLimitNotice(numeric);
        for (int time = 0; time <= 1000; time += 100)
        {
            var notice = filter.Evaluate(limits, Snapshot(numeric, time, time % 200 == 0 ? 180001 : 179999), timing);
            Check.That(notice?.Level == (time < 400 ? null : MonitorNoticeLevel.Warning), "Critical chatter does not starve Warning");
        }
        for (int time = 1100; time <= 1600; time += 100)
        {
            var notice = filter.Evaluate(limits, Snapshot(numeric, time, 180001), timing);
            Check.That(notice?.Level == (time < 1600 ? MonitorNoticeLevel.Warning : MonitorNoticeLevel.Critical), "independent escalation clock");
        }
        Check.That(filter.Evaluate(limits, Snapshot(numeric, 1700, 180000), timing)?.Level == MonitorNoticeLevel.Critical, "start recovery");
        filter.Evaluate(limits, Snapshot(numeric, 1800, 180001), timing);
        for (int time = 1900; time <= 2200; time += 100)
        {
            var notice = filter.Evaluate(limits, Snapshot(numeric, time, 180000), timing);
            Check.That(notice?.Level == (time < 2200 ? MonitorNoticeLevel.Critical : MonitorNoticeLevel.Warning), "interrupted recovery restarts, then downgrades");
        }
    }

    private static void ConfigurableConfirmationRejectsInvalidTimingAtomically()
    {
        var numeric = MonitorNumeric.RespirationRate;
        var filter = new ConfirmedLimitNotice(numeric);
        var limits = MeasuredLimitNotice.Describe(numeric).TeachingDefaults with { Enabled = true };
        var timing = new MeasurementConfirmationTiming(new(0, 0), new(0, 0), new(400, 0), new(600, 0));
        filter.Evaluate(limits, Snapshot(numeric, 0, 40001), timing);
        foreach (var invalid in new[]
        {
            timing with { CriticalHigh = new(-1, 0) }, timing with { WarningLow = new(0, -1) },
            timing with { WarningHigh = new(600001, 0) }, timing with { CriticalLow = new(0, int.MaxValue) },
            timing with { CriticalHigh = null! }
        })
        {
            bool rejected = false;
            try { filter.Evaluate(limits, Snapshot(numeric, 200, 40001), invalid); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid duration or missing boundary rejects as a whole");
        }
        Check.That(filter.Evaluate(limits, Snapshot(numeric, 400, 40001), timing)?.Level == MonitorNoticeLevel.Warning,
            "rejected configuration did not reset pending valid evidence");
        Check.That(filter.Evaluate(limits, Snapshot(numeric, 600, 40001), timing)?.Level == MonitorNoticeLevel.Critical,
            "rejected configuration did not partially publish");
    }

    private static void ConfigurableConfirmationResetsInterruptedEvidence()
    {
        foreach (var d in MeasuredLimitNotice.Descriptors.Append(MeasuredLimitNotice.HeartRateDescriptor))
        {
            var limits = d.TeachingDefaults with { Enabled = true };
            var timing = new MeasurementConfirmationTiming(new(600, 200), new(400, 200), new(400, 200), new(600, 200));
            int value = limits.CriticalHigh!.Value + 1;
            var filter = new ConfirmedLimitNotice(d.Numeric);
            MonitorNotice? At(long time, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid) =>
                filter.Evaluate(limits, Snapshot(d.Numeric, time, value, status), timing);
            At(0); At(200);
            for (int i = 0; i < 10; i++) { Check.That(At(200) is null, "duplicate snapshots cannot advance time"); }
            Check.That(At(1000) is null && At(1200) is null, "gap starts fresh evidence");
            Check.That(At(1400)?.Level == MonitorNoticeLevel.Warning, "fresh warning confirms");
            Check.That(At(1500, WaveformMeasurementStatus.PoorSignal) is null && At(1600) is null, "quality interrupts");
            At(1800);
            timing = timing with { WarningHigh = new(500, 200) };
            Check.That(At(2000) is null && At(2200) is null && At(2400) is null, "timing edit discards previous channel evidence");
            Check.That(At(2500)?.Level == MonitorNoticeLevel.Warning, "edited timing uses its new start");
            limits = limits with { Enabled = false };
            Check.That(At(2600) is null, "disabled suppresses");
            limits = limits with { Enabled = true };
            Check.That(At(2700) is null, "re-enabled waits for fresh evidence");
            Check.That(At(0) is null, "rewind starts a new confirmation");
            At(200); At(400);
            filter.Reset();
            Check.That(At(500) is null, "explicit session reset clears evidence");
        }
    }

    private static void SpO2ConfirmationPreservesLowerBoundarySemantics()
    {
        var numeric = MonitorNumeric.SpO2;
        var limits = MeasuredLimitNotice.SpO2Descriptor.TeachingDefaults with { Enabled = true };
        foreach (bool critical in new[] { false, true })
            foreach (int delay in new[] { 0, 1, 750 })
            {
                var filter = new ConfirmedLimitNotice(numeric);
                var timing = new MeasurementConfirmationTiming(new(delay, delay), new(delay, delay), new(0, 0), new(0, 0));
                int boundary = (critical ? limits.CriticalLow : limits.WarningLow)!.Value;
                MonitorNotice? At(long milliseconds, int value) => filter.Evaluate(limits, Snapshot(numeric, milliseconds, value), timing);
                for (int time = 0; time < delay; time += 100)
                { Check.That(At(time, boundary - 1) is null, "SpO2 confirmation waits for its configured low boundary"); }
                if (delay > 0) { Check.That(At(delay - 1, boundary - 1) is null, "SpO2 does not confirm early"); }
                var active = At(delay, boundary - 1);
                Check.That(active?.Id == "spo2-low" && active.Level == (critical ? MonitorNoticeLevel.Critical : MonitorNoticeLevel.Warning),
                    "SpO2 keeps its identity and native milli-percent comparison");
                for (int time = 0; time < delay; time += 100)
                { Check.That(At(delay + 1 + time, boundary) == active, "equality starts recovery without premature clearing"); }
                Check.That(At(delay * 2 + 1, boundary)?.Level == (critical ? MonitorNoticeLevel.Warning : null),
                    "exact configured recovery downgrades only the recovered boundary");
            }
        var independent = new ConfirmedLimitNotice(numeric);
        var independentTiming = new MeasurementConfirmationTiming(new(400, 300), new(200, 0), new(0, 0), new(0, 0));
        for (int time = 0; time <= 800; time += 100)
        {
            var notice = independent.Evaluate(limits, Snapshot(numeric, time, time % 200 == 0 ? 84999 : 85001), independentTiming);
            Check.That(notice?.Level == (time < 200 ? null : MonitorNoticeLevel.Warning), "SpO2 Critical chatter preserves Warning evidence");
        }
        foreach (var invalidTiming in new[]
        {
            independentTiming with { WarningHigh = new(1, 0) },
            independentTiming with { CriticalHigh = new(0, 1) }
        })
        {
            bool rejected = false;
            try { independent.Evaluate(limits, Snapshot(numeric, 900, 84999), invalidTiming); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "unsupported SpO2 high confirmation cannot be silently ignored");
        }
        Check.That(independent.Evaluate(limits, Snapshot(numeric, 1000, 84999), independentTiming)?.Level == MonitorNoticeLevel.Warning,
            "rejected high configuration preserves valid pending and active low evidence");
        bool rejectedHighLimit = false;
        try { independent.Evaluate(limits with { WarningHigh = 99000 }, Snapshot(numeric, 1100, 84999), independentTiming); }
        catch (ArgumentException) { rejectedHighLimit = true; }
        Check.That(rejectedHighLimit, "unsupported SpO2 high threshold is rejected");
        Check.That(independent.Evaluate(limits, Snapshot(numeric, 1200, 84999), independentTiming)?.Level == MonitorNoticeLevel.Critical,
            "invalid high threshold did not reset the valid low confirmation timer");
    }

    private static void PrimaryConfirmationRejectsUnusableEvidence()
    {
        foreach (var numeric in new[] { MonitorNumeric.HeartRate, MonitorNumeric.SpO2 })
        {
            var descriptor = MeasuredLimitNotice.Describe(numeric);
            var limits = descriptor.TeachingDefaults with { Enabled = true };
            var timing = new MeasurementConfirmationTiming(new(400, 300), new(200, 200), new(0, 0), new(0, 0));
            int low = limits.CriticalLow!.Value - 1;
            foreach (var status in Enum.GetValues<WaveformMeasurementStatus>().Where(s => s != WaveformMeasurementStatus.Valid))
            {
                var filter = new ConfirmedLimitNotice(numeric);
                filter.Evaluate(limits, Snapshot(numeric, 0, low), timing);
                filter.Evaluate(limits, Snapshot(numeric, 200, low), timing);
                Check.That(filter.Evaluate(limits, Snapshot(numeric, 400, low), timing)?.Level == MonitorNoticeLevel.Critical, "primary alarm active");
                Check.That(filter.Evaluate(limits, Snapshot(numeric, 500, low, status), timing) is null, "invalid evidence clears the physiological projection");
                Check.That(filter.Evaluate(limits, Snapshot(numeric, 600, low), timing) is null, "quality recovery starts fresh evidence");
                Check.That(filter.Evaluate(limits, Snapshot(numeric, 800, low), timing)?.Level == MonitorNoticeLevel.Warning, "fresh Warning confirms independently");
                Check.That(filter.Evaluate(limits, Snapshot(numeric, 1000, low), timing)?.Level == MonitorNoticeLevel.Critical, "fresh Critical confirms independently");
                var missing = Snapshot(numeric, 1100, low);
                missing = numeric == MonitorNumeric.HeartRate
                    ? missing with { HeartRate = missing.HeartRate with { MilliBeatsPerMinute = null } }
                    : missing with { SpO2 = missing.SpO2 with { SaturationMilliPercent = null } };
                Check.That(filter.Evaluate(limits, missing, timing) is null, "missing valid-status reading is not zero or recovered physiology");
            }
            var immediate = new ConfirmedLimitNotice(numeric);
            foreach (int value in new[] { limits.CriticalLow!.Value - 1, limits.CriticalLow.Value, limits.WarningLow!.Value - 1, limits.WarningLow.Value })
            {
                var snapshot = Snapshot(numeric, 0, value);
                Check.That(immediate.Evaluate(limits, snapshot) == MeasuredLimitNotice.Evaluate(numeric, limits, snapshot),
                    "omitted timing retains immediate primary alarm behavior even at the same timestamp");
            }
        }
    }

    private static void AlarmConfirmationPreferencesMigrateAndRoundTrip()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-confirmation-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "preferences.json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            var timings = MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric,
                d => new MeasurementConfirmationTiming(new(1, 2), new(3, 4), new(5, 6), new(7, 600000)));
            timings[MonitorNumeric.HeartRate] = new(new(101, 202), new(303, 404), new(505, 606), new(707, 808));
            timings[MonitorNumeric.SpO2] = new(new(909, 1010), new(1111, 1212), new(0, 0), new(0, 0));
            var alarms = MonitorAlarmPreferences.Default with { ConfirmationTimings = timings };
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, alarms)), "save timing overrides");
            string valid = File.ReadAllText(path);
            var loaded = store.Load(out bool rejected);
            Check.That(!rejected && loaded.Alarms is not null && loaded.Alarms.ConfirmationTimings.Count == timings.Count &&
                timings.All(entry => loaded.Alarms.ConfirmationFor(entry.Key) == entry.Value), "all units and boundaries round trip without runtime state");
            foreach (int version in new[] { 2, 3, 5 })
            {
                var legacy = JsonNode.Parse(valid)!.AsObject();
                legacy["Version"] = version;
                legacy["Alarms"]!.AsObject().Remove("ConfirmationTimings");
                File.WriteAllText(path, legacy.ToJsonString());
                var migrated = store.Load(out rejected);
                Check.That(!rejected && migrated.Alarms!.Additional.Values.All(v => !v.Enabled) &&
                    migrated.Alarms.ConfirmationFor(MonitorNumeric.AbpMean).WarningLow == new BoundaryConfirmationTiming(4000, 3000) &&
                    migrated.Alarms.ConfirmationFor(MonitorNumeric.PulseRate).WarningHigh == new BoundaryConfirmationTiming(0, 0) &&
                    migrated.Alarms.ConfirmationFor(MonitorNumeric.HeartRate) == MeasurementConfirmationTiming.DefaultFor(MonitorNumeric.HeartRate) &&
                    migrated.Alarms.ConfirmationFor(MonitorNumeric.SpO2) == MeasurementConfirmationTiming.DefaultFor(MonitorNumeric.SpO2),
                    "old settings preserve disabled flags and channel-specific defaults");
            }
            foreach (var edit in new Action<JsonObject>[]
            {
                a => a["ConfirmationTimings"] = null,
                a => a["ConfirmationTimings"]!["PulseRate"]!["WarningHigh"]!["TriggerMilliseconds"] = -1,
                a => a["ConfirmationTimings"]!["PulseRate"]!["CriticalHigh"]!["RecoveryMilliseconds"] = 600001,
                a => a["ConfirmationTimings"]!["PulseRate"]!["CriticalLow"] = null,
                a => a["ConfirmationTimings"]!["PulseRate"]!["WarningLow"]!.AsObject().Remove("TriggerMilliseconds"),
                a => a["ConfirmationTimings"]!["SpO2"]!["WarningHigh"]!["TriggerMilliseconds"] = 1,
                a => a["ConfirmationTimings"]!["SpO2"]!["CriticalHigh"]!["RecoveryMilliseconds"] = 1,
                a => a["ConfirmationTimings"]!["999"] = a["ConfirmationTimings"]!["PulseRate"]!.DeepClone()
            })
            {
                var corrupt = JsonNode.Parse(valid)!.AsObject();
                edit(corrupt["Alarms"]!.AsObject());
                string json = corrupt.ToJsonString();
                File.WriteAllText(path, json);
                store.Load(out rejected);
                Check.That(rejected && File.ReadAllText(path) == json, "invalid or unsupported timing rejects without rewriting the file");
            }
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
}
