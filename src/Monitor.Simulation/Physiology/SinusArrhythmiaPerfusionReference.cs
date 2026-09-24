// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Fixed authored inputs; irregular event spacing is not a stroke-volume model.
// Minimum RR600ms permits ABP overlap1 and requires PA overlap2.
public static class SinusArrhythmiaPerfusionReference
{
    public const string EvidenceId = "SinusArrhythmiaPerfusionIllustration@1";
    public static PlethRunoffPlan Pleth { get; } = AcceleratedAtrialPerfusionReference.Pleth;
    public static VascularPressurePlan Arterial { get; } = AcceleratedAtrialPerfusionReference.Arterial;
    public static VascularPressurePlan Pulmonary { get; } = AcceleratedAtrialPerfusionReference.Pulmonary;
    public static CentralVenousPressurePlan Venous { get; } = AcceleratedAtrialPerfusionReference.Venous;
}
