// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record SweepClippedColumnEnvelope(SweepSampleSource Source, ulong CycleIndex, int RegionIndex,
    int ColumnPixels, ulong FirstEndSampleIndex, ulong LastEndSampleIndex,
    ClippedSweepPoint First, ClippedSweepPoint Last, ClippedSweepPoint MinimumY, ClippedSweepPoint MaximumY);

public sealed record ReducedSweepColumnFrame(ReconstructedSweepColumnFrame Frame,
    IReadOnlyList<SweepClippedColumnEnvelope> Envelopes);

// Geometry summaries retain exact clip intersections; they do not prescribe
// alpha coverage or connect separate envelopes into a polyline.
public static class SweepClippedColumnReduction
{
    public static ReducedSweepColumnFrame Reduce(SweepFrameReconstructionInput input,
        int maximumSamples, int maximumSegments, int maximumPieces, int maximumEnvelopes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumEnvelopes <= 0)
        { throw new SweepFrameReconstructionException("ClippedEnvelope.InvalidLimits", nameof(maximumEnvelopes)); }
        ReconstructedSweepColumnFrame frame = new SweepColumnFrameReconstructor(maximumSamples, maximumSegments, maximumPieces)
            .Replace(input, cancellationToken);
        List<SweepClippedColumnEnvelope> output = [];
        foreach (SweepFrameColumnPiece piece in frame.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClippedSweepSegment segment = piece.Piece.Segment;
            SweepClippedColumnEnvelope? previous = output.Count == 0 ? null : output[^1];
            bool joins = previous is not null && previous.Source == piece.Source && previous.CycleIndex == piece.CycleIndex &&
                previous.RegionIndex == piece.RegionIndex && previous.ColumnPixels == piece.Piece.ColumnPixels &&
                previous.LastEndSampleIndex < piece.EndSampleIndex && piece.EndSampleIndex - previous.LastEndSampleIndex == 1 &&
                previous.Last == segment.Start;
            if (joins)
            {
                output[^1] = previous! with
                {
                    LastEndSampleIndex = piece.EndSampleIndex,
                    Last = segment.End,
                    MinimumY = Min(previous!.MinimumY, segment.End),
                    MaximumY = Max(previous.MaximumY, segment.End),
                };
            }
            else
            {
                if (output.Count == maximumEnvelopes)
                { throw new SweepFrameReconstructionException("ClippedEnvelope.OutputLimitExceeded", nameof(maximumEnvelopes)); }
                output.Add(new(piece.Source, piece.CycleIndex, piece.RegionIndex, piece.Piece.ColumnPixels,
                    piece.EndSampleIndex, piece.EndSampleIndex, segment.Start, segment.End,
                    Min(segment.Start, segment.End), Max(segment.Start, segment.End)));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(frame, Array.AsReadOnly(output.ToArray()));
    }

    private static int Compare(ClippedSweepPoint a, ClippedSweepPoint b) =>
        (a.Y.Numerator * b.Y.Denominator).CompareTo(b.Y.Numerator * a.Y.Denominator);
    private static ClippedSweepPoint Min(ClippedSweepPoint a, ClippedSweepPoint b) => Compare(a, b) <= 0 ? a : b;
    private static ClippedSweepPoint Max(ClippedSweepPoint a, ClippedSweepPoint b) => Compare(a, b) >= 0 ? a : b;
}
