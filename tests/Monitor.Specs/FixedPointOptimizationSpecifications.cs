// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Simulation.Determinism;

namespace Monitor.Specs;

internal static class FixedPointOptimizationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PowerOfTwoDivisionMatchesUnboundedRounding), PowerOfTwoDivisionMatchesUnboundedRounding),
        new(nameof(RoundedShiftsMatchUnboundedRounding), RoundedShiftsMatchUnboundedRounding),
        new(nameof(RoundedShiftsRejectInvalidBitCounts), RoundedShiftsRejectInvalidBitCounts),
        new(nameof(GeneralDivisionMatchesUnboundedRounding), GeneralDivisionMatchesUnboundedRounding),
        new(nameof(FixedPointProductsAndQuotientsPreserveSaturation), FixedPointProductsAndQuotientsPreserveSaturation),
        new(nameof(DivisionFaultsPreserveTheirContracts), DivisionFaultsPreserveTheirContracts),
    ];

    private static void RoundedShiftsMatchUnboundedRounding()
    {
        Int128[] boundaries = [Int128.MinValue, Int128.MinValue + 1, -1, 0, 1, Int128.MaxValue - 1, Int128.MaxValue];
        ulong randomState = 0xA0761D6478BD642F;
        for (int shift = 0; shift < 128; shift++)
        {
            BigInteger divisor = BigInteger.One << shift;
            foreach (Int128 value in boundaries) { AssertRoundedShift(value, shift); }
            for (int quotient = -4; quotient <= 4; quotient++)
            {
                for (int offset = -1; offset <= 1; offset++)
                {
                    BigInteger value = divisor * quotient + divisor / 2 + offset;
                    if (value >= (BigInteger)Int128.MinValue && value <= (BigInteger)Int128.MaxValue)
                    { AssertRoundedShift((Int128)value, shift); }
                }
            }
            for (int index = 0; index < 64; index++)
            { AssertRoundedShift(NextInt128(ref randomState), shift); }
        }
    }

    private static void RoundedShiftsRejectInvalidBitCounts()
    {
        foreach (int shift in new[] { int.MinValue, -128, -1, 128, 129, int.MaxValue })
        {
            try
            {
                FixedPointMath.RoundShiftTiesToEven(0, shift);
                throw new InvalidOperationException("out-of-range shifts must not wrap or silently succeed");
            }
            catch (ArgumentOutOfRangeException exception)
            { Check.That(exception.ParamName == "shift", "invalid bit count identifies the shift parameter"); }
        }
    }

    private static void AssertRoundedShift(Int128 value, int shift) =>
        Check.That((BigInteger)FixedPointMath.RoundShiftTiesToEven(value, shift) ==
            RoundUnbounded((BigInteger)value, BigInteger.One << shift),
            $"rounded shift must match unbounded arithmetic: {value} / 2^{shift}");

    private static void PowerOfTwoDivisionMatchesUnboundedRounding()
    {
        Int128[] edges = [Int128.MinValue, Int128.MinValue + 1, -1, 0, 1, Int128.MaxValue - 1, Int128.MaxValue];
        for (int shift = 0; shift < 128; shift++)
        {
            BigInteger magnitude = BigInteger.One << shift;
            foreach (int sign in new[] { -1, 1 })
            {
                BigInteger signedDivisor = magnitude * sign;
                if (signedDivisor > (BigInteger)Int128.MaxValue)
                {
                    continue;
                }

                var denominator = (Int128)signedDivisor;
                foreach (Int128 numerator in edges)
                {
                    AssertRoundedDivision(numerator, denominator);
                }

                for (int quotient = -4; quotient <= 4; quotient++)
                {
                    for (int offset = -1; offset <= 1; offset++)
                    {
                        BigInteger numerator = magnitude * quotient + magnitude / 2 + offset;
                        if (numerator >= (BigInteger)Int128.MinValue && numerator <= (BigInteger)Int128.MaxValue)
                        {
                            AssertRoundedDivision((Int128)numerator, denominator);
                        }
                    }
                }
            }
        }
    }

    private static void GeneralDivisionMatchesUnboundedRounding()
    {
        Int128[] boundaries =
        [
            Int128.MinValue, Int128.MinValue + 1, Int128.MaxValue, Int128.MaxValue - 1,
            -((Int128)1 << 64) - 1, -((Int128)1 << 64) + 1,
            ((Int128)1 << 64) - 1, ((Int128)1 << 64) + 1,
            long.MinValue, long.MaxValue, -1000, -3, -1, 0, 1, 3, 1000,
        ];
        foreach (Int128 numerator in boundaries)
        {
            foreach (Int128 denominator in boundaries)
            {
                if (denominator != 0)
                {
                    AssertRoundedDivision(numerator, denominator);
                }
            }
        }

        ulong randomState = 0x9E3779B97F4A7C15;
        for (int index = 0; index < 8192; index++)
        {
            Int128 numerator = NextInt128(ref randomState);
            Int128 denominator = index % 2 == 0
                ? NextInt128(ref randomState)
                : unchecked((long)NextUInt64(ref randomState));
            AssertRoundedDivision(numerator, denominator == 0 ? 3 : denominator);
        }
    }

    private static void FixedPointProductsAndQuotientsPreserveSaturation()
    {
        long[] boundaries =
        [
            long.MinValue, long.MinValue + 1, long.MaxValue, long.MaxValue - 1,
            -FixedPointMath.Q62One, FixedPointMath.Q62One,
            -FixedPointMath.Q32One, FixedPointMath.Q32One,
            -FixedPointMath.Q32One / 2, FixedPointMath.Q32One / 2,
            -3, -1, 0, 1, 3,
        ];
        foreach (long left in boundaries)
        {
            foreach (long right in boundaries)
            {
                AssertFixedPointOperations(left, right);
            }
        }

        ulong randomState = 0xD1B54A32D192ED03;
        for (int index = 0; index < 2048; index++)
        {
            AssertFixedPointOperations(
                unchecked((long)NextUInt64(ref randomState)),
                unchecked((long)NextUInt64(ref randomState)));
        }
    }

    private static void DivisionFaultsPreserveTheirContracts()
    {
        foreach (Int128 numerator in new[] { Int128.MinValue, (Int128)0, Int128.MaxValue })
        {
            try
            {
                FixedPointMath.RoundDivideTiesToEven(numerator, 0);
                throw new InvalidOperationException("division by zero must reject before selecting a fast path");
            }
            catch (DeterminismArithmeticException exception)
            {
                Check.That(exception.ReasonCode == "DeterminismArithmeticFault.DivideByZero",
                    "the deterministic divide-by-zero reason must remain stable");
            }
        }

        AssertRoundedDivision(Int128.MinValue, -1);
        Check.That(FixedPointMath.RoundDivideTiesToEven(Int128.MinValue, 1) == Int128.MinValue,
            "the negative Int128 boundary must remain representable");
    }

    private static void AssertRoundedDivision(Int128 numerator, Int128 denominator)
    {
        BigInteger expected = RoundUnbounded((BigInteger)numerator, (BigInteger)denominator);
        if (expected < (BigInteger)Int128.MinValue || expected > (BigInteger)Int128.MaxValue)
        {
            try
            {
                FixedPointMath.RoundDivideTiesToEven(numerator, denominator);
                throw new InvalidOperationException("an unrepresentable rounded quotient must throw");
            }
            catch (OverflowException)
            {
                return;
            }
        }

        Check.That((BigInteger)FixedPointMath.RoundDivideTiesToEven(numerator, denominator) == expected,
            $"rounded Int128 division must match unbounded arithmetic: {numerator} / {denominator}");
    }

    private static void AssertFixedPointOperations(long left, long right)
    {
        BigInteger product = (BigInteger)left * right;
        Check.That(FixedPointMath.MultiplyQ32(left, right) ==
            SaturateUnbounded(RoundUnbounded(product, FixedPointMath.Q32One)),
            "Q32 multiplication must preserve ties-to-even and both saturation boundaries");
        Check.That(FixedPointMath.MultiplyCoefficientQ62(left, right) ==
            SaturateUnbounded(RoundUnbounded(product, FixedPointMath.Q62One)),
            "Q62 coefficient multiplication must preserve ties-to-even and both saturation boundaries");
        if (right != 0)
        {
            Check.That(FixedPointMath.DivideQ32(left, right) ==
                SaturateUnbounded(RoundUnbounded((BigInteger)left * FixedPointMath.Q32One, right)),
                "Q32 division must preserve ties-to-even and both saturation boundaries");
        }
    }

    private static BigInteger RoundUnbounded(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        int halfComparison = (BigInteger.Abs(remainder) * 2).CompareTo(BigInteger.Abs(denominator));
        if (halfComparison > 0 || (halfComparison == 0 && !quotient.IsEven))
        {
            quotient += numerator.Sign * denominator.Sign;
        }

        return quotient;
    }

    private static SaturatingInt64 SaturateUnbounded(BigInteger value) => value < long.MinValue
        ? new SaturatingInt64(long.MinValue, true)
        : value > long.MaxValue
            ? new SaturatingInt64(long.MaxValue, true)
            : new SaturatingInt64((long)value, false);

    private static Int128 NextInt128(ref ulong state) =>
        unchecked((Int128)(((UInt128)NextUInt64(ref state) << 64) | NextUInt64(ref state)));

    private static ulong NextUInt64(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}
