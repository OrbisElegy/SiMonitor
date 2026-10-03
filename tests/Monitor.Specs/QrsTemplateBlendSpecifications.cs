// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class QrsTemplateBlendSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(BlendPreservesEndpointsAndOnlyChangesRegionalQrs), BlendPreservesEndpointsAndOnlyChangesRegionalQrs),
        new(nameof(BlendWorksAcrossZonesAndRecovers), BlendWorksAcrossZonesAndRecovers),
        new(nameof(BlendRejectsInvalidWeightsEvenWhenInactive), BlendRejectsInvalidWeightsEvenWhenInactive),
    ];

    private static EcgInfarctionComponents Components(int weight, NecrosisIllustrationShape shape) => new(shape, -500, 200, 100, 50, weight);
    private static IReadOnlyList<ElectrodeWaveformPlan> Source(int weight, NecrosisIllustrationShape shape, InfarctionTerritory territory, bool zones = false) =>
        zones ? TextbookElectrodeReference.CreateElectrodes(zones: new(new(31), new(7), new(4, territory), Components(weight, shape), 80_000_000)) :
        TextbookElectrodeReference.CreateElectrodes(infarction: new(4, InfarctionIllustrationStage.None, territory, 80_000_000, Components(weight, shape)));
    private static ElectrodeWaveformComposition Shape(IReadOnlyList<ElectrodeWaveformPlan> source) =>
        ElectrodeWaveformComposition.Restore(new(source, [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));

    private static void BlendPreservesEndpointsAndOnlyChangesRegionalQrs()
    {
        foreach (var territory in new[] { InfarctionTerritory.CustomChest, InfarctionTerritory.Inferior, InfarctionTerritory.Lateral })
            foreach (var shape in new[] { NecrosisIllustrationShape.QWithReducedR, NecrosisIllustrationShape.QS })
            {
                var reference = Shape(Source(1000, NecrosisIllustrationShape.Reference, territory));
                var zero = Shape(Source(0, shape, territory));
                var full = Shape(Source(1000, shape, territory));
                foreach (int weight in new[] { 1, 250, 500, 999 })
                {
                    var blended = Shape(Source(weight, shape, territory));
                    for (long ns = 0; ns < 500_000_000; ns += 1_234_567)
                    {
                        var b = blended.EvaluateAt(ns).Leads; var r = reference.EvaluateAt(ns).Leads;
                        var f = full.EvaluateAt(ns).Leads; var z = zero.EvaluateAt(ns).Leads;
                        Check.That(b.WilsonCentralTerminal == r.WilsonCentralTerminal &&
                            b[EcgLead.I].Numerator + b[EcgLead.III].Numerator == b[EcgLead.II].Numerator, "blend keeps exact projection");
                        foreach (var lead in Enum.GetValues<EcgLead>())
                        {
                            Check.That(z[lead] == r[lead], "zero weight equals reference QRS with same ST/T");
                            long bv = b[lead].ToQ32(), rv = r[lead].ToQ32(), fv = f[lead].ToQ32();
                            Check.That(Int128.Abs((Int128)(bv - rv) * 1000 - (Int128)(fv - rv) * weight) < 32_000,
                                "intermediate waveform follows weighted target within fixed-point rounding");
                            if (ns >= 80_000_000 || f[lead] == r[lead])
                            { Check.That(b[lead] == r[lead], "other components and untouched leads remain exact"); }
                        }
                    }
                }
            }
    }

    private static void BlendWorksAcrossZonesAndRecovers()
    {
        var territory = InfarctionTerritory.Lateral;
        var electrodes = Source(375, NecrosisIllustrationShape.QS, territory, true);
        var zero = Shape(Source(0, NecrosisIllustrationShape.QS, territory, true));
        var reference = Shape(Source(1000, NecrosisIllustrationShape.Reference, territory, true));
        var partial = Shape(electrodes);
        for (long ns = 0; ns < 500_000_000; ns += 1_234_567)
            foreach (var lead in Enum.GetValues<EcgLead>())
            {
                Check.That(zero.EvaluateAt(ns).Leads[lead] == reference.EvaluateAt(ns).Leads[lead], "zero disables only necrosis zone");
                if (ns >= 80_000_000)
                { Check.That(partial.EvaluateAt(ns).Leads[lead] == reference.EvaluateAt(ns).Leads[lead], "regional ST/T survive QRS recovery"); }
            }
        RegularPhysiologyPlan plan = new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
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
            "blended QRS recovery preserves samples");
    }

    private static void BlendRejectsInvalidWeightsEvenWhenInactive()
    {
        foreach (int weight in new[] { -1, 1001, int.MaxValue })
            foreach (bool zones in new[] { false, true })
            {
                bool rejected = false;
                try
                {
                    var components = new EcgInfarctionComponents(QrsTemplatePermille: weight);
                    _ = zones ? TextbookElectrodeReference.CreateElectrodes(zones: new(new(), new(), new(), components)) :
                        TextbookElectrodeReference.CreateElectrodes(infarction: new(0, InfarctionIllustrationStage.None, Components: components));
                }
                catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgInfarction.InvalidComponents"; }
                Check.That(rejected, "invalid weights cannot hide behind a disabled region or reference shape");
            }
    }
}
