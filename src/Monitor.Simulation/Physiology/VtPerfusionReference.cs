// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Fixed input illustration, not calibrated VT stroke volume or blood pressure.
// Preserve existing pulse supports, with overlap budgets for375ms ventricular RR.
public static class VtPerfusionReference
{
    public const string EvidenceId = "VtPerfusionIllustration@1";
    public static PlethRunoffPlan Pleth { get; } = SvtPerfusionReference.ReferencePleth;
    public static VascularPressurePlan Arterial { get; } = SvtPerfusionReference.ReferenceArterial;
    public static VascularPressurePlan Pulmonary { get; } = SvtPerfusionReference.ReferencePulmonary with
    { Morphology = SvtPerfusionReference.ReferencePulmonary.Morphology! with { MaximumPulseOverlap = 2 } };
    // A follows800ms atrial cycles; ventricular components last at most320ms.
    // This does not model atrial contraction against a closed valve (cannon a).
    public static CentralVenousPressurePlan Venous { get; } = SvtPerfusionReference.Venous with
    { MaximumComponentOverlap = 1 };
}
