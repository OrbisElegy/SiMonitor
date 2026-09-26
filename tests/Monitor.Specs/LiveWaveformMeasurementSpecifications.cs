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
        Check.That(reading.SampleTimeNs >= measured.FrontierNs && reading.SampleTimeNs < measured.SimulationTimeNs,
            "measurement clock follows acquired samples, separate from display and simulation");
        Check.That(plain.Blocks.SelectMany(b => b.Planes.SelectMany(p => p.Samples)).SequenceEqual(measured.Blocks.SelectMany(b => b.Planes.SelectMany(p => p.Samples))) && plain.FrontierNs == measured.FrontierNs,
            "measurements preserve original display waveforms and sweep pacing");
        Check.That(measured.Measurements == reading, "reading or paused preview never consumes/replays events");
        var noOptics = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), true);
        for (int i = 0; i < 200; i++) { noOptics.Advance(50_000_000); }
        Check.That(noOptics.Measurements!.SpO2.Status == WaveformMeasurementStatus.NoData, "main preview has no hidden98% fallback");
    }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (Exception ex) when (ex is ArgumentException or OverflowException) { rejected = true; }
        Check.That(rejected, "invalid input rejected");
    }
}
