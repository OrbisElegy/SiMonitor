// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Explicit teaching strengths, not inferred stroke volumes or a filling model.
public static class PrematureBeatPerfusion
{
    public const string EvidenceId = "PrematureBeatPerfusionIllustration@4";
    public static bool IsPattern(AvConductionPattern pattern) => PrematureVentricularReference.IsPattern(pattern) ||
        PrematureAtrialReference.IsPattern(pattern) || PrematureJunctionalReference.IsPattern(pattern);

    public static int GainPermille(AvConductionPattern pattern, ulong ordinal)
    {
        bool supraventricular = PrematureAtrialReference.IsPattern(pattern) || PrematureJunctionalReference.IsPattern(pattern);
        int count = supraventricular ? 4 : PrematureVentricularReference.BeatsPerGroup(pattern);
        ulong slot = ordinal % (ulong)count;
        bool ectopic = count == 5 ? slot >= 3 : count == 8 ? slot % 4 == 3 : slot == (ulong)count - 1;
        if (ectopic) { return pattern is AvConductionPattern.ShortCoupledRonTPvcIllustration or AvConductionPattern.BlockedPrematureAtrialIllustration ? 0 : supraventricular ? 400 : 200; }
        // No preceding ectopic beat at startup; interpolated PVC has no pause.
        bool recovered = pattern != AvConductionPattern.InterpolatedPvcIllustration &&
            ordinal > 0 && (slot == 0 || count == 8 && slot == 4);
        return recovered ? 1000 : 800;
    }

    // Authored duration ratio, independently applied to each channel's baseline.
    // It is not a rule mapping clinical stroke volume to pulse width.
    public static long DurationNs(AvConductionPattern pattern, ulong ordinal, long baselineNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baselineNs);
        bool supraventricular = PrematureAtrialReference.IsPattern(pattern) || PrematureJunctionalReference.IsPattern(pattern);
        int count = supraventricular ? 4 : PrematureVentricularReference.BeatsPerGroup(pattern);
        ulong slot = ordinal % (ulong)count;
        bool ectopic = count == 5 ? slot >= 3 : count == 8 ? slot % 4 == 3 : slot == (ulong)count - 1;
        return ectopic ? (long)Int128.Max(1, (Int128)baselineNs * 3 / 4) : baselineNs;
    }

    public static long MinimumEjectingIntervalNs(AvConductionPattern pattern) =>
        pattern is AvConductionPattern.ShortCoupledRonTPvcIllustration or AvConductionPattern.BlockedPrematureAtrialIllustration ? 800_000_000 :
        PrematureAtrialReference.IsPattern(pattern) || PrematureJunctionalReference.IsPattern(pattern) ? 500_000_000 : PrematureVentricularReference.MinimumRrNs(pattern);
}
