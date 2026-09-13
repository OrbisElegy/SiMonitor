// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace Monitor.Domain.Presentation;

public sealed record EcgPaperScale(uint SpeedNumeratorMmPerSecond, uint SpeedDenominator,
    uint GainNumeratorMmPerMillivolt, uint GainDenominator);

// Paper-equivalent logical scale only, not physical monitor/printer calibration.
public static class EcgPaperGridCalibration
{
    public static EcgPaperGridPlan Resolve(int plotLeftPixels, int plotWidthPixels, ulong visibleDurationNs,
        EcgVerticalScale verticalScale, EcgPaperScale paperScale, int originXPixels, int originYPixels)
    {
        ArgumentNullException.ThrowIfNull(paperScale);
        _ = SweepPlotGeometry.MapSampleOffset(0, visibleDurationNs, plotLeftPixels, plotWidthPixels);
        _ = EcgVerticalGeometry.MapMicrovolts(verticalScale, 0, 1);
        if (paperScale.SpeedNumeratorMmPerSecond == 0 || paperScale.SpeedDenominator == 0 ||
            paperScale.GainNumeratorMmPerMillivolt == 0 || paperScale.GainDenominator == 0)
        { throw new EcgPaperGridException("PaperGrid.InvalidPaperScale", nameof(paperScale)); }
        BigInteger horizontalNumerator = (BigInteger)plotWidthPixels * 1_000_000_000 * paperScale.SpeedDenominator;
        BigInteger horizontalDenominator = (BigInteger)visibleDurationNs * paperScale.SpeedNumeratorMmPerSecond;
        BigInteger verticalNumerator = (BigInteger)verticalScale.PixelsPerMillivoltNumerator * paperScale.GainDenominator;
        BigInteger verticalDenominator = (BigInteger)verticalScale.PixelsPerMillivoltDenominator * paperScale.GainNumeratorMmPerMillivolt;
        if (horizontalNumerator * verticalDenominator != verticalNumerator * horizontalDenominator)
        { throw new EcgPaperGridException("PaperGrid.InconsistentAxisScale", nameof(paperScale)); }
        var divisor = BigInteger.GreatestCommonDivisor(horizontalNumerator, horizontalDenominator);
        horizontalNumerator /= divisor;
        horizontalDenominator /= divisor;
        if (horizontalNumerator > uint.MaxValue || horizontalDenominator > uint.MaxValue)
        { throw new EcgPaperGridException("PaperGrid.UnrepresentableSpacing", nameof(paperScale)); }
        return new(plotLeftPixels, verticalScale.PlotTopPixels, plotWidthPixels, verticalScale.PlotHeightPixels,
            originXPixels, originYPixels, (uint)horizontalNumerator, (uint)horizontalDenominator);
    }
}
