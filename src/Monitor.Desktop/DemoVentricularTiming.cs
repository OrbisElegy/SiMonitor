// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

internal static class DemoVentricularTiming
{
    internal static long ResolveOffset(int? periodMilliseconds, int? offsetMilliseconds, long fallbackNs) =>
        Monitor.Simulation.Authoring.IllustrationVentricularTiming.ResolveOffset(periodMilliseconds, offsetMilliseconds, fallbackNs);
}
