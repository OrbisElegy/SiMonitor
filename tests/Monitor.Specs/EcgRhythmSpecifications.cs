// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class EcgRhythmSpecifications
{
    private const long StepNs = 4_000_000;
    private static readonly Guid Channel = PhysiologyIllustrationSource.ChannelId(0);
    public static Specification[] All =>
    [
        new(nameof(AfRecoveryKeepsOneAlarmEpisode), AfRecoveryKeepsOneAlarmEpisode),
        new(nameof(AmbiguousAtrialEvidenceEventuallyInterruptsAf), AmbiguousAtrialEvidenceEventuallyInterruptsAf),
        new(nameof(AfEpisodesStartAndEndFromAcquiredEvidence), AfEpisodesStartAndEndFromAcquiredEvidence),
        new(nameof(RhythmEvidenceSurvivesPacketBoundariesAndRestore), RhythmEvidenceSurvivesPacketBoundariesAndRestore),
        new(nameof(SignalLossInterruptsRatherThanEndsAf), SignalLossInterruptsRatherThanEndsAf),
        new(nameof(IrregularIntervalsAloneNeverDeclareAf), IrregularIntervalsAloneNeverDeclareAf),
        new(nameof(RhythmEventsCommitWithAllLiveMeasurements), RhythmEventsCommitWithAllLiveMeasurements),
        new(nameof(PreviewExposesRhythmEventsOnce), PreviewExposesRhythmEventsOnce),
        new(nameof(SlowRhythmInvalidatesPreviouslyReadyEvidence), SlowRhythmInvalidatesPreviouslyReadyEvidence),
    ];

    private static void AfRecoveryKeepsOneAlarmEpisode()
    {
        foreach (bool fine in new[] { false, true })
            foreach (bool discardStartup in new[] { false, true })
            {
                var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true);
                if (discardStartup) { session.DiscardStartup(); }
                var alarms = new EcgAlarmNotices();
                List<DetectedEcgRhythmEvent> events = [];
                List<string> indications = [];
                bool started = false;
                for (int step = 0; step < 1050; step++)
                {
                    if (step is 300 or 750)
                    {
                        var configuration = step == 300 ? PhysiologyIllustrationConfiguration.Fibrillation(fine) : PhysiologyIllustrationConfiguration.Default;
                        session.ScheduleSource(new(configuration, MonitorDisplayConfiguration.Default(), true), 0);
                    }
                    session.Advance(200_000_000);
                    events.AddRange(session.DetectedRhythmEvents.Where(e => e.Kind == EcgRhythmEventKind.SuspectedAtrialFibrillation));
                    var snapshot = session.Measurements!;
                    var notices = alarms.Evaluate(true, snapshot, session.DetectedMonitoringEvents, session.DetectedRhythmEvents);
                    string indication = string.Join(",", notices.Where(n => n.Id is "ecg-af" or "ecg-af-end" or "ecg-irregular" or "ecg-irregular-end").Select(n => n.Id));
                    started |= indication == "ecg-af";
                    if (started && indications.LastOrDefault() != indication) { indications.Add(indication); }
                }
                Check.That(indications is ["ecg-af", "ecg-af-end", ""],
                    "live sinus/AF/sinus must not downgrade or reannounce AF during recovery: " + string.Join(" -> ", indications));
                Check.That(events.Count == 2 && events[0].Transition == EcgRhythmTransition.Started && events[1].Transition == EcgRhythmTransition.Ended,
                    "live conversion produces exactly one confirmed AF episode and one recovery");
                Check.That(alarms.Lifecycle.NotificationRecords.Count(r => r.Decision.Episode.ConditionId == "ecg-af") == 1,
                    "mixed recovery windows never create another AF sound notification");
            }
    }

    private static void AmbiguousAtrialEvidenceEventuallyInterruptsAf()
    {
        short[] af = Acquire(PhysiologyIllustrationConfiguration.Fibrillation(true), 60);
        short[] noisy = af.Select((v, i) => checked((short)(v + (i % 2 == 0 ? 60 : -60)))).ToArray();
        short[] samples = af.Concat(noisy).ToArray();
        var detector = new EcgHeartRateMeasurement(Channel);
        List<DetectedEcgRhythmEvent> events = [];
        bool active = false;
        for (int start = 0; start < samples.Length; start += 50)
        {
            detector.Consume(Wire(samples.Skip(start).Take(50).ToArray(), start, (ulong)(start / 50)), out var transitions);
            foreach (var transition in transitions.Where(e => e.Kind == EcgRhythmEventKind.SuspectedAtrialFibrillation))
            {
                events.Add(transition);
                active = transition.Transition == EcgRhythmTransition.Started;
            }
            var reading = detector.ReadRhythm((start + 49) * StepNs);
            if (active) { Check.That(reading.SuspectedAtrialFibrillation == true, "a confirmed episode stays published until its explicit end or interruption"); }
            if (start % 850 == 0) { detector = EcgHeartRateMeasurement.Restore(detector.Capture()); }
        }
        Check.That(events.Count == 2 && events[1].Transition == EcgRhythmTransition.Interrupted &&
            events[1].Interruption == EcgRhythmInterruption.InsufficientAtrialEvidence &&
            detector.ReadRhythm((samples.Length - 1) * StepNs).SuspectedAtrialFibrillation is null,
            "persistent ambiguous atrial evidence interrupts AF without claiming recovery or retaining it indefinitely");
    }

    private static void AfEpisodesStartAndEndFromAcquiredEvidence()
    {
        short[] sinus = Acquire(PhysiologyIllustrationConfiguration.Default, 60);
        foreach (bool fine in new[] { false, true })
        {
            short[] af = Acquire(PhysiologyIllustrationConfiguration.Fibrillation(fine), 90);
            short[] samples = sinus.Concat(af).Concat(sinus).ToArray();
            var result = Run(samples, 50);
            foreach (var kind in Enum.GetValues<EcgRhythmEventKind>())
            {
                var transitions = result.Events.Where(e => e.Kind == kind).ToArray();
                Check.That(transitions.Length == 2 && transitions[0].Transition == EcgRhythmTransition.Started &&
                    transitions[1].Transition == EcgRhythmTransition.Ended,
                    "sinus/AF/sinus produces one onset and one evidence-based end: " + string.Join(";", transitions.Select(e => e.ToString())));
                Check.That(transitions[0].ConfirmedAtNs is > 60_000_000_000 and < 110_000_000_000 &&
                    transitions[1].ConfirmedAtNs is > 150_000_000_000 and < 200_000_000_000,
                    "events follow actual transitions with bounded analysis latency");
                Check.That(transitions.All(e => e.ConfirmedAtNs > e.EvidenceFromNs && e.Interruption == EcgRhythmInterruption.None),
                    "window evidence and causal confirmation have separate timestamps");
            }
            Check.That(result.Reading.IrregularRhythm == false && result.Reading.SuspectedAtrialFibrillation == false,
                "organized sinus evidence clears both confirmed episodes");
        }
    }

    private static void RhythmEvidenceSurvivesPacketBoundariesAndRestore()
    {
        short[] samples = Acquire(PhysiologyIllustrationConfiguration.Fibrillation(true), 60);
        var reference = Run(samples, 750);
        Check.That(reference.Reading.SuspectedAtrialFibrillation == true && reference.Events.Count == 2,
            "fine AF has independent atrial evidence and two distinct event kinds");
        foreach (int packetSize in new[] { 1, 7, 50, 125 })
        {
            var current = Run(samples, packetSize, restore: true);
            Check.That(reference.Reading == current.Reading && reference.Events.SequenceEqual(current.Events) && reference.Beats.SequenceEqual(current.Beats),
                "all evidence, cues and transitions are independent of packet boundaries and checkpoint restores");
        }
        short[] inverted = samples.Select(v => checked((short)(600 - v))).ToArray();
        var inverse = Run(inverted, 50);
        Check.That(reference.Events.SequenceEqual(inverse.Events) && inverse.Reading.SuspectedAtrialFibrillation == true,
            "lead inversion and DC shift preserve event decisions");
    }

    private static void SignalLossInterruptsRatherThanEndsAf()
    {
        short[] samples = Acquire(PhysiologyIllustrationConfiguration.Fibrillation(), 60);
        foreach (string failure in new[] { "quality", "gap", "identity", "flat" })
        {
            var detector = new EcgHeartRateMeasurement(Channel);
            ulong sequence = 0;
            for (int start = 0; start < samples.Length; start += 50)
            { detector.Consume(Wire(samples.Skip(start).Take(50).ToArray(), start, sequence++)); }
            var before = detector.ReadRhythm(60_000_000_000 - StepNs);
            Check.That(before.SuspectedAtrialFibrillation == true, "episode is active before loss");
            Check.That(detector.ReadRhythm(61_000_000_000).Status == WaveformMeasurementStatus.NoData &&
                detector.ReadRhythm(61_000_000_000).SuspectedAtrialFibrillation is null,
                "a read without samples is unavailable, never an AF end");
            int offset = samples.Length + (failure == "gap" ? 250 : 0);
            List<DetectedEcgRhythmEvent> events = [];
            int blocks = failure == "flat" ? 30 : 1;
            for (int block = 0; block < blocks; block++)
            {
                detector.Consume(Wire(new short[50], offset, sequence++, poor: failure == "quality", revision: failure == "identity" ? 2UL : 1UL), out var transitions);
                events.AddRange(transitions);
                offset += 50;
            }
            Check.That(events.Count == 2 && events.All(e => e.Transition == EcgRhythmTransition.Interrupted),
                "loss closes both episodes with interruption, never recovery: " + failure);
            Check.That(detector.ReadRhythm(offset * StepNs - StepNs).SuspectedAtrialFibrillation is null,
                "unavailable evidence has no normal/AF claim");
            if (failure == "identity") { continue; }
            for (int start = 0; start < samples.Length; start += 50)
            {
                detector.Consume(Wire(samples.Skip(start).Take(50).ToArray(), offset, sequence++), out var transitions);
                events.AddRange(transitions);
                offset += 50;
                if (start < 7500) { Check.That(transitions.Count == 0, "recovery relearns a complete window"); }
            }
            Check.That(events.Count == 4 && events.Skip(2).All(e => e.Transition == EcgRhythmTransition.Started),
                "returning AF starts a fresh episode once after relearning");
        }
    }

    private static void IrregularIntervalsAloneNeverDeclareAf()
    {
        short[] samples = new short[60 * 250];
        int[] intervals = [173, 239, 190, 206, 166, 226, 183, 247, 218, 199, 231];
        for (int peak = 100, ordinal = 0; peak + 8 < samples.Length; peak += intervals[ordinal++ % intervals.Length])
        {
            for (int i = -7; i <= 7; i++) { samples[peak + i] = (short)(1000 - Math.Abs(i) * 125); }
        }
        var absentAtrial = Run(samples, 50);
        Check.That(absentAtrial.Reading.IrregularRhythm == true && absentAtrial.Reading.SuspectedAtrialFibrillation is null &&
            absentAtrial.Events.All(e => e.Kind == EcgRhythmEventKind.IrregularRhythm),
            "irregular RR and no visible P cannot establish AF without positive atrial evidence");
        short[] fine = Acquire(PhysiologyIllustrationConfiguration.Fibrillation(true), 60);
        short[] noisy = fine.Select((v, i) => checked((short)(v + (i % 2 == 0 ? 60 : -60)))).ToArray();
        var noise = Run(noisy, 50);
        Check.That(noise.Reading.SuspectedAtrialFibrillation is null && noise.Events.All(e => e.Kind != EcgRhythmEventKind.SuspectedAtrialFibrillation),
            "unflagged high-frequency contamination cannot substitute for atrial activity");
    }

    private static void RhythmEventsCommitWithAllLiveMeasurements()
    {
        var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Fibrillation());
        var owner = LiveWaveformMeasurements.CreateIllustration();
        bool rejectedOnset = false;
        int starts = 0;
        for (int step = 1; step <= 310; step++)
        {
            foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            {
                var checkpoint = owner.Capture();
                var probe = LiveWaveformMeasurements.Restore(checkpoint);
                var expected = probe.Consume(wire, out _, out _, out var expectedEvents);
                if (expectedEvents.Count > 0)
                {
                    var block = WaveformEnvelopeCodec.Decode(wire);
                    byte[] invalid = WaveformEnvelopeCodec.EncodeRaw(block with
                    { Planes = block.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(4)).ToArray() });
                    IReadOnlyList<DetectedEcgRhythmEvent> rejected = [];
                    bool failed = false;
                    try { owner.Consume(invalid, out _, out _, out rejected); }
                    catch (ArgumentException) { failed = true; }
                    Check.That(failed && rejected.Count == 0 && owner.Read(expected.SampleTimeNs) == LiveWaveformMeasurements.Restore(checkpoint).Read(expected.SampleTimeNs),
                        "late channel failure publishes neither rhythm state nor events");
                    rejectedOnset = true;
                }
                var actual = owner.Consume(wire, out _, out _, out var events);
                Check.That(actual == expected && events.SequenceEqual(expectedEvents), "retry and full owner restore reproduce transitions exactly");
                starts += events.Count(e => e.Transition == EcgRhythmTransition.Started);
            }
        }
        Check.That(rejectedOnset && starts == 2, "AF and irregular onset each commit once");
    }

    private static void SlowRhythmInvalidatesPreviouslyReadyEvidence()
    {
        short[] af = Acquire(PhysiologyIllustrationConfiguration.Fibrillation(), 60);
        short[] slow = Acquire(PhysiologyIllustrationConfiguration.Default with
        { SeededRate = new(30, new string('0', 64), 0) }, 60);
        var detector = new EcgHeartRateMeasurement(Channel);
        List<DetectedEcgRhythmEvent> events = [];
        short[] samples = af.Concat(slow).Concat(af).ToArray();
        for (int start = 0; start < samples.Length; start += 50)
        {
            detector.Consume(Wire(samples.Skip(start).Take(50).ToArray(), start, (ulong)(start / 50)), out var transitions);
            events.AddRange(transitions);
            long timeNs = (start + 49) * StepNs;
            if (timeNs is >= 100_000_000_000 and < 120_000_000_000)
            {
                var reading = detector.ReadRhythm(timeNs);
                Check.That(detector.Read(timeNs).Status == WaveformMeasurementStatus.Valid &&
                    reading.Status == WaveformMeasurementStatus.WarmingUp && reading.Evidence is null &&
                    reading.IrregularRhythm is null && reading.SuspectedAtrialFibrillation is null,
                    "valid slow QRS must not leave stale rhythm evidence after the RR window loses support: " + reading);
                Check.That(reading == EcgHeartRateMeasurement.Restore(detector.Capture()).ReadRhythm(timeNs),
                    "insufficient-window state survives restoration");
            }
        }
        var irregular = events.Where(e => e.Kind == EcgRhythmEventKind.IrregularRhythm).ToArray();
        Check.That(irregular.Length == 3 && irregular[0].Transition == EcgRhythmTransition.Started &&
            irregular[1].Transition == EcgRhythmTransition.Interrupted &&
            irregular[1].Interruption == EcgRhythmInterruption.InsufficientRrEvidence &&
            irregular[2].Transition == EcgRhythmTransition.Started,
            "loss of RR support interrupts once and returning supported evidence starts a fresh episode");
        Check.That(detector.ReadRhythm((samples.Length - 1) * StepNs).SuspectedAtrialFibrillation == true,
            "retaining acquired observations permits recovery after the rate becomes analyzable again");
    }

    private static void PreviewExposesRhythmEventsOnce()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Fibrillation(), MonitorDisplayConfiguration.Default(), true);
        List<DetectedEcgRhythmEvent> events = [];
        for (int i = 0; i < 300; i++)
        {
            session.Advance(200_000_000);
            events.AddRange(session.DetectedRhythmEvents);
        }
        Check.That(events.Count == 2 && events.All(e => e.Transition == EcgRhythmTransition.Started) &&
            session.Measurements!.EcgRhythm.SuspectedAtrialFibrillation == true,
            "the actual live preview exposes both state and non-replayed transitions");
        session.Advance(1);
        Check.That(session.DetectedRhythmEvents.Count == 0, "advances without acquired packets never replay rhythm events");
    }

    private static (EcgRhythmReading Reading, List<DetectedEcgRhythmEvent> Events, List<DetectedEcgBeat> Beats) Run(short[] samples, int packetSize, bool restore = false)
    {
        var detector = new EcgHeartRateMeasurement(Channel);
        List<DetectedEcgRhythmEvent> events = [];
        List<DetectedEcgBeat> beats = [];
        ulong sequence = 0;
        for (int start = 0; start < samples.Length; start += packetSize)
        {
            beats.AddRange(detector.Consume(Wire(samples.Skip(start).Take(packetSize).ToArray(), start, sequence++), out var transitions));
            events.AddRange(transitions);
            if (restore && sequence % 17 == 0) { detector = EcgHeartRateMeasurement.Restore(detector.Capture()); }
        }
        return (detector.ReadRhythm((samples.Length - 1) * StepNs), events, beats);
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
        Check.That(samples.Count >= seconds * 250, "acquisition delay is flushed");
        return samples.Take(seconds * 250).ToArray();
    }

    private static byte[] Wire(short[] samples, int firstSampleIndex, ulong sequence, bool poor = false, ulong revision = 1)
    {
        var plane = new WaveformPlane(Channel, 250, 1, (ulong)firstSampleIndex, 1, 1, 0, 1,
            poor ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples,
            poor ? [new(0, (uint)samples.Length, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, 1, sequence, revision,
            firstSampleIndex * StepNs, checked((uint)(samples.Length * StepNs)), [plane]));
    }
}
