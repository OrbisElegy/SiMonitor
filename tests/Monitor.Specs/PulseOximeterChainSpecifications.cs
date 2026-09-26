// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class PulseOximeterChainSpecifications
{
    private static readonly Guid Pleth = PhysiologyIllustrationSource.ChannelId(2);
    private static readonly Guid Sensor = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static Specification[] All =>
    [
        new(nameof(AcquiredPeripheralSourceProducesPulseAndSaturation), AcquiredPeripheralSourceProducesPulseAndSaturation),
        new(nameof(OpticalStreamFencesIdentityQualityAndContinuity), OpticalStreamFencesIdentityQualityAndContinuity),
        new(nameof(OximeterRestoreAndFailureAreAtomic), OximeterRestoreAndFailureAreAtomic),
    ];
    private static PulseOximeterIllustrationSource Source(int saturation = 98000, Guid? sensor = null) => new(Pleth, Pleth, sensor ?? Sensor, saturation);
    private static PulseOximeterMeasurement Measurement() => PulseOximeterMeasurement.CreateIllustration(Pleth);

    private static void AcquiredPeripheralSourceProducesPulseAndSaturation()
    {
        foreach (int target in new[] { 75000, 90000, 98000, 99000 })
            foreach (int conduction in new[] { 1, 2 })
            {
                var physical = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { VentricularConductionRatio = conduction });
                var optical = Source(target); var measurement = Measurement(); PulseOximeterReading? reading = null;
                for (int step = 1; step <= 65; step++)
                    foreach (var original in physical.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                    {
                        byte[] wire = optical.ConvertAcquiredPulse(original);
                        var packet = WaveformEnvelopeCodec.Decode(wire);
                        var old = WaveformEnvelopeCodec.Decode(original);
                        Check.That(packet.InstanceId == Sensor && packet.StartSimTimeNs == old.StartSimTimeNs &&
                            packet.Planes.Single(p => p.ChannelId == Pleth).Samples.SequenceEqual(old.Planes.Single(p => p.ChannelId == Pleth).Samples),
                            "same sensor bundle preserves acquired Pleth shape/time, not screen pixels");
                        reading = measurement.Consume(wire);
                        if (packet.StartSimTimeNs < 3_800_000_000)
                        { Check.That(reading.SpO2.SaturationMilliPercent is null, "no saturation before full window"); }
                    }
                Check.That(reading!.PulseRate.Status == WaveformMeasurementStatus.Valid && Math.Abs(reading.PulseRate.MilliBeatsPerMinute!.Value - 75000 / conduction) <= 100, "sensor PR follows actual peripheral ejection: " + conduction + "/" + reading);
                Check.That(reading.SpO2.Status == WaveformMeasurementStatus.Valid && Math.Abs(reading.SpO2.SaturationMilliPercent!.Value - target) <= 500,
                    $"authored target recovered from two quantized wavelengths within0.5 percentage point: {target}/{conduction}: {reading.SpO2}");
            }
        var sensor = Source(); var m = Measurement();
        for (int block = 0; block < 40; block++) { m.Consume(sensor.ConvertAcquiredPulse(Input(block, flat: true))); }
        Check.That(m.Read(7_992_000_000).SpO2.Status == WaveformMeasurementStatus.PoorSignal,
            "constant light without pulsatility cannot display the configured98%");
        var originalBytes = Input(0);
        Check.That(Source(90000).ConvertAcquiredPulse(originalBytes).SequenceEqual(Source(90000).ConvertAcquiredPulse(originalBytes)), "conversion is deterministic");
        Reject(() => Source().ConvertAcquiredPulse(Rewrite(Input(0), b => b with { InstanceId = Sensor })), "cannot conceal an upstream source switch");
        var offsetMeasurement = Measurement(); var plainMeasurement = Measurement();
        for (int block = 0; block < 25; block++)
        {
            var plain = sensor.ConvertAcquiredPulse(Input(block));
            var offset = Rewrite(plain, b => b with
            {
                Planes = b.Planes.Select(p => p.ChannelId == Pleth ? p : p with
                {
                    Samples = p.Samples.Select(v => (short)(v - 1000)).ToArray(),
                    OffsetNumerator = 1000
                }).ToArray()
            });
            Check.That(offsetMeasurement.Consume(offset) == plainMeasurement.Consume(plain), "wire scale/offset contract preserves physical optical samples");
        }
    }

    private static void OpticalStreamFencesIdentityQualityAndContinuity()
    {
        var source = Source(); var measurement = Measurement();
        for (int block = 0; block < 30; block++) { measurement.Consume(source.ConvertAcquiredPulse(Input(block))); }
        var good = measurement.Read(5_992_000_000);
        Check.That(good.SpO2.Status == WaveformMeasurementStatus.Valid, "window fills");
        Check.That(measurement.Read(6_600_000_000).SpO2.Status == WaveformMeasurementStatus.NoData, "no new samples expire reading");
        measurement.Consume(source.ConvertAcquiredPulse(Input(30, quality: true)));
        Check.That(measurement.Read(6_192_000_000).SpO2.Status == WaveformMeasurementStatus.PoorSignal, "quality propagates to both optical planes");
        for (int block = 31; block < 50; block++) { measurement.Consume(source.ConvertAcquiredPulse(Input(block))); }
        Check.That(measurement.Read(9_992_000_000).SpO2.Status == WaveformMeasurementStatus.PoorSignal, "bad data retained invalid for full averaging window");
        measurement.Consume(source.ConvertAcquiredPulse(Input(50)));
        Check.That(measurement.Read(10_192_000_000).SpO2.Status == WaveformMeasurementStatus.Valid, "recovers after complete clean window");
        measurement.Consume(source.ConvertAcquiredPulse(Input(52)));
        Check.That(measurement.Read(10_592_000_000).SpO2.Status == WaveformMeasurementStatus.WarmingUp, "gap clears previous history");
        measurement.Consume(source.ConvertAcquiredPulse(Input(53, revision: 2)));
        Reject(() => measurement.Consume(source.ConvertAcquiredPulse(Input(54))), "reject revision rollback");
        var replacement = Source(90000, Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"));
        Check.That(measurement.Consume(replacement.ConvertAcquiredPulse(Input(54))).SpO2.Status == WaveformMeasurementStatus.WarmingUp,
            "different sensor/target starts new window");
        for (int block = 55; block < 75; block++) { measurement.Consume(replacement.ConvertAcquiredPulse(Input(block))); }
        Check.That(Math.Abs(measurement.Read(14_992_000_000).SpO2.SaturationMilliPercent!.Value - 90000) < 500,
            "old target is not blended into replacement sensor readings");
    }

    private static void OximeterRestoreAndFailureAreAtomic()
    {
        var source = Source(); var measurement = Measurement();
        for (int block = 0; block < 7; block++) { measurement.Consume(source.ConvertAcquiredPulse(Input(block))); }
        var checkpoint = measurement.Capture(); var restored = PulseOximeterMeasurement.Restore(checkpoint);
        for (int block = 7; block < 30; block++)
        {
            var wire = source.ConvertAcquiredPulse(Input(block));
            Check.That(measurement.Consume(wire) == restored.Consume(wire), "mid-window restore preserves PR and SpO2");
        }
        Check.That(PulseOximeterMeasurement.Restore(checkpoint).Read(1_392_000_000).SpO2.Status == WaveformMeasurementStatus.WarmingUp,
            "captured window not changed by later consumes");
        var before = measurement.Read(5_992_000_000);
        var next = source.ConvertAcquiredPulse(Input(30));
        foreach (var bad in new[]
        {
            source.ConvertAcquiredPulse(Input(29)),
            Rewrite(next, b => b with { Planes = b.Planes.Where(p => p.ChannelId != PulseOximeterIllustrationSource.InfraredChannelId).ToArray() }),
            Rewrite(next, b => b with { Planes = b.Planes.Select(p => p.ChannelId == PulseOximeterIllustrationSource.InfraredChannelId ? p with { FirstSampleIndex = p.FirstSampleIndex + 1 } : p).ToArray() }),
            Rewrite(next, b => b with { Planes = b.Planes.Select(p => p.ChannelId == PulseOximeterIllustrationSource.RedChannelId ? p with { ScaleNumerator = int.MaxValue } : p).ToArray() })
        })
        {
            Reject(() => measurement.Consume(bad), "reject malformed optical packet");
            Check.That(before == measurement.Read(5_992_000_000), "late optical failure does not advance PR or window");
        }
        Check.That(measurement.Consume(next) == restored.Consume(next), "valid retry agrees with untouched branch");
        var clipped = source.ConvertAcquiredPulse(Input(31, clip: true));
        Check.That(measurement.Consume(clipped).SpO2.Status == WaveformMeasurementStatus.PoorSignal, "source clipping never becomes plausible optical amplitude");
    }

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception ex) when (ex is ArgumentException or OverflowException) { rejected = true; }
        Check.That(rejected, message);
    }
    private static byte[] Rewrite(byte[] bytes, Func<WaveformEnvelope, WaveformEnvelope> change) => WaveformEnvelopeCodec.EncodeRaw(change(WaveformEnvelopeCodec.Decode(bytes)));
    private static byte[] Input(int block, bool flat = false, bool quality = false, ulong revision = 1, bool clip = false)
    {
        short[] samples = Enumerable.Range(block * 25, 25).Select(i =>
        {
            int phase = i % 100;
            return clip ? short.MaxValue : (short)(flat ? 500 : phase < 20 ? phase * 50 : Math.Max(0, 1000 - (phase - 20) * 40));
        }).ToArray();
        var plane = new WaveformPlane(Pleth, 125, 1, (ulong)block * 25, 1, 1, 0, 1,
            quality ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples, quality ? [new(0, 25, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Pleth, Pleth, 1, 1, (ulong)block, revision,
            block * 200_000_000L, 200_000_000, [plane]));
    }
}
