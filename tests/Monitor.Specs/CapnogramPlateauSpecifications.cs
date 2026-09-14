// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CapnogramPlateauSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Timeline => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static CapnogramPlan Plan(int? plateau = null) => new(125_000_000, 250_000_000, 200_000_000, 5, 40, plateau);
    public static Specification[] All =>
    [
        new(nameof(ExplicitPlateauPreservesEndpointsAndFall), ExplicitPlateauPreservesEndpointsAndFall),
        new(nameof(ReferencePlateauRetainsSeedAndFractionalPressure), ReferencePlateauRetainsSeedAndFractionalPressure),
        new(nameof(ExplicitPlateauSurvivesNativeRecovery), ExplicitPlateauSurvivesNativeRecovery),
        new(nameof(InvalidPlateauAndLateFailurePreserveState), InvalidPlateauAndLateFailurePreserveState),
    ];

    private static EventWaveformComposition Compose(CapnogramPlan plan) => EventWaveformComposition.Restore(new(
        plan.CreateChannel(Timeline, Co2, 0).Bands, [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]));

    private static void ExplicitPlateauPreservesEndpointsAndFall()
    {
        var original = Compose(Plan());
        foreach (int start in new[] { 500, 1500, 3250, 4000 })
        {
            var curve = Compose(Plan(start));
            Check.That(curve.EvaluateAt(125_000_000) == 0 && curve.EvaluateAt(375_000_000) == (start - 500L) * Q &&
                curve.EvaluateAt(1_875_000_000) == 3500 * Q && curve.EvaluateAt(2_075_000_000) == 0,
                "independent absolute plateau start preserves dead space, C/D endpoints and next-inspiration closure");
            long previous = curve.EvaluateAt(375_000_000);
            for (long time = 385_000_000; time < 1_875_000_000; time += 10_000_000)
            {
                long current = curve.EvaluateAt(time);
                Check.That(current >= previous && current <= 3500 * Q, "plateau remapping remains bounded and nondecreasing");
                if (start == 4000) { Check.That(current == 3500 * Q, "equal C/D pressure produces a flat plateau"); }
                previous = current;
            }
            for (long time = 1_875_000_000; time <= 2_075_000_000; time += 10_000_000)
            { Check.That(curve.EvaluateAt(time) == original.EvaluateAt(time), "plateau setting leaves inspiratory fall bit-for-bit unchanged"); }
        }
    }

    private static void ReferencePlateauRetainsSeedAndFractionalPressure()
    {
        var band = Plan().CreateChannel(Timeline, Co2, 0).Bands.Single();
        byte[] bytes = new byte[band.TableQ32.Count * 8];
        for (int index = 0; index < band.TableQ32.Count; index++)
        { BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(index * 8), band.TableQ32[index] / 3500); }
        Check.That(Convert.ToHexString(SHA256.HashData(bytes)).Equals(
            "13dc38eeaf61addbe4c7ad43692b6672c2c03cb79eb8cb312bdb24e37d084c54", StringComparison.OrdinalIgnoreCase),
            "omitting explicit plateau retains the original imported normalized seed exactly");
        var channel = Plan(1234).CreateChannel(Timeline, Co2, 0);
        Check.That(Compose(Plan(1234)).EvaluateAt(375_000_000) == 734 * Q && channel.Plane.OffsetNumerator == 5 &&
            channel.Plane.ScaleDenominator == 100, "12.34mmHg plateau includes the5mmHg wire baseline only once");
        var raised = Compose(Plan(1734) with { BaselineMmHg = 10, EndExpiratoryMmHg = 45 });
        var lower = Compose(Plan(1234));
        for (long time = 0; time < 2_100_000_000; time += 8_000_000)
        { Check.That(raised.EvaluateAt(time) == lower.EvaluateAt(time), "equal translation of baseline and both endpoints preserves raw morphology"); }
        Check.That(Compose(Plan(500) with { EndExpiratoryMmHg = 5 }).EvaluateAt(1_000_000_000) == 0,
            "equal baseline and endpoints produce no exhaled increment without division by zero");
    }

    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 40,
        [new RespirationPlan(1000).CreateChannel(Timeline, Resp, 0), Plan(1234).CreateChannel(Timeline, Co2, 0)]);

    private static void ExplicitPlateauSurvivesNativeRecovery()
    {
        var expected = Group().AdvanceTo(6_000_000_000, 750, 30, 100);
        var group = Group();
        List<byte[]> actual = [];
        for (int step = 1; step <= 30; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 25, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(expected.Count == 20 && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)) && actual.Count == expected.Count,
            "fractional plateau and active inspiratory fall survive split/restore in shared native blocks");
        short[] samples = actual.Select(bytes => WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == Co2))
            .SelectMany(plane => plane.Samples).ToArray();
        Check.That(samples.Length == 400 && samples[225] == 734 && samples[375] == 3500 && samples[395] == 0,
            "native100Hz samples preserve explicit plateau resolution, end-expiratory target and source timing");
    }

    private static void InvalidPlateauAndLateFailurePreserveState()
    {
        foreach (int invalid in new[] { int.MinValue, -1, 499, 4001, int.MaxValue })
        {
            bool rejected = false;
            try { Plan(invalid).CreateChannel(Timeline, Co2, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "Capnogram.InvalidPlan"; }
            Check.That(rejected, "plateau outside baseline/end pressure rejects before source publication");
        }
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(6_000_000_000, 750, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late block limit cannot advance either respiratory source");
    }
}
