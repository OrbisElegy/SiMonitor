// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SpontaneousTransportSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000,
        240_000_000, 1_000_000_000, 400_000_000, CardiacActivity: CardiacActivity.Absent,
        RespiratoryPattern: RespiratoryPattern.SpontaneousIllustration)
    {
        InspiratoryPauseNs = 80_000_000,
        ExpiratoryPauseNs = 60_000_000,
    };
    private static readonly VentilationTransportPlan Ventilation = new(450_000, 150_000, 210_000);

    public static Specification[] All =>
    [
        new(nameof(SpontaneousTransportSharesBreathEventsDepthAndPauses), SpontaneousTransportSharesBreathEventsDepthAndPauses),
        new(nameof(SpontaneousVolumesSurvivePartitionsRestoreAndFarQueries), SpontaneousVolumesSurvivePartitionsRestoreAndFarQueries),
        new(nameof(SpontaneousOxygenationValidatesEverySlotBeforePublication), SpontaneousOxygenationValidatesEverySlotBeforePublication),
        new(nameof(SpontaneousOxygenationRetainsGridAndReservoirsAcrossChanges), SpontaneousOxygenationRetainsGridAndReservoirsAcrossChanges),
    ];

    private static PhysiologyTransportSource Transport(RegularPhysiologyPlan? plan = null,
        VentilationTransportPlan? ventilation = null) => PhysiologyTransportSource.Create(plan ?? Plan,
            ventilation ?? Ventilation, new(0, 100_000_000));

    private static void SpontaneousTransportSharesBreathEventsDepthAndPauses()
    {
        var plan = Plan;
        var transport = Transport(plan);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(32_000_000_001, 100);
        var inspirations = events.Where(item => item.Kind == PhysiologyCycleEventKind.InspirationStart).ToArray();
        var expirations = events.Where(item => item.Kind == PhysiologyCycleEventKind.ExpirationStart).ToArray();
        var channel = new RespirationPlan(1000).CreateChannel(plan, Guid.Parse("33333333-3333-4333-8333-333333333333"), 0);
        var waveform = EventWaveformComposition.Restore(new(channel.Bands, events));
        Check.That(inspirations.Length == 33 && expirations.Length == 32 && inspirations[^1].SimTimeNs == 32_000_000_000,
            "32 unequal spontaneous breaths retain their exact nominal group duration and complete event identities");
        HashSet<long> periods = [];
        HashSet<int> depths = [];
        for (int cycle = 0; cycle < expirations.Length; cycle++)
        {
            long start = inspirations[cycle].SimTimeNs;
            long expiration = expirations[cycle].SimTimeNs;
            long next = inspirations[cycle + 1].SimTimeNs;
            // This fixture's active inspiration is exactly four fifths of its
            // event-delimited inspiration, independently of the schedule API.
            long flowEnd = start + (expiration - start) * 4 / 5;
            int depth = checked((int)(waveform.EvaluateAt(expiration) / FixedPointMath.Q32One));
            depths.Add(depth);
            periods.Add(next - start);
            var whole = transport.Integrate(start, next);
            long expectedTidal = (long)Ventilation.TidalVolumeMicrolitersBtps * depth;
            Check.That(whole.DeliveredVolumeNanolitersBtps == expectedTidal &&
                whole.AlveolarVentilationNanolitersBtps == expectedTidal - 150_000_000,
                "the measured RESP peak and physical tidal volume use the same original breath ordinal");
            Check.That(transport.Integrate(start, flowEnd).DeliveredVolumeNanolitersBtps == expectedTidal &&
                transport.Integrate(flowEnd, next).DeliveredVolumeNanolitersBtps == 0 &&
                transport.Integrate(start, start + 8_000_000).DeliveredVolumeNanolitersBtps > 0 &&
                transport.Integrate(flowEnd - 8_000_000, flowEnd).DeliveredVolumeNanolitersBtps > 0,
                "gas delivery follows actual inspiration and stops throughout its hold, expiration and expiratory pause");
        }
        Check.That(periods.Count > 1 && depths.SetEquals([920, 960, 1000, 1040, 1080]),
            "flow validation covers all five authored depths and multiple actual breath durations");
        var closed = Transport(ventilation: Ventilation with { AirwayOpen = false });
        Check.That(closed.Integrate(0, 32_000_000_000).DeliveredVolumeNanolitersBtps == 0,
            "spontaneous effort does not deliver gas through a closed airway");
    }

    private static void SpontaneousVolumesSurvivePartitionsRestoreAndFarQueries()
    {
        var source = Transport();
        var restored = PhysiologyTransportSource.Restore(JsonSerializer.Deserialize<PhysiologyTransportState>(
            JsonSerializer.Serialize(source.CaptureState()))!);
        var whole = source.Integrate(0, 32_000_000_000);
        long delivered = 0, alveolar = 0;
        for (long from = 0; from < whole.ToExclusiveSimTimeNs;)
        {
            long to = Math.Min(whole.ToExclusiveSimTimeNs, from + 73_000_003);
            var part = source.Integrate(from, to);
            Check.That(part == restored.Integrate(from, to), "JSON recovery preserves each irregular partial breath integral");
            delivered += part.DeliveredVolumeNanolitersBtps;
            alveolar += part.AlveolarVentilationNanolitersBtps;
            from = to;
        }
        Check.That(delivered == whole.DeliveredVolumeNanolitersBtps && alveolar == whole.AlveolarVentilationNanolitersBtps &&
            delivered == 32 * 450_000_000L && alveolar == 32 * 300_000_000L,
            "prefix differences conserve nanoliters across arbitrary partitions and the balanced depth sequence preserves group volume");
        long far = long.MaxValue - 1_234_567_891;
        long representative = far % 32_000_000_000;
        var late = source.Integrate(far, long.MaxValue);
        var early = source.Integrate(representative, representative + (long.MaxValue - far));
        Check.That(late == restored.Integrate(far, long.MaxValue) &&
            late.DeliveredVolumeNanolitersBtps == early.DeliveredVolumeNanolitersBtps &&
            late.AlveolarVentilationNanolitersBtps == early.AlveolarVentilationNanolitersBtps,
            "near-long-max lookup retains the same bounded ordinal phase without accumulated schedule drift");
        Reject(() => source.Integrate(0, 32_000_000_000, 31));
        Check.That(source.Integrate(0, 32_000_000_000) == whole,
            "event-budget rejection leaves the immutable spontaneous transport definition unchanged");
    }

    private static void SpontaneousOxygenationValidatesEverySlotBeforePublication()
    {
        var fast = Plan with { InspirationDurationNs = 40_000_000, InspiratoryPauseNs = 0, ExpiratoryPauseNs = 0 };
        var excessive = Ventilation with { TidalVolumeMicrolitersBtps = 480_000 };
        _ = new RealtimeOxygenationSource(Transport(fast with { RespiratoryPattern = RespiratoryPattern.Regular }, excessive), new());
        Reject(() => _ = new RealtimeOxygenationSource(Transport(fast, excessive), new()));
        var source = new RealtimeOxygenationSource(Transport(fast), new());
        var before = source.Snapshot;
        Reject(() => source.ChangeVentilation(excessive, 0));
        Reject(() => source.ChangeTransport(Transport(fast, excessive), 0, 1));
        Check.That(source.Snapshot == before && source.Ventilation == Ventilation,
            "a later deep/short slot exceeding the RK4 flow bound rejects before replacing current controls or stores");
        var closed = new RealtimeOxygenationSource(Transport(fast, excessive with { AirwayOpen = false }), new());
        Reject(() => closed.ChangeVentilation(excessive, 0));
        Check.That(!closed.Ventilation.AirwayOpen, "opening an airway validates the complete future spontaneous pattern");
        var tiny = fast with { BreathPeriodNs = 5_000_000, InspirationDurationNs = 2_000_000 };
        Reject(() => _ = new RealtimeOxygenationSource(Transport(tiny,
            Ventilation with { TidalVolumeMicrolitersBtps = 80_000, DeadSpaceMicrolitersBtps = 0 }), new()));
    }

    private static void SpontaneousOxygenationRetainsGridAndReservoirsAcrossChanges()
    {
        var large = new RealtimeOxygenationSource(Transport(), new());
        var small = new RealtimeOxygenationSource(Transport(), new());
        for (long time = 250_000_000; time <= 32_000_000_000; time += 250_000_000)
        {
            large.AdvanceTo(time);
            small.AdvanceTo(time - 125_000_000);
            small.AdvanceTo(time);
            Check.That(large.Snapshot == small.Snapshot, "irregular breath transport remains independent of owner frame partitions");
            if (time == 8_000_000_000)
            {
                var before = large.Snapshot;
                var next = Transport(ventilation: Ventilation with { InspiredOxygenMillionths = 300_000 });
                large.ChangeTransport(next, time, 1);
                small.ChangeTransport(next, time, 1);
                Check.That(large.Snapshot == before, "a transport change preserves oxygen reservoirs and source history");
            }
            if (time == 16_000_000_000) { small = small.Fork(); }
        }
        Check.That(Math.Abs(large.Snapshot.OxygenBalanceResidualMl) < .000000000000000001m,
            "natural breath timing and depth keep the solved oxygen ledger conserved");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected argument rejection.");
    }
}
