// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Fixed authored inputs, not calibrated AIVR stroke volume or blood pressure.
// Full pulse supports fit within750ms RR; runoff still retains earlier pulses.
public static class AcceleratedVentricularPerfusionReference
{
    public const string EvidenceId = "AcceleratedVentricularPerfusionIllustration@1";
    public static PlethRunoffPlan Pleth { get; } = VtPerfusionReference.Pleth;
    public static VascularPressurePlan Arterial { get; } = VtPerfusionReference.Arterial with
    { Morphology = VtPerfusionReference.Arterial.Morphology! with { MaximumPulseOverlap = 1 } };
    public static VascularPressurePlan Pulmonary { get; } = VtPerfusionReference.Pulmonary with
    { Morphology = VtPerfusionReference.Pulmonary.Morphology! with { MaximumPulseOverlap = 1 } };
    // A follows the independent atria; c/x/v/y follow ventricular ejections.
    // No atrial gain change is inferred from coincidence or dissociation.
    public static CentralVenousPressurePlan Venous { get; } = VtPerfusionReference.Venous;
}
