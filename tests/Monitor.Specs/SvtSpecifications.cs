// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SvtSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SvtHasOverlappingRetrogradePAndRegularNarrowQrs), SvtHasOverlappingRetrogradePAndRegularNarrowQrs),
        new(nameof(SvtRestoresAcrossBeatsAndMatchesMonitorII), SvtRestoresAcrossBeatsAndMatchesMonitorII),
    ];
    private static void SvtHasOverlappingRetrogradePAndRegularNarrowQrs()
    {
        var plan = SupraventricularTachycardiaReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_200_000_000, 40);
        foreach (var kind in new[] { PhysiologyCycleEventKind.AtrialElectrical, PhysiologyCycleEventKind.VentricularElectrical })
            Check.That(events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 0, 300_000_000, 600_000_000, 900_000_000 }), "synchronous regular200bpm electrical grid");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 80_000_000, 380_000_000, 680_000_000, 980_000_000 }), "shared authored mechanical delay");
        foreach (var invalid in new[] { plan with { HeartPeriodNs = 800_000_000 }, plan with { VentricularElectricalOffsetNs = 40_000_000 }, plan with { ConductionPattern = AvConductionPattern.FixedPr }, plan with { CardiacActivity = CardiacActivity.VentricularOnly } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException e) when (e.ReasonCode == "PhysiologyTimeline.InvalidState") { continue; }
            throw new InvalidOperationException("Invalid SVT grid accepted.");
        }
        var electrodes = SupraventricularTachycardiaReference.CreateElectrodes();
        var reference = TextbookElectrodeReference.CreateElectrodes(timing: SupraventricularTachycardiaReference.Timing);
        for (int index = 0; index < 10; index++)
        {
            Check.That(electrodes[index].Bands[1].DurationNs == 80_000_000 && electrodes[index].Bands[1].TableQ32.SequenceEqual(reference[index].Bands[1].TableQ32), "ordinary80ms QRS without delta");
            Check.That(electrodes[index].Bands[0].DurationNs == 40_000_000 && electrodes[index].Bands[0].TableQ32.Zip(reference[index].Bands[0].TableQ32).All(p => Math.Abs(2 * p.First + p.Second) <= 1), "shortened inverted atrial vector");
        }
        var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(1_200_000_000, 300, 100);
        Check.That(a.Take(75).Zip(a.Skip(75)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "identical regular cycles");
        for (int beat = 0; beat < 4; beat++)
            Check.That(a.Skip(beat * 75 + 60).Take(15).All(s => s.MicrovoltValues.All(v => v == 0)), "no leftover sinusP/delta between QT end and nextQRS");
        var pBands = SupraventricularTachycardiaReference.CreateLeadIIBands().Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
        var p = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, pBands).GenerateBefore(300_000_000, 75, 100);
        Check.That(p.Take(10).Any(s => s.NormalizedValue < -30) && p.Skip(10).All(s => s.NormalizedValue == 0), "negative leadII P-prime occurs entirely inside QRS");
        Check.That(a.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity preserved");
    }
    private static void SvtRestoresAcrossBeatsAndMatchesMonitorII()
    {
        var plan = SupraventricularTachycardiaReference.CreatePlan();
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateElectrodes()).GenerateBefore(1_200_000_000, 300, 100);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateLeadIIBands()).GenerateBefore(1_200_000_000, 300, 100);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "SVT shared monitorII parity");
        foreach (long boundary in new[] { 0L, 38_000_000, 78_000_000, 238_000_000, 298_000_000, 300_000_000, 602_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateElectrodes());
            source.GenerateBefore(boundary, 300, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(1_200_000_000, 300, 100); var b = restored.GenerateBefore(1_200_000_000, 300, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "SVT restore across overlap and beat boundaries");
        }
    }
}
