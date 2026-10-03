// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CapnogramTransportSpecifications
{
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Timeline => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static CapnogramPlan Plan(long delay = 0) => new(125_000_000, 250_000_000, 200_000_000, 5, 40, 2500, delay);
    public static Specification[] All =>
    [
        new(nameof(TransportShiftsOnlySignalTime), TransportShiftsOnlySignalTime),
        new(nameof(TransportPreservesAcquisitionAvailability), TransportPreservesAcquisitionAvailability),
        new(nameof(LongTransportSurvivesNativeRecovery), LongTransportSurvivesNativeRecovery),
        new(nameof(InvalidTransportAndLateFailureAreAtomic), InvalidTransportAndLateFailureAreAtomic),
    ];

    private static EventWaveformComposition Compose(long delay) => EventWaveformComposition.Restore(new(
        Plan(delay).CreateChannel(Timeline, Co2, 0).Bands, [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]));

    private static void TransportShiftsOnlySignalTime()
    {
        var original = Compose(0);
        foreach (long delay in new[] { 0L, 123_456_789, 5_000_000_000 })
        {
            var delayed = Compose(delay);
            Check.That(delayed.EvaluateAt(delay) == 0, "transport does not invent pre-arrival exhaled gas");
            for (long time = 0; time <= 2_100_000_000; time += 7_000_000)
            { Check.That(delayed.EvaluateAt(time + delay) == original.EvaluateAt(time), "sub-sample transport shifts the complete Q32 morphology exactly"); }
            var restored = EventWaveformComposition.Restore(delayed.CaptureState());
            Check.That(restored.EvaluateAt(delay + 375_000_000) == original.EvaluateAt(375_000_000), "transport and phase landmarks remain owned across restore");
            var channel = Plan(delay).CreateChannel(Timeline, Co2, 0);
            Check.That(channel.Physiology == Timeline && channel.Plane.ProfileId == "AcqCO2_100@1" &&
                channel.Plane.OffsetNumerator == 5 && channel.Plane.ScaleDenominator == 100 && channel.DelayCapacity == 200,
                "transport does not change patient breath timing, physical pressure units or processing profile");
        }
    }

    private static PhysiologyWaveformGroup Group(long delay) => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 60,
        [new RespirationPlan(1000).CreateChannel(Timeline, Resp, 0), Plan(delay).CreateChannel(Timeline, Co2, 0)]);

    private static void TransportPreservesAcquisitionAvailability()
    {
        foreach (long delay in new[] { 0L, 5_000_000_000 })
        {
            var group = Group(delay);
            Check.That(group.AdvanceTo(2_189_999_999, 548, 1, 100).Count == 0 &&
                group.AdvanceTo(2_190_000_000, 1, 1, 100).Count == 1,
                "processing latency still gates the first block while transported pre-arrival samples carry baseline");
        }
    }

    private static void LongTransportSurvivesNativeRecovery()
    {
        var actual = NativeRecoveryChecks.Verify(() => Group(5_000_000_000),
            [1_875_000_000, 2_189_999_999, 2_190_000_000, 3_750_000_000, 5_000_000_000, 6_875_000_000, 7_000_000_000, 8_750_000_000, 8_950_000_000, 12_000_000_000],
            1500, 60, 100, 50, nameof(LongTransportSurvivesNativeRecovery));
        var original = Group(0).AdvanceTo(12_000_000_000, 1500, 60, 100);
        short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
            .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();
        short[] delayedCo2 = Samples(actual, Co2);
        Check.That(delayedCo2.Take(500).All(value => value == 0) && delayedCo2.Skip(500).SequenceEqual(Samples(original, Co2).Take(500)) &&
            Samples(actual, Resp).SequenceEqual(Samples(original, Resp)),
            "five-second observed CO2 shift leaves Resp samples unchanged on their original shared sample clocks");
    }

    private static void InvalidTransportAndLateFailureAreAtomic()
    {
        foreach (long delay in new[] { -1L, long.MaxValue })
        {
            bool rejected = false;
            try { Plan(delay).CreateChannel(Timeline, Co2, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "Capnogram.InvalidPlan"; }
            Check.That(rejected, "negative or overflowing transport support rejects before channel creation");
        }
        var group = Group(5_000_000_000);
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(12_000_000_000, 1500, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late publication failure retains both channel clocks and delayed evidence");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { group.AdvanceTo(12_000_000_000, 1500, 60, 100, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && JsonSerializer.Serialize(group.CaptureState()) == before, "cancellation cannot publish a partially transported group");
    }
}
