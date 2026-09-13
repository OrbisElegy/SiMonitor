// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace Monitor.Domain.Presentation;

// Page-local logical coordinates; shell scrolling/origin translation is external.
// Rational output deliberately avoids pixel rounding and physical print claims.
public sealed class Ecg12ScreenTransform
{
    private Ecg12ScreenTransform(ExactPlotCoordinate factor, int width, int height)
    {
        Factor = factor;
        Width = Forward(new(width, 1));
        Height = Forward(new(height, 1));
    }

    public ExactPlotCoordinate Factor { get; }
    public ExactPlotCoordinate Width { get; }
    public ExactPlotCoordinate Height { get; }

    public static Ecg12ScreenTransform Resolve(Ecg12ZoomState selection, int pageWidth, int pageHeight,
        int availableWidth, int availableHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0 || availableWidth <= 0 || availableHeight <= 0)
        { throw new Ecg12ZoomSelectionException("Ecg12Zoom.InvalidGeometry", nameof(pageWidth)); }
        Ecg12ZoomState valid = new Ecg12ZoomSelection(selection, SystemViewCommandAssessmentPolicy.Enabled).Selection;
        ExactPlotCoordinate factor = valid.Mode switch
        {
            Ecg12ZoomMode.FitPage => (long)availableWidth * pageHeight <= (long)availableHeight * pageWidth
                ? Reduce(availableWidth, pageWidth) : Reduce(availableHeight, pageHeight),
            Ecg12ZoomMode.ActualSize => new(1, 1),
            _ => new(valid.Numerator, valid.Denominator),
        };
        return new(factor, pageWidth, pageHeight);
    }

    public ExactPlotCoordinate Forward(ExactPlotCoordinate coordinate)
    {
        Validate(coordinate);
        return Reduce(coordinate.Numerator * Factor.Numerator, coordinate.Denominator * Factor.Denominator);
    }

    public ExactPlotCoordinate Inverse(ExactPlotCoordinate coordinate)
    {
        Validate(coordinate);
        return Reduce(coordinate.Numerator * Factor.Denominator, coordinate.Denominator * Factor.Numerator);
    }

    private static void Validate(ExactPlotCoordinate coordinate)
    {
        if (coordinate is null || coordinate.Denominator <= 0)
        { throw new Ecg12ZoomSelectionException("Ecg12Zoom.InvalidCoordinate", nameof(coordinate)); }
    }

    private static ExactPlotCoordinate Reduce(BigInteger numerator, BigInteger denominator)
    {
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(numerator / divisor, denominator / divisor);
    }
}
