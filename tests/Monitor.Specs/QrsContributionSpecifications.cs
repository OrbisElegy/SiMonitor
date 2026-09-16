// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class QrsContributionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(LossSubtractsExplicitContributionWithFiniteSupport), LossSubtractsExplicitContributionWithFiniteSupport),
        new(nameof(ContributionLossPreservesProjectionAndStaticRecovery), ContributionLossPreservesProjectionAndStaticRecovery),
        new(nameof(ContributionInputsRejectConflictsAndInvalidInactiveValues), ContributionInputsRejectConflictsAndInvalidInactiveValues),
    ];
    private static IReadOnlyList<ElectrodeWaveformPlan> Source(EcgQrsContributionLoss? loss, InfarctionTerritory territory = InfarctionTerritory.CustomChest) =>
        TextbookElectrodeReference.CreateElectrodes(zones: new(new(31), new(7), new(4, territory),
            new(TPeakMicrovolts: -500, JMicrovolts: 200, StEndMicrovolts: 100, ContributionLoss: loss), 80_000_000));
    private static ElectrodeWaveformComposition Shape(IReadOnlyList<ElectrodeWaveformPlan> electrodes) =>
        ElectrodeWaveformComposition.Restore(new(electrodes, [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)]));

    private static void LossSubtractsExplicitContributionWithFiniteSupport()
    {
        var baseline = Shape(Source(null)); var full = Shape(Source(new()));
        var half = Shape(Source(new(LossPermille: 500))); var zero = Shape(Source(new(LossPermille: 0)));
        long Delta(ElectrodeWaveformComposition source, long ns) => source.EvaluateAt(ns).Leads[EcgLead.V3].ToQ32() - baseline.EvaluateAt(ns).Leads[EcgLead.V3].ToQ32();
        Check.That(Math.Abs(Delta(full, 30_000_000) + 1200 * FixedPointMath.Q32One) < 16,
            "remove the supplied 1200uV contribution at its midpoint, not a Q template");
        for (long ns = 0; ns < 500_000_000; ns += 1_234_567)
        {
            Check.That(Math.Abs(Delta(full, ns) - 2 * Delta(half, ns)) < 32, "partial removal scales the contribution");
            foreach (var lead in Enum.GetValues<EcgLead>())
            {
                var expected = baseline.EvaluateAt(ns).Leads[lead];
                Check.That(zero.EvaluateAt(ns).Leads[lead] == expected, "zero removal retains exact source");
                if (lead != EcgLead.V3 || ns >= 60_000_000)
                { Check.That(full.EvaluateAt(ns).Leads[lead] == expected, "unselected leads and ST/T remain unchanged"); }
            }
        }
    }

    private static void ContributionLossPreservesProjectionAndStaticRecovery()
    {
        foreach (var territory in Enum.GetValues<InfarctionTerritory>())
        {
            var electrodes = Source(new(), territory); var sourceShape = Shape(electrodes); var baseline = Shape(Source(null));
            Check.That(electrodes.All(e => e.Bands.Count <= EventWaveformComposition.MaximumBandCount), "contribution fits bounded source bands");
            for (long ns = 0; ns < 100_000_000; ns += 1_234_567)
            {
                var p = sourceShape.EvaluateAt(ns).Leads;
                Check.That(p.WilsonCentralTerminal == baseline.EvaluateAt(ns).Leads.WilsonCentralTerminal &&
                    p[EcgLead.I].Numerator + p[EcgLead.III].Numerator == p[EcgLead.II].Numerator,
                    "regional removal retains exact Wilson and limb projection");
            }
        }
        var plans = Source(new(LossPermille: 375), InfarctionTerritory.Lateral);
        RegularPhysiologyPlan timeline = new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
        var generator = ElectrodeSignalGenerator.Start(timeline, "AcqECGMonitor250@1", 1, plans);
        var expectedSamples = generator.GenerateBefore(1_600_000_000, 400, 100);
        generator = ElectrodeSignalGenerator.Start(timeline, "AcqECGMonitor250@1", 1, plans);
        List<ElectrodeSignalSample> actual = [];
        for (int step = 1; step <= 100; step++)
        {
            actual.AddRange(generator.GenerateBefore(step * 16_000_000L, 4, 100));
            generator = ElectrodeSignalGenerator.Restore(generator.CaptureState());
        }
        Check.That(expectedSamples.Count == actual.Count && expectedSamples.Zip(actual).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)),
            "static contribution survives checkpoint boundaries exactly");
        Check.That(actual.Take(200).Zip(actual.Skip(200)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)),
            "the next beat does not advance disease or change loss parameters");
    }

    private static void ContributionInputsRejectConflictsAndInvalidInactiveValues()
    {
        foreach (var component in new[]
        {
            new EcgInfarctionComponents(ContributionLoss: new(-1)), new(ContributionLoss: new(4001)),
            new(ContributionLoss: new(DurationPermille: 99)), new(ContributionLoss: new(DurationPermille: 1001)),
            new(ContributionLoss: new(LossPermille: -1)), new(ContributionLoss: new(LossPermille: 1001)),
            new(NecrosisIllustrationShape.QS, ContributionLoss: new()), new(QrsTemplatePermille: 500, ContributionLoss: new()),
        })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(zones: new(new(), new(), new(), component)); }
            catch (EventWaveformException) { rejected = true; }
            Check.That(rejected, "inactive regions cannot hide malformed or conflicting contribution plans");
        }
        var zeroAmplitude = Shape(Source(new(0)));
        var reference = Shape(Source(null));
        Check.That(zeroAmplitude.EvaluateAt(30_000_000).Leads[EcgLead.V3] == reference.EvaluateAt(30_000_000).Leads[EcgLead.V3],
            "zero amplitude is neutral even at full removal");
    }
}
