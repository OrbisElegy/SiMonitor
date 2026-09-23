// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class BundleBlockSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(StandaloneBundleBlocksKeepEveryBeatAndSharedLeadII), StandaloneBundleBlocksKeepEveryBeatAndSharedLeadII),
        new(nameof(LeftAnteriorFascicularHasRegionalQrRsAndLeftAxis), LeftAnteriorFascicularHasRegionalQrRsAndLeftAxis),
        new(nameof(IncompleteLeftBundleKeepsLeftSidedMorphologyWithin110Ms), IncompleteLeftBundleKeepsLeftSidedMorphologyWithin110Ms),
        new(nameof(IncompleteRightBundleHasShorterRsRPrimeWithoutChangingQt), IncompleteRightBundleHasShorterRsRPrimeWithoutChangingQt),
    ];

    private static void StandaloneBundleBlocksKeepEveryBeatAndSharedLeadII()
    {
        foreach (var mode in new[] { EcgBundleBlockIllustration.CompleteRight, EcgBundleBlockIllustration.IncompleteRight, EcgBundleBlockIllustration.CompleteLeft, EcgBundleBlockIllustration.IncompleteLeft, EcgBundleBlockIllustration.LeftAnteriorFascicular })
        {
            var plan = BundleBlockReference.CreatePlan(mode);
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
            var ventricular = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            Check.That(ventricular.Select(e => e.SimTimeNs).SequenceEqual(Enumerable.Range(0, 8).Select(i => i * 800_000_000L + 160_000_000)), "1:1 including previously dropped fourth/eighth slots");
            Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs)
                .SequenceEqual(ventricular.Select(e => e.SimTimeNs + 80_000_000)), "mechanical timing remains independent of QRS width");
            var electrodes = BundleBlockReference.CreateElectrodes(mode);
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            var samples = source.GenerateBefore(6_400_000_000, 1600, 100);
            var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, BundleBlockReference.CreateLeadIIBands(mode)).GenerateBefore(6_400_000_000, 1600, 100);
            Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II and projected II share all beats including secondary ST/T");
            Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "Einthoven identity retained");
            Check.That(samples.Skip(600).Take(200).Zip(samples.Take(200)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "fourth beat is present and identical to first");
            source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            source.GenerateBefore(232_000_000, 58, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(3_200_000_000, 742, 100).Zip(restored.GenerateBefore(3_200_000_000, 742, 100))
                .All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore within QRS preserves full fourth beat");
            var reference = BundleBlockReference.CreateElectrodes(mode is EcgBundleBlockIllustration.CompleteLeft or EcgBundleBlockIllustration.IncompleteLeft ? EcgBundleBlockIllustration.CompleteLeft : EcgBundleBlockIllustration.CompleteRight);
            Check.That(electrodes.Zip(reference).All(p => p.First.Bands[0].TableQ32.SequenceEqual(p.Second.Bands[0].TableQ32)), "P unchanged");
            if (mode is EcgBundleBlockIllustration.CompleteRight or EcgBundleBlockIllustration.CompleteLeft)
            {
                var old = mode == EcgBundleBlockIllustration.CompleteRight ? RightBundleBlockReference.CreateElectrodes() : LeftBundleBlockReference.CreateElectrodes();
                var oldPlan = mode == EcgBundleBlockIllustration.CompleteRight ? RightBundleBlockReference.CreatePlan() : LeftBundleBlockReference.CreatePlan();
                var oldBeat = ElectrodeSignalGenerator.Start(oldPlan, "AcqECGMonitor250@1", 1, old).GenerateBefore(800_000_000, 200, 100);
                Check.That(samples.Take(200).Zip(oldBeat).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "existing complete BBB morphology byte stable");
            }
        }
        foreach (var invalid in new[] { EcgBundleBlockIllustration.Reference, (EcgBundleBlockIllustration)(-1), (EcgBundleBlockIllustration)99 })
        {
            try { BundleBlockReference.CreateElectrodes(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "EcgBundleBlock.InvalidMode") { continue; }
            throw new InvalidOperationException("Unknown bundle illustration accepted.");
        }
    }

    private static void LeftAnteriorFascicularHasRegionalQrRsAndLeftAxis()
    {
        const EcgBundleBlockIllustration mode = EcgBundleBlockIllustration.LeftAnteriorFascicular;
        var timing = BundleBlockReference.Timing(mode);
        Check.That(timing.QrsDurationNs == 100_000_000 && timing.QtIntervalNs == 400_000_000, "mildly prolonged100ms QRS with QT400ms");
        var plan = BundleBlockReference.CreatePlan(mode);
        var electrodes = BundleBlockReference.CreateElectrodes(mode);
        var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        var reference = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(timing: timing)).GenerateBefore(800_000_000, 200, 100);
        foreach (var lead in new[] { EcgLead.I, EcgLead.AVL })
        {
            Check.That(a[43].MicrovoltValues[(int)lead] < -50 && a[55].MicrovoltValues[(int)lead] > 800, "lateral qR");
        }
        foreach (var lead in new[] { EcgLead.II, EcgLead.III, EcgLead.AVF })
        {
            Check.That(a[43].MicrovoltValues[(int)lead] > 40 && a[55].MicrovoltValues[(int)lead] < -600, "inferior rS");
        }
        var qrs = a.Where(s => s.Tick.SimTimeNs is >= 160_000_000 and < 260_000_000).ToArray();
        var peak = qrs.MaxBy(s => s.MicrovoltValues[(int)EcgLead.AVL])!;
        Check.That(peak.Tick.SimTimeNs - 160_000_000 >= 45_000_000, "aVL delayed R peak");
        long i = qrs.Sum(s => (long)s.MicrovoltValues[0]), ii = qrs.Sum(s => (long)s.MicrovoltValues[1]);
        // y=(2II-I)/sqrt(3). This integer inequality verifies -90<axis<=-45.
        Check.That(i > 0 && ii < 0 && (Int128)(i - 2 * ii) * (i - 2 * ii) >= 3 * (Int128)i * i, "integrated QRS left-superior axis");
        Check.That(a.Zip(reference).All(p => Enumerable.Range(6, 6).All(l => Math.Abs(p.First.MicrovoltValues[l] - p.Second.MicrovoltValues[l]) <= 1)), "reference chest contours preserved through Wilson rebase");
        Check.That(a.Zip(reference).Where(p => p.First.Tick.SimTimeNs < 160_000_000 || p.First.Tick.SimTimeNs >= 260_000_000).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "P/ST/T unchanged");
        foreach (long boundary in new[] { 158_000_000L, 218_000_000, 258_000_000, 260_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            source.GenerateBefore(boundary, 200, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var first = source.GenerateBefore(1_600_000_000, 400, 100);
            var second = restored.GenerateBefore(1_600_000_000, 400, 100);
            Check.That(first.Count == second.Count && first.Zip(second).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "fascicular onset/peak/end recovery exact");
        }
    }

    private static void IncompleteLeftBundleKeepsLeftSidedMorphologyWithin110Ms()
    {
        const EcgBundleBlockIllustration mode = EcgBundleBlockIllustration.IncompleteLeft;
        var timing = BundleBlockReference.Timing(mode);
        Check.That(timing.QrsDurationNs == 110_000_000 && timing.QtIntervalNs == 420_000_000 && timing.StDurationNs == 130_000_000, "110ms QRS with recalculated ST support, QT unchanged");
        var electrodes = BundleBlockReference.CreateElectrodes(mode);
        Check.That(electrodes.All(e => e.Bands[1].DurationNs == 110_000_000), "shared 110ms ventricular support");
        var samples = ElectrodeSignalGenerator.Start(BundleBlockReference.CreatePlan(mode), "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        int V(int index, EcgLead lead) => samples[index].MicrovoltValues[(int)lead];
        foreach (var lead in new[] { EcgLead.I, EcgLead.V5, EcgLead.V6 })
        {
            Check.That(Enumerable.Range(40, 12).All(i => V(i, lead) >= 0), "lateral initial q absent");
            Check.That(V(51, lead) > V(55, lead) + 80 && V(59, lead) > V(55, lead) + 150, "lateral notched R within shorter QRS");
            Check.That(V(70, lead) < -40 && V(125, lead) < -100, "secondary ST/T opposite dominant lateral R");
        }
        foreach (var lead in new[] { EcgLead.V1, EcgLead.V2 })
        {
            Check.That(lead == EcgLead.V1 ? Enumerable.Range(41, 25).All(i => V(i, lead) < 0) :
                V(42, lead) > 20 && Enumerable.Range(45, 21).All(i => V(i, lead) < 0), "V1 QS and V2 small-r deep-S retained");
            Check.That(V(70, lead) > 40 && V(125, lead) > 100, "secondary anterior ST/T upright");
        }
        Check.That(samples.Skip(145).All(s => s.MicrovoltValues.All(v => v == 0)), "QT420 ends at580ms without residual ventricular signal");
        foreach (long boundary in new[] { 268_000_000L, 270_000_000, 272_000_000, 400_000_000, 578_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(BundleBlockReference.CreatePlan(mode), "AcqECGMonitor250@1", 1, electrodes);
            source.GenerateBefore(boundary, 200, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(1_600_000_000, 400, 100);
            var b = restored.GenerateBefore(1_600_000_000, 400, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "QRS/ST/T boundary recovery exact");
        }
    }

    private static void IncompleteRightBundleHasShorterRsRPrimeWithoutChangingQt()
    {
        const EcgBundleBlockIllustration mode = EcgBundleBlockIllustration.IncompleteRight;
        var timing = BundleBlockReference.Timing(mode);
        var electrodes = BundleBlockReference.CreateElectrodes(mode);
        Check.That(timing.QrsDurationNs == 110_000_000 && timing.StDurationNs == 110_000_000 && timing.QtIntervalNs == 400_000_000, "110ms QRS, ST occupies remaining QT support");
        Check.That(electrodes.All(e => e.Bands[1].DurationNs == 110_000_000), "all electrodes use same shorter QRS");
        var samples = ElectrodeSignalGenerator.Start(BundleBlockReference.CreatePlan(mode), "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        int V(int index, EcgLead lead) => samples[index].MicrovoltValues[(int)lead];
        foreach (var lead in new[] { EcgLead.V1, EcgLead.V2 })
        {
            Check.That(V(44, lead) > 100 && V(49, lead) < -300 && V(57, lead) > 800, "incomplete rsR-prime retains early r/S and dominant late R-prime");
            Check.That(V(70, lead) < -40 && V(120, lead) < -150, "post-QRS ST and T negative without QRS tail");
        }
        foreach (var lead in new[] { EcgLead.I, EcgLead.V5, EcgLead.V6 })
        {
            Check.That(V(45, lead) > 700 && Enumerable.Range(51, 15).All(i => V(i, lead) < -20), "lateral R and delayed terminal S preserved");
            Check.That(V(120, lead) > 100, "lateral T upright");
        }
        Check.That(samples.Skip(140).All(s => s.MicrovoltValues.All(v => v == 0)), "QT ends at560ms without residual ventricular signal");
    }
}
