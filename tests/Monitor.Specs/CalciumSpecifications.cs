// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CalciumSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(CalciumChangesStAndQtWithoutMovingDepolarization), CalciumChangesStAndQtWithoutMovingDepolarization),
        new(nameof(CalciumVariantsPreserveDepolarizationAndExplicitRepolarization), CalciumVariantsPreserveDepolarizationAndExplicitRepolarization),
    ];
    private static void CalciumChangesStAndQtWithoutMovingDepolarization()
    {
        var plan = CalciumRepolarizationReference.CreatePlan();
        var ordinary = CalciumRepolarizationReference.CreateElectrodes(CalciumIllustration.Reference);
        foreach (var mode in new[] { CalciumIllustration.High, CalciumIllustration.Low })
        {
            var timing = CalciumRepolarizationReference.Timing(mode);
            var electrodes = CalciumRepolarizationReference.CreateElectrodes(mode);
            long st = timing.QtIntervalNs - timing.QrsDurationNs - timing.TDurationNs;
            Check.That(st == (mode == CalciumIllustration.High ? 40_000_000 : 260_000_000), "ST duration, not QRS stretching, explains QT change");
            for (int i = 0; i < 10; i++)
                for (int j = 0; j < 3; j++)
                {
                    var a = ordinary[i].Bands[j]; var b = electrodes[i].Bands[j];
                    Check.That(a.TableQ32.SequenceEqual(b.TableQ32), "amplitude and authored morphology retained");
                    if (j < 2) { Check.That(a.DelayNs == b.DelayNs && a.DurationNs == b.DurationNs, "P/QRS timing exact"); }
                }
            var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(1_000_000_000, 250, 100);
            long onset = timing.PrIntervalNs + timing.QtIntervalNs - timing.TDurationNs;
            long end = timing.PrIntervalNs + timing.QtIntervalNs;
            Check.That(samples.Where(s => s.Tick.SimTimeNs >= 240_000_000 && s.Tick.SimTimeNs < onset).All(s => s.MicrovoltValues.All(v => v == 0)), "explicit isoelectric ST interval");
            Check.That(samples.Where(s => s.Tick.SimTimeNs >= onset && s.Tick.SimTimeNs < end).Max(s => s.MicrovoltValues[1]) > 100, "visible upright T within expected support");
            Check.That(samples.Where(s => s.Tick.SimTimeNs >= end).All(s => s.MicrovoltValues.All(v => v == 0)), "QT endpoint preserved without late U");
            Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
            var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, CalciumRepolarizationReference.CreateLeadIIBands(mode)).GenerateBefore(1_000_000_000, 250, 100);
            Check.That(samples.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII parity");
        }
    }
    private static void CalciumVariantsPreserveDepolarizationAndExplicitRepolarization()
    {
        var plan = CalciumRepolarizationReference.CreatePlan();
        foreach (var mode in new[] { CalciumIllustration.HighAbsentSt, CalciumIllustration.LowFlatT, CalciumIllustration.LowInvertedT })
        {
            var source = CalciumRepolarizationReference.CreateElectrodes(mode);
            var baseline = CalciumRepolarizationReference.CreateElectrodes(mode == CalciumIllustration.HighAbsentSt ? CalciumIllustration.High : CalciumIllustration.Low);
            for (int electrode = 0; electrode < 10; electrode++)
                for (int band = 0; band < 2; band++)
                {
                    var a = source[electrode].Bands[band]; var b = baseline[electrode].Bands[band];
                    Check.That(a.DelayNs == b.DelayNs && a.DurationNs == b.DurationNs && a.TableQ32.SequenceEqual(b.TableQ32), "P/QRS exact");
                }
            var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, source).GenerateBefore(1_000_000_000, 250, 100);
            if (mode == CalciumIllustration.HighAbsentSt)
            {
                Check.That(CalciumRepolarizationReference.Timing(mode).StDurationNs == 0, "zero ST structurally accepted");
                Check.That(source.All(e => e.Bands[2].DelayNs == e.Bands[1].DurationNs), "T directly follows QRS, no negative overlap");
                Check.That(samples[62].MicrovoltValues[1] > 0 && samples.Skip(105).All(s => s.MicrovoltValues.All(v => v == 0)), "T begins after J and ends at420ms");
            }
            else
            {
                var original = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, baseline).GenerateBefore(1_000_000_000, 250, 100);
                for (int i = 0; i < samples.Count; i++)
                    for (int lead = 0; lead < 12; lead++)
                    {
                        long a = samples[i].MicrovoltValues[lead], b = original[i].MicrovoltValues[lead];
                        Check.That(i < 125 || i >= 155 ? a == b : mode == CalciumIllustration.LowFlatT ? Math.Abs(a * 4 - b) <= 4 : a == -b, "only T amplitude/polarity changes; timing fixed");
                    }
                long peak = samples.Skip(125).Take(30).Max(s => Math.Abs(s.MicrovoltValues[1]));
                Check.That(peak > 20 && (mode != CalciumIllustration.LowFlatT || peak < 100), "low T is reduced but not zero");
            }
            var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, CalciumRepolarizationReference.CreateLeadIIBands(mode)).GenerateBefore(1_000_000_000, 250, 100);
            Check.That(samples.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared monitorII");
            Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
        }
        try { CalciumRepolarizationReference.CreateElectrodes((CalciumIllustration)99); }
        catch (EventWaveformException e) when (e.ReasonCode == "Calcium.InvalidMode") { return; }
        throw new InvalidOperationException("Invalid calcium mode accepted");
    }

}
