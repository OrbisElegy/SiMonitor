// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace Monitor.Simulation.Determinism;

public readonly record struct SaturatingInt64(long Value, bool Saturated);

public static class FixedPointMath
{
    public const long Q32One = 1L << 32;
    public const long Q62One = 1L << 62;

    public static Int128 RoundDivideTiesToEven(Int128 numerator, Int128 denominator)
    {
        if (denominator == 0)
        {
            throw new DeterminismArithmeticException("DeterminismArithmeticFault.DivideByZero");
        }

        bool negative = (numerator < 0) != (denominator < 0);
        UInt128 dividend = Magnitude(numerator);
        UInt128 divisor = Magnitude(denominator);
        UInt128 quotient = dividend / divisor;
        UInt128 remainder = dividend % divisor;
        int halfComparison = remainder.CompareTo(divisor - remainder);
        if (halfComparison > 0 || (halfComparison == 0 && (quotient & 1) != 0))
        {
            quotient++;
        }

        return ApplySign(quotient, negative);
    }

    public static SaturatingInt64 Saturate(Int128 value)
    {
        if (value < long.MinValue)
        {
            return new SaturatingInt64(long.MinValue, true);
        }

        if (value > long.MaxValue)
        {
            return new SaturatingInt64(long.MaxValue, true);
        }

        return new SaturatingInt64((long)value, false);
    }

    public static SaturatingInt64 Add(long left, long right) =>
        Saturate((Int128)left + right);

    public static SaturatingInt64 Subtract(long left, long right) =>
        Saturate((Int128)left - right);

    public static SaturatingInt64 MultiplyQ32(long leftQ32, long rightQ32) =>
        Saturate(RoundDivideTiesToEven((Int128)leftQ32 * rightQ32, Q32One));

    public static SaturatingInt64 DivideQ32(long numeratorQ32, long denominatorQ32) =>
        Saturate(RoundDivideTiesToEven((Int128)numeratorQ32 * Q32One, denominatorQ32));

    public static SaturatingInt64 MultiplyCoefficientQ62(long coefficientQ62, long valueQ32) =>
        Saturate(RoundDivideTiesToEven((Int128)coefficientQ62 * valueQ32, Q62One));

    private static UInt128 Magnitude(Int128 value) => value >= 0
        ? (UInt128)value
        : (UInt128)(-(value + 1)) + 1;

    private static Int128 ApplySign(UInt128 magnitude, bool negative)
    {
        UInt128 minimumMagnitude = (UInt128)Int128.MaxValue + 1;
        if (negative && magnitude == minimumMagnitude)
        {
            return Int128.MinValue;
        }

        var signed = checked((Int128)magnitude);
        return negative ? -signed : signed;
    }
}

public static class PeriodicLutLinear
{
    public static SaturatingInt64 Interpolate(ReadOnlySpan<long> tableQ32, ulong phaseU64)
    {
        int length = tableQ32.Length;
        if (length is < 4 or > 65_536 || !BitOperations.IsPow2((uint)length))
        {
            throw new DeterminismConfigurationException(
                "PeriodicLutLinear.InvalidTableLength",
                nameof(tableQ32));
        }

        int tableBits = BitOperations.Log2((uint)length);
        int fractionalBits = 64 - tableBits;
        int index = (int)(phaseU64 >> fractionalBits);
        ulong fractionMask = (1UL << fractionalBits) - 1;
        ulong fraction = phaseU64 & fractionMask;
        int next = (index + 1) & (length - 1);
        Int128 delta = (Int128)tableQ32[next] - tableQ32[index];
        Int128 interpolated = FixedPointMath.RoundDivideTiesToEven(
            delta * fraction,
            (Int128)1 << fractionalBits);
        return FixedPointMath.Saturate((Int128)tableQ32[index] + interpolated);
    }
}

public readonly record struct FixedBiquadCoefficients(
    long B0Q62,
    long B1Q62,
    long B2Q62,
    long A1Q62,
    long A2Q62);

public readonly record struct FixedBiquadState(long Z1Q32, long Z2Q32);

public readonly record struct FixedBiquadStep(
    long OutputQ32,
    FixedBiquadState State,
    bool Saturated);

public static class FixedBiquadDf2T
{
    public static FixedBiquadStep Step(
        FixedBiquadState state,
        FixedBiquadCoefficients coefficients,
        long inputQ32)
    {
        SaturatingInt64 b0x =
            FixedPointMath.MultiplyCoefficientQ62(coefficients.B0Q62, inputQ32);
        SaturatingInt64 output = FixedPointMath.Add(b0x.Value, state.Z1Q32);
        SaturatingInt64 b1x =
            FixedPointMath.MultiplyCoefficientQ62(coefficients.B1Q62, inputQ32);
        SaturatingInt64 a1y =
            FixedPointMath.MultiplyCoefficientQ62(coefficients.A1Q62, output.Value);
        SaturatingInt64 z1Difference = FixedPointMath.Subtract(b1x.Value, a1y.Value);
        SaturatingInt64 nextZ1 = FixedPointMath.Add(z1Difference.Value, state.Z2Q32);
        SaturatingInt64 b2x =
            FixedPointMath.MultiplyCoefficientQ62(coefficients.B2Q62, inputQ32);
        SaturatingInt64 a2y =
            FixedPointMath.MultiplyCoefficientQ62(coefficients.A2Q62, output.Value);
        SaturatingInt64 nextZ2 = FixedPointMath.Subtract(b2x.Value, a2y.Value);

        bool saturated = b0x.Saturated || output.Saturated || b1x.Saturated ||
            a1y.Saturated || z1Difference.Saturated || nextZ1.Saturated ||
            b2x.Saturated || a2y.Saturated || nextZ2.Saturated;
        return new FixedBiquadStep(
            output.Value,
            new FixedBiquadState(nextZ1.Value, nextZ2.Value),
            saturated);
    }
}
