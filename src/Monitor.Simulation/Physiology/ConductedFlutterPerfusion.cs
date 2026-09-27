// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Reuses the authored filling approximation, not a calibrated flutter pump.
public static class ConductedFlutterPerfusion
{
    private static readonly int TwoToOneGain = FillingLimitedEjection.StrokeVolumePermille(400_000_000);
    private static readonly int ThreeToOneGain = FillingLimitedEjection.StrokeVolumePermille(600_000_000);
    public static bool Supports(RegularPhysiologyPlan plan) =>
        AtrialFlutterReference.IsPattern(plan.ConductionPattern) && plan.VentricularConductionRatio is >= 2 and <= 4;

    public static int GainPermille(RegularPhysiologyPlan plan, ulong ordinal)
    {
        if (!Supports(plan)) { throw new ArgumentException("FlutterPerfusion.UnsupportedPlan", nameof(plan)); }
        // Variable QRS slots are0/400/1000ms in a1800ms group. Slot0 uses
        // the preceding800ms interval, including the established-rhythm startup.
        int ratio = plan.ConductionPattern == AvConductionPattern.VariableAtrialFlutterIllustration
            ? (ordinal % 3) switch { 0 => 4, 1 => 2, _ => 3 }
            : plan.VentricularConductionRatio;
        return ratio switch { 2 => TwoToOneGain, 3 => ThreeToOneGain, _ => 1000 };
    }
}
