// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class StArchSpecifications
{
    private static EcgStSegmentPlan Shape(int arch) => new(
        Enumerable.Repeat(200, 10).ToArray(), Enumerable.Repeat(200, 10).ToArray(), Enumerable.Repeat(arch, 10).ToArray());
    public static Specification[] All =>
    [
        new(nameof(StArchHasSignedRoundedCrownAndFixedEndpoints), StArchHasSignedRoundedCrownAndFixedEndpoints),
        new(nameof(StArchPreservesOutsideSamplesAndZeroParity), StArchPreservesOutsideSamplesAndZeroParity),
        new(nameof(StArchRejectsMalformedPlansAndOwnsTables), StArchRejectsMalformedPlansAndOwnsTables),
        new(nameof(StArchRestoresAcrossIndependentCycles), StArchRestoresAcrossIndependentCycles),
    ];

    private static void StArchHasSignedRoundedCrownAndFixedEndpoints()
    {
        foreach (int amplitude in new[] { -4000, -200, 200, 4000 })
        {
            var bands = TextbookElectrodeReference.CreateElectrodes(stSegment: Shape(amplitude))[4].Bands.Skip(3).ToArray();
            var source = EventWaveformComposition.Restore(new(bands, [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));
            long Value(long milliseconds) => source.EvaluateAt(milliseconds * 1_000_000);
            Check.That(Value(80) == 200 * FixedPointMath.Q32One && Value(180) == 200 * FixedPointMath.Q32One &&
                Value(130) == (200 + amplitude) * FixedPointMath.Q32One, "arch preserves both ST endpoints and reaches its signed midpoint amplitude");
            int sign = Math.Sign(amplitude);
            for (long ms = 81; ms <= 130; ms++)
            { Check.That(sign * (Value(ms) - Value(ms - 1)) >= 0, "arch rises monotonically to its crown"); }
            for (long ms = 131; ms <= 180; ms++)
            { Check.That(sign * (Value(ms) - Value(ms - 1)) <= 0, "arch falls monotonically from its crown"); }
            Check.That(sign * (Value(140) - 2 * Value(130) + Value(120)) < 0, "positive arch has an upward-bulging rounded crown, negative arch reverses it");
            foreach (long boundary in new[] { 80_000_000L, 180_000_000 })
            { Check.That(Math.Abs(source.EvaluateAt(boundary + 1) - source.EvaluateAt(boundary - 1)) < FixedPointMath.Q32One / 1000, "ST junctions have no voltage jump"); }
        }
    }

    private static void StArchPreservesOutsideSamplesAndZeroParity()
    {
        var original = TextbookElectrodeReference.CreateElectrodes(stSegment: new(Shape(0).JMicrovolts, Shape(0).EndMicrovolts));
        var zero = TextbookElectrodeReference.CreateElectrodes(stSegment: Shape(0));
        var changed = TextbookElectrodeReference.CreateElectrodes(stSegment: Shape(200));
        foreach (int electrode in Enumerable.Range(0, 10))
        {
            var events = new PhysiologyCycleEvent[] { new(0, PhysiologyCycleEventKind.AtrialElectrical, 0), new(160_000_000, PhysiologyCycleEventKind.VentricularElectrical, 0) };
            var baseline = EventWaveformComposition.Restore(new(original[electrode].Bands, events));
            var disabled = EventWaveformComposition.Restore(new(zero[electrode].Bands, events));
            var active = EventWaveformComposition.Restore(new(changed[electrode].Bands, events));
            for (long ns = 0; ns < 800_000_000; ns += 1_000_000)
            {
                Check.That(baseline.EvaluateAt(ns) == disabled.EvaluateAt(ns), "explicit zero arch preserves original fixed-point samples");
                if (ns <= 240_000_000 || ns >= 340_000_000)
                { Check.That(baseline.EvaluateAt(ns) == active.EvaluateAt(ns), "arch changes only open ST interior, preserving P/QRS/T and endpoint samples"); }
            }
        }
        var archOnly = TextbookElectrodeReference.CreateElectrodes(stSegment: new(new int[10], new int[10], Enumerable.Repeat(200, 10).ToArray()));
        Check.That(archOnly[4].Bands.Count == 4 && archOnly[4].Bands[3].DelayNs == 80_000_000 && archOnly[4].Bands[3].DurationNs == 100_000_000,
            "zero endpoint offsets still permit an ST arch with exact declared support");
    }

    private static void StArchRejectsMalformedPlansAndOwnsTables()
    {
        foreach (int[]? arch in new[] { new[] { 0 }, Enumerable.Repeat(4001, 10).ToArray(), Enumerable.Repeat(-4001, 10).ToArray() })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(stSegment: new(new int[10], new int[10], arch)); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgSt.InvalidPlan"; }
            Check.That(rejected, "invalid arch arrays/amplitudes reject with stable reason");
        }
        bool collapsed = false;
        try { TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { QtIntervalNs = 260_000_000 }, stSegment: Shape(200)); }
        catch (EventWaveformException e) { collapsed = e.ReasonCode == "EcgSt.InvalidPlan"; }
        Check.That(collapsed, "active arch requires nonzero ST time");
        int[] amplitudes = Enumerable.Repeat(200, 10).ToArray();
        var electrodes = TextbookElectrodeReference.CreateElectrodes(stSegment: new(new int[10], new int[10], amplitudes));
        long[] before = electrodes[4].Bands[3].TableQ32.ToArray();
        amplitudes[4] = -4000;
        Check.That(before.SequenceEqual(electrodes[4].Bands[3].TableQ32), "accepted arch tables do not alias input arrays");
    }

    private static void StArchRestoresAcrossIndependentCycles()
    {
        RegularPhysiologyPlan plan = new(0, 800_000_000, 900_000_000, 80_000_000, 980_000_000, 4_000_000_000, 2_000_000_000,
            IndependentVentricularPeriodNs: 1_100_000_000);
        var electrodes = TextbookElectrodeReference.CreateElectrodes(
            new(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]),
            tShape: new(375), stSegment: new(new int[10], new int[10], [0, 0, 0, 0, 200, -200, 100, 0, 300, -100]),
            pWave: new([null, null, null, null, new(150, -150), null, null, null, null, null]));
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        var expected = source.GenerateBefore(2_800_000_000, 700, 100);
        source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        List<ElectrodeSignalSample> actual = [];
        for (int step = 1; step <= 175; step++)
        {
            actual.AddRange(source.GenerateBefore(step * 16_000_000L, 4, 100));
            source = ElectrodeSignalGenerator.Restore(source.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick && pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
            "signed ST arches retain byte-equivalent samples across recovery, independent clocks and P/T/U");
    }
}
