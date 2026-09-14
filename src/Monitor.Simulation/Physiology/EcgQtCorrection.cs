// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Explicit source configuration, not a measured QTc or a normality classifier.
// Nanoseconds are converted to the formula's seconds by the exact 1e9 divisor.
public sealed record EcgQtCorrection(string MethodId, long QtcIntervalNs, long RrIntervalNs)
{
    public const string Bazett = "Bazett@1";
    public const string Fridericia = "Fridericia@1";
    public const string FormulaRrUnit = "second";
    public const string RoundingId = "NearestNanosecondTiesToEven@1";

    public long ResolveQtIntervalNs()
    {
        // Computational bounds keep all root and midpoint comparisons in Int128.
        // They are not physiological limits. No floating-point root is evaluated.
        if (MethodId is not (Bazett or Fridericia) || QtcIntervalNs is <= 0 or > 1_000_000_000 ||
            RrIntervalNs is <= 0 or > 10_000_000_000)
        { throw new EventWaveformException("EcgQt.InvalidCorrection", "correction"); }
        int degree = MethodId == Bazett ? 2 : 3;
        Int128 Power(long value) => degree == 2 ? (Int128)value * value : (Int128)value * value * value;
        Int128 target = Power(QtcIntervalNs) * RrIntervalNs;
        const long seconds = 1_000_000_000;
        long lower = 0, upper = 4_000_000_000;
        while (upper - lower > 1)
        {
            long middle = lower + (upper - lower) / 2;
            if (Power(middle) * seconds <= target) { lower = middle; }
            else { upper = middle; }
        }
        // Compare the exact root to lower + 1/2, avoiding rounded intermediates.
        Int128 midpoint = Power(2 * lower + 1) * seconds;
        Int128 scaledTarget = target * (1 << degree);
        return scaledTarget > midpoint || (scaledTarget == midpoint && (lower & 1) != 0)
            ? lower + 1 : lower;
    }

    public EcgCycleTiming ResolveTiming(EcgCycleTiming shapeTiming)
    {
        shapeTiming.Validate();
        var timing = shapeTiming with { RrIntervalNs = RrIntervalNs, QtIntervalNs = ResolveQtIntervalNs() };
        // Preserve explicit P/PR/QRS/T durations; reject impossible ST/next-P fits.
        timing.Validate();
        return timing;
    }
}
