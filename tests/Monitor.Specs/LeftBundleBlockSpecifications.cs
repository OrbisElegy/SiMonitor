// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class LeftBundleBlockSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(LeftBundleBlockHasAbsentLateralQAndDelayedNotchedR), LeftBundleBlockHasAbsentLateralQAndDelayedNotchedR),
        new(nameof(LeftBundleBlockRetainsConductionAndRecovers), LeftBundleBlockRetainsConductionAndRecovers),
    ];
    private static void LeftBundleBlockRetainsConductionAndRecovers() => RightBundleBlockSpecifications.VerifyConductionAndRecovery(true);
    private static void LeftBundleBlockHasAbsentLateralQAndDelayedNotchedR()
    {
        var plan = LeftBundleBlockReference.CreatePlan();
        var electrodes = LeftBundleBlockReference.CreateElectrodes();
        Check.That(electrodes.All(e => e.Bands[1].DurationNs == 160_000_000), "QRS160ms support");
        var baseline = TextbookElectrodeReference.CreateElectrodes(timing: LeftBundleBlockReference.Timing);
        Check.That(electrodes.Zip(baseline).All(p => p.First.Bands[0].TableQ32.SequenceEqual(p.Second.Bands[0].TableQ32)), "P morphology unchanged");
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        int V(int index, EcgLead lead) => samples[index].MicrovoltValues[(int)lead];
        Check.That(Enumerable.Range(41, 37).All(i => V(i, EcgLead.V1) < 0) && V(68, EcgLead.V1) < -1000, "V1 broad deep QS");
        Check.That(V(44, EcgLead.V2) is > 0 and < 80 && V(59, EcgLead.V2) < -1200, "V2 small r with deep broad S");
        foreach (var lead in new[] { EcgLead.I, EcgLead.AVL, EcgLead.V5, EcgLead.V6 })
        {
            Check.That(Enumerable.Range(41, 27).All(i => V(i, lead) > 0), "lateral initial q absent");
            Check.That(V(56, lead) > V(61, lead) && V(67, lead) > V(56, lead) && V(67, lead) > 700, "broad R with a notch and delayed dominant peak");
            int peak = Enumerable.Range(40, 40).MaxBy(i => V(i, lead));
            Check.That((peak - 40) * 4 > 60, "lateral R peak time exceeds60ms");
            Check.That(V(84, lead) < -50 && V(129, lead) < -200, "lateral secondary ST depression and inverted T");
        }
        foreach (var lead in new[] { EcgLead.V1, EcgLead.V2 })
        { Check.That(V(84, lead) > 50 && V(129, lead) > 250, "right chest ST/T oppose negative QRS"); }
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity with lateral ST changes");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, LeftBundleBlockReference.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II shares P/QRS/ST/T and limb subtraction");
    }
}
