// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;

namespace Monitor.Application.Measurements;

public enum MeasurementSource { Ecg, Pleth, ImpedanceRespiration, Co2, Pressure, SpO2 }
public enum MeasurementTechnicalFault { None, ExcessiveInterference, SensorDisconnected, LeadsDisconnected }
public sealed record MeasurementDisplay(string NumericText, string? TopNotice)
{
    // TopNotice rendered in the reader's language; null exactly when TopNotice is null.
    // Only the pressure source label is translated.
    public TextMessage? TopNoticeMessage { get; init; }

    // Presentation contract only. Faults must come from an explicit acquisition
    // or connection assessment, not from source presets or a flat waveform.
    // The monitor's future time-adjacent technical notice uses white text.
    public static MeasurementDisplay Resolve(MeasurementSource source, WaveformMeasurementStatus status,
        string? validNumericText, MeasurementTechnicalFault fault = MeasurementTechnicalFault.None)
    {
        if (!Enum.IsDefined(source) || !Enum.IsDefined(status) || !Enum.IsDefined(fault))
        { throw new ArgumentException("MeasurementDisplay.InvalidState"); }
        string label = source switch
        {
            MeasurementSource.Ecg => "ECG",
            MeasurementSource.Pleth => "Pleth",
            MeasurementSource.ImpedanceRespiration => "Resp",
            MeasurementSource.Pressure => "压力",
            MeasurementSource.SpO2 => "SpO₂",
            _ => "CO2"
        };
        object labelArgument = source == MeasurementSource.Pressure ? new TextMessage("measurement.sourcePressure") : label;
        MeasurementDisplay Notice(string numeric, string suffix, string key) =>
            new(numeric, label + suffix) { TopNoticeMessage = new(key, labelArgument) };
        if (fault != MeasurementTechnicalFault.None)
        {
            return fault switch
            {
                MeasurementTechnicalFault.ExcessiveInterference => Notice("---", "干扰过大", "measurement.faultInterference"),
                MeasurementTechnicalFault.LeadsDisconnected => Notice("---", "导联脱落", "measurement.faultLeadsOff"),
                _ => Notice("---", "传感器脱落", "measurement.faultSensorOff")
            };
        }
        return status switch
        {
            WaveformMeasurementStatus.Valid when !string.IsNullOrWhiteSpace(validNumericText) => new(validNumericText, null),
            WaveformMeasurementStatus.Valid => throw new ArgumentException("MeasurementDisplay.MissingValue", nameof(validNumericText)),
            WaveformMeasurementStatus.Uncountable => Notice(source == MeasurementSource.Ecg ? "-?-" : "---", "无法可靠计数", "measurement.uncountable"),
            WaveformMeasurementStatus.PoorSignal => Notice("---", "信号质量不足", "measurement.poorSignal"),
            WaveformMeasurementStatus.OutOfRange => Notice("---", "超出测量范围", "measurement.outOfRange"),
            WaveformMeasurementStatus.NoData => Notice("---", "无数据", "measurement.noData"),
            WaveformMeasurementStatus.Stale => Notice("---", "测量值已过期", "measurement.stale"),
            _ => Notice("---", "等待测量", "measurement.waiting")
        };
    }
}
