// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class InfarctionTerritorySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(InfarctionTerritoriesPreserveWilsonAndLeadIdentities), InfarctionTerritoriesPreserveWilsonAndLeadIdentities),
        new(nameof(InfarctionTerritoriesReachTheirNamedLeads), InfarctionTerritoriesReachTheirNamedLeads),
        new(nameof(InfarctionTerritoriesValidateAndRecover), InfarctionTerritoriesValidateAndRecover),
    ];
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    private static IReadOnlyList<ElectrodeWaveformPlan> Source(InfarctionTerritory territory, InfarctionIllustrationStage stage) =>
        TextbookElectrodeReference.CreateElectrodes(infarction: new(0, stage, territory));

    private static void InfarctionTerritoriesPreserveWilsonAndLeadIdentities()
    {
        var events = new PhysiologyCycleEvent[] { new(0, PhysiologyCycleEventKind.VentricularElectrical, 0) };
        var baseline = ElectrodeWaveformComposition.Restore(new(TextbookElectrodeReference.CreateElectrodes(), events));
        foreach (var territory in new[] { InfarctionTerritory.Inferior, InfarctionTerritory.Lateral })
            foreach (var stage in Enum.GetValues<InfarctionIllustrationStage>())
            {
                var source = ElectrodeWaveformComposition.Restore(new(Source(territory, stage), events));
                for (long time = 0; time < 600_000_000; time += 1_234_567)
                {
                    var actual = source.EvaluateAt(time).Leads;
                    var original = baseline.EvaluateAt(time).Leads;
                    Check.That(actual.WilsonCentralTerminal == original.WilsonCentralTerminal, "zero-sum source perturbation preserves Wilson exactly even between sample ticks");
                    Check.That(actual[EcgLead.I].Numerator + actual[EcgLead.III].Numerator == actual[EcgLead.II].Numerator &&
                        actual[EcgLead.AVR].Numerator + actual[EcgLead.AVL].Numerator + actual[EcgLead.AVF].Numerator == 0,
                        "named territories preserve exact limb identities");
                    Check.That(actual[territory == InfarctionTerritory.Inferior ? EcgLead.I : EcgLead.II] ==
                        original[territory == InfarctionTerritory.Inferior ? EcgLead.I : EcgLead.II], "orthogonal limb lead remains unchanged");
                    for (int lead = 6; lead < (territory == InfarctionTerritory.Inferior ? 12 : 10); lead++)
                    { Check.That(actual[(EcgLead)lead] == original[(EcgLead)lead], "unselected chest leads retain exact potentials"); }
                }
            }
    }

    private static void InfarctionTerritoriesReachTheirNamedLeads()
    {
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        foreach (var (territory, expected) in new[]
        {
            (InfarctionTerritory.Inferior, new[] { 1, 2, 3, 4, 5 }),
            (InfarctionTerritory.Lateral, new[] { 0, 2, 3, 4, 5, 10, 11 }),
            (InfarctionTerritory.Anteroseptal, new[] { 6, 7, 8 }),
            (InfarctionTerritory.Anterior, new[] { 8, 9, 10 }),
            (InfarctionTerritory.ExtensiveAnterior, new[] { 6, 7, 8, 9, 10 }),
        })
        {
            var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, Source(territory, InfarctionIllustrationStage.AcuteQInvertedT));
            var actual = source.GenerateBefore(800_000_000, 200, 100);
            var changed = new HashSet<int>();
            for (int i = 0; i < actual.Count; i++)
                for (int lead = 0; lead < 12; lead++)
                {
                    if (actual[i].MicrovoltValues[lead] != baseline[i].MicrovoltValues[lead])
                    {
                        Check.That(i >= 40 && i < 130, "territory change stays inside ventricular waveform support");
                        changed.Add(lead);
                    }
                }
            Check.That(changed.SetEquals(expected), "named primary and algebraically coupled lead set is correct");
            if (territory == InfarctionTerritory.Inferior)
            { Check.That(actual[65].MicrovoltValues[1] > baseline[65].MicrovoltValues[1] && actual[65].MicrovoltValues[2] > baseline[65].MicrovoltValues[2] && actual[65].MicrovoltValues[5] > baseline[65].MicrovoltValues[5], "inferior ST elevation reaches II/III/aVF"); }
            if (territory == InfarctionTerritory.Lateral)
            { Check.That(actual[65].MicrovoltValues[0] > baseline[65].MicrovoltValues[0] && actual[65].MicrovoltValues[4] > baseline[65].MicrovoltValues[4], "lateral ST elevation reaches I/aVL"); }
        }
    }

    private static void InfarctionTerritoriesValidateAndRecover()
    {
        bool rejected = false;
        try { _ = Source((InfarctionTerritory)99, InfarctionIllustrationStage.None); }
        catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgInfarction.InvalidPlan"; }
        Check.That(rejected, "unknown territory rejects even with stage disabled");
        foreach (var territory in new[] { InfarctionTerritory.Inferior, InfarctionTerritory.Lateral })
        {
            var electrodes = Source(territory, InfarctionIllustrationStage.AcuteQsInvertedT);
            var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes);
            var expected = source.GenerateBefore(800_000_000, 200, 100);
            source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes);
            List<ElectrodeSignalSample> actual = [];
            for (int step = 1; step <= 50; step++)
            {
                actual.AddRange(source.GenerateBefore(step * 16_000_000L, 4, 100));
                // Restore around waveform joins and cycle wrap; still compare every native sample.
                if (step is 1 or 9 or 10 or 11 or 13 or 15 or 22 or 28 or 37 or 38 or 49)
                { source = ElectrodeSignalGenerator.Restore(source.CaptureState()); }
            }
            Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair =>
                pair.First.Tick == pair.Second.Tick &&
                pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
                "limb territory recovery preserves every sample and its native clock");
        }
    }
}
