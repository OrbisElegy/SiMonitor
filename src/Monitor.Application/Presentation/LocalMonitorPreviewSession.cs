// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Application.Presentation;

// Local preview runtime, not a replacement for the product authority gateway.
// Live traces share committed source time. The original delayed acquisition path
// remains available separately for measurements and recorded waveform packets.
public sealed class LocalMonitorPreviewSession
{
    public const long PresentationLatencyNs = 2_200_000_000;
    public const int RetainedBlockCount = 202;
    public const long StartupDiscardNs = 12_000_000_000;
    private long _sourceTimeOffsetNs;
    private PhysiologyIllustrationConfiguration _configuration;
    private RealtimeOxygenationConfiguration? _realtimeConfiguration;
    private PhysiologyIllustrationConfiguration? _beforePacing;
    public PacingIllustration? ActivePacing => _beforePacing is null ? null : _configuration.Pacing;
    public bool PacingAllowed => ElectricalTherapy is { PacingAllowed: true } profile &&
        EcgPacingPermissions.TemplateIds.Contains(profile.TemplateId, StringComparer.Ordinal);
    private LocalMonitorPreviewSession? _pendingSource;
    private (LocalMonitorPreviewSession Definition, long EffectiveNs)? _postShockSinus;
    private readonly List<DefibrillationEcgArtifact> _shockArtifacts = [];
    public IReadOnlyList<DefibrillationEcgArtifact> ShockArtifacts => _shockArtifacts.AsReadOnly();
    private readonly List<(long ToExclusiveSourceTimeNs, PulseOximeterIllustrationSource? Source)> _previousOpticalSources = [];
    private readonly List<(long ToExclusiveSourceTimeNs, PhysiologyIllustrationConfiguration Configuration)> _previousPacingSources = [];
    private ulong _lastElectricalDeliverySequence;
    public PhysiologyIllustrationConfiguration Configuration => _configuration;
    public EcgElectricalTherapyProfile? ElectricalTherapy { get; private set; }
    public long? PendingSourceTimeNs { get; private set; }
    private PhysiologyWaveformGroup _source;
    private WaveformEnvelope[] _blocks = [];
    private readonly Dictionary<Guid, List<(long TimeNs, double Value, uint QualityFlags)>> _immediateSamples;
    private readonly Dictionary<Guid, WaveformBlockPlaneConfiguration> _planeConfigurations;
    private readonly LiveWaveformMeasurements? _measurements;
    private readonly EcgHeartRateMeasurement? _synchronizationEcg;
    private ulong _synchronizationSequence;
    public IReadOnlyList<DetectedEcgBeat> SynchronizationBeats { get; private set; } = [];
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
    public long PresentationFrontierNs(int channel)
    {
        _ = PhysiologyIllustrationSource.ChannelId(channel);
        return SimulationTimeNs;
    }
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
        RealtimeOxygenationConfiguration? realtimeOxygenation = null, ManualVitalSigns? manualVitals = null,
        EcgElectricalTherapyProfile? electricalTherapy = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        electricalTherapy?.Validate();
        ElectricalTherapy = electricalTherapy;
        ManualVitals = manualVitals ?? ManualVitalSigns.Empty;
        _configuration = configuration;
        _realtimeConfiguration = realtimeOxygenation;
        _source = PhysiologyIllustrationSource.Create(configuration, ventilation: realtimeOxygenation?.Ventilation);
        _planeConfigurations = _source.CaptureState().Assembler.Planes.ToDictionary(plane => plane.Configuration.ChannelId, plane => plane.Configuration);
        _immediateSamples = _planeConfigurations.Keys.ToDictionary(id => id, _ => new List<(long TimeNs, double Value, uint QualityFlags)>());
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
        if (enableMeasurements)
        {
            _measurements = LiveWaveformMeasurements.CreateIllustration();
            _synchronizationEcg = new(PhysiologyIllustrationSource.ChannelId(0));
        }
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

    private void ValidateSourceDefinition(LocalMonitorPreviewSession definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (ReferenceEquals(definition, this) || definition.SimulationTimeNs != 0 || definition._sourceTimeOffsetNs != 0 ||
            definition._pendingSource is not null || definition._pendingVentilationSource is not null ||
            (definition._measurements is null) != (_measurements is null))
        { throw new ArgumentException("Preview.SourceMustBeFresh", nameof(definition)); }
        if (_realtimeOxygenation is not null && definition._realtimeOxygenation is not null &&
            OxygenationParameters != definition.OxygenationParameters)
        { throw new ArgumentException("Preview.OxygenationBaselineRequiresRestart"); }
        var trial = _source.Fork();
        trial.ContinueWith(definition._source);
    }

    public long ScheduleSource(LocalMonitorPreviewSession definition, long delayNs)
    {
        ValidateSourceDefinition(definition);
        if (delayNs is < 0 or > 60_000_000_000) { throw new ArgumentOutOfRangeException(nameof(delayNs)); }
        long effective = checked((SimulationTimeNs + delayNs + 199_999_999) / 200_000_000 * 200_000_000);
        // Explicit source changes replace the entire pending post-shock sequence.
        _beforePacing = null;
        _postShockSinus = null;
        _pendingSource = definition;
        PendingSourceTimeNs = effective;
        return effective;
    }

    public long SchedulePacing(PacingIllustration mode, PacingOutputSettings output)
    {
        if (!PacingAllowed) { throw new InvalidOperationException("Pacing.TemplateDisabled"); }
        if (_pendingSource is not null || _pendingVentilationSource is not null)
        { throw new InvalidOperationException("Pacing.SourceChangePending"); }
        var configuration = PacingIllustrationConfiguration.Apply(_configuration, mode, output);
        long effective = ScheduleCardiacConfiguration(configuration);
        _beforePacing ??= _configuration;
        return effective;
    }

    public long StopPacing()
    {
        if (_pendingSource is not null || _pendingVentilationSource is not null)
        { throw new InvalidOperationException("Pacing.SourceChangePending"); }
        var previous = _beforePacing ?? throw new InvalidOperationException("Pacing.NotRunning");
        long effective = ScheduleCardiacConfiguration(previous);
        _beforePacing = null;
        return effective;
    }

    private long ScheduleCardiacConfiguration(PhysiologyIllustrationConfiguration configuration)
    {
        var definition = CreateCardiacDefinition(configuration, ElectricalTherapy);
        var previous = _beforePacing;
        long effective = ScheduleSource(definition, 0);
        _beforePacing = previous;
        return effective;
    }

    public LocalMonitorPreviewSession PrepareSinusAfterShock(EcgElectricalTherapyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.TemplateId != EcgElectricalTherapy.SinusTemplateId)
        { throw new ArgumentException("ElectricalConversion.SinusTargetRequired", nameof(profile)); }
        return CreateCardiacDefinition(SinusIllustrationConfiguration.Apply(_configuration), profile);
    }

    private LocalMonitorPreviewSession CreateCardiacDefinition(PhysiologyIllustrationConfiguration configuration,
        EcgElectricalTherapyProfile? profile)
    {
        var realtime = _realtimeConfiguration is null ? null : _realtimeConfiguration with
        {
            Ventilation = _realtimeOxygenation!.Ventilation,
            OxygenDemandMultiplier = _realtimeOxygenation.Snapshot.OxygenDemandMultiplier
        };
        return new LocalMonitorPreviewSession(configuration, Display, _measurements is not null,
            realtimeOxygenation: realtime, manualVitals: ManualVitals, electricalTherapy: profile)
        {
            _opticalSource = _opticalSource,
            _opticalModulationPermille = _opticalModulationPermille,
            _usesOxygenation = _usesOxygenation
        };
    }

    public ElectricalConversionResult EvaluateElectricalShock(DefibrillationWaveformKind waveform, DefibrillationMode mode, int deliveredEnergyJoules)
    {
        var result = EcgElectricalTherapy.Evaluate(ElectricalTherapy, _configuration, waveform, mode, deliveredEnergyJoules);
        return _pendingSource is not null ? new(ElectricalConversionOutcome.SourceChangePending) : result;
    }

    // Skins supply a prepared sinus source after confirmed delivery. No
    // charging/lease/QRS synchronization is performed by this response adapter.
    // Accepted transitions use the existing atomic 200 ms acquisition boundary.
    public ElectricalConversionResult ApplyElectricalShock(DeliveredElectricalShock delivery, LocalMonitorPreviewSession sinusDefinition,
        int ecgRecoveryMilliseconds = 1000)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.DeliverySequence == 0 || delivery.DeliveredAtSimTimeNs != SimulationTimeNs)
        { throw new ArgumentException("ElectricalConversion.InvalidDelivery", nameof(delivery)); }
        var result = EvaluateElectricalShock(delivery.Waveform, delivery.Mode, delivery.EnergyJoules);
        if (delivery.DeliverySequence <= _lastElectricalDeliverySequence) { return new(ElectricalConversionOutcome.DuplicateDelivery); }
        var artifact = new DefibrillationEcgArtifact(delivery.Waveform, delivery.DeliveredAtSimTimeNs, ecgRecoveryMilliseconds);
        if (_shockArtifacts.Count >= 128) { throw new InvalidOperationException("Defibrillation.TooManyRetainedShocks"); }
        if (result.Outcome != ElectricalConversionOutcome.Eligible)
        {
            _shockArtifacts.Add(artifact);
            _lastElectricalDeliverySequence = delivery.DeliverySequence;
            DataRevision++;
            return result;
        }
        ArgumentNullException.ThrowIfNull(sinusDefinition);
        var plan = sinusDefinition._configuration.ResolvePlan();
        if (sinusDefinition.ElectricalTherapy?.TemplateId != EcgElectricalTherapy.SinusTemplateId ||
            plan.ConductionPattern != AvConductionPattern.FixedPr || plan.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            plan.Pacing is not null || plan.VentricularConductionRatio != 1 || plan.IndependentVentricularPeriodNs is not null ||
            sinusDefinition._configuration.Svt || sinusDefinition._configuration.Vt || sinusDefinition._configuration.Aivr ||
            sinusDefinition._configuration.Aar || sinusDefinition._configuration.Ajr || sinusDefinition._configuration.AtrialEscape)
        { throw new ArgumentException("ElectricalConversion.SinusTargetRequired", nameof(sinusDefinition)); }
        ValidateSourceDefinition(sinusDefinition);
        long delayNs = artifact.Discharge.EndSimTimeNs - SimulationTimeNs;
        int pauseMilliseconds = ElectricalTherapy!.Settings.PostShockPauseMilliseconds;
        long quietAt = checked((SimulationTimeNs + delayNs + 199_999_999) / 200_000_000 * 200_000_000);
        long effective = checked((quietAt + pauseMilliseconds * 1_000_000L + 199_999_999) / 200_000_000 * 200_000_000);
        if (pauseMilliseconds == 0) { ScheduleSource(sinusDefinition, delayNs); }
        else
        {
            var quiet = CreateCardiacDefinition(sinusDefinition._configuration with { CardiacActivity = CardiacActivity.Absent }, ElectricalTherapy);
            ScheduleSource(quiet, delayNs);
            _postShockSinus = (sinusDefinition, effective);
        }
        _shockArtifacts.Add(artifact);
        _lastElectricalDeliverySequence = delivery.DeliverySequence;
        DataRevision++;
        return new(ElectricalConversionOutcome.ConversionScheduled, EcgElectricalTherapy.SinusTemplateId, effective);
    }

    public void UpdateDisplay(MonitorDisplayConfiguration display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (Display.Slots.SequenceEqual(display.Slots)) { return; }
        Display = display;
        Ranges = new(display);
        Ranges.Advance(PresentationFrontierNs, (channel, from, to) => ReadPresentedSamples(channel, from, to, usableOnly: true).Select(s => s.Value));
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
        _realtimeConfiguration = definition._realtimeConfiguration;
        ElectricalTherapy = definition.ElectricalTherapy;
        ManualVitals = definition.ManualVitals;
        _pendingVentilationSource = null;
        _opticalSource = definition._opticalSource;
        _opticalModulationPermille = definition._opticalModulationPermille;
        _usesOxygenation = definition._usesOxygenation;
        OxygenationParameters = definition.OxygenationParameters;
        _pendingSource = null;
        PendingSourceTimeNs = null;
        if (_postShockSinus is { } sinus)
        {
            _pendingSource = sinus.Definition;
            PendingSourceTimeNs = sinus.EffectiveNs;
            _postShockSinus = null;
        }
    }

    private byte[] ApplyShockArtifacts(byte[] wire)
    {
        if (_shockArtifacts.Count == 0) { return wire; }
        var block = WaveformEnvelopeCodec.Decode(wire);
        Guid ecgId = PhysiologyIllustrationSource.ChannelId(0);
        var planes = block.Planes.ToArray();
        int index = Array.FindIndex(planes, plane => plane.ChannelId == ecgId);
        if (index < 0) { return wire; }
        var original = planes[index];
        foreach (var artifact in _shockArtifacts)
        { planes[index] = artifact.Apply(planes[index], block.StartSimTimeNs - _sourceTimeOffsetNs); }
        return ReferenceEquals(original, planes[index]) ? wire : WaveformEnvelopeCodec.EncodeRaw(block with { Planes = planes });
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
        List<DetectedEcgBeat> synchronizationBeats = [];
        SynchronizationBeats = [];
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
            byte[][] wires = source.AdvanceTo(sourceNext, 50, 1, 100, out var immediateSamples)
                .Where(w => WaveformEnvelopeCodec.Decode(w).StartSimTimeNs >= _sourceTimeOffsetNs).Select(ApplyShockArtifacts).ToArray();
            byte[]?[] opticalWires = wires.Select(wire =>
            {
                long start = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs;
                foreach (var previous in _previousOpticalSources)
                {
                    if (start < previous.ToExclusiveSourceTimeNs) { return previous.Source?.ConvertAcquiredPulse(wire); }
                }
                return optics?.ConvertAcquiredPulse(wire);
            }).ToArray();
            synchronizationBeats.AddRange(DetectSynchronizationBeats(immediateSamples));
            _source = source;
            _realtimeOxygenation = oxygenation;
            _opticalSource = optics;
            foreach (var sample in immediateSamples)
            {
                var plane = _planeConfigurations[sample.ChannelId];
                double value = (double)sample.NormalizedValue * plane.ScaleNumerator / plane.ScaleDenominator +
                    (double)plane.OffsetNumerator / plane.OffsetDenominator;
                _immediateSamples[sample.ChannelId].Add((sample.SourceSimTimeNs - _sourceTimeOffsetNs, value, sample.QualityFlags));
            }
            long oldestTimeNs = next - RetainedBlockCount * 200_000_000L;
            foreach (var samples in _immediateSamples.Values)
            {
                int expired = samples.FindIndex(sample => sample.TimeNs >= oldestTimeNs);
                if (expired < 0) { samples.Clear(); }
                else if (expired > 0) { samples.RemoveRange(0, expired); }
            }
            if (immediateSamples.Count > 0) { DataRevision++; }
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
                if (_blocks.Length > 0) { _shockArtifacts.RemoveAll(artifact => artifact.RecoveryEndSimTimeNs <= _blocks[0].StartSimTimeNs); }
                DataRevision++;
            }
            SimulationTimeNs = next;
            FrontierNs = _blocks.Length == 0 ? 0 : Math.Max(FrontierNs,
                Math.Min(_blocks[^1].StartSimTimeNs + 200_000_000, Math.Max(0, next - PresentationLatencyNs)));
            Ranges.Advance(PresentationFrontierNs, (channel, from, to) => ReadPresentedSamples(channel, from, to, usableOnly: true).Select(s => s.Value));
            deltaNs -= chunk;
        }
        DetectedRhythmEvents = rhythmEvents.AsReadOnly();
        DetectedMonitoringEvents = monitoringEvents.AsReadOnly();
        DetectedPulses = Array.AsReadOnly(pulses.Where(p => _measurementFrontier - p.ConfirmedAtNs <= 250_000_000).ToArray());
        DetectedBeats = Array.AsReadOnly(beats.Where(b => _measurementFrontier - b.ConfirmedAtNs <= 250_000_000).ToArray());
        SynchronizationBeats = synchronizationBeats.AsReadOnly();
    }

    private IReadOnlyList<DetectedEcgBeat> DetectSynchronizationBeats(IReadOnlyList<PhysiologyWaveformSample> samples)
    {
        if (_synchronizationEcg is null) { return []; }
        Guid id = PhysiologyIllustrationSource.ChannelId(0);
        var ecg = samples.Where(sample => sample.ChannelId == id).ToArray();
        if (ecg.Length == 0) { return []; }
        const long stepNs = 4_000_000;
        long start = ecg[0].SourceSimTimeNs;
        var flags = ecg.Select((sample, index) => new WaveformQualityRange((uint)index, 1, sample.QualityFlags))
            .Where(range => range.QualityFlags != 0).ToArray();
        var plane = new WaveformPlane(id, 250, 1, (ulong)(start / stepNs), 1, 1, 0, 1,
            flags.Length == 0 ? WaveformQualityEncoding.None : WaveformQualityEncoding.Ranges,
            ecg.Select(sample => sample.NormalizedValue).ToArray(), flags);
        foreach (var artifact in _shockArtifacts) { plane = artifact.Apply(plane, start - _sourceTimeOffsetNs); }
        var block = new WaveformEnvelope(id, _opticalInstanceId, 1, 1, _synchronizationSequence, 1,
            start, (uint)(ecg.Length * stepNs), [plane]);
        var rebased = Rebase(block);
        var detected = _synchronizationEcg.Consume(WaveformEnvelopeCodec.EncodeRaw(rebased), out _, out _, PacingEvidence(block));
        _synchronizationSequence++;
        return _synchronizationEcg.Read(rebased.StartSimTimeNs + rebased.DurationNs - 1).Status is
            WaveformMeasurementStatus.Valid or WaveformMeasurementStatus.WarmingUp ? detected : [];
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
        const long sampleStepNs = 4_000_000;
        var timeline = RegularPhysiologyTimeline.Restore(new(configuration.ResolvePlan(), Math.Max(0, block.StartSimTimeNs - sampleStepNs)));
        var events = timeline.AdvanceBefore(checked(block.StartSimTimeNs + block.DurationNs), 100);
        // Report stimuli at the first acquired sample at/after onset. Arbitrary
        // output rates need not divide the acquisition grid; include the previous
        // grid interval so a pulse just before a packet edge is not lost.
        long[] Times(PhysiologyCycleEventKind kind) => events.Where(e => e.Kind == kind)
            .Select(e => checked((e.SimTimeNs + sampleStepNs - 1) / sampleStepNs * sampleStepNs))
            .Where(t => t >= block.StartSimTimeNs && t < block.StartSimTimeNs + block.DurationNs)
            .Select(t => t - _sourceTimeOffsetNs).Distinct().ToArray();
        bool ventricular = mode != PacingIllustration.AtrialAai;
        return new(Times(PhysiologyCycleEventKind.VentricularPacingPulse))
        {
            AtrialPulseTimesNs = Times(PhysiologyCycleEventKind.AtrialPacingPulse),
            Origin = EcgPacingEvidenceOrigin.Simulation,
            VentricularPacingExpected = ventricular,
            ExpectedVentricularIntervalNs = ventricular ? PacingReference.Timing(mode, configuration.PacingOutput).RrIntervalNs : null
        };
    }

    public IEnumerable<(long TimeNs, double Value)> Samples(int channel, long fromSimTimeNs, long toExclusiveSimTimeNs) =>
        ReadSamples(channel, fromSimTimeNs, toExclusiveSimTimeNs, usableOnly: false);

    public IEnumerable<(long TimeNs, double Value)> PresentedSamples(int channel, long fromSimTimeNs, long toExclusiveSimTimeNs) =>
        ReadPresentedSamples(channel, fromSimTimeNs, toExclusiveSimTimeNs, usableOnly: false);

    private IEnumerable<(long TimeNs, double Value)> ReadPresentedSamples(int channel, long fromSimTimeNs,
        long toExclusiveSimTimeNs, bool usableOnly)
    {
        foreach (var sample in _immediateSamples[PhysiologyIllustrationSource.ChannelId(channel)])
        {
            if (sample.TimeNs < fromSimTimeNs || sample.TimeNs >= toExclusiveSimTimeNs || usableOnly && sample.QualityFlags != 0) { continue; }
            var artifact = channel == 0 ? _shockArtifacts.LastOrDefault(candidate => candidate.Contains(sample.TimeNs)) : null;
            if (artifact is null) { yield return (sample.TimeNs, sample.Value); }
            else if (!usableOnly) { yield return (sample.TimeNs, artifact.EvaluateMicrovolts(sample.TimeNs)); }
        }
    }

    private IEnumerable<(long TimeNs, double Value)> ReadSamples(int channel, long fromSimTimeNs, long toExclusiveSimTimeNs, bool usableOnly)
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
                if (usableOnly && plane.QualityRanges.Any(range => i >= range.FirstSampleOffset && i < range.FirstSampleOffset + range.Count && range.QualityFlags != 0)) { continue; }
                if (time >= fromSimTimeNs && time < toExclusiveSimTimeNs)
                {
                    yield return (time, (double)plane.Samples[i] * plane.ScaleNumerator / plane.ScaleDenominator +
                        (double)plane.OffsetNumerator / plane.OffsetDenominator);
                }
            }
        }
    }
}
