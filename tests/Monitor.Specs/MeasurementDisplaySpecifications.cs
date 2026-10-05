// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;
using Monitor.Application.Measurements;

namespace Monitor.Specs;

internal static class MeasurementDisplaySpecifications
{
    public static Specification[] All => [new(nameof(MeasurementPlaceholdersRespectEvidenceAndSuppressStaleNumbers), MeasurementPlaceholdersRespectEvidenceAndSuppressStaleNumbers)];

    private static void MeasurementPlaceholdersRespectEvidenceAndSuppressStaleNumbers()
    {
        foreach (var source in Enum.GetValues<MeasurementSource>())
        {
            Check.That(MeasurementDisplay.Resolve(source, WaveformMeasurementStatus.Valid, "75") == new MeasurementDisplay("75", null), "valid number shown without technical notice");
            foreach (var status in new[] { WaveformMeasurementStatus.WarmingUp, WaveformMeasurementStatus.NoData, WaveformMeasurementStatus.Stale, WaveformMeasurementStatus.PoorSignal })
            {
                var display = MeasurementDisplay.Resolve(source, status, "75");
                Check.That(display.NumericText == "---" && display.TopNotice is not null && !display.TopNotice.Contains("脱落", StringComparison.Ordinal),
                    "unusable numbers suppressed; missing data does not invent a disconnected sensor");
            }
        }
        Check.That(MeasurementDisplay.Resolve(MeasurementSource.Ecg, WaveformMeasurementStatus.Uncountable, "300").NumericText == "-?-",
            "unreliably countable ECG is distinguished from missing data");
        Check.That(MeasurementDisplay.Resolve(MeasurementSource.Co2, WaveformMeasurementStatus.Uncountable, null).NumericText == "---",
            "ECG question placeholder does not leak to other measurements");
        Check.That(MeasurementDisplay.Resolve(MeasurementSource.Ecg, WaveformMeasurementStatus.Valid, "75", MeasurementTechnicalFault.ExcessiveInterference)
            == new MeasurementDisplay("---", "ECG干扰过大") { TopNoticeMessage = new("measurement.faultInterference", "ECG") },
            "explicit interference overrides a previously valid number");
        Check.That(MeasurementDisplay.Resolve(MeasurementSource.Co2, WaveformMeasurementStatus.NoData, null, MeasurementTechnicalFault.SensorDisconnected)
            == new MeasurementDisplay("---", "CO2传感器脱落") { TopNoticeMessage = new("measurement.faultSensorOff", "CO2") },
            "confirmed sensor fault produces requested notice");
        bool rejected = false;
        try { MeasurementDisplay.Resolve(MeasurementSource.Ecg, WaveformMeasurementStatus.Valid, null); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "valid state cannot omit its numeric value");
    }
}
