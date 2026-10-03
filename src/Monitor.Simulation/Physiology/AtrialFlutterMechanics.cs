// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Flutter retains organized but abnormal mechanical activity. These short,
// reduced transport pulses are authored, not measured atrial contractility.
public static class AtrialFlutterMechanics
{
    public const string EvidenceId = "AtrialFlutterMechanicsIllustration@1";
    public const long ContractionDurationNs = 80_000_000;
    public const int TransportPermille = 400;

    public static long EffectiveAtrialDurationNs(RegularPhysiologyPlan plan, ulong ordinal, long nonFillingDurationNs)
    {
        if (!AtrialFlutterReference.IsPattern(plan.ConductionPattern))
        { throw new ArgumentException("FlutterMechanics.UnsupportedPlan", nameof(plan)); }
        Int128 currentNs = CardiacFillingPerfusion.MechanicalTimeNs(plan, ordinal);
        long intervalNs = CardiacFillingPerfusion.PrecedingIntervalNs(plan, ordinal);
        Int128 startNs = currentNs - intervalNs + nonFillingDurationNs;
        Int128 atrialIndex = (currentNs - plan.AtrialMechanicalOffsetNs) / plan.HeartPeriodNs;
        long overlapNs = 0;
        // At most four 200ms atrial periods fit the capped 800ms history.
        for (int offset = 0; offset < 4; offset++)
        {
            if (atrialIndex - offset < 0) { continue; }
            Int128 atrialNs = (atrialIndex - offset) * plan.HeartPeriodNs + plan.AtrialMechanicalOffsetNs;
            overlapNs += (long)Int128.Max(0, Int128.Min(currentNs, atrialNs + ContractionDurationNs) - Int128.Max(startNs, atrialNs));
        }
        // Multiple flutter contractions cannot stack into several normal kicks.
        return (long)FixedPointMath.RoundDivideTiesToEven(
            (Int128)Math.Min(overlapNs, ContractionDurationNs) * CardiacFillingPerfusion.AtrialContractionDurationNs * TransportPermille,
            ContractionDurationNs * 1000);
    }
}
