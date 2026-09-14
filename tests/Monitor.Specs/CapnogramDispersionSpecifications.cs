// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CapnogramDispersionSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Timeline => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static CapnogramPlan Plan(long step = 0, int plateau = 1234) => new(125_000_000, 250_000_000, 200_000_000, 5, 40, plateau, 0, step);
    public static Specification[] All =>
    [
        new(nameof(DispersionKernelPreservesConstantGain), DispersionKernelPreservesConstantGain),
        new(nameof(DispersionBroadensEdgesWithoutChangingPressureBaseline), DispersionBroadensEdgesWithoutChangingPressureBaseline),
        new(nameof(DispersedNativeSamplesSurviveRecovery), DispersedNativeSamplesSurviveRecovery),
        new(nameof(InvalidDispersionAndLateFailureAreAtomic), InvalidDispersionAndLateFailureAreAtomic),
    ];

    private static EventWaveformComposition Compose(long step, int plateau = 1234) => EventWaveformComposition.Restore(new(
        Plan(step, plateau).CreateChannel(Timeline, Co2, 0).Bands, [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]));

    private static void DispersionKernelPreservesConstantGain()
    {
        var original = Plan().CreateChannel(Timeline, Co2, 0).Bands.Single();
        var channel = (Plan(123_456_789) with { TransportDelayNs = 500_000_000 }).CreateChannel(Timeline, Co2, 0);
        var bands = channel.Bands;
        Check.That(bands.Count == 3 && bands[0].DelayNs == 500_000_000 && bands[1].DelayNs == 623_456_789 &&
            bands[2].DelayNs == 746_913_578 && bands.All(band => band.DurationNs == original.DurationNs),
            "three paths retain independent pure delay, spacing and source phase duration");
        for (int index = 0; index < original.TableQ32.Count; index++)
        {
            Check.That(bands.All(band => band.TableQ32[index] >= 0) && bands[0].TableQ32[index] == bands[2].TableQ32[index] &&
                bands.Sum(band => band.TableQ32[index]) == original.TableQ32[index],
                "nonnegative symmetric paths preserve the source table sum exactly, including fractional plateau residue");
        }
        Check.That(channel.Physiology == Timeline && channel.Plane.OffsetNumerator == 5 && channel.Plane.ScaleDenominator == 100,
            "dispersion does not alter patient breath events or affine pressure baseline");
    }

    private static void DispersionBroadensEdgesWithoutChangingPressureBaseline()
    {
        var original = Compose(0);
        var filtered = Compose(100_000_000);
        long At(long time) => time < 0 ? 0 : original.EvaluateAt(time);
        for (long time = 0; time <= 2_400_000_000; time += 7_000_000)
        {
            long expected = (long)FixedPointMath.RoundDivideTiesToEven((Int128)At(time) + 2 * At(time - 100_000_000) + At(time - 200_000_000), 4);
            Check.That(Math.Abs(filtered.EvaluateAt(time) - expected) <= 3,
                "three-path convolution matches1:2:1 weights within three Q32 units of independent table/interpolation rounding");
        }
        Check.That(filtered.EvaluateAt(250_000_000) < original.EvaluateAt(250_000_000) &&
            original.EvaluateAt(2_150_000_000) == 0 && filtered.EvaluateAt(2_150_000_000) > 0 &&
            filtered.EvaluateAt(2_275_000_000) == 0, "dispersion broadens the rise and extends the observed fall without infinite history");
        var flat = Compose(100_000_000, 4000);
        Check.That(flat.EvaluateAt(575_000_000) == 3500 * Q && flat.EvaluateAt(1_500_000_000) == 3500 * Q,
            "a settled constant plateau retains its exact pressure level");
        Check.That(EventWaveformComposition.Restore(filtered.CaptureState()).EvaluateAt(2_150_000_000) == filtered.EvaluateAt(2_150_000_000),
            "the delayed response tail is reproducible from owned state");
    }

    private static PhysiologyWaveformGroup Group(long step) => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 40,
        [new RespirationPlan(1000).CreateChannel(Timeline, Resp, 0),
         (Plan(step) with { TransportDelayNs = 600_000_000 }).CreateChannel(Timeline, Co2, 0)]);

    private static void DispersedNativeSamplesSurviveRecovery()
    {
        var expected = Group(100_000_000).AdvanceTo(8_000_000_000, 1000, 40, 100);
        var group = Group(100_000_000);
        List<byte[]> actual = [];
        for (int step = 1; step <= 40; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 25, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(actual.Count == 30 && expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "dispersed CO2 paths and active tails survive native split/restore with original sample clocks");
        var undispersed = Group(0).AdvanceTo(8_000_000_000, 1000, 40, 100);
        short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
            .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();
        Check.That(Samples(actual, Resp).SequenceEqual(Samples(undispersed, Resp)) &&
            !Samples(actual, Co2).SequenceEqual(Samples(undispersed, Co2)), "only the CO2 measurement path changes");
        var boundary = Group(100_000_000);
        Check.That(boundary.AdvanceTo(2_189_999_999, 548, 1, 100).Count == 0 && boundary.AdvanceTo(2_190_000_000, 1, 1, 100).Count == 1,
            "response spread does not silently alter acquisition processing latency");
    }

    private static void InvalidDispersionAndLateFailureAreAtomic()
    {
        foreach (var plan in new[] { Plan(-1), Plan(long.MaxValue), Plan(1) with { TransportDelayNs = long.MaxValue - 2_075_000_000 } })
        {
            bool rejected = false;
            try { plan.CreateChannel(Timeline, Co2, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "Capnogram.InvalidPlan"; }
            Check.That(rejected, "negative or overflowing distributed delay rejects before publication");
        }
        var group = Group(100_000_000);
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool rejectedLimit = false;
        try { group.AdvanceTo(8_000_000_000, 1000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { rejectedLimit = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(rejectedLimit && JsonSerializer.Serialize(group.CaptureState()) == before, "late publication failure preserves dispersed and undisrupted channels atomically");
    }
}
