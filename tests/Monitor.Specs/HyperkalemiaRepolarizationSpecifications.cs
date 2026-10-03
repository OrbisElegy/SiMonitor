// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class HyperkalemiaRepolarizationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(HyperkalemiaIllustrationProjectsDiffusePeakedTAndShortQt), HyperkalemiaIllustrationProjectsDiffusePeakedTAndShortQt),
    ];
    private static void HyperkalemiaIllustrationProjectsDiffusePeakedTAndShortQt()
    {
        var plan = HyperkalemiaRepolarizationReference.CreatePlan();
        var source = HyperkalemiaRepolarizationReference.CreateElectrodes();
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, source).GenerateBefore(800_000_000, 200, 100);
        var normal = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        int[] peaks = [500, 700, 200, -600, 150, 450, 300, 600, 1000, 1200, 1000, 700];
        Check.That(samples.Take(60).Zip(normal).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "P/PR and entire narrow QRS unchanged");
        for (int lead = 0; lead < 12; lead++)
        {
            Check.That(Math.Abs(samples[100].MicrovoltValues[lead] - peaks[lead]) <= 1, "diffuse authored peaks retain limb identities and Wilson chest reference");
            for (int delta = 1; delta < 15; delta++)
                Check.That(Math.Abs(samples[100 - delta].MicrovoltValues[lead] - samples[100 + delta].MicrovoltValues[lead]) <= 1, "T is symmetric about400ms");
            Check.That(samples.Skip(115).All(s => s.MicrovoltValues[lead] == 0), "T ends at460ms, QT300ms from QRS160ms");
        }
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "Einthoven identity is preserved");
        Check.That(samples.Count(s => s.Tick.SimTimeNs >= 340_000_000 && s.MicrovoltValues[9] > 600) < 20, "narrow T crown rather than broad hyperacute shape");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, HyperkalemiaRepolarizationReference.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II and projected II agree");
    }
}
