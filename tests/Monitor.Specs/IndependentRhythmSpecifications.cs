// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class IndependentRhythmSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(IndependentRhythmsKeepAuthoredClocksAndRejectConflicts), IndependentRhythmsKeepAuthoredClocksAndRejectConflicts),
        new(nameof(IndependentRhythmsPreserveContourProjectionAndRecovery), IndependentRhythmsPreserveContourProjectionAndRecovery),
    ];

    private sealed record RhythmCase(string Name, RegularPhysiologyPlan Plan, long PeriodNs, int BeatCount,
        long QrsDurationNs, long QtIntervalNs, IReadOnlyList<ElectrodeWaveformPlan> Electrodes,
        IReadOnlyList<ElectrodeWaveformPlan> Reference, IReadOnlyList<EventWaveformBand> LeadII);

    private static RhythmCase[] Cases =>
    [
        new("VT", VentricularTachycardiaReference.CreatePlan(), 375_000_000, 8, 160_000_000, 275_000_000,
            VentricularTachycardiaReference.CreateElectrodes(), CompleteAvBlockVentricularReference.CreateElectrodes(), VentricularTachycardiaReference.CreateLeadIIBands()),
        new("AIVR", AcceleratedVentricularReference.CreatePlan(), 750_000_000, 4, 160_000_000, 400_000_000,
            AcceleratedVentricularReference.CreateElectrodes(), CompleteAvBlockVentricularReference.CreateElectrodes(), AcceleratedVentricularReference.CreateLeadIIBands()),
        new("AJR", AcceleratedJunctionalReference.CreatePlan(), 600_000_000, 5, 80_000_000, 400_000_000,
            AcceleratedJunctionalReference.CreateElectrodes(), TextbookElectrodeReference.CreateElectrodes(timing: AcceleratedJunctionalReference.Timing), AcceleratedJunctionalReference.CreateLeadIIBands()),
    ];

    private static void IndependentRhythmsKeepAuthoredClocksAndRejectConflicts()
    {
        foreach (var item in Cases)
        {
            var plan = item.Plan;
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 100);
            Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical).Select(e => e.SimTimeNs)
                .SequenceEqual(new long[] { 0, 800_000_000, 1_600_000_000, 2_400_000_000 }), "independent75bpm atria");
            foreach (var kind in new[] { PhysiologyCycleEventKind.VentricularElectrical, PhysiologyCycleEventKind.VentricularMechanical })
            {
                long offset = kind == PhysiologyCycleEventKind.VentricularElectrical ? 120_000_000 : 200_000_000;
                Check.That(events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs)
                    .SequenceEqual(Enumerable.Range(0, item.BeatCount).Select(i => offset + i * item.PeriodNs)), $"{item.Name}: independent ventricular grid");
            }
            foreach (var invalid in new[] { plan with { HeartPeriodNs = item.PeriodNs }, plan with { IndependentVentricularPeriodNs = 400_000_000 },
                plan with { ConductionPattern = AvConductionPattern.FixedPr }, plan with { ConductionPattern = AvConductionPattern.CompleteAvBlockVentricularIllustration },
                plan with { ConductionPattern = AvConductionPattern.CompleteAvBlockJunctionalIllustration }, plan with { VentricularConductionRatio = 2 },
                plan with { CardiacActivity = CardiacActivity.VentricularOnly }, plan with { VentricularElectricalOffsetNs = 0 } })
            {
                try { RegularPhysiologyTimeline.Start(invalid); }
                catch (PhysiologyTimelineException e) when (e.ReasonCode == "PhysiologyTimeline.InvalidState") { continue; }
                throw new InvalidOperationException($"{item.Name}: invalid grid accepted.");
            }
            if (item.Name != "VT")
            {
                bool rejected = false;
                try { RegularPhysiologyTimeline.Start(plan with { ConductionPattern = AvConductionPattern.MonomorphicVtIllustration }); }
                catch (PhysiologyTimelineException e) when (e.ReasonCode == "PhysiologyTimeline.InvalidState") { rejected = true; }
                Check.That(rejected, $"{item.Name}: rejects VT mode with accelerated rhythm timing");
            }
            var timeline = RegularPhysiologyTimeline.Start(plan);
            timeline.AdvanceBefore(800_000_000, 100);
            var before = timeline.CaptureState();
            try { timeline.AdvanceBefore(3_000_000_000, 1); throw new InvalidOperationException("Event limit accepted."); }
            catch (PhysiologyTimelineException) { }
            Check.That(timeline.CaptureState() == before, "event budget rejection is atomic");
            Check.That(timeline.AdvanceBefore(3_000_000_000, 100).SequenceEqual(RegularPhysiologyTimeline.Restore(before).AdvanceBefore(3_000_000_000, 100)), "timeline recovery preserves both clocks");
        }
    }
    private static void IndependentRhythmsPreserveContourProjectionAndRecovery()
    {
        foreach (var item in Cases)
        {
            var plan = item.Plan;
            var electrodes = item.Electrodes;
            var escape = item.Reference;
            for (int i = 0; i < electrodes.Count; i++)
            {
                Check.That(electrodes[i].Bands[0].TableQ32.SequenceEqual(escape[i].Bands[0].TableQ32), "normal atrial morphology retained");
                Check.That(electrodes[i].Bands[1].DurationNs == item.QrsDurationNs && electrodes[i].Bands[1].TableQ32.SequenceEqual(escape[i].Bands[1].TableQ32), $"{item.Name}: authored ventricular contour");
                Check.That(electrodes[i].Bands[2].DelayNs + electrodes[i].Bands[2].DurationNs == item.QtIntervalNs, "QT support ends before next ventricular beat");
            }
            var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(3_000_000_000, 750, 200);
            var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, item.LeadII).GenerateBefore(3_000_000_000, 750, 200);
            Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared monitorII projection");
            Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
            var pBands = item.LeadII.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray();
            var p = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, pBands).GenerateBefore(3_000_000_000, 750, 200);
            Check.That(p.Where((_, i) => i % 200 >= 25).All(s => s.NormalizedValue == 0) && p.Take(25).Any(s => s.NormalizedValue > 50), "P follows atrial clock, not each QRS");
            // Atrial phase advances relative to the independently authored ventricular period.
            Check.That(new long[] { 800_000_000, 1_600_000_000, 2_400_000_000 }.Select(t => (t - 120_000_000) % item.PeriodNs).Distinct().Count() == 3, "no fixed PR");
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            List<ElectrodeSignalSample> recovered = [];
            foreach (long boundary in new long[] { 0, 118_000_000, 278_000_000, 394_000_000, 494_000_000, 798_000_000, 870_000_000, 1_600_000_000, 3_000_000_000 })
            {
                recovered.AddRange(source.GenerateBefore(boundary, 750, 200));
                if (boundary < 3_000_000_000) { source = ElectrodeSignalGenerator.Restore(source.CaptureState()); }
            }
            Check.That(recovered.Count == full.Count && recovered.Zip(full).All(pair => pair.First.Tick == pair.Second.Tick &&
                pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)), $"{item.Name}: restore across P/QRS overlap and independent boundaries");
        }
    }
}
