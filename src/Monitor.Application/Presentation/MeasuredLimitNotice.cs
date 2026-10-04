// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

public sealed record MeasurementLimits(bool Enabled, int? CriticalLow, int? WarningLow, int? WarningHigh, int? CriticalHigh);
public sealed record MeasurementLimitDescriptor(MonitorNumeric Numeric, string Id, string Label, string Unit,
    int Divisor, int Minimum, int Maximum, MeasurementLimits TeachingDefaults);

// Values and limits use the measurement's native integer units. These are
// instantaneous local teaching conditions. ConfirmedLimitNotice adds temporal
// validation before these conditions reach presentation.
public static class MeasuredLimitNotice
{
    public static MeasurementLimitDescriptor HeartRateDescriptor { get; } =
        new(MonitorNumeric.HeartRate, "hr", "ECG HR", "bpm", 1000, 0, int.MaxValue, new(false, 40000, 50000, 120000, 180000));
    public static MeasurementLimitDescriptor SpO2Descriptor { get; } =
        new(MonitorNumeric.SpO2, "spo2", "SpO₂", "%", 1000, 0, 100000, new(false, 85000, 92000, null, null));

    // Additional editors keep their existing channel set; HR and SpO2 have dedicated editors.
    public static IReadOnlyList<MeasurementLimitDescriptor> Descriptors { get; } = Array.AsReadOnly(new MeasurementLimitDescriptor[]
    {
        new(MonitorNumeric.RespirationRate, "resp-rate", "RR · RESP", "次/分", 1000, 0, 200000, new(false, 4000, 8000, 30000, 40000)),
        new(MonitorNumeric.PulseRate, "pleth-rate", "PR · PLETH", "bpm", 1000, 0, 350000, new(false, 40000, 50000, 120000, 180000)),
        new(MonitorNumeric.EtCo2, "etco2", "EtCO₂", "mmHg", 100, 0, 20000, new(false, 1500, 2500, 5000, 6000)),
        new(MonitorNumeric.Co2RespirationRate, "co2-rate", "RR · CO₂", "次/分", 1000, 0, 200000, new(false, 4000, 8000, 30000, 40000)),
        new(MonitorNumeric.AbpMean, "abp-mean", "ABP 平均压", "mmHg", 100, -10000, 40000, new(false, 4000, 6000, 11000, 14000)),
        new(MonitorNumeric.PaMean, "pa-mean", "PA 平均压", "mmHg", 100, -10000, 40000, new(false, 0, 500, 3000, 4500)),
        new(MonitorNumeric.CvpMean, "cvp-mean", "CVP 平均压", "mmHg", 100, -10000, 40000, new(false, -500, 0, 1500, 2500))
    });

    public static MeasurementLimitDescriptor Describe(MonitorNumeric numeric) => numeric switch
    {
        MonitorNumeric.HeartRate => HeartRateDescriptor,
        MonitorNumeric.SpO2 => SpO2Descriptor,
        _ => Descriptors.SingleOrDefault(d => d.Numeric == numeric)
            ?? throw new ArgumentException("MeasuredLimit.UnsupportedNumeric", nameof(numeric))
    };

    public static MonitorNotice? Evaluate(MonitorNumeric numeric, MeasurementLimits limits, LiveMeasurementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(limits); ArgumentNullException.ThrowIfNull(snapshot);
        var descriptor = Describe(numeric);
        if (numeric == MonitorNumeric.SpO2)
        {
            if (limits.WarningHigh is not null || limits.CriticalHigh is not null)
            { throw new ArgumentException("SpO2Limit.UnsupportedHighLimits", nameof(limits)); }
            return SpO2LimitNotice.Evaluate(limits.Enabled, limits.WarningLow, limits.CriticalLow, snapshot.SpO2);
        }
        if (!limits.Enabled) { return null; }
        if (limits.CriticalLow is not { } cl || limits.WarningLow is not { } wl || limits.WarningHigh is not { } wh || limits.CriticalHigh is not { } ch ||
            cl < descriptor.Minimum || ch > descriptor.Maximum || cl >= wl || wl >= wh || wh >= ch)
        { return new(descriptor.Id + "-settings", MonitorNoticeLevel.Info, descriptor.Label + " 提示设置无效：须满足 Critical 下限 < Warning 下限 < Warning 上限 < Critical 上限"); }
        var (status, value) = Read(numeric, snapshot);
        if (status != WaveformMeasurementStatus.Valid || value is not { } measured || measured < descriptor.Minimum || measured > descriptor.Maximum)
        { return null; }
        bool low = measured < wl, high = measured > wh;
        if (!low && !high) { return null; }
        bool critical = measured < cl || measured > ch;
        return CreateNotice(descriptor, low, critical);
    }

    internal static MonitorNotice CreateNotice(MeasurementLimitDescriptor descriptor, bool low, bool critical) =>
        new(descriptor.Id + (low ? "-low" : "-high"), critical ? MonitorNoticeLevel.Critical : MonitorNoticeLevel.Warning,
            descriptor.Label + (critical ? low ? " 极低" : " 极高" : low ? " 低" : " 高"))
        { Numeric = descriptor.Numeric };

    internal static (WaveformMeasurementStatus Status, int? Value) Read(MonitorNumeric numeric, LiveMeasurementSnapshot s) => numeric switch
    {
        MonitorNumeric.HeartRate => (s.HeartRate.Status, s.HeartRate.MilliBeatsPerMinute),
        MonitorNumeric.SpO2 => (s.SpO2.Status, s.SpO2.SaturationMilliPercent),
        MonitorNumeric.RespirationRate => (s.ImpedanceRespiration.Status, s.ImpedanceRespiration.MilliBreathsPerMinute),
        MonitorNumeric.PulseRate => (s.PulseRate.Status, s.PulseRate.MilliBeatsPerMinute),
        MonitorNumeric.EtCo2 => (s.Capnography.EndTidalCentiMmHg.Status, s.Capnography.EndTidalCentiMmHg.Value),
        MonitorNumeric.Co2RespirationRate => (s.Capnography.RespirationsMilliPerMinute.Status, s.Capnography.RespirationsMilliPerMinute.Value),
        MonitorNumeric.AbpMean => (s.AbpMean.Status, s.AbpMean.MeanCentiMmHg),
        MonitorNumeric.PaMean => (s.PaMean.Status, s.PaMean.MeanCentiMmHg),
        MonitorNumeric.CvpMean => (s.CvpMean.Status, s.CvpMean.MeanCentiMmHg),
        _ => throw new ArgumentException("MeasuredLimit.UnsupportedNumeric", nameof(numeric))
    };
}
