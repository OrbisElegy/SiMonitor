// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class SweepFrameReconstructionException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record SweepFrameReconstructionInput(
    SweepFramePathState Frame, IReadOnlyList<SweepPathSample> Samples);

public sealed record SweepFrameSegment(
    SweepSampleSource Source, ulong EndSampleIndex, ulong CycleIndex,
    int RegionIndex, ClippedSweepSegment Segment);

public sealed record ReconstructedSweepFrame(
    SweepPlotGeometrySnapshot Geometry, int PlotTopPixels, int PlotHeightPixels,
    IReadOnlyList<SweepFrameSegment> Segments);

public sealed class SweepFrameReconstructor
{
    private readonly int _maximumSamples;
    private readonly int _maximumSegments;
    private SweepFrameReconstructionInput? _checkpoint;

    // Limits are resolved local resource policy, not new frozen profile values.
    public SweepFrameReconstructor(int maximumSamples, int maximumSegments)
    {
        if (maximumSamples <= 0 || maximumSegments <= 0)
        {
            throw Error("FrameReconstruction.InvalidLimits", nameof(maximumSamples));
        }
        _maximumSamples = maximumSamples;
        _maximumSegments = maximumSegments;
    }

    public ReconstructedSweepFrame? Current { get; private set; }

    public ReconstructedSweepFrame Replace(SweepFrameReconstructionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Frame is null || input.Frame.Previous is not null || input.Samples is null)
        {
            throw Error("FrameReconstruction.InvalidInput", nameof(input));
        }
        if (input.Samples.Count > _maximumSamples)
        {
            throw Error("FrameReconstruction.SampleLimitExceeded", nameof(input));
        }
        SweepPathSample[] samples = input.Samples.ToArray();
        SweepFramePathBuilder trial = SweepFramePathBuilder.Restore(input.Frame);
        SweepFramePathState initial = trial.CaptureState();
        List<SweepFrameSegment> segments = [];
        foreach (SweepPathSample sample in samples)
        {
            foreach (SweepRegionPathResult result in trial.Append(sample))
            {
                if (result.Path.Segment is not null)
                {
                    if (segments.Count == _maximumSegments)
                    {
                        throw Error("FrameReconstruction.SegmentLimitExceeded", nameof(input));
                    }
                    segments.Add(new(sample.Source, sample.SampleIndex, sample.CycleIndex,
                        result.RegionIndex, result.Path.Segment));
                }
            }
        }
        ReconstructedSweepFrame completed = new(trial.Geometry, initial.PlotTopPixels, initial.PlotHeightPixels,
            Array.AsReadOnly(segments.ToArray()));
        _checkpoint = new(initial, Array.AsReadOnly(samples));
        Current = completed;
        return completed;
    }

    public SweepFrameReconstructionInput? CaptureCheckpoint() => _checkpoint;

    public static SweepFrameReconstructor Restore(
        int maximumSamples, int maximumSegments, SweepFrameReconstructionInput checkpoint)
    {
        SweepFrameReconstructor restored = new(maximumSamples, maximumSegments);
        try { restored.Replace(checkpoint); }
        catch (ArgumentException)
        {
            throw Error("FrameReconstruction.InvalidCheckpoint", nameof(checkpoint));
        }
        return restored;
    }

    private static SweepFrameReconstructionException Error(string reasonCode, string parameterName) => new(reasonCode, parameterName);
}
