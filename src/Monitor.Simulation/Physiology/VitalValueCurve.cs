// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed record VitalValueChange(long StartSimTimeNs, long DurationNs, int TargetValue);

// Immutable source-time targets. A later event starts from the value reached
// by the preceding event, including when it interrupts an unfinished ramp.
public sealed class VitalValueCurve
{
    private sealed record Segment(long StartNs, long DurationNs, int From, int To, Int128 Integral);
    private readonly Segment[] _segments;
    public int InitialValue { get; }
    public IReadOnlyList<VitalValueChange> Changes { get; }
    public int MinimumValue { get; }
    public int MaximumValue { get; }

    public VitalValueCurve(int initialValue, IReadOnlyList<VitalValueChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count > 1024 || changes.Any(change => change is null || change.StartSimTimeNs < 0 ||
            change.DurationNs < 0 || change.StartSimTimeNs > long.MaxValue - change.DurationNs))
        { throw new ArgumentException("VitalCurve.InvalidChanges", nameof(changes)); }
        var owned = changes.OrderBy(change => change.StartSimTimeNs).ToArray();
        InitialValue = initialValue;
        Changes = Array.AsReadOnly(owned);
        MinimumValue = owned.Select(change => change.TargetValue).Append(initialValue).Min();
        MaximumValue = owned.Select(change => change.TargetValue).Append(initialValue).Max();
        var segments = new List<Segment> { new(0, 0, initialValue, initialValue, 0) };
        foreach (var change in owned)
        {
            var previous = segments[^1];
            long elapsed = change.StartSimTimeNs - previous.StartNs;
            int from = Value(previous, elapsed);
            Int128 integral = previous.Integral + Area(previous, elapsed);
            segments.Add(new(change.StartSimTimeNs, change.DurationNs, from, change.TargetValue, integral));
        }
        _segments = segments.ToArray();
    }

    public int At(long sourceTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceTimeNs);
        var segment = SegmentAt(sourceTimeNs);
        return Value(segment, sourceTimeNs - segment.StartNs);
    }

    internal Int128 IntegralAt(long sourceTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceTimeNs);
        var segment = SegmentAt(sourceTimeNs);
        return segment.Integral + Area(segment, sourceTimeNs - segment.StartNs);
    }

    private Segment SegmentAt(long sourceTimeNs)
    {
        int low = 0, high = _segments.Length;
        while (low + 1 < high)
        {
            int middle = low + (high - low) / 2;
            if (_segments[middle].StartNs <= sourceTimeNs) { low = middle; }
            else { high = middle; }
        }
        return _segments[low];
    }

    private static int Value(Segment segment, long elapsedNs) => segment.DurationNs == 0 || elapsedNs >= segment.DurationNs
        ? segment.To : checked((int)(segment.From + FixedPointMath.RoundDivideTiesToEven(
            ((Int128)segment.To - segment.From) * elapsedNs, segment.DurationNs)));

    internal Int128 InitialInfluenceAt(long sourceTimeNs)
    {
        if (Changes.Count == 0) { return sourceTimeNs; }
        var first = Changes[0];
        if (sourceTimeNs <= first.StartSimTimeNs) { return sourceTimeNs; }
        long elapsed = Math.Min(sourceTimeNs - first.StartSimTimeNs, first.DurationNs);
        if (Changes.Count > 1) { elapsed = Math.Min(elapsed, Changes[1].StartSimTimeNs - first.StartSimTimeNs); }
        return first.StartSimTimeNs + (first.DurationNs == 0 ? 0 : elapsed -
            (Int128)elapsed * elapsed / (2 * (Int128)first.DurationNs));
    }

    private static Int128 Area(Segment segment, long elapsedNs)
    {
        if (segment.DurationNs == 0) { return (Int128)segment.To * elapsedNs; }
        long rampNs = Math.Min(elapsedNs, segment.DurationNs);
        return (Int128)segment.From * rampNs +
            (Int128)(((BigInteger)segment.To - segment.From) * rampNs * rampNs / (2 * (BigInteger)segment.DurationNs)) +
            (Int128)segment.To * (elapsedNs - rampNs);
    }
}
