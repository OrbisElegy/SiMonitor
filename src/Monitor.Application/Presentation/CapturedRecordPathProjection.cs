// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Application.Presentation;

public sealed class CapturedRecordPathException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record RecordWaveformPoint(ExactPlotCoordinate X, EcgVerticalPosition Y);
public sealed record RecordWaveformSegment(ulong StartSampleIndex, ulong EndSampleIndex,
    ulong StartBlockSequence, ulong EndBlockSequence, RecordWaveformPoint Start, RecordWaveformPoint End);
public sealed record CapturedRecordPathPageDisplay(CapturedRecordQualityPageDisplay Content,
    IReadOnlyList<RecordWaveformSegment> Segments);

// Internal only: source continuity is proven by the fresh archive-backed capture.
// These are unclipped source segments, not native drawing commands.
internal static class CapturedRecordPathProjection
{
    internal static CapturedRecordPathPageDisplay Build(CapturedRecordQualityPageDisplay content,
        int maximumSegments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!content.Content.Content.Content.Content.Study.Admission.MayEnter)
        { return new(content, Array.Empty<RecordWaveformSegment>()); }
        if (maximumSegments <= 0)
        { throw new CapturedRecordPathException("RecordPath.InvalidSegmentLimit", nameof(maximumSegments)); }
        List<RecordWaveformSegment> segments = [];
        RecordWaveformPoint? previous = null;
        ulong previousIndex = 0;
        ulong previousBlock = 0;
        foreach (CapturedRecordQualityBlock block in content.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchivedWaveformPlaneBlock source = block.Source.Source.Source;
            for (int index = 0; index < block.Samples.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong sampleIndex = checked(source.Plane.FirstSampleIndex + (uint)index);
                if (!block.Samples[index].Drawable)
                {
                    previous = null;
                    continue;
                }
                RecordWaveformPoint point = new(block.Source.Source.SampleX[index], block.Source.SampleY[index]);
                if (previous is not null && sampleIndex > previousIndex && sampleIndex - previousIndex == 1)
                {
                    if (segments.Count == maximumSegments)
                    { throw new CapturedRecordPathException("RecordPath.SegmentLimitExceeded", nameof(maximumSegments)); }
                    segments.Add(new(previousIndex, sampleIndex, previousBlock, source.BlockSequence, previous, point));
                }
                previous = point;
                previousIndex = sampleIndex;
                previousBlock = source.BlockSequence;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(content, segments.AsReadOnly());
    }
}
