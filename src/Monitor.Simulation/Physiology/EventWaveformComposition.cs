// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public readonly record struct EventWaveformPhasePoint(long OffsetNs, int TableIndex);
public sealed record EventWaveformBand(PhysiologyCycleEventKind Trigger, long DelayNs,
    long DurationNs, IReadOnlyList<long> TableQ32,
    IReadOnlyList<EventWaveformPhasePoint>? PhasePoints = null);
public sealed record EventWaveformState(IReadOnlyList<EventWaveformBand> Bands,
    IReadOnlyList<PhysiologyCycleEvent> Events);
public sealed class EventWaveformException(string reason, string parameter) : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; } = reason;
}

// One immutable, bounded event window. Zero outside each band's support.
public sealed class EventWaveformComposition
{
    public const int MaximumBandCount = 32;
    public const int MaximumEventCount = 4096;
    private readonly EventWaveformBand[] _bands;
    private readonly long[][] _tables;
    private readonly PhysiologyCycleEvent[] _events;

    private EventWaveformComposition(EventWaveformState state)
    {
        if (state is null || state.Bands is null || state.Bands.Count is < 1 or > MaximumBandCount ||
            state.Events is null || state.Events.Count > MaximumEventCount)
        { throw Invalid(); }
        _bands = new EventWaveformBand[state.Bands.Count];
        _tables = new long[_bands.Length][];
        for (int index = 0; index < _bands.Length; index++)
        {
            EventWaveformBand band = state.Bands[index];
            if (band is null || !Enum.IsDefined(band.Trigger) || band.DelayNs < 0 || band.DurationNs <= 0 ||
                band.TableQ32 is null || band.TableQ32.Count is < 4 or > 65_536 ||
                !BitOperations.IsPow2((uint)band.TableQ32.Count)) { throw Invalid(); }
            long[] table = band.TableQ32.ToArray();
            if (table[0] != 0 || table.Any(value => value < short.MinValue * FixedPointMath.Q32One ||
                value > short.MaxValue * FixedPointMath.Q32One)) { throw Invalid(); }
            EventWaveformPhasePoint[]? points = null;
            if (band.PhasePoints is { } map)
            {
                if (map.Count is < 2 or > 32) { throw Invalid(); }
                points = map.ToArray();
                if (points[0] != new EventWaveformPhasePoint(0, 0) ||
                    points[^1] != new EventWaveformPhasePoint(band.DurationNs, table.Length)) { throw Invalid(); }
                for (int point = 1; point < points.Length; point++)
                {
                    if (points[point].OffsetNs <= points[point - 1].OffsetNs ||
                        points[point].TableIndex < points[point - 1].TableIndex ||
                        (point < points.Length - 1 && points[point].TableIndex >= table.Length))
                    { throw Invalid(); }
                }
            }
            _bands[index] = band with
            {
                TableQ32 = Array.AsReadOnly(table),
                PhasePoints = points is null ? null : Array.AsReadOnly(points)
            };
            _tables[index] = table;
        }
        _events = state.Events.ToArray();
        HashSet<(PhysiologyCycleEventKind, ulong)> identities = [];
        for (int index = 0; index < _events.Length; index++)
        {
            PhysiologyCycleEvent item = _events[index];
            if (item.SimTimeNs < 0 || !Enum.IsDefined(item.Kind) || !identities.Add((item.Kind, item.CycleIndex))) { throw Invalid(); }
            if (index > 0 && (_events[index - 1].SimTimeNs > item.SimTimeNs ||
                _events[index - 1].SimTimeNs == item.SimTimeNs && _events[index - 1].Kind >= item.Kind)) { throw Invalid(); }
            foreach (EventWaveformBand band in _bands.Where(band => band.Trigger == item.Kind))
            {
                if ((Int128)item.SimTimeNs + band.DelayNs + band.DurationNs > long.MaxValue) { throw Invalid(); }
            }
        }
    }

    public static EventWaveformComposition Restore(EventWaveformState state) => new(state);
    public EventWaveformState CaptureState() => new(Array.AsReadOnly(_bands), Array.AsReadOnly(_events));

    public long EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (simTimeNs < 0) { throw new EventWaveformException("EventWaveform.InvalidTime", nameof(simTimeNs)); }
        Int128 sum = 0;
        foreach (PhysiologyCycleEvent item in _events)
        {
            if (item.SimTimeNs > simTimeNs) { break; }
            for (int index = 0; index < _bands.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EventWaveformBand band = _bands[index];
                if (band.Trigger != item.Kind) { continue; }
                long elapsed = simTimeNs - item.SimTimeNs - band.DelayNs;
                if (elapsed < 0 || elapsed >= band.DurationNs) { continue; }
                // Equal adjacent table indices hold phase while time advances.
                // Integer phase maps the finite support into one frozen LUT cycle.
                ulong phase = PhaseAt(band, elapsed);
                sum += PeriodicLutLinear.Interpolate(_tables[index], phase).Value;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (sum < short.MinValue * FixedPointMath.Q32One || sum > short.MaxValue * FixedPointMath.Q32One)
        { throw new EventWaveformException("EventWaveform.AmplitudeOverflow", nameof(simTimeNs)); }
        return (long)sum;
    }

    private static EventWaveformException Invalid() => new("EventWaveform.InvalidState", "state");

    private static ulong PhaseAt(EventWaveformBand band, long elapsed)
    {
        if (band.PhasePoints is not { } points)
        { return (ulong)(((UInt128)elapsed << 64) / (ulong)band.DurationNs); }
        for (int index = 1; index < points.Count; index++)
        {
            var right = points[index];
            if (elapsed >= right.OffsetNs) { continue; }
            var left = points[index - 1];
            UInt128 start = ((UInt128)(uint)left.TableIndex << 64) / (uint)band.TableQ32.Count;
            UInt128 end = ((UInt128)(uint)right.TableIndex << 64) / (uint)band.TableQ32.Count;
            return (ulong)(start + (end - start) * (ulong)(elapsed - left.OffsetNs) /
                (ulong)(right.OffsetNs - left.OffsetNs));
        }
        throw Invalid(); // Caller admits only the band's half-open support.
    }
}
