// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record SweepFrameDisplaySelection(string ReasonCode, ReconstructedSweepFrame? Frame);

// Consumes trusted in-process publication snapshots. This checks presentation
// compatibility, not source freshness, gain, authentication or history coverage.
public static class SweepFrameDisplayGate
{
    public static SweepFrameDisplaySelection Select(SweepStateProjectionState current,
        int leftPixels, int widthPixels, int topPixels, int heightPixels,
        PublishedSweepFrame? published)
    {
        SweepFramePathBuilder expected = SweepFramePathBuilder.Start(
            current, leftPixels, widthPixels, topPixels, heightPixels);
        if (published is null) { return new("FrameDisplay.Missing", null); }

        SweepFramePathState seed = published.Checkpoint.Frame;
        if (seed.PlotLeftPixels != leftPixels || seed.PlotWidthPixels != widthPixels ||
            seed.PlotTopPixels != topPixels || seed.PlotHeightPixels != heightPixels)
        {
            return new("FrameDisplay.ViewportMismatch", null);
        }

        SweepStateProjectionState previous = seed.Presentation;
        SweepStateProjectionSnapshot before = SweepStateProjectionStateMachine.Restore(previous).CaptureProjection();
        SweepStateProjectionSnapshot now = SweepStateProjectionStateMachine.Restore(current).CaptureProjection();
        // Coverage owns collections: compare the exact composed regions below,
        // rather than collection reference identity or rounded ppm phase alone.
        if (previous.Plan != current.Plan || DisplayClock(previous) != DisplayClock(current) ||
            (before with { NoDataCoverage = null }) != (now with { NoDataCoverage = null }) ||
            !expected.Geometry.Regions.SequenceEqual(published.Frame.Geometry.Regions))
        {
            return new("FrameDisplay.PresentationMismatch", null);
        }

        return new("FrameDisplay.Matched", published.Frame);
    }

    private static long DisplayClock(SweepStateProjectionState state) =>
        state.TemporalViewMode == TemporalViewMode.LiveSweep
            ? state.LiveSweepClockNs : state.PinnedSweepClockNs!.Value;
}
