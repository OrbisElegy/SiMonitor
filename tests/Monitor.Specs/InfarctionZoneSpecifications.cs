// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class InfarctionZoneSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SeparateZonesIsolateAndCombineTheirComponents), SeparateZonesIsolateAndCombineTheirComponents),
        new(nameof(SeparateLimbZonesPreserveWilsonAndRecover), SeparateLimbZonesPreserveWilsonAndRecover),
        new(nameof(SeparateZonesValidateModesBoundariesAndNeutrality), SeparateZonesValidateModesBoundariesAndNeutrality),
    ];
    private static EcgInfarctionComponents Components => new(NecrosisIllustrationShape.QWithReducedR, -500, 200, 100, 50);
    private static ElectrodeWaveformComposition Shape(EcgInfarctionZones? zones) =>
        ElectrodeWaveformComposition.Restore(new(TextbookElectrodeReference.CreateElectrodes(zones: zones),
            [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));

    private static void SeparateZonesIsolateAndCombineTheirComponents()
    {
        EcgInfarctionRegion off = new();
        var baseline = Shape(null);
        var i = Shape(new(new(31), off, off, Components, 80_000_000));
        var j = Shape(new(off, new(7), off, Components));
        var n = Shape(new(off, off, new(4), Components));
        var all = Shape(new(new(31), new(7), new(4), Components, 80_000_000));
        for (long time = 0; time < 600_000_000; time += 1_000_000)
            foreach (EcgLead lead in Enum.GetValues<EcgLead>())
            {
                long V(ElectrodeWaveformComposition s) => s.EvaluateAt(time).Leads[lead].ToQ32();
                long b = V(baseline);
                Check.That(Math.Abs((V(all) - b) - ((V(i) - b) + (V(j) - b) + (V(n) - b))) <= 32,
                    "overlapping changes sum rather than overwrite one another");
                if ((int)lead < 6 || lead == EcgLead.V6)
                { Check.That(V(all) == b, "outside all chest zones retains exact potentials"); }
                if (time >= 80_000_000) { Check.That(Math.Abs(V(n) - b) <= 16, "necrosis-only zone does not change ST/T"); }
                if (time <= 180_000_000) { Check.That(Math.Abs(V(i) - b) <= 16, "ischemia-only zone does not change QRS/ST"); }
            }
        Check.That(all.EvaluateAt(15_000_000).Leads[EcgLead.V3].ToQ32() < 0 &&
            all.EvaluateAt(100_000_000).Leads[EcgLead.V3].ToQ32() > 0 &&
            all.EvaluateAt(400_000_000).Leads[EcgLead.V3].ToQ32() < 0,
            "center can show Q, elevated ST and delayed inverted T together");
    }

    private static void SeparateLimbZonesPreserveWilsonAndRecover()
    {
        var zones = new EcgInfarctionZones(new(0, InfarctionTerritory.Inferior), new(0, InfarctionTerritory.Lateral),
            new(4), Components, 80_000_000);
        var baseline = Shape(null); var combined = Shape(zones);
        for (long time = 0; time < 600_000_000; time += 1_234_567)
        {
            var a = combined.EvaluateAt(time).Leads;
            Check.That(a.WilsonCentralTerminal == baseline.EvaluateAt(time).Leads.WilsonCentralTerminal &&
                a[EcgLead.I].Numerator + a[EcgLead.III].Numerator == a[EcgLead.II].Numerator,
                "different limb-component territories retain exact off-grid projection identities");
        }
        RegularPhysiologyPlan plan = new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
        var electrodes = TextbookElectrodeReference.CreateElectrodes(zones: zones);
        Check.That(electrodes.All(e => e.Bands.Count <= EventWaveformComposition.MaximumBandCount), "three-zone composition stays inside band limits");
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
            "multiple zones restore without changing acquired samples");
    }

    private static void SeparateZonesValidateModesBoundariesAndNeutrality()
    {
        foreach (var zones in new[]
        {
            new EcgInfarctionZones(null!, new(), new(), Components),
            new(new(64), new(), new(), Components),
            new(new(), new(0, (InfarctionTerritory)99), new(), Components),
            new(new(), new(), new(), Components, -1),
        })
        {
            bool rejected = false;
            try { _ = Shape(zones); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid zone fields reject even when inactive");
        }
        var valid = new EcgInfarctionZones(new(4), new(4), new(4), Components, 80_000_000);
        bool conflict = false;
        try { TextbookElectrodeReference.CreateElectrodes(tShape: new(500), zones: valid); }
        catch (EventWaveformException e) { conflict = e.ReasonCode == "EcgInfarction.ConflictingModes"; }
        Check.That(conflict, "ambiguous source modes reject rather than silently mix");
        bool overlap = false;
        try { TextbookElectrodeReference.CreateElectrodes(new(30_000_000, 120_000_000, [0, 0, 0, 0, 0, 0, 60, 0, 0, 0]), zones: valid); }
        catch (EventWaveformException e) { overlap = e.ReasonCode == "EcgInfarction.UOverlap"; }
        Check.That(overlap, "ischemic extension retains the U overlap guard");
        var reference = Shape(null);
        foreach (var plan in new[] { new EcgInfarctionZones(new(), new(), new(), Components), new(new(63), new(63), new(63), new()) })
        {
            var neutral = Shape(plan);
            for (long time = 0; time < 600_000_000; time += 4_000_000)
                foreach (EcgLead lead in Enum.GetValues<EcgLead>())
                { Check.That(reference.EvaluateAt(time).Leads[lead] == neutral.EvaluateAt(time).Leads[lead], "empty zones and neutral components preserve reference exactly"); }
        }
    }
}
