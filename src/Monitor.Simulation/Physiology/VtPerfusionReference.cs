// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Per-beat filling at actual RR and AV phase, not calibrated VT pump function.
// Preserve existing pulse supports, with overlap budgets for375ms ventricular RR.
public static class VtPerfusionReference
{
    public const string EvidenceId = "VtPerfusionIllustration@3";
    public static PlethRunoffPlan Pleth { get; } = SvtPerfusionReference.ReferencePleth with { UseCardiacFillingPerfusion = true };
    public static VascularPressurePlan Arterial { get; } = SvtPerfusionReference.ReferenceArterial with { UseCardiacFillingPerfusion = true };
    public static VascularPressurePlan Pulmonary { get; } = SvtPerfusionReference.ReferencePulmonary with
    {
        UseCardiacFillingPerfusion = true,
        Morphology = SvtPerfusionReference.ReferencePulmonary.Morphology! with { MaximumPulseOverlap = 2 }
    };
    // A follows800ms atrial cycles; ventricular components last at most320ms.
    // This does not model atrial contraction against a closed valve (cannon a).
    public static CentralVenousPressurePlan Venous { get; } = SvtPerfusionReference.Venous with
    { MaximumComponentOverlap = 1 };
}
