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
    SweepPathSample? Previous, EcgVerticalScale? VerticalScale = null);

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
        int leftPixels, int widthPixels, int topPixels, int heightPixels,
        EcgVerticalScale? verticalScale = null) =>
        Restore(new(presentation, leftPixels, widthPixels, topPixels, heightPixels, null, verticalScale));

    public static SweepFramePathBuilder Restore(SweepFramePathState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            SweepStateProjectionState presentation = SweepStateProjectionStateMachine.Restore(state.Presentation).CaptureState();
            SweepPlotGeometrySnapshot geometry = SweepPlotGeometry.Compose(presentation, state.PlotLeftPixels, state.PlotWidthPixels);
            if (state.VerticalScale is { } scale)
            {
                _ = EcgVerticalGeometry.MapMicrovolts(scale, 0, 1);
                if (scale.PlotTopPixels != state.PlotTopPixels || scale.PlotHeightPixels != state.PlotHeightPixels)
                {
                    throw new ArgumentException("SweepFrame.ScaleBoundsMismatch", nameof(state));
                }
            }
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

    public IReadOnlyList<SweepRegionPathResult> Append(SweepPathSample sample, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(sample);
        var results = new SweepRegionPathResult[_geometry.Regions.Count];
        for (int index = 0; index < results.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trial = SweepPathBuilder.Restore(new(
                _geometry.Regions[index], _state.PlotTopPixels, _state.PlotHeightPixels, _state.Previous));
            results[index] = new(index, trial.Append(sample));
        }
        // One shared frontier commits only after every region succeeds.
        cancellationToken.ThrowIfCancellationRequested();
        _state = _state with { Previous = sample };
        return Array.AsReadOnly(results);
    }

    public IReadOnlyList<SweepRegionPathResult> AppendVoltage(SweepSampleSource source,
        ulong sampleIndex, ulong cycleIndex, bool drawable, SweepPixelPosition x,
        EcgSampleVoltage voltage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(voltage);
        EcgVerticalScale scale = _state.VerticalScale ??
            throw new SweepFramePathException("SweepFrame.VoltageScaleRequired", nameof(voltage));
        EcgVerticalPosition y = EcgVerticalGeometry.MapMicrovolts(scale, voltage.NumeratorMicrovolts, voltage.Denominator);
        return Append(new(source, sampleIndex, cycleIndex, drawable, new(x, y), voltage), cancellationToken);
    }

    public SweepFramePathState CaptureState() => _state;

    public IReadOnlyList<SweepRegionPathResult> AppendVoltageAtOffset(SweepSampleSource source,
        ulong sampleIndex, ulong cycleIndex, bool drawable, ulong cycleOffsetNs,
        EcgSampleVoltage voltage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(voltage);
        EcgVerticalScale scale = _state.VerticalScale ??
            throw new SweepFramePathException("SweepFrame.VoltageScaleRequired", nameof(voltage));
        SweepPixelPosition x = SweepPlotGeometry.MapSampleOffset(cycleOffsetNs,
            _geometry.VisibleDurationNs, _geometry.PlotLeftPixels, _geometry.PlotWidthPixels);
        EcgVerticalPosition y = EcgVerticalGeometry.MapMicrovolts(scale, voltage.NumeratorMicrovolts, voltage.Denominator);
        return Append(new(source, sampleIndex, cycleIndex, drawable, new(x, y), voltage, cycleOffsetNs), cancellationToken);
    }

    public SweepPlotGeometrySnapshot Geometry => _geometry;

    private void ValidateIdentity(SweepPathSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.Source is null || sample.Source.SweepEpoch != _geometry.SweepEpoch ||
            sample.Source.PresentationClockRevision != _geometry.PresentationClockRevision)
        {
            throw new SweepFramePathException("SweepFrame.PresentationIdentityMismatch", nameof(sample));
        }
        if (sample.CycleOffsetNs is { } offset)
        {
            SweepPixelPosition expected;
            try
            {
                expected = SweepPlotGeometry.MapSampleOffset(offset,
                    _geometry.VisibleDurationNs, _geometry.PlotLeftPixels, _geometry.PlotWidthPixels);
            }
            catch (SweepPlotGeometryException)
            {
                throw new SweepFramePathException("SweepFrame.InvalidSampleOffset", nameof(sample));
            }
            if (sample.Point is null || sample.Point.X != expected)
            {
                throw new SweepFramePathException("SweepFrame.TimeMappingMismatch", nameof(sample));
            }
        }
        if ((_state.VerticalScale is null) != (sample.Voltage is null))
        {
            throw new SweepFramePathException("SweepFrame.VoltageEvidenceRequired", nameof(sample));
        }
        if (_state.VerticalScale is { } scale && sample.Voltage is { } voltage)
        {
            EcgVerticalPosition expected;
            try
            {
                expected = EcgVerticalGeometry.MapMicrovolts(scale, voltage.NumeratorMicrovolts, voltage.Denominator);
            }
            catch (EcgVerticalGeometryException)
            {
                throw new SweepFramePathException("SweepFrame.InvalidVoltageAmplitude", nameof(sample));
            }
            if (sample.Point is null || sample.Point.Y != expected)
            {
                throw new SweepFramePathException("SweepFrame.VoltageMappingMismatch", nameof(sample));
            }
        }
    }
}
