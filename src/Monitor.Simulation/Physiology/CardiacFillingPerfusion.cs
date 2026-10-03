// SPDX-License-Identifier: AGPL-3.0-or-later
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

    public static int GainPermille(RegularPhysiologyPlan plan, ulong cycleIndex)
    {
        if (!Supports(plan)) { throw new ArgumentException("CardiacFilling.UnsupportedPlan", nameof(plan)); }
        long nonFillingNs = plan.SeededRate is not null || plan.ConductionPattern == AvConductionPattern.NarrowComplexSvtIllustration
            ? FillingLimitedEjection.NonFillingDurationNs : NonFillingDurationNs;
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
        if (plan.SeededRate is { } seeded) { return seeded.PrecedingIntervalNs(cycleIndex); }
        return plan.ConductionPattern is AvConductionPattern.VariableAtrialFlutterIllustration or
            AvConductionPattern.SinusArrhythmiaIllustration or AvConductionPattern.SinusArrestIllustration
            ? ReferencePeriodNs : (long)Int128.Min(ReferencePeriodNs, plan.IndependentVentricularPeriodNs ??
                (Int128)plan.HeartPeriodNs * (plan.ConductedBeatsPerGroup > 1 ? 1 : plan.VentricularConductionRatio));
    }

    internal static Int128 MechanicalTimeNs(RegularPhysiologyPlan plan, ulong index)
    {
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
