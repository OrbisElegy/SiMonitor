// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class MechanicalUncouplingSpecifications
{
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static RegularPhysiologyPlan Plan(bool enabled) => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, VentricularMechanicalEnabled: enabled);
    public static Specification[] All =>
    [
        new(nameof(UncouplingPreservesElectricalAtrialAndRespiratoryEvents), UncouplingPreservesElectricalAtrialAndRespiratoryEvents),
        new(nameof(UncoupledSourceKeepsEcgWithoutPulseOrCardiacArtifact), UncoupledSourceKeepsEcgWithoutPulseOrCardiacArtifact),
        new(nameof(UncoupledNativeStreamsRecoverIdentically), UncoupledNativeStreamsRecoverIdentically),
        new(nameof(UncouplingDefaultsAndFailurePreserveState), UncouplingDefaultsAndFailurePreserveState),
    ];

    private static void UncouplingPreservesElectricalAtrialAndRespiratoryEvents()
    {
        foreach (CardiacActivity activity in Enum.GetValues<CardiacActivity>())
        {
            foreach (int ratio in new[] { 1, 3 })
            {
                var plan = Plan(true) with { CardiacActivity = activity, VentricularConductionRatio = ratio };
                var expected = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(8_000_000_000, 100)
                    .Where(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical);
                var actual = RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = false }).AdvanceBefore(8_000_000_000, 100);
                Check.That(actual.SequenceEqual(expected), "only ventricular mechanics disappear; electrical, atrial and breathing indices remain exact");
            }
        }
        var electrodes = TextbookElectrodeReference.CreateElectrodes(new(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]));
        var on = ElectrodeSignalGenerator.Start(Plan(true), "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(1_600_000_000, 400, 100);
        var off = ElectrodeSignalGenerator.Start(Plan(false), "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(1_600_000_000, 400, 100);
        Check.That(on.Count == off.Count && on.Zip(off).All(pair => pair.First.Tick == pair.Second.Tick &&
            pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)), "all12 ECG leads including U survive loss of mechanical events unchanged");
    }

    private static PhysiologyWaveformGroup Group(bool enabled, int artifact = 0)
    {
        var plan = Plan(enabled);
        return PhysiologyWaveformGroup.Start(Ecg, Pleth, 1, 1, 1, 0, 50,
            [new(plan, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
             new(plan, new(Pleth, "AcqPleth125@1", 1, 1, 0, 1), new PlethPulsePlan(80_000_000, 512_000_000, 1000).CreateBands(), 250, 0),
             new RespirationPlan(800, artifact).CreateChannel(plan, Resp, 0)]);
    }
    private static short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.SelectMany(bytes =>
        WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == id).Samples).ToArray();

    private static void UncoupledSourceKeepsEcgWithoutPulseOrCardiacArtifact()
    {
        var normal = Group(true).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var off = Group(false, 160).AdvanceTo(8_000_000_000, 2000, 40, 100);
        Check.That(Samples(off, Ecg).SequenceEqual(Samples(normal, Ecg)) && Samples(off, Ecg).Any(value => value > 500),
            "QRS and repolarization continue with unchanged native ECG");
        Check.That(Samples(off, Pleth).All(value => value == 0) && Samples(normal, Pleth).Any(value => value > 0),
            "continued ECG never synthesizes missing mechanical pulses");
        Check.That(Samples(off, Resp).SequenceEqual(Samples(normal, Resp)), "breathing persists without ventricular cardiac artifact");
    }

    private static void UncoupledNativeStreamsRecoverIdentically()
    {
        var expected = Group(false, 160).AdvanceTo(8_000_000_000, 2000, 40, 100);
        var group = Group(false, 160);
        List<byte[]> actual = [];
        for (int step = 1; step <= 40; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(actual.Count == 30 && actual.Count == expected.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "uncoupled mixed-rate samples, block clocks and configuration survive every split restore");
    }

    private static void UncouplingDefaultsAndFailurePreserveState()
    {
        var json = JsonSerializer.SerializeToNode(Plan(true))!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.VentricularMechanicalEnabled));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == Plan(true), "old plans keep mechanical coupling enabled");
        bool rejected = false;
        try { RegularPhysiologyTimeline.Start(Plan(false) with { VentricularMechanicalOffsetNs = 0 }); }
        catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
        Check.That(rejected, "disabled mechanics do not bypass configuration validation");
        var group = Group(false);
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(8_000_000_000, 2000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late failed publication cannot advance electrical channels alone");
    }
}
