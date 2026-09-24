// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PressureZeroOffsetSpecifications
{
    private static readonly Guid Abp = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pa = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static PhysiologyWaveformChannelPlan[] Plans(int offset)
    {
        RegularPhysiologyPlan physiology = new(0, 800_000_000, 160_000_000,
            80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
        VascularPressurePlan pressure = new(80_000_000, 240_000_000,
            1_500_000_000, 10_000, 1200, 26_000);
        return [pressure.CreateChannel(physiology, Abp, 3) with { PressureZeroOffsetCentiMmHg = offset },
            pressure.CreateChannel(physiology, Pa, 1)];
    }
    private static PhysiologyWaveformGroup Start(int offset) => Start(Plans(offset));
    private static PhysiologyWaveformGroup Start(PhysiologyWaveformChannelPlan[] plans) =>
        PhysiologyWaveformGroup.Start(Abp, Pa, 1, 1, 1, 0, 32, plans);
    public static Specification[] All =>
    [
        new(nameof(ZeroOffsetChangesOnlySelectedMeasuredPressure), ZeroOffsetChangesOnlySelectedMeasuredPressure),
        new(nameof(ZeroOffsetPreservesCvpReferenceAndRecovery), ZeroOffsetPreservesCvpReferenceAndRecovery),
        new(nameof(ZeroOffsetRecoveryRevalidatesMeasuredPendingSamples), ZeroOffsetRecoveryRevalidatesMeasuredPendingSamples),
        new(nameof(ZeroOffsetRejectsUnitsBoundsAndOverflowAtomically), ZeroOffsetRejectsUnitsBoundsAndOverflowAtomically),
    ];

    private static void ZeroOffsetChangesOnlySelectedMeasuredPressure()
    {
        foreach (int offset in new[] { -1500, 0, 1500 })
        {
            var baseline = Start(0);
            var measured = Start(offset);
            var expected = baseline.AdvanceTo(2_000_000_000, 250, 10, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
            var actual = measured.AdvanceTo(2_000_000_000, 250, 10, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
            Check.That(actual.Length > 0 && actual.Length == expected.Length, "offset retains block timing");
            foreach (var pair in expected.Zip(actual))
            {
                Check.That(pair.First.Planes[0].Samples.Zip(pair.Second.Planes[0].Samples).All(p => p.Second - p.First == offset),
                    "signed centi-mmHg error changes every selected measurement by exactly the configured amount");
                Check.That(pair.First.Planes[1].Samples.SequenceEqual(pair.Second.Planes[1].Samples), "other pressure channel is isolated");
                Check.That(pair.Second.Planes[0].QualityRanges.All(range => range.QualityFlags == 3), "caller-supplied quality survives acquisition");
            }
            Check.That(JsonSerializer.Serialize(baseline.CaptureState().Channels.Select(c => c.Generator)) ==
                JsonSerializer.Serialize(measured.CaptureState().Channels.Select(c => c.Generator)), "patient source and timing are unchanged");
        }
        // Negative sensor readings are permitted; the physiological reservoir stays positive.
        var negative = Start(-15000).AdvanceTo(400_000_000, 50, 2, 100);
        Check.That(WaveformEnvelopeCodec.Decode(negative.Single()).Planes[0].Samples.All(x => x < 0), "no clamp to physiological zero");
    }

    private static void ZeroOffsetPreservesCvpReferenceAndRecovery()
    {
        var cvp = FixedPerfusionPresets.SinglePulse.Venous.CreateChannel(Plans(0)[0].Physiology, Abp, 1);
        var baseline = Start([cvp]);
        var measured = Start([cvp with { PressureZeroOffsetCentiMmHg = -500 }]);
        for (int step = 1; step <= 20; step++)
        {
            var expected = baseline.AdvanceTo(step * 100_000_000L, 13, 1, 100);
            var actual = measured.AdvanceTo(step * 100_000_000L, 13, 1, 100);
            foreach (var pair in expected.Zip(actual))
            {
                var a = WaveformEnvelopeCodec.Decode(pair.First).Planes.Single();
                var b = WaveformEnvelopeCodec.Decode(pair.Second).Planes.Single();
                Check.That(a.OffsetNumerator == b.OffsetNumerator && a.OffsetDenominator == b.OffsetDenominator &&
                    a.Samples.Zip(b.Samples).All(p => p.Second - p.First == -500), "CVP true baseline metadata stays intact while raw measurements shift");
            }
            measured = PhysiologyWaveformGroup.Restore(measured.CaptureState());
        }
    }

    private static void ZeroOffsetRecoveryRevalidatesMeasuredPendingSamples()
    {
        var whole = Start(1500);
        var expected = whole.AdvanceTo(2_000_000_000, 250, 10, 100);
        var split = Start(1500);
        List<byte[]> actual = [];
        for (int step = 1; step <= 20; step++)
        {
            actual.AddRange(split.AdvanceTo(step * 100_000_000L, 13, 1, 100));
            split = PhysiologyWaveformGroup.Restore(split.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(p => p.First.SequenceEqual(p.Second)), "chunking and restore preserve measured wire bytes");
        var pending = Start(1500);
        _ = pending.AdvanceTo(100_000_000, 13, 1, 100);
        var state = pending.CaptureState();
        var channels = state.Channels.ToArray();
        channels[0] = channels[0] with { PressureZeroOffsetCentiMmHg = 0 };
        Reject(() => PhysiologyWaveformGroup.Restore(state with { Channels = channels }));
        Check.That(JsonSerializer.Serialize(state) == JsonSerializer.Serialize(PhysiologyWaveformGroup.Restore(state).CaptureState()), "valid pending state remains recoverable");
    }

    private static void ZeroOffsetRejectsUnitsBoundsAndOverflowAtomically()
    {
        foreach (int invalid in new[] { int.MinValue, short.MinValue - 1, short.MaxValue + 1, int.MaxValue })
        { Reject(() => Start(invalid)); }
        foreach (int boundary in new[] { (int)short.MinValue, short.MaxValue }) { _ = Start(boundary); }
        var wrongUnit = Plans(100);
        wrongUnit[0] = wrongUnit[0] with { Plane = wrongUnit[0].Plane with { ScaleDenominator = 1 } };
        Reject(() => Start(wrongUnit));
        var wrongProfile = Plans(100);
        wrongProfile[0] = wrongProfile[0] with { Plane = wrongProfile[0].Plane with { ProfileId = "AcqPleth125@1" } };
        Reject(() => Start(wrongProfile));
        var overflow = Start(short.MaxValue);
        string before = JsonSerializer.Serialize(overflow.CaptureState());
        bool rejected = false;
        try { _ = overflow.AdvanceTo(200_000_000, 25, 1, 100); }
        catch (PhysiologyWaveformGroupException ex) { rejected = ex.ReasonCode == "PhysiologyGroup.MeasuredPressureOutOfRange"; }
        Check.That(rejected && before == JsonSerializer.Serialize(overflow.CaptureState()), "wire overflow must leave all channels and assembler unchanged");
    }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (PhysiologyWaveformGroupException ex) { rejected = ex.ReasonCode == "PhysiologyGroup.InvalidCheckpoint"; }
        Check.That(rejected, "invalid acquisition configuration/checkpoint rejected");
    }
}
