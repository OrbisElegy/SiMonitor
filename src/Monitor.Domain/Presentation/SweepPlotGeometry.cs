// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public sealed class SweepPlotGeometryException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// Exact horizontal pixel coordinate, deliberately not a raster rounding policy.
public sealed record SweepPixelPosition(
    int WholePixels,
    ulong FractionNumerator,
    ulong FractionDenominator);

public sealed record SweepPlotRegion(
    SweepPixelPosition StartX,
    SweepPixelPosition EndExclusiveX,
    SweepTraceRegionKind Kind);

public sealed record SweepPlotGeometrySnapshot(
    string GroupId,
    ulong SweepEpoch,
    ulong PresentationClockRevision,
    ulong VisibleDurationNs,
    int PlotLeftPixels,
    int PlotWidthPixels,
    IReadOnlyList<SweepPlotRegion> Regions);

public static class SweepPlotGeometry
{
    // The caller supplies the resolved SweepPlotArea, excluding calibration
    // gutters and chrome. Resizing maps the same time partition to new geometry.
    public static SweepPlotGeometrySnapshot Compose(
        SweepStateProjectionState state,
        int plotLeftPixels,
        int plotWidthPixels)
    {
        if (plotLeftPixels < 0 || plotWidthPixels <= 0 ||
            (long)plotLeftPixels + plotWidthPixels > int.MaxValue)
        {
            throw new SweepPlotGeometryException(
                "SweepGeometry.InvalidPlotBounds", nameof(plotWidthPixels));
        }

        SweepTraceCompositionSnapshot trace = SweepTraceComposition.Compose(state);
        SweepPlotRegion[] regions = trace.Regions.Select(region => new SweepPlotRegion(
            Map(region.StartOffsetNs, trace.VisibleDurationNs, plotLeftPixels, plotWidthPixels),
            Map(region.EndExclusiveOffsetNs, trace.VisibleDurationNs, plotLeftPixels, plotWidthPixels),
            region.Kind)).ToArray();
        return new SweepPlotGeometrySnapshot(trace.GroupId, trace.SweepEpoch,
            trace.PresentationClockRevision, trace.VisibleDurationNs,
            plotLeftPixels, plotWidthPixels, Array.AsReadOnly(regions));
    }

    private static SweepPixelPosition Map(ulong offsetNs, ulong durationNs, int left, int width)
    {
        UInt128 scaled = (UInt128)offsetNs * checked((uint)width);
        int whole = checked(left + (int)(scaled / durationNs));
        ulong remainder = (ulong)(scaled % durationNs);
        return new SweepPixelPosition(whole, remainder, durationNs);
    }
}
