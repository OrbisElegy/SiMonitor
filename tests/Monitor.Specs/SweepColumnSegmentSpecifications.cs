// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepColumnSegmentSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ColumnSegmentsRetainExactBoundaryIntersections), ColumnSegmentsRetainExactBoundaryIntersections),
        new(nameof(ColumnSegmentsPreserveVerticalAndPointGeometry), ColumnSegmentsPreserveVerticalAndPointGeometry),
        new(nameof(ColumnSegmentsKeepRegionClippingBeforeSubdivision), ColumnSegmentsKeepRegionClippingBeforeSubdivision),
        new(nameof(ColumnSegmentsRejectBoundsAndCapacityWithoutPartialOutput), ColumnSegmentsRejectBoundsAndCapacityWithoutPartialOutput),
    ];

    private static ClippedSweepSegment Segment(int x0, int y0, int x1, int y1) =>
        new(new(new(x0, 1), new(y0, 1)), new(new(x1, 1), new(y1, 1)));

    private static void ColumnSegmentsRetainExactBoundaryIntersections()
    {
        IReadOnlyList<SweepColumnSegment> pieces = SweepColumnSegmentSplitter.Split(
            new(new(new(1, 2), new(0, 1)), new(new(5, 2), new(3, 1))), 3);
        Check.That(pieces.Count == 3 && pieces[0].ColumnPixels == 0 && pieces[2].ColumnPixels == 2 &&
            pieces[0].Segment.End.Y == new ExactPlotCoordinate(3, 4) &&
            pieces[1].Segment.End.Y == new ExactPlotCoordinate(9, 4) &&
            pieces[0].Segment.End == pieces[1].Segment.Start && pieces[1].Segment.End == pieces[2].Segment.Start,
            "subdivision preserves fractional endpoints and exact shared column intersections");
        Check.That(SweepColumnSegmentSplitter.Split(Segment(0, 3, 2, 0), 2).Count == 2,
            "an integer right endpoint does not create an extra empty column");
    }

    private static void ColumnSegmentsPreserveVerticalAndPointGeometry()
    {
        ClippedSweepSegment vertical = Segment(2, 1, 2, 99);
        Check.That(SweepColumnSegmentSplitter.Split(vertical, 1).Single() == new SweepColumnSegment(2, vertical),
            "vertical narrow peak remains in its exact integer column without division by zero");
        ClippedSweepSegment point = Segment(2, 5, 2, 5);
        Check.That(SweepColumnSegmentSplitter.Split(point, 1).Single().Segment == point,
            "degenerate point geometry is retained for renderer policy");
    }

    private static void ColumnSegmentsKeepRegionClippingBeforeSubdivision()
    {
        SweepPlotRegion region = new(new(1, 1, 4), new(2, 3, 4), SweepTraceRegionKind.RetainSourceTrace);
        ClippedSweepSegment clipped = SweepSegmentClipper.Clip(region, 0, 10,
            new(new(0, 0, 1), new(0, 1, VerticalPlotRelation.WithinPlot)),
            new(new(4, 0, 1), new(8, 1, VerticalPlotRelation.WithinPlot)))!;
        IReadOnlyList<SweepColumnSegment> pieces = SweepColumnSegmentSplitter.Split(clipped, 2);
        Check.That(pieces[0].Segment.Start == clipped.Start && pieces[^1].Segment.End == clipped.End &&
            pieces[0].Segment.Start.X == new ExactPlotCoordinate(5, 4) &&
            pieces[^1].Segment.End.X == new ExactPlotCoordinate(11, 4),
            "a crossing segment survives even when both source endpoints lie outside the retained region");
        Check.That(pieces.SequenceEqual(SweepColumnSegmentSplitter.Split(clipped, 2)),
            "repeated reconstruction from exact clipped geometry is deterministic");
    }

    private static void ColumnSegmentsRejectBoundsAndCapacityWithoutPartialOutput()
    {
        IReadOnlyList<SweepColumnSegment> accepted = SweepColumnSegmentSplitter.Split(Segment(0, 0, 1, 1), 1);
        foreach ((ClippedSweepSegment segment, int capacity, string reason) in new[]
        {
            (Segment(0, 0, int.MaxValue, 1), 2, "ColumnSegment.OutputLimitExceeded"),
            (Segment(2, 0, 1, 1), 2, "ColumnSegment.InvalidBounds"),
            (Segment(-1, 0, 1, 1), 2, "ColumnSegment.InvalidBounds"),
            (Segment(0, 0, 1, 1), 0, "ColumnSegment.InvalidInput"),
        })
        {
            try { _ = SweepColumnSegmentSplitter.Split(segment, capacity); throw new InvalidOperationException("invalid subdivision accepted"); }
            catch (SweepSegmentClipException exception) { Check.That(exception.ReasonCode == reason, "invalid input rejects with stable reason"); }
        }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { _ = SweepColumnSegmentSplitter.Split(Segment(0, 0, 1, 1), 1, cancellation.Token); throw new InvalidOperationException("cancelled subdivision accepted"); }
        catch (OperationCanceledException exception) { Check.That(exception.CancellationToken == cancellation.Token, "cancellation retains token identity"); }
        Check.That(accepted.Single().Segment == Segment(0, 0, 1, 1), "later rejection never changes accepted geometry");
    }
}
