// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

// Local demo pacing only. Preserve ordinary frame delays; a suspension longer
// than250ms retains the existing no-catch-up behavior and advances50ms instead.
internal static class DemoFrameTiming
{
    internal const long MaximumChunkNs = 50_000_000;
    internal const long MaximumElapsedNs = 250_000_000;

    internal static long ResolveElapsed(TimeSpan elapsed) => elapsed.Ticks > MaximumElapsedNs / 100
        ? MaximumChunkNs : Math.Max(1, Math.Max(0, elapsed.Ticks) * 100);
}
