// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

// Local preview runtime, not a replacement for the product authority gateway.
// Uses the same bounded pacing and 2.2s presentation buffer as the physiology demo.
public sealed class LocalMonitorPreviewSession
{
    public const long PresentationLatencyNs = 2_200_000_000;
    public const int RetainedBlockCount = 202;
    private PhysiologyWaveformGroup _source;
    private WaveformEnvelope[] _blocks = [];
    private readonly LiveWaveformMeasurements? _measurements;
    private PulseOximeterIllustrationSource? _opticalSource;
    private RealtimeOxygenationSource? _realtimeOxygenation;
    private readonly Guid _opticalInstanceId = Guid.NewGuid();
    private readonly int _opticalModulationPermille;
    private readonly bool _usesOxygenation;
    private long _measurementFrontier;
    private static readonly long AcquisitionLatencyNs = FrozenSignalAcquisitionProfiles.Get("AcqPleth125@1").LatencyNs;
    public LiveMeasurementSnapshot? Measurements => _measurements?.Read(Math.Max(_measurementFrontier,
        Math.Max(0, SimulationTimeNs - AcquisitionLatencyNs)));
    public long SimulationTimeNs { get; private set; }
    public long FrontierNs { get; private set; }
    public ulong DataRevision { get; private set; }
    public IReadOnlyList<DetectedPlethPulse> DetectedPulses { get; private set; } = [];
    public IReadOnlyList<DetectedEcgBeat> DetectedBeats { get; private set; } = [];
    public IReadOnlyList<WaveformEnvelope> Blocks => Array.AsReadOnly(_blocks);
    public MonitorDisplayConfiguration Display { get; }
    public MonitorSweepRanges Ranges { get; }
    public RealtimeOxygenationSnapshot? Oxygenation => _realtimeOxygenation?.Snapshot;
    public OxygenReservoirParameters? OxygenationParameters { get; }
    public LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration configuration, MonitorDisplayConfiguration display,
        bool enableMeasurements = false, int? opticalSaturationMilliPercent = null, int opticalModulationPermille = 1000,
        SeededOpticalSaturation? opticalVariation = null, IArterialOxygenationSource? oxygenation = null,
        RealtimeOxygenationConfiguration? realtimeOxygenation = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _source = PhysiologyIllustrationSource.Create(configuration);
        _opticalModulationPermille = opticalModulationPermille;
        if (realtimeOxygenation is not null)
        {
            if (oxygenation is not null) { throw new ArgumentException("Preview.ExclusiveOxygenationSources"); }
            var transport = PhysiologyIllustrationSource.CreateTransport(configuration, realtimeOxygenation.Ventilation,
                realtimeOxygenation.ReferenceStrokeVolumeMicroliters);
            _realtimeOxygenation = new(transport, realtimeOxygenation.Parameters, realtimeOxygenation.OxygenDemandMultiplier);
            OxygenationParameters = realtimeOxygenation.Parameters;
            oxygenation = _realtimeOxygenation;
        }
        if (opticalSaturationMilliPercent.HasValue && !enableMeasurements)
        { throw new ArgumentException("Preview.OpticsRequireMeasurements", nameof(opticalSaturationMilliPercent)); }
        if (opticalVariation is not null && opticalSaturationMilliPercent is null)
        { throw new ArgumentException("Preview.VariationRequiresOptics"); }
        if (oxygenation is not null && (!enableMeasurements || opticalSaturationMilliPercent is not null || opticalVariation is not null))
        { throw new ArgumentException("Preview.OxygenationRequiresExclusiveMeasuredOptics", nameof(oxygenation)); }
        _usesOxygenation = oxygenation is not null;
        if (enableMeasurements) { _measurements = LiveWaveformMeasurements.CreateIllustration(); }
        if (opticalSaturationMilliPercent is { } target)
        {
            _opticalSource = new(PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
                _opticalInstanceId, target, opticalModulationPermille, opticalVariation);
        }
        else if (oxygenation is not null)
        {
            _opticalSource = new(PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
                _opticalInstanceId, oxygenation, opticalModulationPermille);
        }
        Display = display; Ranges = new(display);
    }
    public long UpdateOxygenationVentilation(VentilationTransportPlan ventilation, decimal? oxygenDemandMultiplier = null) =>
        (_realtimeOxygenation ?? throw new InvalidOperationException("Preview.RealtimeOxygenationNotEnabled"))
            .ChangeVentilation(ventilation, SimulationTimeNs, oxygenDemandMultiplier);

    public void Advance(long deltaNs)
    {
        if (deltaNs is <= 0 or > 250_000_000) { throw new ArgumentOutOfRangeException(nameof(deltaNs)); }
        List<DetectedEcgBeat> beats = [];
        List<DetectedPlethPulse> pulses = [];
        DetectedPulses = [];
        DetectedBeats = [];
        while (deltaNs > 0)
        {
            long chunk = Math.Min(deltaNs, 50_000_000);
            long next = checked(SimulationTimeNs + chunk);
            var oxygenation = _realtimeOxygenation?.Fork();
            oxygenation?.AdvanceTo(next);
            var optics = oxygenation is null ? _opticalSource : new PulseOximeterIllustrationSource(
                PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
                _opticalInstanceId, oxygenation, _opticalModulationPermille);
            // Resolve all optical source-time reads before publishing the chunk.
            // Missing history/range errors leave acquisition and clocks retryable.
            PhysiologyWaveformGroup source = _usesOxygenation ? _source.Fork() : _source;
            var wires = source.AdvanceTo(next, 50, 1, 100);
            byte[][]? opticalWires = optics is null ? null : wires.Select(wire => optics.ConvertAcquiredPulse(wire)).ToArray();
            _source = source;
            _realtimeOxygenation = oxygenation;
            _opticalSource = optics;
            if (wires.Count > 0)
            {
                for (int index = 0; index < wires.Count; index++)
                {
                    byte[] wire = wires[index];
                    if (_measurements is null) { break; }
                    byte[] measurementWire = wire;
                    if (_opticalSource is not null)
                    {
                        var optical = WaveformEnvelopeCodec.Decode(opticalWires![index]);
                        var original = WaveformEnvelopeCodec.Decode(wire);
                        measurementWire = WaveformEnvelopeCodec.EncodeRaw(original with
                        {
                            InstanceId = optical.InstanceId,
                            Planes = original.Planes.Concat(optical.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(2))).ToArray()
                        });
                    }
                    var measured = _measurements.Consume(measurementWire, out var detected, out var detectedPulses);
                    _measurementFrontier = measured.SampleTimeNs;
                    if (measured.HeartRate.Status is WaveformMeasurementStatus.Valid or WaveformMeasurementStatus.WarmingUp)
                    { beats.AddRange(detected); }
                    else { beats.Clear(); }
                    if (measured.PulseRate.Status is WaveformMeasurementStatus.Valid or WaveformMeasurementStatus.WarmingUp)
                    { pulses.AddRange(detectedPulses); }
                    else { pulses.Clear(); }
                }
                _blocks = _blocks.Concat(wires.Select(b => WaveformEnvelopeCodec.Decode(b))).TakeLast(RetainedBlockCount).ToArray();
                DataRevision++;
            }
            SimulationTimeNs = next;
            FrontierNs = _blocks.Length == 0 ? 0 : Math.Max(FrontierNs,
                Math.Min(_blocks[^1].StartSimTimeNs + 200_000_000, Math.Max(0, next - PresentationLatencyNs)));
            Ranges.Advance(FrontierNs, (channel, from, to) => Samples(channel, from, to).Select(s => s.Value));
            deltaNs -= chunk;
        }
        DetectedPulses = Array.AsReadOnly(pulses.Where(p => _measurementFrontier - p.ConfirmedAtNs <= 250_000_000).ToArray());
        DetectedBeats = Array.AsReadOnly(beats.Where(b => _measurementFrontier - b.ConfirmedAtNs <= 250_000_000).ToArray());
    }
    public IEnumerable<(long TimeNs, double Value)> Samples(int channel, long fromSimTimeNs, long toExclusiveSimTimeNs)
    {
        Guid id = PhysiologyIllustrationSource.ChannelId(channel);
        foreach (var block in _blocks)
        {
            if (block.StartSimTimeNs + 200_000_000 <= fromSimTimeNs || block.StartSimTimeNs >= toExclusiveSimTimeNs) { continue; }
            var plane = block.Planes.Single(p => p.ChannelId == id);
            long step = checked(1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator);
            for (int i = 0; i < plane.Samples.Count; i++)
            {
                long time = block.StartSimTimeNs + i * step;
                if (time >= fromSimTimeNs && time < toExclusiveSimTimeNs)
                {
                    yield return (time, (double)plane.Samples[i] * plane.ScaleNumerator / plane.ScaleDenominator +
                        (double)plane.OffsetNumerator / plane.OffsetDenominator);
                }
            }
        }
    }
}
