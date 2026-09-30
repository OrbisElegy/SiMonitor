// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Immutable indexed reconstruction from the declared epoch. The model owns its
// 1 microsecond RC kernel; acquisition rates and call/checkpoint boundaries never integrate
// pressure. Only accepted ventricular mechanical events contribute flow.
public sealed class VascularPressureSource
{
    public const string KernelId = "VascularPressureRcTrapezoidal1UsQ62@1";
    public const long MeshStepNs = 1_000;
    public const int MaximumEjectionCount = 4096;
    private const long MinimumTimeConstantNs = 100_000_000;
    private const long MaximumTimeConstantNs = 10_000_000_000;
    private readonly RegularPhysiologyPlan _physiology;
    private readonly VascularPressurePlan _plan;
    private readonly long _decayHorizonNs;
    private readonly long _supportNs;
    private readonly long[] _powers;
    private readonly long _morphologyPeriodNs;
    private readonly long _referenceOnsetQ32;
    private readonly long[]? _morphologyTable;

    private VascularPressureSource(RegularPhysiologyPlan physiology, VascularPressurePlan plan)
    {
        if (physiology is null || plan is null) { throw Invalid(); }
        try { _ = RegularPhysiologyTimeline.Start(physiology); }
        catch (ArgumentException) { throw Invalid(); }
        if (plan.UsePrematureBeatPerfusion && !PrematureBeatPerfusion.IsPattern(physiology.ConductionPattern)) { throw Invalid(); }
        if (plan.UseAtrialFibrillationPerfusion && (plan.UsePrematureBeatPerfusion ||
            !AtrialFibrillationReference.IsPattern(physiology.ConductionPattern))) { throw Invalid(); }
        if (plan.UseConductedFlutterPerfusion && (plan.UsePrematureBeatPerfusion || plan.UseAtrialFibrillationPerfusion ||
            !ConductedFlutterPerfusion.Supports(physiology))) { throw Invalid(); }
        if (plan.IllustrateAfSystemicPulseDeficit && !plan.UseAtrialFibrillationPerfusion) { throw Invalid(); }
        Int128 ventricularPeriod = plan.UsePrematureBeatPerfusion ? PrematureBeatPerfusion.MinimumEjectingIntervalNs(physiology.ConductionPattern) : physiology.VentricularPeriodNs;
        Int128 support = (Int128)plan.EjectionDurationNs + 64 * (Int128)plan.TimeConstantNs;
        Int128 selectedPeriod = physiology.VentricularPeriodNs * physiology.MechanicalEveryCycles;
        if (plan.ModelId != VascularPressurePlan.EvidenceId ||
            plan.TransitDelayNs < 0 || plan.EjectionDurationNs <= 0 || plan.EjectionDurationNs > ventricularPeriod ||
            plan.TimeConstantNs is < MinimumTimeConstantNs or > MaximumTimeConstantNs ||
            support + plan.TransitDelayNs > long.MaxValue ||
            plan.AsymptoticPressureCentiMmHg < 0 || plan.InitialPressureCentiMmHg < plan.AsymptoticPressureCentiMmHg ||
            plan.InitialPressureCentiMmHg > short.MaxValue || plan.EjectionEquilibriumCentiMmHg < 0 ||
            (Int128)plan.AsymptoticPressureCentiMmHg + plan.EjectionEquilibriumCentiMmHg > short.MaxValue ||
            (support + selectedPeriod - 1) / selectedPeriod > MaximumEjectionCount)
        { throw Invalid(); }
        _physiology = physiology;
        _plan = plan;
        _decayHorizonNs = 64 * plan.TimeConstantNs;
        _supportNs = (long)support;
        _powers = new long[30]; // 64*10 seconds contains at most 640,000,000 whole mesh steps.
        _powers[0] = (long)FixedPointMath.RoundDivideTiesToEven(
            (2 * (Int128)plan.TimeConstantNs - MeshStepNs) * FixedPointMath.Q62One,
            2 * (Int128)plan.TimeConstantNs + MeshStepNs);
        for (int index = 1; index < _powers.Length; index++)
        { _powers[index] = Multiply(_powers[index - 1], _powers[index - 1]); }
        // The 64*tau truncation removes only values already zero in this Q62
        // kernel. Verify the actual rounded kernel, not a floating-point claim.
        if (WholeStepDecay(_decayHorizonNs / MeshStepNs) != 0) { throw Invalid(); }
        if (plan.Morphology is { } morphology)
        {
            if (morphology.ModelId != VascularPressureMorphologyPlan.EvidenceId ||
                !Enum.IsDefined(morphology.Kind) || morphology.DurationNs <= 0 ||
                morphology.MaximumPulseOverlap is < 1 or > 8 || morphology.DurationNs > support ||
                ((Int128)morphology.DurationNs + ventricularPeriod - 1) / ventricularPeriod > morphology.MaximumPulseOverlap ||
                morphology.PulseHeightCentiMmHg is < 0 or > short.MaxValue ||
                plan.EjectionEquilibriumCentiMmHg == 0)
            { throw Invalid(); }
            // Nominal ventricular RR deliberately excludes the mechanical stride:
            // dropping inputs must not increase the reference volume per beat.
            _morphologyPeriodNs = (long)ventricularPeriod;
            long endDecay = Decay(_morphologyPeriodNs);
            long endInput = EjectionCoefficient(_morphologyPeriodNs);
            _referenceOnsetQ32 = (long)FixedPointMath.RoundDivideTiesToEven(
                (Int128)plan.EjectionEquilibriumCentiMmHg * endInput * FixedPointMath.Q32One,
                FixedPointMath.Q62One - endDecay);
            // Reject effectively empty reference reservoirs instead of dividing
            // by an arbitrarily small pressure and amplifying a startup residue.
            if (_referenceOnsetQ32 < FixedPointMath.Q32One) { throw Invalid(); }
            Int128 firstOnsetNumerator = (Int128)(plan.InitialPressureCentiMmHg - plan.AsymptoticPressureCentiMmHg) *
                FixedPointMath.Q32One * Decay(physiology.VentricularMechanicalOffsetNs);
            Int128 firstOnsetAbove = (firstOnsetNumerator + FixedPointMath.Q62One - 1) / FixedPointMath.Q62One;
            var maximumAbove = Int128.Max(_referenceOnsetQ32, firstOnsetAbove);
            Int128 maximumShape = _referenceOnsetQ32 +
                (Int128)morphology.PulseHeightCentiMmHg * FixedPointMath.Q32One *
                (((Int128)morphology.DurationNs + ventricularPeriod - 1) / ventricularPeriod);
            // A subset of periodic inputs cannot exceed the complete reference
            // multiplied by max(1, first-onset excess/reference onset). The
            // explicit initial pressure decays until the first possible event.
            // Reserve one
            // centi-mmHg for all fixed-point kernel/ratio rounding differences.
            Int128 maximumOutput = (Int128)plan.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One +
                (maximumAbove * maximumShape + _referenceOnsetQ32 - 1) / _referenceOnsetQ32 + FixedPointMath.Q32One;
            if (maximumOutput > short.MaxValue * FixedPointMath.Q32One) { throw Invalid(); }
            IReadOnlyList<long> seed = morphology.Kind == VascularPressureMorphologyKind.Arterial
                ? ArterialPulseTables.Pulse : PulmonaryArteryTables.Pulse;
            _morphologyTable = seed.Select(value => checked(value * morphology.PulseHeightCentiMmHg)).ToArray();
        }
    }

    public static VascularPressureSource Create(RegularPhysiologyPlan physiology, VascularPressurePlan plan) =>
        new(physiology, plan);

    // Returns complete physical pressure as Q32.32 centi-mmHg.
    public long EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (simTimeNs < _physiology.EpochAnchorSimTimeNs)
        { throw new EventWaveformException("VascularPressure.InvalidTime", nameof(simTimeNs)); }
        long sourceTime = Math.Max(_physiology.EpochAnchorSimTimeNs, simTimeNs - _plan.TransitDelayNs);
        Int128 pressure = (Int128)_plan.AsymptoticPressureCentiMmHg * FixedPointMath.Q62One +
            (Int128)(_plan.InitialPressureCentiMmHg - _plan.AsymptoticPressureCentiMmHg) *
            Decay(sourceTime - _physiology.EpochAnchorSimTimeNs);
        // Age == support has zero weight; the remaining integer-ns interval is
        // exactly support wide, so ceil(support/selectedPeriod) bounds its events.
        long begin = (long)Int128.Max(_physiology.EpochAnchorSimTimeNs, (Int128)sourceTime - _supportNs + 1);
        long? morphologyAge = null;
        int morphologyGain = 1000;
        long pulse = 0;
        long morphologyEjectionDuration = _plan.EjectionDurationNs;
        RegularPhysiologyTimeline.VisitVentricularMechanical(_physiology, begin, (Int128)sourceTime + 1,
            MaximumEjectionCount, item =>
            {
                int gain = _plan.UsePrematureBeatPerfusion ? PrematureBeatPerfusion.GainPermille(_physiology.ConductionPattern, item.CycleIndex) :
                    _plan.UseAtrialFibrillationPerfusion ? AtrialFibrillationPerfusion.GainPermille(_physiology.ConductionPattern, item.CycleIndex, _plan.IllustrateAfSystemicPulseDeficit) :
                    _plan.UseConductedFlutterPerfusion ? ConductedFlutterPerfusion.GainPermille(_physiology, item.CycleIndex) :
                        _physiology.SeededRate?.EjectionGainPermille(item.CycleIndex) ?? 1000;
                if (gain == 0) { return; }
                long age = sourceTime - item.SimTimeNs;
                long duration = _plan.UsePrematureBeatPerfusion ? PrematureBeatPerfusion.DurationNs(_physiology.ConductionPattern, item.CycleIndex, _plan.EjectionDurationNs) : _plan.EjectionDurationNs;
                long coefficient = EjectionCoefficient(age, duration);
                pressure += FixedPointMath.RoundDivideTiesToEven((Int128)_plan.EjectionEquilibriumCentiMmHg * coefficient * gain, 1000);
                if (_morphologyTable is not null)
                {
                    long morphologyDuration = _plan.UsePrematureBeatPerfusion ? PrematureBeatPerfusion.DurationNs(_physiology.ConductionPattern, item.CycleIndex, _plan.Morphology!.DurationNs) : _plan.Morphology!.DurationNs;
                    if (age < morphologyDuration)
                    {
                        long contribution = PeriodicLutLinear.Interpolate(_morphologyTable,
                            (ulong)(((UInt128)age << 64) / (ulong)morphologyDuration)).Value;
                        pulse += (long)FixedPointMath.RoundDivideTiesToEven((Int128)contribution * gain, 1000);
                    }
                }
                if (_morphologyTable is not null && age < _morphologyPeriodNs)
                {
                    morphologyAge = age; morphologyGain = gain;
                    morphologyEjectionDuration = duration;
                }
            }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Retain all weighted contributions in Int128/Q62, then round once to
        // Q32. Nonoverlapping inputs and nonnegative decay bound the full source.
        Int128 result = FixedPointMath.RoundDivideTiesToEven(pressure, 1L << 30);
        if (morphologyAge is not null || pulse != 0)
        {
            long elapsed = morphologyAge ?? _morphologyPeriodNs;
            // The contour and its reference must describe the same ejection.
            // An unweighted reference artificially suppresses a weak beat.
            Int128 referenceInput = FixedPointMath.RoundDivideTiesToEven(
                (Int128)_plan.EjectionEquilibriumCentiMmHg * EjectionCoefficient(elapsed, morphologyEjectionDuration) * morphologyGain, 1000);
            long reference = (long)FixedPointMath.RoundDivideTiesToEven(
                (Int128)_referenceOnsetQ32 * Decay(elapsed) +
                referenceInput * FixedPointMath.Q32One,
                FixedPointMath.Q62One);
            // Enforce the nominal minimum against sub-Q32 endpoint rounding.
            reference = Math.Max(_referenceOnsetQ32, reference);
            Int128 asymptote = (Int128)_plan.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One;
            result = asymptote + FixedPointMath.RoundDivideTiesToEven(
                (result - asymptote) * (_referenceOnsetQ32 + pulse), reference);
        }
        if (result < 0 || result > short.MaxValue * FixedPointMath.Q32One)
        { throw new EventWaveformException("VascularPressure.AmplitudeOverflow", nameof(simTimeNs)); }
        return (long)result;
    }

    private long EjectionCoefficient(long ageNs) => EjectionCoefficient(ageNs, _plan.EjectionDurationNs);

    private long EjectionCoefficient(long ageNs, long durationNs) => ageNs < durationNs
        ? FixedPointMath.Q62One - Decay(ageNs)
        : Decay(ageNs - durationNs) - Decay(ageNs);

    private long Decay(long ageNs)
    {
        if (ageNs >= _decayHorizonNs) { return 0; }
        long steps = ageNs / MeshStepNs;
        long left = WholeStepDecay(steps);
        long fraction = ageNs % MeshStepNs;
        if (fraction == 0) { return left; }
        long right = WholeStepDecay(steps + 1);
        return left + (long)FixedPointMath.RoundDivideTiesToEven((Int128)(right - left) * fraction, MeshStepNs);
    }

    private long WholeStepDecay(long steps)
    {
        long value = FixedPointMath.Q62One;
        for (int index = 0; steps > 0; index++, steps >>= 1)
        {
            if ((steps & 1) != 0) { value = Multiply(value, _powers[index]); }
        }
        return value;
    }

    private static long Multiply(long left, long right) =>
        (long)FixedPointMath.RoundDivideTiesToEven((Int128)left * right, FixedPointMath.Q62One);

    private static EventWaveformException Invalid() => new("VascularPressure.InvalidPlan", "plan");
}
