// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class InfarctionComponentSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ComponentsCanIsolateOrCombineQrsSTAndT), ComponentsCanIsolateOrCombineQrsSTAndT),
        new(nameof(ComponentsRetainRegionalProjectionAndDelay), ComponentsRetainRegionalProjectionAndDelay),
        new(nameof(ComponentsRejectInvalidInputsAndRecover), ComponentsRejectInvalidInputsAndRecover),
    ];
    private static IReadOnlyList<ElectrodeWaveformPlan> Source(EcgInfarctionComponents components, InfarctionTerritory territory = InfarctionTerritory.CustomChest, long delay = 0) =>
        TextbookElectrodeReference.CreateElectrodes(infarction: new(4, InfarctionIllustrationStage.None, territory, delay, components));
    private static ElectrodeWaveformComposition Shape(EcgInfarctionComponents components) =>
        ElectrodeWaveformComposition.Restore(new(Source(components), [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));

    private static void ComponentsCanIsolateOrCombineQrsSTAndT()
    {
        var reference = Shape(new());
        var q = Shape(new(NecrosisIllustrationShape.QWithReducedR));
        var t = Shape(new(TPeakMicrovolts: -500));
        var st = Shape(new(JMicrovolts: 200, StEndMicrovolts: 100));
        var combined = Shape(new(NecrosisIllustrationShape.QWithReducedR, -500, 200, 100));
        long V(ElectrodeWaveformComposition shape, int ms) => shape.EvaluateAt(ms * 1_000_000L).Leads[EcgLead.V3].ToQ32();
        Check.That(V(q, 15) < -590 * FixedPointMath.Q32One && V(combined, 15) == V(q, 15), "Q shape is available without obligatory ischemic T or ST elevation");
        Check.That(V(st, 100) > 100 * FixedPointMath.Q32One && V(combined, 100) == V(st, 100), "ST injury is independent of necrosis selection");
        Check.That(V(t, 290) < -490 * FixedPointMath.Q32One && V(combined, 290) < -400 * FixedPointMath.Q32One, "negative T works alone or with elevated ST and Q");
        for (int ms = 0; ms < 360; ms++)
        {
            if (ms >= 80) { Check.That(V(q, ms) == V(reference, ms), "Q-only leaves ST/T unchanged"); }
            if (ms <= 180) { Check.That(V(t, ms) == V(reference, ms), "T-only leaves QRS/ST unchanged"); }
            Check.That(Math.Abs((V(combined, ms) - V(reference, ms)) -
                ((V(q, ms) - V(reference, ms)) + (V(t, ms) - V(reference, ms)) + (V(st, ms) - V(reference, ms)))) < 32,
                "combined projected shape equals independent changes within Q32 rounding");
        }
        var qs = Shape(new(NecrosisIllustrationShape.QS, 0));
        Check.That(V(qs, 25) < -1000 * FixedPointMath.Q32One && Math.Abs(V(qs, 290)) < 16, "QS and explicitly absent T can be selected together");
    }

    private static void ComponentsRetainRegionalProjectionAndDelay()
    {
        var events = new PhysiologyCycleEvent[] { new(0, PhysiologyCycleEventKind.VentricularElectrical, 0) };
        var baseline = ElectrodeWaveformComposition.Restore(new(TextbookElectrodeReference.CreateElectrodes(), events));
        foreach (var territory in new[] { InfarctionTerritory.CustomChest, InfarctionTerritory.Inferior, InfarctionTerritory.Lateral })
        {
            var source = ElectrodeWaveformComposition.Restore(new(Source(new(NecrosisIllustrationShape.QWithReducedR, -500, 200, 100), territory, 80_000_000), events));
            for (long ns = 0; ns < 600_000_000; ns += 1_234_567)
            {
                var a = source.EvaluateAt(ns).Leads; var b = baseline.EvaluateAt(ns).Leads;
                Check.That(a.WilsonCentralTerminal == b.WilsonCentralTerminal && a[EcgLead.I].Numerator + a[EcgLead.III].Numerator == a[EcgLead.II].Numerator,
                    "independent components retain exact Wilson and limb projection");
                for (int lead = 6; lead < 12; lead++)
                {
                    bool untouched = territory == InfarctionTerritory.Inferior || (territory == InfarctionTerritory.CustomChest ? lead != 8 : lead < 10);
                    if (untouched) { Check.That(a[(EcgLead)lead] == b[(EcgLead)lead], "other chest regions retain exact source potentials"); }
                }
            }
            var leadWithDelay = territory == InfarctionTerritory.Inferior ? EcgLead.II : territory == InfarctionTerritory.Lateral ? EcgLead.I : EcgLead.V3;
            Check.That(source.EvaluateAt(400_000_000).Leads[leadWithDelay].ToQ32() < 0 &&
                Math.Abs(source.EvaluateAt(440_000_000).Leads[leadWithDelay].ToQ32()) < 32,
                "independent components support regional QT extension with stage None");
        }
    }

    private static void ComponentsRejectInvalidInputsAndRecover()
    {
        foreach (var invalid in new[] { new EcgInfarctionComponents((NecrosisIllustrationShape)99), new(TPeakMicrovolts: 4001),
            new(JMicrovolts: -4001), new(StEndMicrovolts: 4001), new(StArchMicrovolts: -4001) })
        {
            bool rejected = false;
            try { _ = Source(invalid); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgInfarction.InvalidComponents"; }
            Check.That(rejected, "malformed independent components reject before publication");
        }
        RegularPhysiologyPlan plan = new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
        var electrodes = Source(new(NecrosisIllustrationShape.QS, -500, 200, 100, 50), InfarctionTerritory.Inferior, 80_000_000);
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        var expected = source.GenerateBefore(1_600_000_000, 400, 100);
        source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        List<ElectrodeSignalSample> actual = [];
        for (int step = 1; step <= 100; step++)
        {
            actual.AddRange(source.GenerateBefore(step * 16_000_000L, 4, 100));
            // Restore around waveform joins and cycle wrap; still compare every native sample.
            if (step is 1 or 9 or 10 or 11 or 13 or 15 or 22 or 28 or 37 or 38 or 49 or 50 or 51 or 99)
            { source = ElectrodeSignalGenerator.Restore(source.CaptureState()); }
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)),
            "combined independent fields recover without sample changes");
    }
}
