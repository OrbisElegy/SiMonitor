// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CardiacActivitySpecifications
{
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Cvp = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static RegularPhysiologyPlan Plan(CardiacActivity activity) => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, CardiacActivity: activity);
    public static Specification[] All =>
    [
        new(nameof(VentricularOnlyRemovesPAndAWithoutChangingOtherSources), VentricularOnlyRemovesPAndAWithoutChangingOtherSources),
        new(nameof(VentricularOnlySchedulesKeepIndicesAndAtomicFailure), VentricularOnlySchedulesKeepIndicesAndAtomicFailure),
        new(nameof(CardiacActivityFiltersOnlySelectedEvents), CardiacActivityFiltersOnlySelectedEvents),
        new(nameof(AtrialOnlyRetainsPAndCvpAWithoutVentricularPulse), AtrialOnlyRetainsPAndCvpAWithoutVentricularPulse),
        new(nameof(CardiacActivityNativeSamplesRecoverIdentically), CardiacActivityNativeSamplesRecoverIdentically),
        new(nameof(CardiacActivityDefaultsValidationAndAtomicFailure), CardiacActivityDefaultsValidationAndAtomicFailure),
    ];

    private static void VentricularOnlyRemovesPAndAWithoutChangingOtherSources()
    {
        var normal = Group(CardiacActivity.AtrialAndVentricular, 160).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var ventricular = Group(CardiacActivity.VentricularOnly, 160).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var atrial = Group(CardiacActivity.AtrialOnly).AdvanceTo(8_000_000_000, 2000, 40, 100);
        foreach (var id in new[] { Ecg, Cvp })
        {
            short[] all = Samples(normal, id);
            short[] a = Samples(atrial, id);
            short[] v = Samples(ventricular, id);
            Check.That(a.Any(value => value != 0) && v.Any(value => value != 0) &&
                all.Select((value, index) => value - a[index]).SequenceEqual(v.Select(value => (int)value)),
                "only P or CVP a is removed; ventricular components retain original amplitude and timing");
        }
        foreach (var id in new[] { Pleth, Resp })
        {
            Check.That(Samples(normal, id).SequenceEqual(Samples(ventricular, id)),
                "mechanical pulse, breathing and cardiac artifact retain exact native samples");
        }
        var electrodes = TextbookElectrodeReference.CreateElectrodes(new(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]));
        var reference = ElectrodeSignalGenerator.Start(Plan(CardiacActivity.AtrialAndVentricular), "AcqECGMonitor250@1", 1, electrodes)
            .GenerateBefore(1_600_000_000, 400, 100);
        var projected = ElectrodeSignalGenerator.Start(Plan(CardiacActivity.VentricularOnly), "AcqECGMonitor250@1", 1, electrodes)
            .GenerateBefore(1_600_000_000, 400, 100);
        for (int index = 0; index < reference.Count; index++)
        {
            Check.That(projected[index].Tick == reference[index].Tick && (index % 200 < 25
                ? projected[index].MicrovoltValues.All(value => value == 0)
                : projected[index].MicrovoltValues.SequenceEqual(reference[index].MicrovoltValues)),
                "all twelve leads lose only P while QRS/T/U and sample clocks remain exact");
        }
    }

    private static void VentricularOnlySchedulesKeepIndicesAndAtomicFailure()
    {
        var plan = Plan(CardiacActivity.AtrialAndVentricular) with
        {
            VentricularConductionRatio = 3,
            VentricularMechanicalEnabled = false,
            MechanicalAfterCycles = 2,
            MechanicalDurationCycles = 3,
            MechanicalEveryCycles = 2,
        };
        var expected = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(20_000_000_000, 200)
            .Where(e => e.Kind is not (PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical)).ToArray();
        var timeline = RegularPhysiologyTimeline.Start(plan with { CardiacActivity = CardiacActivity.VentricularOnly });
        string before = JsonSerializer.Serialize(timeline.CaptureState());
        bool rejected = false;
        try { timeline.AdvanceBefore(20_000_000_000, 1); }
        catch (PhysiologyTimelineException) { rejected = true; }
        Check.That(rejected && JsonSerializer.Serialize(timeline.CaptureState()) == before, "event budget failure does not advance ventricular-only source");
        List<PhysiologyCycleEvent> actual = [];
        for (int step = 1; step <= 100; step++)
        {
            actual.AddRange(timeline.AdvanceBefore(step * 200_000_000L, 20));
            timeline = RegularPhysiologyTimeline.Restore(timeline.CaptureState());
        }
        Check.That(actual.SequenceEqual(expected) && actual.Any(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical),
            "suppression, recovery and stride retain original ventricular indices through restore");
        Check.That((int)CardiacActivity.Absent == 2 && (int)CardiacActivity.VentricularOnly == 3,
            "appended activity preserves existing serialized enum values");
    }

    private static void CardiacActivityFiltersOnlySelectedEvents()
    {
        foreach (int ratio in new[] { 1, 3 })
        {
            var plan = Plan(CardiacActivity.AtrialAndVentricular) with { VentricularConductionRatio = ratio };
            var normal = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100);
            foreach (CardiacActivity activity in Enum.GetValues<CardiacActivity>())
            {
                var events = RegularPhysiologyTimeline.Start(plan with { CardiacActivity = activity }).AdvanceBefore(8_000_000_000, 100);
                var expected = normal.Where(e => e.Kind is PhysiologyCycleEventKind.InspirationStart or PhysiologyCycleEventKind.ExpirationStart ||
                    activity == CardiacActivity.AtrialAndVentricular || activity == CardiacActivity.AtrialOnly &&
                    e.Kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical ||
                    activity == CardiacActivity.VentricularOnly && e.Kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical);
                Check.That(events.SequenceEqual(expected), "source activity filters events without shifting remaining timestamps or cycle indices");
            }
        }
        var empty = RegularPhysiologyTimeline.Start(Plan(CardiacActivity.Absent) with { RespiratoryActivity = RespiratoryActivity.Absent });
        Check.That(empty.AdvanceBefore(8_000_000_000, 1).Count == 0 && empty.CaptureState().CursorSimTimeNs == 8_000_000_000,
            "a source without events still advances its exact cursor");
    }

    private static PhysiologyWaveformGroup Group(CardiacActivity activity, int artifact = 0)
    {
        var plan = Plan(activity);
        return PhysiologyWaveformGroup.Start(Ecg, Pleth, 1, 1, 1, 0, 50,
            [new(plan, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
             new(plan, new(Pleth, "AcqPleth125@1", 1, 1, 0, 1), new PlethPulsePlan(80_000_000, 512_000_000, 1000).CreateBands(), 250, 0),
             new RespirationPlan(800, artifact).CreateChannel(plan, Resp, 0),
             new CentralVenousPressurePlan(600, new(0, 120_000_000, 200), new(0, 120_000_000, 80),
                 new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250),
                 new(400_000_000, 160_000_000, 120), 0).CreateChannel(plan, Cvp, 0)]);
    }
    private static short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.SelectMany(bytes =>
        WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == id).Samples).ToArray();

    private static void AtrialOnlyRetainsPAndCvpAWithoutVentricularPulse()
    {
        var normal = Group(CardiacActivity.AtrialAndVentricular).AdvanceTo(8_000_000_000, 2000, 40, 100);
        foreach (CardiacActivity activity in new[] { CardiacActivity.AtrialOnly, CardiacActivity.Absent })
        {
            var blocks = Group(activity, 160).AdvanceTo(8_000_000_000, 2000, 40, 100);
            short[] ecg = Samples(blocks, Ecg);
            Check.That(ecg.Where((_, index) => index % 200 >= 25).All(value => value == 0) &&
                (activity == CardiacActivity.AtrialOnly ? ecg.Any(value => value > 0) : ecg.All(value => value == 0)),
                "atrial-only retains P while absent removes it; neither invents ventricular waves");
            Check.That(Samples(blocks, Pleth).All(value => value == 0), "no ventricular mechanical event produces no Pleth excursion");
            Check.That(Samples(blocks, Resp).SequenceEqual(Samples(normal, Resp)), "breathing remains and ventricular cardiac artifact stops");
            short[] cvp = Samples(blocks, Cvp);
            Check.That(cvp.Where((_, index) => index % 100 < 10 || index % 100 >= 25).All(value => value == 0) &&
                (activity == CardiacActivity.AtrialOnly ? cvp.Any(value => value > 0) : cvp.All(value => value == 0)),
                "CVP retains only the atrial a component or no cardiac component, with baseline held separately");
        }
    }

    private static void CardiacActivityNativeSamplesRecoverIdentically()
    {
        foreach (CardiacActivity activity in new[] { CardiacActivity.AtrialOnly, CardiacActivity.Absent, CardiacActivity.VentricularOnly })
        {
            var expected = Group(activity).AdvanceTo(8_000_000_000, 2000, 40, 100);
            var group = Group(activity);
            List<byte[]> actual = [];
            for (int step = 1; step <= 40; step++)
            {
                actual.AddRange(group.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                group = PhysiologyWaveformGroup.Restore(group.CaptureState());
            }
            Check.That(actual.Count == 30 && actual.Count == expected.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
                "native planes, empty cardiac bands and original acquisition clocks survive split recovery");
        }
    }

    private static void CardiacActivityDefaultsValidationAndAtomicFailure()
    {
        var plan = Plan(CardiacActivity.AtrialAndVentricular);
        var json = JsonSerializer.SerializeToNode(plan)!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.CardiacActivity));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == plan, "old plans default to both cardiac event sources");
        bool rejected = false;
        try { RegularPhysiologyTimeline.Restore(new(plan with { CardiacActivity = (CardiacActivity)99 }, 0)); }
        catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
        Check.That(rejected, "undefined cardiac activity rejects on restore");
        var group = Group(CardiacActivity.AtrialOnly);
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(8_000_000_000, 2000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "failed publication preserves all source channels");
    }
}
