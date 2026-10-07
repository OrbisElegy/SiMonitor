// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EcgMonitoringSpecifications
{
    private const long StepNs = 4_000_000;
    private static readonly Guid Channel = PhysiologyIllustrationSource.ChannelId(0);
    public static Specification[] All =>
    [
        new(nameof(EcgPauseAndAsystoleUseLiveSamples), EcgPauseAndAsystoleUseLiveSamples),
        new(nameof(EcgVentricularPatternsRequireLearnedMorphology), EcgVentricularPatternsRequireLearnedMorphology),
        new(nameof(EcgVentricularRunsRespectRateAndLength), EcgVentricularRunsRespectRateAndLength),
        new(nameof(EcgMonitoringRestoresAndIgnoresPacketBoundaries), EcgMonitoringRestoresAndIgnoresPacketBoundaries),
        new(nameof(EcgMonitoringInterruptsAndRejectsAtomically), EcgMonitoringInterruptsAndRejectsAtomically),
        new(nameof(EcgRepolarizationRequiresSustainedMeasuredEvidence), EcgRepolarizationRequiresSustainedMeasuredEvidence),
        new(nameof(EcgQtCorrectionAndStPolarityUseMeasuredUnits), EcgQtCorrectionAndStPolarityUseMeasuredUnits),
        new(nameof(EcgMonitoringEventsCommitWithAllChannels), EcgMonitoringEventsCommitWithAllChannels),
        new(nameof(EcgFibrillationRequiresFrequencyAndPersistence), EcgFibrillationRequiresFrequencyAndPersistence),
        new(nameof(EcgMonitoringFlowsThroughLivePreview), EcgMonitoringFlowsThroughLivePreview),
        new(nameof(EcgAcquiredPresetsProduceVentricularEvidence), EcgAcquiredPresetsProduceVentricularEvidence),
        new(nameof(EcgPacingRequiresAcquiredPulseEvidence), EcgPacingRequiresAcquiredPulseEvidence),
        new(nameof(EcgRonTWaitsForItsCompensatoryPause), EcgRonTWaitsForItsCompensatoryPause),
        new(nameof(EcgRonTRequiresUnfinishedTAndConfirmation), EcgRonTRequiresUnfinishedTAndConfirmation),
        new(nameof(EcgRonTAcquiredPresetsDistinguishLongQtFromOrdinaryPvcs), EcgRonTAcquiredPresetsDistinguishLongQtFromOrdinaryPvcs),
        new(nameof(EcgRelearningAndPvcWindowExpireEvidence), EcgRelearningAndPvcWindowExpireEvidence),
        new(nameof(EcgMonitoringSettingsRejectInvalidCombinations), EcgMonitoringSettingsRejectInvalidCombinations),
        new(nameof(EcgRepolarizationRecoversAfterAbnormalStartup), EcgRepolarizationRecoversAfterAbnormalStartup),
        new(nameof(EcgRepolarizationRecoversAfterStableRateChange), EcgRepolarizationRecoversAfterStableRateChange),
        new(nameof(EcgReferenceRecoveryRequiresConsistentNarrowBeats), EcgReferenceRecoveryRequiresConsistentNarrowBeats),
        new(nameof(EcgReferenceRecoveryRestoresAndRejectsAtomically), EcgReferenceRecoveryRestoresAndRejectsAtomically),
        new(nameof(EcgReferenceRecoveryPreservesVentricularAndSvtDetection), EcgReferenceRecoveryPreservesVentricularAndSvtDetection),
    ];

    private static void EcgPauseAndAsystoleUseLiveSamples()
    {
        short[] samples = Normal(26);
        Array.Clear(samples, 20 * 250, 6 * 250);
        var result = Run(samples);
        foreach (var condition in new[] { EcgMonitoringConditions.MissedBeat, EcgMonitoringConditions.Pause, EcgMonitoringConditions.Asystole })
        {
            var onset = result.Events.Single(e => e.Condition == condition && e.Transition == EcgMonitoringTransition.Started);
            long lastPeak = result.Beats[^1].PeakTimeNs;
            long threshold = condition == EcgMonitoringConditions.Asystole ? 4_000_000_000 :
                condition == EcgMonitoringConditions.Pause ? 2_000_000_000 : 1_750_000_000;
            Check.That(onset.ConfirmedAtNs == ((lastPeak + threshold) / StepNs + 1) * StepNs, "strict threshold on acquired time: " + onset);
        }
        Check.That(result.Detector.ReadMonitoring(27_000_000_000).Status == WaveformMeasurementStatus.NoData &&
            result.Detector.ReadMonitoring(27_000_000_000).ActiveConditions == EcgMonitoringConditions.None,
            "transport silence cannot create or retain a live flatline verdict");
        var equal = Run(samples, settings: new() { PauseMilliseconds = 2500, AsystoleMilliseconds = 2500 });
        Check.That(equal.Events.All(e => e.Condition != EcgMonitoringConditions.Pause), "equal thresholds select asystole");
    }

    private static void EcgVentricularPatternsRequireLearnedMorphology()
    {
        foreach (var (pattern, expected) in new[]
        {
            (new[] { 0, 1, 1, 0 }, EcgMonitoringConditions.PairPvcs),
            (new[] { 0, 1, 0, 1, 0 }, EcgMonitoringConditions.VentricularBigeminy),
            (new[] { 0, 0, 1, 0, 0, 1, 0, 0 }, EcgMonitoringConditions.VentricularTrigeminy),
            (new[] { 0, 1, 0, -1, 0 }, EcgMonitoringConditions.MultiformPvcs)
        })
        {
            short[] samples = Normal(20).Concat(new short[pattern.Length * 250]).ToArray();
            for (int i = 0; i < pattern.Length; i++) { Qrs(samples, (20 + i) * 250 + 125, pattern[i] == 0 ? 8 : 20, pattern[i] < 0 ? -1 : 1); }
            var result = Run(samples, settings: new() { PvcsPerMinuteLimit = 1 });
            Check.That(result.Events.Any(e => e.Condition == expected && e.Transition is EcgMonitoringTransition.Started or EcgMonitoringTransition.Occurred),
                "measured morphology drives " + expected + ": " + result.Reading.LastBeat);
            Check.That(result.Events.Any(e => e.Condition == EcgMonitoringConditions.PvcsPerMinuteHigh && e.Transition == EcgMonitoringTransition.Started), "rolling PVC count reaches its threshold");
        }
        foreach (int period in new[] { 2, 3 })
        {
            short[] repeating = Normal(20).Concat(new short[20 * 250]).ToArray();
            for (int i = 0; i < 20; i++) { Qrs(repeating, 5000 + i * 250 + 75, i % period == period - 1 ? 20 : 8); }
            var repeatingResult = Run(repeating);
            var condition = period == 2 ? EcgMonitoringConditions.VentricularBigeminy : EcgMonitoringConditions.VentricularTrigeminy;
            Check.That(repeatingResult.Events.Count(e => e.Condition == condition) == 1 && (repeatingResult.Reading.ActiveConditions & condition) != 0,
                "ongoing periodic ectopy must not end and restart on each phase of its cycle");
        }
        short[] wide = new short[30 * 250];
        for (int i = 0; i < 30; i++) { Qrs(wide, i * 250 + 125, 20); }
        var learnedWide = Run(wide);
        Check.That(learnedWide.Reading.PvcsLastMinute == 0 && learnedWide.Reading.LastBeat?.Label == EcgBeatLabel.Normal,
            "a stable dominant wide QRS is learned, never automatically called ventricular");
    }

    private static void EcgVentricularRunsRespectRateAndLength()
    {
        foreach (var (count, period, expected) in new[]
        {
            (3, 100, EcgMonitoringConditions.NonSustainedVentricularTachycardia),
            (6, 100, EcgMonitoringConditions.VentricularTachycardia),
            (3, 250, EcgMonitoringConditions.RunPvcs),
            (6, 250, EcgMonitoringConditions.VentricularRhythm)
        })
        {
            short[] samples = Normal(20).Concat(new short[count * period + 500]).ToArray();
            for (int i = 0; i < count; i++) { Qrs(samples, 5000 + 125 + i * period, 20); }
            Qrs(samples, 5000 + 125 + count * period, 8);
            var result = Run(samples);
            Check.That(result.Events.Any(e => e.Condition == expected && e.Transition is EcgMonitoringTransition.Started or EcgMonitoringTransition.Occurred), "rate and run select " + expected);
            if (count >= 6)
            { Check.That(result.Events.All(e => e.Condition != EcgMonitoringConditions.NonSustainedVentricularTachycardia), "sustained run is not later relabeled NSVT"); }
        }
        short[] fast = Normal(20).Concat(new short[2000]).ToArray();
        for (int peak = 5125; peak < fast.Length - 10; peak += 75) { Qrs(fast, peak, 8); }
        var svt = Run(fast);
        Check.That(svt.Events.Any(e => e.Condition == EcgMonitoringConditions.SupraventricularTachycardia), "premature narrow onset followed by fast narrow run supports SVT");
        Check.That(svt.Events.Any(e => e.Condition == EcgMonitoringConditions.ExtremeTachycardia), "extreme rate is independently available");
    }

    private static void EcgMonitoringRestoresAndIgnoresPacketBoundaries()
    {
        short[] samples = Normal(20).Concat(new short[2000]).ToArray();
        for (int i = 0; i < 8; i++) { Qrs(samples, 5125 + i * 200, i % 2 == 0 ? 20 : 8); }
        var expected = Run(samples, 750);
        foreach (int size in new[] { 1, 7, 50, 125 })
        {
            var actual = Run(samples, size, restore: true);
            Check.That(actual.Reading == expected.Reading && actual.Events.SequenceEqual(expected.Events) && actual.Beats.SequenceEqual(expected.Beats),
                "morphology, timers, evidence and transitions survive arbitrary packet boundaries and restore");
        }
        var inverse = Run(samples.Select(s => checked((short)(600 - s))).ToArray());
        Check.That(inverse.Events.SequenceEqual(expected.Events), "polarity and DC changes preserve decisions");
    }

    private static void EcgMonitoringInterruptsAndRejectsAtomically()
    {
        var result = Run(new short[1500]);
        var detector = result.Detector;
        var checkpoint = detector.Capture();
        byte[] poor = Wire(new short[50], 1500, result.Sequence, poor: true);
        detector.Consume(poor, out _, out var events);
        Check.That(events.Any(e => e.Condition == EcgMonitoringConditions.Asystole && e.Transition == EcgMonitoringTransition.Interrupted), "quality loss interrupts rather than announces recovery");
        Check.That(detector.ReadMonitoring(1549 * StepNs).Status == WaveformMeasurementStatus.PoorSignal, "bad signal hides all claims");
        var restored = EcgHeartRateMeasurement.Restore(checkpoint);
        restored.Consume(Wire(new short[50], 1750, result.Sequence + 1), out _, out var gapEvents);
        Check.That(gapEvents.Single(e => e.Condition == EcgMonitoringConditions.Asystole).Transition == EcgMonitoringTransition.Interrupted,
            "gap closes old episode without importing its timer");
        var before = detector.ReadMonitoring(1549 * StepNs);
        IReadOnlyList<DetectedEcgMonitoringEvent> rejected = [];
        bool failed = false;
        try { detector.Consume(poor, out _, out rejected); }
        catch (ArgumentException) { failed = true; }
        Check.That(failed && rejected.Count == 0 && detector.ReadMonitoring(1549 * StepNs) == before, "duplicate packet is atomic");
    }

    private static void EcgRepolarizationRequiresSustainedMeasuredEvidence()
    {
        short[] samples = Normal(400, stMicrovolts: 160, tEndMilliseconds: 540);
        var result = Run(samples, settings: new() { QtcBaselineMilliseconds = 350 });
        Check.That(result.Reading.Repolarization is
        {
            StStatus: WaveformMeasurementStatus.Valid, StMicrovolts: > 100,
            QtStatus: WaveformMeasurementStatus.Valid, QtMilliseconds: > 500, QtcMilliseconds: > 500, DeltaQtcMilliseconds: > 60
        },
            "ST and QT are independently measured from waveform: " + result.Reading.Repolarization);
        foreach (var condition in new[] { EcgMonitoringConditions.StHigh, EcgMonitoringConditions.QtcHigh, EcgMonitoringConditions.DeltaQtcHigh })
        {
            var onset = result.Events.Single(e => e.Condition == condition && e.Transition == EcgMonitoringTransition.Started);
            long delay = condition == EcgMonitoringConditions.StHigh ? 60_000_000_000 : 300_000_000_000;
            Check.That(onset.ConfirmedAtNs - onset.EvidenceFromNs > delay, "persistent ST/QTc threshold: " + onset);
        }
        var shortEpisode = Run(Normal(60, stMicrovolts: 160, tEndMilliseconds: 540));
        Check.That(shortEpisode.Events.All(e => e.Condition is not (EcgMonitoringConditions.StHigh or EcgMonitoringConditions.QtcHigh)), "brief excursion does not satisfy persistence");
        var noT = Run(Normal(30));
        Check.That(noT.Reading.Repolarization.QtMilliseconds is null, "absent T is unavailable, not QT zero");
    }

    private static void EcgQtCorrectionAndStPolarityUseMeasuredUnits()
    {
        short[] source = Normal(40, tEndMilliseconds: 440);
        short[] samples = Enumerable.Range(0, 40).SelectMany(i => source.Skip(i * 250).Take(200)).ToArray();
        var bazett = Run(samples).Reading.Repolarization;
        var fridericia = Run(samples, settings: new() { QtCorrection = EcgQtCorrectionMethod.Fridericia }).Reading.Repolarization;
        Check.That(bazett.QtMilliseconds is > 400 && fridericia.QtMilliseconds == bazett.QtMilliseconds &&
            bazett.QtcMilliseconds > fridericia.QtcMilliseconds &&
            Math.Abs(bazett.QtcMilliseconds!.Value - bazett.QtMilliseconds.Value / Math.Sqrt(0.8)) <= 1 &&
            Math.Abs(fridericia.QtcMilliseconds!.Value - fridericia.QtMilliseconds!.Value / Math.Cbrt(0.8)) <= 1,
            "independent numerical oracle for both corrections at measured RR800 ms");
        var depressed = Run(Normal(100, stMicrovolts: 160, tEndMilliseconds: 540).Select(v => (short)-v).ToArray());
        Check.That(depressed.Reading.Repolarization.StMicrovolts < -100 &&
            depressed.Events.Any(e => e.Condition == EcgMonitoringConditions.StLow && e.Transition == EcgMonitoringTransition.Started),
            "ST signed physical units survive lead polarity");
    }

    private static void EcgMonitoringEventsCommitWithAllChannels()
    {
        var owner = new LiveWaveformMeasurements(OpticalSaturationMeasurement.CreateIllustration(),
            new() { LowHeartRate = 40, ExtremeLowHeartRate = 20, HighHeartRate = 60 });
        var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default);
        bool exercised = false;
        for (int step = 1; step < 50; step++)
        {
            foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            {
                var checkpoint = owner.Capture();
                var probe = LiveWaveformMeasurements.Restore(checkpoint);
                var expected = probe.Consume(wire, out _, out _, out _, out var expectedEvents);
                if (expectedEvents.Count > 0)
                {
                    var block = WaveformEnvelopeCodec.Decode(wire);
                    byte[] invalid = WaveformEnvelopeCodec.EncodeRaw(block with
                    { Planes = block.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(4)).ToArray() });
                    IReadOnlyList<DetectedEcgMonitoringEvent> rejected = [];
                    bool failed = false;
                    try { owner.Consume(invalid, out _, out _, out _, out rejected); }
                    catch (ArgumentException) { failed = true; }
                    Check.That(failed && rejected.Count == 0 && owner.Read(expected.SampleTimeNs) == LiveWaveformMeasurements.Restore(checkpoint).Read(expected.SampleTimeNs),
                        "a late non-ECG failure cannot publish monitoring events or settings-dependent state");
                    exercised = true;
                }
                var actual = owner.Consume(wire, out _, out _, out _, out var events);
                Check.That(actual == expected && events.SequenceEqual(expectedEvents), "owner retries and restores preserve all monitoring state");
            }
        }
        Check.That(exercised, "transaction test reaches an actual advanced event");
    }

    private static void EcgFibrillationRequiresFrequencyAndPersistence()
    {
        short[] wave = Enumerable.Range(0, 2500).Select(i => (short)(350 * Math.Sin(i * 2 * Math.PI * 5 / 250))).ToArray();
        var result = Run(wave);
        Check.That(result.Events.Any(e => e.Condition == EcgMonitoringConditions.SuspectedVentricularFibrillation && e.ConfirmedAtNs >= 4_000_000_000), "smooth uncountable 5 Hz activity requires four seconds");
        var noise = Run(Enumerable.Range(0, 2500).Select(i => (short)(i % 2 == 0 ? 350 : -350)).ToArray());
        Check.That(noise.Events.All(e => e.Condition != EcgMonitoringConditions.SuspectedVentricularFibrillation), "high-frequency noise is not VF evidence");
    }

    private static void EcgMonitoringFlowsThroughLivePreview()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true);
        for (int i = 0; i < 200; i++) { session.Advance(200_000_000); }
        Check.That(session.Measurements?.EcgMonitoring is { Status: WaveformMeasurementStatus.Valid, Learning: false, PvcsLastMinute: 0 },
            "live acquired owner exposes monitoring state");
        session.Advance(1);
        Check.That(session.DetectedMonitoringEvents.Count == 0, "preview does not replay an old event batch");
    }

    private static void EcgAcquiredPresetsProduceVentricularEvidence()
    {
        var pvc = Run(Acquire(PhysiologyIllustrationConfiguration.PrematureVentricular, 60));
        Check.That(pvc.Reading.PvcsLastMinute > 0 && pvc.Events.Any(e => e.Condition == EcgMonitoringConditions.PvcsPerMinuteHigh),
            "actual acquired PVC preset provides ventricular evidence: " + pvc.Reading);
        var vt = Run(Acquire(PhysiologyIllustrationConfiguration.Default, 30)
            .Concat(Acquire(PhysiologyIllustrationConfiguration.VtPreset, 20)).ToArray());
        Check.That(vt.Events.Any(e => e.Condition == EcgMonitoringConditions.VentricularTachycardia),
            "actual acquired VT after normal learning produces VT: " + vt.Reading);
    }

    private static short[] Acquire(PhysiologyIllustrationConfiguration configuration, int seconds)
    {
        var source = PhysiologyIllustrationSource.Create(configuration);
        List<short> samples = [];
        for (int step = 1; step <= (seconds + 3) * 5; step++)
        {
            foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            { samples.AddRange(WaveformEnvelopeCodec.Decode(wire).Planes.Single(p => p.ChannelId == Channel).Samples); }
        }
        return samples.Take(seconds * 250).ToArray();
    }

    private static void EcgPacingRequiresAcquiredPulseEvidence()
    {
        foreach (string mode in new[] { "capture", "pacing", "unknown" })
        {
            var detector = new EcgHeartRateMeasurement(Channel, new() { PacedMode = true });
            short[] samples = Normal(20).Concat(new short[750]).ToArray();
            List<DetectedEcgMonitoringEvent> events = [];
            for (int start = 0; start < samples.Length; start += 50)
            {
                long[] pulses = Enumerable.Range(start, 50).Where(i => i % 250 == 65 &&
                    (i < 5000 || mode == "capture")).Select(i => i * StepNs).ToArray();
                detector.Consume(Wire(samples.Skip(start).Take(50).ToArray(), start, (ulong)(start / 50)),
                    out _, out var batch, mode == "unknown" ? null : new(pulses));
                events.AddRange(batch);
            }
            var failures = events.Where(e => e.Condition is EcgMonitoringConditions.PacerNotCaptured or EcgMonitoringConditions.PacerNotPacing).ToArray();
            if (mode == "unknown")
            {
                Check.That(failures.Length == 0 && !detector.ReadMonitoring((samples.Length - 1) * StepNs).PacingEvidenceAvailable,
                    "missing pulse detector is unknown, never evidence of no pacing");
            }
            else
            {
                var expected = mode == "capture" ? EcgMonitoringConditions.PacerNotCaptured : EcgMonitoringConditions.PacerNotPacing;
                Check.That(failures.Any(e => e.Condition == expected && e.Transition == EcgMonitoringTransition.Started), "pulse/QRS dissociation selects " + expected);
                var restored = EcgHeartRateMeasurement.Restore(detector.Capture());
                Check.That(restored.ReadMonitoring((samples.Length - 1) * StepNs) == detector.ReadMonitoring((samples.Length - 1) * StepNs), "pulse evidence is checkpointed");
            }
            Check.That(events.All(e => e.Condition != EcgMonitoringConditions.MissedBeat), "paced mode disables missed-beat rule");
        }
        var invalid = new EcgHeartRateMeasurement(Channel, new() { PacedMode = true });
        bool rejected = false;
        try { invalid.Consume(Wire(new short[50], 0, 0), out _, out _, new([-StepNs])); }
        catch (ArgumentException) { rejected = true; }
        Check.That(rejected && invalid.Read(0).Status == WaveformMeasurementStatus.NoData, "out-of-packet pulse evidence rejects entire packet");
    }

    private static void EcgRonTWaitsForItsCompensatoryPause()
    {
        short[] samples = Normal(20).Concat(new short[750]).ToArray();
        Qrs(samples, 5075, 8);
        Qrs(samples, 5145, 20);
        Qrs(samples, 5545, 8);
        var result = Run(samples);
        var ronT = result.Events.Single(e => e.Condition == EcgMonitoringConditions.RonTPvc);
        Check.That(ronT.Transition == EcgMonitoringTransition.Occurred && ronT.ConfirmedAtNs > 5545 * StepNs,
            "short coupling alone cannot confirm R-on-T until the compensatory interval is observed");
        var noPause = Run(samples.Take(5400).ToArray());
        Check.That(noPause.Events.All(e => e.Condition != EcgMonitoringConditions.RonTPvc), "unobserved following beat cannot prove the compensatory pause");
    }

    private static void EcgRonTAcquiredPresetsDistinguishLongQtFromOrdinaryPvcs()
    {
        foreach (var pattern in new[] { AvConductionPattern.RonTLongQtPvcIllustration, AvConductionPattern.ShortCoupledRonTPvcIllustration })
        {
            short[] samples = Acquire(PhysiologyIllustrationConfiguration.PrematureVentricular with { ConductionPattern = pattern }, 40);
            var result = Run(samples);
            Check.That(result.Events.Any(e => e.Condition == EcgMonitoringConditions.RonTPvc), "actual acquired R-on-T preset is detected: " + pattern);
            var restored = Run(samples, 37, true);
            Check.That(result.Events.SequenceEqual(restored.Events) && result.Reading == restored.Reading,
                "R-on-T evidence survives packets and checkpoints: " + pattern);
            var inverted = Run(samples.Select(value => (short)-value).ToArray());
            Check.That(result.Events.SequenceEqual(inverted.Events), "R-on-T evidence is independent of lead polarity");
        }
        var ordinary = Run(Acquire(PhysiologyIllustrationConfiguration.PrematureVentricular, 40));
        Check.That(ordinary.Reading.PvcsLastMinute > 0 && ordinary.Events.All(e => e.Condition != EcgMonitoringConditions.RonTPvc),
            "identical PVC schedule without the preceding prolonged T must not produce R-on-T");
    }

    private static void EcgRonTRequiresUnfinishedTAndConfirmation()
    {
        short[] Samples(int tEndMilliseconds, bool plateau = false)
        {
            short[] samples = Normal(21, tEndMilliseconds: tEndMilliseconds).Concat(new short[500]).ToArray();
            if (plateau) { Array.Fill(samples, (short)200, 5084, 100); }
            Qrs(samples, 5200, 20);
            Qrs(samples, 5575, 8);
            return samples;
        }
        short[] overlap = Samples(640);
        var result = Run(overlap);
        var occurrence = result.Events.Single(e => e.Condition == EcgMonitoringConditions.RonTPvc);
        Check.That(occurrence.ConfirmedAtNs > 5575 * StepNs, "longer coupling retains compensatory-pause confirmation");
        Check.That(Run(overlap.Take(5400).ToArray()).Events.All(e => e.Condition != EcgMonitoringConditions.RonTPvc),
            "unfinished T alone does not announce before confirmation");
        foreach (short[] control in new[] { Samples(0), Samples(440), Samples(0, true) })
        {
            Check.That(Run(control).Events.All(e => e.Condition != EcgMonitoringConditions.RonTPvc),
                "absent T, T projected to end before R, and a flat ST plateau are not overlap evidence");
        }
        // Interrupt between the preceding normal beat and the ectopic complex.
        var interrupted = Run(overlap.Take(5150).ToArray());
        interrupted.Detector.Consume(Wire(overlap.Skip(5150).Take(1).ToArray(), 5150, interrupted.Sequence++, true), out _, out _);
        List<DetectedEcgMonitoringEvent> events = [];
        for (int start = 5151; start < overlap.Length; start += 37)
        {
            interrupted.Detector.Consume(Wire(overlap.Skip(start).Take(37).ToArray(), start, interrupted.Sequence++), out _, out var batch);
            events.AddRange(batch);
        }
        Check.That(events.All(e => e.Condition != EcgMonitoringConditions.RonTPvc), "quality interruption discards the preceding T and pending R-on-T evidence");
    }

    private static void EcgRelearningAndPvcWindowExpireEvidence()
    {
        short[] samples = Normal(100);
        Array.Clear(samples, 20 * 250, 2 * 250);
        Qrs(samples, 5075, 20);
        Qrs(samples, 5325, 20);
        var result = Run(samples, settings: new() { PvcsPerMinuteLimit = 1 });
        Check.That(result.Reading.PvcsLastMinute == 0 && result.Events.Any(e => e.Condition == EcgMonitoringConditions.PvcsPerMinuteHigh && e.Transition == EcgMonitoringTransition.Ended),
            "PVC rate is a rolling sixty-second count");
        result.Detector.Relearn(out _);
        Check.That(result.Detector.ReadMonitoring((samples.Length - 1) * StepNs).Learning &&
            result.Detector.ReadMonitoring((samples.Length - 1) * StepNs).PvcsLastMinute is null,
            "manual relearning clears template-dependent claims");
        Check.That(result.Detector.Read((samples.Length - 1) * StepNs).Status == WaveformMeasurementStatus.Valid,
            "manual morphology learning preserves independent QRS rate");
    }

    private static void EcgMonitoringSettingsRejectInvalidCombinations()
    {
        foreach (var settings in new[] { new EcgMonitoringSettings { PauseMilliseconds = 1499 },
            new EcgMonitoringSettings { AsystoleMilliseconds = 4100 }, new EcgMonitoringSettings { VtachRunBeats = 2 },
            new EcgMonitoringSettings { HighHeartRate = 20 }, new EcgMonitoringSettings { QtCorrection = (EcgQtCorrectionMethod)9 } })
        {
            bool rejected = false;
            try { _ = new EcgHeartRateMeasurement(Channel, settings); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid complete configuration is rejected before state exists");
        }
    }

    private static void EcgRepolarizationRecoversAfterAbnormalStartup()
    {
        short[] abnormal = Acquire(PhysiologyIllustrationConfiguration.VtPreset, 30);
        short[] sinus = Acquire(PhysiologyIllustrationConfiguration.Default, 60);
        var initial = Run(abnormal);
        Check.That(initial.Reading.Repolarization.QtMilliseconds is null, "wide startup does not supply a false QT reading");
        var recovered = Run(abnormal.Concat(sinus).ToArray());
        var reference = Run(sinus);
        Check.That(recovered.Reading.LastBeat?.Label == EcgBeatLabel.Normal &&
            recovered.Reading.Repolarization == reference.Reading.Repolarization,
            "later acquired sinus beats recover the same measured ST/QT as a clean sinus startup: " + recovered.Reading);
        var noT = Run(Normal(25).Concat(Normal(40, tEndMilliseconds: 440)).ToArray());
        Check.That(noT.Reading.Repolarization.QtStatus == WaveformMeasurementStatus.Valid,
            "an early unmeasurable T wave is retried without replacing a valid QRS template");
    }

    private static void EcgRepolarizationRecoversAfterStableRateChange()
    {
        short[] normal = Normal(60, tEndMilliseconds: 440);
        short[] faster = Enumerable.Range(0, 60).SelectMany(i => normal.Skip(i * 250).Take(175)).ToArray();
        var recovered = Run(Normal(30, tEndMilliseconds: 440).Concat(faster).ToArray());
        var reference = Run(faster);
        Check.That(recovered.Reading.LastBeat?.Label == EcgBeatLabel.Normal &&
            recovered.Reading.Repolarization == reference.Reading.Repolarization,
            "a stable faster non-SVT rhythm updates its RR reference instead of staying permanently premature: " + recovered.Reading);
    }

    private static short[] WideStartup()
    {
        short[] samples = new short[30 * 250];
        for (int second = 0; second < 30; second++) { Qrs(samples, second * 250 + 75, 20); }
        return samples;
    }

    private static void EcgReferenceRecoveryRequiresConsistentNarrowBeats()
    {
        short[] wide = WideStartup();
        var shortRun = Run(wide.Concat(Normal(8, tEndMilliseconds: 440)).ToArray());
        Check.That(shortRun.Reading.LastBeat?.Label == EcgBeatLabel.Unknown && shortRun.Reading.Repolarization.QtMilliseconds is null,
            "a brief narrow run cannot replace the old template");
        short[] source = Normal(60, tEndMilliseconds: 440);
        short[] irregular = Enumerable.Range(0, 60).SelectMany(i => source.Skip(i * 250).Take(i % 2 == 0 ? 175 : 250)).ToArray();
        var unstable = Run(wide.Concat(irregular).ToArray());
        Check.That(unstable.Reading.LastBeat?.Label == EcgBeatLabel.Unknown && unstable.Reading.Repolarization.QtMilliseconds is null,
            "irregular narrow intervals never accumulate a normal reference");
        var paced = Run(wide.Concat(source).ToArray(), settings: new() { PacedMode = true });
        Check.That(paced.Reading.LastBeat?.Label == EcgBeatLabel.Unknown && paced.Reading.Repolarization.QtMilliseconds is null,
            "automatic recovery cannot bypass missing pacing evidence");
        shortRun.Detector.Relearn(out _);
        short[] afterRelearn = Normal(8, tEndMilliseconds: 440);
        for (int start = 0; start < afterRelearn.Length; start += 50)
        {
            shortRun.Detector.Consume(Wire(afterRelearn.Skip(start).Take(50).ToArray(), wide.Length + 8 * 250 + start,
                shortRun.Sequence + (ulong)(start / 50)), out _, out _);
        }
        Check.That(shortRun.Detector.ReadMonitoring((wide.Length + 16 * 250 - 1) * StepNs).Learning,
            "manual relearning discards an incomplete recovery cohort");
    }

    private static void EcgReferenceRecoveryRestoresAndRejectsAtomically()
    {
        short[] prefix = WideStartup().Concat(Normal(8, tEndMilliseconds: 440)).ToArray();
        short[] samples = prefix.Concat(Normal(35, stMicrovolts: 160, tEndMilliseconds: 540)).ToArray();
        var settings = new EcgMonitoringSettings { QtcBaselineMilliseconds = 350 };
        var expected = Run(samples, settings: settings);
        foreach (int size in new[] { 7, 125, 750 })
        {
            var actual = Run(samples, size, restore: true, settings: settings);
            Check.That(actual.Reading == expected.Reading && actual.Events.SequenceEqual(expected.Events) &&
                actual.Beats.SequenceEqual(expected.Beats), "recovery cohort and updated reference survive arbitrary packetization and checkpoint restore");
        }
        var partial = Run(prefix, settings: settings);
        var checkpoint = partial.Detector.Capture();
        byte[] next = Wire(samples.Skip(prefix.Length).Take(50).ToArray(), prefix.Length, partial.Sequence);
        var restored = EcgHeartRateMeasurement.Restore(checkpoint);
        var expectedBeats = restored.Consume(next, out _, out var expectedEvents);
        IReadOnlyList<DetectedEcgMonitoringEvent> rejected = [];
        bool failed = false;
        try { partial.Detector.Consume(next, out _, out rejected, new([-StepNs])); }
        catch (ArgumentException) { failed = true; }
        Check.That(failed && rejected.Count == 0 && partial.Detector.ReadMonitoring((prefix.Length - 1) * StepNs) == partial.Reading,
            "invalid acquisition evidence cannot consume recovery candidates");
        Check.That(partial.Detector.Consume(next, out _, out var events).SequenceEqual(expectedBeats) && events.SequenceEqual(expectedEvents) &&
            partial.Detector.ReadMonitoring((prefix.Length + 49) * StepNs) == restored.ReadMonitoring((prefix.Length + 49) * StepNs),
            "retry after rejection exactly matches the captured recovery branch");
        Check.That(expected.Reading.Repolarization is { QtStatus: WaveformMeasurementStatus.Valid, DeltaQtcMilliseconds: > 60 } &&
            expected.Reading.Repolarization.DeltaQtcMilliseconds == expected.Reading.Repolarization.QtcMilliseconds - 350 &&
            expected.Events.All(e => e.Condition is not (EcgMonitoringConditions.StHigh or EcgMonitoringConditions.QtcHigh or EcgMonitoringConditions.DeltaQtcHigh)),
            "recovery preserves the configured QTc baseline without bypassing ST/QT alarm persistence");
    }

    private static void EcgReferenceRecoveryPreservesVentricularAndSvtDetection()
    {
        var vt = Run(Acquire(PhysiologyIllustrationConfiguration.Default, 30)
            .Concat(Acquire(PhysiologyIllustrationConfiguration.VtPreset, 45)).ToArray());
        Check.That(vt.Reading.LastBeat?.Label == EcgBeatLabel.Ventricular &&
            vt.Reading.ActiveConditions.HasFlag(EcgMonitoringConditions.VentricularTachycardia) && vt.Reading.Repolarization.QtMilliseconds is null,
            "sustained VT is not learned away while attempting measurement recovery");
        short[] fast = Normal(20).Concat(new short[12000]).ToArray();
        for (int peak = 5125; peak < fast.Length - 10; peak += 75) { Qrs(fast, peak, 8); }
        var svt = Run(fast);
        Check.That(svt.Reading.ActiveConditions.HasFlag(EcgMonitoringConditions.SupraventricularTachycardia) &&
            svt.Reading.LastBeat?.Label == EcgBeatLabel.SupraventricularPremature && svt.Reading.Repolarization.QtMilliseconds is null,
            "sustained fast narrow rhythm remains SVT instead of becoming a recovery reference");
    }

    private static short[] Normal(int seconds, int stMicrovolts = 0, int tEndMilliseconds = 0)
    {
        short[] samples = new short[seconds * 250];
        for (int second = 0; second < seconds; second++)
        {
            int peak = second * 250 + 75;
            Qrs(samples, peak, 8);
            if (tEndMilliseconds == 0) { continue; }
            for (int offset = 9; offset < tEndMilliseconds / 4; offset++)
            {
                int t = offset * 4;
                int value = t < 180 ? stMicrovolts : t < 320 ? stMicrovolts + (300 - stMicrovolts) * (t - 180) / 140 :
                    300 * (tEndMilliseconds - t) / (tEndMilliseconds - 320);
                samples[peak + offset] = (short)value;
            }
        }
        return samples;
    }

    private static void Qrs(short[] samples, int peak, int halfWidth, int polarity = 1)
    {
        for (int offset = -halfWidth; offset <= halfWidth; offset++)
        { samples[peak + offset] = (short)(polarity * (1000 - Math.Abs(offset) * 1000 / halfWidth)); }
    }

    private static (EcgHeartRateMeasurement Detector, EcgMonitoringReading Reading, List<DetectedEcgBeat> Beats,
        List<DetectedEcgMonitoringEvent> Events, ulong Sequence) Run(short[] samples, int size = 50, bool restore = false, EcgMonitoringSettings? settings = null)
    {
        var detector = new EcgHeartRateMeasurement(Channel, settings ?? new());
        List<DetectedEcgBeat> beats = [];
        List<DetectedEcgMonitoringEvent> events = [];
        ulong sequence = 0;
        for (int start = 0; start < samples.Length; start += size)
        {
            beats.AddRange(detector.Consume(Wire(samples.Skip(start).Take(size).ToArray(), start, sequence++), out _, out var batch));
            events.AddRange(batch);
            if (restore && sequence % 17 == 0) { detector = EcgHeartRateMeasurement.Restore(detector.Capture()); }
        }
        return (detector, detector.ReadMonitoring((samples.Length - 1) * StepNs), beats, events, sequence);
    }

    private static byte[] Wire(short[] samples, int firstSampleIndex, ulong sequence, bool poor = false)
    {
        var plane = new WaveformPlane(Channel, 250, 1, (ulong)firstSampleIndex, 1, 1, 0, 1,
            poor ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples,
            poor ? [new(0, (uint)samples.Length, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, 1, sequence, 1,
            firstSampleIndex * StepNs, checked((uint)(samples.Length * StepNs)), [plane]));
    }
}
