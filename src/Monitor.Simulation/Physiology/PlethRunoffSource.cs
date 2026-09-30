// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Relative optical-pulse illustration, not oxygen saturation or arterial pressure.
public sealed record PlethRunoffPlan(long TransitDelayNs, long PulseDurationNs, int AmplitudeCounts,
    long TailTimeConstantNs = 400_000_000, bool IncludeNotch = false, bool UsePrematureBeatPerfusion = false,
    string ModelId = "PlethSmoothRunoff@1", bool UseAtrialFibrillationPerfusion = false, bool IllustrateAfSystemicPulseDeficit = false, bool UseConductedFlutterPerfusion = false);

// Immutable bounded reconstruction: no accumulated integration and no epoch scan.
public sealed class PlethRunoffSource
{
    public const int MaximumEventCount = 4096;
    private const long Q = FixedPointMath.Q32One;
    private readonly RegularPhysiologyPlan _physiology;
    private readonly PlethRunoffPlan _plan;
    private readonly long[] _powers = new long[27];
    private readonly long[] _table;
    private readonly int _joinIndex;
    public long SupportNs { get; }
    public int MaximumHistoryEvents { get; }

    private PlethRunoffSource(RegularPhysiologyPlan physiology, PlethRunoffPlan plan)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        if (plan is null || plan.ModelId != "PlethSmoothRunoff@1" || plan.TransitDelayNs < 0 ||
            plan.PulseDurationNs <= 0 || plan.AmplitudeCounts is < 0 or > short.MaxValue ||
            plan.TailTimeConstantNs is < 100_000_000 or > 2_000_000_000 ||
            plan.UsePrematureBeatPerfusion && !PrematureBeatPerfusion.IsPattern(physiology.ConductionPattern)) { throw Invalid(); }
        if (plan.UseAtrialFibrillationPerfusion && (plan.UsePrematureBeatPerfusion ||
            !AtrialFibrillationReference.IsPattern(physiology.ConductionPattern))) { throw Invalid(); }
        if (plan.UseConductedFlutterPerfusion && (plan.UsePrematureBeatPerfusion || plan.UseAtrialFibrillationPerfusion ||
            !ConductedFlutterPerfusion.Supports(physiology))) { throw Invalid(); }
        if (plan.IllustrateAfSystemicPulseDeficit && !plan.UseAtrialFibrillationPerfusion) { throw Invalid(); }
        _physiology = physiology; _plan = plan;
        _table = (plan.IncludeNotch ? PlethPulseTables.Notched : PlethPulseTables.Plain).Select(value => checked(value * plan.AmplitudeCounts)).ToArray();
        _joinIndex = plan.IncludeNotch ? 72 : 88;
        Int128 join = (Int128)plan.PulseDurationNs * _joinIndex / 128;
        Int128 support = join + 64 * (Int128)plan.TailTimeConstantNs;
        Int128 interval = physiology.VentricularPeriodNs * physiology.MechanicalEveryCycles;
        if (join <= 0 || plan.UsePrematureBeatPerfusion && (Int128)plan.PulseDurationNs * 3 / 4 * _joinIndex / 128 <= 0 || support + plan.TransitDelayNs > long.MaxValue ||
            (support + interval - 1) / interval > MaximumEventCount) { throw Invalid(); }
        SupportNs = (long)support;
        MaximumHistoryEvents = (int)((support + interval - 1) / interval);
        // Same1us trapezoidal Q62 decay definition as the pressure kernel.
        _powers[0] = (long)FixedPointMath.RoundDivideTiesToEven(
            (2 * (Int128)plan.TailTimeConstantNs - 1000) * FixedPointMath.Q62One,
            2 * (Int128)plan.TailTimeConstantNs + 1000);
        for (int i = 1; i < _powers.Length; i++)
        { _powers[i] = (long)FixedPointMath.RoundDivideTiesToEven((Int128)_powers[i - 1] * _powers[i - 1], FixedPointMath.Q62One); }
        if (Decay(64 * plan.TailTimeConstantNs) != 0) { throw Invalid(); }
        // Before join reserve full peaks; afterwards reserve the monotone tail
        // at the earliest possible age of each older event. One count per event
        // covers fixed-point/quantization rounding; never saturate accepted data.
        Int128 bound = ((join + interval - 1) / interval) * plan.AmplitudeCounts * Q;
        for (Int128 age = 0; age < 64 * (Int128)plan.TailTimeConstantNs; age += interval)
        { bound += (Int128)plan.AmplitudeCounts * Tail((long)age) / (1L << 30); }
        if (bound + (Int128)MaximumHistoryEvents * Q > short.MaxValue * Q) { throw Invalid(); }
    }

    public static PlethRunoffSource Create(RegularPhysiologyPlan physiology, PlethRunoffPlan plan) => new(physiology, plan);

    public long EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (simTimeNs < _physiology.EpochAnchorSimTimeNs) { throw Invalid(); }
        Int128 source = (Int128)simTimeNs - _plan.TransitDelayNs;
        if (source < _physiology.EpochAnchorSimTimeNs) { return 0; }
        long begin = (long)Int128.Max(_physiology.EpochAnchorSimTimeNs, source - SupportNs + 1);
        Int128 sum = 0;
        RegularPhysiologyTimeline.VisitVentricularMechanical(_physiology, begin, source + 1, MaximumHistoryEvents, beat =>
        {
            int gain = _plan.UsePrematureBeatPerfusion ? PrematureBeatPerfusion.GainPermille(_physiology.ConductionPattern, beat.CycleIndex) :
                _plan.UseAtrialFibrillationPerfusion ? AtrialFibrillationPerfusion.GainPermille(_physiology.ConductionPattern, beat.CycleIndex, _plan.IllustrateAfSystemicPulseDeficit) :
                    _plan.UseConductedFlutterPerfusion ? ConductedFlutterPerfusion.GainPermille(_physiology, beat.CycleIndex) :
                        _physiology.SeededRate?.EjectionGainPermille(beat.CycleIndex) ?? 1000;
            if (gain == 0) { return; }
            long duration = _plan.UsePrematureBeatPerfusion ? PrematureBeatPerfusion.DurationNs(_physiology.ConductionPattern, beat.CycleIndex, _plan.PulseDurationNs) : _plan.PulseDurationNs;
            long join = (long)((Int128)duration * _joinIndex / 128);
            long age = (long)(source - beat.SimTimeNs);
            long value = age < join ? PeriodicLutLinear.Interpolate(_table, (ulong)(((UInt128)age << 64) / (ulong)duration)).Value :
                (long)FixedPointMath.RoundDivideTiesToEven((Int128)_table[_joinIndex] * Tail(age - join), FixedPointMath.Q62One);
            sum += FixedPointMath.RoundDivideTiesToEven((Int128)value * gain, 1000);
        }, cancellationToken);
        if (sum < 0 || sum > short.MaxValue * Q) { throw new EventWaveformException("PlethRunoff.AmplitudeOverflow", nameof(simTimeNs)); }
        return (long)sum;
    }

    // (1+u/tau)*exp(-u/tau): zero initial slope joins the rounded shoulder.
    // It is an authored smooth tail, not a fitted optical or vascular parameter.
    private long Tail(long age)
    {
        if (age >= 64 * _plan.TailTimeConstantNs) { return 0; }
        return (long)Int128.Min(FixedPointMath.Q62One, FixedPointMath.RoundDivideTiesToEven(
            (Int128)Decay(age) * (_plan.TailTimeConstantNs + age), _plan.TailTimeConstantNs));
    }
    private long Decay(long age)
    {
        long steps = age / 1000, fraction = age % 1000;
        long left = Whole(steps), right = fraction == 0 ? left : Whole(steps + 1);
        return left + (long)FixedPointMath.RoundDivideTiesToEven((Int128)(right - left) * fraction, 1000);
    }
    private long Whole(long steps)
    {
        long value = FixedPointMath.Q62One;
        for (int i = 0; steps > 0; i++, steps >>= 1)
        { if ((steps & 1) != 0) { value = (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * _powers[i], FixedPointMath.Q62One); } }
        return value;
    }
    private static EventWaveformException Invalid() => new("PlethRunoff.InvalidPlan", "plan");
}
