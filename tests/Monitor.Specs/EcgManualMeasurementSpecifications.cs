// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class EcgManualMeasurementSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ManualCursorsProduceExactDifferences), ManualCursorsProduceExactDifferences),
        new(nameof(ManualRateRequiresPermissionAndNonzeroTime), ManualRateRequiresPermissionAndNonzeroTime),
        new(nameof(ManualCursorValidationDoesNotChangeAcceptedEvidence), ManualCursorValidationDoesNotChangeAcceptedEvidence),
        new(nameof(ManualMeasurementHandlesExtremeRationalInputs), ManualMeasurementHandlesExtremeRationalInputs),
    ];

    private static void ManualCursorsProduceExactDifferences()
    {
        EcgManualMeasurementResult result = EcgManualMeasurement.Calculate(new(100_000_000, 1000, 3), new(900_000_000, -2000, 3), true);
        Check.That(result.ElapsedMilliseconds == new EcgMeasurementRatio(800, 1) &&
            result.AmplitudeChangeMillivolts == new EcgMeasurementRatio(-1, 1) &&
            result.AuxiliaryRatePerMinute == new EcgMeasurementRatio(75, 1),
            "manual cursors produce exact elapsed milliseconds, signed millivolts and optional auxiliary rate");
    }

    private static void ManualRateRequiresPermissionAndNonzeroTime()
    {
        EcgManualCursor cursor = new(0, 0, 1);
        EcgManualMeasurementResult zero = EcgManualMeasurement.Calculate(cursor, cursor, true);
        Check.That(zero.ElapsedMilliseconds == new EcgMeasurementRatio(0, 1) &&
            zero.AmplitudeChangeMillivolts == new EcgMeasurementRatio(0, 1) && zero.AuxiliaryRatePerMinute is null &&
            EcgManualMeasurement.Calculate(cursor, new(1, 1, 3), false).AuxiliaryRatePerMinute is null,
            "zero interval has no finite rate and denied auxiliary rate never appears");
        Check.That(EcgManualMeasurement.Calculate(cursor, new(1, 1, 3), true).ElapsedMilliseconds == new EcgMeasurementRatio(1, 1_000_000),
            "sub-millisecond cursor precision is retained");
    }

    private static void ManualCursorValidationDoesNotChangeAcceptedEvidence()
    {
        EcgManualCursor first = new(5, 1000, 1), second = new(10, 2000, 1);
        EcgManualMeasurementResult accepted = EcgManualMeasurement.Calculate(first, second, false);
        EcgManualCursor[] invalid = [null!, new(-1, 0, 1), new(10, 0, 0)];
        foreach (EcgManualCursor cursor in invalid)
        {
            try { _ = EcgManualMeasurement.Calculate(first, cursor, true); throw new InvalidOperationException("invalid cursor accepted"); }
            catch (EcgManualMeasurementException exception) { Check.That(exception.ReasonCode == "ManualMeasurement.InvalidCursor", "invalid cursor rejects"); }
        }
        try { _ = EcgManualMeasurement.Calculate(second, first, true); throw new InvalidOperationException("reversed time accepted"); }
        catch (EcgManualMeasurementException exception) { Check.That(exception.ReasonCode == "ManualMeasurement.TimeReversed", "reversed cursors reject"); }
        Check.That(EcgManualMeasurement.Calculate(first, second, false) == accepted, "rejected calculations preserve accepted cursor evidence");
    }

    private static void ManualMeasurementHandlesExtremeRationalInputs()
    {
        EcgManualCursor first = new(0, long.MinValue, uint.MaxValue), second = new(long.MaxValue, long.MaxValue, uint.MaxValue);
        EcgManualMeasurementResult result = EcgManualMeasurement.Calculate(first, second, true);
        Check.That(result.ElapsedMilliseconds.Numerator * 1_000_000 == (BigInteger)long.MaxValue * result.ElapsedMilliseconds.Denominator &&
            result.AmplitudeChangeMillivolts.Numerator * uint.MaxValue * 1000 ==
                ((BigInteger)long.MaxValue - long.MinValue) * result.AmplitudeChangeMillivolts.Denominator &&
            result == EcgManualMeasurement.Calculate(first with { }, second with { }, true),
            "extreme rational inputs avoid machine overflow and reconstruct deterministically from cursor values");
    }
}
