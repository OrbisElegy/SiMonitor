// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CapnogramSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Timeline => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static CapnogramPlan Plan => new(125_000_000, 250_000_000, 200_000_000, 5, 40);
    public static Specification[] All =>
    [
        new(nameof(PhaseTimingIsExactOwnedAndBackwardsCompatible), PhaseTimingIsExactOwnedAndBackwardsCompatible),
        new(nameof(PhaseTimingRejectsInvalidMaps), PhaseTimingRejectsInvalidMaps),
        new(nameof(CapnogramAlignsCompleteCycleToBreathing), CapnogramAlignsCompleteCycleToBreathing),
        new(nameof(CapnogramTimingAndBaselineAreIndependent), CapnogramTimingAndBaselineAreIndependent),
        new(nameof(CapnogramNativeBlocksPreserveActiveFallAcrossRestore), CapnogramNativeBlocksPreserveActiveFallAcrossRestore),
        new(nameof(CapnogramRejectsImpossibleTimingAndRollsBack), CapnogramRejectsImpossibleTimingAndRollsBack),
    ];

    private static EventWaveformComposition Compose(EventWaveformBand band) => EventWaveformComposition.Restore(new([band],
        [new(0, PhysiologyCycleEventKind.ExpirationStart, 0)]));

    private static void PhaseTimingIsExactOwnedAndBackwardsCompatible()
    {
        EventWaveformPhasePoint[] points = [new(0, 0), new(80, 1), new(100, 4)];
        EventWaveformBand band = new(PhysiologyCycleEventKind.ExpirationStart, 0, 100, [0, Q, 0, -Q], points);
        var mapped = Compose(band);
        points[1] = new(50, 2);
        Check.That(mapped.EvaluateAt(40) == Q / 2 && mapped.EvaluateAt(80) == Q && mapped.EvaluateAt(90) == -Q / 2 &&
            mapped.EvaluateAt(100) == 0, "owned piecewise timing reaches exact independent landmarks and a half-open endpoint");
        Check.That(EventWaveformComposition.Restore(mapped.CaptureState()).EvaluateAt(90) == -Q / 2, "phase ownership survives restore");
        foreach (long duration in new[] { 100L, long.MaxValue })
        {
            var linear = Compose(band with { DurationNs = duration, PhasePoints = null });
            var identity = Compose(band with { DurationNs = duration, PhasePoints = new[] { new EventWaveformPhasePoint(0, 0), new(duration, 4) } });
            foreach (long time in new[] { 0, 1, duration / 2, duration - 1 })
            { Check.That(linear.EvaluateAt(time) == identity.EvaluateAt(time), "identity phase maps preserve frozen phase arithmetic including Int128 bounds"); }
        }
    }

    private static void PhaseTimingRejectsInvalidMaps()
    {
        EventWaveformPhasePoint[][] maps = [[], [new(0, 0)], [new(1, 0), new(100, 4)],
            [new(0, 0), new(99, 4)], [new(0, 0), new(100, 3)], [new(0, 0), new(0, 1), new(100, 4)],
            [new(0, 0), new(50, 4), new(100, 4)], [new(0, 0), new(101, 2), new(100, 4)]];
        foreach (var map in maps)
        {
            bool rejected = false;
            try { Compose(new(PhysiologyCycleEventKind.ExpirationStart, 0, 100, [0, Q, 0, -Q], map)); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EventWaveform.InvalidState"; }
            Check.That(rejected, "incomplete, nonmonotonic or mismatched time/phase endpoints reject");
        }
    }

    private static PhysiologySignalGenerator Source(CapnogramPlan? plan = null)
    {
        var channel = (plan ?? Plan).CreateChannel(Timeline, Co2, 0);
        return PhysiologySignalGenerator.Start(Timeline, channel.Plane.ProfileId, 1, channel.Bands);
    }

    private static void CapnogramAlignsCompleteCycleToBreathing()
    {
        var frames = Source().GenerateBefore(4_000_000_000, 400, 100);
        Check.That(frames.Take(201).All(frame => frame.NormalizedValue == 0) && frames[210].NormalizedValue > 0 &&
            frames[225].NormalizedValue == 3250 && frames[375].NormalizedValue == 3500 &&
            frames[380].NormalizedValue is > 0 and < 3500 && frames.Skip(395).All(frame => frame.NormalizedValue == 0),
            "dead space, rise, sloped plateau and next-inspiration fall stay continuous on native100Hz samples");
        var channel = Plan.CreateChannel(Timeline, Co2, 0);
        Check.That(channel.Plane.OffsetNumerator == 5 && channel.Plane.ScaleNumerator == 1 && channel.Plane.ScaleDenominator == 100,
            "nonzero inspiratory baseline uses explicit mmHg affine metadata");
        var absent = EventWaveformComposition.Restore(new(channel.Bands, [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));
        Check.That(absent.EvaluateAt(3_750_000_000) == 0, "no expiration means no invented exhaled CO2 increment");
    }

    private static void CapnogramTimingAndBaselineAreIndependent()
    {
        var original = Source().GenerateBefore(4_000_000_000, 400, 100);
        var changed = Source(Plan with { DeadSpaceNs = 225_000_000, RiseNs = 400_000_000, InspiratoryFallNs = 100_000_000 })
            .GenerateBefore(4_000_000_000, 400, 100);
        Check.That(changed[205].NormalizedValue == 0 && original[205].NormalizedValue > 0 &&
            changed[375].NormalizedValue == original[375].NormalizedValue && changed[385].NormalizedValue == 0 && original[385].NormalizedValue > 0,
            "independent phase durations keep expiration end aligned with next inspiration");
        var raised = Source(Plan with { BaselineMmHg = 10, EndExpiratoryMmHg = 45 }).GenerateBefore(4_000_000_000, 400, 100);
        Check.That(original.Select(frame => frame.NormalizedValue).SequenceEqual(raised.Select(frame => frame.NormalizedValue)),
            "raising baseline and end-expiratory target equally preserves raw increment morphology");
    }

    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Ecg, Co2, 1, 1, 1, 0, 40,
        [new(Timeline, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
         Plan.CreateChannel(Timeline, Co2, 0)]);

    private static void CapnogramNativeBlocksPreserveActiveFallAcrossRestore()
    {
        var boundary = Group();
        Check.That(boundary.AdvanceTo(2_189_999_999, 548, 1, 100).Count == 0 &&
            boundary.AdvanceTo(2_190_000_000, 1, 1, 100).Count == 1, "source phase timing does not bypass two-second CO2 processing delay");
        var expected = Group().AdvanceTo(6_000_000_000, 1500, 30, 100);
        var group = Group();
        List<byte[]> actual = [];
        for (int step = 1; step <= 30; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "mixed ECG/CO2 wire bytes and active prior-expiration fall survive split/restore");
        var planes = actual.Select(bytes => WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == Co2)).ToArray();
        Check.That(planes.All(plane => plane.ScaleDenominator == 100 && plane.OffsetNumerator == 5 && plane.SampleRateNumerator == 100) &&
            planes.SelectMany(plane => plane.Samples).SequenceEqual(Source().GenerateBefore(4_000_000_000, 400, 100).Select(frame => frame.NormalizedValue)),
            "encoded samples match the native source while preserving CO2 pressure units");
    }

    private static void CapnogramRejectsImpossibleTimingAndRollsBack()
    {
        foreach (var invalid in new[] { Plan with { DeadSpaceNs = 0 }, Plan with { RiseNs = long.MaxValue },
            Plan with { InspiratoryFallNs = 1_875_000_001 }, Plan with { BaselineMmHg = -1 }, Plan with { EndExpiratoryMmHg = 4 },
            Plan with { EndExpiratoryMmHg = 328 } })
        {
            bool rejected = false;
            try { invalid.CreateChannel(Timeline, Co2, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "Capnogram.InvalidPlan"; }
            Check.That(rejected, "impossible phase timing or raw amplitude rejects before publication");
        }
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(6_000_000_000, 1500, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late publication failure cannot advance either source");
    }
}
