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
// All live consumers share committed source samples and one measurement owner.
// Fixed-duration waveform packets are a recording projection of the same stream.
public sealed partial class LocalMonitorPreviewSession
{
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
    private ulong _lastElectricalDeliverySequence;
    public PhysiologyIllustrationConfiguration Configuration => _configuration;
    public EcgElectricalTherapyProfile? ElectricalTherapy { get; private set; }
    public long? PendingSourceTimeNs { get; private set; }
    private PhysiologyWaveformGroup _source;
    private LiveEcgProjection? _ecgSource;
    public EcgLead DisplayEcgLead { get; private set; } = EcgLead.II;

    public void SelectDisplayEcgLead(EcgLead lead)
    {
        if (!Enum.IsDefined(lead)) { throw new ArgumentOutOfRangeException(nameof(lead)); }
        if (lead != EcgLead.II && _ecgSource is null) { throw new InvalidOperationException("Preview.TwelveLeadSourceRequired"); }
        if (lead == DisplayEcgLead) { return; }
        Ranges.RefreshChannel(0, SimulationTimeNs, (_, from, to) => ReadEcgSamples(lead, from, to, usableOnly: true).Select(sample => sample.Value));
        RecordEvent(PreviewRecordEventKind.LeadChanged, $"{DisplayEcgLead} → {lead}");
        DisplayEcgLead = lead;
    }
    private readonly List<(long TimeNs, IReadOnlyList<short> Values)> _ecgSamples = [];
    private WaveformEnvelope[] _blocks = [];
    private readonly Dictionary<Guid, List<(long TimeNs, double Value, uint QualityFlags)>> _samples;
    private readonly Dictionary<Guid, WaveformBlockPlaneConfiguration> _planeConfigurations;
    private readonly LiveWaveformMeasurements? _measurements;
    private readonly Dictionary<Guid, ulong> _channelSequences;
    public MonitorBeatEvidence? BeatEvidence { get; private set; }
    public AedEcgEvidence? EcgEvidence => Measurements is { } reading &&
        _samples[PhysiologyIllustrationSource.ChannelId(0)] is { Count: > 0 } ecg
        ? new(ecg[^1].TimeNs, reading.HeartRate, reading.EcgMonitoring) : null;
    private PulseOximeterIllustrationSource? _opticalSource;
    private RealtimeOxygenationSource? _realtimeOxygenation;
    private PhysiologyWaveformGroup? _pendingVentilationSource;
    private long _pendingVentilationTimeNs;
    private readonly Guid _opticalInstanceId = Guid.NewGuid();
    private int _opticalModulationPermille;
    private bool _usesOxygenation;
    public LiveMeasurementSnapshot? Measurements
    {
        get
        {
            if (_measurements is null) { return null; }
            var reading = _measurements.Read(SimulationTimeNs);
            // Delivery invalidates the front end immediately, including the
            // fraction of a sample interval before its first unusable sample.
            if (_shockArtifacts.Any(artifact => artifact.Contains(SimulationTimeNs)))
            {
                var poor = WaveformMeasurementStatus.PoorSignal;
                reading = reading with
                {
                    HeartRate = new(poor, null, null),
                    EcgRhythm = new(poor, null, null, null),
                    EcgMonitoring = EcgMonitoringReading.NoData with
                    {
                        Status = poor,
                        Repolarization = new(poor, null, poor, null, null, null)
                    }
                };
            }
            return PressureTransducers.Apply(reading);
        }
    }
    public PressureTransducerLimits PressureTransducers { get; private set; }
    public ManualVitalSigns ManualVitals { get; private set; }
    public NibpMeasurement Nibp { get; } = new();
    public long SimulationTimeNs { get; private set; }
    public ulong DataRevision { get; private set; }
    public IReadOnlyList<DetectedPlethPulse> DetectedPulses { get; private set; } = [];
    public IReadOnlyList<DetectedEcgMonitoringEvent> DetectedMonitoringEvents { get; private set; } = [];
    public IReadOnlyList<DetectedEcgRhythmEvent> DetectedRhythmEvents { get; private set; } = [];
    public IReadOnlyList<DetectedEcgBeat> DetectedBeats { get; private set; } = [];
    public IReadOnlyList<long> EmittedPacingPulseTimesNs { get; private set; } = [];
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
        EcgElectricalTherapyProfile? electricalTherapy = null, PressureTransducerLimits? pressureTransducers = null,
        bool enableTwelveLead = false)
    {
        ArgumentNullException.ThrowIfNull(display);
        electricalTherapy?.Validate();
        ElectricalTherapy = electricalTherapy;
        PressureTransducers = pressureTransducers ?? new();
        PressureTransducers.Validate();
        ManualVitals = manualVitals ?? ManualVitalSigns.Empty;
        Nibp.Advance(0, ManualVitals.Nibp);
        Nibp.Changed += item => _recordEvents.Add(new(item.TimeNs, item.Kind switch
        {
            NibpEventKind.Started => PreviewRecordEventKind.NibpStarted,
            NibpEventKind.Completed => PreviewRecordEventKind.NibpCompleted,
            _ => PreviewRecordEventKind.NibpCancelled
        }, item.Reading is { } reading ? $"{reading.SystolicMmHg} / {reading.DiastolicMmHg} ({reading.MeanMmHg}) mmHg" : ""));
        _configuration = configuration;
        _realtimeConfiguration = realtimeOxygenation;
        _source = PhysiologyIllustrationSource.Create(configuration, ventilation: realtimeOxygenation?.Ventilation);
        _ecgSource = enableTwelveLead ? LiveEcgIllustrationSource.Create(configuration) : null;
        _planeConfigurations = _source.CaptureState().Assembler.Planes.ToDictionary(plane => plane.Configuration.ChannelId, plane => plane.Configuration);
        _channelSequences = _planeConfigurations.Keys.ToDictionary(id => id, _ => 0UL);
        _samples = _planeConfigurations.Keys.ToDictionary(id => id, _ => new List<(long TimeNs, double Value, uint QualityFlags)>());
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
        RecordEvent(PreviewRecordEventKind.VentilationChanged, "");
    }

    public void DiscardStartup()
    {
        if (SimulationTimeNs != 0 || _sourceTimeOffsetNs != 0 || _pendingSource is not null)
        { throw new InvalidOperationException("Preview.StartupAlreadyStarted"); }
        var source = _source.Fork();
        var ecgSource = _ecgSource?.Fork();
        if (_pendingVentilationSource is not null) { source.ContinueWith(_pendingVentilationSource); }
        var oxygenation = _realtimeOxygenation?.Fork();
        for (long time = 50_000_000; time <= StartupDiscardNs; time += 50_000_000)
        {
            oxygenation?.AdvanceTo(time);
            source.AdvanceTo(time, 50, 1, 100);
            ecgSource?.GenerateBefore(time, 50, 100);
        }
        _source = source;
        _ecgSource = ecgSource;
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
        var ecgTrial = _ecgSource?.Fork();
        ecgTrial?.ContinueWith(definition._ecgSource ?? LiveEcgIllustrationSource.Create(definition._configuration));
    }

    public long ScheduleSource(LocalMonitorPreviewSession definition, long delayNs, string description = "")
    {
        ValidateSourceDefinition(definition);
        if (delayNs is < 0 or > 60_000_000_000) { throw new ArgumentOutOfRangeException(nameof(delayNs)); }
        long effective = checked((SimulationTimeNs + delayNs + 199_999_999) / 200_000_000 * 200_000_000);
        // Explicit source changes replace the entire pending post-shock sequence.
        _beforePacing = null;
        _postShockSinus = null;
        _pendingSource = definition;
        _pendingSourceDescription = description;
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
            realtimeOxygenation: realtime, manualVitals: ManualVitals, electricalTherapy: profile, pressureTransducers: PressureTransducers,
            enableTwelveLead: _ecgSource is not null)
        {
            _opticalSource = _opticalSource,
            _opticalModulationPermille = _opticalModulationPermille,
            _usesOxygenation = _usesOxygenation
        };
    }

    public ElectricalConversionResult EvaluateElectricalShock(DefibrillationWaveformKind waveform, DefibrillationMode mode, int deliveredEnergyJoules, bool automated = false)
    {
        var result = EcgElectricalTherapy.Evaluate(ElectricalTherapy, _configuration, waveform, mode, deliveredEnergyJoules, automated);
        return _pendingSource is not null ? new(ElectricalConversionOutcome.SourceChangePending) : result;
    }

    // Skins supply a prepared sinus source after confirmed delivery. No
    // charging/lease/QRS synchronization is performed by this response adapter.
    // Accepted transitions retain the explicit 200 ms source-change boundary.
    public ElectricalConversionResult ApplyElectricalShock(DeliveredElectricalShock delivery, LocalMonitorPreviewSession sinusDefinition,
        int ecgRecoveryMilliseconds = 1000)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.DeliverySequence == 0 || delivery.DeliveredAtSimTimeNs != SimulationTimeNs)
        { throw new ArgumentException("ElectricalConversion.InvalidDelivery", nameof(delivery)); }
        var result = EvaluateElectricalShock(delivery.Waveform, delivery.Mode, delivery.EnergyJoules, delivery.Automated);
        if (delivery.DeliverySequence <= _lastElectricalDeliverySequence) { return new(ElectricalConversionOutcome.DuplicateDelivery); }
        var artifact = new DefibrillationEcgArtifact(delivery.Waveform, delivery.DeliveredAtSimTimeNs, ecgRecoveryMilliseconds);
        if (_shockArtifacts.Count >= 128) { throw new InvalidOperationException("Defibrillation.TooManyRetainedShocks"); }
        if (result.Outcome != ElectricalConversionOutcome.Eligible)
        {
            RecordEvent(PreviewRecordEventKind.ShockDelivered, $"{delivery.EnergyJoules} J · {delivery.Mode}");
            _shockArtifacts.Add(artifact);
            WithdrawShockBeatCues();
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
        int pauseMilliseconds = (delivery.Automated ? ElectricalTherapy!.AedSettings : ElectricalTherapy!.Settings).PostShockPauseMilliseconds;
        long quietAt = checked((SimulationTimeNs + delayNs + 199_999_999) / 200_000_000 * 200_000_000);
        long effective = checked((quietAt + pauseMilliseconds * 1_000_000L + 199_999_999) / 200_000_000 * 200_000_000);
        if (pauseMilliseconds == 0) { ScheduleSource(sinusDefinition, delayNs); }
        else
        {
            var quiet = CreateCardiacDefinition(sinusDefinition._configuration with { CardiacActivity = CardiacActivity.Absent }, ElectricalTherapy);
            ScheduleSource(quiet, delayNs);
            _postShockSinus = (sinusDefinition, effective);
        }
        RecordEvent(PreviewRecordEventKind.ShockDelivered, $"{delivery.EnergyJoules} J · {delivery.Mode}");
        _shockArtifacts.Add(artifact);
        WithdrawShockBeatCues();
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
        Ranges.Advance(_ => SimulationTimeNs, (channel, from, to) => ReadDisplaySamples(channel, from, to, usableOnly: true).Select(s => s.Value));
    }

    private void ActivatePendingSource()
    {
        var definition = _pendingSource!;
        long sourceTime = checked(SimulationTimeNs + _sourceTimeOffsetNs);
        var source = _source.Fork();
        source.ContinueWith(definition._source);
        var ecgSource = _ecgSource?.Fork();
        ecgSource?.ContinueWith(definition._ecgSource ?? LiveEcgIllustrationSource.Create(definition._configuration));
        RealtimeOxygenationSource? oxygenation = null;
        if (definition._realtimeConfiguration is { } config)
        {
            var transport = PhysiologyIllustrationSource.CreateTransport(definition._configuration,
                config.Ventilation, config.ReferenceStrokeVolumeMicroliters);
            oxygenation = _realtimeOxygenation?.Fork() ?? new(transport, config.Parameters, config.OxygenDemandMultiplier, sourceTime);
            oxygenation.ChangeTransport(transport, sourceTime, config.OxygenDemandMultiplier);
        }
        _source = source;
        _ecgSource = ecgSource;
        _realtimeOxygenation = oxygenation;
        RecordEvent(PreviewRecordEventKind.SourceChanged, string.IsNullOrEmpty(_pendingSourceDescription)
            ? $"{ElectricalTherapy?.TemplateId ?? _configuration.ConductionPattern.ToString()} / {_configuration.CardiacActivity} → " +
                $"{definition.ElectricalTherapy?.TemplateId ?? definition._configuration.ConductionPattern.ToString()} / {definition._configuration.CardiacActivity}"
            : _pendingSourceDescription);
        if (_configuration.Pacing != definition._configuration.Pacing || _configuration.PacingOutput != definition._configuration.PacingOutput)
        {
            RecordEvent(PreviewRecordEventKind.PacingChanged,
                $"{_configuration.Pacing?.ToString() ?? "—"} → {definition._configuration.Pacing?.ToString() ?? "—"}" +
                (definition._configuration.PacingOutput is { } output ? $" · {output.RatePerMinute}/min · {output.CurrentMilliamps} mA" : ""));
        }
        _pendingSourceDescription = "";
        _configuration = definition._configuration;
        _realtimeConfiguration = definition._realtimeConfiguration;
        ElectricalTherapy = definition.ElectricalTherapy;
        PressureTransducers = definition.PressureTransducers;
        ManualVitals = definition.ManualVitals;
        Nibp.Advance(SimulationTimeNs, ManualVitals.Nibp);
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

    private WaveformEnvelope ApplyShockArtifacts(WaveformEnvelope block)
    {
        if (_shockArtifacts.Count == 0) { return block; }
        Guid ecgId = PhysiologyIllustrationSource.ChannelId(0);
        var planes = block.Planes.ToArray();
        int index = Array.FindIndex(planes, plane => plane.ChannelId == ecgId);
        if (index < 0) { return block; }
        var original = planes[index];
        foreach (var artifact in _shockArtifacts)
        { planes[index] = artifact.Apply(planes[index], block.StartSimTimeNs - _sourceTimeOffsetNs); }
        return ReferenceEquals(original, planes[index]) ? block : block with { Planes = planes };
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
        List<DetectedPlethPulse> pulses = [];
        List<DetectedEcgMonitoringEvent> monitoringEvents = [];
        List<DetectedEcgRhythmEvent> rhythmEvents = [];
        List<long> pacingPulseTimesNs = [];
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
            // Prepare sampling, optical conversion and all measurements before
            // publishing this chunk. A failed source-time read can be retried.
            var source = _source.Fork();
            var ecgSource = _ecgSource?.Fork();
            var ecgSamples = ecgSource?.GenerateBefore(sourceNext, 50, 100);
            var records = source.AdvanceTo(sourceNext, 50, 1, 100, out var samples)
                .Select(wire => WaveformEnvelopeCodec.Decode(wire))
                .Where(block => block.StartSimTimeNs >= _sourceTimeOffsetNs)
                .Select(block => Rebase(ApplyShockArtifacts(ProjectEcgRecord(block, ecgSamples)))).ToArray();
            if (ecgSamples is not null)
            {
                // Monitoring, detection and the twelve-lead view share projected II.
                // The legacy single-channel illustration is not another live ECG.
                Guid ecgId = PhysiologyIllustrationSource.ChannelId(0);
                samples = samples.Where(sample => sample.ChannelId != ecgId).Concat(ecgSamples.Select(sample =>
                    new PhysiologyWaveformSample(ecgId, sample.Tick.SimTimeNs, sample.MicrovoltValues[(int)EcgLead.II], 0))).ToArray();
            }
            var channelBlocks = CreateSampleBlocks(samples, optics);
            var ecg = channelBlocks.SingleOrDefault(block => block.Planes[0].ChannelId == PhysiologyIllustrationSource.ChannelId(0));
            var pacing = ecg is null ? null : PacingEvidence(ecg);
            var rebased = channelBlocks.Select(Rebase).ToArray();
            if (_measurements is not null)
            {
                var measured = _measurements.ConsumeSamples(rebased, next, out var detected, out var detectedPulses,
                    out var detectedRhythms, out var detectedMonitoring, pacing);
                monitoringEvents.AddRange(detectedMonitoring);
                rhythmEvents.AddRange(detectedRhythms);
                if (measured.HeartRate.Status is WaveformMeasurementStatus.Valid or WaveformMeasurementStatus.WarmingUp)
                { beats.AddRange(detected); }
                else { beats.Clear(); }
                if (measured.PulseRate.Status is WaveformMeasurementStatus.Valid or WaveformMeasurementStatus.WarmingUp)
                { pulses.AddRange(detectedPulses); }
                else { pulses.Clear(); }
            }
            _source = source;
            _ecgSource = ecgSource;
            if (ecgSamples is not null)
            {
                _ecgSamples.AddRange(ecgSamples.Select(sample =>
                    (sample.Tick.SimTimeNs - _sourceTimeOffsetNs, sample.MicrovoltValues)));
            }
            _realtimeOxygenation = oxygenation;
            _opticalSource = optics;
            foreach (var block in rebased)
            {
                var plane = block.Planes[0];
                _channelSequences[plane.ChannelId]++;
                long stepNs = 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
                for (int index = 0; index < plane.Samples.Count; index++)
                {
                    double value = (double)plane.Samples[index] * plane.ScaleNumerator / plane.ScaleDenominator +
                        (double)plane.OffsetNumerator / plane.OffsetDenominator;
                    uint quality = plane.QualityRanges.Where(range => index >= range.FirstSampleOffset &&
                        index < range.FirstSampleOffset + range.Count).Aggregate(0u, (flags, range) => flags | range.QualityFlags);
                    _samples[plane.ChannelId].Add((block.StartSimTimeNs + index * stepNs, value, quality));
                }
            }
            if (ActivePacing is not null && pacing is not null)
            {
                pacingPulseTimesNs.AddRange(pacing.PulseTimesNs);
                pacingPulseTimesNs.AddRange(pacing.AtrialPulseTimesNs);
            }
            long oldestTimeNs = next - RetainedBlockCount * 200_000_000L;
            _recordEvents.RemoveAll(item => item.TimeNs < oldestTimeNs);
            _ecgSamples.RemoveAll(sample => sample.TimeNs < oldestTimeNs);
            foreach (var history in _samples.Values)
            {
                int expired = history.FindIndex(sample => sample.TimeNs >= oldestTimeNs);
                if (expired < 0) { history.Clear(); }
                else if (expired > 0) { history.RemoveRange(0, expired); }
            }
            if (records.Length > 0) { _blocks = _blocks.Concat(records).TakeLast(RetainedBlockCount).ToArray(); }
            _shockArtifacts.RemoveAll(artifact => artifact.RecoveryEndSimTimeNs <= oldestTimeNs);
            if (samples.Count > 0) { DataRevision++; }
            SimulationTimeNs = next;
            Nibp.Advance(next, ManualVitals.Nibp);
            Ranges.Advance(_ => SimulationTimeNs, (channel, from, to) => ReadDisplaySamples(channel, from, to, usableOnly: true).Select(s => s.Value));
            deltaNs -= chunk;
        }
        foreach (var item in rhythmEvents)
        { _recordEvents.Add(new(item.ConfirmedAtNs, PreviewRecordEventKind.RhythmChanged, $"{item.Kind} · {item.Transition}")); }
        foreach (var item in monitoringEvents)
        { _recordEvents.Add(new(item.ConfirmedAtNs, PreviewRecordEventKind.MonitoringChanged, $"{item.Condition} · {item.Transition}")); }
        DetectedRhythmEvents = rhythmEvents.AsReadOnly();
        DetectedMonitoringEvents = monitoringEvents.AsReadOnly();
        DetectedPulses = pulses.AsReadOnly();
        DetectedBeats = beats.AsReadOnly();
        EmittedPacingPulseTimesNs = Array.AsReadOnly(pacingPulseTimesNs.Distinct().Order().ToArray());
        if (Measurements is { } reading)
        {
            BeatEvidence = new(SimulationTimeNs, reading.HeartRate.Status, reading.PulseRate.Status, DetectedBeats, DetectedPulses);
        }
    }

    private WaveformEnvelope[] CreateSampleBlocks(IReadOnlyList<PhysiologyWaveformSample> samples,
        PulseOximeterIllustrationSource? optics)
    {
        List<WaveformEnvelope> blocks = [];
        foreach (var channel in samples.GroupBy(sample => sample.ChannelId))
        {
            var values = channel.ToArray();
            var configuration = _planeConfigurations[channel.Key];
            var profile = FrozenSignalAcquisitionProfiles.Get(configuration.ProfileId);
            long start = values[0].SourceSimTimeNs;
            var flags = values.Select((sample, index) => new WaveformQualityRange((uint)index, 1, sample.QualityFlags))
                .Where(range => range.QualityFlags != 0).ToArray();
            var plane = new WaveformPlane(channel.Key, profile.SampleRateHz, 1, (ulong)(start / profile.SamplePeriodNs),
                configuration.ScaleNumerator, configuration.ScaleDenominator, configuration.OffsetNumerator, configuration.OffsetDenominator,
                flags.Length == 0 ? WaveformQualityEncoding.None : WaveformQualityEncoding.Ranges,
                values.Select(sample => sample.NormalizedValue).ToArray(), flags);
            var block = new WaveformEnvelope(PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
                1, 1, _channelSequences[channel.Key], 1, start, checked((uint)(values.Length * profile.SamplePeriodNs)), [plane]);
            block = ApplyShockArtifacts(block);
            if (channel.Key == PhysiologyIllustrationSource.ChannelId(2) && optics is not null)
            { block = WaveformEnvelopeCodec.Decode(optics.ConvertAcquiredPulse(WaveformEnvelopeCodec.EncodeRaw(block))); }
            blocks.Add(block);
        }
        return blocks.ToArray();
    }

    private WaveformEnvelope ProjectEcgRecord(WaveformEnvelope block, IReadOnlyList<ElectrodeSignalSample>? current)
    {
        if (current is null) { return block; }
        long start = block.StartSimTimeNs, end = start + block.DurationNs;
        short[] values = _ecgSamples.Where(sample => sample.TimeNs + _sourceTimeOffsetNs >= start && sample.TimeNs + _sourceTimeOffsetNs < end)
            .Select(sample => sample.Values[(int)EcgLead.II])
            .Concat(current.Where(sample => sample.Tick.SimTimeNs >= start && sample.Tick.SimTimeNs < end)
                .Select(sample => sample.MicrovoltValues[(int)EcgLead.II])).ToArray();
        return block with
        {
            Planes = block.Planes.Select(plane => plane.ChannelId == PhysiologyIllustrationSource.ChannelId(0)
                ? plane with { Samples = values } : plane).ToArray()
        };
    }

    private void WithdrawShockBeatCues()
    {
        DetectedBeats = [];
        DetectedRhythmEvents = [];
        DetectedMonitoringEvents = [];
        if (Measurements is { } reading)
        { BeatEvidence = new(SimulationTimeNs, reading.HeartRate.Status, reading.PulseRate.Status, DetectedBeats, DetectedPulses); }
    }

    private EcgPacingEvidence? PacingEvidence(WaveformEnvelope block)
    {
        var configuration = _configuration;
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

    public IEnumerable<(long TimeNs, double Value)> EcgSamples(EcgLead lead, long fromSimTimeNs, long toExclusiveSimTimeNs)
        => ReadEcgSamples(lead, fromSimTimeNs, toExclusiveSimTimeNs, usableOnly: false);

    public IEnumerable<(long TimeNs, double Value)> DisplaySamples(int channel, long fromSimTimeNs, long toExclusiveSimTimeNs) =>
        ReadDisplaySamples(channel, fromSimTimeNs, toExclusiveSimTimeNs, usableOnly: false);

    private IEnumerable<(long TimeNs, double Value)> ReadDisplaySamples(int channel, long fromSimTimeNs, long toExclusiveSimTimeNs, bool usableOnly) =>
        channel == 0 ? ReadEcgSamples(DisplayEcgLead, fromSimTimeNs, toExclusiveSimTimeNs, usableOnly)
            : ReadSamples(channel, fromSimTimeNs, toExclusiveSimTimeNs, usableOnly);

    private IEnumerable<(long TimeNs, double Value)> ReadEcgSamples(EcgLead lead, long fromSimTimeNs, long toExclusiveSimTimeNs, bool usableOnly)
    {
        if (!Enum.IsDefined(lead)) { throw new ArgumentOutOfRangeException(nameof(lead)); }
        // The rhythm strip and the monitor both consume the canonical II samples.
        if (lead == EcgLead.II) { return ReadSamples(0, fromSimTimeNs, toExclusiveSimTimeNs, usableOnly); }
        return ReadProjectedEcg();

        IEnumerable<(long TimeNs, double Value)> ReadProjectedEcg()
        {
            foreach (var sample in _ecgSamples)
            {
                if (sample.TimeNs < fromSimTimeNs || sample.TimeNs >= toExclusiveSimTimeNs) { continue; }
                var artifact = _shockArtifacts.LastOrDefault(item => item.Contains(sample.TimeNs));
                if (usableOnly && artifact is not null) { continue; }
                yield return (sample.TimeNs, artifact?.EvaluateMicrovolts(sample.TimeNs) ?? sample.Values[(int)lead]);
            }
        }
    }

    private IEnumerable<(long TimeNs, double Value)> ReadSamples(int channel, long fromSimTimeNs,
        long toExclusiveSimTimeNs, bool usableOnly)
    {
        foreach (var sample in _samples[PhysiologyIllustrationSource.ChannelId(channel)])
        {
            if (sample.TimeNs >= fromSimTimeNs && sample.TimeNs < toExclusiveSimTimeNs && (!usableOnly || sample.QualityFlags == 0))
            { yield return (sample.TimeNs, sample.Value); }
        }
    }
}
