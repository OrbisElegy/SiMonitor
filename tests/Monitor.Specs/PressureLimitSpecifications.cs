// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class PressureLimitSpecifications
{
    private const long StepNs = 200_000_000;
    private const long SecondNs = 1_000_000_000;
    private static readonly LiveMeasurementSnapshot Empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
    private static readonly MonitorNumeric[] Pressures = [MonitorNumeric.AbpMean, MonitorNumeric.PaMean, MonitorNumeric.CvpMean];
    public static Specification[] All =>
    [
        new(nameof(PressureLimitsConfirmEachBoundaryAtItsExactTime), PressureLimitsConfirmEachBoundaryAtItsExactTime),
        new(nameof(PressureLimitsRejectBriefCrossingsAndRequireRecovery), PressureLimitsRejectBriefCrossingsAndRequireRecovery),
        new(nameof(PressureLimitsConfirmSeverityIndependently), PressureLimitsConfirmSeverityIndependently),
        new(nameof(PressureLimitsClearInvalidEvidenceAndConfiguration), PressureLimitsClearInvalidEvidenceAndConfiguration),
        new(nameof(PressureLimitsUseSimulationTimeAndResetDiscontinuities), PressureLimitsUseSimulationTimeAndResetDiscontinuities),
        new(nameof(PressureLimitsKeepChannelsAndDirectionsIndependent), PressureLimitsKeepChannelsAndDirectionsIndependent),
        new("AarPressureAlarmBoundaryRegression", () => VerifyRhythm(PhysiologyIllustrationConfiguration.AarPreset, "AAR")),
        new("AjrPressureAlarmBoundaryRegression", () => VerifyRhythm(PhysiologyIllustrationConfiguration.AjrPreset, "AJR")),
        new("AivrPressureAlarmBoundaryRegression", () => VerifyRhythm(PhysiologyIllustrationConfiguration.AivrPreset, "AIVR")),
        new("VtPressureAlarmBoundaryRegression", () => VerifyRhythm(PhysiologyIllustrationConfiguration.VtPreset, "VT"))
    ];

    private static LiveMeasurementSnapshot Snapshot(long timeNs, MonitorNumeric numeric, int? value,
        WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid)
    {
        var reading = new MeanPressureReading(status, value, Math.Max(0, timeNs - 4 * SecondNs), timeNs);
        var snapshot = Empty with { SampleTimeNs = timeNs };
        return numeric switch
        {
            MonitorNumeric.AbpMean => snapshot with { AbpMean = reading },
            MonitorNumeric.PaMean => snapshot with { PaMean = reading },
            MonitorNumeric.CvpMean => snapshot with { CvpMean = reading },
            _ => throw new ArgumentOutOfRangeException(nameof(numeric))
        };
    }

    private sealed class Probe(MonitorNumeric numeric = MonitorNumeric.AbpMean)
    {
        internal PressureLimitNotice Filter { get; } = new(numeric);
        internal MeasurementLimits Limits { get; set; } = MeasuredLimitNotice.Describe(numeric).TeachingDefaults with { Enabled = true };
        internal MonitorNotice? At(long timeNs, int? value, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid) =>
            Filter.Evaluate(Limits, Snapshot(timeNs, numeric, value, status));
        internal MonitorNotice? Run(long fromNs, long throughNs, int value)
        {
            MonitorNotice? result = null;
            for (long time = fromNs; time <= throughNs; time += StepNs) { result = At(time, value); }
            return result;
        }
    }

    private static void PressureLimitsConfirmEachBoundaryAtItsExactTime()
    {
        foreach (var numeric in Pressures)
            foreach (bool low in new[] { true, false })
                foreach (bool critical in new[] { false, true })
                {
                    var probe = new Probe(numeric);
                    int boundary = (low ? critical ? probe.Limits.CriticalLow : probe.Limits.WarningLow :
                        critical ? probe.Limits.CriticalHigh : probe.Limits.WarningHigh)!.Value;
                    int value = boundary + (low ? -1 : 1);
                    long delay = low ? 4 * SecondNs : 10 * SecondNs;
                    for (long time = 0; time < delay; time += StepNs)
                    { Check.That(probe.At(time, value) is null, "unconfirmed pressure must not alarm"); }
                    Check.That(probe.At(delay - 1, value) is null, "one nanosecond before confirmation");
                    var notice = probe.At(delay, value);
                    Check.That(notice?.Level == (critical ? MonitorNoticeLevel.Critical : MonitorNoticeLevel.Warning) &&
                        notice.Numeric == numeric && notice.Id.EndsWith(low ? "-low" : "-high", StringComparison.Ordinal),
                        "exact confirmation retains channel, direction and native centi-mmHg precision");
                    var equality = new Probe(numeric);
                    Check.That(equality.Run(0, delay, boundary)?.Level == (critical ? MonitorNoticeLevel.Warning : null),
                        "equality is not a violation of that boundary");
                }
    }

    private static void PressureLimitsRejectBriefCrossingsAndRequireRecovery()
    {
        var probe = new Probe();
        for (long time = 0; time <= 20 * SecondNs; time += StepNs)
        { Check.That(probe.At(time, time / StepNs % 2 == 0 ? 5999 : 6001) is null, "brief repeated crossings do not accumulate"); }
        Check.That(probe.Run(21 * SecondNs, 25 * SecondNs, 5999)?.Level == MonitorNoticeLevel.Warning, "sustained low pressure confirms");
        for (long time = 25 * SecondNs + StepNs; time <= 40 * SecondNs; time += StepNs)
        { Check.That(probe.At(time, time / StepNs % 2 == 0 ? 5999 : 6001)?.Level == MonitorNoticeLevel.Warning, "brief recovery retains the same alarm"); }
        Check.That(probe.Run(40 * SecondNs + StepNs, 43 * SecondNs, 6000)?.Level == MonitorNoticeLevel.Warning, "recovery is not complete early");
        Check.That(probe.At(43 * SecondNs + StepNs - 1, 6000)?.Level == MonitorNoticeLevel.Warning, "exact recovery edge minus one");
        Check.That(probe.At(43 * SecondNs + StepNs, 6000) is null, "three continuous seconds back within limits clears");
        Check.That(probe.Run(44 * SecondNs, 44 * SecondNs + StepNs, 5999) is null, "a new episode must confirm again");
        Check.That(probe.Run(44 * SecondNs + 2 * StepNs, 48 * SecondNs, 5999)?.Level == MonitorNoticeLevel.Warning, "genuine recurrence can alarm");
    }

    private static void PressureLimitsConfirmSeverityIndependently()
    {
        foreach (bool low in new[] { true, false })
        {
            var probe = new Probe();
            int boundary = low ? 4000 : 14000;
            long delay = low ? 4 * SecondNs : 10 * SecondNs;
            for (long time = 0; time <= 12 * SecondNs; time += StepNs)
            {
                var notice = probe.At(time, boundary + (time / StepNs % 2 == 0 ? -1 : 1));
                Check.That(notice?.Level == (time >= delay ? MonitorNoticeLevel.Warning : null),
                    "Critical chatter cannot starve a continuously breached Warning threshold");
            }
            int criticalValue = boundary + (low ? -1 : 1);
            probe.At(12 * SecondNs, boundary + (low ? 1 : -1));
            long start = 12 * SecondNs + StepNs;
            Check.That(probe.Run(start, start + delay - StepNs, criticalValue)?.Level == MonitorNoticeLevel.Warning, "escalation retains warning while confirming");
            var critical = probe.At(start + delay, criticalValue);
            Check.That(critical?.Level == MonitorNoticeLevel.Critical, "sustained deterioration escalates");
            var rotation = new MonitorNoticeRotation();
            rotation.Update([critical!], start + delay);
            for (long time = start + delay + StepNs; time <= 30 * SecondNs; time += StepNs)
            {
                var notice = probe.At(time, boundary + (time / StepNs % 2 == 0 ? -1 : 1));
                Check.That(notice == critical, "boundary chatter preserves the exact notice used by display and sound");
                rotation.Update([notice!], time);
                Check.That(rotation.CriticalElapsedNs(notice!.Id) == time - start - delay, "Critical elapsed timer never restarts on chatter");
            }
            int warningValue = boundary + (low ? 1 : -1);
            probe.At(30 * SecondNs, criticalValue);
            Check.That(probe.Run(30 * SecondNs + StepNs, 33 * SecondNs, warningValue)?.Level == MonitorNoticeLevel.Critical, "downgrade waits for stable recovery");
            Check.That(probe.At(33 * SecondNs + StepNs, warningValue)?.Level == MonitorNoticeLevel.Warning, "recovered Critical downgrades to ongoing Warning");
        }
    }

    private static void PressureLimitsClearInvalidEvidenceAndConfiguration()
    {
        foreach (var numeric in Pressures)
        {
            var descriptor = MeasuredLimitNotice.Describe(numeric);
            foreach (var status in Enum.GetValues<WaveformMeasurementStatus>().Where(s => s != WaveformMeasurementStatus.Valid))
            {
                var probe = new Probe(numeric);
                int value = probe.Limits.CriticalLow!.Value - 1;
                Check.That(probe.Run(0, 4 * SecondNs, value)?.Level == MonitorNoticeLevel.Critical, "precondition active");
                Check.That(probe.At(4 * SecondNs + StepNs, value, status) is null, "invalid data clears immediately, without recovery delay");
                Check.That(probe.Run(4 * SecondNs + 2 * StepNs, 8 * SecondNs + StepNs, value) is null, "old evidence is not retained across invalid data");
                Check.That(probe.At(8 * SecondNs + 2 * StepNs, value)?.Level == MonitorNoticeLevel.Critical, "fresh evidence confirms after loss");
            }
            foreach (int? invalid in new int?[] { null, descriptor.Minimum - 1, descriptor.Maximum + 1 })
            {
                var probe = new Probe(numeric);
                probe.Run(0, 4 * SecondNs, probe.Limits.CriticalLow!.Value - 1);
                Check.That(probe.At(4 * SecondNs, invalid) is null, "missing and out-of-range values clear even at the same timestamp");
            }
        }
        foreach (var changed in new MeasurementLimits[]
        {
            new(false, 4000, 6000, 11000, 14000), new(true, null, 6000, 11000, 14000),
            new(true, 6000, 4000, 11000, 14000), new(true, 4100, 6000, 11000, 14000)
        })
        {
            var probe = new Probe();
            probe.Run(0, 4 * SecondNs, 3999);
            var original = probe.Limits;
            probe.Limits = changed;
            var notice = probe.At(4 * SecondNs, 3999);
            Check.That(notice is null || notice.Level == MonitorNoticeLevel.Info && notice.Numeric is null, "configuration changes clear old physiological state immediately");
            probe.Limits = original;
            Check.That(probe.Run(4 * SecondNs + StepNs, 8 * SecondNs, 3999) is null, "re-enabling or editing starts a fresh confirmation");
            Check.That(probe.At(8 * SecondNs + StepNs, 3999)?.Level == MonitorNoticeLevel.Critical, "new configuration can alarm after confirmation");
        }
    }

    private static void PressureLimitsUseSimulationTimeAndResetDiscontinuities()
    {
        var probe = new Probe();
        probe.Run(0, 4 * SecondNs - StepNs, 3999);
        for (int i = 0; i < 100; i++)
        { Check.That(probe.At(4 * SecondNs - StepNs, 3999) is null, "pause and repeated reads do not advance confirmation"); }
        Check.That(probe.At(4 * SecondNs, 3999)?.Level == MonitorNoticeLevel.Critical, "only simulation elapsed time advances evidence");
        Check.That(probe.At(20 * SecondNs, 3999) is null, "an observation gap cannot supply unobserved evidence");
        Check.That(probe.Run(20 * SecondNs + StepNs, 24 * SecondNs, 3999)?.Level == MonitorNoticeLevel.Critical, "recovery after a gap uses fresh evidence");
        Check.That(probe.At(0, 3999) is null, "clock rewind resets previous session state");
        probe.Run(StepNs, 4 * SecondNs, 3999);
        probe.Filter.Reset();
        Check.That(probe.At(4 * SecondNs, 3999) is null, "explicit session reset also works without a clock rewind");
        bool rejected = false;
        try { probe.At(-1, 3999); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected, "negative simulation time rejects");
    }

    private static void PressureLimitsKeepChannelsAndDirectionsIndependent()
    {
        var abp = new PressureLimitNotice(MonitorNumeric.AbpMean);
        var pa = new PressureLimitNotice(MonitorNumeric.PaMean);
        var limits = new MeasurementLimits(true, 4000, 6000, 11000, 14000);
        for (long time = 0; time <= 10 * SecondNs; time += StepNs)
        {
            var snapshot = Snapshot(time, MonitorNumeric.AbpMean, 3999) with
            { PaMean = new(WaveformMeasurementStatus.Valid, 14001, 0, time) };
            Check.That(abp.Evaluate(limits, snapshot)?.Level == (time >= 4 * SecondNs ? MonitorNoticeLevel.Critical : null), "low ABP has its own timer");
            Check.That(pa.Evaluate(limits, snapshot)?.Level == (time >= 10 * SecondNs ? MonitorNoticeLevel.Critical : null), "high PA has its own timer");
        }
        var probe = new Probe();
        for (long time = 0; time <= 30 * SecondNs; time += StepNs)
        { Check.That(probe.At(time, time / StepNs % 2 == 0 ? 3999 : 14001) is null, "opposite violations never share accumulated confirmation"); }
        probe.Run(31 * SecondNs, 35 * SecondNs, 3999);
        Check.That(probe.Run(35 * SecondNs + StepNs, 45 * SecondNs, 14001) is null, "new high direction has not completed its own delay");
        Check.That(probe.At(45 * SecondNs + StepNs, 14001)?.Id == "abp-mean-high", "sustained opposite violation eventually replaces low");
    }

    private static void VerifyRhythm(PhysiologyIllustrationConfiguration configuration, string name)
    {
        var source = PhysiologyIllustrationSource.Create(configuration);
        var measurement = LiveWaveformMeasurements.CreateIllustration();
        var snapshots = new List<LiveMeasurementSnapshot>();
        for (long time = StepNs; time <= 80 * SecondNs; time += StepNs)
            foreach (byte[] wire in source.AdvanceTo(time, 50, 1, 100))
            {
                var snapshot = measurement.Consume(wire);
                if (snapshot.SampleTimeNs >= 20 * SecondNs) { snapshots.Add(snapshot); }
            }
        Check.That(snapshots.Count > 250 && snapshots.All(s => s.AbpMean.Status == WaveformMeasurementStatus.Valid), "real acquired pressure remains valid: " + name);
        int minimum = snapshots.Min(s => s.AbpMean.MeanCentiMmHg!.Value);
        int maximum = snapshots.Max(s => s.AbpMean.MeanCentiMmHg!.Value);
        int midpoint = (minimum + maximum) / 2;
        Check.That(maximum - minimum > 2, "fixture has real boundary-crossing variation: " + name);
        foreach (bool critical in new[] { false, true })
        {
            // Put the chosen boundary inside the actual acquired range; leave
            // source, waveform and pressure measurement entirely untouched.
            var limits = new MeasurementLimits(true, minimum - 3000, minimum - 2000,
                critical ? minimum - 1 : midpoint, critical ? midpoint : maximum + 1000);
            var filter = new PressureLimitNotice(MonitorNumeric.AbpMean);
            MonitorNotice? lastRaw = null, lastFiltered = null;
            int rawChanges = 0, filteredChanges = 0;
            foreach (var snapshot in snapshots)
            {
                var raw = MeasuredLimitNotice.Evaluate(MonitorNumeric.AbpMean, limits, snapshot);
                var filtered = filter.Evaluate(limits, snapshot);
                if (raw != lastRaw) { rawChanges++; }
                if (filtered != lastFiltered) { filteredChanges++; }
                lastRaw = raw;
                lastFiltered = filtered;
            }
            Console.WriteLine($"{name} ABP {minimum / 100m:F2}–{maximum / 100m:F2} mmHg, {(critical ? "Critical" : "Warning")} boundary: {rawChanges} instantaneous changes, {filteredChanges} confirmed changes");
            // AIVR filling introduces a 12-second AV-phase cycle: this roughly
            // one-minute acquired interval now has ten real boundary crossings.
            Check.That(rawChanges >= 10 && filteredChanges <= 1, "pressure oscillation no longer creates repeated alarm transitions: " + name);
            Check.That(lastFiltered?.Level == (critical ? MonitorNoticeLevel.Warning : null), "continuous warning survives Critical chatter: " + name);
        }
        var lowLimits = new MeasurementLimits(true, minimum - 1000, maximum + 1, maximum + 2000, maximum + 3000);
        var lowFilter = new PressureLimitNotice(MonitorNumeric.AbpMean);
        long firstNs = snapshots[0].SampleTimeNs;
        foreach (var snapshot in snapshots)
        {
            var notice = lowFilter.Evaluate(lowLimits, snapshot);
            Check.That(notice?.Level == (snapshot.SampleTimeNs - firstNs >= 4 * SecondNs ? MonitorNoticeLevel.Warning : null),
                "sustained low pressure from the filling model still confirms at four seconds: " + name);
        }
    }
}
