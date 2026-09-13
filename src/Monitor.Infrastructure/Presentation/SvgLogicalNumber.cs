// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Numerics;

namespace Monitor.Infrastructure.Presentation;

internal static class SvgLogicalNumber
{
    public static BigInteger RoundMillionths(BigInteger numerator, BigInteger denominator)
    {
        var scaled = BigInteger.DivRem(BigInteger.Abs(numerator) * 1_000_000, denominator, out BigInteger remainder);
        if (remainder * 2 >= denominator) { scaled++; }
        return numerator.Sign < 0 ? -scaled : scaled;
    }

    // Six decimal logical pixels, nearest with ties away from zero.
    public static string Format(BigInteger numerator, BigInteger denominator)
    {
        var scaled = BigInteger.Abs(RoundMillionths(numerator, denominator));
        string sign = numerator.Sign < 0 && scaled != 0 ? "-" : "";
        string fraction = (scaled % 1_000_000).ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');
        return sign + (scaled / 1_000_000).ToString(CultureInfo.InvariantCulture) + (fraction.Length == 0 ? "" : "." + fraction);
    }
}
