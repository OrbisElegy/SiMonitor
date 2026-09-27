// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SeededRateSpecifications
{
    private static readonly string Seed = new('1', 64);
    public static Specification[] All =>
    [new(nameof(SeededRateKeepsSharedTimingAndRestores), SeededRateKeepsSharedTimingAndRestores),
        new(nameof(SeededRatesGenerateRealMeasuredSignals), SeededRatesGenerateRealMeasuredSignals),
        new(nameof(NinetySecondVariationSurvivesMeasurementWindows), NinetySecondVariationSurvivesMeasurementWindows)];
    private static void NinetySecondVariationSurvivesMeasurementWindows()
    {
        bool visible = true;
        foreach (string seed in new[] { new string('0', 63) + "1", Seed, new string('2', 64) })
        {
            var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default with { SeededRate = new(75, seed, 50) },
                MonitorDisplayConfiguration.Default(), enableMeasurements: true, opticalSaturationMilliPercent: 98000,
                opticalVariation: new(98000, 1000, seed));
            List<int> hr = [], pr = [], spo2 = [];
            for (int i = 1; i <= 450; i++)
            {
                session.Advance(200_000_000);
                if (i < 100) { continue; } // Exclude startup/short averaging windows.
                var reading = session.Measurements!;
                Check.That(reading.HeartRate.Status == WaveformMeasurementStatus.Valid && reading.PulseRate.Status == WaveformMeasurementStatus.Valid &&
                    reading.SpO2.Status == WaveformMeasurementStatus.Valid, "slow teaching variation retains usable readings");
                hr.Add(reading.HeartRate.MilliBeatsPerMinute!.Value);
                pr.Add(reading.PulseRate.MilliBeatsPerMinute!.Value);
                spo2.Add(reading.SpO2.SaturationMilliPercent!.Value);
            }
            int TextCount(List<int> values) => values.Select(v => ((decimal)v / 1000).ToString("0", CultureInfo.InvariantCulture)).Distinct().Count();
            Console.WriteLine($"90s seed={seed[..4]} HR={hr.Min()}..{hr.Max()} PR={pr.Min()}..{pr.Max()} SpO2={spo2.Min()}..{spo2.Max()} texts={TextCount(hr)}/{TextCount(pr)}/{TextCount(spo2)}");
            visible &= hr.Max() - hr.Min() >= 3000 && pr.Max() - pr.Min() >= 3000 && spo2.Max() - spo2.Min() >= 1200 &&
                TextCount(hr) >= 3 && TextCount(pr) >= 3 && TextCount(spo2) >= 2;
        }
        Check.That(visible, "configured5% RR and1 percentage-point optical variation must survive90s measurement/whole-number display");
    }
    private static void SeededRateKeepsSharedTimingAndRestores()
    {
        var rate = new SeededCardiacRate(75, Seed, 50);
        var plan = (PhysiologyIllustrationConfiguration.Default with { SeededRate = rate }).ResolvePlan();
        var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(240_000_000_000, 10000);
        var split = RegularPhysiologyTimeline.Start(plan); var events = new List<PhysiologyCycleEvent>();
        for (int i = 1; i <= 1200; i++)
        {
            events.AddRange(split.AdvanceBefore(i * 200_000_000L, 100));
            if (i == 501) { split = RegularPhysiologyTimeline.Restore(split.CaptureState()); }
        }
        Check.That(all.SequenceEqual(events), "chunking and checkpoint preserve seeded event sequence through256-beat wrap");
        var atrial = all.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
        long[] intervals = atrial.Zip(atrial.Skip(1)).Select(p => p.Second.SimTimeNs - p.First.SimTimeNs).ToArray();
        Check.That(intervals.Distinct().Count() > 100 && intervals.All(x => x >= rate.MinimumPeriodNs && x <= rate.PeriodNs * 105 / 100), "correlated interval variation is bounded and nonconstant");
        Check.That(atrial[256].SimTimeNs - atrial[0].SimTimeNs == 256 * rate.PeriodNs,
            "paired interval excursions preserve exact nominal256-beat mean");
        foreach (var beat in atrial.Take(100))
        {
            var mechanical = all.Single(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical && e.CycleIndex == beat.CycleIndex);
            Check.That(mechanical.SimTimeNs - beat.SimTimeNs == plan.VentricularMechanicalOffsetNs, "electrical and ejection clocks share identical seeded jitter");
        }
        var same = new SeededCardiacRate(75, Seed, 50);
        Check.That(rate.PreparedState == same.PreparedState && all.SequenceEqual(RegularPhysiologyTimeline.Start(plan with { SeededRate = same }).AdvanceBefore(240_000_000_000, 10000)), "seed and full preparation state reproduce");
        var changed = new SeededCardiacRate(75, new string('2', 64), 50);
        Check.That(!all.SequenceEqual(RegularPhysiologyTimeline.Start(plan with { SeededRate = changed }).AdvanceBefore(240_000_000_000, 10000)), "different seed changes actual events");
        var fixedRate = new SeededCardiacRate(75, Seed, 0);
        Check.That(RegularPhysiologyTimeline.Start(plan with { SeededRate = fixedRate }).AdvanceBefore(10_000_000_000, 1000)
            .SequenceEqual(RegularPhysiologyTimeline.Start(plan with { SeededRate = null }).AdvanceBefore(10_000_000_000, 1000)), "zero spread preserves regular timeline exactly");
        var late = RegularPhysiologyTimeline.Restore(new RegularPhysiologyState(plan, 86_400_000_000_000));
        Check.That(late.AdvanceBefore(86_401_000_000_000, 100).Count > 0, "one-day lookup needs only a one-second event budget");
        foreach (string? invalidSeed in new[] { "", new string('A', 64), new string('0', 63), new string('z', 64) })
        {
            bool invalid = false;
            try { _ = new SeededCardiacRate(75, invalidSeed, 50); } catch (ArgumentException) { invalid = true; }
            Check.That(invalid, "invalid root seed rejects before preparing events");
        }
        var bounded = RegularPhysiologyTimeline.Start(plan); var before = bounded.CaptureState();
        bool rejected = false;
        try { bounded.AdvanceBefore(240_000_000_000, 1); } catch (PhysiologyTimelineException) { rejected = true; }
        Check.That(rejected && bounded.CaptureState() == before, "budget failure remains atomic");
    }
    private static void SeededRatesGenerateRealMeasuredSignals()
    {
        foreach (int bpm in new[] { 30, 75, 100, 158, 180 })
        {
            var configuration = PhysiologyIllustrationConfiguration.Default with { SeededRate = new(bpm, Seed, 50) };
            var source = PhysiologyIllustrationSource.Create(configuration);
            var owner = LiveWaveformMeasurements.CreateIllustration(); LiveMeasurementSnapshot? reading = null;
            for (int i = 1; i <= 60; i++)
                foreach (byte[] wire in source.AdvanceTo(i * 200_000_000L, 50, 1, 100)) { reading = owner.Consume(wire); }
            Check.That(reading?.HeartRate.Status == WaveformMeasurementStatus.Valid && Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - bpm * 1000) < bpm * 100,
                "source-derived HR tracks adjusted seeded rhythm: " + bpm + " " + reading?.HeartRate);
            Check.That(reading!.AbpMean.Status == WaveformMeasurementStatus.Valid && reading.PaMean.Status == WaveformMeasurementStatus.Valid,
                "perfusion samples remain valid across supported rates");
        }
        foreach (int period in new[] { 1000, 10000 })
        {
            var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
            { BreathPeriodMilliseconds = period, InspirationMilliseconds = period / 2, Co2EndExpiratoryMmHg = 80 });
            for (int i = 1; i <= 20; i++) { source.AdvanceTo(i * 200_000_000L, 50, 1, 100); }
        }
    }
}
