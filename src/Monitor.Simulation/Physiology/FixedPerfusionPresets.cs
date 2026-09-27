// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// A bundle of existing authored inputs, not a rhythm or a calibrated pump model.
// The accepted physiology plan still owns all atrial and ventricular events.
public sealed record FixedPerfusionPreset(PlethRunoffPlan Pleth,
    VascularPressurePlan Arterial, VascularPressurePlan Pulmonary,
    CentralVenousPressurePlan Venous);

public static class FixedPerfusionPresets
{
    // All full supports fit the750/800/1200ms examples. Preserve the existing
    // amplitudes, input durations and finite runoff; only overlap budgets vary.
    public static FixedPerfusionPreset SinglePulse { get; } = new(
        SvtPerfusionReference.ReferencePleth,
        SvtPerfusionReference.ReferenceArterial with
        { Morphology = SvtPerfusionReference.ReferenceArterial.Morphology! with { MaximumPulseOverlap = 1 } },
        SvtPerfusionReference.ReferencePulmonary with
        { Morphology = SvtPerfusionReference.ReferencePulmonary.Morphology! with { MaximumPulseOverlap = 1 } },
        VtPerfusionReference.Venous);

    // At minimum RR600ms the640ms PA support may overlap its successor.
    public static FixedPerfusionPreset PulmonaryOverlap { get; } = SinglePulse with
    {
        Pulmonary = SinglePulse.Pulmonary with
        { Morphology = SinglePulse.Pulmonary.Morphology! with { MaximumPulseOverlap = 2 } }
    };
}
