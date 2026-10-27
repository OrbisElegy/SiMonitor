// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

public sealed partial class LocalMonitorPreviewSession
{
    private PatientTimelineProgram? _timelineProgram;
    private IReadOnlyDictionary<string, IReadOnlyList<PatientTimelineSource?>> _programSources =
        new ReadOnlyDictionary<string, IReadOnlyList<PatientTimelineSource?>>(new Dictionary<string, IReadOnlyList<PatientTimelineSource?>>());
    private IReadOnlyDictionary<string, PatientTimelineSource?> _programInitialSources =
        new ReadOnlyDictionary<string, PatientTimelineSource?>(new Dictionary<string, PatientTimelineSource?>());
    private PhysiologyIllustrationConfiguration? _timelineEntryConfiguration;
    private RealtimeOxygenationConfiguration? _programOxygenation;
    private PatientTimelineTreatmentPolicy? _currentTimelineTreatment;
    private (LocalMonitorPreviewSession Source, string TimelineId)? _pendingTreatmentBranch;
    public string? CurrentTimelineId { get; private set; }
    public string? CurrentTimelineName { get; private set; }
    public ulong TimelineGeneration { get; private set; }
    public long TimelineOriginTimeNs { get; private set; }
    public string? TimelineBranchError { get; private set; }
    public string? TimelineBranchErrorTimelineName { get; private set; }
    public PatientTimelineTreatmentPolicy? CurrentTimelineTreatment => PatientTimelineStopped ? null : _currentTimelineTreatment;
    public bool ShockAllowed => CurrentTimelineTreatment?.AllowShock != false;

    public void ConfigurePatientTimelineProgram(PatientTimelineProgram program,
        IReadOnlyDictionary<string, IReadOnlyList<PatientTimelineSource?>>? templateSources = null,
        RealtimeOxygenationConfiguration? oxygenationModel = null,
        IReadOnlyDictionary<string, PatientTimelineSource?>? initialSources = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (SimulationTimeNs != 0 || _patientTimeline.Count != 0 || _timelineProgram is not null ||
            _pendingSource is not null || _pendingVentilationSource is not null)
        { throw new InvalidOperationException("PatientTimeline.RequiresRestart"); }
        var owned = program.Snapshot();
        var sources = new Dictionary<string, IReadOnlyList<PatientTimelineSource?>>(StringComparer.Ordinal);
        var entrySources = new Dictionary<string, PatientTimelineSource?>(StringComparer.Ordinal);
        if (templateSources is not null && templateSources.Keys.Any(id => !owned.Timelines.Any(timeline => timeline.Id == id)) ||
            initialSources is not null && initialSources.Keys.Any(id => !owned.Timelines.Any(timeline => timeline.Id == id)))
        { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
        foreach (var timeline in owned.Timelines)
        {
            var supplied = templateSources is not null && templateSources.TryGetValue(timeline.Id, out var value) ? value : null;
            if (supplied is not null && supplied.Count != timeline.Events.Count)
            { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
            var list = supplied?.ToArray() ?? new PatientTimelineSource?[timeline.Events.Count];
            for (int index = 0; index < list.Length; index++)
            {
                if (timeline.Events[index].ChangesTemplate && list[index] is null)
                { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
                if (list[index] is { } source)
                {
                    ArgumentNullException.ThrowIfNull(source.Configuration);
                    _ = source.Configuration.ResolvePlan();
                    source.ElectricalTherapy?.Validate();
                }
            }
            sources.Add(timeline.Id, Array.AsReadOnly(list));
            var entrySource = initialSources is not null && initialSources.TryGetValue(timeline.Id, out var entry) ? entry : null;
            if (timeline.InitialState is { ChangesTemplate: true } && entrySource is null)
            { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
            if (entrySource is not null)
            {
                ArgumentNullException.ThrowIfNull(entrySource.Configuration);
                _ = entrySource.Configuration.ResolvePlan();
                entrySource.ElectricalTherapy?.Validate();
            }
            if (timeline.InitialState?.Ventilation is not null && _realtimeConfiguration is null && oxygenationModel is null)
            { throw new ArgumentException("PatientTimeline.OxygenationModelRequired"); }
            if (timeline.InitialState?.Targets.SaturationMilliPercent is not null && _measurements is null)
            { throw new ArgumentException("Preview.OpticsRequireMeasurements"); }
            entrySources.Add(timeline.Id, entrySource);
        }
        var trial = (LocalMonitorPreviewSession)MemberwiseClone();
        trial._timelineProgram = owned;
        trial._programSources = new ReadOnlyDictionary<string, IReadOnlyList<PatientTimelineSource?>>(sources);
        trial._programInitialSources = new ReadOnlyDictionary<string, PatientTimelineSource?>(entrySources);
        trial._programOxygenation = oxygenationModel;
        var prepared = trial.PrepareTimelineBranch(owned.InitialTimelineId, fork: false);
        PublishTimelineBranch(prepared);
    }

    private LocalMonitorPreviewSession PrepareTimelineBranch(string timelineId, bool fork = true)
    {
        var definition = _timelineProgram!.Timelines.Single(timeline => timeline.Id == timelineId);
        // Preparation writes only this shallow view's private fields. Shared
        // sample histories, measurements, NIBP and record lists are not touched.
        var trial = (LocalMonitorPreviewSession)MemberwiseClone();
        trial._pendingSource = null;
        trial._pendingVentilationSource = null;
        trial._currentTimelineTreatment = definition.Treatment;
        trial._timelineOpticalCurve = fork ? FreezePatientCurve(_timelineOpticalCurve) : _timelineOpticalCurve;
        trial._timelineEntryConfiguration = null;
        if (definition.InitialState is { } initial)
        {
            trial.PrepareTimelineInitialState(initial, _programInitialSources[definition.Id], fork);
            trial._timelineEntryConfiguration = trial.FreezePatientConfiguration(trial._configuration);
        }
        trial.ConfigurePatientTimelineSequence(definition.Events, _programSources[definition.Id],
            _programOxygenation, SimulationTimeNs, fork || definition.InitialState is not null);
        trial.CurrentTimelineId = definition.Id;
        trial.CurrentTimelineName = definition.Name;
        trial.TimelineGeneration = checked(TimelineGeneration + 1);
        return trial;
    }

    private void PublishTimelineBranch(LocalMonitorPreviewSession prepared)
    {
        bool enteringBranch = TimelineGeneration > 0;
        PublishTimelineState(prepared);
        PatientTimelineTriggeredCount = 0;
        PatientTimelineStopped = false;
        CurrentTimelineId = prepared.CurrentTimelineId;
        CurrentTimelineName = prepared.CurrentTimelineName;
        TimelineGeneration = prepared.TimelineGeneration;
        TimelineOriginTimeNs = prepared.TimelineOriginTimeNs;
        TimelineBranchError = null;
        TimelineBranchErrorTimelineName = null;
        _pendingSource = null;
        _pendingVentilationSource = null;
        PendingSourceTimeNs = null;
        _pendingSourceDescription = "";
        _pendingStopsPatientTimeline = false;
        _postShockSinus = null;
        _pendingTreatmentBranch = null;
        _beforePacing = prepared._beforePacing;
        if (_beforePacing is not null) { _beforePacing = FreezePatientConfiguration(_beforePacing); }
        Nibp.Advance(SimulationTimeNs, ManualVitals.Nibp);
        if (enteringBranch) { RecordEvent(PreviewRecordEventKind.PatientTimelineBranchEntered, CurrentTimelineName!); }
    }

    private void PublishTimelineState(LocalMonitorPreviewSession prepared)
    {
        _timelineProgram = prepared._timelineProgram;
        _programSources = prepared._programSources;
        _programInitialSources = prepared._programInitialSources;
        _programOxygenation = prepared._programOxygenation;
        _timelineEntryConfiguration = prepared._timelineEntryConfiguration;
        _currentTimelineTreatment = prepared._currentTimelineTreatment;
        _source = prepared._source;
        _ecgSource = prepared._ecgSource;
        _realtimeOxygenation = prepared._realtimeOxygenation;
        _realtimeConfiguration = prepared._realtimeConfiguration;
        _configuration = prepared._configuration;
        ElectricalTherapy = prepared.ElectricalTherapy;
        ManualVitals = prepared.ManualVitals;
        OxygenationParameters = prepared.OxygenationParameters;
        _usesOxygenation = prepared._usesOxygenation;
        _opticalSource = prepared._opticalSource;
        _authoredOpticalSaturationMilliPercent = prepared._authoredOpticalSaturationMilliPercent;
        _patientTimeline = prepared._patientTimeline;
        _timelineSources = prepared._timelineSources;
        _patientTimelineEndNs = prepared._patientTimelineEndNs;
        _timelineOpticalCurve = prepared._timelineOpticalCurve;
        _timelineTemperature = prepared._timelineTemperature;
        _timelineNibpSystolic = prepared._timelineNibpSystolic;
        _timelineNibpDiastolic = prepared._timelineNibpDiastolic;
        _timelineNibpMean = prepared._timelineNibpMean;
        _timelineTemperatureStartsNs = prepared._timelineTemperatureStartsNs;
        _timelineNibpStartsNs = prepared._timelineNibpStartsNs;
        _timelineEventTimesNs = prepared._timelineEventTimesNs;
        _timelineAuthoredSources = prepared._timelineAuthoredSources;
        _timelineNodesOxygenation = prepared._timelineNodesOxygenation;
        PatientTimelineTriggeredCount = prepared.PatientTimelineTriggeredCount;
        TimelineOriginTimeNs = prepared.TimelineOriginTimeNs;
    }

    private LocalMonitorPreviewSession? TryPrepareTreatmentBranch(string timelineId, out string? error)
    {
        try
        {
            var prepared = PrepareTimelineBranch(timelineId);
            error = null;
            return prepared;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            error = exception.Message;
            return null;
        }
    }

    private void RecordTreatmentBranchError(string timelineId, string error)
    {
        TimelineBranchError = error;
        TimelineBranchErrorTimelineName = _timelineProgram!.Timelines.Single(timeline => timeline.Id == timelineId).Name;
        RecordEvent(PreviewRecordEventKind.PatientTimelineBranchRejected, TimelineBranchErrorTimelineName);
    }

    private LocalMonitorPreviewSession PrepareTimelineEventSource(LocalMonitorPreviewSession definition, PatientTimelineEvent item)
    {
        var prepared = definition._configuration;
        var configuration = MergeTimelineTemplate(_configuration, prepared, item) with
        {
            CardiacRateClock = item.EcgTemplateId is not null ? prepared.CardiacRateClock : _configuration.CardiacRateClock,
            RespiratoryRateClock = prepared.RespiratoryRateClock,
            AbpTarget = prepared.AbpTarget,
            PaTarget = prepared.PaTarget,
            AbpSystolicCentiMmHgCurve = prepared.AbpSystolicCentiMmHgCurve,
            AbpDiastolicCentiMmHgCurve = prepared.AbpDiastolicCentiMmHgCurve,
            PaSystolicCentiMmHgCurve = prepared.PaSystolicCentiMmHgCurve,
            PaDiastolicCentiMmHgCurve = prepared.PaDiastolicCentiMmHgCurve,
            CvpBaselineCentiMmHgCurve = prepared.CvpBaselineCentiMmHgCurve,
            Co2EndExpiratoryCentiMmHgCurve = prepared.Co2EndExpiratoryCentiMmHgCurve
        };
        var therapy = item.EcgTemplateId is not null ? definition.ElectricalTherapy : ElectricalTherapy;
        var realtime = item.Ventilation is not null ? definition._realtimeConfiguration : _realtimeConfiguration;
        if (realtime is not null && _realtimeOxygenation is { } oxygenation)
        {
            realtime = realtime with
            {
                Ventilation = item.Ventilation ?? oxygenation.Ventilation,
                OxygenDemandMultiplier = oxygenation.Snapshot.OxygenDemandMultiplier
            };
        }
        if (configuration == prepared && therapy == definition.ElectricalTherapy && realtime == definition._realtimeConfiguration)
        { return definition; }
        // A previous treatment may have changed the inherited rhythm after
        // preparation. Apply only the categories this event actually authors.
        var updated = new LocalMonitorPreviewSession(configuration, Display, _measurements is not null,
            realtimeOxygenation: realtime, manualVitals: ManualVitals, electricalTherapy: therapy,
            pressureTransducers: PressureTransducers, enableTwelveLead: _ecgSource is not null)
        {
            _opticalSource = _opticalSource,
            _timelineOpticalCurve = _timelineOpticalCurve,
            _authoredOpticalSaturationMilliPercent = _authoredOpticalSaturationMilliPercent,
            _opticalModulationPermille = _opticalModulationPermille,
            _usesOxygenation = _usesOxygenation
        };
        ValidateSourceDefinition(updated);
        return updated;
    }

    private static PhysiologyIllustrationConfiguration MergeTimelineTemplate(PhysiologyIllustrationConfiguration current,
        PhysiologyIllustrationConfiguration template, PatientTimelineEvent item)
    {
        var next = item.EcgTemplateId is null ? current : template with
        {
            BreathPeriodMilliseconds = current.BreathPeriodMilliseconds,
            InspirationMilliseconds = current.InspirationMilliseconds,
            RespAmplitudeCounts = current.RespAmplitudeCounts,
            RespCardiacArtifactCounts = current.RespCardiacArtifactCounts,
            RespiratoryActivity = current.RespiratoryActivity,
            RespiratoryPattern = current.RespiratoryPattern,
            RespirationIllustration = current.RespirationIllustration,
            ActivityAfterBreaths = current.ActivityAfterBreaths,
            ActivityDurationBreaths = current.ActivityDurationBreaths,
            InspiratoryPauseMilliseconds = current.InspiratoryPauseMilliseconds,
            ExpiratoryPauseMilliseconds = current.ExpiratoryPauseMilliseconds,
            Co2BaselineMmHg = current.Co2BaselineMmHg,
            Co2EndExpiratoryMmHg = current.Co2EndExpiratoryMmHg,
            Co2PlateauStartCentiMmHg = current.Co2PlateauStartCentiMmHg,
            Co2DeadSpaceMilliseconds = current.Co2DeadSpaceMilliseconds,
            Co2RiseMilliseconds = current.Co2RiseMilliseconds,
            Co2FallMilliseconds = current.Co2FallMilliseconds,
            Co2TransportDelayMilliseconds = current.Co2TransportDelayMilliseconds,
            Co2DispersionStepMilliseconds = current.Co2DispersionStepMilliseconds,
            SeededCo2 = current.SeededCo2,
            CapnogramIllustration = current.CapnogramIllustration,
            AbpTarget = current.AbpTarget,
            PaTarget = current.PaTarget,
            AbpPulsePermille = current.AbpPulsePermille,
            PaPulsePermille = current.PaPulsePermille,
            CvpBaselineCentiMmHg = current.CvpBaselineCentiMmHg,
            PressureVariation = current.PressureVariation,
            PressureMeasurement = current.PressureMeasurement,
            PlethShape = current.PlethShape,
            UseVascularReservoir = current.UseVascularReservoir
        };
        if (item.RespirationTemplate is not null)
        {
            next = next with
            {
                RespiratoryActivity = template.RespiratoryActivity,
                RespiratoryPattern = template.RespiratoryPattern,
                RespirationIllustration = template.RespirationIllustration,
                ActivityAfterBreaths = template.ActivityAfterBreaths,
                ActivityDurationBreaths = template.ActivityDurationBreaths
            };
        }
        if (item.EjectionTemplate is not null)
        {
            next = next with
            {
                PressureMeasurement = template.PressureMeasurement,
                VentricularMechanicalEnabled = template.VentricularMechanicalEnabled,
                VentricularConductionRatio = item.EjectionTemplate == 2 || current.VentricularConductionRatio == 2 &&
                    current.ConductionPattern == AvConductionPattern.FixedPr ? template.VentricularConductionRatio : next.VentricularConductionRatio
            };
        }
        if (item.PlethTemplate is not null) { next = next with { PlethShape = template.PlethShape }; }
        if (item.CapnogramTemplate is not null)
        {
            next = next with
            {
                CapnogramIllustration = template.CapnogramIllustration,
                Co2BaselineMmHg = template.Co2BaselineMmHg,
                Co2PlateauStartCentiMmHg = template.Co2PlateauStartCentiMmHg,
                Co2RiseMilliseconds = template.Co2RiseMilliseconds,
                Co2FallMilliseconds = template.Co2FallMilliseconds
            };
        }
        return next;
    }
}
