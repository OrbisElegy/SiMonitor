// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace Monitor.Domain.Presentation;

public sealed class SweepSegmentClipException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record SweepSamplePoint(SweepPixelPosition X, EcgVerticalPosition Y);
public sealed record ExactPlotCoordinate(BigInteger Numerator, BigInteger Denominator);
public sealed record ClippedSweepPoint(ExactPlotCoordinate X, ExactPlotCoordinate Y);
public sealed record ClippedSweepSegment(ClippedSweepPoint Start, ClippedSweepPoint End);

public static class SweepSegmentClipper
{
    // Clips one already-authorized source segment. It does not infer adjacency,
    // quality, channel identity or epoch continuity from geometric coordinates.
    public static ClippedSweepSegment? Clip(
        SweepPlotRegion region, int plotTopPixels, int plotHeightPixels,
        SweepSamplePoint start, SweepSamplePoint end)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        long bottom = (long)plotTopPixels + plotHeightPixels;
        if (plotTopPixels < 0 || plotHeightPixels <= 0 || bottom > int.MaxValue || !Enum.IsDefined(region.Kind))
        {
            throw Error("SweepClip.InvalidRegion", nameof(region));
        }
        Rational left = X(region.StartX);
        Rational right = X(region.EndExclusiveX);
        Rational x0 = X(start.X);
        Rational x1 = X(end.X);
        Rational y0 = Y(start.Y);
        Rational y1 = Y(end.Y);
        if (left.CompareTo(right) >= 0)
        {
            throw Error("SweepClip.InvalidRegion", nameof(region));
        }
        if (x1.CompareTo(x0) < 0)
        {
            throw Error("SweepClip.ReversedSegment", nameof(end));
        }
        if (region.Kind is SweepTraceRegionKind.BackgroundEraseGap or SweepTraceRegionKind.NoDataBaseline)
        {
            return null;
        }

        Rational enter = new(0, 1);
        Rational leave = new(1, 1);
        if (!Axis(x0, x1, left, right) ||
            !Axis(y0, y1, new(plotTopPixels, 1), new(bottom, 1)))
        {
            return null;
        }
        return new ClippedSweepSegment(Point(enter), Point(leave));

        bool Axis(Rational a, Rational b, Rational min, Rational max)
        {
            Rational delta = b - a;
            if (delta.Numerator.IsZero)
            {
                return a.CompareTo(min) >= 0 && a.CompareTo(max) <= 0;
            }
            Rational first = (min - a) / delta;
            Rational last = (max - a) / delta;
            if (first.CompareTo(last) > 0) { (first, last) = (last, first); }
            if (first.CompareTo(enter) > 0) { enter = first; }
            if (last.CompareTo(leave) < 0) { leave = last; }
            return enter.CompareTo(leave) <= 0;
        }

        ClippedSweepPoint Point(Rational t) => new(
            (x0 + (x1 - x0) * t).Export(), (y0 + (y1 - y0) * t).Export());
    }

    private static Rational X(SweepPixelPosition value)
    {
        if (value is null || value.WholePixels < 0 || value.FractionDenominator == 0 ||
            value.FractionNumerator >= value.FractionDenominator ||
            (value.WholePixels == int.MaxValue && value.FractionNumerator != 0))
        {
            throw Error("SweepClip.InvalidCoordinate", nameof(value));
        }
        return new Rational((BigInteger)value.WholePixels * value.FractionDenominator + value.FractionNumerator,
            value.FractionDenominator);
    }

    private static Rational Y(EcgVerticalPosition value)
    {
        if (value is null || value.PixelDenominator <= 0 || !Enum.IsDefined(value.Relation))
        {
            throw Error("SweepClip.InvalidCoordinate", nameof(value));
        }
        return new Rational((BigInteger)value.PixelNumerator, (BigInteger)value.PixelDenominator);
    }

    private static SweepSegmentClipException Error(string reasonCode, string parameterName) => new(reasonCode, parameterName);

    // Intersections of bounded rational input coordinates can exceed Int128.
    // Exact BigInteger arithmetic is local presentation geometry, not simulation.
    private readonly record struct Rational
    {
        public Rational(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            Numerator = numerator / divisor;
            Denominator = denominator / divisor;
        }
        public BigInteger Numerator { get; }
        public BigInteger Denominator { get; }
        public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
        public ExactPlotCoordinate Export() => new(Numerator, Denominator);
        public static Rational operator +(Rational a, Rational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator -(Rational a, Rational b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator *(Rational a, Rational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
        public static Rational operator /(Rational a, Rational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }
}
