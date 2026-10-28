// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Monitor.Simulation.Physiology;

// A normalized binary64 pair. Both parts belong to the inventory and must be
// retained by checkpoints; the low part is not merely a reporting correction.
public readonly record struct OxygenReservoirAmount(double HighMl, double LowMl = 0)
{
    private const double DecimalLimitHighMl = 79228162514264337593543950336d; // 2^96; decimal.MaxValue is one less.
    internal double Value => HighMl + LowMl;
    internal bool IsValid => double.IsFinite(HighMl) && double.IsFinite(LowMl) &&
        HighMl + LowMl == HighMl && (Math.Abs(HighMl) < DecimalLimitHighMl ||
            HighMl == DecimalLimitHighMl && LowMl <= -1 || HighMl == -DecimalLimitHighMl && LowMl >= 1);
    internal bool IsNegative => HighMl < 0 || HighMl == 0 && LowMl < 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal OxygenReservoirAmount Add(double amountMl)
    {
        double sum = HighMl + amountMl;
        double intermediate = sum - HighMl;
        double error = (HighMl - (sum - intermediate)) + (amountMl - intermediate);
        double correction = LowMl + error;
        double high = sum + correction;
        return new(high, correction - (high - sum));
    }

    internal OxygenReservoirAmount Add(OxygenReservoirAmount amount) => Add(amount.HighMl).Add(amount.LowMl);
    internal OxygenReservoirAmount Negate() => new(-HighMl, -LowMl);

    // Decimal casts alone round binary64 to 15 significant decimal digits.
    // Convert the exact binary values at the reporting/checkpoint boundary,
    // never in the RK4 loop, so conservation diagnostics retain the low parts.
    internal decimal ToDecimal()
    {
        if (!IsValid) { throw new ArgumentException("OxygenReservoir.InvalidAmount"); }
        BigInteger units = BinaryUnits(HighMl) + BinaryUnits(LowMl);
        bool negative = units.Sign < 0;
        units = BigInteger.Abs(units);
        BigInteger denominator = BigInteger.One << 1074;
        BigInteger maximum = (BigInteger.One << 96) - 1;
        for (int scale = 28; scale >= 0; scale--)
        {
            var rounded = BigInteger.DivRem(units * BigInteger.Pow(10, scale), denominator, out BigInteger remainder);
            int comparison = (remainder * 2).CompareTo(denominator);
            if (comparison > 0 || comparison == 0 && !rounded.IsEven) { rounded++; }
            if (rounded <= maximum)
            {
                return new decimal(unchecked((int)(uint)(rounded & uint.MaxValue)),
                    unchecked((int)(uint)((rounded >> 32) & uint.MaxValue)),
                    unchecked((int)(uint)((rounded >> 64) & uint.MaxValue)), negative, (byte)scale);
            }
        }
        throw new OverflowException("OxygenReservoir.AmountOutsideDecimalRange");
    }

    internal static OxygenReservoirAmount FromDecimal(decimal amountMl)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(amountMl, bits);
        BigInteger numerator = (uint)bits[0] + (new BigInteger((uint)bits[1]) << 32) + (new BigInteger((uint)bits[2]) << 64);
        if (bits[3] < 0) { numerator = -numerator; }
        var denominator = BigInteger.Pow(10, (bits[3] >> 16) & 255);
        double high = (double)amountMl;
        BigInteger remainder = (numerator << 1074) - BinaryUnits(high) * denominator;
        int shift = Math.Max(0, (int)BigInteger.Abs(remainder).GetBitLength() - 53);
        double low = Math.ScaleB((double)(BigInteger.Abs(remainder) >> shift), shift - 1074) * remainder.Sign / (double)denominator;
        // Normalize even when the decimal conversion chose a neighboring high.
        return new OxygenReservoirAmount(high).Add(low);
    }

    private static BigInteger BinaryUnits(double value)
    {
        ulong bits = BitConverter.DoubleToUInt64Bits(value);
        ulong fraction = bits & 0x000f_ffff_ffff_ffffUL;
        int exponent = (int)((bits >> 52) & 2047);
        BigInteger units = exponent == 0 ? fraction : new BigInteger(fraction | (1UL << 52)) << (exponent - 1);
        return (bits >> 63) == 0 ? units : -units;
    }
}
