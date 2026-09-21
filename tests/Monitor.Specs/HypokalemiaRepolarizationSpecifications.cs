// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class HypokalemiaRepolarizationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(HypokalemiaPreservesQtAndSeparatesProminentU), HypokalemiaPreservesQtAndSeparatesProminentU),
        new(nameof(HypokalemiaInvertsOnlyIntrinsicTWithIndependentU), HypokalemiaInvertsOnlyIntrinsicTWithIndependentU),
        new(nameof(HypokalemiaRestoresAcrossTAndU), HypokalemiaRestoresAcrossTAndU),
        new(nameof(HypokalemiaFusionOverlapsOnlyUAndPreservesEndpoints), HypokalemiaFusionOverlapsOnlyUAndPreservesEndpoints),
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
    private static void HypokalemiaFusionOverlapsOnlyUAndPreservesEndpoints()
    {
        var plan = HypokalemiaRepolarizationReference.CreatePlan();
        var separate = HypokalemiaRepolarizationReference.CreateElectrodes();
        var fused = HypokalemiaRepolarizationReference.CreateElectrodes(true);
        for (int i = 0; i < 10; i++)
        {
            Check.That(separate[i].Bands.Count == fused[i].Bands.Count, "fusion preserves component count");
            for (int j = 0; j < separate[i].Bands.Count - 1; j++)
            {
                var a = separate[i].Bands[j]; var b = fused[i].Bands[j];
                Check.That(a.DelayNs == b.DelayNs && a.DurationNs == b.DurationNs && a.TableQ32.SequenceEqual(b.TableQ32), "P/QRS/ST/intrinsicT unchanged");
            }
            var u = fused[i].Bands[^1];
            Check.That(u.DelayNs == 300_000_000 && u.DurationNs == 350_000_000 && u.DelayNs + u.DurationNs == HypokalemiaRepolarizationReference.QuIntervalNs, "U overlap keeps QU endpoint outside latentQT");
        }
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, fused).GenerateBefore(1_000_000_000, 250, 100);
        var baseline = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, separate).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Take(115).Zip(baseline).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "no changes before U starts460ms");
        foreach (int lead in new[] { 1, 7, 8, 9 })
        {
            Check.That(samples.Where(s => s.Tick.SimTimeNs is >= 520_000_000 and < 600_000_000).All(s => s.MicrovoltValues[lead] > 20), "fusion fills former T/U baseline gap with continuous positive signal");
            Check.That(samples.Where(s => s.Tick.SimTimeNs >= 812_000_000).All(s => s.MicrovoltValues[lead] == 0), "fused U ends before next beat");
        }
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, HypokalemiaRepolarizationReference.CreateLeadIIBands(true)).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII shares fusedT/U");
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities survive overlap");
        try { new EcgUWavePlan(-100_000_000, 350_000_000, new long[10]).Validate(HypokalemiaRepolarizationReference.Timing); }
        catch (EventWaveformException e) when (e.ReasonCode == "EcgUWave.InvalidPlan") { return; }
        throw new InvalidOperationException("General U API unexpectedly permits overlap");
    }
    private static void HypokalemiaInvertsOnlyIntrinsicTWithIndependentU()
    {
        foreach (bool fuse in new[] { false, true })
        {
            var plan = HypokalemiaRepolarizationReference.CreatePlan();
            var ordinary = HypokalemiaRepolarizationReference.CreateElectrodes(fuse);
            var inverted = HypokalemiaRepolarizationReference.CreateElectrodes(fuse, true);
            for (int i = 0; i < 10; i++)
                for (int band = 0; band < ordinary[i].Bands.Count; band++)
                {
                    var a = ordinary[i].Bands[band]; var b = inverted[i].Bands[band];
                    Check.That(a.DelayNs == b.DelayNs && a.DurationNs == b.DurationNs && a.Trigger == b.Trigger, "all component timing retained");
                    Check.That(a.TableQ32.Zip(b.TableQ32).All(p => band == 2 ? p.First == -p.Second : p.First == p.Second), "only intrinsic T negated; U is independent");
                }
            var aSamples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ordinary).GenerateBefore(1_000_000_000, 250, 100);
            var bSamples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, inverted).GenerateBefore(1_000_000_000, 250, 100);
            var tOnly = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                ordinary.Select(e => e with { Bands = [e.Bands[2]] }).ToArray()).GenerateBefore(1_000_000_000, 250, 100);
            for (int i = 0; i < aSamples.Count; i++)
                for (int lead = 0; lead < 12; lead++)
                    Check.That(Math.Abs(aSamples[i].MicrovoltValues[lead] - bSamples[i].MicrovoltValues[lead] - 2 * tOnly[i].MicrovoltValues[lead]) <= 3, "isolated T explains composite difference including U overlap");
            Check.That(bSamples[110].MicrovoltValues[1] < -30 && bSamples[110].MicrovoltValues[7] < -30 && bSamples[110].MicrovoltValues[3] > 0, "projected negative T with opposite aVR");
            Check.That(bSamples.Where(s => s.Tick.SimTimeNs >= 560_000_000).Zip(aSamples.Where(s => s.Tick.SimTimeNs >= 560_000_000)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "post-T U unchanged");
            Check.That(bSamples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity preserved");
            var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, HypokalemiaRepolarizationReference.CreateLeadIIBands(fuse, true)).GenerateBefore(1_000_000_000, 250, 100);
            Check.That(bSamples.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared monitorII parity");
        }
    }
    private static void HypokalemiaRestoresAcrossTAndU()
    {
        foreach (bool fuse in new[] { false, true })
            foreach (bool invert in new[] { false, true })
                foreach (long boundary in new[] { 450_000_000L, 578_000_000, 650_000_000, 810_000_000 })
                {
                    var source = ElectrodeSignalGenerator.Start(HypokalemiaRepolarizationReference.CreatePlan(), "AcqECGMonitor250@1", 1, HypokalemiaRepolarizationReference.CreateElectrodes(fuse, invert));
                    source.GenerateBefore(boundary, 250, 100);
                    var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                    var a = source.GenerateBefore(2_000_000_000, 500, 100);
                    var b = restored.GenerateBefore(2_000_000_000, 500, 100);
                    Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "T/U gap, U and next cycle restore exactly");
                }
    }
}
