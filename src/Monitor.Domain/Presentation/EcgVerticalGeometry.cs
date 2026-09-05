// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public sealed class EcgVerticalGeometryException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// Already resolved display geometry, not a claim of physical screen calibration.
public sealed record EcgVerticalScale(
    int PlotTopPixels,
    int PlotHeightPixels,
    int ZeroBaselinePixels,
    uint PixelsPerMillivoltNumerator,
    uint PixelsPerMillivoltDenominator);

public enum VerticalPlotRelation
{
    AbovePlot,
    WithinPlot,
    BelowPlot,
}

public sealed record EcgVerticalPosition(
    Int128 PixelNumerator,
    Int128 PixelDenominator,
    VerticalPlotRelation Relation);

public static class EcgVerticalGeometry
{
    // Positive voltage moves upward in screen coordinates. Preserve the exact
    // unclamped point so a future path clipper cannot create an edge plateau.
    public static EcgVerticalPosition MapMicrovolts(
        EcgVerticalScale scale,
        long amplitudeNumeratorMicrovolts,
        uint amplitudeDenominator)
    {
        ArgumentNullException.ThrowIfNull(scale);
        long bottom = (long)scale.PlotTopPixels + scale.PlotHeightPixels;
        if (scale.PlotTopPixels < 0 || scale.PlotHeightPixels <= 0 || bottom > int.MaxValue ||
            scale.ZeroBaselinePixels < scale.PlotTopPixels || scale.ZeroBaselinePixels > bottom ||
            scale.PixelsPerMillivoltNumerator == 0 || scale.PixelsPerMillivoltDenominator == 0)
        {
            throw new EcgVerticalGeometryException("EcgGeometry.InvalidScale", nameof(scale));
        }
        if (amplitudeDenominator == 0)
        {
            throw new EcgVerticalGeometryException("EcgGeometry.InvalidAmplitude", nameof(amplitudeDenominator));
        }

        // Bounded input widths keep all products below signed Int128 capacity.
        Int128 denominator = (Int128)amplitudeDenominator * scale.PixelsPerMillivoltDenominator * 1000;
        Int128 numerator = (Int128)scale.ZeroBaselinePixels * denominator -
            (Int128)amplitudeNumeratorMicrovolts * scale.PixelsPerMillivoltNumerator;
        VerticalPlotRelation relation = numerator < (Int128)scale.PlotTopPixels * denominator
            ? VerticalPlotRelation.AbovePlot
            : numerator > (Int128)bottom * denominator
                ? VerticalPlotRelation.BelowPlot
                : VerticalPlotRelation.WithinPlot;
        Int128 divisor = GreatestCommonDivisor(Int128.Abs(numerator), denominator);
        return new EcgVerticalPosition(numerator / divisor, denominator / divisor, relation);
    }

    private static Int128 GreatestCommonDivisor(Int128 left, Int128 right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }
        return left;
    }
}
