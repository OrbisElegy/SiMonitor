// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Authored filling-limited inputs for200ms RR, not calibrated flutter hemodynamics.
public static class FlutterOneToOnePerfusionReference
{
    public const string EvidenceId = "FlutterOneToOnePerfusionIllustration@2";
    // Conservative shared non-filling interval uses the longer authored input
    // (160ms); it is not a measured valve or QT interval.
    public const long NonFillingDurationNs = 160_000_000;
    public static int StrokeVolumePermille => FillingLimitedEjection.StrokeVolumePermille(200_000_000, NonFillingDurationNs);
    public static PlethRunoffPlan Pleth { get; } = new(80_000_000, 512_000_000, StrokeVolumePermille);
    public static VascularPressurePlan Arterial { get; } = FillingLimitedEjection.LimitDuration(SvtPerfusionReference.ReferenceArterial with
    {
        EjectionDurationNs = 120_000_000,
        Morphology = SvtPerfusionReference.ReferenceArterial.Morphology! with { MaximumPulseOverlap = 3 }
    }, 200_000_000, NonFillingDurationNs, 240_000_000);
    public static VascularPressurePlan Pulmonary { get; } = FillingLimitedEjection.LimitDuration(SvtPerfusionReference.ReferencePulmonary with
    {
        EjectionDurationNs = 160_000_000,
        Morphology = SvtPerfusionReference.ReferencePulmonary.Morphology! with { MaximumPulseOverlap = 4 }
    }, 200_000_000, NonFillingDurationNs, 200_000_000);
    // Flutter timeline suppresses AtrialMechanical; do not synthesize normal a waves.
    public static CentralVenousPressurePlan Venous { get; } = SvtPerfusionReference.Venous;
}
