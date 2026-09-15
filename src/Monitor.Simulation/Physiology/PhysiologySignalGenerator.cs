// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed record PhysiologySignalState(RegularPhysiologyState Timeline,
    SignalSampleClockState Clock, IReadOnlyList<EventWaveformBand> Bands,
    VascularPressurePlan? VascularPressure = null);
public readonly record struct PhysiologySignalSample(SignalSampleTick Tick, long ValueQ32, short NormalizedValue);
public sealed class PhysiologySignalException(string reason, string parameter) : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; } = reason;
}

// Continuous source sampling with reconstructible, bounded regular-event history.
public sealed class PhysiologySignalGenerator
{
    private RegularPhysiologyTimeline _timeline;
    private SignalSampleClock _clock;
    private readonly IReadOnlyList<EventWaveformBand> _bands;
    private readonly long _lookbackNs;
    private readonly VascularPressurePlan? _vascularPressurePlan;
    private readonly VascularPressureSource? _vascularPressure;

    private PhysiologySignalGenerator(PhysiologySignalGenerator source)
    {
        _timeline = RegularPhysiologyTimeline.Restore(source._timeline.CaptureState());
        _clock = SignalSampleClock.Restore(source._clock.CaptureState());
        _bands = source._bands;
        _lookbackNs = source._lookbackNs;
        _vascularPressurePlan = source._vascularPressurePlan;
        _vascularPressure = source._vascularPressure;
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
        if (state.VascularPressure is { } pressure)
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
            _lookbackNs = _bands.Max(band => checked(band.DelayNs + band.DurationNs));
        }
    }

    public static PhysiologySignalGenerator Start(RegularPhysiologyPlan plan, string profileId,
        ulong streamEpoch, IReadOnlyList<EventWaveformBand> bands, VascularPressurePlan? vascularPressure = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Restore(new(RegularPhysiologyTimeline.Start(plan).CaptureState(),
            SignalSampleClock.Start(profileId, streamEpoch, plan.EpochAnchorSimTimeNs).CaptureState(), bands, vascularPressure));
    }

    public static PhysiologySignalGenerator Restore(PhysiologySignalState state)
    {
        try { return new(state); }
        catch (ArgumentException) { throw Invalid(); }
        catch (OverflowException) { throw Invalid(); }
    }

    public PhysiologySignalState CaptureState() => new(_timeline.CaptureState(), _clock.CaptureState(), _bands, _vascularPressurePlan);

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
        RegularPhysiologyTimeline trialTimeline = RegularPhysiologyTimeline.Restore(timeline with { CursorSimTimeNs = start });
        IReadOnlyList<PhysiologyCycleEvent> events = trialTimeline.AdvanceBefore(exclusiveSimTimeNs, maximumEvents, cancellationToken);
        EventWaveformComposition? composition = _vascularPressure is null ? EventWaveformComposition.Restore(new(_bands, events)) : null;
        SignalSampleClock trialClock = SignalSampleClock.Restore(_clock.CaptureState());
        IReadOnlyList<SignalSampleTick> ticks = trialClock.DrainBefore(exclusiveSimTimeNs);
        PhysiologySignalSample[] output = new PhysiologySignalSample[ticks.Count];
        for (int index = 0; index < ticks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long value = _vascularPressure is { } pressure
                ? pressure.EvaluateAt(ticks[index].SimTimeNs, cancellationToken)
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
