// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class LiveWaveformMeasurementSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(LiveMeasurementsCommitTogetherAndRestore), LiveMeasurementsCommitTogetherAndRestore),
        new(nameof(LiveOpticsAndQualityRemainSourceSpecific), LiveOpticsAndQualityRemainSourceSpecific),
        new(nameof(NativeSampleBatchesPreserveMeasurementsAndEvents), NativeSampleBatchesPreserveMeasurementsAndEvents),
        new(nameof(NativeSampleBatchesCommitAtomicallyAndRejectFutureSamples), NativeSampleBatchesCommitAtomicallyAndRejectFutureSamples),
        new(nameof(PreviewMeasurementsFollowAcquisitionNotSweep), PreviewMeasurementsFollowAcquisitionNotSweep),
    ];
    private static List<byte[]> Input()
    {
        var source = PhysiologyIllustrationSource.Create(); List<byte[]> result = [];
        for (int i = 1; i <= 110; i++) { result.AddRange(source.AdvanceTo(i * 200_000_000L, 50, 1, 100)); }
        return result;
    }
    private static byte[] Change(byte[] wire, Func<WaveformEnvelope, WaveformEnvelope> change) => WaveformEnvelopeCodec.EncodeRaw(change(WaveformEnvelopeCodec.Decode(wire)));
    private static void LiveMeasurementsCommitTogetherAndRestore()
    {
        var wires = Input(); var owner = LiveWaveformMeasurements.CreateIllustration();
        for (int i = 0; i < 27; i++) { owner.Consume(wires[i]); }
        var checkpoint = owner.Capture(); var restored = LiveWaveformMeasurements.Restore(checkpoint);
        for (int i = 27; i < 80; i++) { Check.That(owner.Consume(wires[i]) == restored.Consume(wires[i]), "partial multi-detector restore matches all readings"); }
        var before = owner.Read(15_999_999_999);
        Check.That(before.HeartRate.MilliBeatsPerMinute == 75000 && Math.Abs(before.ImpedanceRespiration.MilliBreathsPerMinute.GetValueOrDefault() - 16000) < 50 &&
            before.PulseRate.Status == WaveformMeasurementStatus.Valid && Math.Abs(before.Capnography.RespirationsMilliPerMinute.Value.GetValueOrDefault() - 16000) < 50 &&
            before.SpO2.Status == WaveformMeasurementStatus.NoData, "one packet produces independent readings without invented saturation: " + before);
        foreach (byte[]? bad in new[]
        {
            wires[79],
            Change(wires[80], b => b with { Planes = b.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(4)).ToArray() }),
            Change(wires[80], b => b with { Planes = b.Planes.Select(p => p.ChannelId == PhysiologyIllustrationSource.ChannelId(4) ? p with { OffsetNumerator = int.MaxValue } : p).ToArray() })
        })
        {
            Reject(() => owner.Consume(bad));
            Check.That(before == owner.Read(15_999_999_999), "late CO2 failure rolls back already-tried ECG/Resp/Pleth");
        }
        Check.That(owner.Consume(wires[80]) == restored.Consume(wires[80]), "valid retry after atomic rejection");
        Check.That(LiveWaveformMeasurements.Restore(checkpoint).Read(5_399_999_999).SampleTimeNs == 5_399_999_999, "checkpoint was not mutated");
        var stale = owner.Read(17_000_000_000);
        Check.That(stale.HeartRate.Status == WaveformMeasurementStatus.NoData && stale.ImpedanceRespiration.Status == WaveformMeasurementStatus.NoData &&
            stale.PulseRate.Status == WaveformMeasurementStatus.NoData && stale.Capnography.EndTidalCentiMmHg.Status == WaveformMeasurementStatus.NoData,
            "advance sampling query time without input expires all sources");
        owner.Consume(wires[90]);
        Check.That(owner.Read(18_199_999_999).HeartRate.MilliBeatsPerMinute is null, "gap resets learned rates");
        byte[] replacement = Change(wires[0], b => b with { InstanceId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd") });
        Check.That(owner.Consume(replacement).HeartRate.Status == WaveformMeasurementStatus.WarmingUp, "new source can restart sampling clock without old history");
    }
    private static void LiveOpticsAndQualityRemainSourceSpecific()
    {
        var wires = Input(); var owner = LiveWaveformMeasurements.CreateIllustration();
        var source = new PulseOximeterIllustrationSource(PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
            Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"), 90000);
        byte[] Optical(byte[] original)
        {
            var light = WaveformEnvelopeCodec.Decode(source.ConvertAcquiredPulse(original));
            return Change(original, b => b with { InstanceId = light.InstanceId, Planes = b.Planes.Concat(light.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(2))).ToArray() });
        }
        for (int i = 0; i < 70; i++) { owner.Consume(Optical(wires[i])); }
        var before = owner.Read(13_999_999_999);
        Check.That(before.SpO2.Status == WaveformMeasurementStatus.Valid && Math.Abs(before.SpO2.SaturationMilliPercent!.Value - 90000) < 500, "optional paired light measured with other channels");
        byte[] incomplete = Change(Optical(wires[70]), b => b with { Planes = b.Planes.Where(p => p.ChannelId != PulseOximeterIllustrationSource.RedChannelId).ToArray() });
        Reject(() => owner.Consume(incomplete));
        Check.That(before == owner.Read(13_999_999_999), "half optical pair is a malformed transaction");
        byte[] poor = Change(Optical(wires[70]), b => b with
        {
            Planes = b.Planes.Select(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(1) ? p : p with
            { QualityEncoding = WaveformQualityEncoding.Ranges, QualityRanges = [new(0, (uint)p.Samples.Count, 1)] }).ToArray()
        });
        var reading = owner.Consume(poor);
        Check.That(reading.ImpedanceRespiration.Status == WaveformMeasurementStatus.PoorSignal && reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
            reading.SpO2.Status == WaveformMeasurementStatus.Valid, "Resp quality does not invalidate unrelated sensors");
        byte[] noOptics = Change(Optical(wires[71]), b => b with { Planes = b.Planes.Where(p => p.ChannelId != PulseOximeterIllustrationSource.RedChannelId && p.ChannelId != PulseOximeterIllustrationSource.InfraredChannelId).ToArray() });
        Check.That(owner.Consume(noOptics).SpO2.Status == WaveformMeasurementStatus.NoData, "omitted optional sensor immediately removes old saturation");
        Check.That(owner.Consume(Optical(wires[72])).SpO2.Status == WaveformMeasurementStatus.WarmingUp, "returning optical data needs new full window");
    }
    private static void PreviewMeasurementsFollowAcquisitionNotSweep()
    {
        var plain = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default());
        var measured = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true, 98000);
        for (int i = 0; i < 800; i++) { plain.Advance(25_000_000); measured.Advance(25_000_000); }
        var reading = measured.Measurements!;
        Check.That(plain.Measurements is null && reading.HeartRate.Status == WaveformMeasurementStatus.Valid && reading.SpO2.Status == WaveformMeasurementStatus.Valid,
            "opt-in live calculation, thumbnail/default runtime stays cheap");
        Check.That(reading.SampleTimeNs == measured.SimulationTimeNs,
            "all measurements use the current source time without an acquisition backlog");
        Check.That(Enumerable.Range(0, 7).All(channel => plain.Samples(channel, 0, plain.SimulationTimeNs)
                .SequenceEqual(measured.Samples(channel, 0, measured.SimulationTimeNs))),
            "measurements preserve the source samples used by the display");
        Check.That(measured.Measurements == reading, "reading or paused preview never consumes/replays events");
        var noOptics = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true);
        for (int i = 0; i < 200; i++) { noOptics.Advance(50_000_000); }
        Check.That(noOptics.Measurements!.SpO2.Status == WaveformMeasurementStatus.NoData, "main preview has no hidden98% fallback");
    }

    private static void NativeSampleBatchesPreserveMeasurementsAndEvents()
    {
        var optical = new PulseOximeterIllustrationSource(PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
            Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"), 90000);
        WaveformEnvelope[] blocks = Input().Take(80).Select(wire =>
        {
            var original = WaveformEnvelopeCodec.Decode(wire);
            var light = WaveformEnvelopeCodec.Decode(optical.ConvertAcquiredPulse(wire));
            return original with
            {
                InstanceId = light.InstanceId,
                Planes = original.Planes.Concat(light.Planes.Where(plane => plane.ChannelId != PhysiologyIllustrationSource.ChannelId(2))).ToArray()
            };
        }).ToArray();
        var recorded = LiveWaveformMeasurements.CreateIllustration();
        List<DetectedEcgBeat> recordedBeats = [];
        List<DetectedPlethPulse> recordedPulses = [];
        List<DetectedEcgRhythmEvent> recordedRhythms = [];
        List<DetectedEcgMonitoringEvent> recordedMonitoring = [];
        foreach (var block in blocks)
        {
            recorded.Consume(WaveformEnvelopeCodec.EncodeRaw(block), out var beats, out var pulses, out var rhythms, out var monitoring);
            recordedBeats.AddRange(beats);
            recordedPulses.AddRange(pulses);
            recordedRhythms.AddRange(rhythms);
            recordedMonitoring.AddRange(monitoring);
        }
        var live = LiveWaveformMeasurements.CreateIllustration();
        LiveWaveformMeasurements? restored = null;
        List<DetectedEcgBeat> liveBeats = [];
        List<DetectedPlethPulse> livePulses = [];
        List<DetectedEcgRhythmEvent> liveRhythms = [];
        List<DetectedEcgMonitoringEvent> liveMonitoring = [];
        int frames = 0;
        bool partialChannels = false;
        foreach (var (time, channels) in NativeFrames(blocks))
        {
            var reading = live.ConsumeSamples(channels, time, out var beats, out var pulses, out var rhythms, out var monitoring);
            liveBeats.AddRange(beats);
            livePulses.AddRange(pulses);
            liveRhythms.AddRange(rhythms);
            liveMonitoring.AddRange(monitoring);
            partialChannels |= channels.Count is > 0 and < 7;
            if (restored is not null)
            {
                Check.That(restored.ConsumeSamples(channels, time, out var restoredBeats, out var restoredPulses,
                    out var restoredRhythms, out var restoredMonitoring) == reading &&
                    beats.SequenceEqual(restoredBeats) && pulses.SequenceEqual(restoredPulses) &&
                    rhythms.SequenceEqual(restoredRhythms) && monitoring.SequenceEqual(restoredMonitoring),
                    "native partial-frame checkpoint resumes all readings and event identities");
            }
            if (++frames == 40) { restored = LiveWaveformMeasurements.Restore(live.Capture()); }
            Check.That(reading.SampleTimeNs == time && reading == live.Read(time), "querying the current frame does not consume or replay samples");
        }
        const long finalTime = 16_000_000_000;
        Check.That(partialChannels && live.Read(finalTime) == recorded.Read(finalTime),
            "uneven frame lengths and omitted sample grids preserve every recorded-packet measurement including saturation");
        Check.That(liveBeats.SequenceEqual(recordedBeats) && livePulses.SequenceEqual(recordedPulses) &&
            liveRhythms.SequenceEqual(recordedRhythms) && liveMonitoring.SequenceEqual(recordedMonitoring),
            "one detector owner preserves sample-time events independently of rendering frame boundaries");
    }

    private static void NativeSampleBatchesCommitAtomicallyAndRejectFutureSamples()
    {
        WaveformEnvelope[] blocks = Input().Take(2).Select(wire => WaveformEnvelopeCodec.Decode(wire)).ToArray();
        var frames = NativeFrames(blocks).Where(frame => frame.Channels.Count == 7).Take(2).ToArray();
        var owner = LiveWaveformMeasurements.CreateIllustration();
        var first = frames[0];
        owner.ConsumeSamples(first.Channels, first.Time, out _, out _, out _, out _);
        var restored = LiveWaveformMeasurements.Restore(owner.Capture());
        var before = owner.Read(first.Time);
        var next = frames[1];
        var invalid = next.Channels.Select(block => block.Planes[0].ChannelId == PhysiologyIllustrationSource.ChannelId(4)
            ? block with { Planes = [block.Planes[0] with { OffsetNumerator = int.MaxValue }] } : block).ToArray();
        Reject(() => owner.ConsumeSamples(invalid, next.Time, out _, out _, out _, out _));
        Reject(() => owner.ConsumeSamples(next.Channels, first.Time, out _, out _, out _, out _));
        Reject(() => owner.ConsumeSamples([next.Channels[0], next.Channels[0]], next.Time, out _, out _, out _, out _));
        Check.That(owner.Read(first.Time) == before, "late detector failure, future samples and duplicate channels preserve the whole committed batch");
        Check.That(owner.ConsumeSamples(next.Channels, next.Time, out var beats, out var pulses, out var rhythms, out var monitoring) ==
            restored.ConsumeSamples(next.Channels, next.Time, out var restoredBeats, out var restoredPulses, out var restoredRhythms, out var restoredMonitoring) &&
            beats.SequenceEqual(restoredBeats) && pulses.SequenceEqual(restoredPulses) && rhythms.SequenceEqual(restoredRhythms) &&
            monitoring.SequenceEqual(restoredMonitoring), "valid native retry commits once without hidden detector progress");
        Reject(() => owner.ConsumeSamples(next.Channels, next.Time, out _, out _, out _, out _));
        Check.That(owner.ConsumeSamples([], next.Time, out beats, out pulses, out rhythms, out monitoring) == owner.Read(next.Time) &&
            beats.Count + pulses.Count + rhythms.Count + monitoring.Count == 0,
            "an empty or paused frame publishes no historical events");
    }

    private static IEnumerable<(long Time, IReadOnlyList<WaveformEnvelope> Channels)> NativeFrames(WaveformEnvelope[] blocks)
    {
        long[] frameDurations = [1_000_000, 16_000_000, 33_000_000, 50_000_000, 125_000_000];
        var template = blocks[0];
        var samples = template.Planes.ToDictionary(plane => plane.ChannelId,
            plane => blocks.SelectMany(block => block.Planes.Single(candidate => candidate.ChannelId == plane.ChannelId).Samples).ToArray());
        int[] positions = new int[7];
        ulong[] sequences = new ulong[7];
        long end = blocks[^1].StartSimTimeNs + blocks[^1].DurationNs;
        long time = template.StartSimTimeNs;
        int frame = 0;
        while (time < end)
        {
            time = Math.Min(end, time + frameDurations[frame++ % frameDurations.Length]);
            List<WaveformEnvelope> channels = [];
            for (int channel = 0; channel < 7; channel++)
            {
                Guid id = PhysiologyIllustrationSource.ChannelId(channel);
                var primary = template.Planes.Single(plane => plane.ChannelId == id);
                long step = 1_000_000_000L * primary.SampleRateDenominator / primary.SampleRateNumerator;
                int from = positions[channel];
                int to = Math.Min(samples[id].Length, checked((int)((time - template.StartSimTimeNs + step - 1) / step)));
                if (from == to) { continue; }
                WaveformPlane[] planes = template.Planes.Where(plane => plane.ChannelId == id || channel == 2 &&
                    (plane.ChannelId == PulseOximeterIllustrationSource.RedChannelId || plane.ChannelId == PulseOximeterIllustrationSource.InfraredChannelId))
                    .Select(plane => plane with { FirstSampleIndex = plane.FirstSampleIndex + (ulong)from, Samples = samples[plane.ChannelId][from..to] }).ToArray();
                channels.Add(template with
                {
                    BlockSequence = sequences[channel]++,
                    StartSimTimeNs = template.StartSimTimeNs + from * step,
                    DurationNs = checked((uint)((to - from) * step)),
                    Planes = planes
                });
                positions[channel] = to;
            }
            yield return (time, channels);
        }
    }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (Exception ex) when (ex is ArgumentException or OverflowException) { rejected = true; }
        Check.That(rejected, "invalid input rejected");
    }
}
