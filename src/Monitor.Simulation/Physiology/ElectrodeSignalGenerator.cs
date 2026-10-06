// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed record ElectrodeSignalState(RegularPhysiologyState Timeline,
    SignalSampleClockState Clock, IReadOnlyList<ElectrodeWaveformPlan> Electrodes, EcgLimbPlacement Placement = EcgLimbPlacement.Standard);
public sealed record ElectrodeSignalSample(SignalSampleTick Tick, EcgLeadProjection ExactLeads,
    IReadOnlyList<short> MicrovoltValues);
public sealed class ElectrodeSignalException(string reason, string parameter) : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; } = reason;
}

// Synchronous projected monitoring source, before acquisition delay and wire publication.
// Output microvolt values use EcgLead enum order; exact values retain identity evidence.
public sealed class ElectrodeSignalGenerator
{
    private RegularPhysiologyTimeline _timeline;
    private SignalSampleClock _clock;
    private readonly IReadOnlyList<ElectrodeWaveformPlan> _electrodes;
    private readonly long _lookbackNs;
    private readonly EcgLimbPlacement _placement;

    private ElectrodeSignalGenerator(ElectrodeSignalGenerator source)
    {
        _timeline = RegularPhysiologyTimeline.Restore(source._timeline.CaptureState());
        _clock = SignalSampleClock.Restore(source._clock.CaptureState());
        _electrodes = source._electrodes;
        _lookbackNs = source._lookbackNs;
        _placement = source._placement;
    }

    // Only immutable, already owned electrode bands and phase maps are shared.
    internal ElectrodeSignalGenerator Fork() => new(this);

    private ElectrodeSignalGenerator(ElectrodeSignalState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _timeline = RegularPhysiologyTimeline.Restore(state.Timeline);
        _clock = SignalSampleClock.Restore(state.Clock);
        if (_clock.ProfileId != "AcqECGMonitor250@1" || state.Timeline.CursorSimTimeNs != _clock.CursorSimTimeNs ||
            state.Timeline.Plan.EpochAnchorSimTimeNs != _clock.EpochAnchorSimTimeNs)
        { throw Invalid(); }
        _electrodes = ElectrodeWaveformComposition.Restore(new(state.Electrodes, [], state.Placement)).CaptureState().Electrodes;
        if (_electrodes.SelectMany(e => e.Bands).Any(b => b.AfBeatSelection is not null) &&
            !AtrialFibrillationReference.IsPattern(state.Timeline.Plan.ConductionPattern)) { throw Invalid(); }
        state.Timeline.Plan.RateAdjustment?.ValidateBands(state.Timeline.Plan, _electrodes.SelectMany(e => e.Bands));
        _placement = state.Placement;
        _lookbackNs = _electrodes.SelectMany(item => item.Bands).Max(band => checked(band.DelayNs + band.DurationNs));
    }

    public static ElectrodeSignalGenerator Start(RegularPhysiologyPlan plan, string profileId,
        ulong streamEpoch, IReadOnlyList<ElectrodeWaveformPlan> electrodes, EcgLimbPlacement placement = EcgLimbPlacement.Standard)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Restore(new(RegularPhysiologyTimeline.Start(plan).CaptureState(),
            SignalSampleClock.Start(profileId, streamEpoch, plan.EpochAnchorSimTimeNs).CaptureState(), electrodes, placement));
    }

    public static ElectrodeSignalGenerator Restore(ElectrodeSignalState state)
    {
        try { return new(state); }
        catch (ArgumentException) { throw Invalid(); }
        catch (OverflowException) { throw Invalid(); }
    }

    public ElectrodeSignalState CaptureState() => new(_timeline.CaptureState(), _clock.CaptureState(), _electrodes, _placement);

    public IReadOnlyList<ElectrodeSignalSample> GenerateBefore(long exclusiveSimTimeNs, int maximumSamples,
        int maximumEvents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumSamples is <= 0 or > PeriodicSignalGenerator.MaximumBatchSampleCount)
        { throw new ElectrodeSignalException("ElectrodeSignal.InvalidSampleLimit", nameof(maximumSamples)); }
        if (maximumEvents is <= 0 or > EventWaveformComposition.MaximumEventCount)
        { throw new ElectrodeSignalException("ElectrodeSignal.InvalidEventLimit", nameof(maximumEvents)); }
        if (exclusiveSimTimeNs < _clock.CursorSimTimeNs)
        { throw new ElectrodeSignalException("ElectrodeSignal.TimeRegression", nameof(exclusiveSimTimeNs)); }
        if (exclusiveSimTimeNs > _clock.NextSampleSimTimeNs &&
            ((Int128)exclusiveSimTimeNs - 1 - _clock.NextSampleSimTimeNs) / _clock.SamplePeriodNs + 1 > maximumSamples)
        { throw new ElectrodeSignalException("ElectrodeSignal.SampleLimitExceeded", nameof(maximumSamples)); }

        RegularPhysiologyState timeline = _timeline.CaptureState();
        long start = Math.Max(timeline.Plan.EpochAnchorSimTimeNs, timeline.CursorSimTimeNs - _lookbackNs);
        var trialTimeline = RegularPhysiologyTimeline.Restore(timeline with { CursorSimTimeNs = start });
        IReadOnlyList<PhysiologyCycleEvent> events = trialTimeline.AdvanceBefore(exclusiveSimTimeNs, maximumEvents, cancellationToken);
        var composition = ElectrodeWaveformComposition.Restore(new(_electrodes, events, _placement));
        var trialClock = SignalSampleClock.Restore(_clock.CaptureState());
        IReadOnlyList<SignalSampleTick> ticks = trialClock.DrainBefore(exclusiveSimTimeNs);
        var output = new ElectrodeSignalSample[ticks.Count];
        for (int index = 0; index < ticks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EcgLeadProjection leads = composition.EvaluateAt(ticks[index].SimTimeNs, cancellationToken).Leads;
            short[] values = new short[12];
            for (int lead = 0; lead < values.Length; lead++)
            {
                // Round exact projected microvolts once, avoiding double rounding
                // through an intermediate Q32 lead value.
                Int128 value = FixedPointMath.RoundDivideTiesToEven(leads[(EcgLead)lead].Numerator,
                    6 * (Int128)FixedPointMath.Q32One);
                if (value < short.MinValue || value > short.MaxValue)
                { throw new ElectrodeSignalException("ElectrodeSignal.AmplitudeOverflow", "electrodes"); }
                values[lead] = (short)value;
            }
            output[index] = new(ticks[index], leads, Array.AsReadOnly(values));
        }
        cancellationToken.ThrowIfCancellationRequested();
        _timeline = trialTimeline;
        _clock = trialClock;
        return Array.AsReadOnly(output);
    }

    private static ElectrodeSignalException Invalid() => new("ElectrodeSignal.InvalidCheckpoint", "state");
}
