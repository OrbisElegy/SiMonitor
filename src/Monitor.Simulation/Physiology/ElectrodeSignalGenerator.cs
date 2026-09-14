// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed record ElectrodeSignalState(RegularPhysiologyState Timeline,
    SignalSampleClockState Clock, IReadOnlyList<ElectrodeWaveformPlan> Electrodes);
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

    private ElectrodeSignalGenerator(ElectrodeSignalState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _timeline = RegularPhysiologyTimeline.Restore(state.Timeline);
        _clock = SignalSampleClock.Restore(state.Clock);
        if (_clock.ProfileId != "AcqECGMonitor250@1" || state.Timeline.CursorSimTimeNs != _clock.CursorSimTimeNs ||
            state.Timeline.Plan.EpochAnchorSimTimeNs != _clock.EpochAnchorSimTimeNs)
        { throw Invalid(); }
        _electrodes = ElectrodeWaveformComposition.Restore(new(state.Electrodes, [])).CaptureState().Electrodes;
        _lookbackNs = _electrodes.SelectMany(item => item.Bands).Max(band => checked(band.DelayNs + band.DurationNs));
    }

    public static ElectrodeSignalGenerator Start(RegularPhysiologyPlan plan, string profileId,
        ulong streamEpoch, IReadOnlyList<ElectrodeWaveformPlan> electrodes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Restore(new(RegularPhysiologyTimeline.Start(plan).CaptureState(),
            SignalSampleClock.Start(profileId, streamEpoch, plan.EpochAnchorSimTimeNs).CaptureState(), electrodes));
    }

    public static ElectrodeSignalGenerator Restore(ElectrodeSignalState state)
    {
        try { return new(state); }
        catch (ArgumentException) { throw Invalid(); }
        catch (OverflowException) { throw Invalid(); }
    }

    public ElectrodeSignalState CaptureState() => new(_timeline.CaptureState(), _clock.CaptureState(), _electrodes);

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
        RegularPhysiologyTimeline trialTimeline = RegularPhysiologyTimeline.Restore(timeline with { CursorSimTimeNs = start });
        IReadOnlyList<PhysiologyCycleEvent> events = trialTimeline.AdvanceBefore(exclusiveSimTimeNs, maximumEvents, cancellationToken);
        ElectrodeWaveformComposition composition = ElectrodeWaveformComposition.Restore(new(_electrodes, events));
        SignalSampleClock trialClock = SignalSampleClock.Restore(_clock.CaptureState());
        IReadOnlyList<SignalSampleTick> ticks = trialClock.DrainBefore(exclusiveSimTimeNs);
        ElectrodeSignalSample[] output = new ElectrodeSignalSample[ticks.Count];
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
