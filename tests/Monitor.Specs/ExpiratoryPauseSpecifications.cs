// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ExpiratoryPauseSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Cvp = Guid.Parse("77777777-7777-4777-8777-777777777777");
    internal static RegularPhysiologyPlan Plan(long pause = 800_000_000) =>
        new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, 800_000_000, pause);
    private static CentralVenousPressurePlan CvpPlan => new(600, new(0, 120_000_000, 200), new(0, 120_000_000, 80),
        new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250), new(400_000_000, 160_000_000, 120), -100);
    public static Specification[] All =>
    [
        new(nameof(ExpirationReturnsToBaselineBeforeNextBreath), ExpirationReturnsToBaselineBeforeNextBreath),
        new(nameof(OldPlansRetainNoExpiratoryPause), OldPlansRetainNoExpiratoryPause),
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

    internal static PhysiologyWaveformGroup Group(long pause = 800_000_000) => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 40,
        [new RespirationPlan(800).CreateChannel(Plan(pause), Resp, 0), CvpPlan.CreateChannel(Plan(pause), Cvp, 0),
         new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40, null, 600_000_000, 100_000_000).CreateChannel(Plan(pause), Co2, 0)]);
}
