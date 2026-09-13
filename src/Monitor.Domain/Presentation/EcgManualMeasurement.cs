// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace Monitor.Domain.Presentation;

public sealed class EcgManualMeasurementException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// Caller-resolved cursors from one authorized record, never detected wave boundaries.
public sealed record EcgManualCursor(long DataTimeNs, long NumeratorMicrovolts, uint Denominator);
public sealed record EcgMeasurementRatio(BigInteger Numerator, BigInteger Denominator);
public sealed record EcgManualMeasurementResult(EcgMeasurementRatio ElapsedMilliseconds,
    EcgMeasurementRatio AmplitudeChangeMillivolts, EcgMeasurementRatio? AuxiliaryRatePerMinute);

public static class EcgManualMeasurement
{
    // Time cursors must be ordered. Amplitude change is signed (second - first).
    // Permission and common-record identity must be established by the caller.
    public static EcgManualMeasurementResult Calculate(EcgManualCursor first, EcgManualCursor second,
        bool allowAuxiliaryRate)
    {
        Validate(first, nameof(first));
        Validate(second, nameof(second));
        if (second.DataTimeNs < first.DataTimeNs)
        { throw new EcgManualMeasurementException("ManualMeasurement.TimeReversed", nameof(second)); }
        BigInteger elapsed = (BigInteger)second.DataTimeNs - first.DataTimeNs;
        BigInteger amplitude = (BigInteger)second.NumeratorMicrovolts * first.Denominator -
            (BigInteger)first.NumeratorMicrovolts * second.Denominator;
        BigInteger amplitudeDenominator = (BigInteger)first.Denominator * second.Denominator * 1000;
        return new(Ratio(elapsed, 1_000_000), Ratio(amplitude, amplitudeDenominator),
            allowAuxiliaryRate && elapsed > 0 ? Ratio(60_000_000_000, elapsed) : null);
    }

    private static void Validate(EcgManualCursor cursor, string parameterName)
    {
        if (cursor is null || cursor.DataTimeNs < 0 || cursor.Denominator == 0)
        { throw new EcgManualMeasurementException("ManualMeasurement.InvalidCursor", parameterName); }
    }

    private static EcgMeasurementRatio Ratio(BigInteger numerator, BigInteger denominator)
    {
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(numerator / divisor, denominator / divisor);
    }
}
