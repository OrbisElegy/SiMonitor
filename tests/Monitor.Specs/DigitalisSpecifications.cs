// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class DigitalisSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(DigitalisCompoundScoopsStAndEndsInNarrowUprightT), DigitalisCompoundScoopsStAndEndsInNarrowUprightT),
        new(nameof(DigitalisRestoresAcrossCompoundJoins), DigitalisRestoresAcrossCompoundJoins),
    ];
    private static void DigitalisCompoundScoopsStAndEndsInNarrowUprightT()
    {
        var plan = DigitalisEffectReference.CreatePlan();
        var source = DigitalisEffectReference.CreateElectrodes();
        var ordinary = TextbookElectrodeReference.CreateElectrodes(timing: DigitalisEffectReference.Timing);
        for (int i = 0; i < 10; i++)
        {
            Check.That(source[i].Bands.Count == 3, "compound replaces T instead of duplicating it");
            for (int j = 0; j < 2; j++)
                Check.That(source[i].Bands[j].TableQ32.SequenceEqual(ordinary[i].Bands[j].TableQ32) && source[i].Bands[j].DelayNs == ordinary[i].Bands[j].DelayNs && source[i].Bands[j].DurationNs == ordinary[i].Bands[j].DurationNs, "P/QRS components unchanged");
        }
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, source).GenerateBefore(1_000_000_000, 250, 100);
        var normal = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ordinary).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Take(55).Zip(normal).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "no effect before smooth terminal-QRS onset");
        var ii = samples.Select(s => s.MicrovoltValues[1]).ToArray();
        Check.That(ii[60] < -30 && ii[78] < -120 && ii[90] > ii[78] && ii[90] < 0, "ST descends into rounded trough then rises");
        Check.That(ii[110] >= 69 && ii[115] < ii[110] && ii[115] > 0, "low narrow upright terminal T");
        Check.That(samples.Skip(120).All(s => s.MicrovoltValues.All(v => v == 0)), "QT320ms ends at480ms, no latent ordinary T");
        Check.That(Enumerable.Range(61, 60).Max(i => Math.Abs(ii[i] - ii[i - 1])) < 20, "smooth composite through ST/T joins");
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, DigitalisEffectReference.CreateLeadIIBands()).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII projection parity");
    }
    private static void DigitalisRestoresAcrossCompoundJoins()
    {
        foreach (long boundary in new[] { 218_000_000L, 238_000_000, 310_000_000, 438_000_000, 478_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(DigitalisEffectReference.CreatePlan(), "AcqECGMonitor250@1", 1, DigitalisEffectReference.CreateElectrodes());
            source.GenerateBefore(boundary, 250, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(2_000_000_000, 500, 100);
            var b = restored.GenerateBefore(2_000_000_000, 500, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "compound recovery exact");
        }
    }
}
