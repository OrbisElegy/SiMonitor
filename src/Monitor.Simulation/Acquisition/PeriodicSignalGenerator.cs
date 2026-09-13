// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Acquisition;

public sealed class PeriodicSignalGeneratorException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record PeriodicSignalPlan(string ProfileId, ulong StreamEpoch, long EpochAnchorSimTimeNs,
    ulong InitialPhaseU64, ulong PhaseIncrementU64, IReadOnlyList<long> TableQ32);
public sealed record PeriodicSignalState(PeriodicSignalPlan Plan, SignalSampleClockState Clock);
public readonly record struct GeneratedSignalSample(SignalSampleTick Tick, ulong PhaseU64,
    long ValueQ32, short NormalizedValue);

// Caller-supplied periodic morphology. No built-in ECG, unit or quality claim.
public sealed class PeriodicSignalGenerator
{
    public const int MaximumBatchSampleCount = 1_000_000;
    private readonly PeriodicSignalPlan _plan;
    private readonly long[] _table;
    private SignalSampleClock _clock;

    private PeriodicSignalGenerator(PeriodicSignalPlan plan, SignalSampleClock clock)
    {
        if (plan is null || plan.TableQ32 is null || plan.TableQ32.Count is < 4 or > 65_536 ||
            !BitOperations.IsPow2((uint)plan.TableQ32.Count) || plan.PhaseIncrementU64 == 0)
        { throw new PeriodicSignalGeneratorException("PeriodicSignal.InvalidPlan", nameof(plan)); }
        _table = new long[plan.TableQ32.Count];
        for (int index = 0; index < _table.Length; index++)
        {
            long value = plan.TableQ32[index];
            if (value < short.MinValue * FixedPointMath.Q32One || value > short.MaxValue * FixedPointMath.Q32One)
            { throw new PeriodicSignalGeneratorException("PeriodicSignal.InvalidAmplitude", nameof(plan)); }
            _table[index] = value;
        }
        _plan = plan with { TableQ32 = Array.AsReadOnly(_table) };
        _clock = clock;
        if (clock.ProfileId != plan.ProfileId || clock.StreamEpoch != plan.StreamEpoch ||
            clock.EpochAnchorSimTimeNs != plan.EpochAnchorSimTimeNs)
        { throw new PeriodicSignalGeneratorException("PeriodicSignal.IdentityMismatch", nameof(plan)); }
    }

    public static PeriodicSignalGenerator Start(PeriodicSignalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(plan, SignalSampleClock.Start(plan.ProfileId, plan.StreamEpoch, plan.EpochAnchorSimTimeNs));
    }

    public PeriodicSignalState CaptureState() => new(_plan, _clock.CaptureState());

    public static PeriodicSignalGenerator Restore(PeriodicSignalState state)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            return new(state.Plan, SignalSampleClock.Restore(state.Clock));
        }
        catch (ArgumentException)
        { throw new PeriodicSignalGeneratorException("PeriodicSignal.InvalidCheckpoint", nameof(state)); }
    }

    public IReadOnlyList<GeneratedSignalSample> GenerateBefore(long exclusiveSimTimeNs, int maximumSamples,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumSamples <= 0 || maximumSamples > MaximumBatchSampleCount)
        { throw new PeriodicSignalGeneratorException("PeriodicSignal.InvalidSampleLimit", nameof(maximumSamples)); }
        SignalSampleClock trial = SignalSampleClock.Restore(_clock.CaptureState());
        if (exclusiveSimTimeNs > trial.NextSampleSimTimeNs)
        {
            Int128 count = ((Int128)exclusiveSimTimeNs - 1 - trial.NextSampleSimTimeNs) / trial.SamplePeriodNs + 1;
            if (count > maximumSamples)
            { throw new PeriodicSignalGeneratorException("PeriodicSignal.SampleLimitExceeded", nameof(maximumSamples)); }
        }
        IReadOnlyList<SignalSampleTick> ticks = trial.DrainBefore(exclusiveSimTimeNs);
        GeneratedSignalSample[] output = new GeneratedSignalSample[ticks.Count];
        for (int index = 0; index < output.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SignalSampleTick tick = ticks[index];
            ulong phase = unchecked(_plan.InitialPhaseU64 + tick.SampleIndex * _plan.PhaseIncrementU64);
            long value = PeriodicLutLinear.Interpolate(_table, phase).Value;
            short normalized = checked((short)FixedPointMath.RoundDivideTiesToEven(value, FixedPointMath.Q32One));
            output[index] = new(tick, phase, value, normalized);
        }
        cancellationToken.ThrowIfCancellationRequested();
        _clock = trial;
        return Array.AsReadOnly(output);
    }
}
