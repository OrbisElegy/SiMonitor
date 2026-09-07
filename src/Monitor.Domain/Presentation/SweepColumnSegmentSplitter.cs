// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace Monitor.Domain.Presentation;

public sealed record SweepColumnSegment(int ColumnPixels, ClippedSweepSegment Segment);

// Subdivides an already region/plot-clipped segment. Shared geometric endpoints
// do not define stroke coverage or alpha blending for neighboring raster columns.
public static class SweepColumnSegmentSplitter
{
    public static IReadOnlyList<SweepColumnSegment> Split(ClippedSweepSegment segment,
        int maximumPieces, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(segment);
        if (maximumPieces <= 0 || segment.Start is null || segment.End is null)
        { throw Error("ColumnSegment.InvalidInput", nameof(segment)); }
        Fraction x0 = Read(segment.Start.X), x1 = Read(segment.End.X);
        Fraction y0 = Read(segment.Start.Y), y1 = Read(segment.End.Y);
        if (x0.N < 0 || x0.CompareTo(x1) > 0 || x1.CompareTo(new(int.MaxValue, 1)) > 0)
        { throw Error("ColumnSegment.InvalidBounds", nameof(segment)); }
        bool vertical = x0.CompareTo(x1) == 0;
        BigInteger first = x0.N / x0.D;
        BigInteger endExclusive = vertical ? first + 1 : (x1.N + x1.D - 1) / x1.D;
        if (first >= int.MaxValue) { throw Error("ColumnSegment.InvalidBounds", nameof(segment)); }
        BigInteger count = endExclusive - first;
        if (count > maximumPieces) { throw Error("ColumnSegment.OutputLimitExceeded", nameof(maximumPieces)); }
        var result = new SweepColumnSegment[(int)count];
        for (int index = 0; index < result.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int column = (int)first + index;
            Fraction left = Max(x0, new(column, 1));
            Fraction right = Min(x1, new((long)column + 1, 1));
            ClippedSweepPoint start = new(left.Export(), vertical ? y0.Export() : At(left).Export());
            ClippedSweepPoint end = new(right.Export(), vertical ? y1.Export() : At(right).Export());
            result[index] = new(column, new(start, end));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(result);

        Fraction At(Fraction x) => y0 + (y1 - y0) * ((x - x0) / (x1 - x0));
    }

    private static Fraction Read(ExactPlotCoordinate value)
    {
        if (value is null || value.Denominator <= 0) { throw Error("ColumnSegment.InvalidCoordinate", nameof(value)); }
        return new(value.Numerator, value.Denominator);
    }
    private static Fraction Min(Fraction a, Fraction b) => a.CompareTo(b) <= 0 ? a : b;
    private static Fraction Max(Fraction a, Fraction b) => a.CompareTo(b) >= 0 ? a : b;
    private static SweepSegmentClipException Error(string reason, string parameterName) => new(reason, parameterName);

    private readonly record struct Fraction(BigInteger N, BigInteger D)
    {
        public int CompareTo(Fraction other) => (N * other.D).CompareTo(other.N * D);
        public ExactPlotCoordinate Export()
        {
            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(N), D);
            return new(N / divisor, D / divisor);
        }
        public static Fraction operator +(Fraction a, Fraction b) => new(a.N * b.D + b.N * a.D, a.D * b.D);
        public static Fraction operator -(Fraction a, Fraction b) => new(a.N * b.D - b.N * a.D, a.D * b.D);
        public static Fraction operator *(Fraction a, Fraction b) => new(a.N * b.N, a.D * b.D);
        public static Fraction operator /(Fraction a, Fraction b) => new(a.N * b.D, a.D * b.N);
    }
}
