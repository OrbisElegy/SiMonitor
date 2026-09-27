// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

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
    // F(d)=d/(d+tau), normalized to the800ms reference. The240ms
    // effective non-filling interval and200ms filling constant are teaching
    // assumptions. Same dimensionless stroke-volume gain drives both circuits.
    public const long ReferencePeriodNs = 800_000_000;
    public const long NonFillingDurationNs = 240_000_000;
    public const long FillingConstantNs = 200_000_000;
    public static int StrokeVolumePermille(long periodNs)
    {
        if (periodNs <= NonFillingDurationNs || periodNs > ReferencePeriodNs)
        { throw new ArgumentOutOfRangeException(nameof(periodNs)); }
        long filling = periodNs - NonFillingDurationNs;
        long reference = ReferencePeriodNs - NonFillingDurationNs;
        return (int)FixedPointMath.RoundDivideTiesToEven((Int128)1000 * filling * (reference + FillingConstantNs),
            (Int128)reference * (filling + FillingConstantNs));
    }
    private static int Scale(int value) => (int)FixedPointMath.RoundDivideTiesToEven(
        (Int128)value * StrokeVolumePermille(300_000_000), 1000);
    private static VascularPressurePlan Limit(VascularPressurePlan pressure) => pressure with
    {
        EjectionEquilibriumCentiMmHg = Scale(pressure.EjectionEquilibriumCentiMmHg),
        Morphology = pressure.Morphology! with { PulseHeightCentiMmHg = Scale(pressure.Morphology!.PulseHeightCentiMmHg) }
    };
    public static PlethRunoffPlan Pleth { get; } = new(80_000_000, 512_000_000, Scale(1000));
    public static VascularPressurePlan Arterial { get; } = Limit(ReferenceArterial);
    public static VascularPressurePlan Pulmonary { get; } = Limit(ReferencePulmonary);
    public static CentralVenousPressurePlan Venous { get; } = new(600,
        new(0, 120_000_000, 200), new(0, 120_000_000, 80),
        new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250),
        new(400_000_000, 160_000_000, 120), -100, MaximumComponentOverlap: 2);
}
