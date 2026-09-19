// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PlethPulseSpecifications
{
    private static RegularPhysiologyPlan Timeline => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static PlethPulsePlan Pulse => new(80_000_000, 512_000_000, 1000);
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static Specification[] All =>
    [
        new(nameof(PlethShoulderSurvivesDurationGainAndNativeSampling), PlethShoulderSurvivesDurationGainAndNativeSampling),
        new(nameof(PlethNeedsMechanicalEventsAndExplicitTransit), PlethNeedsMechanicalEventsAndExplicitTransit),
        new(nameof(PlethHasFastRiseOptionalNotchAndIndependentAmplitude), PlethHasFastRiseOptionalNotchAndIndependentAmplitude),
        new(nameof(PlethNativeDelayAndGroupRestorePreserveSamples), PlethNativeDelayAndGroupRestorePreserveSamples),
        new(nameof(PlethRejectsInvalidPlansAndPreservesFailedGroup), PlethRejectsInvalidPlansAndPreservesFailedGroup),
    ];

    private static EventWaveformComposition Composition(PlethPulsePlan plan, bool mechanical = true) =>
        EventWaveformComposition.Restore(new(plan.CreateBands(), mechanical
            ? [new(160_000_000, PhysiologyCycleEventKind.VentricularElectrical, 0),
               new(240_000_000, PhysiologyCycleEventKind.VentricularMechanical, 0)]
            : [new(160_000_000, PhysiologyCycleEventKind.VentricularElectrical, 0)]));

    private static void PlethNeedsMechanicalEventsAndExplicitTransit()
    {
        var present = Composition(Pulse);
        var electricalOnly = Composition(Pulse, false);
        var delayed = Composition(Pulse with { TransitDelayNs = 160_000_000 });
        for (long time = 0; time <= 1_000_000_000; time += 8_000_000)
        {
            Check.That(electricalOnly.EvaluateAt(time) == 0, "electrical markers alone cannot create peripheral pulses");
            if (time <= 320_000_000 || time >= 832_000_000)
            { Check.That(present.EvaluateAt(time) == 0, "pulse support follows mechanical time plus caller transit"); }
            Check.That(present.EvaluateAt(time) == delayed.EvaluateAt(time + 80_000_000), "transit changes arrival without changing morphology");
        }
        Check.That(present.EvaluateAt(480_000_000) == 1000 * FixedPointMath.Q32One,
            "peak occurs after independent electromechanical and transit delays");
    }

    private static void PlethHasFastRiseOptionalNotchAndIndependentAmplitude()
    {
        var plain = Composition(Pulse);
        var notch = Composition(Pulse with { IncludeNotch = true });
        var half = Composition(Pulse with { AmplitudeCounts = 500 });
        var absent = Composition(Pulse with { AmplitudeCounts = 0 });
        long previous = 0;
        for (long time = 320_000_000; time <= 832_000_000; time += 8_000_000)
        {
            long value = plain.EvaluateAt(time);
            Check.That(value >= 0 && (time <= 480_000_000 ? value >= previous : value <= previous),
                "plain pulse has a rounded160ms rise and352ms descending shoulder/tail");
            Check.That(Math.Abs(value - 2 * half.EvaluateAt(time)) <= 1 && absent.EvaluateAt(time) == 0,
                "explicit relative amplitude scales raw pulse without an SpO2 dependency");
            previous = value;
        }
        Check.That(notch.EvaluateAt(544_000_000) < notch.EvaluateAt(608_000_000) &&
            notch.EvaluateAt(608_000_000) < notch.EvaluateAt(416_000_000), "optional notch has a smaller secondary peak");
    }

    private static void PlethShoulderSurvivesDurationGainAndNativeSampling()
    {
        foreach (long duration in new[] { 320_000_000L, 384_000_000L, 512_000_000L, 640_000_000L })
        {
            var wave = Composition(Pulse with { DurationNs = duration });
            long At(int phase) => wave.EvaluateAt(320_000_000 + duration * phase / 128);
            long earlySlope = (At(48) - At(64)) / 16;
            long shoulderSlope = (At(72) - At(88)) / 16;
            long lateSlope = (At(96) - At(112)) / 16;
            Check.That(shoulderSlope > 0 && earlySlope > 5 * shoulderSlope && lateSlope > 5 * shoulderSlope,
                "descending shoulder slows substantially without reversal or a rectangular plateau");
            Check.That(At(0) == 0 && At(40) == 1000 * FixedPointMath.Q32One && At(128) == 0,
                "rounded peak keeps amplitude and half-open support over different pulse durations");
            var samples = PhysiologySignalGenerator.Start(Timeline, "AcqPleth125@1", 1,
                (Pulse with { DurationNs = duration }).CreateBands()).GenerateBefore(1_000_000_000, 125, 100);
            short[] shoulder = samples.Where(sample => sample.Tick.SimTimeNs >= 320_000_000 + duration * 72 / 128 &&
                sample.Tick.SimTimeNs <= 320_000_000 + duration * 88 / 128).Select(sample => sample.NormalizedValue).ToArray();
            Check.That(shoulder.Length >= 5 && shoulder.All(value => value is >= 439 and <= 481),
                "shoulder remains represented by multiple native125Hz samples");
        }
    }

    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Ecg, Pleth, 1, 1, 1, 0, 32,
        [new(Timeline, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
         new(Timeline, new(Pleth, "AcqPleth125@1", 1, 1, 0, 1), Pulse.CreateBands(), 250, 0)]);

    private static void PlethNativeDelayAndGroupRestorePreserveSamples()
    {
        var boundary = Group();
        Check.That(boundary.AdvanceTo(2_191_999_999, 548, 1, 100).Count == 0, "PPG processing delay is additional to source transit");
        var first = WaveformEnvelopeCodec.Decode(boundary.AdvanceTo(2_192_000_000, 1, 1, 100).Single());
        Check.That(first.StartSimTimeNs == 0 && first.Planes.Single(plane => plane.ChannelId == Pleth).Samples.Count == 25,
            "first shared block waits for the last 125Hz sample's two-second acquisition delay");
        var expected = Group().AdvanceTo(4_000_000_000, 1000, 20, 100);
        var split = Group();
        List<byte[]> actual = [];
        for (int step = 1; step <= 20; step++)
        {
            actual.AddRange(split.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            split = PhysiologyWaveformGroup.Restore(split.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "mixed ECG/Pleth wire bytes survive split generation and restore during active pulses and delay");
        var source = PhysiologySignalGenerator.Start(Timeline, "AcqPleth125@1", 1, Pulse.CreateBands())
            .GenerateBefore(2_000_000_000, 250, 100);
        var decoded = actual.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).SelectMany(block => block.Planes.Single(plane => plane.ChannelId == Pleth).Samples);
        Check.That(decoded.SequenceEqual(source.Select(sample => sample.NormalizedValue)) && source[60].NormalizedValue == 1000,
            "published 125Hz raw counts exactly match mechanical source samples");
    }

    private static void PlethRejectsInvalidPlansAndPreservesFailedGroup()
    {
        foreach (var invalid in new[] { Pulse with { TransitDelayNs = -1 }, Pulse with { DurationNs = 0 },
            Pulse with { DurationNs = long.MaxValue }, Pulse with { AmplitudeCounts = -1 }, Pulse with { AmplitudeCounts = 32768 } })
        {
            bool rejected = false;
            try { invalid.CreateBands(); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "PlethPulse.InvalidPlan"; }
            Check.That(rejected, "invalid support or raw amplitude rejects before returning bands");
        }
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(4_000_000_000, 1000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late block limit preserves every source and delay cursor");
    }
}
