// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class DigitalisSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(DigitalisCompoundScoopsStAndEndsInNarrowUprightT), DigitalisCompoundScoopsStAndEndsInNarrowUprightT),
        new(nameof(DigitalisVariantsChangeOnlyPostStContour), DigitalisVariantsChangeOnlyPostStContour),
        new(nameof(DigitalisJoinsRDescentToStWithoutRebound), DigitalisJoinsRDescentToStWithoutRebound),
    ];
    private static void DigitalisCompoundScoopsStAndEndsInNarrowUprightT()
    {
        var plan = DigitalisEffectReference.CreatePlan();
        var source = DigitalisEffectReference.CreateElectrodes();
        var ordinary = TextbookElectrodeReference.CreateElectrodes(timing: DigitalisEffectReference.Timing);
        for (int i = 0; i < 10; i++)
        {
            Check.That(source[i].Bands.Count == 2, "single QRS-ST-T compound without duplicated S recovery");
            Check.That(source[i].Bands[0].TableQ32.SequenceEqual(ordinary[i].Bands[0].TableQ32) && source[i].Bands[0].DelayNs == ordinary[i].Bands[0].DelayNs && source[i].Bands[0].DurationNs == ordinary[i].Bands[0].DurationNs, "P component unchanged");
        }
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, source).GenerateBefore(1_000_000_000, 250, 100);
        var normal = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ordinary).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Take(48).Zip(normal).All(p => p.First.MicrovoltValues.Zip(p.Second.MicrovoltValues).All(v => Math.Abs(v.First - v.Second) <= 1)), "P and early QRS preserved before R descent");
        short[] ii = samples.Select(s => s.MicrovoltValues[1]).ToArray();
        Check.That(ii[60] < 0 && ii[78] < -120 && ii[90] > ii[78] && ii[90] < 0, "ST descends into rounded trough then rises");
        Check.That(ii[110] >= 69 && ii[115] < ii[110] && ii[115] > 0, "low narrow upright terminal T");
        Check.That(samples.Skip(120).All(s => s.MicrovoltValues.All(v => v == 0)), "QT320ms ends at480ms, no latent ordinary T");
        Check.That(Enumerable.Range(61, 60).Max(i => Math.Abs(ii[i] - ii[i - 1])) < 20, "smooth composite through ST/T joins");
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, DigitalisEffectReference.CreateLeadIIBands()).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII projection parity");
    }
    private static void DigitalisVariantsChangeOnlyPostStContour()
    {
        var plan = DigitalisEffectReference.CreatePlan();
        var baseline = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, DigitalisEffectReference.CreateElectrodes()).GenerateBefore(1_000_000_000, 250, 100);
        foreach (var shape in new[] { DigitalisTShape.LowT, DigitalisTShape.InvertedT })
        {
            var source = DigitalisEffectReference.CreateElectrodes(shape);
            var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, source).GenerateBefore(1_000_000_000, 250, 100);
            Check.That(a.Take(78).Zip(baseline).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "P/QRS and descending ST through trough unchanged");
            Check.That(a.Skip(120).All(v => v.MicrovoltValues.All(x => x == 0)), "QT320 endpoint unchanged");
            Check.That(a.All(v => Math.Abs(v.MicrovoltValues[0] + v.MicrovoltValues[2] - v.MicrovoltValues[1]) <= 1), "limb identity");
            if (shape == DigitalisTShape.LowT)
                Check.That(a[110].MicrovoltValues[1] is >= 17 and <= 18, "low T stays nonzero at one-quarter terminal peak");
            else
            {
                Check.That(a[94].MicrovoltValues[1] > a[110].MicrovoltValues[1] && a[110].MicrovoltValues[1] < -69 && a[110].MicrovoltValues[3] > 0, "distinct negative terminal T after ST recovery with opposite aVR");
                Check.That(a.Skip(60).Take(60).All(v => v.MicrovoltValues[1] <= 0), "ST is never inverted into elevation");
            }
            var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, DigitalisEffectReference.CreateLeadIIBands(shape)).GenerateBefore(1_000_000_000, 250, 100);
            Check.That(a.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared monitorII");
        }
        try { DigitalisEffectReference.CreateElectrodes((DigitalisTShape)99); }
        catch (EventWaveformException e) when (e.ReasonCode == "Digitalis.InvalidShape") { return; }
        throw new InvalidOperationException("Invalid digitalis shape accepted");
    }
    private static void DigitalisJoinsRDescentToStWithoutRebound()
    {
        foreach (var shape in Enum.GetValues<DigitalisTShape>())
        {
            var samples = ElectrodeSignalGenerator.Start(DigitalisEffectReference.CreatePlan(), "AcqECGMonitor250@1", 1,
                DigitalisEffectReference.CreateElectrodes(shape)).GenerateBefore(1_000_000_000, 250, 100);
            var reference = ElectrodeSignalGenerator.Start(DigitalisEffectReference.CreatePlan(), "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(timing: DigitalisEffectReference.Timing)).GenerateBefore(1_000_000_000, 250, 100);
            foreach (int lead in new[] { 6, 7, 8 })
            {
                Check.That(samples.Take(55).Zip(reference).All(p => Math.Abs(p.First.MicrovoltValues[lead] - p.Second.MicrovoltValues[lead]) <= 1), "anterior rS descent retained before ST begins");
                int gain = lead switch { 6 => 300, 7 => 600, _ => 1000 };
                Check.That(Math.Abs(samples[60].MicrovoltValues[lead] + 50 * gain / 1000) <= 1 &&
                    Math.Abs(samples[78].MicrovoltValues[lead] + 180 * gain / 1000) <= 1, "anterior J and ST trough retained");
            }
            foreach (int lead in new[] { 0, 1, 9, 10, 11 })
            {
                short[] descent = samples.Where(s => s.Tick.SimTimeNs is >= 200_000_000 and <= 308_000_000).Select(s => s.MicrovoltValues[lead]).ToArray();
                Check.That(descent.Zip(descent.Skip(1)).All(p => p.Second <= p.First + 1), "R descent must join the ST trough without S recovery and a second downswing");
            }
        }
    }
}
