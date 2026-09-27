// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Filling-limited teaching input at fixed300ms RR, followed by the unchanged RC
// reservoir. Parameters are authored, not patient-calibrated; see docs/svt-perfusion.md.
public static class SvtPerfusionReference
{
    public const string EvidenceId = "SvtPerfusionIllustration@2";
    internal static PlethRunoffPlan ReferencePleth { get; } = new(80_000_000, 512_000_000, 1000);
    internal static VascularPressurePlan ReferenceArterial { get; } = new(80_000_000, 240_000_000,
        2_900_000_000, 8000, 1000, 30000,
        Morphology: new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000, MaximumPulseOverlap: 2));
    internal static VascularPressurePlan ReferencePulmonary { get; } = new(40_000_000, 200_000_000,
        700_000_000, 1000, 500, 5000,
        Morphology: new(VascularPressureMorphologyKind.PulmonaryArtery, 640_000_000, 1500, MaximumPulseOverlap: 3));
    public static int StrokeVolumePermille(long periodNs) => FillingLimitedEjection.StrokeVolumePermille(periodNs);
    public static PlethRunoffPlan Pleth { get; } = new(80_000_000, 512_000_000, FillingLimitedEjection.Scale(1000, 300_000_000));
    public static VascularPressurePlan Arterial { get; } = FillingLimitedEjection.Limit(ReferenceArterial, 300_000_000);
    public static VascularPressurePlan Pulmonary { get; } = FillingLimitedEjection.Limit(ReferencePulmonary, 300_000_000);
    public static CentralVenousPressurePlan Venous { get; } = new(600,
        new(0, 120_000_000, 200), new(0, 120_000_000, 80),
        new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250),
        new(400_000_000, 160_000_000, 120), -100, MaximumComponentOverlap: 2);
}
