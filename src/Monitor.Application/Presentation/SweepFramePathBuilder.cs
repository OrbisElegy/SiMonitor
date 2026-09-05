// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class SweepFramePathException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record SweepFramePathState(
    SweepStateProjectionState Presentation,
    int PlotLeftPixels, int PlotWidthPixels, int PlotTopPixels, int PlotHeightPixels,
    SweepPathSample? Previous);

public sealed record SweepRegionPathResult(int RegionIndex, SweepPathAppendResult Path);

// Reconstructs source paths for one fixed presentation frame. A later phase or
// viewport starts a new reconstruction rather than reusing old pixel coordinates.
public sealed class SweepFramePathBuilder
{
    private SweepFramePathState _state;
    private readonly SweepPlotGeometrySnapshot _geometry;

    private SweepFramePathBuilder(SweepFramePathState state, SweepPlotGeometrySnapshot geometry)
    {
        _state = state;
        _geometry = geometry;
    }

    public static SweepFramePathBuilder Start(SweepStateProjectionState presentation,
        int leftPixels, int widthPixels, int topPixels, int heightPixels) =>
        Restore(new(presentation, leftPixels, widthPixels, topPixels, heightPixels, null));

    public static SweepFramePathBuilder Restore(SweepFramePathState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            SweepStateProjectionState presentation = SweepStateProjectionStateMachine.Restore(state.Presentation).CaptureState();
            SweepPlotGeometrySnapshot geometry = SweepPlotGeometry.Compose(presentation, state.PlotLeftPixels, state.PlotWidthPixels);
            foreach (SweepPlotRegion region in geometry.Regions)
            {
                _ = SweepPathBuilder.Restore(new(region, state.PlotTopPixels, state.PlotHeightPixels, state.Previous));
            }
            SweepFramePathBuilder result = new(state with { Presentation = presentation }, geometry);
            if (state.Previous is not null) { result.ValidateIdentity(state.Previous); }
            return result;
        }
        catch (ArgumentException)
        {
            throw new SweepFramePathException("SweepFrame.InvalidCheckpoint", nameof(state));
        }
    }

    public IReadOnlyList<SweepRegionPathResult> Append(SweepPathSample sample)
    {
        ValidateIdentity(sample);
        SweepRegionPathResult[] results = new SweepRegionPathResult[_geometry.Regions.Count];
        for (int index = 0; index < results.Length; index++)
        {
            SweepPathBuilder trial = SweepPathBuilder.Restore(new(
                _geometry.Regions[index], _state.PlotTopPixels, _state.PlotHeightPixels, _state.Previous));
            results[index] = new(index, trial.Append(sample));
        }
        // One shared frontier commits only after every region succeeds.
        _state = _state with { Previous = sample };
        return Array.AsReadOnly(results);
    }

    public SweepFramePathState CaptureState() => _state;

    public SweepPlotGeometrySnapshot Geometry => _geometry;

    private void ValidateIdentity(SweepPathSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.Source is null || sample.Source.SweepEpoch != _geometry.SweepEpoch ||
            sample.Source.PresentationClockRevision != _geometry.PresentationClockRevision)
        {
            throw new SweepFramePathException("SweepFrame.PresentationIdentityMismatch", nameof(sample));
        }
    }
}
