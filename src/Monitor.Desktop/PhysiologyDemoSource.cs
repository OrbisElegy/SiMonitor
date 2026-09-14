// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PhysiologyDemoSource
{
    internal static Guid ChannelId(int row) => row switch
    {
        0 => Guid.Parse("11111111-1111-4111-8111-111111111111"),
        1 => Guid.Parse("33333333-3333-4333-8333-333333333333"),
        2 => Guid.Parse("22222222-2222-4222-8222-222222222222"),
        3 => Guid.Parse("44444444-4444-4444-8444-444444444444"),
        4 => Guid.Parse("55555555-5555-4555-8555-555555555555"),
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    internal static PhysiologyWaveformGroup Create()
    {
        EcgCycleTiming timing = TextbookEcgReference.Timing;
        RegularPhysiologyPlan plan = new(0, timing.RrIntervalNs, timing.PrIntervalNs, 80_000_000,
            timing.PrIntervalNs + 80_000_000, 3_750_000_000, 1_875_000_000);
        return PhysiologyWaveformGroup.Start(ChannelId(0), ChannelId(2), 1, 1, 1, 0, 16,
            [new(plan, new(ChannelId(0), "AcqECGMonitor250@1", 1, 1, 0, 1),
                TextbookEcgReference.CreateBands(), 10, 0),
             new(plan, new(ChannelId(1), "AcqResp125@1", 1, 1, 0, 1),
                [new(PhysiologyCycleEventKind.InspirationStart, 0, 3_750_000_000, PhysiologyDemoTables.Resp)], 10, 0),
             new(plan, new(ChannelId(2), "AcqPleth125@1", 1, 1, 0, 1),
                new PlethPulsePlan(80_000_000, 512_000_000, 1000).CreateBands(), 250, 0),
             new ArterialPulsePlan(80_000_000, 600_000_000, 80, 40).CreateChannel(plan, ChannelId(3), 0),
             new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40).CreateChannel(plan, ChannelId(4), 0)]);
    }
}
