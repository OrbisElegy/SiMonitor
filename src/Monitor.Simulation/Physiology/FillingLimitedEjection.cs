// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored filling approximation, not a calibrated ventricular pump.
public static class FillingLimitedEjection
{
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
    internal static int Scale(int value, long periodNs) => (int)FixedPointMath.RoundDivideTiesToEven(
        (Int128)value * StrokeVolumePermille(periodNs), 1000);
    internal static VascularPressurePlan Limit(VascularPressurePlan pressure, long periodNs) => pressure with
    {
        EjectionEquilibriumCentiMmHg = Scale(pressure.EjectionEquilibriumCentiMmHg, periodNs),
        Morphology = pressure.Morphology! with { PulseHeightCentiMmHg = Scale(pressure.Morphology!.PulseHeightCentiMmHg, periodNs) }
    };
}
