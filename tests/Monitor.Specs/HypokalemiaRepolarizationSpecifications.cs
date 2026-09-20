// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class HypokalemiaRepolarizationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(HypokalemiaPreservesQtAndSeparatesProminentU), HypokalemiaPreservesQtAndSeparatesProminentU),
        new(nameof(HypokalemiaRestoresAcrossTAndU), HypokalemiaRestoresAcrossTAndU),
    ];
    private static void HypokalemiaPreservesQtAndSeparatesProminentU()
    {
        var plan = HypokalemiaRepolarizationReference.CreatePlan();
        var electrodes = HypokalemiaRepolarizationReference.CreateElectrodes();
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(1_000_000_000, 250, 100);
        var normal = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(timing: HypokalemiaRepolarizationReference.Timing)).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Take(55).Zip(normal).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "P and QRS before smooth terminal ST transition remain unchanged");
        int[] st = [-40, -80, -40, 60, 0, -60, -50, -80, -100, -100, -80, -60];
        int[] u = [80, 160, 80, -120, 0, 120, 150, 450, 450, 350, 250, 180];
        for (int lead = 0; lead < 12; lead++)
        {
            Check.That(Math.Abs(samples[75].MicrovoltValues[lead] - st[lead]) <= 1, "ST depression and coupled limb polarity follow electrode projection");
            long peakU = samples.Where(s => s.Tick.SimTimeNs is >= 590_000_000 and < 810_000_000).Max(s => Math.Abs(s.MicrovoltValues[lead]));
            Check.That(Math.Abs(peakU - Math.Abs(u[lead])) <= 2, "prominent U appears outside QT");
            Check.That(samples.Where(s => s.Tick.SimTimeNs is >= 560_000_000 and < 590_000_000).All(s => s.MicrovoltValues[lead] == 0), "T ends at560ms: QT400ms, distinct from U endpoint810ms: QU650ms");
            Check.That(samples.Where(s => s.Tick.SimTimeNs >= 812_000_000).All(s => s.MicrovoltValues[lead] == 0), "U ends before next atrial cycle");
        }
        foreach (int lead in new[] { 7, 8, 9 })
        {
            long t = samples.Where(s => s.Tick.SimTimeNs is >= 380_000_000 and < 560_000_000).Max(s => Math.Abs(s.MicrovoltValues[lead]));
            long up = samples.Where(s => s.Tick.SimTimeNs is >= 590_000_000 and < 810_000_000).Max(s => s.MicrovoltValues[lead]);
            Check.That(t <= 100 && up > 3 * t, "low T and prominent U are distinct, not a large late T");
        }
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity holds for ST, T and U");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, HypokalemiaRepolarizationReference.CreateLeadIIBands()).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II includes shared ST/T/U components");
    }
    private static void HypokalemiaRestoresAcrossTAndU()
    {
        foreach (long boundary in new[] { 450_000_000L, 578_000_000, 650_000_000, 810_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(HypokalemiaRepolarizationReference.CreatePlan(), "AcqECGMonitor250@1", 1, HypokalemiaRepolarizationReference.CreateElectrodes());
            source.GenerateBefore(boundary, 250, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(2_000_000_000, 500, 100);
            var b = restored.GenerateBefore(2_000_000_000, 500, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "T/U gap, U and next cycle restore exactly");
        }
    }
}
