// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class TwistingVtSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(TwistingVtUsesIndependentAxesAndFourBeatReversal), TwistingVtUsesIndependentAxesAndFourBeatReversal),
        new(nameof(TwistingVtFitsBandBudgetAndRestoresRotationPhase), TwistingVtFitsBandBudgetAndRestoresRotationPhase),
    ];
    private static void TwistingVtUsesIndependentAxesAndFourBeatReversal()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_000_000_000, 100);
        var electrodes = TwistingVtReference.CreateElectrodes();
        var ordinary = VentricularTachycardiaReference.CreateElectrodes();
        var vectors = electrodes.Select(e => EventWaveformComposition.Restore(new(e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).ToArray(), events))).ToArray();
        for (int i = 0; i < electrodes.Count; i++)
        {
            var p = electrodes[i].Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical);
            Check.That(p.TableQ32.SequenceEqual(ordinary[i].Bands[0].TableQ32) && p.DurationNs == 100_000_000 && p.VentricularCycles is null, "normal P source remains single and independent");
            for (int beat = 0; beat < 4; beat++)
                for (long phase = 0; phase < 375_000_000; phase += 4_000_000)
                {
                    long t = 120_000_000 + beat * 375_000_000L + phase;
                    Check.That(vectors[i].EvaluateAt(t) == -vectors[i].EvaluateAt(t + 1_500_000_000), "opposite vector after four beats");
                    Check.That(vectors[i].EvaluateAt(t) == vectors[i].EvaluateAt(t + 3_000_000_000), "eight-beat morphology cycle");
                }
        }
        // Measure limb projections from electrode sources without P contamination.
        List<long> signedPeaks = [];
        bool independent = false;
        for (int beat = 0; beat < 8; beat++)
        {
            long largest = 0, allLeadsPeak = 0;
            for (long phase = 0; phase < 160_000_000; phase += 2_000_000)
            {
                long t = 120_000_000 + beat * 375_000_000L + phase;
                long ii = vectors[3].EvaluateAt(t) - vectors[0].EvaluateAt(t);
                if (Math.Abs(ii) > Math.Abs(largest)) { largest = ii; }
                long wilson = (vectors[0].EvaluateAt(t) + vectors[1].EvaluateAt(t) + vectors[3].EvaluateAt(t)) / 3;
                for (int chest = 4; chest < 10; chest++)
                    allLeadsPeak = Math.Max(allLeadsPeak, Math.Abs(vectors[chest].EvaluateAt(t) - wilson));
                if (beat == 0)
                {
                    long a1 = vectors[1].EvaluateAt(t) - vectors[0].EvaluateAt(t), a2 = ii;
                    long b1 = vectors[1].EvaluateAt(t + 750_000_000) - vectors[0].EvaluateAt(t + 750_000_000);
                    long b2 = vectors[3].EvaluateAt(t + 750_000_000) - vectors[0].EvaluateAt(t + 750_000_000);
                    independent |= (Int128)a1 * b2 != (Int128)a2 * b1;
                }
            }
            Check.That(allLeadsPeak > 100 * Monitor.Simulation.Determinism.FixedPointMath.Q32One, "rotation never erases QRS from all chest leads");
            signedPeaks.Add(largest);
        }
        Check.That(independent && signedPeaks.Select(Math.Abs).Distinct().Count() >= 3, "independent vector basis changes projection amplitude, not global scalar sign");
        Check.That(signedPeaks.Take(4).Zip(signedPeaks.Skip(4)).All(p => p.First == -p.Second), "dominant main waves reverse over several beats");
        foreach (var modes in new[] { (true, false, false), (false, true, false), (false, false, true) })
        {
            try { VentricularTachycardiaReference.CreateElectrodes(modes.Item1, modes.Item2, modes.Item3, true); }
            catch (EventWaveformException e) when (e.ReasonCode == "Vt.ConflictingModes") { continue; }
            throw new InvalidOperationException("Undefined twisting combination accepted.");
        }
    }
    private static void TwistingVtFitsBandBudgetAndRestoresRotationPhase()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var bands = VentricularTachycardiaReference.CreateLeadIIBands(twisting: true);
        Check.That(bands.Count == 17 && bands.Count <= EventWaveformComposition.MaximumBandCount, "monitorII combines matching electrodes within band budget");
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TwistingVtReference.CreateElectrodes()).GenerateBefore(6_000_000_000, 1500, 200);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, bands).GenerateBefore(6_000_000_000, 1500, 200);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared monitorII parity across rotation");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities across all phases");
        foreach (long boundary in new long[] { 494_000_000, 880_000_000, 1_640_000_000, 2_400_000_000, 2_980_000_000, 3_120_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TwistingVtReference.CreateElectrodes());
            source.GenerateBefore(boundary, 1500, 200);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(6_000_000_000, 1500, 200); var b = restored.GenerateBefore(6_000_000_000, 1500, 200);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "checkpoint does not reset rotating beat ordinal");
        }
    }
}
