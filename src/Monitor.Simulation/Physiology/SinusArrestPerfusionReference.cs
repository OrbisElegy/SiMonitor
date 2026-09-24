// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Authored fixed inputs with minimum800ms RR; not calibrated pause-related pump function.
public static class SinusArrestPerfusionReference
{
    public const string EvidenceId = "SinusArrestPerfusionIllustration@1";
    public static PlethRunoffPlan Pleth { get; } = AcceleratedVentricularPerfusionReference.Pleth;
    public static VascularPressurePlan Arterial { get; } = AcceleratedVentricularPerfusionReference.Arterial;
    public static VascularPressurePlan Pulmonary { get; } = AcceleratedVentricularPerfusionReference.Pulmonary;
    // A follows the sinus atrial mechanical event; c/x/v/y follow conducted ejection.
    public static CentralVenousPressurePlan Venous { get; } = AcceleratedVentricularPerfusionReference.Venous;
}
