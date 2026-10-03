// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Filling-limited teaching input at fixed300ms RR, followed by the unchanged RC
// reservoir. Parameters are authored, not patient-calibrated; see docs/svt-perfusion.md.
public static class SvtPerfusionReference
{
    public const string EvidenceId = "SvtPerfusionIllustration@3";
    internal static PlethRunoffPlan ReferencePleth { get; } = new(80_000_000, 512_000_000, 1000);
    internal static VascularPressurePlan ReferenceArterial { get; } = new(80_000_000, 240_000_000,
        2_900_000_000, 8000, 1000, 30000,
        Morphology: new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000, MaximumPulseOverlap: 2));
    internal static VascularPressurePlan ReferencePulmonary { get; } = new(40_000_000, 200_000_000,
        700_000_000, 1000, 500, 5000,
        Morphology: new(VascularPressureMorphologyKind.PulmonaryArtery, 640_000_000, 1500, MaximumPulseOverlap: 3));
    // Rate-only reference curve retained for callers comparing filling time.
    // Product ejections additionally use their actual atrial phase below.
    public static int StrokeVolumePermille(long periodNs) => FillingLimitedEjection.StrokeVolumePermille(periodNs);
    public static PlethRunoffPlan Pleth { get; } = ReferencePleth with { UseCardiacFillingPerfusion = true };
    public static VascularPressurePlan Arterial { get; } = ReferenceArterial with { UseCardiacFillingPerfusion = true };
    public static VascularPressurePlan Pulmonary { get; } = ReferencePulmonary with { UseCardiacFillingPerfusion = true };
    public static CentralVenousPressurePlan Venous { get; } = new(600,
        new(0, 120_000_000, 200), new(0, 120_000_000, 80),
        new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250),
        new(400_000_000, 160_000_000, 120), -100, MaximumComponentOverlap: 2);
}
