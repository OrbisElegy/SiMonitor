// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class DextrocardiaSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(DextrocardiaDistinguishesAnatomyFromWiring), DextrocardiaDistinguishesAnatomyFromWiring),
        new(nameof(DextrocardiaPreservesMonitorTimingAndRecovery), DextrocardiaPreservesMonitorTimingAndRecovery),
    ];

    private static void DextrocardiaDistinguishesAnatomyFromWiring()
    {
        var plan = PhysiologyIllustrationConfiguration.Default.ResolvePlan();
        var normal = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes())
            .GenerateBefore(800_000_000, 200, 100);
        var mirror = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, DextrocardiaReference.CreateElectrodes())
            .GenerateBefore(800_000_000, 200, 100);
        var swapped = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(), EcgLimbPlacement.SwapRaLa)
            .GenerateBefore(800_000_000, 200, 100);
        int[] mapping = [0, 2, 1, 4, 3, 5];
        for (int sample = 0; sample < normal.Count; sample++)
        {
            for (int lead = 0; lead < 6; lead++)
            {
                Check.That(mirror[sample].MicrovoltValues[lead] == normal[sample].MicrovoltValues[mapping[lead]] * (lead == 0 ? -1 : 1),
                    "limb morphology mirrors the reference throughout P, QRS and T");
            }
        }
        Check.That(mirror[12].MicrovoltValues[0] < 0 && mirror[100].MicrovoltValues[0] < 0,
            "lead I has negative P and T waves");
        int previousR = int.MaxValue;
        for (int lead = 6; lead < 12; lead++)
        {
            short[] qrs = mirror.Skip(40).Take(20).Select(sample => sample.MicrovoltValues[lead]).ToArray();
            int r = qrs.Max();
            Check.That(r > 0 && r < previousR && (lead == 6 || -qrs.Min() > r),
                "left precordial R amplitude regresses and V2-V6 are S-dominant");
            previousR = r;
            Check.That(swapped.Select(sample => sample.MicrovoltValues[lead]).SequenceEqual(normal.Select(sample => sample.MicrovoltValues[lead])) &&
                !mirror.Select(sample => sample.MicrovoltValues[lead]).SequenceEqual(swapped.Select(sample => sample.MicrovoltValues[lead])),
                "arm reversal preserves normal chest morphology and differs from dextrocardia");
        }
    }

    private static void DextrocardiaPreservesMonitorTimingAndRecovery()
    {
        var config = PhysiologyIllustrationConfiguration.Default with { Dextrocardia = true };
        var plan = config.ResolvePlan();
        Check.That(plan == PhysiologyIllustrationConfiguration.Default.ResolvePlan(), "anatomy does not change electrical or mechanical event timing");
        var electrodes = DextrocardiaReference.CreateElectrodes();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_600_000_000, 100);
        var projection = ElectrodeWaveformComposition.Restore(new(electrodes, events));
        var monitor = EventWaveformComposition.Restore(new(DextrocardiaReference.CreateLeadIIBands(), events));
        for (long time = 0; time < 1_600_000_000; time += 4_000_000)
        {
            Check.That(projection.EvaluateAt(time).Leads[EcgLead.II].ToQ32() == monitor.EvaluateAt(time),
                "monitor II and twelve-lead II use the same electrode source");
        }
        var generator = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
        generator.GenerateBefore(196_000_000, 49, 100);
        var restored = ElectrodeSignalGenerator.Restore(generator.CaptureState());
        var expected = generator.GenerateBefore(1_600_000_000, 351, 100);
        var actual = restored.GenerateBefore(1_600_000_000, 351, 100);
        Check.That(expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick && pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
            "recovery inside a QRS preserves all twelve leads");
        var normalGroup = PhysiologyIllustrationSource.Create();
        var mirrorGroup = PhysiologyIllustrationSource.Create(config);
        for (int step = 1; step <= 20; step++)
        {
            var normalBlocks = normalGroup.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
            var mirrorBlocks = mirrorGroup.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
            Check.That(normalBlocks.Zip(mirrorBlocks).All(pair => pair.First.Planes.Skip(1).Zip(pair.Second.Planes.Skip(1))
                .All(planes => planes.First.Samples.SequenceEqual(planes.Second.Samples))), "other six channels retain reference perfusion and respiration");
        }
        foreach (var invalid in new[] { config with { Vt = true }, config with { Pacing = PacingIllustration.DualChamberDdd },
            config with { AtrialShape = EcgAtrialIllustration.LeftAtrialAbnormality } })
        {
            bool rejected = false;
            try { invalid.ResolvePlan(); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "unsupported combinations are rejected before source publication");
        }
    }
}
