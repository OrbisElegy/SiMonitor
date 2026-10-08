// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class EcgTemplateDetectionSmokeChecks
{
    internal static void Register()
    {
        for (int index = 0; index < DesignPreviewSettings.EcgChoiceCount; index++)
        {
            int templateIndex = index;
            NativeSmokePartition.Run(() => Verify(templateIndex));
        }
    }

    private static void Verify(int templateIndex)
    {
        var configuration = DesignPreviewWindow.ResolveStyle(templateIndex, 0, 0).Physiology;
        var source = PhysiologyIllustrationSource.Create(configuration);
        var detector = new EcgHeartRateMeasurement(PhysiologyIllustrationSource.ChannelId(0));
        var beats = new List<DetectedEcgBeat>();
        var rhythmEvents = new List<DetectedEcgRhythmEvent>();
        var expectedStatus = configuration.CardiacActivity == CardiacActivity.Absent ? WaveformMeasurementStatus.Stale :
            configuration.CardiacActivity == CardiacActivity.AtrialOnly || configuration.HyperkalemiaFusion ||
            configuration.Pacing is PacingIllustration.VentricularNoncapture or PacingIllustration.VentricularOutputFailure ||
            VentricularDisorganizationReference.IsPattern(configuration.ConductionPattern)
                ? WaveformMeasurementStatus.Uncountable : WaveformMeasurementStatus.Valid;
        for (int step = 1; step <= 310; step++)
        {
            foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            {
                var block = WaveformEnvelopeCodec.Decode(wire);
                var checkpoint = detector.Capture();
                var detected = detector.Consume(wire, out var transitions);
                rhythmEvents.AddRange(transitions);
                beats.AddRange(detected);
                long lastSampleTimeNs = block.StartSimTimeNs + block.DurationNs - 4_000_000;
                var reading = detector.Read(lastSampleTimeNs);
                if (step % 17 == 0)
                {
                    var restored = EcgHeartRateMeasurement.Restore(checkpoint);
                    Require(detected.SequenceEqual(restored.Consume(wire, out var restoredTransitions)) && reading == restored.Read(lastSampleTimeNs) &&
                        transitions.SequenceEqual(restoredTransitions) && detector.ReadRhythm(lastSampleTimeNs) == restored.ReadRhythm(lastSampleTimeNs),
                        "partial candidate, rhythm evidence and event persistence restore exactly");
                }
                if (lastSampleTimeNs < 10_000_000_000) { continue; }
                Require(reading.Status == expectedStatus &&
                    (expectedStatus == WaveformMeasurementStatus.Valid ? reading.MilliBeatsPerMinute > 0 : reading.MilliBeatsPerMinute is null),
                    "template retains its measured validity: " + reading);
            }
        }
        var afEvents = rhythmEvents.Where(e => e.Kind == EcgRhythmEventKind.SuspectedAtrialFibrillation).ToArray();
        bool expectedAf = AtrialFibrillationReference.IsPattern(configuration.ConductionPattern);
        Require(expectedAf ? afEvents.Length == 1 && afEvents[0].Transition == EcgRhythmTransition.Started &&
            afEvents[0].ConfirmedAtNs < 45_000_000_000 : afEvents.Length == 0,
            "AF templates start once; all other catalog templates produce no AF events");
        if (expectedStatus != WaveformMeasurementStatus.Valid) { return; }
        long[] truth = RegularPhysiologyTimeline.Start(configuration.ResolvePlan()).AdvanceBefore(60_000_000_000, 5000)
            .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).ToArray();
        // A bounded electrical-response association for catalog regression,
        // not an automatic measurement of clinical QRS onset/offset.
        const long responseWindowNs = 200_000_000;
        foreach (long activationTimeNs in truth.Where(timeNs => timeNs >= 10_000_000_000 && timeNs < 59_000_000_000))
        {
            var matched = beats.Where(beat => beat.PeakTimeNs >= activationTimeNs &&
                beat.PeakTimeNs < activationTimeNs + responseWindowNs).ToArray();
            Require(matched.Length == 1, $"each electrical activation has exactly one detected response at {activationTimeNs}; nearby {string.Join(",", beats.Where(b => Math.Abs(b.PeakTimeNs - activationTimeNs) < 500_000_000).Select(b => b.PeakTimeNs))}");
            Require(matched[0].ConfirmedAtNs > matched[0].PeakTimeNs && matched[0].ConfirmedAtNs <= activationTimeNs + 300_000_000,
                "confirmation is causal and bounded");
        }
        Require(beats.Where(beat => beat.PeakTimeNs >= 10_000_000_000 && beat.PeakTimeNs < 59_000_000_000)
            .All(beat => truth.Any(timeNs => beat.PeakTimeNs >= timeNs && beat.PeakTimeNs < timeNs + responseWindowNs)),
            "no unmatched T-wave or secondary-lobe events compensate for missed responses");

        void Require(bool condition, string message)
        {
            if (!condition) { throw new InvalidOperationException($"ECG template {templateIndex}: {message}"); }
        }
    }
}
