// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class InspiratoryPauseSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Cvp = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static RegularPhysiologyPlan Plan(long pause = 800_000_000) =>
        new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, pause);
    private static CentralVenousPressurePlan CvpPlan => new(600, new(0, 120_000_000, 200), new(0, 120_000_000, 80),
        new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250), new(400_000_000, 160_000_000, 120), -100);
    public static Specification[] All =>
    [
        new(nameof(HeldPhaseIsExactOwnedAndBounded), HeldPhaseIsExactOwnedAndBounded),
        new(nameof(InspiratoryPauseCoordinatesRespAndCvp), InspiratoryPauseCoordinatesRespAndCvp),
        new(nameof(OldPlansRetainDefaultTiming), OldPlansRetainDefaultTiming),
        new(nameof(PausedBreathNativeRecoveryIsDeterministic), PausedBreathNativeRecoveryIsDeterministic),
        new(nameof(InvalidPauseAndLateFailureAreAtomic), InvalidPauseAndLateFailureAreAtomic),
    ];

    private static void HeldPhaseIsExactOwnedAndBounded()
    {
        EventWaveformPhasePoint[] points = [new(0, 0), new(20, 1), new(80, 1), new(100, 4)];
        var source = EventWaveformComposition.Restore(new([
            new(PhysiologyCycleEventKind.InspirationStart, 0, 100, [0, Q, 0, -Q], points)],
            [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));
        points[2] = new(80, 2);
        Check.That(source.EvaluateAt(10) == Q / 2 && source.EvaluateAt(90) == -Q / 2 && source.EvaluateAt(100) == 0,
            "holding phase preserves approach, departure and half-open closure");
        for (long time = 20; time <= 80; time++)
        { Check.That(source.EvaluateAt(time) == Q, "the complete held interval has exact constant Q32 amplitude"); }
        Check.That(EventWaveformComposition.Restore(source.CaptureState()).EvaluateAt(50) == Q,
            "caller mutation and checkpoint recovery preserve owned hold points");
        foreach (var map in new EventWaveformPhasePoint[][] {
            [new(0, 0), new(20, 2), new(80, 1), new(100, 4)],
            [new(0, 0), new(20, 1), new(20, 1), new(100, 4)],
            [new(0, 0), new(20, 4), new(80, 4), new(100, 4)] })
        {
            bool rejected = false;
            try
            {
                EventWaveformComposition.Restore(source.CaptureState() with
                {
                    Bands = [source.CaptureState().Bands.Single() with { PhasePoints = map }]
                });
            }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EventWaveform.InvalidState"; }
            Check.That(rejected, "backwards phase, duplicate time and premature terminal phase reject");
        }
    }

    private static EventWaveformComposition Compose(IReadOnlyList<EventWaveformBand> bands) =>
        EventWaveformComposition.Restore(new(bands, [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));

    private static void InspiratoryPauseCoordinatesRespAndCvp()
    {
        foreach (long pause in new[] { 1L, 800_000_000, 1_999_999_999 })
        {
            var plan = Plan(pause);
            long start = plan.InspirationDurationNs - pause;
            var resp = Compose(new RespirationPlan(-800).CreateChannel(plan, Resp, 0).Bands);
            var cvp = Compose(CvpPlan.CreateChannel(plan, Cvp, 0).Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.InspirationStart).ToArray());
            foreach (long time in new[] { start, start + pause / 2, plan.InspirationDurationNs })
            {
                Check.That(resp.EvaluateAt(time) == -800 * Q && cvp.EvaluateAt(time) == -100 * Q,
                "signed impedance and independent respiratory pressure hold together through end inspiration");
            }
            Check.That(resp.EvaluateAt(3_000_000_000) is < 0 and > -800 * Q && resp.EvaluateAt(plan.BreathPeriodNs) == 0,
                "expiration returns continuously to baseline without extending the breath period");
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100);
            Check.That(events.SequenceEqual(RegularPhysiologyTimeline.Start(Plan(0)).AdvanceBefore(8_000_000_000, 100)),
                "pause reshapes the inspiratory excursion without moving expiration or cardiac events");
        }
    }

    private static void OldPlansRetainDefaultTiming()
    {
        var json = JsonSerializer.SerializeToNode(Plan(0))!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.InspiratoryPauseNs));
        var restored = json.Deserialize<RegularPhysiologyPlan>()!;
        Check.That(restored == Plan(0), "existing serialized plans omit pause and restore as zero");
        var band = new RespirationPlan(1000).CreateChannel(restored, Resp, 0).Bands.Single();
        var current = PhysiologySignalGenerator.Start(restored, "AcqResp125@1", 1, [band]);
        var old = PhysiologySignalGenerator.Start(restored, "AcqResp125@1", 1, [band with { PhasePoints = null }]);
        Check.That(current.GenerateBefore(8_000_000_000, 1000, 100).SequenceEqual(old.GenerateBefore(8_000_000_000, 1000, 100)),
            "disabled pause retains every default native sample and clock");
    }

    private static PhysiologyWaveformGroup Group(long pause = 800_000_000) => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 40,
        [new RespirationPlan(-800).CreateChannel(Plan(pause), Resp, 0), CvpPlan.CreateChannel(Plan(pause), Cvp, 0),
         new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40, null, 600_000_000, 100_000_000).CreateChannel(Plan(pause), Co2, 0)]);

    private static void PausedBreathNativeRecoveryIsDeterministic()
    {
        foreach (var (name, createGroup, bothPauses) in new (string, Func<long, PhysiologyWaveformGroup>, bool)[]
        {
            ("inspiratory pause", Group, false),
            ("inspiratory and expiratory pauses", ExpiratoryPauseSpecifications.Group, true)
        })
        {
            var actual = NativeRecoveryChecks.Verify(() => createGroup(800_000_000),
                [1_199_999_999, 1_200_000_000, 1_600_000_000, 2_000_000_000, 2_190_000_000,
                 3_200_000_000, 3_600_000_000, 4_000_000_000, 4_800_000_000, 8_000_000_000],
                1000, 40, 100, 30, name);
            var previous = createGroup(0).AdvanceTo(8_000_000_000, 1000, 40, 100);
            short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
                .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();
            Check.That(Samples(actual, Co2).SequenceEqual(Samples(previous, Co2)) &&
                !Samples(actual, Resp).SequenceEqual(Samples(previous, Resp)),
                $"{name}: excursion changes without changing CO2 phases or transport");
            if (bothPauses)
            {
                Check.That(Samples(actual, Resp).Skip(400).Take(100).All(value => value == 0),
                    "native125Hz Resp is exactly baseline throughout3.2..4s");
                Check.That(Samples(actual, Cvp).Skip(400).Take(100).Distinct().Count() > 1,
                    "cardiac CVP components continue during the respiratory baseline hold");
            }
            else
            {
                Check.That(!Samples(actual, Cvp).SequenceEqual(Samples(previous, Cvp)) &&
                    Samples(actual, Resp).Skip(150).Take(101).All(value => value == -800),
                    "native Resp holds throughout1.2..2s and the respiratory pressure changes with it");
            }
        }
    }

    private static void InvalidPauseAndLateFailureAreAtomic()
    {
        foreach (var (name, createPlan, createGroup) in new
            (string, Func<long, RegularPhysiologyPlan>, Func<long, PhysiologyWaveformGroup>)[]
        {
            ("inspiration", Plan, Group),
            ("expiration", ExpiratoryPauseSpecifications.Plan, ExpiratoryPauseSpecifications.Group)
        })
        {
            foreach (long pause in new[] { -1L, 2_000_000_000, long.MaxValue })
            {
                bool rejected = false;
                try { RegularPhysiologyTimeline.Restore(new(createPlan(pause), 0)); }
                catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
                Check.That(rejected, $"{name}: negative or nonpositive active phase rejects before construction");
            }
            var group = createGroup(800_000_000);
            string before = JsonSerializer.Serialize(group.CaptureState());
            bool limited = false;
            try { group.AdvanceTo(8_000_000_000, 1000, 1, 100); }
            catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
            Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before,
                $"{name}: late publication failure preserves all paused-breath channel states atomically");
        }
    }
}
