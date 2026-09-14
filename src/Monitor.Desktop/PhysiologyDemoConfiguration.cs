// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record PhysiologyDemoConfiguration(int BreathPeriodMilliseconds,
    int InspirationMilliseconds, int RespAmplitudeCounts, int? Co2PlateauStartCentiMmHg = null,
    int Co2BaselineMmHg = 0, int Co2EndExpiratoryMmHg = 40, int Co2DeadSpaceMilliseconds = 125,
    int Co2RiseMilliseconds = 250, int Co2FallMilliseconds = 200, int Co2TransportDelayMilliseconds = 0)
{
    internal static PhysiologyDemoConfiguration Default { get; } = new(3750, 1875, 1000);

    internal CapnogramPlan ResolveCapnogram()
    {
        // Match the demo's fixed0..80mmHg display range; source limits are separate.
        if (Co2BaselineMmHg is < 0 or > 80 || Co2EndExpiratoryMmHg is < 0 or > 80)
        { throw new ArgumentException("PhysiologyDemo.InvalidCo2Pressure"); }
        if (Co2TransportDelayMilliseconds is < 0 or > 5000)
        { throw new ArgumentException("PhysiologyDemo.InvalidCo2TransportDelay"); }
        return new(Co2DeadSpaceMilliseconds * 1_000_000L, Co2RiseMilliseconds * 1_000_000L,
            Co2FallMilliseconds * 1_000_000L, Co2BaselineMmHg, Co2EndExpiratoryMmHg, Co2PlateauStartCentiMmHg, Co2TransportDelayMilliseconds * 1_000_000L);
    }

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
