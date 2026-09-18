// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Explicit teaching strengths, not inferred stroke volumes or a filling model.
public static class PrematureBeatPerfusion
{
    public const string EvidenceId = "PrematureBeatPerfusionIllustration@1";
    public static int GainPermille(AvConductionPattern pattern, ulong ordinal)
    {
        int count = PrematureVentricularReference.BeatsPerGroup(pattern);
        ulong slot = ordinal % (ulong)count;
        bool ectopic = count == 5 ? slot >= 3 : count == 8 ? slot % 4 == 3 : slot == (ulong)count - 1;
        if (ectopic) { return pattern == AvConductionPattern.ShortCoupledRonTPvcIllustration ? 0 : 200; }
        // No preceding ectopic beat at startup; interpolated PVC has no pause.
        bool recovered = pattern != AvConductionPattern.InterpolatedPvcIllustration &&
            ordinal > 0 && (slot == 0 || count == 8 && slot == 4);
        return recovered ? 1000 : 800;
    }

    public static long MinimumEjectingIntervalNs(AvConductionPattern pattern) =>
        pattern == AvConductionPattern.ShortCoupledRonTPvcIllustration ? 800_000_000 : PrematureVentricularReference.MinimumRrNs(pattern);
}
