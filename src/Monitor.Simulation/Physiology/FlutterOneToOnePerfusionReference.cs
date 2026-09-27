// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Authored fixed inputs for200ms RR, not calibrated flutter hemodynamics.
public static class FlutterOneToOnePerfusionReference
{
    public const string EvidenceId = "FlutterOneToOnePerfusionIllustration@1";
    public static PlethRunoffPlan Pleth { get; } = SvtPerfusionReference.ReferencePleth;
    public static VascularPressurePlan Arterial { get; } = SvtPerfusionReference.ReferenceArterial with
    {
        EjectionDurationNs = 120_000_000,
        Morphology = SvtPerfusionReference.ReferenceArterial.Morphology! with { MaximumPulseOverlap = 3 }
    };
    public static VascularPressurePlan Pulmonary { get; } = SvtPerfusionReference.ReferencePulmonary with
    {
        EjectionDurationNs = 160_000_000,
        Morphology = SvtPerfusionReference.ReferencePulmonary.Morphology! with { MaximumPulseOverlap = 4 }
    };
    // Flutter timeline suppresses AtrialMechanical; do not synthesize normal a waves.
    public static CentralVenousPressurePlan Venous { get; } = SvtPerfusionReference.Venous;
}
