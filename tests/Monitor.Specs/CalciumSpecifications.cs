// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CalciumSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(CalciumChangesStAndQtWithoutMovingDepolarization), CalciumChangesStAndQtWithoutMovingDepolarization),
        new(nameof(CalciumRecoveryAndInvalidModeAreDeterministic), CalciumRecoveryAndInvalidModeAreDeterministic),
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
    private static void CalciumRecoveryAndInvalidModeAreDeterministic()
    {
        foreach (var mode in new[] { CalciumIllustration.High, CalciumIllustration.Low })
            foreach (long boundary in new[] { 278_000_000L, 458_000_000, 498_000_000, 618_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(CalciumRepolarizationReference.CreatePlan(), "AcqECGMonitor250@1", 1, CalciumRepolarizationReference.CreateElectrodes(mode));
                source.GenerateBefore(boundary, 250, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                var a = source.GenerateBefore(2_000_000_000, 500, 100);
                var b = restored.GenerateBefore(2_000_000_000, 500, 100);
                Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore at ST/T boundaries");
            }
        try { CalciumRepolarizationReference.CreateElectrodes((CalciumIllustration)99); }
        catch (EventWaveformException e) when (e.ReasonCode == "Calcium.InvalidMode") { return; }
        throw new InvalidOperationException("Invalid calcium mode accepted");
    }
}
