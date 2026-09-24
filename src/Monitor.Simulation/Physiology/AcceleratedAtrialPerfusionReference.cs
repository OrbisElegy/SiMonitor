// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Fixed authored inputs, not calibrated ectopic atrial rhythm pump function.
// Same600ms RR support budgets as the junctional example; source events differ.
public static class AcceleratedAtrialPerfusionReference
{
    public const string EvidenceId = "AcceleratedAtrialPerfusionIllustration@1";
    public static PlethRunoffPlan Pleth { get; } = AcceleratedJunctionalPerfusionReference.Pleth;
    public static VascularPressurePlan Arterial { get; } = AcceleratedJunctionalPerfusionReference.Arterial;
    public static VascularPressurePlan Pulmonary { get; } = AcceleratedJunctionalPerfusionReference.Pulmonary;
    // Atrial contraction follows P-prime; c/x/v/y follow conducted ejection.
    // Electrical P-prime polarity does not invert the mechanical a wave.
    public static CentralVenousPressurePlan Venous { get; } = AcceleratedJunctionalPerfusionReference.Venous;
}
