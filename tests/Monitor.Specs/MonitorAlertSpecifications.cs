// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class MonitorAlertSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(RotationWeightsAndPreemption), RotationWeightsAndPreemption),
        new(nameof(AlarmPcmMatchesGroupedPatterns), AlarmPcmMatchesGroupedPatterns),
        new(nameof(PerfusionUsesOpticalSamples), PerfusionUsesOpticalSamples),
        new(nameof(SelectedTonesRestoreAndMixIndependently), SelectedTonesRestoreAndMixIndependently),
        new(nameof(MeasuredBeatEventsCommitOnce), MeasuredBeatEventsCommitOnce),
        new(nameof(AlarmLevelDominatesRoutineHeartbeat), AlarmLevelDominatesRoutineHeartbeat),
        new(nameof(OpticalRunoffCannotMasqueradeAsSaturation), OpticalRunoffCannotMasqueradeAsSaturation),
        new(nameof(SaturationLimitsUseValidMeasurements), SaturationLimitsUseValidMeasurements),
        new(nameof(SaturationLimitsFollowAcquiredOptics), SaturationLimitsFollowAcquiredOptics),
    ];
    private static void SaturationLimitsUseValidMeasurements()
    {
        OpticalSaturationReading Reading(int? value, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid) => new(status, value, null, 0);
        foreach (var (value, expected) in new (int, MonitorNoticeLevel?)[]
        { (0, MonitorNoticeLevel.Critical), (84999, MonitorNoticeLevel.Critical), (85000, MonitorNoticeLevel.Warning),
            (91999, MonitorNoticeLevel.Warning), (92000, null), (100000, null) })
        {
            var notice = SpO2LimitNotice.Evaluate(true, 92000, 85000, Reading(value));
            Check.That(notice?.Level == expected && (notice is null || notice.Id == "spo2-low" && notice.Numeric == MonitorNumeric.SpO2),
                "strict unrounded measured limits bind only SpO2 numerical field");
            Check.That(SpO2LimitNotice.Evaluate(false, 92000, 85000, Reading(value)) is null, "explicit opt-in required");
        }
        foreach (var status in Enum.GetValues<WaveformMeasurementStatus>().Where(s => s != WaveformMeasurementStatus.Valid))
        { Check.That(SpO2LimitNotice.Evaluate(true, 92000, 85000, Reading(80000, status)) is null, "invalid measurement clears previous low condition"); }
        foreach (int? value in new int?[] { null, -1, 100001 })
        { Check.That(SpO2LimitNotice.Evaluate(true, 92000, 85000, Reading(value)) is null, "missing/out-of-range reading cannot trigger physiological alert"); }
        foreach (var (warning, critical) in new (int?, int?)[] { (null, 85000), (92000, null), (85000, 85000), (84000, 85000), (100001, 85000), (92000, 999) })
        {
            var notice = SpO2LimitNotice.Evaluate(true, warning, critical, Reading(80000));
            Check.That(notice?.Id == "spo2-settings" && notice.Level == MonitorNoticeLevel.Info && notice.Numeric is null,
                "bad settings yield only unbound configuration info");
        }
        Check.That(SpO2LimitNotice.Evaluate(true, 90250, 85250, Reading(90249))?.Level == MonitorNoticeLevel.Warning &&
            SpO2LimitNotice.Evaluate(true, 90250, 85250, Reading(90250)) is null, "fractional thresholds do not round through displayed integer");
    }
    private static void SaturationLimitsFollowAcquiredOptics()
    {
        foreach (var (target, expected) in new (int, MonitorNoticeLevel?)[]
        { (98000, null), (90000, MonitorNoticeLevel.Warning), (80000, MonitorNoticeLevel.Critical) })
        {
            var physical = PhysiologyIllustrationSource.Create();
            var optical = new PulseOximeterIllustrationSource(PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
                Guid.NewGuid(), target);
            var measurement = LiveWaveformMeasurements.CreateIllustration();
            LiveMeasurementSnapshot? snapshot = null;
            for (int step = 1; step <= 45; step++)
                foreach (var wire in physical.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                {
                    var original = WaveformEnvelopeCodec.Decode(wire);
                    var light = WaveformEnvelopeCodec.Decode(optical.ConvertAcquiredPulse(wire));
                    var combined = original with
                    {
                        InstanceId = light.InstanceId,
                        Planes = original.Planes.Concat(light.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(2))).ToArray()
                    };
                    snapshot = measurement.Consume(WaveformEnvelopeCodec.EncodeRaw(combined));
                }
            Check.That(snapshot?.SpO2.Status == WaveformMeasurementStatus.Valid, "actual paired sensor path reaches a measured saturation");
            var notice = SpO2LimitNotice.Evaluate(true, 92000, 85000, snapshot!.SpO2);
            Check.That(notice?.Level == expected, "alarm consumes estimated optical ratio, not configured target");
            var expired = measurement.Read(snapshot.SampleTimeNs + 600_000_000);
            Check.That(SpO2LimitNotice.Evaluate(true, 92000, 85000, expired.SpO2) is null, "loss of samples clears low alert without inventing a sensor disconnection");
        }
    }
    private static void OpticalRunoffCannotMasqueradeAsSaturation()
    {
        var estimator = new OpticalSaturationMeasurement("runoff-test", [new(400000, 100000), new(1600000, 70000)]);
        foreach (int direction in new[] { -1, 1 })
        {
            var samples = Enumerable.Range(0, 500).Select(i => new OpticalSample(i * 8_000_000L,
                10000 + direction * i, 20000 + direction * 2 * i)).ToArray();
            var result = estimator.Estimate(samples, samples[^1].SampleTimeNs);
            Check.That(result.Status == WaveformMeasurementStatus.PoorSignal && result.SaturationMilliPercent is null && result.PerfusionMilliPercent is null,
                "large coherent monotonic drift/runoff cannot become saturation or apparent PI");
            var jitter = samples.Select((s, i) => s with { Red = s.Red + i % 2 * 3, Infrared = s.Infrared + i % 3 * 3 }).ToArray();
            Check.That(estimator.Estimate(jitter, jitter[^1].SampleTimeNs).SaturationMilliPercent is null, "quantization-scale reversals cannot qualify residual runoff");
        }
        var physical = PhysiologyIllustrationSource.Create();
        var optical = new PulseOximeterIllustrationSource(PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), 98000);
        var measurement = PulseOximeterMeasurement.CreateIllustration(PhysiologyIllustrationSource.ChannelId(2));
        bool normal = false, lost = false, recovered = false;
        for (int step = 1; step <= 100; step++)
            foreach (var wire in physical.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            {
                var block = Monitor.Simulation.Acquisition.WaveformEnvelopeCodec.Decode(wire);
                long time = block.StartSimTimeNs;
                if (time is >= 6_000_000_000 and < 14_000_000_000)
                {
                    block = block with
                    {
                        Planes = block.Planes.Select(p => p.ChannelId == PhysiologyIllustrationSource.ChannelId(2)
                        ? p with { Samples = Enumerable.Range(0, p.Samples.Count).Select(i => (short)(1500 - (time - 6_000_000_000 + i * 8_000_000L) / 8_000_000)).ToArray() } : p).ToArray()
                    };
                }
                var reading = measurement.Consume(optical.ConvertAcquiredPulse(Monitor.Simulation.Acquisition.WaveformEnvelopeCodec.EncodeRaw(block)));
                if (time is >= 4_000_000_000 and < 6_000_000_000) { normal |= reading.SpO2.Status == WaveformMeasurementStatus.Valid; }
                if (time is >= 10_000_000_000 and < 14_000_000_000)
                {
                    Check.That(reading.SpO2.Status == WaveformMeasurementStatus.PoorSignal && reading.SpO2.SaturationMilliPercent is null,
                        "entire finite window without new pulsation expires optical percentage despite ongoing sample delivery");
                    lost = true;
                }
                if (time >= 17_800_000_000) { recovered |= reading.SpO2.Status == WaveformMeasurementStatus.Valid; }
            }
        Check.That(normal && lost && recovered, "valid-pulse to prolonged runoff to recovery exercised without changing oxygen target");
    }
    private static void AlarmLevelDominatesRoutineHeartbeat()
    {
        float[] Render(TonePreset preset)
        {
            float[] pcm = new float[4800]; new ToneVoice(preset).Render(pcm); return pcm;
        }
        double Rms(float[] pcm) => Math.Sqrt(pcm.Average(v => (double)v * v));
        foreach (int volume in new[] { 25, 50, 100 })
        {
            var beat = Render(SelectedMonitorTones.Heartbeat(volume));
            foreach (var level in Enum.GetValues<MonitorNoticeLevel>())
            {
                var alarm = Render(SelectedMonitorTones.Alarm(level, volume));
                Check.That(Rms(alarm) >= 4 * Rms(beat), "every alarm attack exceeds routine beep by at least12dB RMS over100ms");
                Check.That(alarm.Zip(beat).All(p => Math.Abs(p.First + p.Second) < 1), "simultaneous alarm and heartbeat retain headroom");
            }
        }
        Check.That(Render(SelectedMonitorTones.Heartbeat(0)).All(v => v == 0) &&
            Render(SelectedMonitorTones.Alarm(MonitorNoticeLevel.Critical, 0)).All(v => v == 0), "master mute silences both voices");
    }
    private static void SelectedTonesRestoreAndMixIndependently()
    {
        var preset = SelectedMonitorTones.Alarm(MonitorNoticeLevel.Critical, 100);
        Check.That(preset.TotalFrames == 168000 && new MonitorSoundTiming().CriticalMilliseconds == 1500, "selected3.5s tail and1.5s default");
        float[] whole = new float[preset.TotalFrames]; new ToneVoice(preset).Render(whole);
        var voice = new ToneVoice(preset); voice.Render(new float[72000]);
        var restored = ToneVoice.Restore(voice.CaptureState()); float[] tail = new float[whole.Length - 72000]; restored.Render(tail);
        Check.That(tail.SequenceEqual(whole.Skip(72000)) && whole.Skip(48000).Take(24000).Any(v => Math.Abs(v) > .00025), "restoration preserves natural decay through former silent gap");
        var measured = new ToneVoice(preset); float[] scratch = new float[480]; measured.Render(scratch);
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        while (!measured.Finished) { measured.Render(scratch); }
        Check.That(GC.GetAllocatedBytesForCurrentThread() == allocation, "selected voice rendering allocates nothing");

        float[] Render(bool alarms, bool beats, int period = 1500)
        {
            var session = new AudioRenderSession(); var sequencer = new MonitorAlarmSequencer(session);
            var request = alarms ? new MonitorAlarmSoundRequest(MonitorNoticeLevel.Critical, 100, new(CriticalMilliseconds: period)) : null;
            float[] result = new float[240000];
            for (int tick = 0; tick < 500; tick++)
            {
                sequencer.Update(request);
                sequencer.UpdateHeartbeat(beats, beats && tick % 80 == 0 ? 100 : null);
                session.TryProduce(480); session.Read(result.AsSpan(tick * 480, 480));
            }
            return result;
        }
        var alarm = Render(true, false); var beat = Render(false, true); var mix = Render(true, true);
        Check.That(mix.Select((v, i) => Math.Abs(v - alarm[i] - beat[i])).Max() < .000001f && mix.Max(Math.Abs) < 1,
            "independent800ms beat clock adds even at simultaneous starts, no ducking or clipping");
        var rapid = Render(true, false, 250);
        float[] expected = new float[rapid.Length];
        for (int start = 480; start < expected.Length; start += 12000)
            for (int i = 0; i < whole.Length && start + i < expected.Length; i++) { expected[start + i] += whole[i]; }
        Check.That(expected.Zip(rapid).All(p => Math.Abs(p.First - p.Second) < .000001f), "minimum interval retains every overlapping tail within bounded voice capacity");

        var cancel = new AudioRenderSession(); var owner = new MonitorAlarmSequencer(cancel);
        owner.Update(new(MonitorNoticeLevel.Critical, 100, new())); owner.UpdateHeartbeat(true, 100);
        cancel.TryProduce(480); cancel.Read(scratch); cancel.TryProduce(480); cancel.Read(scratch);
        owner.Update(null);
        cancel.TryProduce(480); cancel.Read(scratch); cancel.TryProduce(480); cancel.Read(scratch);
        Check.That(scratch.Any(v => v != 0), "alarm cancellation leaves independent heartbeat sounding");
        owner.UpdateHeartbeat(false, null); cancel.TryProduce(480); cancel.Read(scratch);
        cancel.TryProduce(480); cancel.Read(scratch);
        Check.That(scratch.All(v => v == 0), "pause/disable fades heartbeat without replay");
    }
    private static void MeasuredBeatEventsCommitOnce()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true);
        var observed = new List<DetectedEcgBeat>();
        for (int i = 0; i < 400; i++)
        {
            session.Advance(25_000_000); observed.AddRange(session.DetectedBeats);
        }
        Check.That(observed.Count >= 8 && observed.Select(b => b.PeakTimeNs).Distinct().Count() == observed.Count,
            "acquired ECG emits actual detections once rather than extrapolating rate");
        Check.That(observed.All(b => b.ConfirmedAtNs > b.PeakTimeNs), "detector confirmation is not a fabricated zero-latency event");
        var source = PhysiologyIllustrationSource.Create(); var owner = LiveWaveformMeasurements.CreateIllustration();
        var flat = LiveWaveformMeasurements.CreateIllustration();
        int events = 0;
        for (int tick = 1; tick <= 40; tick++)
            foreach (var wire in source.AdvanceTo(tick * 200_000_000L, 50, 1, 100))
            {
                var before = owner.Capture();
                var block = Monitor.Simulation.Acquisition.WaveformEnvelopeCodec.Decode(wire);
                var zero = block with
                {
                    Planes = block.Planes.Select(p => p.ChannelId == PhysiologyIllustrationSource.ChannelId(0)
                        ? p with { Samples = new short[p.Samples.Count] } : p).ToArray()
                };
                flat.Consume(Monitor.Simulation.Acquisition.WaveformEnvelopeCodec.EncodeRaw(zero), out var noBeats);
                Check.That(noBeats.Count == 0, "flat acquired ECG produces no beep despite unchanged generator heart-rate settings");
                owner.Consume(wire, out var beats); events += beats.Count;
                var restored = LiveWaveformMeasurements.Restore(before);
                restored.Consume(wire, out var replay);
                Check.That(beats.SequenceEqual(replay), "checkpoint detector continuation preserves confirmed event identity");
                if (beats.Count > 0)
                {
                    var broken = block with { Planes = block.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(6)).ToArray() };
                    var atomic = LiveWaveformMeasurements.Restore(before);
                    IReadOnlyList<DetectedEcgBeat> leaked = [];
                    bool lateFailure = false;
                    try { atomic.Consume(Monitor.Simulation.Acquisition.WaveformEnvelopeCodec.EncodeRaw(broken), out leaked); }
                    catch (ArgumentException) { lateFailure = true; }
                    Check.That(lateFailure && leaked.Count == 0, "late CVP rejection cannot publish earlier ECG detections");
                    atomic.Consume(wire, out var retry);
                    Check.That(retry.SequenceEqual(beats), "valid retry publishes detector events once after rollback");
                }
                IReadOnlyList<DetectedEcgBeat> rejected = beats;
                bool failed = false;
                try { owner.Consume(wire, out rejected); } catch (ArgumentException) { failed = true; }
                Check.That(failed && rejected.Count == 0, "rejected duplicate packet cannot leak or replay a beat");
            }
        Check.That(events > 0, "real detector events exercised");
    }
    private static void RotationWeightsAndPreemption()
    {
        var rotation = new MonitorNoticeRotation();
        MonitorNotice[] messages = [new("i", MonitorNoticeLevel.Info, "info"), new("n", MonitorNoticeLevel.Notice, "notice"),
            new("w", MonitorNoticeLevel.Warning, "warning"), new("c", MonitorNoticeLevel.Critical, "critical")];
        int[] counts = new int[4];
        for (int second = 0; second < 40; second++)
        {
            rotation.Update(messages, second * 1_000_000_000L); counts[(int)rotation.Current!.Level]++;
            Check.That(rotation.Highest == MonitorNoticeLevel.Critical, "audio priority independent of rotated text");
        }
        Check.That(counts.SequenceEqual(new[] { 4, 8, 12, 16 }), "one message with level-weighted time share");
        rotation.Update(messages[..2], 40_000_000_000); Check.That(rotation.Current!.Level == MonitorNoticeLevel.Notice, "removed selected alert leaves immediately");
        rotation.Update(messages, 41_000_000_000); Check.That(rotation.Current!.Level == MonitorNoticeLevel.Critical, "higher new priority preempts");
        var previous = rotation.Current;
        bool rejected = false;
        try { rotation.Update([messages[0], messages[0]], 42_000_000_000); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected && rotation.Current == previous, "duplicate-set rejection preserves accepted display");
        rotation.Update([], 42_000_000_000); Check.That(rotation.Current is null && rotation.Highest is null, "clear has no residual alert");
        var equal = new MonitorNoticeRotation();
        MonitorNotice[] peers = [new("a", MonitorNoticeLevel.Info, "a"), new("b", MonitorNoticeLevel.Info, "b")];
        equal.Update(peers, 0); string first = equal.Current!.Id;
        equal.Update(peers, 2_000_000_000); Check.That(equal.Current!.Id != first, "same-level round robin");
    }
    private static void AlarmPcmMatchesGroupedPatterns()
    {
        foreach (var (level, seconds, expected) in new[]
        {
            (MonitorNoticeLevel.Info, 2, 0), (MonitorNoticeLevel.Notice, 2, 3),
            (MonitorNoticeLevel.Warning, 4, 10), (MonitorNoticeLevel.Critical, 2, 1)
        })
        {
            var session = new AudioRenderSession(); var sequencer = new MonitorAlarmSequencer(session);
            var request = new MonitorAlarmSoundRequest(level, 50, new());
            float[] pcm = new float[480]; int bursts = 0; bool old = false;
            for (int tick = 0; tick < seconds * 100; tick++)
            {
                sequencer.Update(request); Check.That(session.TryProduce(pcm.Length), "bounded audio production"); session.Read(pcm);
                bool active = pcm.Any(v => Math.Abs(v) > .0001f);
                if (active && !old) { bursts++; }
                old = active;
            }
            Check.That(bursts == expected, $"{level} audible grouped onsets: {bursts}");
            sequencer.Update(null); session.TryProduce(480); session.Read(pcm);
            session.TryProduce(480); session.Read(pcm);
            Check.That(pcm.All(v => v == 0), "cleared alert cancels pending cues and fades active cue");
        }
        var output = new AudioRenderSession(); var owner = new MonitorAlarmSequencer(output);
        owner.Update(new(MonitorNoticeLevel.Warning, 50, new()));
        owner.Update(new(MonitorNoticeLevel.Info, 50, new()));
        float[] cancelled = new float[480]; output.TryProduce(480); output.Read(cancelled);
        Check.That(cancelled.All(v => v == 0), "priority change cancels queued old group before start");
        bool rejected = false;
        try { owner.Update(new(MonitorNoticeLevel.Warning, 50, new(WarningMilliseconds: 200))); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "overlapping group periods rejected");
    }
    private static void PerfusionUsesOpticalSamples()
    {
        var estimator = new OpticalSaturationMeasurement("test", [new(400000, 100000), new(1600000, 70000)]);
        var samples = Enumerable.Range(0, 500).Select(i => new OpticalSample(i * 8_000_000L,
            i % 100 < 50 ? 9800 : 10200, i % 100 < 50 ? 19600 : 20400)).ToArray();
        var reading = estimator.Estimate(samples, samples[^1].SampleTimeNs);
        Check.That(reading.PerfusionMilliPercent == 4000, "IR peak-to-peak over mean is four percent");
        var flat = samples.Select(s => s with { Red = 10000, Infrared = 20000 }).ToArray();
        var zero = estimator.Estimate(flat, flat[^1].SampleTimeNs);
        Check.That(zero.PerfusionMilliPercent == 0 && zero.SaturationMilliPercent is null, "zero modulation can show zero PI without fabricating saturation");
        samples[25] = samples[25] with { QualityFlags = 1 };
        Check.That(estimator.Estimate(samples, samples[^1].SampleTimeNs).PerfusionMilliPercent is null, "flagged optical signal has no PI");
        Check.That(estimator.Estimate(flat, 5_000_000_000).PerfusionMilliPercent is null, "expired PI not retained");
        LiveMeasurementSnapshot Run(int gain)
        {
            var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true, 98000, gain);
            for (int i = 0; i < 240; i++) { session.Advance(50_000_000); }
            return session.Measurements!;
        }
        var baseline = Run(1000); var stronger = Run(2000);
        Check.That(stronger.SpO2.PerfusionMilliPercent > baseline.SpO2.PerfusionMilliPercent * 1.9m &&
            stronger.SpO2.PerfusionMilliPercent < baseline.SpO2.PerfusionMilliPercent * 2.1m, "optical gain changes sample-derived PI");
        Check.That(Math.Abs(stronger.SpO2.SaturationMilliPercent!.Value - baseline.SpO2.SaturationMilliPercent!.Value) < 500 &&
            stronger.PulseRate.MilliBeatsPerMinute == baseline.PulseRate.MilliBeatsPerMinute, "gain does not substitute for saturation or PR");
    }
}
