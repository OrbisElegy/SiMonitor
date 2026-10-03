// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class StSegmentSpecifications
{
    private static EcgStSegmentPlan Offsets(int j, int end) => new([0, 0, 0, 0, j, j, j, j, j, j], [0, 0, 0, 0, end, end, end, end, end, end]);
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(StOffsetsHitLandmarksAndRemainContinuous), StOffsetsHitLandmarksAndRemainContinuous),
        new(nameof(StOffsetsPreserveOtherBandsAndProjection), StOffsetsPreserveOtherBandsAndProjection),
        new(nameof(StOffsetsOwnInputsAndRejectInvalidPlans), StOffsetsOwnInputsAndRejectInvalidPlans),
    ];

    private static void StOffsetsHitLandmarksAndRemainContinuous()
    {
        foreach (var (j, end) in new[] { (200, 200), (-200, -200), (100, 300), (200, -100) })
        {
            var electrode = TextbookElectrodeReference.CreateElectrodes(stSegment: Offsets(j, end))[4];
            var band = electrode.Bands[3];
            var source = EventWaveformComposition.Restore(new([band], [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));
            Check.That(source.EvaluateAt(60_000_000) == 0 && source.EvaluateAt(80_000_000) == j * FixedPointMath.Q32One &&
                source.EvaluateAt(180_000_000) == end * FixedPointMath.Q32One && source.EvaluateAt(360_000_000) == 0,
                "terminal QRS onset, J, ST end and QT end follow explicit source landmarks");
            long previous = source.EvaluateAt(80_000_000);
            for (long time = 81_000_000; time <= 180_000_000; time += 1_000_000)
            {
                long value = source.EvaluateAt(time);
                Check.That(end == j ? value == previous : end > j ? value >= previous : value <= previous, "ST contour is flat or monotonic according to separate endpoints");
                previous = value;
            }
            foreach (long boundary in new[] { 60_000_000L, 80_000_000, 180_000_000, 360_000_000 })
            { Check.That(Math.Abs(source.EvaluateAt(boundary + 1) - source.EvaluateAt(boundary - 1)) < FixedPointMath.Q32One / 1000, "junctions have no voltage step"); }
        }
    }

    private static void StOffsetsPreserveOtherBandsAndProjection()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]);
        var normal = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u)).GenerateBefore(1_600_000_000, 400, 100);
        foreach (var offsets in new[] { Offsets(0, 0), Offsets(200, -100) })
        {
            var actual = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u, stSegment: offsets)).GenerateBefore(1_600_000_000, 400, 100);
            bool differs = false;
            for (int i = 0; i < actual.Count; i++)
            {
                bool same = actual[i].MicrovoltValues.SequenceEqual(normal[i].MicrovoltValues);
                Check.That(actual[i].Tick == normal[i].Tick && actual[i].MicrovoltValues.Take(6).SequenceEqual(normal[i].MicrovoltValues.Take(6)), "limb samples and clocks remain unchanged");
                if (i % 200 < 55 || i % 200 >= 130 || offsets.JMicrovolts[4] == 0) { Check.That(same, "zero plan and samples outside the declared terminal-QRS/ST/T support retain original bytes"); }
                differs |= !same;
                var leads = actual[i].ExactLeads;
                Check.That(leads[EcgLead.I].Numerator + leads[EcgLead.III].Numerator == leads[EcgLead.II].Numerator, "projection identities remain exact");
            }
            Check.That(differs == (offsets.JMicrovolts[4] != 0), "enabled ST offsets change sampled chest potentials");
        }
    }

    private static void StOffsetsOwnInputsAndRejectInvalidPlans()
    {
        int[] j = [0, 0, 0, 0, 200, 200, 200, 200, 200, 200];
        var electrodes = TextbookElectrodeReference.CreateElectrodes(stSegment: new(j, j));
        long[] before = electrodes[4].Bands[3].TableQ32.ToArray();
        j[4] = -100;
        Check.That(before.SequenceEqual(electrodes[4].Bands[3].TableQ32), "owned bands do not alias caller offsets");
        foreach (var invalid in new[] { new EcgStSegmentPlan(null!, j), new EcgStSegmentPlan([0], j), Offsets(4001, 0), Offsets(0, -4001) })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(stSegment: invalid); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgSt.InvalidPlan"; }
            Check.That(rejected, "invalid ST offsets reject with a stable reason");
        }
        var shortTiming = TextbookEcgReference.Timing with { QtIntervalNs = 260_000_000 };
        _ = TextbookElectrodeReference.CreateElectrodes(timing: shortTiming, stSegment: Offsets(0, 0));
        bool collapsed = false;
        try { TextbookElectrodeReference.CreateElectrodes(timing: shortTiming, stSegment: Offsets(100, 100)); }
        catch (EventWaveformException e) { collapsed = e.ReasonCode == "EcgSt.InvalidPlan"; }
        Check.That(collapsed, "nonzero offsets require a noncollapsed ST interval");
    }


}
