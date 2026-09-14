// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ExpiratoryPauseSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Cvp = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static RegularPhysiologyPlan Plan(long pause = 800_000_000) =>
        new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, 800_000_000, pause);
    private static CentralVenousPressurePlan CvpPlan => new(600, new(0, 120_000_000, 200), new(0, 120_000_000, 80),
        new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250), new(400_000_000, 160_000_000, 120), -100);
    public static Specification[] All =>
    [
        new(nameof(ExpirationReturnsToBaselineBeforeNextBreath), ExpirationReturnsToBaselineBeforeNextBreath),
        new(nameof(OldPlansRetainNoExpiratoryPause), OldPlansRetainNoExpiratoryPause),
        new(nameof(BothPausesSurviveNativeRecovery), BothPausesSurviveNativeRecovery),
        new(nameof(InvalidExpiratoryPauseAndLateFailureReject), InvalidExpiratoryPauseAndLateFailureReject),
    ];

    private static void ExpirationReturnsToBaselineBeforeNextBreath()
    {
        foreach (long pause in new[] { 1L, 800_000_000, 1_999_999_999 })
        {
            var plan = Plan(pause);
            long end = plan.BreathPeriodNs - pause;
            foreach (int amplitude in new[] { -800, 0, 800 })
            {
                var band = new RespirationPlan(amplitude).CreateChannel(plan, Resp, 0).Bands.Single();
                var source = EventWaveformComposition.Restore(new([band], [new(0, PhysiologyCycleEventKind.InspirationStart, 0),
                    new(plan.BreathPeriodNs, PhysiologyCycleEventKind.InspirationStart, 1)]));
                Check.That(band.DurationNs == end && source.EvaluateAt(1_600_000_000) == amplitude * Q,
                    "active support ends early while the independent inspiratory hold retains its signed depth");
                foreach (long time in new[] { end, end + pause / 2, plan.BreathPeriodNs - 1, plan.BreathPeriodNs })
                { Check.That(source.EvaluateAt(time) == 0, "expiration closes at zero and holds baseline without a wrapped table or a fabricated breath"); }
                Check.That(amplitude == 0 || Math.Sign(source.EvaluateAt(4_600_000_000)) == Math.Sign(amplitude),
                    "the next inspiration resumes at the original cycle clock");
            }
            var cvpBands = CvpPlan.CreateChannel(plan, Cvp, 0).Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.InspirationStart).ToArray();
            var cvp = EventWaveformComposition.Restore(new(cvpBands, [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));
            Check.That(cvp.EvaluateAt(1_600_000_000) == -100 * Q && cvp.EvaluateAt(end) == 0 && cvp.EvaluateAt(plan.BreathPeriodNs - 1) == 0,
                "the CVP respiratory component shares both holds, independently of cardiac components");
        }
    }

    private static void OldPlansRetainNoExpiratoryPause()
    {
        var json = JsonSerializer.SerializeToNode(Plan(0))!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.ExpiratoryPauseNs));
        var plan = json.Deserialize<RegularPhysiologyPlan>()!;
        Check.That(plan == Plan(0), "previous plans restore the new optional pause as zero");
        var band = new RespirationPlan(800).CreateChannel(plan, Resp, 0).Bands.Single();
        Check.That(band.DurationNs == plan.BreathPeriodNs && band.PhasePoints![^1].OffsetNs == plan.BreathPeriodNs,
            "disabled expiratory pause preserves previous support and phase endpoints");
        Check.That(RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100).SequenceEqual(
            RegularPhysiologyTimeline.Start(Plan()).AdvanceBefore(8_000_000_000, 100)),
            "neither respiratory nor cardiac event clocks move when the excursion is shortened");
    }

    private static PhysiologyWaveformGroup Group(long pause = 800_000_000) => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 40,
        [new RespirationPlan(800).CreateChannel(Plan(pause), Resp, 0), CvpPlan.CreateChannel(Plan(pause), Cvp, 0),
         new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40, null, 600_000_000, 100_000_000).CreateChannel(Plan(pause), Co2, 0)]);

    private static void BothPausesSurviveNativeRecovery()
    {
        var expected = Group().AdvanceTo(8_000_000_000, 1000, 40, 100);
        var group = Group();
        List<byte[]> actual = [];
        for (int step = 1; step <= 40; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 25, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(actual.Count == 30 && actual.Count == expected.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "both respiratory holds survive split native generation and pending-state recovery byte-for-byte");
        short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
            .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();
        var old = Group(0).AdvanceTo(8_000_000_000, 1000, 40, 100);
        Check.That(Samples(actual, Co2).SequenceEqual(Samples(old, Co2)) &&
            !Samples(actual, Resp).SequenceEqual(Samples(old, Resp)), "CO2 retains independent phases and measurement response");
        Check.That(Samples(actual, Resp).Skip(400).Take(100).All(value => value == 0),
            "native125Hz Resp is exactly baseline throughout3.2..4s");
        Check.That(Samples(actual, Cvp).Skip(400).Take(100).Distinct().Count() > 1,
            "cardiac CVP components continue during the respiratory baseline hold");
    }

    private static void InvalidExpiratoryPauseAndLateFailureReject()
    {
        foreach (long pause in new[] { -1L, 2_000_000_000, long.MaxValue })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Restore(new(Plan(pause), 0)); }
            catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "negative pause or absence of active expiration rejects before phase construction");
        }
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(8_000_000_000, 1000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before,
            "late publication failure preserves all paused-breath channel states atomically");
    }
}
