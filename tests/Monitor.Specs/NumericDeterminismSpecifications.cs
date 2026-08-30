// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Specs;

internal static class NumericDeterminismSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(TiesToEvenAndSaturationMatchFrozenVectors),
            TiesToEvenAndSaturationMatchFrozenVectors),
        new(nameof(PeriodicLookupMatchesFrozenVectors), PeriodicLookupMatchesFrozenVectors),
        new(nameof(BiquadMatchesFrozenVectors), BiquadMatchesFrozenVectors),
        new(nameof(BiquadPropagatesIntermediateSaturation),
            BiquadPropagatesIntermediateSaturation),
    ];

    private static void TiesToEvenAndSaturationMatchFrozenVectors()
    {
        RoundingVector[] rounding =
        [
            new(5, 2, 2),
            new(7, 2, 4),
            new(-5, 2, -2),
            new(-7, 2, -4),
            new(7, -2, -4),
        ];
        foreach (RoundingVector vector in rounding)
        {
            Check.That(FixedPointMath.RoundDivideTiesToEven(
                vector.Numerator,
                vector.Denominator) == vector.Expected,
                "integer division must round to nearest with ties to even");
        }

        SaturatingInt64 aboveMaximum = FixedPointMath.Saturate((Int128)long.MaxValue + 1);
        SaturatingInt64 belowMinimum = FixedPointMath.Saturate((Int128)long.MinValue - 1);
        SaturatingInt64 inRange = FixedPointMath.Saturate(42);
        Check.That(aboveMaximum == new SaturatingInt64(long.MaxValue, true),
            "positive overflow must saturate to i64 max and raise the flag");
        Check.That(belowMinimum == new SaturatingInt64(long.MinValue, true),
            "negative overflow must saturate to i64 min and raise the flag");
        Check.That(inRange == new SaturatingInt64(42, false),
            "an in-range value must remain unchanged without saturation");
        Check.That(FixedPointMath.Add(long.MaxValue, 1).Saturated &&
            FixedPointMath.Subtract(long.MinValue, 1).Saturated,
            "runtime addition and subtraction must saturate");
        Check.That(FixedPointMath.MultiplyQ32(long.MaxValue, long.MaxValue) ==
            new SaturatingInt64(long.MaxValue, true) &&
            FixedPointMath.DivideQ32(long.MaxValue, 1) ==
            new SaturatingInt64(long.MaxValue, true),
            "runtime fixed-point multiplication and division must saturate");
        Check.That(FixedPointMath.MultiplyQ32(1, FixedPointMath.Q32One / 2).Value == 0 &&
            FixedPointMath.MultiplyQ32(3, FixedPointMath.Q32One / 2).Value == 2,
            "Q32 products must quantize with ties-to-even after the Int128 product");
        Check.That(DivideByZeroReason() == "DeterminismArithmeticFault.DivideByZero",
            "division by zero must expose the frozen deterministic fault");
    }

    private static void PeriodicLookupMatchesFrozenVectors()
    {
        long[] table = [0, FixedPointMath.Q32One, 0, -FixedPointMath.Q32One];
        LutVector[] vectors =
        [
            new(0x0000000000000000UL, 0),
            new(0x2000000000000000UL, 2_147_483_648),
            new(0x4000000000000000UL, 4_294_967_296),
            new(0x6000000000000000UL, 2_147_483_648),
            new(0x8000000000000000UL, 0),
            new(0xa000000000000000UL, -2_147_483_648),
            new(0xc000000000000000UL, -4_294_967_296),
            new(0xe000000000000000UL, -2_147_483_648),
        ];
        foreach (LutVector vector in vectors)
        {
            SaturatingInt64 actual = PeriodicLutLinear.Interpolate(table, vector.PhaseU64);
            Check.That(actual == new SaturatingInt64(vector.OutputQ32, false),
                "PeriodicLutLinear must match the frozen Q32 output");
        }

        foreach (int invalidLength in new[] { 0, 3, 5, 65_537 })
        {
            Check.That(ConfigurationReason(() =>
                PeriodicLutLinear.Interpolate(new long[invalidLength], 0)) ==
                "PeriodicLutLinear.InvalidTableLength",
                $"invalid periodic table length must reject: {invalidLength}");
        }
    }

    private static void BiquadMatchesFrozenVectors()
    {
        FixedBiquadCoefficients coefficients = new(
            1L << 60,
            1L << 60,
            0,
            -(1L << 61),
            1L << 59);
        BiquadVector[] vectors =
        [
            new(4_294_967_296, 1_073_741_824, 1_610_612_736, -134_217_728),
            new(4_294_967_296, 2_684_354_560, 2_281_701_376, -335_544_320),
            new(0, 2_281_701_376, 805_306_368, -285_212_672),
            new(-4_294_967_296, -268_435_456, -1_493_172_224, 33_554_432),
            new(8_589_934_592, 654_311_424, 2_508_193_792, -81_788_928),
            new(0, 2_508_193_792, 1_172_307_968, -313_524_224),
            new(0, 1_172_307_968, 272_629_760, -146_538_496),
            new(0, 272_629_760, -10_223_616, -34_078_720),
        ];

        FixedBiquadState state = default;
        foreach (BiquadVector vector in vectors)
        {
            FixedBiquadStep actual = FixedBiquadDf2T.Step(state, coefficients, vector.InputQ32);
            Check.That(actual.OutputQ32 == vector.OutputQ32 &&
                actual.State == new FixedBiquadState(vector.Z1Q32, vector.Z2Q32) &&
                !actual.Saturated,
                "FixedBiquadDF2T must preserve the frozen per-product evaluation order");
            state = actual.State;
        }
    }

    private static void BiquadPropagatesIntermediateSaturation()
    {
        FixedBiquadCoefficients coefficients = new(long.MaxValue, 0, 0, 0, 0);
        FixedBiquadStep step = FixedBiquadDf2T.Step(default, coefficients, long.MaxValue);
        Check.That(step.OutputQ32 == long.MaxValue && step.Saturated,
            "an intermediate coefficient product saturation must reach the step result");
    }

    private static string? DivideByZeroReason()
    {
        try
        {
            FixedPointMath.DivideQ32(1, 0);
            return null;
        }
        catch (DeterminismArithmeticException exception)
        {
            return exception.ReasonCode;
        }
    }

    private static string? ConfigurationReason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (DeterminismConfigurationException exception)
        {
            return exception.ReasonCode;
        }
    }

    private sealed record RoundingVector(long Numerator, long Denominator, long Expected);

    private sealed record LutVector(ulong PhaseU64, long OutputQ32);

    private sealed record BiquadVector(
        long InputQ32,
        long OutputQ32,
        long Z1Q32,
        long Z2Q32);
}
