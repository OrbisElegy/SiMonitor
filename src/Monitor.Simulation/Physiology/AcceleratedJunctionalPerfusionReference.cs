// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Fixed authored inputs, not calibrated accelerated junctional rhythm stroke volume or blood pressure.
// Retain full supports: ABP fits600ms RR; PA640ms needs two-pulse overlap.
public static class AcceleratedJunctionalPerfusionReference
{
    public const string EvidenceId = "AcceleratedJunctionalPerfusionIllustration@1";
    public static PlethRunoffPlan Pleth { get; } = VtPerfusionReference.Pleth;
    public static VascularPressurePlan Arterial { get; } = VtPerfusionReference.Arterial with
    { Morphology = VtPerfusionReference.Arterial.Morphology! with { MaximumPulseOverlap = 1 } };
    public static VascularPressurePlan Pulmonary { get; } = VtPerfusionReference.Pulmonary with
    { Morphology = VtPerfusionReference.Pulmonary.Morphology! with { MaximumPulseOverlap = 2 } };
    // A follows the independent atria; c/x/v/y follow ventricular ejections.
    // No atrial gain change is inferred from coincidence or dissociation.
    public static CentralVenousPressurePlan Venous { get; } = VtPerfusionReference.Venous;
}
