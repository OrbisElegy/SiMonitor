// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class EcgStripException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record EcgStripCheckpoint(
    SweepFrameReconstructionInput Source, int GutterLeftPixels, int PulseLeftPixels);

public sealed record ReconstructedEcgStrip(
    ReconstructedSweepFrame PatientFrame, EcgCalibrationGeometrySnapshot Calibration,
    EcgStripCheckpoint Checkpoint, ReducedSweepColumnFrame? ColumnReduction = null);

public sealed record EcgColumnReductionLimits(int MaximumPieces, int MaximumEnvelopes);

// Serialized local composition. The UI must swap the whole result; this is not
// a concurrent publication service or authority scale-change scheduler.
public sealed class EcgStripReconstructor
{
    private readonly int _maximumSamples;
    private readonly int _maximumSegments;
    private readonly EcgColumnReductionLimits? _columnLimits;

    public EcgStripReconstructor(int maximumSamples, int maximumSegments, EcgColumnReductionLimits? columnLimits = null)
    {
        _ = new SweepFrameReconstructor(maximumSamples, maximumSegments);
        if (columnLimits is not null && (columnLimits.MaximumPieces <= 0 || columnLimits.MaximumEnvelopes <= 0))
        { throw new EcgStripException("EcgStrip.InvalidColumnLimits", nameof(columnLimits)); }
        _columnLimits = columnLimits;
        _maximumSamples = maximumSamples;
        _maximumSegments = maximumSegments;
    }

    public ReconstructedEcgStrip? Current { get; private set; }

    public ReconstructedEcgStrip Replace(EcgStripCheckpoint input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        SweepFrameReconstructor trial = new(_maximumSamples, _maximumSegments);
        ReconstructedSweepFrame frame = trial.Replace(input.Source, cancellationToken);
        return Publish(frame, trial.CaptureCheckpoint()!, input.GutterLeftPixels, input.PulseLeftPixels, cancellationToken);
    }

    public ReconstructedEcgStrip ResizeHorizontal(int plotLeftPixels, int plotWidthPixels,
        int gutterLeftPixels, int pulseLeftPixels, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReconstructedEcgStrip current = Current ?? throw new EcgStripException("EcgStrip.NoFrame", nameof(plotWidthPixels));
        SweepFrameResizeResult trial = SweepFrameHorizontalResize.Rebuild(current.Checkpoint.Source,
            plotLeftPixels, plotWidthPixels, _maximumSamples, _maximumSegments, cancellationToken);
        return Publish(trial.Frame, trial.Checkpoint, gutterLeftPixels, pulseLeftPixels, cancellationToken);
    }

    public EcgStripCheckpoint? CaptureCheckpoint() => Current?.Checkpoint;

    public static EcgStripReconstructor Restore(int maximumSamples, int maximumSegments, EcgStripCheckpoint checkpoint,
        EcgColumnReductionLimits? columnLimits = null)
    {
        EcgStripReconstructor result = new(maximumSamples, maximumSegments, columnLimits);
        try { result.Replace(checkpoint); }
        catch (ArgumentException) { throw new EcgStripException("EcgStrip.InvalidCheckpoint", nameof(checkpoint)); }
        return result;
    }

    private ReconstructedEcgStrip Publish(ReconstructedSweepFrame frame, SweepFrameReconstructionInput source,
        int gutterLeftPixels, int pulseLeftPixels, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReducedSweepColumnFrame? reduction = null;
        if (_columnLimits is { } limits)
        {
            reduction = SweepClippedColumnReduction.Reduce(source, _maximumSamples, _maximumSegments,
                limits.MaximumPieces, limits.MaximumEnvelopes, cancellationToken);
            frame = reduction.Frame.SourceFrame;
            source = reduction.Frame.Checkpoint;
        }
        SweepFramePathState seed = source.Frame;
        EcgVerticalScale scale = seed.VerticalScale ??
            throw new EcgStripException("EcgStrip.VoltageScaleRequired", nameof(source));
        EcgCalibrationGeometrySnapshot calibration = EcgCalibrationGeometry.Compose(seed.Presentation,
            seed.PlotLeftPixels, seed.PlotWidthPixels, scale, gutterLeftPixels, pulseLeftPixels);
        ReconstructedEcgStrip completed = new(frame, calibration, new(source, gutterLeftPixels, pulseLeftPixels), reduction);
        cancellationToken.ThrowIfCancellationRequested();
        Current = completed;
        return completed;
    }
}
