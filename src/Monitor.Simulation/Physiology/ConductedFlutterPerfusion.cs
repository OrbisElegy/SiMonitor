// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Reuses the authored filling approximation, not a calibrated flutter pump.
public static class ConductedFlutterPerfusion
{
    public static bool Supports(RegularPhysiologyPlan plan) =>
        AtrialFlutterReference.IsPattern(plan.ConductionPattern) && plan.VentricularConductionRatio is >= 2 and <= 4;

    public static int GainPermille(RegularPhysiologyPlan plan, ulong ordinal)
    {
        if (!Supports(plan)) { throw new ArgumentException("FlutterPerfusion.UnsupportedPlan", nameof(plan)); }
        // Rapid organized atrial activity retains a bounded transport component;
        // it is distinct from both normal atrial systole and fibrillation.
        return CardiacFillingPerfusion.StrokeVolumePermille(
            CardiacFillingPerfusion.PrecedingIntervalNs(plan, ordinal),
            AtrialFlutterMechanics.EffectiveAtrialDurationNs(plan, ordinal, FillingLimitedEjection.NonFillingDurationNs),
            FillingLimitedEjection.NonFillingDurationNs);
    }
}
