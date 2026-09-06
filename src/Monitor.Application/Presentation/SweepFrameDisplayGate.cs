// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record SweepFrameDisplaySelection(string ReasonCode, ReconstructedSweepFrame? Frame);

// Consumes trusted in-process publication snapshots. This checks presentation
// compatibility and declared scale, not source freshness, authentication or
// proof that caller-mapped samples actually used the declared scale.
public static class SweepFrameDisplayGate
{
    public static SweepFrameDisplaySelection Select(SweepStateProjectionState current,
        int leftPixels, int widthPixels, int topPixels, int heightPixels,
        PublishedSweepFrame? published, EcgVerticalScale? verticalScale = null) =>
        SelectFrame(current, leftPixels, widthPixels, topPixels, heightPixels,
            published?.Frame, published?.Checkpoint, verticalScale);

    internal static SweepFrameDisplaySelection SelectFrame(SweepStateProjectionState current,
        int leftPixels, int widthPixels, int topPixels, int heightPixels,
        ReconstructedSweepFrame? frame, SweepFrameReconstructionInput? checkpoint, EcgVerticalScale? verticalScale)
    {
        var expected = SweepFramePathBuilder.Start(
            current, leftPixels, widthPixels, topPixels, heightPixels, verticalScale);
        if (frame is null || checkpoint is null) { return new("FrameDisplay.Missing", null); }

        SweepFramePathState seed = checkpoint.Frame;
        if (seed.PlotLeftPixels != leftPixels || seed.PlotWidthPixels != widthPixels ||
            seed.PlotTopPixels != topPixels || seed.PlotHeightPixels != heightPixels)
        {
            return new("FrameDisplay.ViewportMismatch", null);
        }

        if (!SameScale(seed.VerticalScale, verticalScale))
        {
            return new("FrameDisplay.ScaleMismatch", null);
        }

        SweepStateProjectionState previous = seed.Presentation;
        SweepStateProjectionSnapshot before = SweepStateProjectionStateMachine.Restore(previous).CaptureProjection();
        SweepStateProjectionSnapshot now = SweepStateProjectionStateMachine.Restore(current).CaptureProjection();
        // Coverage owns collections: compare the exact composed regions below,
        // rather than collection reference identity or rounded ppm phase alone.
        if (previous.Plan != current.Plan || DisplayClock(previous) != DisplayClock(current) ||
            (before with { NoDataCoverage = null }) != (now with { NoDataCoverage = null }) ||
            !expected.Geometry.Regions.SequenceEqual(frame.Geometry.Regions))
        {
            return new("FrameDisplay.PresentationMismatch", null);
        }

        return new("FrameDisplay.Matched", frame);
    }

    private static long DisplayClock(SweepStateProjectionState state) =>
        state.TemporalViewMode == TemporalViewMode.LiveSweep
            ? state.LiveSweepClockNs : state.PinnedSweepClockNs!.Value;

    private static bool SameScale(EcgVerticalScale? before, EcgVerticalScale? now)
    {
        if (before is null || now is null) { return before is null && now is null; }
        return before.PlotTopPixels == now.PlotTopPixels && before.PlotHeightPixels == now.PlotHeightPixels &&
            before.ZeroBaselinePixels == now.ZeroBaselinePixels &&
            (ulong)before.PixelsPerMillivoltNumerator * now.PixelsPerMillivoltDenominator ==
            (ulong)now.PixelsPerMillivoltNumerator * before.PixelsPerMillivoltDenominator;
    }
}
