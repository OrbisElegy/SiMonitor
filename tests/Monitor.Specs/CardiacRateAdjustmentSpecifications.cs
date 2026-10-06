// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CardiacRateAdjustmentSpecifications
{
    private static readonly string Seed = new('1', 64);
    public static Specification[] All =>
    [
        new(nameof(SeededRhythmsReplayWithoutRateVariation), SeededRhythmsReplayWithoutRateVariation),
        new(nameof(PauseDurationIsIndependentOfNormalRate), PauseDurationIsIndependentOfNormalRate),
        new(nameof(FlutterPercentageControlsConductedFraction), FlutterPercentageControlsConductedFraction),
        new(nameof(SeededAfSelectionAndPerfusionAgree), SeededAfSelectionAndPerfusionAgree),
        new(nameof(IndependentRatesPreserveDissociation), IndependentRatesPreserveDissociation),
        new(nameof(AdjustedPatternsPreserveEventsAndRecovery), AdjustedPatternsPreserveEventsAndRecovery),
        new(nameof(RonTCouplingSurvivesRateChanges), RonTCouplingSurvivesRateChanges),
        new(nameof(FlutterCyclesRemainContinuous), FlutterCyclesRemainContinuous),
        new(nameof(AdjustedRateRejectsInvalidModes), AdjustedRateRejectsInvalidModes)
    ];

    private static void SeededRhythmsReplayWithoutRateVariation()
    {
        foreach (var reference in new[] { SinusArrestReference.CreatePlan(), SinusArrhythmiaReference.CreatePlan(),
            AtrialFlutterReference.CreateVariablePlan(), AtrialFibrillationReference.CreatePlan() })
        {
            var plan = reference with { RhythmSchedule = new(Seed) };
            var expected = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(240_000_000_000, 10000);
            var replay = RegularPhysiologyTimeline.Start(reference with { RhythmSchedule = new(Seed) }).AdvanceBefore(240_000_000_000, 10000);
            Check.That(expected.SequenceEqual(replay), "global seed reproduces intrinsic rhythm without a rate override");
            var changed = RegularPhysiologyTimeline.Start(reference with { RhythmSchedule = new(new string('2', 64)) }).AdvanceBefore(240_000_000_000, 10000);
            Check.That(!expected.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).SequenceEqual(
                changed.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical)), "seed changes intrinsic event sequence");
            var split = RegularPhysiologyTimeline.Start(plan);
            var first = split.AdvanceBefore(17_333_333_333, 10000);
            var restored = RegularPhysiologyTimeline.Restore(split.CaptureState());
            Check.That(expected.SequenceEqual(first.Concat(restored.AdvanceBefore(240_000_000_000, 10000))), "arbitrary cut and recovery preserve seeded events across table wrap");
        }
    }

    private static void PauseDurationIsIndependentOfNormalRate()
    {
        foreach (int rate in new[] { 50, 60, 75 })
        {
            var plan = SinusArrestReference.CreatePlan() with { RhythmSchedule = new(Seed, 2_700_000_000), RateAdjustment = new(rate, null, Seed, 0) };
            var timeline = RegularPhysiologyTimeline.Start(plan);
            var events = timeline.AdvanceBefore(480_000_000_000, 10000);
            var atrial = events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
            long[] intervals = atrial.Zip(atrial.Skip(1)).Select(p => p.Second.SimTimeNs - p.First.SimTimeNs).ToArray();
            Check.That(intervals.Contains(2_700_000_000) && intervals.Contains(60_000_000_000 / rate) &&
                intervals.All(n => n == 2_700_000_000 || n == 60_000_000_000 / rate), "normal PP changes while pauses retain configured duration across wrap");
            var split = RegularPhysiologyTimeline.Start(plan);
            var first = split.AdvanceBefore(13_123_456_789, 10000);
            Check.That(events.SequenceEqual(first.Concat(RegularPhysiologyTimeline.Restore(split.CaptureState()).AdvanceBefore(480_000_000_000, 10000))), "pause retiming inverse preserves window boundaries");
        }
        var varied = SinusArrestReference.CreatePlan() with { RhythmSchedule = new(Seed, 2_700_000_000), RateAdjustment = new(60, null, Seed, 50) };
        var beats = RegularPhysiologyTimeline.Start(varied).AdvanceBefore(60_000_000_000, 1000).Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
        long[] gaps = beats.Zip(beats.Skip(1)).Select(p => p.Second.SimTimeNs - p.First.SimTimeNs).ToArray();
        Check.That(gaps.Where(n => n > 1_100_000_000).All(n => n == 2_700_000_000) && gaps.Distinct().Count() > 2, "slow variation leaves pauses fixed");
    }

    private static void FlutterPercentageControlsConductedFraction()
    {
        foreach (int percent in new[] { 10, 25, 33, 40, 50 })
        {
            var plan = AtrialFlutterReference.CreateVariablePlan() with { RhythmSchedule = new(Seed, conductionPercent: percent) };
            var beats = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(500_000_000_000, 20000)
                .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Take(241).ToArray();
            long[] gaps = beats.Zip(beats.Skip(1)).Select(p => p.Second.SimTimeNs - p.First.SimTimeNs).ToArray();
            Check.That(gaps.Length == 240 && gaps.All(n => n >= 400_000_000 && n % 200_000_000 == 0), "all conducted beats remain on atrial grid with minimum support");
            decimal actual = 240 * 100m / (gaps.Sum() / 200_000_000);
            Check.That(Math.Abs(actual - percent) < 0.1m, "prepared sequence realizes target conduction percentage");
        }
    }

    private static void SeededAfSelectionAndPerfusionAgree()
    {
        var plan = AtrialFibrillationReference.CreatePlan() with { RhythmSchedule = new(Seed) };
        var beats = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(220_000_000_000, 10000)
            .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
        int selected = 0;
        for (int i = 2; i < beats.Length; i++)
        {
            bool expected = beats[i].SimTimeNs - beats[i - 1].SimTimeNs < 600_000_000 && beats[i - 1].SimTimeNs - beats[i - 2].SimTimeNs >= 900_000_000;
            Check.That(AtrialFibrillationReference.IsLongShortBeat(plan, beats[i].CycleIndex) == expected, "aberrancy follows actual seeded long-short sequence");
            Check.That((AtrialFibrillationPerfusion.GainPermille(plan, beats[i].CycleIndex, true) == 0) == expected, "pulse deficit follows the same seeded beat selection");
            if (expected) { selected++; }
        }
        Check.That(selected > 0, "seeded sequence exercises long-short selection");
    }

    private static void IndependentRatesPreserveDissociation()
    {
        var plan = CompleteAvBlockVentricularReference.CreatePlan() with { RateAdjustment = new(40, 100, Seed, 0) };
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(12_000_000_000, 1000);
        var atrial = events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
        var ventricular = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
        Check.That(atrial.Length == 20 && ventricular.Length == 8, "independent 100 bpm atrial and 40 bpm ventricular clocks");
        Check.That(atrial.Zip(atrial.Skip(1)).All(p => p.Second.SimTimeNs - p.First.SimTimeNs == 600_000_000), "atrial PP interval");
        Check.That(ventricular.Zip(ventricular.Skip(1)).All(p => p.Second.SimTimeNs - p.First.SimTimeNs == 1_500_000_000), "ventricular RR interval");
        var mechanics = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        Check.That(ventricular.Zip(mechanics).All(p => p.Second.SimTimeNs - p.First.SimTimeNs == 80_000_000), "electromechanical delay is not scaled");
        var fasterAtria = RegularPhysiologyTimeline.Start(plan with { RateAdjustment = new(40, 120, Seed, 0) }).AdvanceBefore(12_000_000_000, 1000);
        Check.That(ventricular.SequenceEqual(fasterAtria.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical)), "changing atrial rate does not move ventricular events");
    }

    private static void AdjustedPatternsPreserveEventsAndRecovery()
    {
        RegularPhysiologyPlan[] plans = [SinusArrhythmiaReference.CreatePlan(), SinusArrestReference.CreatePlan(),
            AtrialFibrillationReference.CreatePlan(), AtrialFlutterReference.CreatePlan(2),
            CompleteAvBlockJunctionalReference.CreatePlan()];
        foreach (var original in plans)
        {
            int bpm = original.IndependentVentricularPeriodNs is null ? (int)(60_000_000_000 / original.HeartPeriodNs * 4 / 5) : 40;
            var plan = original with { RateAdjustment = new(bpm, original.IndependentVentricularPeriodNs is null ? null : 60, Seed, 50) };
            var whole = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(120_000_000_000, 10000);
            var timeline = RegularPhysiologyTimeline.Start(plan);
            List<PhysiologyCycleEvent> split = [];
            for (long end = 137_000_000; end < 120_000_000_000; end += 137_000_000)
            {
                split.AddRange(timeline.AdvanceBefore(end, 100));
                timeline = RegularPhysiologyTimeline.Restore(timeline.CaptureState());
            }
            split.AddRange(timeline.AdvanceBefore(120_000_000_000, 100));
            Check.That(whole.SequenceEqual(split), "arbitrary cuts and checkpoints preserve adjusted event times");
            var baseEvents = RegularPhysiologyTimeline.Start(original).AdvanceBefore(120_000_000_000, 10000);
            foreach (var kind in new[] { PhysiologyCycleEventKind.AtrialElectrical, PhysiologyCycleEventKind.VentricularElectrical })
            {
                var actual = whole.Where(e => e.Kind == kind).ToArray();
                Check.That(actual.Select(e => e.CycleIndex).SequenceEqual(baseEvents.Where(e => e.Kind == kind).Take(actual.Length).Select(e => e.CycleIndex)),
                    "retiming preserves missing beats and cycle identities");
            }
            Check.That(whole.Where(e => e.Kind == PhysiologyCycleEventKind.InspirationStart).SequenceEqual(
                baseEvents.Where(e => e.Kind == PhysiologyCycleEventKind.InspirationStart)), "cardiac adjustment leaves breathing unchanged");
        }
    }

    private static void AdjustedRateRejectsInvalidModes()
    {
        var reference = CompleteAvBlockVentricularReference.CreatePlan();
        foreach (var rate in new[] { new CardiacRateAdjustment(75, 100, Seed, 0), new(40, 40, Seed, 0), new(30, null, Seed, 0) })
        {
            bool rejected = false;
            try { _ = RegularPhysiologyTimeline.Start(reference with { RateAdjustment = rate }); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid independent rates are rejected before publishing state");
        }
    }

    private static void RonTCouplingSurvivesRateChanges()
    {
        foreach (var pattern in new[] { AvConductionPattern.RonTLongQtPvcIllustration, AvConductionPattern.ShortCoupledRonTPvcIllustration })
        {
            var plan = PrematureVentricularReference.CreatePlan(pattern) with { RateAdjustment = new(40, null, Seed, 50) };
            var whole = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(32_000_000_000, 1000);
            var ventricular = whole.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            long expected = pattern == AvConductionPattern.RonTLongQtPvcIllustration ? 500_000_000 : 200_000_000;
            for (int i = 3; i < ventricular.Length; i += 4)
            { Check.That(ventricular[i].SimTimeNs - ventricular[i - 1].SimTimeNs == expected, "R-on-T retains its coupling against the unchanged T wave"); }
            var split = RegularPhysiologyTimeline.Start(plan);
            List<PhysiologyCycleEvent> actual = [];
            for (long end = 100_000_000; end <= 32_000_000_000; end += 100_000_000)
            { actual.AddRange(split.AdvanceBefore(end, 100)); }
            Check.That(actual.SequenceEqual(whole), "fixed premature coupling obeys exact half-open bounds");
        }
    }

    private static void FlutterCyclesRemainContinuous()
    {
        var config = PhysiologyIllustrationConfiguration.Flutter(2) with { RateAdjustment = new(240, null, Seed, 50) };
        var source = PhysiologyIllustrationSource.Create(config);
        var channel = source.CaptureState().Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(0));
        var band = channel.Generator.Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical);
        var events = RegularPhysiologyTimeline.Start(config.ResolvePlan()).AdvanceBefore(4_000_000_000, 1000)
            .Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
        var composition = EventWaveformComposition.Restore(new([band], events));
        foreach (var (first, second) in events.Zip(events.Skip(1)))
        {
            Check.That(composition.EvaluateAt(second.SimTimeNs - 20_000_000) != 0, "slower flutter has no old-period isoelectric gap");
            Check.That(composition.EvaluateAt(second.SimTimeNs) == 0, "flutter cycle ends exactly at the following event");
        }
    }
}
