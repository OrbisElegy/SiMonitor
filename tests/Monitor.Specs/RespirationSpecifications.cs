// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RespirationSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Timeline(long inspiration = 1_875_000_000) =>
        new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, inspiration);
    public static Specification[] All =>
    [
        new(nameof(DefaultRespPreservesPreviousNativeMorphology), DefaultRespPreservesPreviousNativeMorphology),
        new(nameof(RespTurningPointFollowsSharedExpiration), RespTurningPointFollowsSharedExpiration),
        new(nameof(RespDepthPolarityAndMissingEventsAreExplicit), RespDepthPolarityAndMissingEventsAreExplicit),
        new(nameof(RespCo2NativeRecoveryPreservesUnequalPhases), RespCo2NativeRecoveryPreservesUnequalPhases),
        new(nameof(RespRejectsInvalidPlansAndPreservesAtomicState), RespRejectsInvalidPlansAndPreservesAtomicState),
    ];

    private static EventWaveformComposition Compose(RegularPhysiologyPlan timeline, int amplitude)
    {
        var bands = new RespirationPlan(amplitude).CreateChannel(timeline, Resp, 0).Bands;
        return EventWaveformComposition.Restore(new(bands, [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));
    }

    private static void DefaultRespPreservesPreviousNativeMorphology()
    {
        var plan = Timeline();
        var band = new RespirationPlan(1000).CreateChannel(plan, Resp, 0).Bands.Single();
        byte[] bytes = new byte[band.TableQ32.Count * 8];
        for (int index = 0; index < band.TableQ32.Count; index++)
        { BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(index * 8), band.TableQ32[index]); }
        Check.That(Convert.ToHexString(SHA256.HashData(bytes)).Equals(
            "6155911724b73038bb5fae85d68fac990af636eb8d299495de925a8498cd9e24", StringComparison.OrdinalIgnoreCase),
            "unit table scaled to1000 retains the original desktop Resp Q32 hash exactly");
        var current = PhysiologySignalGenerator.Start(plan, "AcqResp125@1", 1, [band]);
        var legacy = PhysiologySignalGenerator.Start(plan, "AcqResp125@1", 1, [band with { PhasePoints = null }]);
        Check.That(current.GenerateBefore(10_000_000_000, 1250, 100).SequenceEqual(
            legacy.GenerateBefore(10_000_000_000, 1250, 100)), "default1:1 phase mapping preserves every native sample and timestamp");
    }

    private static void RespTurningPointFollowsSharedExpiration()
    {
        foreach (long inspiration in new[] { 1_250_000_000L, 1_875_000_000, 2_500_000_000 })
        {
            var plan = Timeline(inspiration);
            var resp = Compose(plan, 1000);
            var co2Bands = new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40).CreateChannel(plan, Co2, 0).Bands;
            var co2 = EventWaveformComposition.Restore(new(co2Bands,
                [new(inspiration, PhysiologyCycleEventKind.ExpirationStart, 0)]));
            Check.That(resp.EvaluateAt(0) == 0 && resp.EvaluateAt(inspiration) == 1000 * Q &&
                resp.EvaluateAt(plan.BreathPeriodNs) == 0 && resp.EvaluateAt(inspiration - 80_000_000) < 1000 * Q &&
                resp.EvaluateAt(inspiration + 80_000_000) < 1000 * Q,
                "Resp turn follows expiration for1:2,1:1 and2:1 inspiration/expiration");
            Check.That(co2.EvaluateAt(inspiration) == 0 && co2.EvaluateAt(inspiration + 125_000_000) == 0 &&
                co2.EvaluateAt(inspiration + 250_000_000) > 0 && co2.EvaluateAt(plan.BreathPeriodNs) == 4000 * Q &&
                co2.EvaluateAt(plan.BreathPeriodNs + 200_000_000) == 0,
                "shared expiration and next inspiration preserve CO2 phases independently of Resp peak timing");
            var restored = EventWaveformComposition.Restore(resp.CaptureState());
            Check.That(restored.EvaluateAt(inspiration) == 1000 * Q, "phase mapping and owned tables survive restore");
        }
    }

    private static void RespDepthPolarityAndMissingEventsAreExplicit()
    {
        var timeline = Timeline(1_250_000_000);
        var positive = Compose(timeline, 800);
        var negative = Compose(timeline, -800);
        var deep = Compose(timeline, 1600);
        var flat = Compose(timeline, 0);
        for (long time = 0; time <= timeline.BreathPeriodNs; time += 8_000_000)
        {
            long value = positive.EvaluateAt(time);
            Check.That(negative.EvaluateAt(time) == -value && Math.Abs(deep.EvaluateAt(time) - 2 * value) <= 1 && flat.EvaluateAt(time) == 0,
                "polarity is exact; independently rounded doubled amplitude differs by at most one Q32 unit");
        }
        var bands = new RespirationPlan(1000).CreateChannel(timeline, Resp, 0).Bands;
        var absent = EventWaveformComposition.Restore(new(bands, [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]));
        Check.That(absent.EvaluateAt(timeline.InspirationDurationNs) == 0, "no inspiration trigger invents no Resp excursion");
        Check.That(Compose(timeline, short.MinValue).EvaluateAt(timeline.InspirationDurationNs) == short.MinValue * Q &&
            Compose(timeline, short.MaxValue).EvaluateAt(timeline.InspirationDurationNs) == short.MaxValue * Q,
            "full signed relative-count bounds are computational bounds, not clinical depths");
    }

    private static PhysiologyWaveformGroup Group()
    {
        var plan = Timeline(1_250_000_000);
        return PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 40,
            [new RespirationPlan(-1000).CreateChannel(plan, Resp, 7),
             new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40).CreateChannel(plan, Co2, 0)]);
    }

    private static void RespCo2NativeRecoveryPreservesUnequalPhases()
    {
        var boundary = Group();
        Check.That(boundary.AdvanceTo(2_189_999_999, 548, 1, 100).Count == 0 &&
            boundary.AdvanceTo(2_190_000_000, 1, 1, 100).Count == 1, "shared CO2 processing delay still gates Resp publication");
        var actual = NativeRecoveryChecks.Verify(Group,
            [1_250_000_000, 1_375_000_000, 2_190_000_000, 3_750_000_000, 3_850_000_000, 3_950_000_000, 6_000_000_000],
            750, 30, 100, 20, nameof(RespCo2NativeRecoveryPreservesUnequalPhases));
        var planes = actual.Select(bytes => WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == Resp)).ToArray();
        var plan = Timeline(1_250_000_000);
        var source = PhysiologySignalGenerator.Start(plan, "AcqResp125@1", 1, new RespirationPlan(-1000).CreateChannel(plan, Resp, 7).Bands);
        Check.That(planes.All(plane => plane.SampleRateNumerator == 125 && plane.ScaleNumerator == 1 &&
            plane.ScaleDenominator == 1 && plane.OffsetNumerator == 0) && planes.SelectMany(plane => plane.Samples).SequenceEqual(
                source.GenerateBefore(4_000_000_000, 500, 100).Select(frame => frame.NormalizedValue)),
            "native125Hz relative counts remain unchanged through the actual shared wire plane");
    }

    private static void RespRejectsInvalidPlansAndPreservesAtomicState()
    {
        foreach (int amplitude in new[] { int.MinValue, -32769, 32768, int.MaxValue })
        {
            bool rejected = false;
            try { new RespirationPlan(amplitude).CreateChannel(Timeline(), Resp, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "Respiration.InvalidPlan"; }
            Check.That(rejected, "unrepresentable signed amplitudes reject before channel publication");
        }
        foreach (long inspiration in new[] { 0L, 3_750_000_000, long.MaxValue })
        {
            bool rejected = false;
            try { new RespirationPlan(1000).CreateChannel(Timeline(inspiration), Resp, 0); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid shared inspiration timing rejects before phase construction");
        }
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(6_000_000_000, 750, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late block failure preserves both channels atomically");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { group.AdvanceTo(6_000_000_000, 750, 30, 100, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && JsonSerializer.Serialize(group.CaptureState()) == before, "cancellation cannot partially advance shared Resp/CO2 state");
    }
}
