// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

// Owns the finite sample window for one explicitly bound sensor/calibration.
// It has no reference to simulation targets or generator configuration.
public sealed class OpticalSaturationAcquisition
{
    private readonly Guid _redChannel, _infraredChannel;
    private readonly OpticalSaturationMeasurement _measurement;
    private State _state = new();
    public OpticalSaturationAcquisition(Guid redChannel, Guid infraredChannel, OpticalSaturationMeasurement measurement)
    {
        if (redChannel == Guid.Empty || infraredChannel == Guid.Empty || redChannel == infraredChannel)
        { throw new ArgumentException("OpticalStream.InvalidChannels"); }
        ArgumentNullException.ThrowIfNull(measurement);
        _redChannel = redChannel; _infraredChannel = infraredChannel; _measurement = measurement;
    }

    public OpticalSaturationReading Consume(ReadOnlySpan<byte> wire)
    {
        var block = WaveformEnvelopeCodec.Decode(wire);
        var red = block.Planes.SingleOrDefault(p => p.ChannelId == _redChannel)
            ?? throw new ArgumentException("OpticalStream.RedMissing", nameof(wire));
        var infrared = block.Planes.SingleOrDefault(p => p.ChannelId == _infraredChannel)
            ?? throw new ArgumentException("OpticalStream.InfraredMissing", nameof(wire));
        if (red.SampleRateNumerator != 125 || infrared.SampleRateNumerator != 125 ||
            red.SampleRateDenominator != 1 || infrared.SampleRateDenominator != 1 ||
            red.Samples.Count is < 1 or > OpticalSaturationMeasurement.WindowSamples ||
            infrared.Samples.Count != red.Samples.Count || infrared.FirstSampleIndex != red.FirstSampleIndex ||
            block.StartSimTimeNs < 0 || (long)red.Samples.Count * OpticalSaturationMeasurement.SampleStepNs != block.DurationNs)
        { throw new ArgumentException("OpticalStream.UnpairedSampling", nameof(wire)); }
        long end = checked(block.StartSimTimeNs + block.DurationNs);
        ulong nextIndex = checked(red.FirstSampleIndex + (ulong)red.Samples.Count);
        var identity = new Identity(block.SessionId, block.InstanceId, block.TimebaseEpoch, block.StreamEpoch, block.ConfigurationRevision);
        if (_state.Identity is { } old && old.Session == identity.Session && old.Instance == identity.Instance &&
            (identity.Timebase < old.Timebase || identity.Timebase == old.Timebase &&
            (identity.Stream < old.Stream || identity.Stream == old.Stream && identity.Revision < old.Revision)))
        { throw new ArgumentException("OpticalStream.EpochRollback", nameof(wire)); }
        // A changed identity starts an independent stream. Same-stream overlaps
        // and duplicate delivery are rejected atomically, never replayed.
        bool same = _state.Identity == identity;
        bool sameClock = _state.Identity is { } previous && previous.Session == identity.Session && previous.Instance == identity.Instance &&
            previous.Timebase == identity.Timebase && previous.Stream == identity.Stream;
        if (sameClock && (block.StartSimTimeNs < _state.NextTime || block.BlockSequence <= _state.Sequence || red.FirstSampleIndex < _state.NextIndex))
        { throw new ArgumentException("OpticalStream.OutOfOrder", nameof(wire)); }
        var next = same && block.StartSimTimeNs == _state.NextTime && red.FirstSampleIndex == _state.NextIndex &&
            _state.Sequence != ulong.MaxValue && block.BlockSequence == _state.Sequence + 1 ? _state with { } : new State();
        List<OpticalSample> samples = new(next.Samples);
        for (int i = 0; i < red.Samples.Count; i++)
        {
            bool poor = Unusable(red, i) || Unusable(infrared, i);
            samples.Add(new(block.StartSimTimeNs + i * OpticalSaturationMeasurement.SampleStepNs,
                Convert(red, i), Convert(infrared, i), poor ? 1u : 0u));
        }
        next.Samples = samples.TakeLast(OpticalSaturationMeasurement.WindowSamples).ToArray();
        next.Identity = identity; next.NextTime = end; next.NextIndex = nextIndex; next.Sequence = block.BlockSequence;
        var reading = _measurement.Estimate(next.Samples, next.Samples[^1].SampleTimeNs);
        _state = next;
        return reading;
    }

    public OpticalSaturationReading Read(long asOfSampleTimeNs) => _measurement.Estimate(_state.Samples, asOfSampleTimeNs);
    public Checkpoint Capture() => new(_redChannel, _infraredChannel, _measurement, _state with { });
    public static OpticalSaturationAcquisition Restore(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(checkpoint.Red, checkpoint.Infrared, checkpoint.Measurement) { _state = checkpoint.Value with { } };
    }
    public sealed class Checkpoint
    {
        internal Guid Red { get; }
        internal Guid Infrared { get; }
        internal OpticalSaturationMeasurement Measurement { get; }
        internal State Value { get; }
        internal Checkpoint(Guid red, Guid infrared, OpticalSaturationMeasurement measurement, State state)
        { Red = red; Infrared = infrared; Measurement = measurement; Value = state; }
    }
    private static bool Unusable(WaveformPlane plane, int index) =>
        plane.Samples[index] is short.MinValue or short.MaxValue ||
        plane.QualityRanges.Any(r => (uint)index >= r.FirstSampleOffset && (ulong)index < (ulong)r.FirstSampleOffset + r.Count && r.QualityFlags != 0);
    private static int Convert(WaveformPlane plane, int index) => checked((int)FixedPointMath.RoundDivideTiesToEven(
        (Int128)plane.Samples[index] * plane.ScaleNumerator * plane.OffsetDenominator +
        (Int128)plane.OffsetNumerator * plane.ScaleDenominator,
        (Int128)plane.ScaleDenominator * plane.OffsetDenominator));
    internal sealed record Identity(Guid Session, Guid Instance, ulong Timebase, ulong Stream, ulong Revision);
    internal sealed record State
    {
        internal Identity? Identity;
        internal long NextTime;
        internal ulong NextIndex, Sequence;
        // Immutable records in an array replaced on every consume; checkpoints
        // share only immutable material, never a mutable ring exposed to callers.
        internal OpticalSample[] Samples = [];
    }
}
