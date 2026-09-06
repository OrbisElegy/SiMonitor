// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public sealed class SweepPathException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record SweepSampleSource(
    Guid SessionId, Guid InstanceId, Guid ChannelId,
    ulong TimebaseEpoch, ulong StreamEpoch, ulong ConfigurationRevision,
    ulong SweepEpoch, ulong PresentationClockRevision,
    uint SampleRateNumerator, uint SampleRateDenominator);

public sealed record EcgSampleVoltage(long NumeratorMicrovolts, uint Denominator);

public sealed record SweepPathSample(
    SweepSampleSource Source, ulong SampleIndex, ulong CycleIndex,
    bool Drawable, SweepSamplePoint Point, EcgSampleVoltage? Voltage = null);

public sealed record SweepPathState(
    SweepPlotRegion Region, int PlotTopPixels, int PlotHeightPixels,
    SweepPathSample? Previous);

public sealed record SweepPathAppendResult(string ReasonCode, ClippedSweepSegment? Segment);

// One resolved source region. The caller owns unit/quality and identity evidence;
// this boundary never interprets opaque waveform quality bits or authenticates it.
public sealed class SweepPathBuilder
{
    private SweepPathState _state;

    private SweepPathBuilder(SweepPathState state) => _state = state;

    public static SweepPathBuilder Start(SweepPlotRegion region, int topPixels, int heightPixels) =>
        Restore(new SweepPathState(region, topPixels, heightPixels, null));

    public static SweepPathBuilder Restore(SweepPathState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            SweepSamplePoint probe = new(new(0, 0, 1), new(0, 1, VerticalPlotRelation.WithinPlot));
            _ = SweepSegmentClipper.Clip(state.Region, state.PlotTopPixels, state.PlotHeightPixels, probe, probe);
            if (state.Previous is not null) { Validate(state, state.Previous); }
            return new SweepPathBuilder(state);
        }
        catch (ArgumentException)
        {
            throw new SweepPathException("SweepPath.InvalidCheckpoint", nameof(state));
        }
    }

    public SweepPathAppendResult Append(SweepPathSample sample)
    {
        Validate(_state, sample);
        SweepPathSample? previous = _state.Previous;
        ClippedSweepSegment? segment = null;
        string reason;
        if (previous is null) { reason = "SweepPath.FirstPoint"; }
        else if (previous.Source != sample.Source) { reason = "SweepPath.SourceChanged"; }
        else
        {
            if (sample.SampleIndex <= previous.SampleIndex || sample.CycleIndex < previous.CycleIndex)
            {
                throw new SweepPathException("SweepPath.FrontierReversed", nameof(sample));
            }
            if (sample.SampleIndex - previous.SampleIndex != 1) { reason = "SweepPath.SampleGap"; }
            else if (sample.CycleIndex != previous.CycleIndex) { reason = "SweepPath.CycleChanged"; }
            else if (!previous.Drawable || !sample.Drawable) { reason = "SweepPath.NotDrawable"; }
            else
            {
                segment = SweepSegmentClipper.Clip(_state.Region, _state.PlotTopPixels, _state.PlotHeightPixels,
                    previous.Point, sample.Point);
                reason = segment is null ? "SweepPath.OutsideSourceRegion" : "SweepPath.Segment";
            }
        }
        _state = _state with { Previous = sample };
        return new SweepPathAppendResult(reason, segment);
    }

    public SweepPathState CaptureState() => _state;

    private static void Validate(SweepPathState state, SweepPathSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        SweepSampleSource source = sample.Source;
        if (source is null || source.SessionId == Guid.Empty || source.InstanceId == Guid.Empty ||
            source.ChannelId == Guid.Empty || source.SampleRateNumerator == 0 || source.SampleRateDenominator == 0)
        {
            throw new SweepPathException("SweepPath.InvalidSource", nameof(sample));
        }
        // Validate even suppressed points before accepting them as a frontier.
        _ = SweepSegmentClipper.Clip(state.Region, state.PlotTopPixels, state.PlotHeightPixels, sample.Point, sample.Point);
    }
}
