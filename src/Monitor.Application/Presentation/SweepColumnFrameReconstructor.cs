// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record SweepFrameColumnPiece(SweepSampleSource Source, ulong EndSampleIndex,
    ulong CycleIndex, int RegionIndex, SweepColumnSegment Piece);

public sealed record ReconstructedSweepColumnFrame(ReconstructedSweepFrame SourceFrame,
    IReadOnlyList<SweepFrameColumnPiece> Pieces, SweepFrameReconstructionInput Checkpoint);

// Serialized complete geometry publication, not raster coverage or UI dispatch.
public sealed class SweepColumnFrameReconstructor
{
    private readonly int _maximumSamples;
    private readonly int _maximumSegments;
    private readonly int _maximumPieces;

    public SweepColumnFrameReconstructor(int maximumSamples, int maximumSegments, int maximumPieces)
    {
        _ = new SweepFrameReconstructor(maximumSamples, maximumSegments);
        if (maximumPieces <= 0)
        { throw new SweepFrameReconstructionException("ColumnFrame.InvalidLimits", nameof(maximumPieces)); }
        _maximumSamples = maximumSamples;
        _maximumSegments = maximumSegments;
        _maximumPieces = maximumPieces;
    }

    public ReconstructedSweepColumnFrame? Current { get; private set; }

    public ReconstructedSweepColumnFrame Replace(SweepFrameReconstructionInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SweepFrameReconstructor trial = new(_maximumSamples, _maximumSegments);
        ReconstructedSweepFrame frame = trial.Replace(input, cancellationToken);
        ReconstructedSweepColumnFrame completed = SubdivideValidated(frame, trial.CaptureCheckpoint()!, _maximumPieces, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Current = completed;
        return completed;
    }

    // Internal only: callers must pass the matching frame/checkpoint produced by
    // reconstruction, never caller-created geometry or deserialized output.
    internal static ReconstructedSweepColumnFrame SubdivideValidated(ReconstructedSweepFrame frame,
        SweepFrameReconstructionInput input, int maximumPieces, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumPieces <= 0)
        { throw new SweepFrameReconstructionException("ColumnFrame.InvalidLimits", nameof(maximumPieces)); }
        List<SweepFrameColumnPiece> pieces = [];
        foreach (SweepFrameSegment segment in frame.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int remaining = maximumPieces - pieces.Count;
            if (remaining == 0)
            { throw new SweepFrameReconstructionException("ColumnFrame.OutputLimitExceeded", nameof(input)); }
            IReadOnlyList<SweepColumnSegment> split;
            try { split = SweepColumnSegmentSplitter.Split(segment.Segment, remaining, cancellationToken); }
            catch (SweepSegmentClipException exception) when (exception.ReasonCode == "ColumnSegment.OutputLimitExceeded")
            { throw new SweepFrameReconstructionException("ColumnFrame.OutputLimitExceeded", nameof(input)); }
            foreach (SweepColumnSegment piece in split)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pieces.Add(new(segment.Source, segment.EndSampleIndex, segment.CycleIndex, segment.RegionIndex, piece));
            }
        }
        ReconstructedSweepColumnFrame completed = new(frame, Array.AsReadOnly(pieces.ToArray()), input);
        cancellationToken.ThrowIfCancellationRequested();
        return completed;
    }

    public SweepFrameReconstructionInput? CaptureCheckpoint() => Current?.Checkpoint;

    public static SweepColumnFrameReconstructor Restore(int maximumSamples, int maximumSegments, int maximumPieces,
        SweepFrameReconstructionInput checkpoint)
    {
        SweepColumnFrameReconstructor result = new(maximumSamples, maximumSegments, maximumPieces);
        try { result.Replace(checkpoint); }
        catch (ArgumentException)
        { throw new SweepFrameReconstructionException("ColumnFrame.InvalidCheckpoint", nameof(checkpoint)); }
        return result;
    }
}
