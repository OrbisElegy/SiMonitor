// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RegionalRepolarizationSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(RegionalDelayStretchesTWithoutMovingItsOnset), RegionalDelayStretchesTWithoutMovingItsOnset),
        new(nameof(RegionalDelayRetainsOtherLeadsAndWilson), RegionalDelayRetainsOtherLeadsAndWilson),
        new(nameof(RegionalDelayRejectsOverflowAndUOverlap), RegionalDelayRejectsOverflowAndUOverlap),
        new(nameof(RegionalDelayRecoversAndZeroPreservesBytes), RegionalDelayRecoversAndZeroPreservesBytes),
    ];
    private static IReadOnlyList<ElectrodeWaveformPlan> Source(long delay, InfarctionTerritory territory = InfarctionTerritory.CustomChest,
        InfarctionIllustrationStage stage = InfarctionIllustrationStage.HyperacuteT, EcgUWavePlan? u = null) =>
        TextbookElectrodeReference.CreateElectrodes(uWave: u, infarction: new(4, stage, territory, delay));

    private static void RegionalDelayStretchesTWithoutMovingItsOnset()
    {
        var events = new PhysiologyCycleEvent[] { new(0, PhysiologyCycleEventKind.VentricularElectrical, 0) };
        var baseline = ElectrodeWaveformComposition.Restore(new(Source(0), events));
        var extended = ElectrodeWaveformComposition.Restore(new(Source(80_000_000), events));
        long Value(ElectrodeWaveformComposition s, long time) => s.EvaluateAt(time).Leads[EcgLead.V3].ToQ32();
        for (long time = 0; time <= 180_000_000; time += 1_000_000)
        { Check.That(Value(baseline, time) == Value(extended, time), "P/QRS/ST and T onset do not shift"); }
        Check.That(Value(extended, 181_000_000) > 0 && Value(extended, 360_000_000) > 0 &&
            Math.Abs(Value(extended, 440_000_000)) < 16, "T starts at the original time and finishes at extended QT");
        Check.That(Math.Abs(Value(baseline, 292_500_000) - 900 * FixedPointMath.Q32One) < 16 &&
            Math.Abs(Value(extended, 342_500_000) - 900 * FixedPointMath.Q32One) < 16,
            "stretch changes peak time rather than peak amplitude");
        foreach (var stage in new[] { InfarctionIllustrationStage.HyperacuteInjury, InfarctionIllustrationStage.AcuteMonophasic })
        {
            var source = ElectrodeWaveformComposition.Restore(new(Source(80_000_000, stage: stage), events));
            Check.That(source.EvaluateAt(400_000_000).Leads[EcgLead.V3].ToQ32() > 0 &&
                Math.Abs(source.EvaluateAt(440_000_000).Leads[EcgLead.V3].ToQ32()) < 16,
                "unified contours also extend to new QT");
        }
    }

    private static void RegionalDelayRetainsOtherLeadsAndWilson()
    {
        foreach (var territory in new[] { InfarctionTerritory.CustomChest, InfarctionTerritory.Inferior, InfarctionTerritory.Lateral })
        {
            var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Source(0, territory)).GenerateBefore(800_000_000, 200, 100);
            var extended = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Source(80_000_000, territory)).GenerateBefore(800_000_000, 200, 100);
            for (int i = 0; i < extended.Count; i++)
            {
                Check.That(baseline[i].Tick == extended[i].Tick &&
                    baseline[i].ExactLeads.WilsonCentralTerminal == extended[i].ExactLeads.WilsonCentralTerminal,
                    "regional delay preserves sample clock and exact Wilson");
                for (int lead = 0; lead < 12; lead++)
                {
                    bool unaffected = territory switch
                    {
                        InfarctionTerritory.CustomChest => lead != 8,
                        InfarctionTerritory.Inferior => lead == 0 || lead >= 6,
                        _ => lead == 1 || lead is >= 6 and < 10,
                    };
                    if (unaffected || i < 85)
                    { Check.That(baseline[i].MicrovoltValues[lead] == extended[i].MicrovoltValues[lead], "unaffected leads and pre-T samples remain identical"); }
                }
            }
        }
    }

    private static void RegionalDelayRejectsOverflowAndUOverlap()
    {
        foreach (long delay in new[] { -1L, 281_000_000, long.MaxValue })
        {
            bool rejected = false;
            try { _ = Source(delay); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "negative, overflowing and cycle-exceeding extension reject");
        }
        var u = new EcgUWavePlan(100_000_000, 120_000_000, [0, 0, 0, 0, 0, 0, 60, 0, 0, 0]);
        _ = Source(100_000_000, u: u);
        bool overlap = false;
        try { _ = Source(100_000_001, u: u); }
        catch (EventWaveformException e) { overlap = e.ReasonCode == "EcgInfarction.UOverlap"; }
        Check.That(overlap, "adjacent T end/U onset is allowed but overlap rejects without moving U");
        var events = new PhysiologyCycleEvent[] { new(0, PhysiologyCycleEventKind.VentricularElectrical, 0) };
        var a = ElectrodeWaveformComposition.Restore(new(Source(0, u: u), events));
        var b = ElectrodeWaveformComposition.Restore(new(Source(80_000_000, u: u), events));
        for (long time = 460_000_000; time <= 580_000_000; time += 1_000_000)
        { Check.That(a.EvaluateAt(time).Leads[EcgLead.V3] == b.EvaluateAt(time).Leads[EcgLead.V3], "U retains its original support and samples"); }
    }

    private static void RegionalDelayRecoversAndZeroPreservesBytes()
    {
        var baseline = TextbookElectrodeReference.CreateElectrodes(infarction: new(4, InfarctionIllustrationStage.HyperacuteT));
        var expectedZero = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, baseline).GenerateBefore(800_000_000, 200, 100);
        var actualZero = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Source(0)).GenerateBefore(800_000_000, 200, 100);
        Check.That(expectedZero.Zip(actualZero).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "zero retains old source bytes");
        var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Source(80_000_000, InfarctionTerritory.Inferior));
        var expected = source.GenerateBefore(1_600_000_000, 400, 100);
        source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Source(80_000_000, InfarctionTerritory.Inferior));
        List<ElectrodeSignalSample> actual = [];
        for (int step = 1; step <= 100; step++)
        {
            actual.AddRange(source.GenerateBefore(step * 16_000_000L, 4, 100));
            // Restore around waveform joins and cycle wrap; still compare every native sample.
            if (step is 1 or 9 or 10 or 11 or 13 or 15 or 22 or 28 or 37 or 38 or 49 or 50 or 51 or 99)
            { source = ElectrodeSignalGenerator.Restore(source.CaptureState()); }
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)),
            "extended repolarization restores deterministically");
    }
}
