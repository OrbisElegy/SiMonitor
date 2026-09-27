// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Authoring;

// Authored optical illustration, not human tissue calibration. Reuses the
// acquired peripheral pulse (including missing/weak ejections) as modulation.
// No extra time shift, resampling or display interpolation is introduced.
public sealed class PulseOximeterIllustrationSource
{
    public const string ModelId = "PulseOximeterLinearIllustration@1";
    public static Guid RedChannelId { get; } = Guid.Parse("88888888-8888-4888-8888-888888888888");
    public static Guid InfraredChannelId { get; } = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private readonly Guid _plethChannel, _acquisitionInstance, _sensorInstance;
    private readonly int _ratioPpm;
    private readonly int _modulationPermille;
    private readonly SeededOpticalSaturation? _variation;

    // Use a NEW instance ID when changing the optical model/target. Acquisition
    // configuration changes within that instance retain their input revisions.
    public PulseOximeterIllustrationSource(Guid plethChannel, Guid acquisitionInstance, Guid sensorInstance, int saturationMilliPercent, int modulationPermille = 1000, SeededOpticalSaturation? variation = null)
    {
        if (plethChannel == Guid.Empty || acquisitionInstance == Guid.Empty || sensorInstance == Guid.Empty || sensorInstance == acquisitionInstance ||
            plethChannel == RedChannelId || plethChannel == InfraredChannelId ||
            saturationMilliPercent is < 75000 or > 100000 || modulationPermille is < 100 or > 2000)
        { throw new ArgumentException("OpticalSource.InvalidConfiguration"); }
        if (variation is not null && variation.TargetMilliPercent != saturationMilliPercent)
        { throw new ArgumentException("OpticalSource.VariationTargetMismatch"); }
        _variation = variation;
        _plethChannel = plethChannel; _acquisitionInstance = acquisitionInstance; _sensorInstance = sensorInstance;
        // Educational curve S%=110-25R; deliberately not a clinical calibration.
        _ratioPpm = (110000 - saturationMilliPercent) * 40;
        _modulationPermille = modulationPermille;
    }

    public byte[] ConvertAcquiredPulse(ReadOnlySpan<byte> wire)
    {
        var block = WaveformEnvelopeCodec.Decode(wire);
        if (block.InstanceId != _acquisitionInstance)
        { throw new ArgumentException("OpticalSource.AcquisitionInstanceMismatch", nameof(wire)); }
        var pulse = block.Planes.SingleOrDefault(p => p.ChannelId == _plethChannel)
            ?? throw new ArgumentException("OpticalSource.PlethMissing", nameof(wire));
        if (pulse.SampleRateNumerator != 125 || pulse.SampleRateDenominator != 1 || pulse.Samples.Count == 0 ||
            block.StartSimTimeNs < 0 || (long)pulse.Samples.Count * 8_000_000 != block.DurationNs)
        { throw new ArgumentException("OpticalSource.UnsupportedSampling", nameof(wire)); }
        _ = checked(block.StartSimTimeNs + block.DurationNs);
        short[] red = new short[pulse.Samples.Count], infrared = new short[pulse.Samples.Count];
        for (int i = 0; i < pulse.Samples.Count; i++)
        {
            if (pulse.Samples[i] is short.MinValue or short.MaxValue)
            { red[i] = infrared[i] = 0; continue; }
            int modulation = checked((int)FixedPointMath.RoundDivideTiesToEven(
                (Int128)pulse.Samples[i] * pulse.ScaleNumerator * pulse.OffsetDenominator +
                (Int128)pulse.OffsetNumerator * pulse.ScaleDenominator,
                (Int128)pulse.ScaleDenominator * pulse.OffsetDenominator));
            if (modulation is < -10000 or > 10000)
            { throw new ArgumentException("OpticalSource.ModulationOutOfRange", nameof(wire)); }
            // DC(red)=16000, DC(IR)=20000; a1000-count pulse modulates IR2%.
            // Pulsatile absorption reduces transmitted light on both channels.
            int ratio = _variation is null ? _ratioPpm : (110000 - _variation.At(block.StartSimTimeNs + i * 8_000_000L)) * 40;
            // A0.2pp source-side guard band keeps finite-DC/ADC error at100%
            // inside the illustration calibration. Never relax estimator quality gates.
            ratio = Math.Max(ratio, 408000);
            red[i] = checked((short)(16000 - FixedPointMath.RoundDivideTiesToEven((Int128)modulation * 32 * ratio * _modulationPermille, 100_000_000_000)));
            infrared[i] = checked((short)(20000 - FixedPointMath.RoundDivideTiesToEven((Int128)modulation * 2 * _modulationPermille, 5000)));
        }
        WaveformPlane Optical(Guid channel, short[] values) => new(channel, 125, 1, pulse.FirstSampleIndex,
            1, 1, 0, 1, pulse.QualityEncoding, values, pulse.QualityRanges);
        return WaveformEnvelopeCodec.EncodeRaw(block with
        {
            InstanceId = _sensorInstance,
            Planes = [pulse, Optical(RedChannelId, red), Optical(InfraredChannelId, infrared)]
        });
    }
}
