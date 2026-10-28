// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// The RC kernel advances on a canonical source-time grid with bounded one-second
// reconstruction anchors. Acquisition and checkpoint boundaries never change the
// trajectory. Only accepted ventricular mechanical events contribute flow.
public sealed partial class VascularPressureSource
{
    public const string KernelId = "VascularPressureRcAnchored8MsQ62@2";
    public const long MeshStepNs = 1_000;
    public const int MaximumEjectionCount = 4096;
    private const long MinimumTimeConstantNs = 100_000_000;
    private const long MaximumTimeConstantNs = 10_000_000_000;
    // With morphology the checked output bound below limits the samples, so R*Q may
    // exceed the sample range; this cap keeps every Int128 product below 2^126.
    private const int MaximumMorphologyEjectionEquilibriumCentiMmHg = 1_000_000;
    private const int CalibrationBeatCount = 32;
    private const long CalibrationWindowNs = 40_000_000_000;
    private const long CalibrationPeakStepNs = 16_000_000;
    private const int StrongBeatGainPermille = 900;
    private const int MinimumStrongBeatCount = 4;
    private readonly RegularPhysiologyPlan _physiology;
    private readonly VascularPressurePlan _plan;
    private readonly long _decayHorizonNs;
    private readonly long _supportNs;
    private readonly long[] _powers;
    private readonly long _morphologyPeriodNs;
    private readonly long _referenceOnsetQ32;
    private readonly long[]? _morphologyTable;
    private readonly bool _useLinearStrokeMorphology;
    private readonly bool _useEjectionBoundedMorphology;
    private readonly int _variationProbeGainPermille;
    private readonly VascularPressureCurveSource? _targetCurves;

    private VascularPressureSource(RegularPhysiologyPlan physiology, VascularPressurePlan plan, int variationProbeGainPermille = 1000)
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
        if (plan.UseCardiacFillingPerfusion && (plan.UsePrematureBeatPerfusion || plan.UseAtrialFibrillationPerfusion ||
            plan.UseConductedFlutterPerfusion || !CardiacFillingPerfusion.Supports(physiology))) { throw Invalid(); }
        Int128 ventricularPeriod = plan.UsePrematureBeatPerfusion ? (physiology.RateAdjustment?.MinimumEjectingIntervalNs(physiology) ?? PrematureBeatPerfusion.MinimumEjectingIntervalNs(physiology.ConductionPattern)) : physiology.VentricularPeriodNs;
        if (physiology.CardiacRateClock is { } rateClock) { ventricularPeriod = rateClock.MinimumDuration(ventricularPeriod); }
        Int128 support = (Int128)plan.EjectionDurationNs + 64 * (Int128)plan.TimeConstantNs;
        Int128 selectedPeriod = physiology.MinimumVentricularPeriodNs * physiology.MechanicalEveryCycles;
        if (plan.ModelId != VascularPressurePlan.EvidenceId ||
            plan.TransitDelayNs < 0 || plan.EjectionDurationNs <= 0 || plan.EjectionDurationNs > ventricularPeriod ||
            plan.TimeConstantNs is < MinimumTimeConstantNs or > MaximumTimeConstantNs ||
            support + plan.TransitDelayNs > long.MaxValue ||
            plan.AsymptoticPressureCentiMmHg < 0 || plan.InitialPressureCentiMmHg < plan.AsymptoticPressureCentiMmHg ||
            plan.InitialPressureCentiMmHg > short.MaxValue || plan.EjectionEquilibriumCentiMmHg < 0 ||
            (plan.Morphology is null
                ? (Int128)plan.AsymptoticPressureCentiMmHg + ((Int128)plan.EjectionEquilibriumCentiMmHg *
                    (1000 + (plan.Variation?.AmplitudePermille ?? 0)) + 999) / 1000 > short.MaxValue
                : plan.AsymptoticPressureCentiMmHg > short.MaxValue ||
                    plan.EjectionEquilibriumCentiMmHg > MaximumMorphologyEjectionEquilibriumCentiMmHg) ||
            (support + selectedPeriod - 1) / selectedPeriod > MaximumEjectionCount)
        { throw Invalid(); }
        _physiology = physiology;
        _plan = plan;
        _variationProbeGainPermille = variationProbeGainPermille;
        // Leave unit-gain reference schedules on their established startup
        // contour. Schedules with altered stroke strength need linear shaping.
        var referencePhysiology = PacingReference.WithoutPulseDivision(physiology with { CardiacRateClock = null });
        long atrialLeadNs = referencePhysiology.VentricularMechanicalOffsetNs - referencePhysiology.AtrialMechanicalOffsetNs;
        _useLinearStrokeMorphology = plan.UseAtrialFibrillationPerfusion || plan.UseConductedFlutterPerfusion ||
            plan.UseCardiacFillingPerfusion && (referencePhysiology.RateAdjustment is not null || referencePhysiology.SeededRate is not null ||
                referencePhysiology.VentricularPeriodNs < CardiacFillingPerfusion.ReferencePeriodNs ||
                referencePhysiology.IndependentVentricularPeriodNs is { } independent && independent != (Int128)referencePhysiology.HeartPeriodNs * referencePhysiology.VentricularConductionRatio ||
                referencePhysiology.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
                WenckebachIllustration.GroupSize(referencePhysiology.ConductionPattern) > 0 ||
                atrialLeadNs < CardiacFillingPerfusion.AtrialContractionDurationNs ||
                atrialLeadNs > CardiacFillingPerfusion.ReferencePeriodNs - CardiacFillingPerfusion.NonFillingDurationNs);
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
            if (morphology.ModelId is not (VascularPressureMorphologyPlan.EvidenceId or
                VascularPressureMorphologyPlan.PreviousEvidenceId or VascularPressureMorphologyPlan.LegacyEvidenceId) ||
                !Enum.IsDefined(morphology.Kind) || morphology.DurationNs <= 0 ||
                morphology.MaximumPulseOverlap is < 1 or > 8 || morphology.DurationNs > support ||
                ((Int128)morphology.DurationNs + ventricularPeriod - 1) / ventricularPeriod > morphology.MaximumPulseOverlap ||
                morphology.PulseHeightCentiMmHg is < 0 or > short.MaxValue ||
                plan.EjectionEquilibriumCentiMmHg == 0)
            { throw Invalid(); }
            // Nominal ventricular RR deliberately excludes the mechanical stride
            // and temporary pacing division: dropping inputs must not increase
            // the reference volume per beat. Actual RC inputs still come only
            // from the sparse mechanical event stream.
            _morphologyPeriodNs = physiology.PacingOutput is { PulseDivision: 4 } output
                ? output.PeriodNs / output.PulseDivision : plan.UsePrematureBeatPerfusion
                    ? referencePhysiology.RateAdjustment?.MinimumEjectingIntervalNs(referencePhysiology) ?? PrematureBeatPerfusion.MinimumEjectingIntervalNs(referencePhysiology.ConductionPattern)
                    : (long)referencePhysiology.VentricularPeriodNs;
            _useEjectionBoundedMorphology = _useLinearStrokeMorphology &&
                morphology.ModelId == VascularPressureMorphologyPlan.EvidenceId &&
                morphology.DurationNs < _morphologyPeriodNs;
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
            int maximumGain = plan.UseCardiacFillingPerfusion ? CardiacFillingPerfusion.MaximumGainPermille(physiology) : 1000;
            long boundOnsetQ32 = _referenceOnsetQ32;
            if (physiology.CardiacRateClock is not null)
            {
                long boundPeriodNs = (long)ventricularPeriod;
                boundOnsetQ32 = Math.Max(boundOnsetQ32, (long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)plan.EjectionEquilibriumCentiMmHg * EjectionCoefficient(boundPeriodNs) * FixedPointMath.Q32One,
                    FixedPointMath.Q62One - Decay(boundPeriodNs)));
            }
            Int128 maximumReference = ((Int128)boundOnsetQ32 * maximumGain + 999) / 1000;
            var maximumAbove = Int128.Max(maximumReference, firstOnsetAbove);
            Int128 maximumShape = _referenceOnsetQ32 +
                (Int128)morphology.PulseHeightCentiMmHg * FixedPointMath.Q32One *
                (((Int128)morphology.DurationNs + ventricularPeriod - 1) / ventricularPeriod);
            // A subset of periodic inputs cannot exceed the complete reference
            // scaled by the larger of its bounded filling gain (full atrial
            // contribution for regular pacing) and first-onset excess/reference onset. The
            // explicit initial pressure decays until the first possible event.
            // Reserve one
            // centi-mmHg for all fixed-point kernel/ratio rounding differences.
            // A seeded drift scales every input by at most 1 + amplitude.
            Int128 above = (maximumAbove * maximumShape + _referenceOnsetQ32 - 1) / _referenceOnsetQ32;
            if (_useLinearStrokeMorphology && physiology.Pacing is
                PacingIllustration.VentricularOversensing or PacingIllustration.IntermittentVentricularNoncapture)
            {
                // These authored patterns admit at most two ejections in each
                // four-cycle group. Bound their reservoir by that full-strength
                // schedule, not by four hypothetical ejections. Temporary 4:1
                // output is a sparser subset of the same reference schedule.
                long period = _morphologyPeriodNs;
                Int128 groupInput = (Int128)EjectionCoefficient(4 * period) + EjectionCoefficient(3 * period);
                Int128 groupDenominator = FixedPointMath.Q62One - Decay(4 * period);
                Int128 groupOnset = ((Int128)plan.EjectionEquilibriumCentiMmHg * groupInput * FixedPointMath.Q32One +
                    groupDenominator - 1) / groupDenominator;
                Int128 Peak(long age, bool second)
                {
                    Int128 input = EjectionCoefficient(age);
                    if (second) { input += EjectionCoefficient(age - period); }
                    Int128 numerator = groupOnset * Decay(age) +
                        (Int128)plan.EjectionEquilibriumCentiMmHg * input * FixedPointMath.Q32One;
                    return (numerator + FixedPointMath.Q62One - 1) / FixedPointMath.Q62One;
                }
                Int128 reservoir = Int128.Max(Peak(plan.EjectionDurationNs, false), Peak(period + plan.EjectionDurationNs, true)) +
                    Int128.Max(0, firstOnsetAbove - groupOnset);
                // Linear shaping subtracts a nonnegative ejection input. Keep
                // its positive replacement and contour as separate upper bounds.
                Int128 replacement = ((Int128)_referenceOnsetQ32 * (FixedPointMath.Q62One - Decay(period)) +
                    FixedPointMath.Q62One - 1) / FixedPointMath.Q62One;
                above = Int128.Min(above, reservoir + replacement + maximumShape - _referenceOnsetQ32);
            }
            if (plan.Variation is { } variation) { above = (above * (1000 + variation.AmplitudePermille) + 999) / 1000; }
            Int128 maximumOutput = (Int128)plan.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One + above + FixedPointMath.Q32One;
            if (maximumOutput > short.MaxValue * FixedPointMath.Q32One) { throw Invalid(); }
            IReadOnlyList<long> seed = Contour(morphology);
            _morphologyTable = seed.Select(value => checked(value * morphology.PulseHeightCentiMmHg)).ToArray();
        }
        if (plan.TargetCurves is not null) { _targetCurves = new(physiology, plan); }
    }

    public static VascularPressureSource Create(RegularPhysiologyPlan physiology, VascularPressurePlan plan) =>
        new(physiology, plan);

    // Calibrate against the generated waveform, including the selected rhythm,
    // contour and pulse factor. Compare four-second means with the identical
    // undrifted source at both endpoints of the proposed ejection gain range.
    // Fixed time-indexed probes keep preparation independent of random draws.
    public static int? SolveVariationAmplitude(RegularPhysiologyPlan physiology, VascularPressurePlan plan, int amplitudeCentiMmHg)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amplitudeCentiMmHg);
        if (amplitudeCentiMmHg == 0) { return 0; }
        const long stepNs = 16_000_000;
        const int meanSamples = 250;
        const int probeSamples = 1000;
        var reference = new VascularPressureSource(physiology, plan with { Variation = null });
        long startNs = checked(physiology.EpochAnchorSimTimeNs + 10 * plan.TimeConstantNs);
        long[] samples = Enumerable.Range(0, probeSamples).Select(i => reference.EvaluateAt(checked(startNs + i * stepNs))).ToArray();
        Int128 target = (Int128)amplitudeCentiMmHg * FixedPointMath.Q32One * meanSamples;
        int low = 0;
        int high = SeededVascularVariation.MaximumAmplitudePermille;
        if (Response(high) < target) { return null; }
        while (low + 1 < high)
        {
            int middle = (low + high) / 2;
            if (Response(middle) <= target) { low = middle; }
            else { high = middle; }
        }
        return low == 0 ? null : low;

        Int128 Response(int amplitudePermille)
        {
            Int128 maximum = 0;
            foreach (int gainPermille in new[] { 1000 - amplitudePermille, 1000 + amplitudePermille })
            {
                var source = new VascularPressureSource(physiology, plan with { Variation = null }, gainPermille);
                long[] differences = new long[meanSamples];
                Int128 sum = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    long value;
                    try { value = source.EvaluateAt(checked(startNs + i * stepNs)); }
                    catch (EventWaveformException) { return Int128.MaxValue; }
                    int slot = i % meanSamples;
                    sum -= differences[slot];
                    differences[slot] = value - samples[i];
                    sum += differences[slot];
                    if (i >= meanSamples - 1) { maximum = Int128.Max(maximum, Int128.Abs(sum)); }
                }
            }
            return maximum;
        }
    }

    // Solves R*Q and the pulse height so the mean beat onset and the mean contour peak
    // of this source meet diastolic and systolic targets. Generated pressure is linear
    // in (R*Q, pulse height), so a probe without a pulse and a probe with one determine
    // the 2x2 system; only the per-beat peak is sampled, near the contour maximum.
    // Starts from the nominal steady-state onset. Returns null when the targets
    // cannot be represented by this plan.
    public static (int EjectionEquilibriumCentiMmHg, int PulseHeightCentiMmHg)? SolveTarget(
        RegularPhysiologyPlan physiology, VascularPressurePlan plan, int systolicCentiMmHg, int diastolicCentiMmHg)
    {
        if (plan?.Morphology is not { } morphology) { throw Invalid(); }
        int pulseCentiMmHg = systolicCentiMmHg - diastolicCentiMmHg;
        int onsetCentiMmHg = diastolicCentiMmHg - plan.AsymptoticPressureCentiMmHg;
        if (pulseCentiMmHg <= 0 || onsetCentiMmHg <= 0) { return null; }
        long nominal = EjectionEquilibriumForOnset(physiology, plan, onsetCentiMmHg);
        if (nominal is <= 0 or > MaximumMorphologyEjectionEquilibriumCentiMmHg) { return null; }
        // Calibrate the level without the seeded drift, so the targets stay its centre.
        var withoutPulse = plan with
        {
            Variation = null,
            EjectionEquilibriumCentiMmHg = (int)nominal,
            InitialPressureCentiMmHg = plan.AsymptoticPressureCentiMmHg,
            Morphology = morphology with { PulseHeightCentiMmHg = 0 }
        };
        var withPulse = withoutPulse with { Morphology = morphology with { PulseHeightCentiMmHg = pulseCentiMmHg } };
        (Int128 OnsetsQ32, Int128 PeaksQ32, int Count) probe, pulsed;
        try
        {
            probe = new VascularPressureSource(physiology, withoutPulse).SampleBeats();
            pulsed = new VascularPressureSource(physiology, withPulse).SampleBeats();
        }
        catch (EventWaveformException) { return null; }
        if (probe.Count == 0 || probe.Count != pulsed.Count) { return null; }
        Int128 floor = (Int128)probe.Count * plan.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One;
        Int128 onsetA = probe.OnsetsQ32 - floor;
        Int128 peakA = probe.PeaksQ32 - floor;
        Int128 onsetGain = pulsed.OnsetsQ32 - probe.OnsetsQ32;
        Int128 peakGain = pulsed.PeaksQ32 - probe.PeaksQ32;
        Int128 onsetTarget = (Int128)probe.Count * onsetCentiMmHg * FixedPointMath.Q32One;
        Int128 peakTarget = (Int128)probe.Count * (systolicCentiMmHg - plan.AsymptoticPressureCentiMmHg) * FixedPointMath.Q32One;
        Int128 determinant = onsetA * peakGain - peakA * onsetGain;
        Int128 scaleNumerator = onsetTarget * peakGain - peakTarget * onsetGain;
        Int128 pulseNumerator = onsetA * peakTarget - peakA * onsetTarget;
        if (determinant <= 0 || scaleNumerator <= 0 || pulseNumerator < 0) { return null; }
        Int128 equilibrium = FixedPointMath.RoundDivideTiesToEven(nominal * scaleNumerator, determinant);
        Int128 height = FixedPointMath.RoundDivideTiesToEven(pulseCentiMmHg * pulseNumerator, determinant);
        if (equilibrium <= 0 || equilibrium > MaximumMorphologyEjectionEquilibriumCentiMmHg || height > short.MaxValue) { return null; }
        return ((int)equilibrium, (int)height);
    }

    // Sums the pressure at each calibration beat onset and near its contour peak over a
    // window that starts ten time constants after the epoch.
    private (Int128 OnsetsQ32, Int128 PeaksQ32, int Count) SampleBeats()
    {
        var morphology = _plan.Morphology!;
        // The probe without a pulse has an all-zero table; locate the peak on the seed contour.
        IReadOnlyList<long> contour = Contour(morphology);
        int peakIndex = 0;
        for (int index = 1; index < contour.Count; index++)
        {
            if (contour[index] > contour[peakIndex]) { peakIndex = index; }
        }
        long windowStart = checked(_physiology.EpochAnchorSimTimeNs + 10 * _plan.TimeConstantNs);
        var accepted = new List<(PhysiologyCycleEvent Beat, int GainPermille)>();
        RegularPhysiologyTimeline.VisitVentricularMechanical(_physiology, windowStart, (Int128)windowStart + CalibrationWindowNs,
            MaximumEjectionCount, item =>
            {
                int gain = GainPermille(item);
                if (gain != 0) { accepted.Add((item, gain)); }
            }, CancellationToken.None);
        // Weak premature or filling-limited beats are often below the pulse detector's
        // threshold; calibrate on full-strength beats whenever a rhythm has enough of them.
        // With ejection-bounded baselines the separated weak pulses also have
        // stable troughs, so include them in the displayed pressure average.
        var strong = accepted.Where(item => item.GainPermille >= StrongBeatGainPermille).ToList();
        var beats = (!_useEjectionBoundedMorphology && strong.Count >= MinimumStrongBeatCount ? strong : accepted)
            .Take(CalibrationBeatCount).Select(item => item.Beat).ToList();
        Int128 onsets = 0;
        Int128 peaks = 0;
        foreach (var beat in beats)
        {
            long onset = checked(beat.SimTimeNs + _plan.TransitDelayNs);
            long durationNs = _plan.UsePrematureBeatPerfusion
                ? PrematureBeatPerfusion.DurationNs(_physiology.ConductionPattern, beat.CycleIndex, morphology.DurationNs)
                : morphology.DurationNs;
            long peakTime = onset + (long)((Int128)durationNs * peakIndex / contour.Count);
            onsets += EvaluateAt(onset);
            long peak = EvaluateAt(peakTime);
            foreach (long offset in new[] { -CalibrationPeakStepNs, CalibrationPeakStepNs })
            {
                if (peakTime + offset > onset) { peak = Math.Max(peak, EvaluateAt(peakTime + offset)); }
            }
            peaks += peak;
        }
        return (onsets, peaks, beats.Count);
    }

    // Checkpoints identify the contour through the model version. Retain the
    // frozen legacy tables so restoring an old pending sample stays exact.
    private static IReadOnlyList<long> Contour(VascularPressureMorphologyPlan morphology) =>
        (morphology.ModelId, morphology.Kind) switch
        {
            (VascularPressureMorphologyPlan.LegacyEvidenceId, VascularPressureMorphologyKind.Arterial) => ArterialPulseTables.Pulse,
            (VascularPressureMorphologyPlan.LegacyEvidenceId, VascularPressureMorphologyKind.PulmonaryArtery) => PulmonaryArteryTables.Pulse,
            (_, VascularPressureMorphologyKind.Arterial) => AuthoredHemodynamicContours.Arterial,
            _ => AuthoredHemodynamicContours.PulmonaryArtery,
        };

    // Inverts the reference onset S0 = E * input(T) / (1 - decay(T)) with the same kernel.
    private static long EjectionEquilibriumForOnset(RegularPhysiologyPlan physiology, VascularPressurePlan plan,
        int onsetAboveAsymptoteCentiMmHg)
    {
        var kernel = new VascularPressureSource(physiology, plan with
        {
            Morphology = null,
            EjectionEquilibriumCentiMmHg = 0,
            InitialPressureCentiMmHg = plan.AsymptoticPressureCentiMmHg
        });
        long period = plan.UsePrematureBeatPerfusion
            ? (physiology.RateAdjustment?.MinimumEjectingIntervalNs(physiology) ?? PrematureBeatPerfusion.MinimumEjectingIntervalNs(physiology.ConductionPattern))
            : (long)physiology.VentricularPeriodNs;
        long input = kernel.EjectionCoefficient(period);
        if (input <= 0) { throw Invalid(); }
        return (long)FixedPointMath.RoundDivideTiesToEven(
            (Int128)onsetAboveAsymptoteCentiMmHg * (FixedPointMath.Q62One - kernel.Decay(period)), input);
    }

    // Returns complete physical pressure as Q32.32 centi-mmHg.
    public long EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default) =>
        EvaluateIntervalAt(simTimeNs, _physiology.EpochAnchorSimTimeNs, long.MaxValue, true, cancellationToken);

    internal long EvaluateIntervalAt(long simTimeNs, long fromEventTimeNs, long toExclusiveEventTimeNs,
        bool includeInitialPressure, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (simTimeNs < _physiology.EpochAnchorSimTimeNs)
        { throw new EventWaveformException("VascularPressure.InvalidTime", nameof(simTimeNs)); }
        if (_targetCurves is not null)
        { return _targetCurves.EvaluateIntervalAt(simTimeNs, fromEventTimeNs, toExclusiveEventTimeNs, includeInitialPressure, cancellationToken); }
        long sourceTime = (long)Int128.Max(_physiology.EpochAnchorSimTimeNs, (Int128)simTimeNs - _plan.TransitDelayNs);
        var block = GetFlowBlock(sourceTime, fromEventTimeNs, toExclusiveEventTimeNs, includeInitialPressure, cancellationToken);
        long reservoir = block.ReservoirAt(sourceTime, this);
        return ApplyMorphology(sourceTime, reservoir, block.Beats, cancellationToken);
    }

    private long ApplyMorphology(long sourceTimeNs, long reservoirQ32, FlowBeat[] beats, CancellationToken cancellationToken)
    {
        long? morphologyAge = null;
        int morphologyGain = 1000;
        long morphologyEjectionDuration = _plan.EjectionDurationNs;
        long pulse = 0;
        if (_morphologyTable is not null)
        {
            foreach (var beat in beats)
            {
                long age = sourceTimeNs - beat.TimeNs;
                if (age < 0) { break; }
                if (age < beat.MorphologyDurationNs)
                {
                    long contribution = PeriodicLutLinear.Interpolate(_morphologyTable,
                        (ulong)(((UInt128)age << 64) / (ulong)beat.MorphologyDurationNs)).Value;
                    pulse += (long)FixedPointMath.RoundDivideTiesToEven((Int128)contribution * beat.GainPermille, 1000);
                }
                if (age < _morphologyPeriodNs)
                {
                    morphologyAge = age;
                    morphologyGain = beat.GainPermille;
                    morphologyEjectionDuration = beat.EjectionDurationNs;
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        Int128 result = reservoirQ32;
        if (_useLinearStrokeMorphology && _morphologyTable is not null)
        {
            // Replace this beat's reference RC excursion with its weighted
            // contour. Multiplying an already weak reservoir by another weighted
            // pulse would apply the stroke reduction twice and erase SVT pulses.
            // Residual pressure from preceding beats continues to decay normally.
            result += pulse;
            if (morphologyAge is { } age)
            {
                Int128 replacement = (Int128)_referenceOnsetQ32 * (FixedPointMath.Q62One - Decay(age));
                Int128 input = (Int128)_plan.EjectionEquilibriumCentiMmHg *
                    EjectionCoefficient(age, morphologyEjectionDuration) * FixedPointMath.Q32One;
                result += FixedPointMath.RoundDivideTiesToEven((replacement - input) * morphologyGain,
                    (Int128)1000 * FixedPointMath.Q62One);
                if (_useEjectionBoundedMorphology)
                {
                    // The shaped baseline is S0*gain + residual*decay(age).
                    // Settle that residual to this beat's nominal endpoint during
                    // ejection, rather than letting it pull the completed contour
                    // up or down throughout diastole. The endpoint still joins
                    // the original RC history exactly; no volume is added.
                    Int128 asymptote = (Int128)_plan.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One;
                    Int128 onset = FixedPointMath.RoundDivideTiesToEven((Int128)_referenceOnsetQ32 * morphologyGain, 1000);
                    Int128 residual = result - pulse - asymptote - onset;
                    long referenceAge = EjectionBoundedReferenceAge(age, morphologyEjectionDuration);
                    result = asymptote + onset + pulse + FixedPointMath.RoundShiftTiesToEven(
                        residual * Decay(referenceAge - age), 62);
                }
            }
        }
        else if (morphologyAge is not null || pulse != 0)
        {
            long elapsed = morphologyAge ?? _morphologyPeriodNs;
            // The contour and its reference must describe the same ejection.
            // An unweighted reference artificially suppresses a weak beat.
            Int128 referenceInput = FixedPointMath.RoundDivideTiesToEven(
                (Int128)_plan.EjectionEquilibriumCentiMmHg * EjectionCoefficient(elapsed, morphologyEjectionDuration) * morphologyGain, 1000);
            long reference = (long)FixedPointMath.RoundShiftTiesToEven(
                (Int128)_referenceOnsetQ32 * Decay(elapsed) +
                referenceInput * FixedPointMath.Q32One, 62);
            // Enforce the nominal minimum against sub-Q32 endpoint rounding.
            reference = Math.Max(_referenceOnsetQ32, reference);
            Int128 asymptote = (Int128)_plan.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One;
            result = asymptote + FixedPointMath.RoundDivideTiesToEven(
                (result - asymptote) * (_referenceOnsetQ32 + pulse), reference);
        }
        if (result < 0 || result > short.MaxValue * FixedPointMath.Q32One)
        { throw new EventWaveformException("VascularPressure.AmplitudeOverflow", nameof(sourceTimeNs)); }
        return (long)result;
    }

    // Monotone Hermite time mapping: slope one at onset, zero at ejection
    // end. It reaches the nominal cycle endpoint before the contour runs out,
    // avoiding an abrupt baseline slope change at the end of ejection.
    private long EjectionBoundedReferenceAge(long ageNs, long ejectionDurationNs)
    {
        if (ageNs >= ejectionDurationNs) { return _morphologyPeriodNs; }
        long position = (long)FixedPointMath.RoundDivideTiesToEven((Int128)ageNs * FixedPointMath.Q32One, ejectionDurationNs);
        long square = (long)FixedPointMath.RoundShiftTiesToEven((Int128)position * position, 32);
        long cube = (long)FixedPointMath.RoundShiftTiesToEven((Int128)square * position, 32);
        long mappedAge = (long)FixedPointMath.RoundShiftTiesToEven(
            (Int128)(3 * square - 2 * cube) * _morphologyPeriodNs +
            (Int128)(position - 2 * square + cube) * ejectionDurationNs, 32);
        return Math.Clamp(mappedAge, ageNs, _morphologyPeriodNs);
    }

    // The rhythm's stroke response, scaled by the optional seeded drift at the beat's time.
    private int GainPermille(PhysiologyCycleEvent beat)
    {
        int gain = RhythmGainPermille(beat.CycleIndex);
        if (_variationProbeGainPermille != 1000)
        { gain = (int)FixedPointMath.RoundDivideTiesToEven((Int128)gain * _variationProbeGainPermille, 1000); }
        return _plan.Variation is { } variation && gain != 0
            ? (int)FixedPointMath.RoundDivideTiesToEven((Int128)gain * variation.GainPermille(beat.SimTimeNs), 1000) : gain;
    }

    private int RhythmGainPermille(ulong cycleIndex) =>
        _plan.UsePrematureBeatPerfusion ? PrematureBeatPerfusion.GainPermille(_physiology.ConductionPattern, cycleIndex) :
        _plan.UseAtrialFibrillationPerfusion ? AtrialFibrillationPerfusion.GainPermille(_physiology, cycleIndex, _plan.IllustrateAfSystemicPulseDeficit) :
        _plan.UseConductedFlutterPerfusion ? ConductedFlutterPerfusion.GainPermille(_physiology, cycleIndex) :
        _plan.UseCardiacFillingPerfusion ? CardiacFillingPerfusion.GainPermille(_physiology, cycleIndex) :
            _physiology.SeededRate?.EjectionGainPermille(cycleIndex) ?? 1000;

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
        (long)FixedPointMath.RoundShiftTiesToEven((Int128)left * right, 62);

    private static EventWaveformException Invalid() => new("VascularPressure.InvalidPlan", "plan");
}
