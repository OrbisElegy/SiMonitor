// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

// Logical pixels only: 125 px/s. Resizing changes coverage, never paper speed.
internal static class DemoSweepLayout
{
    internal const int ReferencePlotWidth = 1000;
    internal const long ReferenceDurationNs = 8_000_000_000;
    internal const long NanosecondsPerPixel = ReferenceDurationNs / ReferencePlotWidth;
    internal const int MaximumPlotWidth = 7500;
    internal const int RetainedBlockCount = 301; // 60s plus a predecessor block.

    internal static int PlotWidth(double width) => !double.IsFinite(width) || width < 1
        ? 0 : (int)Math.Min(Math.Floor(width), MaximumPlotWidth);
}
