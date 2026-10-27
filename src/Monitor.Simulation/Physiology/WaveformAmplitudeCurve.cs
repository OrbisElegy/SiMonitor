// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Source counts, before sensor effects, wire quantization, and measurements.
// Gas gain multiplies an existing expiration; zero gas remains zero gas.
public sealed record WaveformAmplitudeCurve(VitalValueCurve Values, int ReferenceCentiMmHg,
    bool ShiftBaseline, int BaselineCentiMmHg = 0)
{
    internal void Validate(string profileId)
    {
        ArgumentNullException.ThrowIfNull(Values);
        if (ShiftBaseline ? profileId != "AcqPressure125@1" || ReferenceCentiMmHg is < -500 or > 3000 ||
                Values.MinimumValue < -500 || Values.MaximumValue > 3000 :
            profileId != "AcqCO2_100@1" || ReferenceCentiMmHg is <= 0 or > 32700 ||
                BaselineCentiMmHg is < 0 or > 8000 || Values.MinimumValue < BaselineCentiMmHg || Values.MaximumValue > 8000)
        { throw new ArgumentException("Physiology.InvalidAmplitudeCurve"); }
    }

    internal long Apply(long valueQ32, long sourceTimeNs) => ShiftBaseline
        ? checked(valueQ32 + (long)(Values.At(sourceTimeNs) - ReferenceCentiMmHg) * FixedPointMath.Q32One)
        : checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)valueQ32 * (Values.At(sourceTimeNs) - BaselineCentiMmHg), ReferenceCentiMmHg));
}

public sealed record WaveformAmplitudeCurveSegment(long FromSimTimeNs, WaveformAmplitudeCurve? Curve);

internal static class WaveformAmplitudeCurves
{
    internal static void Validate(IReadOnlyList<WaveformAmplitudeCurveSegment> segments, string profileId,
        long epochAnchorSimTimeNs, long cursorSimTimeNs)
    {
        if (segments is null || segments.Count > 512) { throw new ArgumentException("Physiology.InvalidAmplitudeCurves"); }
        long previous = -1;
        foreach (var segment in segments)
        {
            if (segment is null || segment.FromSimTimeNs < epochAnchorSimTimeNs ||
                segment.FromSimTimeNs > cursorSimTimeNs || segment.FromSimTimeNs <= previous)
            { throw new ArgumentException("Physiology.InvalidAmplitudeCurves"); }
            segment.Curve?.Validate(profileId);
            previous = segment.FromSimTimeNs;
        }
    }

    internal static WaveformAmplitudeCurve? At(IReadOnlyList<WaveformAmplitudeCurveSegment> segments, long timeNs) =>
        segments.LastOrDefault(segment => segment.FromSimTimeNs <= timeNs)?.Curve;

    internal static IReadOnlyList<WaveformAmplitudeCurveSegment> ContinueWith(
        IReadOnlyList<WaveformAmplitudeCurveSegment> history, WaveformAmplitudeCurve? next,
        long boundaryNs, long keepFromNs)
    {
        if (At(history, boundaryNs) == next) { return history; }
        var retained = history.Where((segment, index) => segment.FromSimTimeNs < boundaryNs &&
            (index + 1 == history.Count || history[index + 1].FromSimTimeNs > keepFromNs)).ToList();
        retained.Add(new(boundaryNs, next));
        return retained.AsReadOnly();
    }
}
