// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class MorphologyRecoverySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AuthoredMorphologiesRestoreRepresentativePartitions), AuthoredMorphologiesRestoreRepresentativePartitions),
        new(nameof(CompoundMorphologyRestoresAcrossClocksAndWiring), CompoundMorphologyRestoresAcrossClocksAndWiring),
    ];

    private static void AuthoredMorphologiesRestoreRepresentativePartitions()
    {
        // Morphology builders produce static bands; recovery belongs to the shared generator.
        // Keep each source family, but avoid replaying every contour option at every join.
        (string Name, RegularPhysiologyPlan Plan, IReadOnlyList<ElectrodeWaveformPlan> Electrodes)[] cases =
        [
            ("short PR", ShortPrReference.CreatePlan(), ShortPrReference.CreateElectrodes()),
            ("normal PR delta", NormalPrDeltaReference.CreatePlan(false), NormalPrDeltaReference.CreateElectrodes(false)),
            ("prolonged PR delta", NormalPrDeltaReference.CreatePlan(true), NormalPrDeltaReference.CreateElectrodes(true)),
            ("separate T/U", HypokalemiaRepolarizationReference.CreatePlan(), HypokalemiaRepolarizationReference.CreateElectrodes(false, false, false)),
            ("fused inverted T/U", HypokalemiaRepolarizationReference.CreatePlan(), HypokalemiaRepolarizationReference.CreateElectrodes(true, true, true)),
            ("QRS/T fusion", HyperkalemiaFusionReference.CreatePlan(), HyperkalemiaFusionReference.CreateElectrodes()),
            ("delayed conduction", HyperkalemiaConductionReference.CreatePlan(false), HyperkalemiaConductionReference.CreateElectrodes(false)),
            ("absent P", HyperkalemiaConductionReference.CreatePlan(true), HyperkalemiaConductionReference.CreateElectrodes(true)),
            ("peaked T", HyperkalemiaRepolarizationReference.CreatePlan(), HyperkalemiaRepolarizationReference.CreateElectrodes()),
            ("accelerated atrial", AcceleratedAtrialReference.CreatePlan(), AcceleratedAtrialReference.CreateElectrodes()),
            ("atrial escape", AtrialEscapeReference.CreatePlan(), AtrialEscapeReference.CreateElectrodes()),
            .. Enum.GetValues<DigitalisTShape>().Select(shape =>
                ($"digitalis {shape}", DigitalisEffectReference.CreatePlan(), DigitalisEffectReference.CreateElectrodes(shape))),
        ];
        foreach (var (name, plan, electrodes) in cases)
        {
            VerifyPartitions(name, plan, electrodes, EcgLimbPlacement.Standard,
                [173_000_000, 451_000_000, 817_000_000, 3_000_000_000]);
        }
        foreach (var mode in Enum.GetValues<CalciumIllustration>().Where(mode => mode != CalciumIllustration.Reference))
            VerifyPartitions($"calcium {mode}", CalciumRepolarizationReference.CreatePlan(),
                CalciumRepolarizationReference.CreateElectrodes(mode), EcgLimbPlacement.Standard,
                [73_000_000, 451_000_000, 817_000_000, 3_000_000_000]);
        foreach (var mode in Enum.GetValues<QuinidineIllustration>().Where(mode => mode != QuinidineIllustration.Reference))
            foreach (bool notchedP in new[] { false, true })
                VerifyPartitions($"quinidine {mode}, notched P {notchedP}", QuinidineEffectReference.CreatePlan(mode),
                    QuinidineEffectReference.CreateElectrodes(mode, notchedP), EcgLimbPlacement.Standard,
                    [73_000_000, 551_000_000, 917_000_000, 3_000_000_000]);
    }

    private static void CompoundMorphologyRestoresAcrossClocksAndWiring()
    {
        RegularPhysiologyPlan regular = new(0, 800_000_000, 160_000_000,
            80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
        var independent = regular with
        {
            IndependentVentricularPeriodNs = 1_100_000_000,
            VentricularElectricalOffsetNs = 900_000_000,
            VentricularMechanicalOffsetNs = 980_000_000,
        };
        var electrodes = TextbookElectrodeReference.CreateElectrodes(
            new(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]),
            tWave: new([1000, 1000, 1000, 1000, -1250, 0, 1250, 1000, 1000, 1000]),
            tShape: new(375),
            stSegment: new([0, 0, 0, 0, -200, -200, -200, -200, -200, -200],
                Enumerable.Repeat(100, 10).ToArray(), [0, 0, 0, 0, 200, -200, 100, 0, 300, -100]),
            pWave: new(Enumerable.Range(0, 10).Select(i => (EcgPWaveComponents?)new EcgPWaveComponents(i * 20, -i * 10)).ToArray()));
        var fused = TextbookElectrodeReference.CreateElectrodes(
            new(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 30, 40, 50, 60, 70]),
            tWave: new([1000, 1000, 1000, 1000, 1000, -1250, 0, 2000, 1000, 1000]), tShape: new(375),
            stSegment: new([0, 0, 0, 0, 200, 200, 200, 200, 200, 200],
                [0, 0, 0, 0, -100, -100, -100, -100, -100, -100],
                [0, 0, 0, 0, 200, 200, 200, 200, 200, 200]),
            pWave: new([null, null, null, null, new(150, -150), null, null, null, null, null]),
            fusion: new([null, null, null, null, null, new(200, 500, 50), new(200, 500, 50), null, null, null]));
        foreach (var (name, plan, wiring, bands) in new[]
        {
            ("conducted", regular, EcgLimbPlacement.Standard, electrodes),
            ("independent swapped limbs", independent, EcgLimbPlacement.SwapRaLa, electrodes),
            ("regional fusion, independent swapped limbs", independent, EcgLimbPlacement.SwapRaLa, fused),
        })
        {
            VerifyPartitions(name, plan, bands, wiring,
                [23_000_000, 173_000_000, 979_000_000, 1_201_000_000, 1_401_000_000, 2_817_000_000, 3_000_000_000]);
        }
        var ventricularOnly = ElectrodeSignalGenerator.Start(independent with { CardiacActivity = CardiacActivity.VentricularOnly },
            "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        Check.That(ventricularOnly.All(sample => sample.MicrovoltValues.All(value => value == 0)),
            "both P components follow atrial availability before the first independent ventricular event");
    }

    private static void VerifyPartitions(string name, RegularPhysiologyPlan plan, IReadOnlyList<ElectrodeWaveformPlan> electrodes,
        EcgLimbPlacement wiring, long[] boundaries)
    {
        var expected = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes, wiring)
            .GenerateBefore(boundaries[^1], 750, 100);
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes, wiring);
        List<ElectrodeSignalSample> actual = [];
        foreach (long boundary in boundaries)
        {
            actual.AddRange(source.GenerateBefore(boundary, 750, 100));
            if (boundary != boundaries[^1])
            {
                source = ElectrodeSignalGenerator.Restore(source.CaptureState());
            }
        }
        Check.That(expected.Count == actual.Count, $"{name}: recovery loses or duplicates no sample");
        for (int index = 0; index < expected.Count; index++)
        {
            Check.That(expected[index].Tick == actual[index].Tick &&
                expected[index].MicrovoltValues.SequenceEqual(actual[index].MicrovoltValues) &&
                Enum.GetValues<EcgLead>().All(lead => expected[index].ExactLeads[lead] == actual[index].ExactLeads[lead]),
                $"{name}: sample {index} retains its clock, exact projection and native values");
        }
    }
}
