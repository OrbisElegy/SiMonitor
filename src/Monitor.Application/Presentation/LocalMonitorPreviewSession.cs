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
    public const long StartupDiscardNs = 12_000_000_000;
    private long _sourceTimeOffsetNs;
    private PhysiologyIllustrationConfiguration _configuration;
    private readonly RealtimeOxygenationConfiguration? _realtimeConfiguration;
    private LocalMonitorPreviewSession? _pendingSource;
    private readonly List<(long ToExclusiveSourceTimeNs, PulseOximeterIllustrationSource? Source)> _previousOpticalSources = [];
    private readonly List<(long ToExclusiveSourceTimeNs, PhysiologyIllustrationConfiguration Configuration)> _previousPacingSources = [];
    public long? PendingSourceTimeNs { get; private set; }
    private PhysiologyWaveformGroup _source;
    private WaveformEnvelope[] _blocks = [];
    private readonly LiveWaveformMeasurements? _measurements;
    private PulseOximeterIllustrationSource? _opticalSource;
    private RealtimeOxygenationSource? _realtimeOxygenation;
    private PhysiologyWaveformGroup? _pendingVentilationSource;
    private long _pendingVentilationTimeNs;
    private readonly Guid _opticalInstanceId = Guid.NewGuid();
    private int _opticalModulationPermille;
    private bool _usesOxygenation;
    private long _measurementFrontier;
    private static readonly long AcquisitionLatencyNs = FrozenSignalAcquisitionProfiles.Get("AcqPleth125@1").LatencyNs;
    public LiveMeasurementSnapshot? Measurements => _measurements?.Read(Math.Max(_measurementFrontier,
        Math.Max(0, SimulationTimeNs - AcquisitionLatencyNs)));
    public ManualVitalSigns ManualVitals { get; private set; }
    public long SimulationTimeNs { get; private set; }
    public long FrontierNs { get; private set; }
    public ulong DataRevision { get; private set; }
    public IReadOnlyList<DetectedPlethPulse> DetectedPulses { get; private set; } = [];
    public IReadOnlyList<DetectedEcgMonitoringEvent> DetectedMonitoringEvents { get; private set; } = [];
    public IReadOnlyList<DetectedEcgRhythmEvent> DetectedRhythmEvents { get; private set; } = [];
    public IReadOnlyList<DetectedEcgBeat> DetectedBeats { get; private set; } = [];
    public IReadOnlyList<WaveformEnvelope> Blocks => Array.AsReadOnly(_blocks);
    public MonitorDisplayConfiguration Display { get; private set; }
    public MonitorSweepRanges Ranges { get; private set; }
    public RealtimeOxygenationSnapshot? Oxygenation => _realtimeOxygenation?.Snapshot is { } snapshot
        ? snapshot with { SourceSimTimeNs = snapshot.SourceSimTimeNs - _sourceTimeOffsetNs } : null;
    public OxygenReservoirParameters? OxygenationParameters { get; private set; }
    public LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration configuration, MonitorDisplayConfiguration display,
        bool enableMeasurements = false, int? opticalSaturationMilliPercent = null, int opticalModulationPermille = 1000,
        SeededOpticalSaturation? opticalVariation = null, IArterialOxygenationSource? oxygenation = null,
        RealtimeOxygenationConfiguration? realtimeOxygenation = null, ManualVitalSigns? manualVitals = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ManualVitals = manualVitals ?? ManualVitalSigns.Empty;
        _configuration = configuration;
        _realtimeConfiguration = realtimeOxygenation;
        _source = PhysiologyIllustrationSource.Create(configuration, ventilation: realtimeOxygenation?.Ventilation);
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
    public long UpdateOxygenationVentilation(VentilationTransportPlan ventilation, decimal? oxygenDemandMultiplier = null)
    {
        var oxygenation = (_realtimeOxygenation ?? throw new InvalidOperationException("Preview.RealtimeOxygenationNotEnabled")).Fork();
        long effectiveNs = oxygenation.ChangeVentilation(ventilation, checked(SimulationTimeNs + _sourceTimeOffsetNs), oxygenDemandMultiplier);
        var definition = PhysiologyIllustrationSource.Create(_configuration, ventilation: ventilation);
        var trial = _source.Fork();
        trial.ContinueWith(definition);
        // Publish both pending changes only after validating the full edit.
        _realtimeOxygenation = oxygenation;
        _pendingVentilationSource = definition;
        _pendingVentilationTimeNs = effectiveNs;
        return effectiveNs - _sourceTimeOffsetNs;
    }

    private void ActivatePendingVentilation()
    {
        if (_pendingVentilationSource is null) { return; }
        var source = _source.Fork();
        source.ContinueWith(_pendingVentilationSource);
        _source = source;
        _pendingVentilationSource = null;
    }

    public void DiscardStartup()
    {
        if (SimulationTimeNs != 0 || _sourceTimeOffsetNs != 0 || _pendingSource is not null)
        { throw new InvalidOperationException("Preview.StartupAlreadyStarted"); }
        var source = _source.Fork();
        if (_pendingVentilationSource is not null) { source.ContinueWith(_pendingVentilationSource); }
        var oxygenation = _realtimeOxygenation?.Fork();
        for (long time = 50_000_000; time <= StartupDiscardNs; time += 50_000_000)
        {
            oxygenation?.AdvanceTo(time);
            source.AdvanceTo(time, 50, 1, 100);
        }
        _source = source;
        _realtimeOxygenation = oxygenation;
        _pendingVentilationSource = null;
        _sourceTimeOffsetNs = StartupDiscardNs;
    }

    public long ScheduleSource(LocalMonitorPreviewSession definition, long delayNs)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (ReferenceEquals(definition, this) || definition.SimulationTimeNs != 0 || definition._sourceTimeOffsetNs != 0 ||
            definition._pendingSource is not null || definition._pendingVentilationSource is not null ||
            (definition._measurements is null) != (_measurements is null))
        { throw new ArgumentException("Preview.SourceMustBeFresh", nameof(definition)); }
        if (delayNs is < 0 or > 60_000_000_000) { throw new ArgumentOutOfRangeException(nameof(delayNs)); }
        if (_realtimeOxygenation is not null && definition._realtimeOxygenation is not null &&
            OxygenationParameters != definition.OxygenationParameters)
        { throw new ArgumentException("Preview.OxygenationBaselineRequiresRestart"); }
        long effective = checked((SimulationTimeNs + delayNs + 199_999_999) / 200_000_000 * 200_000_000);
        // Validate against the current source without generating future samples.
        var trial = _source.Fork();
        trial.ContinueWith(definition._source);
        _pendingSource = definition;
        PendingSourceTimeNs = effective;
        return effective;
    }

    public void UpdateDisplay(MonitorDisplayConfiguration display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (Display.Slots.SequenceEqual(display.Slots)) { return; }
        Display = display;
        Ranges = new(display);
        Ranges.Advance(FrontierNs, (channel, from, to) => Samples(channel, from, to).Select(s => s.Value));
    }

    private void ActivatePendingSource()
    {
        var definition = _pendingSource!;
        long sourceTime = checked(SimulationTimeNs + _sourceTimeOffsetNs);
        var source = _source.Fork();
        source.ContinueWith(definition._source);
        RealtimeOxygenationSource? oxygenation = null;
        if (definition._realtimeConfiguration is { } config)
        {
            var transport = PhysiologyIllustrationSource.CreateTransport(definition._configuration,
                config.Ventilation, config.ReferenceStrokeVolumeMicroliters);
            oxygenation = _realtimeOxygenation?.Fork() ?? new(transport, config.Parameters, config.OxygenDemandMultiplier, sourceTime);
            oxygenation.ChangeTransport(transport, sourceTime, config.OxygenDemandMultiplier);
        }
        _previousOpticalSources.Add((sourceTime, _opticalSource));
        _previousPacingSources.Add((sourceTime, _configuration));
        _source = source;
        _realtimeOxygenation = oxygenation;
        _configuration = definition._configuration;
        ManualVitals = definition.ManualVitals;
        _pendingVentilationSource = null;
        _opticalSource = definition._opticalSource;
        _opticalModulationPermille = definition._opticalModulationPermille;
        _usesOxygenation = definition._usesOxygenation;
        OxygenationParameters = definition.OxygenationParameters;
        _pendingSource = null;
        PendingSourceTimeNs = null;
    }

    private WaveformEnvelope Rebase(WaveformEnvelope block) => block with
    {
        InstanceId = _opticalInstanceId,
        StartSimTimeNs = checked(block.StartSimTimeNs - _sourceTimeOffsetNs),
        Planes = block.Planes.Select(p => p with
        {
            FirstSampleIndex = checked(p.FirstSampleIndex - (ulong)(_sourceTimeOffsetNs /
                (1_000_000_000L * p.SampleRateDenominator / p.SampleRateNumerator)))
        }).ToArray()
    };

    public void Advance(long deltaNs)
    {
        if (deltaNs is <= 0 or > 250_000_000) { throw new ArgumentOutOfRangeException(nameof(deltaNs)); }
        List<DetectedEcgBeat> beats = [];
        List<DetectedEcgMonitoringEvent> monitoringEvents = [];
        DetectedMonitoringEvents = [];
        List<DetectedEcgRhythmEvent> rhythmEvents = [];
        DetectedRhythmEvents = [];
        List<DetectedPlethPulse> pulses = [];
        DetectedPulses = [];
        DetectedBeats = [];
        while (deltaNs > 0)
        {
            if (PendingSourceTimeNs is { } effective && SimulationTimeNs >= effective) { ActivatePendingSource(); }
            long sourceTime = checked(SimulationTimeNs + _sourceTimeOffsetNs);
            if (_pendingVentilationSource is not null && sourceTime >= _pendingVentilationTimeNs) { ActivatePendingVentilation(); }
            long chunk = Math.Min(deltaNs, 50_000_000);
            if (PendingSourceTimeNs is { } pending) { chunk = Math.Min(chunk, pending - SimulationTimeNs); }
            if (_pendingVentilationSource is not null) { chunk = Math.Min(chunk, _pendingVentilationTimeNs - sourceTime); }
            long next = checked(SimulationTimeNs + chunk);
            var oxygenation = _realtimeOxygenation?.Fork();
            long sourceNext = checked(next + _sourceTimeOffsetNs);
            oxygenation?.AdvanceTo(sourceNext);
            var optics = oxygenation is null ? _opticalSource : new PulseOximeterIllustrationSource(
                PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
                _opticalInstanceId, oxygenation, _opticalModulationPermille);
            // Resolve all optical source-time reads before publishing the chunk.
            // Missing history/range errors leave acquisition and clocks retryable.
            PhysiologyWaveformGroup source = _usesOxygenation ? _source.Fork() : _source;
            byte[][] wires = source.AdvanceTo(sourceNext, 50, 1, 100)
                .Where(w => WaveformEnvelopeCodec.Decode(w).StartSimTimeNs >= _sourceTimeOffsetNs).ToArray();
            byte[]?[] opticalWires = wires.Select(wire =>
            {
                long start = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs;
                foreach (var previous in _previousOpticalSources)
                {
                    if (start < previous.ToExclusiveSourceTimeNs) { return previous.Source?.ConvertAcquiredPulse(wire); }
                }
                return optics?.ConvertAcquiredPulse(wire);
            }).ToArray();
            _source = source;
            _realtimeOxygenation = oxygenation;
            _opticalSource = optics;
            if (wires.Length > 0)
            {
                for (int index = 0; index < wires.Length; index++)
                {
                    byte[] wire = wires[index];
                    if (_measurements is null) { break; }
                    byte[] measurementWire = WaveformEnvelopeCodec.EncodeRaw(Rebase(WaveformEnvelopeCodec.Decode(wire)));
                    if (opticalWires[index] is { } opticalWire)
                    {
                        var optical = WaveformEnvelopeCodec.Decode(opticalWire);
                        var original = WaveformEnvelopeCodec.Decode(wire);
                        measurementWire = WaveformEnvelopeCodec.EncodeRaw(Rebase(original with
                        {
                            InstanceId = optical.InstanceId,
                            Planes = original.Planes.Concat(optical.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(2))).ToArray()
                        }));
                    }
                    var measured = _measurements.Consume(measurementWire, out var detected, out var detectedPulses, out var detectedRhythmEvents, out var detectedMonitoringEvents, PacingEvidence(WaveformEnvelopeCodec.Decode(wire)));
                    monitoringEvents.AddRange(detectedMonitoringEvents);
                    rhythmEvents.AddRange(detectedRhythmEvents);
                    _measurementFrontier = measured.SampleTimeNs;
                    if (measured.HeartRate.Status is WaveformMeasurementStatus.Valid or WaveformMeasurementStatus.WarmingUp)
                    { beats.AddRange(detected); }
                    else { beats.Clear(); }
                    if (measured.PulseRate.Status is WaveformMeasurementStatus.Valid or WaveformMeasurementStatus.WarmingUp)
                    { pulses.AddRange(detectedPulses); }
                    else { pulses.Clear(); }
                }
                _blocks = _blocks.Concat(wires.Select(b => Rebase(WaveformEnvelopeCodec.Decode(b)))).TakeLast(RetainedBlockCount).ToArray();
                long acquiredEnd = WaveformEnvelopeCodec.Decode(wires[^1]).StartSimTimeNs + 200_000_000;
                _previousOpticalSources.RemoveAll(p => p.ToExclusiveSourceTimeNs <= acquiredEnd);
                _previousPacingSources.RemoveAll(p => p.ToExclusiveSourceTimeNs <= acquiredEnd);
                DataRevision++;
            }
            SimulationTimeNs = next;
            FrontierNs = _blocks.Length == 0 ? 0 : Math.Max(FrontierNs,
                Math.Min(_blocks[^1].StartSimTimeNs + 200_000_000, Math.Max(0, next - PresentationLatencyNs)));
            Ranges.Advance(FrontierNs, (channel, from, to) => Samples(channel, from, to).Select(s => s.Value));
            deltaNs -= chunk;
        }
        DetectedRhythmEvents = rhythmEvents.AsReadOnly();
        DetectedMonitoringEvents = monitoringEvents.AsReadOnly();
        DetectedPulses = Array.AsReadOnly(pulses.Where(p => _measurementFrontier - p.ConfirmedAtNs <= 250_000_000).ToArray());
        DetectedBeats = Array.AsReadOnly(beats.Where(b => _measurementFrontier - b.ConfirmedAtNs <= 250_000_000).ToArray());
    }
    private EcgPacingEvidence? PacingEvidence(WaveformEnvelope block)
    {
        var configuration = _configuration;
        foreach (var previous in _previousPacingSources)
        {
            if (block.StartSimTimeNs >= previous.ToExclusiveSourceTimeNs) { continue; }
            configuration = previous.Configuration;
            break;
        }
        if (configuration.Pacing is not { } mode) { return null; }
        var timeline = RegularPhysiologyTimeline.Restore(new(configuration.ResolvePlan(), block.StartSimTimeNs));
        var events = timeline.AdvanceBefore(checked(block.StartSimTimeNs + block.DurationNs), 100);
        long[] Times(PhysiologyCycleEventKind kind) => events.Where(e => e.Kind == kind)
            .Select(e => e.SimTimeNs - _sourceTimeOffsetNs).ToArray();
        bool ventricular = mode != PacingIllustration.AtrialAai;
        return new(Times(PhysiologyCycleEventKind.VentricularPacingPulse))
        {
            AtrialPulseTimesNs = Times(PhysiologyCycleEventKind.AtrialPacingPulse),
            Origin = EcgPacingEvidenceOrigin.Simulation,
            VentricularPacingExpected = ventricular,
            ExpectedVentricularIntervalNs = ventricular ? PacingReference.Timing(mode).RrIntervalNs : null
        };
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
