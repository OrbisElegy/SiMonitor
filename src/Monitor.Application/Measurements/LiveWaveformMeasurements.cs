// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Application.Measurements;

public sealed record LiveMeasurementSnapshot(long SampleTimeNs, EcgHeartRateReading HeartRate,
    ImpedanceRespirationReading ImpedanceRespiration, PlethPulseRateReading PulseRate,
    CapnographyResult Capnography, OpticalSaturationReading SpO2, MeanPressureReading AbpMean,
    MeanPressureReading PaMean, MeanPressureReading CvpMean)
{
    public EcgMonitoringReading EcgMonitoring { get; init; } = EcgMonitoringReading.NoData;
    public EcgRhythmReading EcgRhythm { get; init; } = new(WaveformMeasurementStatus.NoData, null, null, null);
}

// One measurement owner for the local illustration channel binding. Live native
// sample batches and recorded packets share the same detectors and quality gates.
public sealed class LiveWaveformMeasurements
{
    private EcgHeartRateMeasurement _ecg;
    private ImpedanceRespirationMeasurement _resp = new(PhysiologyIllustrationSource.ChannelId(1));
    private PlethPulseRateMeasurement _pleth = new(PhysiologyIllustrationSource.ChannelId(2));
    private CapnographyMeasurement _co2 = new(PhysiologyIllustrationSource.ChannelId(4));
    private MeanPressureMeasurement _abp = new(PhysiologyIllustrationSource.ChannelId(3), detectPulse: true);
    private MeanPressureMeasurement _pa = new(PhysiologyIllustrationSource.ChannelId(5), detectPulse: true);
    private MeanPressureMeasurement _cvp = new(PhysiologyIllustrationSource.ChannelId(6));
    private OpticalSaturationAcquisition _optical;
    private readonly OpticalSaturationMeasurement _calibration;
    private long? _lastSampleTime;

    public LiveWaveformMeasurements(OpticalSaturationMeasurement calibration) : this(calibration, new EcgMonitoringSettings()) { }
    public LiveWaveformMeasurements(OpticalSaturationMeasurement calibration, EcgMonitoringSettings ecgSettings)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        _ecg = new(PhysiologyIllustrationSource.ChannelId(0), ecgSettings);
        _calibration = calibration;
        _optical = NewOptical();
    }
    public static LiveWaveformMeasurements CreateIllustration() => new(OpticalSaturationMeasurement.CreateIllustration());
    private OpticalSaturationAcquisition NewOptical() => new(PulseOximeterIllustrationSource.RedChannelId,
        PulseOximeterIllustrationSource.InfraredChannelId, _calibration);

    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire) => Consume(wire, out _);
    // Publish events only after the entire acquired packet commits; Read and
    // checkpoint restoration never replay a previous batch of cues.
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats)
        => Consume(wire, out detectedBeats, out _);
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats, out IReadOnlyList<DetectedPlethPulse> detectedPulses)
        => Consume(wire, out detectedBeats, out detectedPulses, out _);
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats,
        out IReadOnlyList<DetectedPlethPulse> detectedPulses, out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents)
        => Consume(wire, out detectedBeats, out detectedPulses, out rhythmEvents, out _);
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats,
        out IReadOnlyList<DetectedPlethPulse> detectedPulses, out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents,
        out IReadOnlyList<DetectedEcgMonitoringEvent> monitoringEvents, EcgPacingEvidence? pacingEvidence = null)
    {
        var block = WaveformEnvelopeCodec.Decode(wire);
        if (block.DurationNs != 200_000_000) { throw new ArgumentException("LiveMeasurement.BlockDuration", nameof(wire)); }
        long sampleTime = checked(block.StartSimTimeNs + block.DurationNs - 1);
        Dictionary<Guid, WaveformEnvelope> channels = [];
        for (int channel = 0; channel < 7; channel++)
        {
            Guid id = PhysiologyIllustrationSource.ChannelId(channel);
            if (block.Planes.All(plane => plane.ChannelId != id))
            { throw new ArgumentException("LiveMeasurement.ChannelMissing", nameof(wire)); }
            channels.Add(id, block);
        }
        return ConsumeBatch(channels, sampleTime, out detectedBeats, out detectedPulses, out rhythmEvents, out monitoringEvents, pacingEvidence);
    }

    // Each envelope has one native-rate channel; PLETH may include its paired
    // optical planes. Channels with no new sample in this frame are omitted.
    // Their next sample grid may lie beyond asOfSampleTimeNs, but no consumed
    // sample may do so. A failed channel leaves every detector unchanged.
    public LiveMeasurementSnapshot ConsumeSamples(IReadOnlyList<WaveformEnvelope> channelBlocks, long asOfSampleTimeNs,
        out IReadOnlyList<DetectedEcgBeat> detectedBeats, out IReadOnlyList<DetectedPlethPulse> detectedPulses,
        out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents, out IReadOnlyList<DetectedEcgMonitoringEvent> monitoringEvents,
        EcgPacingEvidence? pacingEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(channelBlocks);
        if (asOfSampleTimeNs < 0 || _lastSampleTime is { } last && asOfSampleTimeNs < last)
        { throw new ArgumentException("LiveMeasurement.TimeBeforeFrontier", nameof(asOfSampleTimeNs)); }
        Dictionary<Guid, WaveformEnvelope> channels = [];
        foreach (var block in channelBlocks)
        {
            ArgumentNullException.ThrowIfNull(block);
            var primary = block.Planes.Where(plane => plane.ChannelId != PulseOximeterIllustrationSource.RedChannelId &&
                plane.ChannelId != PulseOximeterIllustrationSource.InfraredChannelId).ToArray();
            if (primary.Length != 1 || !Enumerable.Range(0, 7).Any(channel => PhysiologyIllustrationSource.ChannelId(channel) == primary[0].ChannelId) ||
                !channels.TryAdd(primary[0].ChannelId, block))
            { throw new ArgumentException("LiveMeasurement.InvalidSampleChannels", nameof(channelBlocks)); }
            if (block.Planes.Count != 1 && primary[0].ChannelId != _pleth.ChannelId)
            { throw new ArgumentException("LiveMeasurement.UnpairedPleth", nameof(channelBlocks)); }
            foreach (var plane in block.Planes)
            {
                if (plane.SampleRateNumerator == 0 || plane.SampleRateDenominator == 0 || plane.Samples.Count == 0)
                { throw new ArgumentException("LiveMeasurement.InvalidSampling", nameof(channelBlocks)); }
                long lastSampleTime = checked(block.StartSimTimeNs + (long)((Int128)(plane.Samples.Count - 1) *
                    1_000_000_000 * plane.SampleRateDenominator / plane.SampleRateNumerator));
                if (lastSampleTime > asOfSampleTimeNs)
                { throw new ArgumentException("LiveMeasurement.FutureSample", nameof(channelBlocks)); }
            }
        }
        return ConsumeBatch(channels, asOfSampleTimeNs, out detectedBeats, out detectedPulses, out rhythmEvents, out monitoringEvents, pacingEvidence);
    }

    private LiveMeasurementSnapshot ConsumeBatch(Dictionary<Guid, WaveformEnvelope> channels, long sampleTime,
        out IReadOnlyList<DetectedEcgBeat> detectedBeats, out IReadOnlyList<DetectedPlethPulse> detectedPulses,
        out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents, out IReadOnlyList<DetectedEcgMonitoringEvent> monitoringEvents,
        EcgPacingEvidence? pacingEvidence)
    {
        detectedBeats = [];
        detectedPulses = [];
        rhythmEvents = [];
        monitoringEvents = [];
        bool hasPleth = channels.TryGetValue(_pleth.ChannelId, out var plethBlock);
        var red = plethBlock?.Planes.SingleOrDefault(plane => plane.ChannelId == PulseOximeterIllustrationSource.RedChannelId);
        var infrared = plethBlock?.Planes.SingleOrDefault(plane => plane.ChannelId == PulseOximeterIllustrationSource.InfraredChannelId);
        if ((red is null) != (infrared is null))
        { throw new ArgumentException("LiveMeasurement.IncompleteOptics", nameof(channels)); }
        if (red is not null)
        {
            var plethPlane = plethBlock!.Planes.Single(plane => plane.ChannelId == _pleth.ChannelId);
            if (plethPlane.FirstSampleIndex != red.FirstSampleIndex || plethPlane.Samples.Count != red.Samples.Count)
            { throw new ArgumentException("LiveMeasurement.UnpairedPleth", nameof(channels)); }
        }
        var ecg = EcgHeartRateMeasurement.Restore(_ecg.Capture());
        var resp = ImpedanceRespirationMeasurement.Restore(_resp.Capture());
        var pleth = PlethPulseRateMeasurement.Restore(_pleth.Capture());
        var co2 = CapnographyMeasurement.Restore(_co2.Capture());
        var abp = MeanPressureMeasurement.Restore(_abp.Capture());
        var pa = MeanPressureMeasurement.Restore(_pa.Capture());
        var cvp = MeanPressureMeasurement.Restore(_cvp.Capture());
        var optical = hasPleth && red is null ? NewOptical() : OpticalSaturationAcquisition.Restore(_optical.Capture());
        IReadOnlyList<DetectedEcgBeat> beats = [];
        IReadOnlyList<DetectedPlethPulse> pulses = [];
        IReadOnlyList<DetectedEcgRhythmEvent> transitions = [];
        IReadOnlyList<DetectedEcgMonitoringEvent> monitoring = [];
        Dictionary<WaveformEnvelope, byte[]> encoded = new(ReferenceEqualityComparer.Instance);
        byte[] Wire(WaveformEnvelope block)
        {
            if (!encoded.TryGetValue(block, out byte[]? wire))
            {
                wire = WaveformEnvelopeCodec.EncodeRaw(block);
                encoded.Add(block, wire);
            }
            return wire;
        }
        if (channels.TryGetValue(ecg.ChannelId, out var ecgBlock))
        { beats = ecg.Consume(Wire(ecgBlock), out transitions, out monitoring, pacingEvidence); }
        if (channels.TryGetValue(resp.ChannelId, out var respBlock)) { resp.Consume(Wire(respBlock)); }
        if (hasPleth)
        {
            byte[] plethWire = Wire(plethBlock!);
            pulses = pleth.Consume(plethWire);
            if (red is not null) { optical.Consume(plethWire); }
        }
        if (channels.TryGetValue(co2.ChannelId, out var co2Block)) { co2.Consume(Wire(co2Block)); }
        if (channels.TryGetValue(abp.ChannelId, out var abpBlock)) { abp.Consume(Wire(abpBlock)); }
        if (channels.TryGetValue(pa.ChannelId, out var paBlock)) { pa.Consume(Wire(paBlock)); }
        if (channels.TryGetValue(cvp.ChannelId, out var cvpBlock)) { cvp.Consume(Wire(cvpBlock)); }
        var snapshot = new LiveMeasurementSnapshot(sampleTime, ecg.Read(sampleTime), resp.Read(sampleTime),
            pleth.Read(sampleTime), co2.Read(sampleTime), optical.Read(sampleTime), abp.Read(sampleTime), pa.Read(sampleTime), cvp.Read(sampleTime))
        { EcgRhythm = ecg.ReadRhythm(sampleTime), EcgMonitoring = ecg.ReadMonitoring(sampleTime) };
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
        rhythmEvents = transitions;
        monitoringEvents = monitoring;
        return snapshot;
    }

    public LiveMeasurementSnapshot Read(long asOfSampleTimeNs)
    {
        if (_lastSampleTime is { } last && asOfSampleTimeNs < last)
        { throw new ArgumentException("LiveMeasurement.TimeBeforeFrontier", nameof(asOfSampleTimeNs)); }
        return new(asOfSampleTimeNs, _ecg.Read(asOfSampleTimeNs), _resp.Read(asOfSampleTimeNs),
            _pleth.Read(asOfSampleTimeNs), _co2.Read(asOfSampleTimeNs), _optical.Read(asOfSampleTimeNs),
            _abp.Read(asOfSampleTimeNs), _pa.Read(asOfSampleTimeNs), _cvp.Read(asOfSampleTimeNs))
        { EcgRhythm = _ecg.ReadRhythm(asOfSampleTimeNs), EcgMonitoring = _ecg.ReadMonitoring(asOfSampleTimeNs) };
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
