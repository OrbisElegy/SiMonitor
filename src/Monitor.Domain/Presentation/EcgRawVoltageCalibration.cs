// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public sealed class EcgRawVoltageException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// Explicit affine calibration: value in UnitCode = raw * scale + offset.
// The caller resolves these fields from the applicable acquisition profile.
public sealed record EcgRawVoltageCalibration(int ScaleNumerator, uint ScaleDenominator,
    int OffsetNumerator, uint OffsetDenominator, string UnitCode)
{
    public void Validate()
    {
        if (ScaleDenominator == 0 || OffsetDenominator == 0)
        { throw new EcgRawVoltageException("EcgRawVoltage.InvalidCalibration", "calibration"); }
        if (UnitCode is not ("MICROVOLT" or "MILLIVOLT"))
        { throw new EcgRawVoltageException("EcgRawVoltage.UnsupportedUnit", nameof(UnitCode)); }
    }

    public EcgSampleVoltage Convert(short raw)
    {
        Validate();
        Int128 numerator = (Int128)raw * ScaleNumerator * OffsetDenominator +
            (Int128)OffsetNumerator * ScaleDenominator;
        Int128 denominator = (Int128)ScaleDenominator * OffsetDenominator;
        if (UnitCode == "MILLIVOLT") { numerator *= 1000; }
        var left = Int128.Abs(numerator);
        Int128 right = denominator;
        while (right != 0) { (left, right) = (right, left % right); }
        numerator /= left;
        denominator /= left;
        if (numerator < long.MinValue || numerator > long.MaxValue || denominator > uint.MaxValue)
        { throw new EcgRawVoltageException("EcgRawVoltage.UnrepresentableVoltage", nameof(raw)); }
        return new((long)numerator, (uint)denominator);
    }
}
