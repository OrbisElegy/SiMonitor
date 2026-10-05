// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

// Immediate local teaching limits, not a latched AlarmEpisode or a diagnosis.
// Evaluate only the current measured value; never retain a last valid low value.
public static class SpO2LimitNotice
{
    public static MonitorNotice? Evaluate(bool enabled, int? warningMilliPercent,
        int? criticalMilliPercent, OpticalSaturationReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (!enabled) { return null; }
        if (warningMilliPercent is not (>= 1000 and <= 100000) ||
            criticalMilliPercent is not (>= 1000 and <= 99000) || criticalMilliPercent >= warningMilliPercent)
        {
            return new("spo2-settings", MonitorNoticeLevel.Info, "SpO₂ 提示设置无效：须满足 1% ≤ Critical 下限 < Warning 下限 ≤ 100%")
            { Message = new("alarm.spo2SettingsInvalid") };
        }
        if (reading.Status != WaveformMeasurementStatus.Valid || reading.SaturationMilliPercent is not (>= 0 and <= 100000))
        { return null; }
        int value = reading.SaturationMilliPercent.Value;
        if (value >= warningMilliPercent) { return null; }
        bool critical = value < criticalMilliPercent;
        return new("spo2-low", critical ? MonitorNoticeLevel.Critical : MonitorNoticeLevel.Warning,
            critical ? "SpO₂ 极低" : "SpO₂ 低")
        { Numeric = MonitorNumeric.SpO2, Message = MeasuredLimitNotice.LimitMessage(MeasuredLimitNotice.SpO2Descriptor.LabelMessage, true, critical) };
    }
}
