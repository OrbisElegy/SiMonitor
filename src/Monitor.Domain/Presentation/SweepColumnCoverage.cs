// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

// Every column in this half-open run has the same exact horizontal coverage.
// Several regions can contribute to one edge column; no blend/color is implied.
public sealed record SweepColumnCoverageRun(
    int StartColumn,
    int EndExclusiveColumn,
    SweepTraceRegionKind Kind,
    ulong CoverageNumerator,
    ulong CoverageDenominator);

public sealed record SweepColumnCoverageSnapshot(
    SweepPlotGeometrySnapshot Geometry,
    IReadOnlyList<SweepColumnCoverageRun> Runs);

public static class SweepColumnCoverage
{
    public static SweepColumnCoverageSnapshot Compose(
        SweepStateProjectionState state,
        int plotLeftPixels,
        int plotWidthPixels)
    {
        SweepPlotGeometrySnapshot geometry = SweepPlotGeometry.Compose(state, plotLeftPixels, plotWidthPixels);
        List<SweepColumnCoverageRun> runs = [];
        foreach (SweepPlotRegion region in geometry.Regions)
        {
            SweepPixelPosition start = region.StartX;
            SweepPixelPosition end = region.EndExclusiveX;
            ulong denominator = geometry.VisibleDurationNs;
            if (start.WholePixels == end.WholePixels)
            {
                Add(start.WholePixels, checked(start.WholePixels + 1),
                    end.FractionNumerator - start.FractionNumerator);
                continue;
            }

            int fullStart = start.WholePixels;
            if (start.FractionNumerator != 0)
            {
                fullStart = checked(fullStart + 1);
                Add(start.WholePixels, fullStart, denominator - start.FractionNumerator);
            }

            Add(fullStart, end.WholePixels, denominator);
            if (end.FractionNumerator != 0)
            {
                Add(end.WholePixels, checked(end.WholePixels + 1), end.FractionNumerator);
            }

            void Add(int first, int endExclusive, ulong numerator)
            {
                if (first < endExclusive && numerator > 0)
                {
                    runs.Add(new SweepColumnCoverageRun(first, endExclusive, region.Kind, numerator, denominator));
                }
            }
        }

        return new SweepColumnCoverageSnapshot(geometry, Array.AsReadOnly(runs.ToArray()));
    }
}
