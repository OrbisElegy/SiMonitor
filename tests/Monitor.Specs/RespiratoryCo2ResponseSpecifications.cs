// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RespiratoryCo2ResponseSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(CheyneStokesCo2TracksVentilationWithReservoirLag), CheyneStokesCo2TracksVentilationWithReservoirLag),
        new(nameof(CheyneStokesCo2OwnsGainAndPreservesTransport), CheyneStokesCo2OwnsGainAndPreservesTransport),
    ];
    private static RegularPhysiologyPlan Plan(long period) => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000,
        period, period * 2 / 5, RespiratoryPattern: RespiratoryPattern.CheyneStokesIllustration);
    private static CapnogramPlan Gas => new(125_000_000, 250_000_000, 200_000_000, 2, 40, 3000);
    private static void CheyneStokesCo2TracksVentilationWithReservoirLag()
    {
        int[] depths = [200, 400, 600, 800, 1000, 800, 600, 400, 200, 0, 0];
        foreach (long period in new long[] { 1_000_000_000, 3_750_000_000, 10_000_000_000 })
        {
            var plan = Plan(period);
            var channel = Gas.CreateChannel(plan, Guid.NewGuid(), 0);
            var gains = channel.Bands[0].ExpirationCycleGainsPermille!;
            double t = period / 1e9, x = 1;
            // Independent iterative floating-point reference is test-only.
            for (int cycle = 0; cycle < 2000; cycle++)
                foreach (int depth in depths) { x = (20 * x + t) / (20 + t * depth / 500.0); }
            for (int slot = 0; slot < 11; slot++)
            {
                x = (20 * x + t) / (20 + t * depths[slot] / 500.0);
                Check.That(Math.Abs(gains[slot] - x * 1000) <= 0.500001, "exact periodic solution agrees with independent mass-balance recurrence");
            }
            Check.That(gains[4] > gains[6] && gains[0] > gains[6] && gains[10] > gains[9] && gains[9] > gains[8], "delayed CO2 nadir follows deepest breath; reservoir rises during apnea");
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(23 * period, 2000);
            var wave = EventWaveformComposition.Restore(new(channel.Bands, events));
            for (int slot = 0; slot < 9; slot++)
            {
                long peak = wave.EvaluateAt((slot + 1) * period);
                long expected = checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)3800 * FixedPointMath.Q32One * gains[slot], 1000));
                Check.That(peak == expected && peak == wave.EvaluateAt((slot + 12) * period), "end-expiratory excess follows depth response and repeats on original cycle grid");
            }
            Check.That(wave.EvaluateAt(10 * period) == 0, "apnea has no newly measured exhaled CO2 despite rising reservoir");
            var regular = Gas.CreateChannel(plan with { RespiratoryPattern = RespiratoryPattern.Regular }, Guid.NewGuid(), 0);
            Check.That(regular.Bands.All(b => b.ExpirationCycleGainsPermille is null), "regular breathing keeps original tables and gain path");
        }
        try { (Gas with { EndExpiratoryMmHg = 327 }).CreateChannel(Plan(3_750_000_000), Guid.NewGuid(), 0); }
        catch (EventWaveformException e) when (e.ReasonCode == "Capnogram.Co2ResponseOutOfRange") { return; }
        throw new InvalidOperationException("Coupled CO2 overflow accepted.");
    }
    private static void CheyneStokesCo2OwnsGainAndPreservesTransport()
    {
        var plan = Plan(1_000_000_000);
        var plain = Gas.CreateChannel(plan, Guid.NewGuid(), 0);
        var delayed = (Gas with { TransportDelayNs = 300_000_000, DispersionStepNs = 100_000_000 }).CreateChannel(plan, Guid.NewGuid(), 0);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(23_000_000_000, 300);
        var a = EventWaveformComposition.Restore(new(plain.Bands, events));
        var b = EventWaveformComposition.Restore(new(delayed.Bands, events));
        for (long time = 600_000_000; time < 22_000_000_000; time += 10_000_000)
        {
            Int128 expected = (Int128)a.EvaluateAt(time - 300_000_000) + 2 * (Int128)a.EvaluateAt(time - 400_000_000) + a.EvaluateAt(time - 500_000_000);
            Check.That(Int128.Abs((Int128)b.EvaluateAt(time) * 4 - expected) <= 12, "all dispersion paths retain their triggering breath's concentration, including pause/wrap");
        }
        int[] mutable = plain.Bands[0].ExpirationCycleGainsPermille!.ToArray();
        var owned = EventWaveformComposition.Restore(new([plain.Bands[0] with { ExpirationCycleGainsPermille = mutable }], events));
        long before = owned.EvaluateAt(1_000_000_000); mutable[0] = 0;
        Check.That(owned.EvaluateAt(1_000_000_000) == before, "gain array owned defensively");
        var band = plain.Bands[0];
        foreach (var invalid in new[] { band with { ExpirationCycleGainsPermille = [] }, band with { ExpirationCycleGainsPermille = [-1] }, band with { Trigger = PhysiologyCycleEventKind.InspirationStart }, band with { ExpirationCycleGainsPermille = [10001] } })
        {
            try { EventWaveformComposition.Restore(new([invalid], [])); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Invalid expiratory gain accepted.");
        }
        ulong cycle = ulong.MaxValue;
        var late = EventWaveformComposition.Restore(new(plain.Bands, [new(400_000_000, PhysiologyCycleEventKind.ExpirationStart, cycle)]));
        var samePhase = EventWaveformComposition.Restore(new(plain.Bands, [new(400_000_000, PhysiologyCycleEventKind.ExpirationStart, cycle % 11)]));
        Check.That(late.EvaluateAt(1_000_000_000) == samePhase.EvaluateAt(1_000_000_000), "cycle lookup bounded without accumulated replay");
    }
}
