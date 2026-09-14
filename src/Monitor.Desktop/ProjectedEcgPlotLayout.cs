// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// Logical display coordinates. No monitor-DPI or physical-mm claim.
internal sealed record ProjectedEcgPlotLayout(int PlotLeft, int PlotWidth)
{
    internal const long VisibleDurationNs = 8_000_000_000;
    internal const int RowHeight = 160;
    internal const uint PixelsPerMillivolt = 40;
    private static readonly SweepStateProjectionState ScaleState = SweepStateProjectionStateMachine.Start(
        new("electrode-demo", 1, 1, 0, VisibleDurationNs, 200_000_000, VisibleDurationNs + 200_000_000), 1, 1,
        SessionRunState.Running, DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(),
        0, 0).CaptureState();

    internal static ProjectedEcgPlotLayout? Resolve(double width)
    {
        if (!double.IsFinite(width) || width < 100 || width > int.MaxValue) { return null; }
        int available = (int)Math.Floor(width);
        int plot = (int)((available - 60L) * 40 / 41);
        return new(available - plot, plot);
    }

    internal static EcgVerticalScale VerticalScale(int lead) =>
        new(lead * RowHeight, RowHeight, lead * RowHeight + RowHeight / 2, PixelsPerMillivolt, 1);

    internal EcgCalibrationGeometrySnapshot Calibration(int lead) => EcgCalibrationGeometry.Compose(
        ScaleState, PlotLeft, PlotWidth, VerticalScale(lead), 44, 48);
}
