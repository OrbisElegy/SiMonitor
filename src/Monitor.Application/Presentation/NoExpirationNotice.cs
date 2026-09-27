// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

// Absence of accepted CO2 expirations in a continuous usable sample stream.
// Not a diagnosis of asphyxia and not a substitute for sensor-fault detection.
public static class NoExpirationNotice
{
    public static MonitorNotice? Evaluate(bool enabled, int? delaySeconds, long nowNs, CapnographyActivity? activity)
    {
        if (!enabled) { return null; }
        if (delaySeconds is null or < 5 or > 120)
        { return new("co2-absence-settings", MonitorNoticeLevel.Info, "CO₂ 呼吸等待时限无效：请输入5–120秒整数") { Audible = false }; }
        if (activity is not { Status: WaveformMeasurementStatus.Valid, ContinuousUsableSinceNs: { } since, LastSampleNs: { } last } ||
            since < 0 || since > last || last > nowNs || nowNs - last > 500_000_000 ||
            activity.LastExpirationNs is { } invalid && (invalid < 0 || invalid > last)) { return null; }
        long anchor = Math.Max(since, activity.LastExpirationNs ?? since);
        return last - anchor >= delaySeconds.Value * 1_000_000_000L
            ? new("co2-no-expiration", MonitorNoticeLevel.Critical, "CO₂ 未检出呼吸") { Numeric = MonitorNumeric.Co2RespirationRate }
            : null;
    }
}
