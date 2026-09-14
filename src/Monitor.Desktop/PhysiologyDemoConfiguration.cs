// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record PhysiologyDemoConfiguration(int BreathPeriodMilliseconds,
    int InspirationMilliseconds, int RespAmplitudeCounts)
{
    internal static PhysiologyDemoConfiguration Default { get; } = new(3750, 1875, 1000);

    internal RegularPhysiologyPlan ResolvePlan()
    {
        // Demo input/display bounds, not physiological normal ranges.
        if (BreathPeriodMilliseconds is < 1000 or > 10000 || InspirationMilliseconds <= 0 ||
            InspirationMilliseconds >= BreathPeriodMilliseconds || RespAmplitudeCounts is < -1000 or > 1000)
        { throw new ArgumentException("PhysiologyDemo.InvalidConfiguration"); }
        var timing = TextbookEcgReference.Timing;
        return new(0, timing.RrIntervalNs, timing.PrIntervalNs, 80_000_000,
            timing.PrIntervalNs + 80_000_000, BreathPeriodMilliseconds * 1_000_000L, InspirationMilliseconds * 1_000_000L);
    }
}
