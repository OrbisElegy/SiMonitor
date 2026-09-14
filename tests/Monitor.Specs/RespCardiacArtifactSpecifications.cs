// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RespCardiacArtifactSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000,
        4_000_000_000, 2_000_000_000, 800_000_000, 800_000_000);
    public static Specification[] All =>
    [
        new(nameof(ArtifactFollowsMechanicalEventsWithoutBreaths), ArtifactFollowsMechanicalEventsWithoutBreaths),
        new(nameof(ArtifactAddsDuringBothRespiratoryHolds), ArtifactAddsDuringBothRespiratoryHolds),
        new(nameof(ArtifactBoundsDefaultsAndOwnedState), ArtifactBoundsDefaultsAndOwnedState),
        new(nameof(ArtifactNativeRecoveryAndAtomicFailure), ArtifactNativeRecoveryAndAtomicFailure),
    ];

    private static EventWaveformComposition Source(int amplitude, int artifact) => EventWaveformComposition.Restore(new(
        new RespirationPlan(amplitude, artifact).CreateChannel(Plan, Resp, 0).Bands,
        RegularPhysiologyTimeline.Start(Plan).AdvanceBefore(8_000_000_000, 100)));

    private static void ArtifactFollowsMechanicalEventsWithoutBreaths()
    {
        var bands = new RespirationPlan(0, 160).CreateChannel(Plan, Resp, 0).Bands;
        var artifact = bands.Single(b => b.Trigger == PhysiologyCycleEventKind.VentricularMechanical);
        Check.That(artifact.DurationNs == Plan.HeartPeriodNs && artifact.DelayNs == 0 && artifact.PhasePoints is null,
            "cardiac artifact has its own beat support, independent of respiratory phase maps");
        var events = RegularPhysiologyTimeline.Start(Plan).AdvanceBefore(8_000_000_000, 100)
            .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).ToArray();
        var source = EventWaveformComposition.Restore(new(bands, events));
        Check.That(source.EvaluateAt(200_000_000) == 0, "no artifact precedes its mechanical trigger");
        foreach (var beat in events.Take(5))
        {
            Check.That(source.EvaluateAt(beat.SimTimeNs) == 0 && source.EvaluateAt(beat.SimTimeNs + 200_000_000) == 160 * Q &&
                source.EvaluateAt(beat.SimTimeNs + 400_000_000) == 0 && source.EvaluateAt(beat.SimTimeNs + 600_000_000) == -160 * Q,
                "bipolar cardiac extrema exist without any inspiration or expiration events");
        }
        var absent = EventWaveformComposition.Restore(new(bands, [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));
        Check.That(absent.EvaluateAt(440_000_000) == 0, "a breath alone cannot trigger the cardiac artifact");
        Check.That(artifact.TableQ32.Sum() == 0 && artifact.TableQ32.Max() == 160 * Q && artifact.TableQ32.Min() == -160 * Q,
            "the symmetric illustrative artifact introduces no table DC offset");
    }

    private static void ArtifactAddsDuringBothRespiratoryHolds()
    {
        var breathing = Source(-800, 0);
        var artifact = Source(0, 160);
        var combined = Source(-800, 160);
        var inverted = Source(0, -160);
        for (long time = 0; time < 8_000_000_000; time += 7_000_000)
        {
            Check.That(combined.EvaluateAt(time) == breathing.EvaluateAt(time) + artifact.EvaluateAt(time) &&
                inverted.EvaluateAt(time) == -artifact.EvaluateAt(time), "separate Q32 bands add exactly and invert independently");
        }
        Check.That(breathing.EvaluateAt(1_240_000_000) == -800 * Q && combined.EvaluateAt(1_240_000_000) == -640 * Q &&
            breathing.EvaluateAt(3_640_000_000) == 0 && combined.EvaluateAt(3_640_000_000) == 160 * Q,
            "cardiac perturbations continue during inspiratory and expiratory holds");
    }

    private static void ArtifactBoundsDefaultsAndOwnedState()
    {
        var json = JsonSerializer.SerializeToNode(new RespirationPlan(1000))!.AsObject();
        json.Remove(nameof(RespirationPlan.CardiacArtifactCounts));
        Check.That(json.Deserialize<RespirationPlan>() == new RespirationPlan(1000, 0), "old source plans default to no artifact");
        Check.That(new RespirationPlan(short.MinValue).CreateChannel(Plan, Resp, 0).Bands.Count == 1 &&
            new RespirationPlan(short.MaxValue).CreateChannel(Plan, Resp, 0).Bands.Count == 1,
            "disabled artifact preserves the previous full signed amplitude range and one-band output");
        foreach (var plan in new[] { new RespirationPlan(32767, 1), new(-32768, 1), new(0, -32768), new(0, int.MinValue), new(int.MaxValue, 0) })
        {
            bool rejected = false;
            try { plan.CreateChannel(Plan, Resp, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "Respiration.InvalidPlan"; }
            Check.That(rejected, "conservative combined signed budget rejects before generating overflowing bands");
        }
        _ = new RespirationPlan(32700, 67).CreateChannel(Plan, Resp, 0);
        _ = new RespirationPlan(-32700, -68).CreateChannel(Plan, Resp, 0);
        var source = Source(800, 160);
        var restored = EventWaveformComposition.Restore(source.CaptureState());
        Check.That(restored.EvaluateAt(3_640_000_000) == source.EvaluateAt(3_640_000_000), "owned artifact tables survive restore");
    }

    private static PhysiologyWaveformGroup Group(int artifact) => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 40,
        [new RespirationPlan(0, artifact).CreateChannel(Plan, Resp, 0),
         new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40).CreateChannel(Plan, Co2, 0)]);

    private static void ArtifactNativeRecoveryAndAtomicFailure()
    {
        var expected = Group(160).AdvanceTo(8_000_000_000, 1000, 40, 100);
        var group = Group(160);
        List<byte[]> actual = [];
        for (int step = 1; step <= 40; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 25, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(actual.Count == 30 && actual.Count == expected.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "artifact and breathing samples retain native clocks and byte-for-byte split recovery");
        short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
            .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();
        var without = Group(0).AdvanceTo(8_000_000_000, 1000, 40, 100);
        Check.That(Samples(actual, Co2).SequenceEqual(Samples(without, Co2)) && Samples(actual, Resp)[55] == 160 &&
            Samples(actual, Resp)[105] == -160 && Samples(without, Resp).All(value => value == 0),
            "only Resp changes; bipolar native extrema remain visible with zero breathing depth");
        var fresh = Group(160);
        string before = JsonSerializer.Serialize(fresh.CaptureState());
        bool limited = false;
        try { fresh.AdvanceTo(8_000_000_000, 1000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(fresh.CaptureState()) == before, "late publication failure preserves all channel states");
    }
}
