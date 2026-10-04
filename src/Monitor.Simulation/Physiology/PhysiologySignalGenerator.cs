// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed record PhysiologySignalState(RegularPhysiologyState Timeline,
    SignalSampleClockState Clock, IReadOnlyList<EventWaveformBand> Bands,
    VascularPressurePlan? VascularPressure = null, PlethRunoffPlan? PlethRunoff = null)
{
    public long? ActiveFromEventTimeNs { get; init; }
    public IReadOnlyList<PhysiologySignalSegment> History { get; init; } = [];
}
public readonly record struct PhysiologySignalSample(SignalSampleTick Tick, long ValueQ32, short NormalizedValue);
public sealed class PhysiologySignalException(string reason, string parameter) : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; } = reason;
}

// Continuous source sampling with reconstructible, bounded regular-event history.
public sealed class PhysiologySignalGenerator
{
    private RegularPhysiologyTimeline _timeline;
    private PhysiologySignalContinuation[] _history = [];
    private PhysiologySignalContinuation? _active;
    private SignalSampleClock _clock;
    private readonly IReadOnlyList<EventWaveformBand> _bands;
    private readonly long _lookbackNs;
    private readonly VascularPressurePlan? _vascularPressurePlan;
    private readonly VascularPressureSource? _vascularPressure;
    private readonly PlethRunoffPlan? _plethRunoffPlan;
    internal PlethRunoffSource? PlethRunoff { get; }

    private PhysiologySignalGenerator(PhysiologySignalGenerator source)
    {
        _history = source._history;
        _active = source._active;
        _timeline = RegularPhysiologyTimeline.Restore(source._timeline.CaptureState());
        _clock = SignalSampleClock.Restore(source._clock.CaptureState());
        _bands = source._bands;
        _lookbackNs = source._lookbackNs;
        _vascularPressurePlan = source._vascularPressurePlan;
        _vascularPressure = source._vascularPressure;
        _plethRunoffPlan = source._plethRunoffPlan;
        PlethRunoff = source.PlethRunoff;
    }

    // Only immutable, already owned source definitions are shared.
    internal PhysiologySignalGenerator Fork() => new(this);

    private PhysiologySignalGenerator(PhysiologySignalState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _timeline = RegularPhysiologyTimeline.Restore(state.Timeline);
        _clock = SignalSampleClock.Restore(state.Clock);
        if (state.Timeline.CursorSimTimeNs != _clock.CursorSimTimeNs ||
            state.Timeline.Plan.EpochAnchorSimTimeNs != _clock.EpochAnchorSimTimeNs)
        { throw Invalid(); }
        if (state.PlethRunoff is { } runoff)
        {
            if (state.VascularPressure is not null || state.Bands is null || state.Bands.Count != 0) { throw Invalid(); }
            PlethRunoff = PlethRunoffSource.Create(state.Timeline.Plan, runoff);
            _plethRunoffPlan = runoff;
            _bands = Array.Empty<EventWaveformBand>();
        }
        else if (state.VascularPressure is { } pressure)
        {
            // Pressure is an independent source, not an offset added to a second
            // copy of the old pressure morphology. Its history is reconstructible
            // from the immutable plan, independently of the acquisition clock.
            if (state.Bands is null || state.Bands.Count != 0) { throw Invalid(); }
            _vascularPressure = VascularPressureSource.Create(state.Timeline.Plan, pressure);
            _vascularPressurePlan = pressure;
            _bands = Array.Empty<EventWaveformBand>();
        }
        else
        {
            _bands = EventWaveformComposition.Restore(new(state.Bands, [])).CaptureState().Bands;
            if (_bands.Any(b => b.EjectionIllustration is { } mode && mode != state.Timeline.Plan.ConductionPattern)) { throw Invalid(); }
            if (_bands.Any(b => b.AfBeatSelection is not null) && !AtrialFibrillationReference.IsPattern(state.Timeline.Plan.ConductionPattern)) { throw Invalid(); }
            _lookbackNs = _bands.Max(band => checked(band.DelayNs + band.DurationNs));
        }
        if (state.History is null || state.History.Count > 512) { throw Invalid(); }
        _history = state.History.Select(s => new PhysiologySignalContinuation(s)).ToArray();
        for (int i = 0; i < _history.Length; i++)
        {
            if (_history[i].Segment.Plan.EpochAnchorSimTimeNs != state.Clock.EpochAnchorSimTimeNs ||
                i > 0 && _history[i - 1].Segment.ToExclusiveEventTimeNs > _history[i].Segment.FromEventTimeNs)
            { throw Invalid(); }
        }
        if (state.ActiveFromEventTimeNs is { } from)
        {
            if (from < state.Clock.EpochAnchorSimTimeNs || from > state.Clock.CursorSimTimeNs ||
                _history.Any(s => s.Segment.ToExclusiveEventTimeNs > from)) { throw Invalid(); }
            _active = new(new(state.Timeline.Plan, _bands, _vascularPressurePlan, _plethRunoffPlan, from, long.MaxValue, false));
        }
        else if (_history.Length != 0) { throw Invalid(); }
    }

    internal PhysiologySignalGenerator ContinueWith(PhysiologySignalState definition)
    {
        var current = CaptureState();
        bool sameRate = current.Timeline.Plan.SeededRate is { } rate && definition.Timeline.Plan.SeededRate is { } nextRate
            ? rate.HeartRateBpm == nextRate.HeartRateBpm && rate.SeedHex == nextRate.SeedHex && rate.VariationPermille == nextRate.VariationPermille
            : current.Timeline.Plan.SeededRate is null && definition.Timeline.Plan.SeededRate is null;
        if (sameRate && current.Timeline.Plan with { SeededRate = definition.Timeline.Plan.SeededRate } == definition.Timeline.Plan && _vascularPressurePlan == definition.VascularPressure &&
            _plethRunoffPlan == definition.PlethRunoff && _bands.Count == definition.Bands.Count &&
            _bands.Zip(definition.Bands).All(pair =>
                pair.First with
                {
                    TableQ32 = pair.Second.TableQ32,
                    PhasePoints = pair.Second.PhasePoints,
                    ExpirationCycleGainsPermille = pair.Second.ExpirationCycleGainsPermille
                } == pair.Second &&
                pair.First.TableQ32.SequenceEqual(pair.Second.TableQ32) &&
                (pair.First.PhasePoints ?? []).SequenceEqual(pair.Second.PhasePoints ?? []) &&
                (pair.First.ExpirationCycleGainsPermille ?? []).SequenceEqual(pair.Second.ExpirationCycleGainsPermille ?? [])))
        { return Fork(); }
        long boundary = current.Clock.CursorSimTimeNs;
        long from = _active?.Segment.FromEventTimeNs ?? current.Clock.EpochAnchorSimTimeNs;
        var history = _history.Where(s => boundary - s.Segment.ToExclusiveEventTimeNs <= s.SupportNs + 2_200_000_000)
            .Select(s => s.Segment).ToList();
        if (boundary > from)
        {
            history.Add(new(current.Timeline.Plan, _bands, _vascularPressurePlan, _plethRunoffPlan,
                from, boundary, _active is null));
        }
        return Restore(definition with
        {
            Timeline = new(definition.Timeline.Plan with { EpochAnchorSimTimeNs = current.Clock.EpochAnchorSimTimeNs }, boundary),
            Clock = current.Clock,
            ActiveFromEventTimeNs = boundary,
            History = history
        });
    }

    internal long EvaluateAt(long timeNs, CancellationToken cancellationToken = default)
    {
        long result;
        if (_active is not null) { result = _active.EvaluateAt(timeNs, true, cancellationToken); }
        else if (_vascularPressure is not null) { return _vascularPressure.EvaluateAt(timeNs, cancellationToken); }
        else if (PlethRunoff is not null) { return PlethRunoff.EvaluateAt(timeNs, cancellationToken); }
        else
        {
            var state = _timeline.CaptureState();
            var timeline = RegularPhysiologyTimeline.Restore(state with
            { CursorSimTimeNs = Math.Max(state.Plan.EpochAnchorSimTimeNs, timeNs - _lookbackNs) });
            return EventWaveformComposition.Restore(new(_bands,
                timeline.AdvanceBefore(checked(timeNs + 1), EventWaveformComposition.MaximumEventCount, cancellationToken)))
                .EvaluateAt(timeNs, cancellationToken);
        }
        foreach (var previous in _history)
        {
            bool baseline = timeNs < _active!.Segment.FromEventTimeNs && timeNs >= previous.Segment.FromEventTimeNs &&
                timeNs < previous.Segment.ToExclusiveEventTimeNs;
            result = checked(result + previous.EvaluateAt(timeNs, baseline, cancellationToken));
        }
        return result;
    }

    public static PhysiologySignalGenerator Start(RegularPhysiologyPlan plan, string profileId,
        ulong streamEpoch, IReadOnlyList<EventWaveformBand> bands, VascularPressurePlan? vascularPressure = null, PlethRunoffPlan? plethRunoff = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Restore(new(RegularPhysiologyTimeline.Start(plan).CaptureState(),
            SignalSampleClock.Start(profileId, streamEpoch, plan.EpochAnchorSimTimeNs).CaptureState(), bands, vascularPressure, plethRunoff));
    }

    public static PhysiologySignalGenerator Restore(PhysiologySignalState state)
    {
        try { return new(state); }
        catch (ArgumentException) { throw Invalid(); }
        catch (OverflowException) { throw Invalid(); }
    }

    public PhysiologySignalState CaptureState() => new(_timeline.CaptureState(), _clock.CaptureState(), _bands, _vascularPressurePlan, _plethRunoffPlan)
    {
        ActiveFromEventTimeNs = _active?.Segment.FromEventTimeNs,
        History = Array.AsReadOnly(_history.Select(s => s.Segment).ToArray())
    };

    internal VascularPressureSource? VascularPressure => _vascularPressure;

    public IReadOnlyList<PhysiologySignalSample> GenerateBefore(long exclusiveSimTimeNs, int maximumSamples,
        int maximumEvents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumSamples is <= 0 or > PeriodicSignalGenerator.MaximumBatchSampleCount)
        { throw new PhysiologySignalException("PhysiologySignal.InvalidSampleLimit", nameof(maximumSamples)); }
        if (maximumEvents is <= 0 or > EventWaveformComposition.MaximumEventCount)
        { throw new PhysiologySignalException("PhysiologySignal.InvalidEventLimit", nameof(maximumEvents)); }
        if (exclusiveSimTimeNs < _clock.CursorSimTimeNs)
        { throw new PhysiologySignalException("PhysiologySignal.TimeRegression", nameof(exclusiveSimTimeNs)); }
        if (exclusiveSimTimeNs > _clock.NextSampleSimTimeNs &&
            ((Int128)exclusiveSimTimeNs - 1 - _clock.NextSampleSimTimeNs) / _clock.SamplePeriodNs + 1 > maximumSamples)
        { throw new PhysiologySignalException("PhysiologySignal.SampleLimitExceeded", nameof(maximumSamples)); }

        RegularPhysiologyState timeline = _timeline.CaptureState();
        long start = Math.Max(timeline.Plan.EpochAnchorSimTimeNs, timeline.CursorSimTimeNs - _lookbackNs);
        var trialTimeline = RegularPhysiologyTimeline.Restore(timeline with { CursorSimTimeNs = start });
        IReadOnlyList<PhysiologyCycleEvent> events = trialTimeline.AdvanceBefore(exclusiveSimTimeNs, maximumEvents, cancellationToken);
        EventWaveformComposition? composition = _vascularPressure is null && PlethRunoff is null ? EventWaveformComposition.Restore(new(_bands, events)) : null;
        var trialClock = SignalSampleClock.Restore(_clock.CaptureState());
        IReadOnlyList<SignalSampleTick> ticks = trialClock.DrainBefore(exclusiveSimTimeNs);
        var output = new PhysiologySignalSample[ticks.Count];
        for (int index = 0; index < ticks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long value = _active is not null ? EvaluateAt(ticks[index].SimTimeNs, cancellationToken) : _vascularPressure is { } pressure
                ? pressure.EvaluateAt(ticks[index].SimTimeNs, cancellationToken)
                : PlethRunoff is { } runoff ? runoff.EvaluateAt(ticks[index].SimTimeNs, cancellationToken)
                : composition!.EvaluateAt(ticks[index].SimTimeNs, cancellationToken);
            short normalized = checked((short)FixedPointMath.RoundDivideTiesToEven(value, FixedPointMath.Q32One));
            output[index] = new(ticks[index], value, normalized);
        }
        cancellationToken.ThrowIfCancellationRequested();
        _timeline = trialTimeline;
        _clock = trialClock;
        return Array.AsReadOnly(output);
    }

    private static PhysiologySignalException Invalid() => new("PhysiologySignal.InvalidCheckpoint", "state");
}
