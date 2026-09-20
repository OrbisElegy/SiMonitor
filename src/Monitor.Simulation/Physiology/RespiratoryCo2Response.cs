// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;

namespace Monitor.Simulation.Physiology;

// Bounded authored gas-reservoir response to the existing depth sequence.
// x is CO2 excess above inspired baseline / reference excess. Each slot uses
// backward Euler: x_next = (tau*x + T) / (tau + T*depth/referenceDepth).
// Constant production continues in apnea; no expired sample is emitted then.
// Start on the exact periodic orbit, not an unbounded replay from epoch.
internal static class RespiratoryCo2Response
{
    internal const long TimeConstantNs = 20_000_000_000;
    internal static IReadOnlyList<int> Create(long periodNs, RespiratoryPattern pattern)
    {
        if (periodNs <= 0) { throw Invalid(); }
        int length = RespiratoryPatternDepth.Length(pattern);
        int referenceDepth = pattern == RespiratoryPattern.CheyneStokesIllustration ? 500 : 1000;
        BigInteger a = (BigInteger)TimeConstantNs * referenceDepth;
        BigInteger b = (BigInteger)periodNs * referenceDepth;
        BigInteger product = 1, constant = 0, denominator = 1;
        for (ulong slot = 0; slot < (ulong)length; slot++)
        {
            BigInteger divisor = a + (BigInteger)periodNs * RespiratoryPatternDepth.At(pattern, slot);
            constant = a * constant + b * denominator;
            product *= a;
            denominator *= divisor;
        }
        BigInteger numerator = constant;
        denominator -= product;
        int[] gains = new int[length];
        for (ulong slot = 0; slot < (ulong)length; slot++)
        {
            numerator = a * numerator + b * denominator;
            denominator *= a + (BigInteger)periodNs * RespiratoryPatternDepth.At(pattern, slot);
            var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
            numerator /= gcd; denominator /= gcd;
            var gain = BigInteger.DivRem(numerator * 1000, denominator, out var remainder);
            if (remainder * 2 > denominator || remainder * 2 == denominator && !gain.IsEven) { gain++; }
            if (gain < 1 || gain > 10_000) { throw Invalid(); }
            gains[slot] = (int)gain;
        }
        return Array.AsReadOnly(gains);
    }
    private static EventWaveformException Invalid() => new("Capnogram.InvalidCo2Response", "periodNs");
}
