// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Numerics;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

internal static class MeasurementReadout
{
    public static string? Format(RecordMeasurementDisplay? display)
    {
        if (display is not { ReasonCode: "RecordMeasurement.Ready", Measurement: { } result }) { return null; }
        string text = $"Δt：{Exact(result.ElapsedMilliseconds)} ms    ΔV（终点−起点）：{Exact(result.AmplitudeChangeMillivolts)} mV";
        if (result.AuxiliaryRatePerMinute is { } rate) { text += $"    辅助频率：{Exact(rate)} 次/分"; }
        return text;
    }

    // Prefer short exact decimals, otherwise retain the rational value. Display
    // precision must not introduce rounding or modify the measurement evidence.
    internal static string Exact(EcgMeasurementRatio ratio)
    {
        BigInteger numerator = ratio.Numerator;
        BigInteger denominator = ratio.Denominator;
        if (denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(ratio)); }
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        numerator /= gcd;
        denominator /= gcd;
        for (int decimals = 0; decimals <= 6; decimals++)
        {
            var scaled = BigInteger.DivRem(BigInteger.Abs(numerator) * BigInteger.Pow(10, decimals), denominator, out BigInteger remainder);
            if (!remainder.IsZero) { continue; }
            string digits = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
            if (decimals != 0) { digits = digits.Insert(digits.Length - decimals, "."); }
            return numerator.Sign < 0 ? "-" + digits : digits;
        }
        return numerator.ToString(CultureInfo.InvariantCulture) + "/" + denominator.ToString(CultureInfo.InvariantCulture);
    }
}
