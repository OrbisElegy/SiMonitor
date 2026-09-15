// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

internal static class DemoVentricularTiming
{
    internal static long ResolveOffset(int? periodMilliseconds, int? offsetMilliseconds, long fallbackNs)
    {
        if (offsetMilliseconds is not { } offset) { return fallbackNs; }
        if (periodMilliseconds is not { } period || offset < 0 || (long)offset + 80 >= period)
        { throw new ArgumentException("DemoVentricularTiming.InvalidOffset"); }
        return offset * 1_000_000L;
    }
}
