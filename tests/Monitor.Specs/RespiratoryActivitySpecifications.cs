// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RespiratoryActivitySpecifications
{
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Cvp = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static RegularPhysiologyPlan Plan(RespiratoryActivity activity) => new(0, 800_000_000, 160_000_000, 80_000_000,
        240_000_000, 4_000_000_000, 2_000_000_000, 800_000_000, 800_000_000, activity);
    public static Specification[] All =>
    [
        new(nameof(ActivitySeparatesEffortFromExpiredGas), ActivitySeparatesEffortFromExpiredGas),
        new(nameof(AbsentBreathingRetainsCardiacArtifactAndCvp), AbsentBreathingRetainsCardiacArtifactAndCvp),
        new(nameof(ActivityDefaultsAndInvalidStates), ActivityDefaultsAndInvalidStates),
        new(nameof(ActivityRecoveryAndAtomicFailure), ActivityRecoveryAndAtomicFailure),
    ];

    private static PhysiologyWaveformGroup Group(RespiratoryActivity activity, int artifact = 160)
    {
        var plan = Plan(activity);
        return PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 40,
            [new RespirationPlan(800, artifact).CreateChannel(plan, Resp, 0),
             new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 5, 40, 1234, 600_000_000, 100_000_000).CreateChannel(plan, Co2, 0),
             new CentralVenousPressurePlan(600, new(0, 120_000_000, 200), new(0, 120_000_000, 80),
                 new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250), new(400_000_000, 160_000_000, 120), -100)
                 .CreateChannel(plan, Cvp, 0)]);
    }

    private static short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
        .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();

    private static void ActivitySeparatesEffortFromExpiredGas()
    {
        var normal = Group(RespiratoryActivity.Breathing).AdvanceTo(8_000_000_000, 1000, 40, 100);
        var effort = Group(RespiratoryActivity.EffortOnly).AdvanceTo(8_000_000_000, 1000, 40, 100);
        Check.That(Samples(normal, Resp).SequenceEqual(Samples(effort, Resp)) && Samples(normal, Cvp).SequenceEqual(Samples(effort, Cvp)),
            "effort-only preserves thoracic motion, phase holds, cardiac artifact and venous pressure");
        Check.That(Samples(normal, Co2).Any(value => value > 0) && Samples(effort, Co2).All(value => value == 0),
            "effort cannot generate expired gas despite configured plateau, lag and dispersion");
        Check.That(effort.Select(bytes => WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == Co2))
            .All(plane => plane.OffsetNumerator == 5 && plane.ScaleDenominator == 100),
            "absent gas excursion retains the configured affine pressure baseline rather than claiming sensor failure");
    }

    private static void AbsentBreathingRetainsCardiacArtifactAndCvp()
    {
        var plan = Plan(RespiratoryActivity.Absent);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100);
        Check.That(events.All(e => e.Kind != PhysiologyCycleEventKind.InspirationStart && e.Kind != PhysiologyCycleEventKind.ExpirationStart) &&
            events.SequenceEqual(RegularPhysiologyTimeline.Start(Plan(RespiratoryActivity.Breathing)).AdvanceBefore(8_000_000_000, 100)
                .Where(e => e.Kind != PhysiologyCycleEventKind.InspirationStart && e.Kind != PhysiologyCycleEventKind.ExpirationStart)),
            "absent breathing removes chest cycle events while all cardiac clocks and identities continue");
        var artifact = Group(RespiratoryActivity.Absent).AdvanceTo(8_000_000_000, 1000, 40, 100);
        var quiet = Group(RespiratoryActivity.Absent, 0).AdvanceTo(8_000_000_000, 1000, 40, 100);
        Check.That(Samples(quiet, Resp).All(value => value == 0) && Samples(artifact, Resp)[55] == 160 && Samples(artifact, Resp)[105] == -160 &&
            Samples(artifact, Co2).All(value => value == 0), "only cardiac artifact remains in Resp with no respiratory or gas cycles");
        Check.That(Samples(quiet, Cvp).SequenceEqual(Samples(artifact, Cvp)) && Samples(quiet, Cvp).Distinct().Count() > 1,
            "CVP cardiac components continue independently of Resp artifact");
    }

    private static void ActivityDefaultsAndInvalidStates()
    {
        var json = JsonSerializer.SerializeToNode(Plan(RespiratoryActivity.Breathing))!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.RespiratoryActivity));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == Plan(RespiratoryActivity.Breathing), "old plans default to normal activity");
        foreach (int value in new[] { -1, 3, int.MaxValue })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Restore(new(Plan((RespiratoryActivity)value), 0)); }
            catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "unknown source activity fails closed at restore");
        }
        var capnogram = new CapnogramPlan(0, 250_000_000, 200_000_000, 5, 40);
        bool invalid = false;
        try { capnogram.CreateChannel(Plan(RespiratoryActivity.EffortOnly), Co2, 0); }
        catch (EventWaveformException exception) { invalid = exception.ReasonCode == "Capnogram.InvalidPlan"; }
        Check.That(invalid, "inactive gas does not bypass full source parameter validation");
    }

    private static void ActivityRecoveryAndAtomicFailure()
    {
        foreach (var activity in new[] { RespiratoryActivity.EffortOnly, RespiratoryActivity.Absent })
        {
            var expected = Group(activity).AdvanceTo(8_000_000_000, 1000, 40, 100);
            var group = Group(activity);
            List<byte[]> actual = [];
            for (int step = 1; step <= 40; step++)
            {
                actual.AddRange(group.AdvanceTo(step * 200_000_000L, 25, 1, 100));
                group = PhysiologyWaveformGroup.Restore(group.CaptureState());
            }
            Check.That(actual.Count == 30 && expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
                "source activity and pending mixed-channel samples restore byte-for-byte");
            var fresh = Group(activity);
            string before = JsonSerializer.Serialize(fresh.CaptureState());
            bool limited = false;
            try { fresh.AdvanceTo(8_000_000_000, 1000, 1, 100); }
            catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
            Check.That(limited && JsonSerializer.Serialize(fresh.CaptureState()) == before, "late block failure retains source activity atomically");
        }
    }
}
