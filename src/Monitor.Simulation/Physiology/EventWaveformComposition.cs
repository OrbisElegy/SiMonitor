// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum AtrialFibrillationBeatSelection { Ordinary, LongShort }

public readonly record struct EventWaveformPhasePoint(long OffsetNs, int TableIndex);
public sealed record RespiratoryCycleShape(long DelayNs, long DurationNs, IReadOnlyList<long> TableQ32,
    IReadOnlyList<EventWaveformPhasePoint>? PhasePoints = null);
// Local morphology selection by original ventricular beat ordinal; not a wire event.
public readonly record struct VentricularCyclePattern(int Length, ulong IncludedSlots);
// TriggerCycleLimit is exclusive in the trigger's cycle-index domain. It gates
// new contributions; support already triggered before the limit still finishes.
// Optional TriggerCycleResume reopens contributions at that original cycle index.
public sealed record EventWaveformBand(PhysiologyCycleEventKind Trigger, long DelayNs,
    long DurationNs, IReadOnlyList<long> TableQ32,
    IReadOnlyList<EventWaveformPhasePoint>? PhasePoints = null, ulong? TriggerCycleLimit = null, ulong? TriggerCycleResume = null, RespiratoryPattern DepthPattern = RespiratoryPattern.Regular, IReadOnlyList<int>? ExpirationCycleGainsPermille = null, VentricularCyclePattern? VentricularCycles = null, AvConductionPattern? EjectionIllustration = null, AtrialFibrillationBeatSelection? AfBeatSelection = null)
{
    // Continuous flutter cycles use the exact next atrial interval, including
    // seeded variation. DurationNs remains the conservative lookback bound.
    public RegularPhysiologyPlan? AfTiming { get; init; }
    public SeededCardiacRate? CycleDurationRate { get; init; }
    public IReadOnlyList<int>? InspirationCycleGainsPermille { get; init; }
    // Indexed complete respiratory contributions; the outer delay/duration
    // bound all slots so continuation and indexed lookback remain conservative.
    public IReadOnlyList<RespiratoryCycleShape>? RespiratoryCycleShapes { get; init; }
    // Global breath gains retain their original ordinal. Only this finite
    // activation envelope restarts when the source definition changes.
    public ulong ExpirationCycleOrigin { get; init; }
    public IReadOnlyList<int>? ExpirationActivationGainsPermille { get; init; }
    // Small gas-motion attenuation follows actual ventricular mechanical events.
    // It never creates expired gas when the respiratory contribution is zero.
    public long CardiogenicRippleDurationNs { get; init; }
    public int CardiogenicRipplePermille { get; init; }
}
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
    private readonly long[][]?[] _cycleTables;
    private readonly PhysiologyCycleEvent[] _events;

    private EventWaveformComposition(EventWaveformState state)
    {
        if (state is null || state.Bands is null || state.Bands.Count is < 1 or > MaximumBandCount ||
            state.Events is null || state.Events.Count > MaximumEventCount)
        { throw Invalid(); }
        _bands = new EventWaveformBand[state.Bands.Count];
        _tables = new long[_bands.Length][];
        _cycleTables = new long[][]?[_bands.Length];
        for (int index = 0; index < _bands.Length; index++)
        {
            EventWaveformBand band = state.Bands[index];
            if (band is null || !Enum.IsDefined(band.DepthPattern) ||
                (band.DepthPattern != RespiratoryPattern.Regular && band.Trigger != PhysiologyCycleEventKind.InspirationStart) || !Enum.IsDefined(band.Trigger) || band.DelayNs < 0 || band.DurationNs <= 0 ||
                band.TriggerCycleLimit == 0 ||
                (band.TriggerCycleResume is { } resume && (band.TriggerCycleLimit is null || resume <= band.TriggerCycleLimit.Value)) ||
                band.TableQ32 is null || band.TableQ32.Count is < 4 or > 65_536 ||
                !BitOperations.IsPow2((uint)band.TableQ32.Count)) { throw Invalid(); }
            if (band.EjectionIllustration is { } perfusion &&
                (band.Trigger != PhysiologyCycleEventKind.VentricularMechanical || band.ExpirationCycleGainsPermille is not null || !PrematureBeatPerfusion.IsPattern(perfusion))) { throw Invalid(); }
            if (band.AfBeatSelection is { } af && (!Enum.IsDefined(af) ||
                band.Trigger != PhysiologyCycleEventKind.VentricularElectrical || band.VentricularCycles is not null)) { throw Invalid(); }
            if (band.CycleDurationRate is { } rate && (band.Trigger != PhysiologyCycleEventKind.AtrialElectrical ||
                band.DelayNs != 0 || band.DurationNs < rate.PeriodNs * (1000 + rate.VariationPermille) / 1000)) { throw Invalid(); }
            long[] table = band.TableQ32.ToArray();
            if (band.VentricularCycles is { } cycles &&
                (band.Trigger != PhysiologyCycleEventKind.VentricularElectrical || cycles.Length is < 1 or > 64 ||
                 cycles.IncludedSlots == 0 || (cycles.Length < 64 && cycles.IncludedSlots >> cycles.Length != 0)))
            { throw Invalid(); }
            if (table[0] != 0 || table.Any(value => value < short.MinValue * FixedPointMath.Q32One ||
                value > short.MaxValue * FixedPointMath.Q32One)) { throw Invalid(); }
            int[]? gains = null;
            if (band.ExpirationActivationGainsPermille is null && band.ExpirationCycleOrigin != 0)
            { throw Invalid(); }
            if (band.ExpirationCycleGainsPermille is { } cycleGains)
            {
                if (band.Trigger != PhysiologyCycleEventKind.ExpirationStart || band.DepthPattern != RespiratoryPattern.Regular ||
                    cycleGains.Count is < 1 or > 64) { throw Invalid(); }
                gains = cycleGains.ToArray();
                if (gains.Any(g => g is < 0 or > 10_000) || table.Any(value =>
                    (Int128)value * gains.Max() < (Int128)short.MinValue * FixedPointMath.Q32One * 1000 ||
                    (Int128)value * gains.Max() > (Int128)short.MaxValue * FixedPointMath.Q32One * 1000)) { throw Invalid(); }
            }
            int[]? inspirationGains = null;
            if (band.InspirationCycleGainsPermille is { } inspirationCycleGains)
            {
                if (band.Trigger != PhysiologyCycleEventKind.InspirationStart || inspirationCycleGains.Count is < 1 or > 64)
                { throw Invalid(); }
                inspirationGains = inspirationCycleGains.ToArray();
                if (inspirationGains.Any(g => g is < 0 or > 10_000) || table.Any(value =>
                    (Int128)value * inspirationGains.Max() < (Int128)short.MinValue * FixedPointMath.Q32One * 1000 ||
                    (Int128)value * inspirationGains.Max() > (Int128)short.MaxValue * FixedPointMath.Q32One * 1000))
                { throw Invalid(); }
            }
            int[]? activationGains = null;
            if (band.ExpirationActivationGainsPermille is { } finiteGains)
            {
                if (band.Trigger != PhysiologyCycleEventKind.ExpirationStart || finiteGains.Count is < 1 or > 64 ||
                    finiteGains.Any(gain => gain is < 0 or > 1000))
                { throw Invalid(); }
                activationGains = finiteGains.ToArray();
            }
            if (band.CardiogenicRippleDurationNs < 0 || band.CardiogenicRipplePermille is < 0 or > 1000 ||
                (band.CardiogenicRippleDurationNs == 0) != (band.CardiogenicRipplePermille == 0) ||
                band.CardiogenicRippleDurationNs > 0 && (band.Trigger != PhysiologyCycleEventKind.ExpirationStart ||
                    band.CardiogenicRippleDurationNs > band.DurationNs || table.Any(value => value < 0)))
            { throw Invalid(); }
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
            if (band.DepthPattern == RespiratoryPattern.SpontaneousIllustration)
            {
                int inspirationGain = inspirationGains?.Max() ?? 1000;
                if (table.Any(value => (Int128)value * 1080 * inspirationGain <
                        (Int128)short.MinValue * FixedPointMath.Q32One * 1_000_000 ||
                    (Int128)value * 1080 * inspirationGain >
                        (Int128)short.MaxValue * FixedPointMath.Q32One * 1_000_000)) { throw Invalid(); }
            }
            RespiratoryCycleShape[]? shapes = null;
            if (band.RespiratoryCycleShapes is { } cycleShapes)
            {
                if (cycleShapes.Count != SpontaneousRespirationSchedule.Length ||
                    band.Trigger is not (PhysiologyCycleEventKind.InspirationStart or PhysiologyCycleEventKind.ExpirationStart) ||
                    band.CycleDurationRate is not null) { throw Invalid(); }
                shapes = new RespiratoryCycleShape[cycleShapes.Count];
                _cycleTables[index] = new long[cycleShapes.Count][];
                for (int slot = 0; slot < shapes.Length; slot++)
                {
                    var shape = cycleShapes[slot];
                    if (shape is null || shape.DelayNs < band.DelayNs ||
                        (Int128)shape.DelayNs + shape.DurationNs > (Int128)band.DelayNs + band.DurationNs)
                    { throw Invalid(); }
                    var owned = Restore(new([band with
                    {
                        DelayNs = shape.DelayNs,
                        DurationNs = shape.DurationNs,
                        TableQ32 = shape.TableQ32,
                        PhasePoints = shape.PhasePoints,
                        RespiratoryCycleShapes = null
                    }], [])).CaptureState().Bands[0];
                    long[] cycleTable = owned.TableQ32.ToArray();
                    _cycleTables[index]![slot] = cycleTable;
                    shapes[slot] = new(owned.DelayNs, owned.DurationNs, Array.AsReadOnly(cycleTable), owned.PhasePoints);
                }
            }
            _bands[index] = band with
            {
                TableQ32 = Array.AsReadOnly(table),
                PhasePoints = points is null ? null : Array.AsReadOnly(points),
                ExpirationCycleGainsPermille = gains is null ? null : Array.AsReadOnly(gains),
                InspirationCycleGainsPermille = inspirationGains is null ? null : Array.AsReadOnly(inspirationGains),
                ExpirationActivationGainsPermille = activationGains is null ? null : Array.AsReadOnly(activationGains),
                RespiratoryCycleShapes = shapes is null ? null : Array.AsReadOnly(shapes)
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
            foreach (EventWaveformBand band in _bands.Where(band => Accepts(band, item)))
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
                if (!Accepts(band, item)) { continue; }
                int slot = (int)(item.CycleIndex % SpontaneousRespirationSchedule.Length);
                var shape = band.RespiratoryCycleShapes?[slot];
                long delay = shape?.DelayNs ?? band.DelayNs;
                long elapsed = simTimeNs - item.SimTimeNs - delay;
                long duration = shape?.DurationNs ?? (band.CycleDurationRate is { } rate ? rate.FollowingIntervalNs(item.CycleIndex) :
                    band.EjectionIllustration is { } durationMode ? PrematureBeatPerfusion.DurationNs(durationMode, item.CycleIndex, band.DurationNs) : band.DurationNs);
                if (elapsed < 0 || elapsed >= duration) { continue; }
                // Equal adjacent table indices hold phase while time advances.
                // Integer phase maps the finite support into one frozen LUT cycle.
                var phasePoints = shape is null ? band.PhasePoints : shape.PhasePoints;
                long[] table = _cycleTables[index]?[slot] ?? _tables[index];
                ulong phase = PhaseAt(phasePoints, table.Length, shape?.DurationNs ?? band.DurationNs, elapsed, duration);
                long value = PeriodicLutLinear.Interpolate(table, phase).Value;
                int gain = band.EjectionIllustration is { } perfusion ? PrematureBeatPerfusion.GainPermille(perfusion, item.CycleIndex) : band.ExpirationCycleGainsPermille is { } cycleGains
                    ? cycleGains[(int)(item.CycleIndex % (ulong)cycleGains.Count)]
                    : RespiratoryPatternDepth.At(band.DepthPattern, item.CycleIndex);
                if (band.InspirationCycleGainsPermille is { } inspirationGains)
                { value = (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * inspirationGains[(int)(item.CycleIndex % (ulong)inspirationGains.Count)], 1000); }
                if (band.ExpirationActivationGainsPermille is { } activationGains)
                { value = (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * activationGains[(int)(item.CycleIndex - band.ExpirationCycleOrigin)], 1000); }
                if (band.CardiogenicRippleDurationNs > 0 && value > 0 &&
                    (phasePoints is not { Count: > 2 } points || elapsed >= points[2].OffsetNs))
                {
                    int attenuation = CardiogenicAttenuation(band, simTimeNs - delay);
                    value = (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * (1000 - attenuation), 1000);
                }
                sum += gain == 1000 ? value : FixedPointMath.RoundDivideTiesToEven((Int128)value * gain, 1000);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (sum < short.MinValue * FixedPointMath.Q32One || sum > short.MaxValue * FixedPointMath.Q32One)
        { throw new EventWaveformException("EventWaveform.AmplitudeOverflow", nameof(simTimeNs)); }
        return (long)sum;
    }

    private static bool Accepts(EventWaveformBand band, PhysiologyCycleEvent item) => band.Trigger == item.Kind &&
        (band.ExpirationActivationGainsPermille is not { } activationGains || item.CycleIndex >= band.ExpirationCycleOrigin &&
            item.CycleIndex - band.ExpirationCycleOrigin < (ulong)activationGains.Count) &&
        (band.AfBeatSelection is not { } af || (band.AfTiming is { } timing ? AtrialFibrillationReference.IsLongShortBeat(timing, item.CycleIndex) : AtrialFibrillationReference.IsLongShortBeat(item.CycleIndex)) == (af == AtrialFibrillationBeatSelection.LongShort)) &&
        (band.VentricularCycles is not { } cycles || (cycles.IncludedSlots & (1UL << (int)(item.CycleIndex % (ulong)cycles.Length))) != 0) &&
        (band.TriggerCycleLimit is null || item.CycleIndex < band.TriggerCycleLimit.Value ||
         (band.TriggerCycleResume is { } resume && item.CycleIndex >= resume));

    private int CardiogenicAttenuation(EventWaveformBand band, long sourceTimeNs)
    {
        int maximum = 0;
        foreach (var heartbeat in _events)
        {
            if (heartbeat.SimTimeNs > sourceTimeNs) { break; }
            long age = sourceTimeNs - heartbeat.SimTimeNs;
            if (heartbeat.Kind != PhysiologyCycleEventKind.VentricularMechanical || age >= band.CardiogenicRippleDurationNs)
            { continue; }
            long distance = Math.Min(age, band.CardiogenicRippleDurationNs - age);
            int attenuation = (int)FixedPointMath.RoundDivideTiesToEven(
                (Int128)distance * 2 * band.CardiogenicRipplePermille, band.CardiogenicRippleDurationNs);
            maximum = Math.Max(maximum, attenuation);
        }
        return maximum;
    }

    private static EventWaveformException Invalid() => new("EventWaveform.InvalidState", "state");

    private static ulong PhaseAt(IReadOnlyList<EventWaveformPhasePoint>? points, int tableCount,
        long referenceDuration, long elapsed, long duration)
    {
        if (points is null)
        { return (ulong)(((UInt128)elapsed << 64) / (ulong)duration); }
        elapsed = (long)((Int128)elapsed * referenceDuration / duration);
        for (int index = 1; index < points.Count; index++)
        {
            var right = points[index];
            if (elapsed >= right.OffsetNs) { continue; }
            var left = points[index - 1];
            UInt128 start = ((UInt128)(uint)left.TableIndex << 64) / (uint)tableCount;
            UInt128 end = ((UInt128)(uint)right.TableIndex << 64) / (uint)tableCount;
            return (ulong)(start + (end - start) * (ulong)(elapsed - left.OffsetNs) /
                (ulong)(right.OffsetNs - left.OffsetNs));
        }
        throw Invalid(); // Caller admits only the band's half-open support.
    }
}
