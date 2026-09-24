// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class BidirectionalVtSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(BidirectionalVtAlternatesVentricularVectorWithoutInvertingP), BidirectionalVtAlternatesVentricularVectorWithoutInvertingP),
        new(nameof(BidirectionalVtPreservesProjectionAndRestoresOddBeats), BidirectionalVtPreservesProjectionAndRestoresOddBeats),
    ];
    private static void BidirectionalVtAlternatesVentricularVectorWithoutInvertingP()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 100);
        var electrodes = BidirectionalVtReference.CreateElectrodes();
        var ordinary = VentricularTachycardiaReference.CreateElectrodes();
        bool nonzero = false;
        for (int i = 0; i < electrodes.Count; i++)
        {
            var p = electrodes[i].Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical);
            Check.That(p.DurationNs == ordinary[i].Bands[0].DurationNs && p.TableQ32.SequenceEqual(ordinary[i].Bands[0].TableQ32) && p.VentricularCycles is null, "independent P is neither duplicated nor inverted");
            var v = EventWaveformComposition.Restore(new(electrodes[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).ToArray(), events));
            var baseline = EventWaveformComposition.Restore(new(ordinary[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).ToArray(), events));
            for (int beat = 0; beat < 7; beat++)
                for (long phase = 0; phase < 375_000_000; phase += 4_000_000)
                {
                    long t = 120_000_000 + beat * 375_000_000L + phase;
                    long value = v.EvaluateAt(t);
                    Check.That(value == (beat % 2 == 0 ? baseline.EvaluateAt(t) : -baseline.EvaluateAt(t)), "alternating QRS and secondary T retain full support and magnitude");
                    Check.That(value == -v.EvaluateAt(t + 375_000_000), "adjacent ventricular components have opposite direction");
                    nonzero |= value != 0;
                }
        }
        Check.That(nonzero, "alternation is not an empty signal");
        foreach (bool capture in new[] { false, true })
        {
            try { VentricularTachycardiaReference.CreateElectrodes(fusion: !capture, capture: capture, bidirectional: true); }
            catch (EventWaveformException e) when (e.ReasonCode == "Vt.ConflictingModes") { continue; }
            throw new InvalidOperationException("Mixed bidirectional and capture/fusion model accepted.");
        }
    }
    private static void BidirectionalVtPreservesProjectionAndRestoresOddBeats()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, BidirectionalVtReference.CreateElectrodes()).GenerateBefore(3_000_000_000, 750, 200);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateLeadIIBands(bidirectional: true)).GenerateBefore(3_000_000_000, 750, 200);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII uses same alternating source");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "alternation preserves limb identities");
        foreach (long boundary in new long[] { 494_000_000, 495_000_000, 550_000_000, 675_000_000, 770_000_000, 1_245_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, BidirectionalVtReference.CreateElectrodes());
            source.GenerateBefore(boundary, 750, 200);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(3_000_000_000, 750, 200); var b = restored.GenerateBefore(3_000_000_000, 750, 200);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restoring odd QRS/T cannot restart alternation parity");
        }
    }
}
