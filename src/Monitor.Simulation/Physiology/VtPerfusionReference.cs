// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Filling-limited input at375ms RR, not calibrated VT pump function.
// Preserve existing pulse supports, with overlap budgets for375ms ventricular RR.
public static class VtPerfusionReference
{
    public const string EvidenceId = "VtPerfusionIllustration@2";
    public static PlethRunoffPlan Pleth { get; } = new(80_000_000, 512_000_000, FillingLimitedEjection.Scale(1000, 375_000_000));
    public static VascularPressurePlan Arterial { get; } = FillingLimitedEjection.Limit(SvtPerfusionReference.ReferenceArterial, 375_000_000);
    public static VascularPressurePlan Pulmonary { get; } = FillingLimitedEjection.Limit(SvtPerfusionReference.ReferencePulmonary, 375_000_000) with
    { Morphology = FillingLimitedEjection.Limit(SvtPerfusionReference.ReferencePulmonary, 375_000_000).Morphology! with { MaximumPulseOverlap = 2 } };
    // A follows800ms atrial cycles; ventricular components last at most320ms.
    // This does not model atrial contraction against a closed valve (cannon a).
    public static CentralVenousPressurePlan Venous { get; } = SvtPerfusionReference.Venous with
    { MaximumComponentOverlap = 1 };
}
