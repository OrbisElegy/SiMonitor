// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// Logical display coordinates. No monitor-DPI or physical-mm claim.
internal sealed record ProjectedEcgPlotLayout(int PlotLeft, int PlotWidth)
{
    internal const long VisibleDurationNs = DemoSweepLayout.ReferenceDurationNs;
    internal const int RowHeight = 320;
    internal const uint PixelsPerMillivolt = 40;
    private static readonly SweepStateProjectionState ScaleState = SweepStateProjectionStateMachine.Start(
        new("electrode-demo", 1, 1, 0, VisibleDurationNs, 200_000_000, VisibleDurationNs + 200_000_000), 1, 1,
        SessionRunState.Running, DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(),
        0, 0).CaptureState();

    internal static ProjectedEcgPlotLayout? Resolve(double width)
    {
        if (!double.IsFinite(width) || width < 100 || width > int.MaxValue) { return null; }
        int available = (int)Math.Floor(width);
        return new(85, Math.Min(available - 85, DemoSweepLayout.MaximumPlotWidth));
    }

    internal static EcgVerticalScale VerticalScale(int lead) =>
        new(lead * RowHeight, RowHeight, lead * RowHeight + RowHeight / 2, PixelsPerMillivolt, 1);

    internal EcgCalibrationGeometrySnapshot Calibration(int lead) => EcgCalibrationGeometry.Compose(
        ScaleState, PlotLeft, DemoSweepLayout.ReferencePlotWidth, VerticalScale(lead), 44, 48);
}
