// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EcgQtCorrectionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(QtMatchesIndependentFormulaVectors), QtMatchesIndependentFormulaVectors),
        new(nameof(QtRoundsExactMidpointsAndRejectsInvalidInputs), QtRoundsExactMidpointsAndRejectsInvalidInputs),
        new(nameof(CorrectedTimingReachesNativeLeadsAndRestores), CorrectedTimingReachesNativeLeadsAndRestores),
        new(nameof(CorrectedTimingKeepsUOutsideQtAndRejectsOverlap), CorrectedTimingKeepsUOutsideQtAndRejectsOverlap),
    ];

    private static void QtMatchesIndependentFormulaVectors()
    {
        // Independently evaluated with Python Decimal at 70 digits, half-even ns.
        (long Rr, long Bazett, long Fridericia)[] vectors =
        [
            (600_000_000, 309_838_668, 337_373_066),
            (800_000_000, 357_770_876, 371_327_107),
            (1_000_000_000, 400_000_000, 400_000_000),
            (1_500_000_000, 489_897_949, 457_885_697),
            (10_000_000_000, 1_264_911_064, 861_773_876),
        ];
        foreach (var vector in vectors)
        {
            Check.That(new EcgQtCorrection(EcgQtCorrection.Bazett, 400_000_000, vector.Rr).ResolveQtIntervalNs() == vector.Bazett &&
                new EcgQtCorrection(EcgQtCorrection.Fridericia, 400_000_000, vector.Rr).ResolveQtIntervalNs() == vector.Fridericia,
                "same QTc changes QT with RR according to the explicitly versioned method and seconds unit");
        }
        Check.That(new EcgQtCorrection(EcgQtCorrection.Fridericia, 1_000_000_000, 10_000_000_000).ResolveQtIntervalNs() == 2_154_434_690,
            "maximum admitted cube-root inputs stay representable");
    }

    private static void Reject(Action action, string reason)
    {
        bool rejected = false;
        try { action(); }
        catch (EventWaveformException exception) { rejected = exception.ReasonCode == reason; }
        Check.That(rejected, "invalid correction or impossible timing rejects with a stable reason");
    }

    private static void QtRoundsExactMidpointsAndRejectsInvalidInputs()
    {
        foreach (var (method, rr) in new[] { (EcgQtCorrection.Bazett, 250_000_000L), (EcgQtCorrection.Fridericia, 125_000_000L) })
        {
            Check.That(new EcgQtCorrection(method, 1, rr).ResolveQtIntervalNs() == 0 &&
                new EcgQtCorrection(method, 3, rr).ResolveQtIntervalNs() == 2,
                "exact half-nanosecond roots round to the even neighbor in both directions");
        }
        var valid = new EcgQtCorrection(EcgQtCorrection.Bazett, 400_000_000, 800_000_000);
        foreach (var invalid in new[] { valid with { MethodId = "Bazett@2" }, valid with { QtcIntervalNs = 0 },
            valid with { QtcIntervalNs = 1_000_000_001 }, valid with { RrIntervalNs = -1 }, valid with { RrIntervalNs = long.MaxValue } })
        { Reject(() => invalid.ResolveQtIntervalNs(), "EcgQt.InvalidCorrection"); }
        Reject(() => (valid with { RrIntervalNs = 200_000_000 }).ResolveTiming(TextbookEcgReference.Timing),
            "EcgTiming.InconsistentIntervals");
    }

    private static ElectrodeSignalGenerator Source(EcgCycleTiming timing, EcgUWavePlan? u = null) => ElectrodeSignalGenerator.Start(
        new(0, timing.RrIntervalNs, timing.PrIntervalNs, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000),
        "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u, timing));

    private static void CorrectedTimingReachesNativeLeadsAndRestores()
    {
        foreach (string method in new[] { EcgQtCorrection.Bazett, EcgQtCorrection.Fridericia })
        {
            var slow = new EcgQtCorrection(method, 400_000_000, 1_000_000_000).ResolveTiming(TextbookEcgReference.Timing);
            var fast = new EcgQtCorrection(method, 400_000_000, 600_000_000).ResolveTiming(TextbookEcgReference.Timing);
            var expected = Source(fast).GenerateBefore(1_200_000_000, 300, 100);
            var slower = Source(slow).GenerateBefore(600_000_000, 150, 100);
            Check.That(expected.Take(60).Zip(slower).All(pair => pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
                "P and QRS keep their original samples when QT and RR change");
            Check.That(expected[130].MicrovoltValues.All(value => value == 0) && slower[130].MicrovoltValues[(int)EcgLead.II] > 0,
                "native 520ms sample has completed fast T while the slow T remains active");
            var source = Source(fast);
            var first = source.GenerateBefore(401_000_000, 101, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var actual = first.Concat(restored.GenerateBefore(1_200_000_000, 200, 100)).ToArray();
            Check.That(expected.Count == actual.Length && expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick &&
                pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)), "active corrected T survives split and restore on all twelve leads");
        }
    }

    private static void CorrectedTimingKeepsUOutsideQtAndRejectsOverlap()
    {
        var timing = new EcgQtCorrection(EcgQtCorrection.Fridericia, 400_000_000, 1_000_000_000)
            .ResolveTiming(TextbookEcgReference.Timing);
        EcgUWavePlan u = new(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]);
        var off = Source(timing).GenerateBefore(1_000_000_000, 250, 100);
        var on = Source(timing, u).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(off.Zip(on).Where(pair => pair.First.Tick.SimTimeNs < 590_000_000 || pair.First.Tick.SimTimeNs >= 710_000_000)
            .All(pair => pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)) &&
            on.Any(frame => frame.Tick.SimTimeNs > 590_000_000 && frame.MicrovoltValues[(int)EcgLead.V3] > 0),
            "optional U follows the resolved T endpoint, preserving QT and preceding bands");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(u with { DurationNs = 500_000_000 }, timing), "EcgUWave.InvalidPlan");
    }
}
