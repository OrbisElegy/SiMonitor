// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace Monitor.Domain.Presentation;

public sealed record EcgPaperGridPlan(int LeftPixels, int TopPixels, int WidthPixels, int HeightPixels,
    int OriginXPixels, int OriginYPixels, uint MinorSpacingNumerator, uint MinorSpacingDenominator);
public sealed record EcgPaperGridLine(bool IsVertical, ExactPlotCoordinate Position, bool IsMajor);
public sealed class EcgPaperGridException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// Logical geometry only. Caller resolves spacing against time/gain calibration.
public static class EcgPaperGridGeometry
{
    public static IReadOnlyList<EcgPaperGridLine> Build(EcgPaperGridPlan plan, int maximumLines)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (maximumLines <= 0)
        { throw new EcgPaperGridException("PaperGrid.InvalidLimit", nameof(maximumLines)); }
        if (plan.LeftPixels < 0 || plan.TopPixels < 0 || plan.WidthPixels <= 0 || plan.HeightPixels <= 0 ||
            (long)plan.LeftPixels + plan.WidthPixels > int.MaxValue || (long)plan.TopPixels + plan.HeightPixels > int.MaxValue ||
            plan.MinorSpacingNumerator == 0 || plan.MinorSpacingDenominator == 0)
        { throw new EcgPaperGridException("PaperGrid.InvalidPlan", nameof(plan)); }
        (BigInteger firstX, BigInteger endX) = Indices(plan.LeftPixels, plan.WidthPixels, plan.OriginXPixels, plan);
        (BigInteger firstY, BigInteger endY) = Indices(plan.TopPixels, plan.HeightPixels, plan.OriginYPixels, plan);
        BigInteger count = endX - firstX + endY - firstY;
        if (count > maximumLines)
        { throw new EcgPaperGridException("PaperGrid.LineLimitExceeded", nameof(maximumLines)); }
        List<EcgPaperGridLine> lines = new((int)count);
        AddLines(lines, firstX, endX, plan.OriginXPixels, true, plan);
        AddLines(lines, firstY, endY, plan.OriginYPixels, false, plan);
        return lines.AsReadOnly();
    }

    private static (BigInteger First, BigInteger End) Indices(int start, int length, int origin, EcgPaperGridPlan plan) =>
        (Ceiling(((BigInteger)start - origin) * plan.MinorSpacingDenominator, plan.MinorSpacingNumerator),
         Ceiling(((BigInteger)start + length - origin) * plan.MinorSpacingDenominator, plan.MinorSpacingNumerator));

    private static BigInteger Ceiling(BigInteger numerator, BigInteger denominator)
    {
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        return remainder > 0 ? quotient + 1 : quotient;
    }

    private static void AddLines(List<EcgPaperGridLine> lines, BigInteger first, BigInteger end,
        int origin, bool vertical, EcgPaperGridPlan plan)
    {
        for (BigInteger index = first; index < end; index++)
        {
            BigInteger numerator = (BigInteger)origin * plan.MinorSpacingDenominator + index * plan.MinorSpacingNumerator;
            BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, plan.MinorSpacingDenominator);
            lines.Add(new(vertical, new(numerator / divisor, plan.MinorSpacingDenominator / divisor), index % 5 == 0));
        }
    }
}
