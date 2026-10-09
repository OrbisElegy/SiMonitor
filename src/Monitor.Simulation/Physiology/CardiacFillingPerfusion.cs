// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored, bounded stroke-volume approximation. The accepted mechanical
// schedule supplies RR and atrial phase; no elapsed-time integration or BP target.
public static class CardiacFillingPerfusion
{
    public const string EvidenceId = "CardiacFillingPerfusionIllustration@2";
    public const long ReferencePeriodNs = 800_000_000;
    public const long NonFillingDurationNs = 300_000_000;
    public const long FillingConstantNs = 200_000_000;
    public const long AtrialContractionDurationNs = 120_000_000;
    public const int AtrialContributionPermille = 250;

    public static bool Supports(RegularPhysiologyPlan plan) =>
        !PrematureBeatPerfusion.IsPattern(plan.ConductionPattern) &&
        !AtrialFibrillationReference.IsPattern(plan.ConductionPattern) &&
        !AtrialFlutterReference.IsPattern(plan.ConductionPattern) &&
        !VentricularDisorganizationReference.IsPattern(plan.ConductionPattern);

    // Saturating passive filling, with a separate atrial contribution admitted
    // only while the ventricle can fill. Constants are teaching assumptions,
    // not fitted patient parameters. Long pauses cannot exceed reference volume.
    public static int StrokeVolumePermille(long periodNs, long effectiveAtrialDurationNs, long nonFillingDurationNs = NonFillingDurationNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodNs);
        if (effectiveAtrialDurationNs is < 0 or > AtrialContractionDurationNs)
        { throw new ArgumentOutOfRangeException(nameof(effectiveAtrialDurationNs)); }
        if (nonFillingDurationNs is < 0 or >= ReferencePeriodNs)
        { throw new ArgumentOutOfRangeException(nameof(nonFillingDurationNs)); }
        long fillingNs = Math.Max(0, Math.Min(periodNs, ReferencePeriodNs) - nonFillingDurationNs);
        long referenceFillingNs = ReferencePeriodNs - nonFillingDurationNs;
        Int128 atrialWeight = (1000 - AtrialContributionPermille) * (Int128)AtrialContractionDurationNs +
            AtrialContributionPermille * (Int128)effectiveAtrialDurationNs;
        return (int)FixedPointMath.RoundDivideTiesToEven(
            fillingNs * (referenceFillingNs + FillingConstantNs) * atrialWeight,
            (Int128)referenceFillingNs * (fillingNs + FillingConstantNs) * AtrialContractionDurationNs);
    }

    private static readonly ConditionalWeakTable<RegularPhysiologyPlan, BeatGainCache> Gains = new();

    // Every pressure/pleth sample in the finite response window reuses the same
    // immutable beat's filling result. Cache at most the pressure event budget,
    // with weak plan ownership; eviction only causes exact recomputation.
    public static int GainPermille(RegularPhysiologyPlan plan, ulong cycleIndex) =>
        Gains.GetValue(plan, static _ => new()).Get(plan, cycleIndex);

    private sealed class BeatGainCache
    {
        private sealed record Entry(ulong CycleIndex, int GainPermille);
        private readonly Entry?[] _entries = new Entry[VascularPressureSource.MaximumEjectionCount];

        internal int Get(RegularPhysiologyPlan plan, ulong cycleIndex)
        {
            int slot = (int)(cycleIndex % (ulong)_entries.Length);
            var previous = Volatile.Read(ref _entries[slot]);
            if (previous is not null && previous.CycleIndex == cycleIndex) { return previous.GainPermille; }
            int gain = CalculateGainPermille(plan, cycleIndex);
            Volatile.Write(ref _entries[slot], new(cycleIndex, gain));
            return gain;
        }
    }

    private static int CalculateGainPermille(RegularPhysiologyPlan plan, ulong cycleIndex)
    {
        if (!Supports(plan)) { throw new ArgumentException("CardiacFilling.UnsupportedPlan", nameof(plan)); }
        long nonFillingNs = plan.SeededRate is not null || plan.ConductionPattern == AvConductionPattern.NarrowComplexSvtIllustration
            ? FillingLimitedEjection.NonFillingDurationNs : NonFillingDurationNs;
        if (plan.RateAdjustment is not null || plan.Pacing is not null)
        {
            Int128 adjustedCurrentNs = MechanicalTimeNs(plan, cycleIndex);
            long adjustedIntervalNs = PrecedingIntervalNs(plan, cycleIndex);
            Int128 adjustedStartNs = adjustedCurrentNs - Math.Min(adjustedIntervalNs, ReferencePeriodNs) + nonFillingNs;
            long contributionNs = 0;
            if (adjustedStartNs < adjustedCurrentNs && plan.CardiacActivity == CardiacActivity.AtrialAndVentricular)
            {
                Int128 originNs = plan.EpochAnchorSimTimeNs;
                RegularPhysiologyTimeline.VisitCycles(plan, PhysiologyCycleEventKind.AtrialMechanical,
                    plan.HeartPeriodNs, plan.AtrialMechanicalOffsetNs,
                    (long)Int128.Max(originNs, originNs + adjustedStartNs - AtrialContractionDurationNs), originNs + adjustedCurrentNs, 100,
                    item => contributionNs += (long)Int128.Max(0, Int128.Min(adjustedCurrentNs, item.SimTimeNs - originNs + AtrialContractionDurationNs) -
                        Int128.Max(adjustedStartNs, item.SimTimeNs - originNs)), CancellationToken.None);
            }
            return StrokeVolumePermille(adjustedIntervalNs, Math.Min(contributionNs, AtrialContractionDurationNs), nonFillingNs);
        }
        Int128 currentNs = MechanicalTimeNs(plan, cycleIndex);
        long periodNs = PrecedingIntervalNs(plan, cycleIndex);
        // Bound the late filling window as well as the passive volume. Extra
        // atrial contractions during a long escape interval cannot add volumes.
        Int128 fillingStartNs = currentNs - Math.Min(periodNs, ReferencePeriodNs) + nonFillingNs;
        long atrialDurationNs = 0;
        if (plan.CardiacActivity == CardiacActivity.AtrialAndVentricular && fillingStartNs < currentNs)
        {
            bool sharedSchedule = plan.SeededRate is not null || plan.ConductionPattern is
                AvConductionPattern.SinusArrhythmiaIllustration or AvConductionPattern.SinusArrestIllustration;
            Int128 latestAtrialIndex = sharedSchedule ? cycleIndex : (currentNs - plan.AtrialMechanicalOffsetNs) / plan.HeartPeriodNs;
            // Two recent contractions suffice: if more fit in this window,
            // a complete contraction already saturates the atrial contribution.
            for (int offset = 0; offset < 2; offset++)
            {
                Int128 atrialIndex = latestAtrialIndex - offset;
                if (atrialIndex < 0) { continue; }
                Int128 atrialStartNs = (sharedSchedule ? CycleStartNs(plan, (ulong)atrialIndex) : atrialIndex * plan.HeartPeriodNs) +
                    plan.AtrialMechanicalOffsetNs;
                atrialDurationNs += (long)Int128.Max(0,
                    Int128.Min(currentNs, atrialStartNs + AtrialContractionDurationNs) -
                    Int128.Max(fillingStartNs, atrialStartNs));
            }
        }
        return StrokeVolumePermille(periodNs, Math.Min(atrialDurationNs, AtrialContractionDurationNs), nonFillingNs);
    }

    internal static long PrecedingIntervalNs(RegularPhysiologyPlan plan, ulong cycleIndex)
    {
        ulong stride = (ulong)plan.MechanicalEveryCycles;
        ulong? previous = cycleIndex >= stride ? cycleIndex - stride : null;
        if (previous is { } candidate && plan.MechanicalAfterCycles is { } limit &&
            plan.MechanicalDurationCycles is { } duration && candidate >= limit &&
            (Int128)candidate < (Int128)limit + duration)
        { previous = limit == 0 ? null : (limit - 1) / stride * stride; }
        if (previous is { } grouped && plan.ConductedBeatsPerGroup > 1)
        {
            ulong slot = grouped % (ulong)plan.VentricularConductionRatio;
            if (slot >= (ulong)plan.ConductedBeatsPerGroup)
            { previous = grouped - slot + (ulong)plan.ConductedBeatsPerGroup - 1; }
        }
        if (previous is { } index)
        {
            return (long)Int128.Min(ReferencePeriodNs, MechanicalTimeNs(plan, cycleIndex) - MechanicalTimeNs(plan, index));
        }
        if (cycleIndex > 0) { return ReferencePeriodNs; }
        if (plan.RateAdjustment is { } adjustment)
        { return (long)Int128.Min(ReferencePeriodNs, adjustment.Map(plan, false, plan.IndependentVentricularPeriodNs ?? plan.HeartPeriodNs)); }
        if (plan.SeededRate is { } seeded) { return seeded.PrecedingIntervalNs(cycleIndex); }
        return plan.ConductionPattern is AvConductionPattern.VariableAtrialFlutterIllustration or
            AvConductionPattern.SinusArrhythmiaIllustration or AvConductionPattern.SinusArrestIllustration
            ? ReferencePeriodNs : (long)Int128.Min(ReferencePeriodNs, plan.IndependentVentricularPeriodNs ??
                (Int128)plan.HeartPeriodNs * (plan.ConductedBeatsPerGroup > 1 ? 1 : plan.VentricularConductionRatio));
    }

    internal static Int128 MechanicalTimeNs(RegularPhysiologyPlan plan, ulong index)
    {
        if (plan.Pacing is { } pacing)
        {
            Int128 cycle = pacing is PacingIllustration.VentricularOversensing or PacingIllustration.IntermittentVentricularNoncapture
                ? (Int128)(index / 2) * 4 + index % 2 : index;
            return cycle * (plan.PacingOutput?.PeriodNs ?? 1_000_000_000) + plan.VentricularMechanicalOffsetNs;
        }
        if (plan.RateAdjustment is { } adjustment)
        {
            Int128 reference = MechanicalTimeNs(plan with { RateAdjustment = null }, index) - plan.VentricularMechanicalOffsetNs;
            return adjustment.Map(plan, false, reference) + plan.VentricularMechanicalOffsetNs;
        }
        Int128 cycleNs;
        if (plan.SeededRate is not null || plan.ConductionPattern is AvConductionPattern.SinusArrhythmiaIllustration or
            AvConductionPattern.SinusArrestIllustration or AvConductionPattern.VariableAtrialFlutterIllustration)
        { cycleNs = CycleStartNs(plan, index); }
        else
        {
            cycleNs = (Int128)index * (plan.IndependentVentricularPeriodNs ??
                (Int128)plan.HeartPeriodNs * (plan.ConductedBeatsPerGroup > 1 ? 1 : plan.VentricularConductionRatio));
            int groupSize = WenckebachIllustration.GroupSize(plan.ConductionPattern);
            if (groupSize > 0) { cycleNs += WenckebachIllustration.ExtraDelayNs((int)(index % (ulong)groupSize)); }
            if (plan.ConductionPattern == AvConductionPattern.VtCaptureIllustration)
            { cycleNs -= VtCaptureSchedule.AdvanceForCycleNs(index); }
        }
        return cycleNs + plan.VentricularMechanicalOffsetNs;
    }

    private static Int128 CycleStartNs(RegularPhysiologyPlan plan, ulong index)
    {
        if (plan.SeededRate is { } rate)
        { return (Int128)(index / (ulong)rate.Slots.Length) * rate.PeriodNs * rate.Slots.Length + rate.Slots[index % (ulong)rate.Slots.Length]; }
        if (plan.RhythmSchedule is { } rhythm) { return rhythm.CycleStartNs(plan.ConductionPattern, index); }
        ReadOnlySpan<long> slots = plan.ConductionPattern switch
        {
            AvConductionPattern.SinusArrhythmiaIllustration => SinusArrhythmiaReference.CycleOffsetsNs,
            AvConductionPattern.SinusArrestIllustration => SinusArrestReference.CycleOffsetsNs,
            _ => AtrialFlutterReference.VariableCycleOffsetsNs
        };
        long groupNs = plan.ConductionPattern switch
        {
            AvConductionPattern.SinusArrhythmiaIllustration => SinusArrhythmiaReference.GroupDurationNs,
            AvConductionPattern.SinusArrestIllustration => SinusArrestReference.GroupDurationNs,
            _ => AtrialFlutterReference.VariableGroupDurationNs
        };
        return (Int128)(index / (ulong)slots.Length) * groupNs + slots[(int)(index % (ulong)slots.Length)];
    }

}
