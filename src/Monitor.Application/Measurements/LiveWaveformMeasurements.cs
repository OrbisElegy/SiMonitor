// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Application.Measurements;

public sealed record LiveMeasurementSnapshot(long SampleTimeNs, EcgHeartRateReading HeartRate,
    ImpedanceRespirationReading ImpedanceRespiration, PlethPulseRateReading PulseRate,
    CapnographyResult Capnography, OpticalSaturationReading SpO2, MeanPressureReading AbpMean,
    MeanPressureReading PaMean, MeanPressureReading CvpMean);

// Serialized acquired-packet owner for the local illustration channel binding.
// Presentation/review never feeds this class. No generator target enters it.
public sealed class LiveWaveformMeasurements
{
    private EcgHeartRateMeasurement _ecg = new(PhysiologyIllustrationSource.ChannelId(0));
    private ImpedanceRespirationMeasurement _resp = new(PhysiologyIllustrationSource.ChannelId(1));
    private PlethPulseRateMeasurement _pleth = new(PhysiologyIllustrationSource.ChannelId(2));
    private CapnographyMeasurement _co2 = new(PhysiologyIllustrationSource.ChannelId(4));
    private MeanPressureMeasurement _abp = new(PhysiologyIllustrationSource.ChannelId(3), detectPulse: true);
    private MeanPressureMeasurement _pa = new(PhysiologyIllustrationSource.ChannelId(5), detectPulse: true);
    private MeanPressureMeasurement _cvp = new(PhysiologyIllustrationSource.ChannelId(6));
    private OpticalSaturationAcquisition _optical;
    private readonly OpticalSaturationMeasurement _calibration;
    private long? _lastSampleTime;

    public LiveWaveformMeasurements(OpticalSaturationMeasurement calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        _calibration = calibration;
        _optical = NewOptical();
    }
    public static LiveWaveformMeasurements CreateIllustration() => new(new(PulseOximeterIllustrationSource.ModelId,
        [new(400000, 100000), new(1600000, 70000)]));
    private OpticalSaturationAcquisition NewOptical() => new(PulseOximeterIllustrationSource.RedChannelId,
        PulseOximeterIllustrationSource.InfraredChannelId, _calibration);

    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire) => Consume(wire, out _);
    // Publish events only after the entire acquired packet commits; Read and
    // checkpoint restoration never replay a previous batch of cues.
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats)
        => Consume(wire, out detectedBeats, out _);
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats, out IReadOnlyList<DetectedPlethPulse> detectedPulses)
    {
        detectedBeats = [];
        detectedPulses = [];
        var block = WaveformEnvelopeCodec.Decode(wire);
        if (block.DurationNs != 200_000_000) { throw new ArgumentException("LiveMeasurement.BlockDuration", nameof(wire)); }
        long sampleTime = checked(block.StartSimTimeNs + block.DurationNs - 1);
        var red = block.Planes.SingleOrDefault(p => p.ChannelId == PulseOximeterIllustrationSource.RedChannelId);
        var infrared = block.Planes.SingleOrDefault(p => p.ChannelId == PulseOximeterIllustrationSource.InfraredChannelId);
        if ((red is null) != (infrared is null)) { throw new ArgumentException("LiveMeasurement.IncompleteOptics", nameof(wire)); }
        if (red is not null)
        {
            var plethPlane = block.Planes.SingleOrDefault(p => p.ChannelId == _pleth.ChannelId);
            if (plethPlane is null || plethPlane.FirstSampleIndex != red.FirstSampleIndex || plethPlane.Samples.Count != red.Samples.Count)
            { throw new ArgumentException("LiveMeasurement.UnpairedPleth", nameof(wire)); }
        }
        var ecg = EcgHeartRateMeasurement.Restore(_ecg.Capture());
        var resp = ImpedanceRespirationMeasurement.Restore(_resp.Capture());
        var pleth = PlethPulseRateMeasurement.Restore(_pleth.Capture());
        var co2 = CapnographyMeasurement.Restore(_co2.Capture());
        var abp = MeanPressureMeasurement.Restore(_abp.Capture());
        var pa = MeanPressureMeasurement.Restore(_pa.Capture());
        var cvp = MeanPressureMeasurement.Restore(_cvp.Capture());
        var optical = red is null ? NewOptical() : OpticalSaturationAcquisition.Restore(_optical.Capture());
        var beats = ecg.Consume(wire);
        resp.Consume(wire);
        var pulses = pleth.Consume(wire);
        co2.Consume(wire);
        if (red is not null) { optical.Consume(wire); }
        abp.Consume(wire);
        pa.Consume(wire);
        cvp.Consume(wire);
        var snapshot = new LiveMeasurementSnapshot(sampleTime, ecg.Read(sampleTime), resp.Read(sampleTime),
            pleth.Read(sampleTime), co2.Read(sampleTime), optical.Read(sampleTime), abp.Read(sampleTime), pa.Read(sampleTime), cvp.Read(sampleTime));
        _ecg = ecg;
        _resp = resp;
        _pleth = pleth;
        _co2 = co2;
        _optical = optical;
        _lastSampleTime = sampleTime;
        _abp = abp;
        _pa = pa;
        _cvp = cvp;
        detectedBeats = beats;
        detectedPulses = pulses;
        return snapshot;
    }

    public LiveMeasurementSnapshot Read(long asOfSampleTimeNs)
    {
        if (_lastSampleTime is { } last && asOfSampleTimeNs < last)
        { throw new ArgumentException("LiveMeasurement.TimeBeforeFrontier", nameof(asOfSampleTimeNs)); }
        return new(asOfSampleTimeNs, _ecg.Read(asOfSampleTimeNs), _resp.Read(asOfSampleTimeNs),
            _pleth.Read(asOfSampleTimeNs), _co2.Read(asOfSampleTimeNs), _optical.Read(asOfSampleTimeNs),
            _abp.Read(asOfSampleTimeNs), _pa.Read(asOfSampleTimeNs), _cvp.Read(asOfSampleTimeNs));
    }

    public Checkpoint Capture() => new(_calibration, _ecg.Capture(), _resp.Capture(), _pleth.Capture(), _co2.Capture(), _optical.Capture(),
        _abp.Capture(), _pa.Capture(), _cvp.Capture(), _lastSampleTime);
    public static LiveWaveformMeasurements Restore(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(checkpoint.Calibration)
        {
            _ecg = EcgHeartRateMeasurement.Restore(checkpoint.Ecg),
            _resp = ImpedanceRespirationMeasurement.Restore(checkpoint.Resp),
            _pleth = PlethPulseRateMeasurement.Restore(checkpoint.Pleth),
            _co2 = CapnographyMeasurement.Restore(checkpoint.Co2),
            _optical = OpticalSaturationAcquisition.Restore(checkpoint.Optical),
            _abp = MeanPressureMeasurement.Restore(checkpoint.Abp),
            _pa = MeanPressureMeasurement.Restore(checkpoint.Pa),
            _cvp = MeanPressureMeasurement.Restore(checkpoint.Cvp),
            _lastSampleTime = checkpoint.LastSampleTime
        };
    }
    public sealed class Checkpoint
    {
        internal OpticalSaturationMeasurement Calibration { get; }
        internal EcgHeartRateMeasurement.Checkpoint Ecg { get; }
        internal ImpedanceRespirationMeasurement.Checkpoint Resp { get; }
        internal PlethPulseRateMeasurement.Checkpoint Pleth { get; }
        internal CapnographyMeasurement.Checkpoint Co2 { get; }
        internal OpticalSaturationAcquisition.Checkpoint Optical { get; }
        internal MeanPressureMeasurement.Checkpoint Abp { get; }
        internal MeanPressureMeasurement.Checkpoint Pa { get; }
        internal MeanPressureMeasurement.Checkpoint Cvp { get; }
        internal long? LastSampleTime { get; }
        internal Checkpoint(OpticalSaturationMeasurement calibration, EcgHeartRateMeasurement.Checkpoint ecg,
            ImpedanceRespirationMeasurement.Checkpoint resp, PlethPulseRateMeasurement.Checkpoint pleth,
            CapnographyMeasurement.Checkpoint co2, OpticalSaturationAcquisition.Checkpoint optical,
            MeanPressureMeasurement.Checkpoint abp, MeanPressureMeasurement.Checkpoint pa, MeanPressureMeasurement.Checkpoint cvp, long? last)
        {
            Calibration = calibration;
            Ecg = ecg;
            Resp = resp;
            Pleth = pleth;
            Co2 = co2;
            Optical = optical;
            Abp = abp;
            Pa = pa;
            Cvp = cvp;
            LastSampleTime = last;
        }
    }
}
