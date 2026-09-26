// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class MonitorAlertSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(RotationWeightsAndPreemption), RotationWeightsAndPreemption),
        new(nameof(AlarmPcmMatchesGroupedPatterns), AlarmPcmMatchesGroupedPatterns),
        new(nameof(PerfusionUsesOpticalSamples), PerfusionUsesOpticalSamples),
    ];
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
            (MonitorNoticeLevel.Warning, 4, 10), (MonitorNoticeLevel.Critical, 2, 4)
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
