// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class PressureTransducerSpecifications
{
    private static readonly LiveMeasurementSnapshot Empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
    public static Specification[] All =>
    [
        new(nameof(PressureRangeCensorsOnlyBelowItsIndependentLimits), PressureRangeCensorsOnlyBelowItsIndependentLimits),
        new(nameof(PressureRangeRetainsConfirmedLowAlarmEvidence), PressureRangeRetainsConfirmedLowAlarmEvidence),
        new(nameof(PressureRangeDoesNotInventSeverityOrRecovery), PressureRangeDoesNotInventSeverityOrRecovery),
        new(nameof(PressureRangePreferencesRestoreDefaultsAndRejectInvalidValues), PressureRangePreferencesRestoreDefaultsAndRejectInvalidValues),
        new(nameof(PressureRangeFollowsAppliedSourceAndTherapy), PressureRangeFollowsAppliedSourceAndTherapy)
    ];

    private static LiveMeasurementSnapshot Snapshot(int meanCentiMmHg, long simTimeNs = 0, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid)
    {
        var reading = new MeanPressureReading(status, status == WaveformMeasurementStatus.Valid ? meanCentiMmHg : null, 0, simTimeNs)
        { Pulse = new(status, meanCentiMmHg + 100, meanCentiMmHg - 100, simTimeNs) };
        return Empty with { SampleTimeNs = simTimeNs, AbpMean = reading, PaMean = reading, CvpMean = reading };
    }

    private static void PressureRangeCensorsOnlyBelowItsIndependentLimits()
    {
        var defaults = new PressureTransducerLimits();
        Check.That(defaults.Apply(Snapshot(-5000)).AbpMean is { Status: WaveformMeasurementStatus.Valid, MeanCentiMmHg: -5000 }, "equality remains measurable, including negative pressures");
        var below = defaults.Apply(Snapshot(-5001));
        Check.That(below.AbpMean is { Status: WaveformMeasurementStatus.OutOfRange, MeanCentiMmHg: null, BelowRangeUpperBoundCentiMmHg: -5000 } &&
            below.AbpMean.Pulse is { Status: WaveformMeasurementStatus.OutOfRange, SystolicCentiMmHg: null, DiastolicCentiMmHg: null }, "out-of-range readings expose a bound, never a hidden exact value");
        Check.That(MeasurementDisplay.Resolve(MeasurementSource.Pressure, below.AbpMean.Status, null).NumericText == "---", "below range uses dashes");
        var independent = new PressureTransducerLimits(2000, 0, -5000).Apply(Snapshot(1000));
        Check.That(independent.AbpMean.MeanCentiMmHg is null && independent.PaMean.MeanCentiMmHg == 1000 && independent.CvpMean.MeanCentiMmHg == 1000, "channels have independent lower limits");
        foreach (var status in new[] { WaveformMeasurementStatus.NoData, WaveformMeasurementStatus.PoorSignal, WaveformMeasurementStatus.Stale, WaveformMeasurementStatus.WarmingUp })
        {
            var input = Snapshot(-9000, status: status);
            Check.That(defaults.Apply(input) == input, "unavailable input never becomes low-range evidence");
        }
    }

    private static void PressureRangeRetainsConfirmedLowAlarmEvidence()
    {
        foreach (var numeric in new[] { MonitorNumeric.AbpMean, MonitorNumeric.PaMean, MonitorNumeric.CvpMean })
        {
            var alarm = new ConfirmedLimitNotice(numeric);
            var limits = MeasuredLimitNotice.Describe(numeric).TeachingDefaults with { Enabled = true };
            for (long time = 0; time <= 4_000_000_000; time += 200_000_000)
            {
                var snapshot = new PressureTransducerLimits().Apply(Snapshot(-6000, time));
                var notice = alarm.Evaluate(limits, snapshot);
                Check.That(time < 4_000_000_000 ? notice is null : notice?.Level == MonitorNoticeLevel.Critical, "censored low pressure retains the normal confirmation delay: " + numeric);
            }
            Check.That(alarm.Evaluate(limits, Snapshot(-6000, 4_200_000_000, WaveformMeasurementStatus.PoorSignal)) is null, "signal failure interrupts even an active below-range alarm");
            Check.That(alarm.Evaluate(limits with { Enabled = false }, new PressureTransducerLimits().Apply(Snapshot(-6000, 4_400_000_000))) is null, "disabled pressure alarms remain disabled");
        }
    }

    private static void PressureRangeDoesNotInventSeverityOrRecovery()
    {
        var alarm = new ConfirmedLimitNotice(MonitorNumeric.AbpMean);
        var limits = new MeasurementLimits(true, 1000, 3000, 10000, 15000);
        var floor = new PressureTransducerLimits(2000);
        for (long time = 0; time <= 4_000_000_000; time += 200_000_000)
        {
            var notice = alarm.Evaluate(limits, floor.Apply(Snapshot(-8000, time)));
            if (time == 4_000_000_000) { Check.That(notice?.Level == MonitorNoticeLevel.Warning, "pressure below 20 does not prove pressure below 10, even if hidden source is much lower"); }
        }
        for (long time = 4_200_000_000; time <= 7_200_000_000; time += 200_000_000)
        {
            var notice = alarm.Evaluate(limits, Snapshot(4000, time));
            Check.That(time < 7_200_000_000 ? notice is not null : notice is null, "measurable recovery uses its normal delay");
        }
        for (long time = 7_400_000_000; time <= 11_400_000_000; time += 200_000_000)
        { alarm.Evaluate(limits, Snapshot(500, time)); }
        for (long time = 11_600_000_000; time <= 15_600_000_000; time += 200_000_000)
        { Check.That(alarm.Evaluate(limits, floor.Apply(Snapshot(500, time)))?.Level == MonitorNoticeLevel.Critical, "an uncertain bound cannot falsely recover an existing critical alarm"); }
        Check.That(MeasuredLimitNotice.Evaluate(MonitorNumeric.AbpMean, limits, Snapshot(0, status: WaveformMeasurementStatus.OutOfRange)) is null, "generic out-of-range without a bound cannot invent low pressure");
    }

    private static void PressureRangePreferencesRestoreDefaultsAndRejectInvalidValues()
    {
        var generator = new MonitorGeneratorPreferences(0, "sinus", 0, 0, "1", new Dictionary<string, decimal?>(), new Dictionary<string, bool>(), new Dictionary<string, int>())
        { PressureTransducers = new(2000, -200, -5000, 450, 800) };
        string path = Path.Combine(Path.GetTempPath(), "monitor-pressure-range-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new DisplayPreferenceStore(path);
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, Generator: generator)), "save pressure ranges");
            var restored = store.Load(out bool rejected);
            Check.That(!rejected && restored.Generator!.PressureTransducers == generator.PressureTransducers, "restore independent channel settings");
            var document = JsonNode.Parse(File.ReadAllText(path))!;
            document["Generator"]!["PressureTransducers"]!.AsObject().Remove("AbpMinimumPulseCentiMmHg");
            document["Generator"]!["PressureTransducers"]!.AsObject().Remove("PaMinimumPulseCentiMmHg");
            File.WriteAllText(path, document.ToJsonString());
            restored = store.Load(out rejected);
            Check.That(!rejected && restored.Generator!.PressureTransducers == new PressureTransducerLimits(2000, -200, -5000), "range-only configurations acquire default pulse thresholds");
            document["Generator"]!.AsObject().Remove("PressureTransducers");
            File.WriteAllText(path, document.ToJsonString());
            restored = store.Load(out rejected);
            Check.That(!rejected && restored.Generator!.PressureTransducers == new PressureTransducerLimits(), "older configurations retain the researched default");
            foreach (string invalid in new[] { "null", "{\"AbpMinimumCentiMmHg\":40001}", "{\"PaMinimumCentiMmHg\":-10001}", "{\"AbpMinimumPulseCentiMmHg\":199}", "{\"PaMinimumPulseCentiMmHg\":10001}" })
            {
                document["Generator"]!["PressureTransducers"] = JsonNode.Parse(invalid);
                File.WriteAllText(path, document.ToJsonString());
                store.Load(out rejected);
                Check.That(rejected, "invalid ranges cannot replace saved configuration");
            }
        }
        finally { File.Delete(path); }
    }

    private static void PressureRangeFollowsAppliedSourceAndTherapy()
    {
        var display = MonitorDisplayConfiguration.Default();
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, display, true);
        for (int i = 0; i < 50; i++) { session.Advance(200_000_000); }
        var limits = new PressureTransducerLimits(15000, 3000, 2000, 600, 700);
        session.ScheduleSource(new(PhysiologyIllustrationConfiguration.Default, display, true, pressureTransducers: limits), 1_000_000_000);
        session.Advance(200_000_000);
        Check.That(session.Measurements!.AbpMean.Status == WaveformMeasurementStatus.Valid, "pending ranges do not take effect early");
        for (int i = 0; i < 8; i++) { session.Advance(200_000_000); }
        Check.That(session.PressureTransducers == limits && session.Measurements!.AbpMean.Status == WaveformMeasurementStatus.OutOfRange &&
            session.Measurements.PaMean.Status == WaveformMeasurementStatus.OutOfRange && session.Measurements.CvpMean.Status == WaveformMeasurementStatus.OutOfRange, "applied ranges affect all invasive channels");
        var sinus = session.PrepareSinusAfterShock(new(EcgElectricalTherapy.SinusTemplateId, ElectricalConversionSettings.Default));
        Check.That(sinus.PressureTransducers == limits, "cardiac therapy does not reset transducer settings");
    }
}
