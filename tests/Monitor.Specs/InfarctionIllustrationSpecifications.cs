// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class InfarctionIllustrationSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(InfarctionStagesExposeDistinctQrsAndRepolarization), InfarctionStagesExposeDistinctQrsAndRepolarization),
        new(nameof(InfarctionSelectionPreservesOtherLeadsAndPQTU), InfarctionSelectionPreservesOtherLeadsAndPQTU),
        new(nameof(InfarctionStagesRejectInvalidRegionsAndTiming), InfarctionStagesRejectInvalidRegionsAndTiming),
        new(nameof(InfarctionStagesRecoverAcrossIndependentClocks), InfarctionStagesRecoverAcrossIndependentClocks),
    ];
    private static ElectrodeWaveformComposition Shape(InfarctionIllustrationStage stage) =>
        ElectrodeWaveformComposition.Restore(new(TextbookElectrodeReference.CreateElectrodes(infarction: new(4, stage)),
            [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));

    private static void InfarctionStagesExposeDistinctQrsAndRepolarization()
    {
        long Value(ElectrodeWaveformComposition shape, long ns) => shape.EvaluateAt(ns).Leads[EcgLead.V3].ToQ32();
        long Peak(ElectrodeWaveformComposition shape, int from, int to) =>
            Enumerable.Range(from, to - from).Max(ms => Value(shape, ms * 1_000_000L));
        long Trough(ElectrodeWaveformComposition shape, int from, int to) =>
            Enumerable.Range(from, to - from).Min(ms => Value(shape, ms * 1_000_000L));
        var baseline = Shape(InfarctionIllustrationStage.None);
        var tall = Shape(InfarctionIllustrationStage.HyperacuteT);
        Check.That(Peak(tall, 180, 360) > Peak(baseline, 180, 360), "hyperacute T is taller");
        for (int ms = 0; ms <= 80; ms++)
        { Check.That(Math.Abs(Value(tall, ms * 1_000_000L) - Value(baseline, ms * 1_000_000L)) < 16, "initial high T keeps reference QRS"); }
        var injury = Shape(InfarctionIllustrationStage.HyperacuteInjury);
        Check.That(Value(injury, 36_000_000) > Value(baseline, 30_000_000) &&
            Value(injury, 90_000_000) != 0, "regional injury illustration increases amplitude and stretches QRS");
        var q = Shape(InfarctionIllustrationStage.AcuteQInvertedT);
        var qs = Shape(InfarctionIllustrationStage.AcuteQsInvertedT);
        Check.That(Value(q, 15_000_000) < -590 * FixedPointMath.Q32One && Peak(q, 40, 60) < 300 * FixedPointMath.Q32One &&
            Peak(q, 40, 60) > 200 * FixedPointMath.Q32One, "Q wave and reduced surviving R coexist");
        Check.That(Peak(qs, 0, 60) <= 16 && Trough(qs, 0, 60) < -1000 * FixedPointMath.Q32One,
            "QS loses the positive R deflection");
        Check.That(Value(q, 100_000_000) > 0 && Trough(q, 180, 360) < -400 * FixedPointMath.Q32One,
            "acute Q, elevated ST and negative T coexist");
        var deep = Shape(InfarctionIllustrationStage.SubacuteDeepT);
        var shallow = Shape(InfarctionIllustrationStage.SubacuteRecoveringT);
        var old = Shape(InfarctionIllustrationStage.OldQNormalT);
        Check.That(Math.Abs(Value(deep, 120_000_000)) < 16 && Math.Abs(Value(shallow, 120_000_000)) < 16,
            "subacute ST returns to baseline");
        Check.That(Trough(deep, 180, 360) < Trough(shallow, 180, 360) && Trough(shallow, 180, 360) < 0,
            "two subacute snapshots show progressively shallower inversion");
        Check.That(Peak(old, 180, 360) > 0 &&
            Math.Abs(Value(old, 15_000_000) - Value(deep, 15_000_000)) < 16, "old illustration retains Q while T recovers");
        var inverted = Shape(InfarctionIllustrationStage.OldQInvertedT);
        var low = Shape(InfarctionIllustrationStage.OldQLowT);
        Check.That(Trough(inverted, 180, 360) < 0 && Peak(low, 180, 360) < Peak(old, 180, 360),
            "old inverted and low T alternatives remain distinct");
    }

    private static void InfarctionSelectionPreservesOtherLeadsAndPQTU()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 30, 40, 50, 60, 70]);
        IReadOnlyList<ElectrodeWaveformPlan> Source(InfarctionIllustrationStage stage) =>
            TextbookElectrodeReference.CreateElectrodes(u, tShape: new(375),
                fusion: new([null, null, null, null, null, new(200, 500, 50), null, null, null, null]),
                infarction: new(12, stage));
        var original = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Source(InfarctionIllustrationStage.None)).GenerateBefore(1_600_000_000, 400, 100);
        foreach (var stage in Enum.GetValues<InfarctionIllustrationStage>())
        {
            var actual = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Source(stage)).GenerateBefore(1_600_000_000, 400, 100);
            var changed = new HashSet<int>();
            for (int i = 0; i < actual.Count; i++)
            {
                Check.That(actual[i].Tick == original[i].Tick, "stage changes never alter acquisition time");
                for (int lead = 0; lead < 12; lead++)
                {
                    bool equal = actual[i].MicrovoltValues[lead] == original[i].MicrovoltValues[lead];
                    if (stage == InfarctionIllustrationStage.None || lead is not (8 or 9) || i % 200 < 40 || i % 200 >= 130)
                    { Check.That(equal, "unselected leads and samples outside QRS-to-QT remain identical"); }
                    if (!equal) { changed.Add(lead); }
                }
            }
            Check.That(stage == InfarctionIllustrationStage.None ? changed.Count == 0 : changed.SetEquals([8, 9]),
                "all active snapshots affect exactly V3/V4");
        }
    }

    private static void InfarctionStagesRejectInvalidRegionsAndTiming()
    {
        foreach (var plan in new[] { new EcgChestInfarctionPlan(-1, InfarctionIllustrationStage.None),
            new EcgChestInfarctionPlan(64, InfarctionIllustrationStage.HyperacuteT), new EcgChestInfarctionPlan(0, (InfarctionIllustrationStage)99) })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(infarction: plan); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgInfarction.InvalidPlan"; }
            Check.That(rejected, "unknown stage/region rejects even when inactive");
        }
        bool tooShort = false;
        try
        {
            TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { QtIntervalNs = 260_000_000 },
            infarction: new(4, InfarctionIllustrationStage.HyperacuteInjury));
        }
        catch (ArgumentException) { tooShort = true; }
        Check.That(tooShort, "regional QRS widening must still fit existing QT");
    }

    private static void InfarctionStagesRecoverAcrossIndependentClocks()
    {
        var plan = Plan with { IndependentVentricularPeriodNs = 1_100_000_000, VentricularElectricalOffsetNs = 900_000_000, VentricularMechanicalOffsetNs = 980_000_000 };
        foreach (var stage in new[] { InfarctionIllustrationStage.HyperacuteInjury, InfarctionIllustrationStage.AcuteMonophasic, InfarctionIllustrationStage.AcuteQsInvertedT })
        {
            var electrodes = TextbookElectrodeReference.CreateElectrodes(infarction: new(63, stage));
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes, EcgLimbPlacement.SwapRaLa);
            var expected = source.GenerateBefore(2_800_000_000, 700, 100);
            source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes, EcgLimbPlacement.SwapRaLa);
            List<ElectrodeSignalSample> actual = [];
            for (int step = 1; step <= 175; step++)
            {
                actual.AddRange(source.GenerateBefore(step * 16_000_000L, 4, 100));
                source = ElectrodeSignalGenerator.Restore(source.CaptureState());
            }
            Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick && pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
                "stage snapshots recover exactly across independent clocks and wiring");
        }
    }
}
