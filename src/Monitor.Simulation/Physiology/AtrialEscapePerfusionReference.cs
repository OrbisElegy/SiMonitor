// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Authored fixed inputs at1200ms RR, not calibrated escape-rhythm pump function.
public static class AtrialEscapePerfusionReference
{
    public const string EvidenceId = "AtrialEscapePerfusionIllustration@1";
    public static PlethRunoffPlan Pleth { get; } = AcceleratedVentricularPerfusionReference.Pleth;
    public static VascularPressurePlan Arterial { get; } = AcceleratedVentricularPerfusionReference.Arterial;
    public static VascularPressurePlan Pulmonary { get; } = AcceleratedVentricularPerfusionReference.Pulmonary;
    // A follows the ectopic atrial mechanical event; c/x/v/y follow conducted ejection.
    public static CentralVenousPressurePlan Venous { get; } = AcceleratedVentricularPerfusionReference.Venous;
}
