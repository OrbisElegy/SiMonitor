// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// Preserve the exact finite binary value delivered in native logical pixels.
// No device-DPI inference, decimal rounding or sample snapping is performed.
internal static class NativeLogicalCoordinate
{
    public static ExactPlotCoordinate FromDouble(double value)
    {
        if (!double.IsFinite(value)) { throw new ArgumentOutOfRangeException(nameof(value)); }
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        int exponent = (int)((bits >> 52) & 0x7ff);
        ulong fraction = bits & 0x000f_ffff_ffff_ffff;
        BigInteger numerator = exponent == 0 ? fraction : fraction | (1UL << 52);
        if ((bits >> 63) != 0) { numerator = -numerator; }
        int shift = exponent == 0 ? -1074 : exponent - 1075;
        BigInteger denominator = BigInteger.One;
        if (shift >= 0) { numerator <<= shift; }
        else { denominator <<= -shift; }
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        return new(numerator / divisor, denominator / divisor);
    }
}
