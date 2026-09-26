// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Measurements;

public enum MeasurementSource { Ecg, Pleth, ImpedanceRespiration, Co2 }
public enum MeasurementTechnicalFault { None, ExcessiveInterference, SensorDisconnected, LeadsDisconnected }
public sealed record MeasurementDisplay(string NumericText, string? TopNotice)
{
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
            _ => "CO2"
        };
        if (fault != MeasurementTechnicalFault.None)
        {
            string notice = fault switch
            {
                MeasurementTechnicalFault.ExcessiveInterference => label + "干扰过大",
                MeasurementTechnicalFault.LeadsDisconnected => label + "导联脱落",
                _ => label + "传感器脱落"
            };
            return new("---", notice);
        }
        return status switch
        {
            WaveformMeasurementStatus.Valid when !string.IsNullOrWhiteSpace(validNumericText) => new(validNumericText, null),
            WaveformMeasurementStatus.Valid => throw new ArgumentException("MeasurementDisplay.MissingValue", nameof(validNumericText)),
            WaveformMeasurementStatus.Uncountable => new(source == MeasurementSource.Ecg ? "-?-" : "---", label + "无法可靠计数"),
            WaveformMeasurementStatus.PoorSignal => new("---", label + "信号质量不足"),
            WaveformMeasurementStatus.NoData => new("---", label + "无数据"),
            WaveformMeasurementStatus.Stale => new("---", label + "测量值已过期"),
            _ => new("---", label + "等待测量")
        };
    }
}
