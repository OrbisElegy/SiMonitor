// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepSegmentClipperSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ClippingKeepsExactIntersectionsInsteadOfPlateaus), ClippingKeepsExactIntersectionsInsteadOfPlateaus),
        new(nameof(ClippingHandlesParallelPointsAndBoundaryContacts), ClippingHandlesParallelPointsAndBoundaryContacts),
        new(nameof(ClippingSuppressesNonSourceRegionsAndWrapConnections), ClippingSuppressesNonSourceRegionsAndWrapConnections),
        new(nameof(ClippingReconstructsExtremeRationalsAndRejectsInvalidInputs), ClippingReconstructsExtremeRationalsAndRejectsInvalidInputs),
    ];

    private static SweepPlotRegion Region(SweepTraceRegionKind kind = SweepTraceRegionKind.RetainSourceTrace) =>
        new(new(0, 0, 1), new(10, 0, 1), kind);
    private static SweepSamplePoint Point(int x, Int128 y, Int128 denominator = default) =>
        new(new(x, 0, 1), new(y, denominator == 0 ? 1 : denominator, VerticalPlotRelation.WithinPlot));
    private static ClippedSweepPoint Exact(int xn, int xd, int yn, int yd) => new(new(xn, xd), new(yn, yd));

    private static void ClippingKeepsExactIntersectionsInsteadOfPlateaus()
    {
        ClippedSweepSegment? result = SweepSegmentClipper.Clip(Region(), 0, 10, Point(0, -10), Point(10, 20));
        Check.That(result == new ClippedSweepSegment(Exact(10, 3, 0, 1), Exact(20, 3, 10, 1)),
            "crossing points produce exact segment intersections, not clamped original endpoints");
        SweepPlotRegion narrow = new(new(2, 1, 2), new(7, 1, 2), SweepTraceRegionKind.RetainSourceTrace);
        Check.That(SweepSegmentClipper.Clip(narrow, 0, 10, Point(0, 0), Point(10, 10)) ==
            new ClippedSweepSegment(Exact(5, 2, 5, 2), Exact(15, 2, 15, 2)),
            "fractional horizontal clipping retains exact line geometry");
    }

    private static void ClippingHandlesParallelPointsAndBoundaryContacts()
    {
        Check.That(SweepSegmentClipper.Clip(Region(), 0, 10, Point(0, -1), Point(10, -1)) is null &&
            SweepSegmentClipper.Clip(Region(), 0, 10, Point(11, 0), Point(11, 10)) is null,
            "parallel outside segments return no geometry");
        Check.That(SweepSegmentClipper.Clip(Region(), 0, 10, Point(5, 5), Point(5, 5)) ==
            new ClippedSweepSegment(Exact(5, 1, 5, 1), Exact(5, 1, 5, 1)) &&
            SweepSegmentClipper.Clip(Region(), 0, 10, Point(5, 20), Point(5, -10)) ==
            new ClippedSweepSegment(Exact(5, 1, 10, 1), Exact(5, 1, 0, 1)),
            "degenerate and vertical segments preserve their direction");
        Check.That(SweepSegmentClipper.Clip(Region(), 0, 10, Point(0, -10), Point(10, 0)) ==
            new ClippedSweepSegment(Exact(10, 1, 0, 1), Exact(10, 1, 0, 1)),
            "corner contact is retained geometrically without deciding raster coverage");
    }

    private static void ClippingSuppressesNonSourceRegionsAndWrapConnections()
    {
        foreach (SweepTraceRegionKind kind in new[] { SweepTraceRegionKind.BackgroundEraseGap, SweepTraceRegionKind.NoDataBaseline })
        {
            Check.That(SweepSegmentClipper.Clip(Region(kind), 0, 10, Point(0, 5), Point(10, 5)) is null,
                "source traces cannot be drawn through background or NoData regions");
        }
        Check.That(SweepSegmentClipper.Clip(Region(SweepTraceRegionKind.PinnedHistory), 0, 10, Point(0, 5), Point(10, 5)) is not null,
            "pinned source segments remain drawable");
        Check.That(Reason(() => SweepSegmentClipper.Clip(Region(), 0, 10, Point(9, 5), Point(1, 5))) ==
            "SweepClip.ReversedSegment", "a right-to-left wrap cannot become a diagonal connection");
    }

    private static void ClippingReconstructsExtremeRationalsAndRejectsInvalidInputs()
    {
        SweepSamplePoint start = Point(0, Int128.MinValue, Int128.MaxValue);
        SweepSamplePoint end = Point(10, Int128.MaxValue, 1);
        ClippedSweepSegment? first = SweepSegmentClipper.Clip(Region(), 0, 10, start, end);
        Check.That(first is not null && first.Start.Y == new ExactPlotCoordinate(0, 1) &&
            first.End.Y == new ExactPlotCoordinate(10, 1) &&
            first == SweepSegmentClipper.Clip(Region() with { }, 0, 10, start with { }, end with { }),
            "bounded extreme input fractions reconstruct canonical intersections beyond Int128 products");
        Check.That(Reason(() => SweepSegmentClipper.Clip(Region(), 0, 0, start, end)) == "SweepClip.InvalidRegion" &&
            Reason(() => SweepSegmentClipper.Clip(Region(), 0, 10, start with { X = new(0, 1, 1) }, end)) == "SweepClip.InvalidCoordinate" &&
            Reason(() => SweepSegmentClipper.Clip(Region(), 0, 10, start with { Y = new(1, 0, VerticalPlotRelation.WithinPlot) }, end)) == "SweepClip.InvalidCoordinate" &&
            first == SweepSegmentClipper.Clip(Region(), 0, 10, start, end),
            "invalid geometry fails without changing immutable inputs or accepted output");
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (SweepSegmentClipException exception) { return exception.ReasonCode; }
    }
}
