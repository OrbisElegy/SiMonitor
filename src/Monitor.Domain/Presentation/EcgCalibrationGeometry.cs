// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public sealed class EcgCalibrationGeometryException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record EcgCalibrationPoint(SweepPixelPosition X, EcgVerticalPosition Y);

public sealed record EcgCalibrationGeometrySnapshot(
    string GroupId, ulong SweepEpoch, ulong PresentationClockRevision,
    int GutterLeftPixels, int GutterRightPixels,
    IReadOnlyList<EcgCalibrationPoint> Points);

// A persistent scale glyph, never patient samples or evidence of signal validity.
// Coordinates describe a centerline; stroke padding and raster ownership are
// resolved by the layout/renderer, not by silently shrinking the calibration.
public static class EcgCalibrationGeometry
{
    public const ulong PulseDurationNs = 200_000_000;

    public static EcgCalibrationGeometrySnapshot Compose(SweepStateProjectionState state,
        int plotLeftPixels, int plotWidthPixels, EcgVerticalScale verticalScale,
        int gutterLeftPixels, int pulseLeftPixels)
    {
        SweepPlotGeometrySnapshot plot = SweepPlotGeometry.Compose(state, plotLeftPixels, plotWidthPixels);
        EcgVerticalPosition baseline = EcgVerticalGeometry.MapMicrovolts(verticalScale, 0, 1);
        EcgVerticalPosition peak = EcgVerticalGeometry.MapMicrovolts(verticalScale, 1000, 1);
        if (gutterLeftPixels < 0 || gutterLeftPixels >= plotLeftPixels ||
            pulseLeftPixels < gutterLeftPixels || pulseLeftPixels >= plotLeftPixels)
        {
            throw new EcgCalibrationGeometryException("EcgCalibration.InvalidGutter", nameof(gutterLeftPixels));
        }

        UInt128 widthNumerator = (UInt128)(uint)plotWidthPixels * PulseDurationNs;
        ulong denominator = plot.VisibleDurationNs;
        if (widthNumerator > (UInt128)(uint)(plotLeftPixels - pulseLeftPixels) * denominator ||
            peak.Relation != VerticalPlotRelation.WithinPlot)
        {
            throw new EcgCalibrationGeometryException("EcgCalibration.InsufficientSpace", nameof(verticalScale));
        }

        int endWhole = checked(pulseLeftPixels + (int)(widthNumerator / denominator));
        ulong remainder = (ulong)(widthNumerator % denominator);
        SweepPixelPosition start = new(pulseLeftPixels, 0, denominator);
        SweepPixelPosition end = new(endWhole, remainder, denominator);
        EcgCalibrationPoint[] points = [new(start, baseline), new(start, peak), new(end, peak), new(end, baseline)];
        return new(plot.GroupId, plot.SweepEpoch, plot.PresentationClockRevision,
            gutterLeftPixels, plotLeftPixels, Array.AsReadOnly(points));
    }
}
