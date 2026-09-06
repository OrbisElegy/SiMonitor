// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class SweepFrameResizeException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record SweepFrameResizeResult(
    ReconstructedSweepFrame Frame, SweepFrameReconstructionInput Checkpoint);

// A geometry-only resize of one fixed frame. Does not advance presentation,
// change scale/epochs or infer missing history or source-to-cycle mapping.
public static class SweepFrameHorizontalResize
{
    public static SweepFrameResizeResult Rebuild(SweepFrameReconstructionInput checkpoint,
        int leftPixels, int widthPixels, int maximumSamples, int maximumSegments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SweepFrameReconstructor original = new(maximumSamples, maximumSegments);
        // Validate and own the old evidence before replacing any coordinates.
        original.Replace(checkpoint, cancellationToken);
        SweepFrameReconstructionInput owned = original.CaptureCheckpoint()!;
        SweepFramePathState target = owned.Frame with { PlotLeftPixels = leftPixels, PlotWidthPixels = widthPixels };
        _ = SweepFramePathBuilder.Restore(target);
        var samples = new SweepPathSample[owned.Samples.Count];
        for (int index = 0; index < samples.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SweepPathSample sample = owned.Samples[index];
            if (sample.CycleOffsetNs is not { } offset)
            {
                throw new SweepFrameResizeException("FrameResize.OffsetEvidenceRequired", nameof(checkpoint));
            }
            SweepPixelPosition x = SweepPlotGeometry.MapSampleOffset(offset,
                target.Presentation.Plan.VisibleDurationNs, leftPixels, widthPixels);
            samples[index] = sample with { Point = sample.Point with { X = x } };
        }
        SweepFrameReconstructor resized = new(maximumSamples, maximumSegments);
        ReconstructedSweepFrame frame = resized.Replace(new(target, Array.AsReadOnly(samples)), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(frame, resized.CaptureCheckpoint()!);
    }
}
