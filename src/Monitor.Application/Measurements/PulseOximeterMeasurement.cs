// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Application.Measurements;

public sealed record PulseOximeterReading(PlethPulseRateReading PulseRate, OpticalSaturationReading SpO2);

// One sensor packet feeds both measurements; neither reads its target settings.
public sealed class PulseOximeterMeasurement
{
    private readonly Guid _plethChannel, _redChannel;
    private PlethPulseRateMeasurement _pulse;
    private OpticalSaturationAcquisition _saturation;
    public PulseOximeterMeasurement(Guid plethChannel, Guid redChannel, Guid infraredChannel, OpticalSaturationMeasurement calibration)
    {
        if (plethChannel == redChannel || plethChannel == infraredChannel)
        { throw new ArgumentException("PulseOximeter.ChannelCollision"); }
        _plethChannel = plethChannel; _redChannel = redChannel;
        _pulse = new(plethChannel); _saturation = new(redChannel, infraredChannel, calibration);
    }
    private PulseOximeterMeasurement(Checkpoint checkpoint)
    {
        _plethChannel = checkpoint.Pleth; _redChannel = checkpoint.Red;
        _pulse = PlethPulseRateMeasurement.Restore(checkpoint.Pulse);
        _saturation = OpticalSaturationAcquisition.Restore(checkpoint.Saturation);
    }

    // Explicit opt-in to the teaching model. Never use this calibration for
    // hardware, or pass a saturation target to the measurement layer.
    public static PulseOximeterMeasurement CreateIllustration(Guid plethChannel) => new(plethChannel,
        PulseOximeterIllustrationSource.RedChannelId, PulseOximeterIllustrationSource.InfraredChannelId,
        new(PulseOximeterIllustrationSource.ModelId, [new(400000, 100000), new(1600000, 70000)]));

    public PulseOximeterReading Consume(ReadOnlySpan<byte> wire)
    {
        var block = WaveformEnvelopeCodec.Decode(wire);
        var pleth = block.Planes.SingleOrDefault(p => p.ChannelId == _plethChannel);
        var red = block.Planes.SingleOrDefault(p => p.ChannelId == _redChannel);
        if (pleth is null || red is null || pleth.FirstSampleIndex != red.FirstSampleIndex || pleth.Samples.Count != red.Samples.Count)
        { throw new ArgumentException("PulseOximeter.UnpairedPleth", nameof(wire)); }
        var pulse = PlethPulseRateMeasurement.Restore(_pulse.Capture());
        var saturation = OpticalSaturationAcquisition.Restore(_saturation.Capture());
        pulse.Consume(wire);
        var reading = saturation.Consume(wire);
        var result = new PulseOximeterReading(pulse.Read(reading.MeasuredAtNs!.Value), reading);
        _pulse = pulse; _saturation = saturation;
        return result;
    }

    public PulseOximeterReading Read(long asOfSampleTimeNs) => new(_pulse.Read(asOfSampleTimeNs), _saturation.Read(asOfSampleTimeNs));
    public Checkpoint Capture() => new(_plethChannel, _redChannel, _pulse.Capture(), _saturation.Capture());
    public static PulseOximeterMeasurement Restore(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(checkpoint);
    }
    public sealed class Checkpoint
    {
        internal Guid Pleth { get; }
        internal Guid Red { get; }
        internal PlethPulseRateMeasurement.Checkpoint Pulse { get; }
        internal OpticalSaturationAcquisition.Checkpoint Saturation { get; }
        internal Checkpoint(Guid pleth, Guid red, PlethPulseRateMeasurement.Checkpoint pulse, OpticalSaturationAcquisition.Checkpoint saturation)
        { Pleth = pleth; Red = red; Pulse = pulse; Saturation = saturation; }
    }
}
