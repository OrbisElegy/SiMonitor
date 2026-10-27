// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

public sealed partial class LocalMonitorPreviewSession
{
    private IReadOnlyList<PatientTimelineEvent> _patientTimeline = [];
    private LocalMonitorPreviewSession?[] _timelineSources = [];
    private VitalValueCurve? _timelineOpticalCurve;
    private VitalValueCurve? _timelineTemperature;
    private VitalValueCurve? _timelineNibpSystolic;
    private VitalValueCurve? _timelineNibpDiastolic;
    private VitalValueCurve? _timelineNibpMean;
    private int? _authoredOpticalSaturationMilliPercent;
    private long? _timelineTemperatureStartsNs;
    private long? _timelineNibpStartsNs;
    private long _patientTimelineEndNs;
    public IReadOnlyList<PatientTimelineEvent> PatientTimeline => _patientTimeline;
    public int PatientTimelineTriggeredCount { get; private set; }
    public bool PatientTimelineStopped { get; private set; }

    public void StopPatientTimeline()
    {
        if (PatientTimelineStopped || _patientTimeline.Count == 0 && _timelineProgram is null) { return; }
        ulong generation = checked(TimelineGeneration + 1);
        // Curves are prepared ahead of time, so stopping only the event cursor would
        // still allow future targets to change the patient. Fork at the current values.
        var trial = (LocalMonitorPreviewSession)MemberwiseClone();
        if (NextPatientTimelineTimeNs is { } pendingTime && pendingTime <= SimulationTimeNs)
        {
            // Before the first advance, time-zero targets are compiled but have not
            // entered. Disabling must preserve the original patient in that case too.
            long cutoff = checked(pendingTime + _sourceTimeOffsetNs);
            VitalValueCurve? BeforePending(VitalValueCurve? curve) => curve is null ? null :
                new(curve.InitialValue, curve.Changes.Where(change => change.StartSimTimeNs < cutoff).ToArray());
            PhysiologyRateClock? BeforePendingClock(PhysiologyRateClock? clock) => clock is null ? null :
                new(clock.ReferencePeriodNs, BeforePending(clock.RatePerMinute)!);
            trial._configuration = PatientTimelineTriggeredCount == 0 && _timelineEntryConfiguration is { } entry ? entry : _configuration with
            {
                CardiacRateClock = BeforePendingClock(_configuration.CardiacRateClock),
                RespiratoryRateClock = BeforePendingClock(_configuration.RespiratoryRateClock),
                AbpSystolicCentiMmHgCurve = BeforePending(_configuration.AbpSystolicCentiMmHgCurve),
                AbpDiastolicCentiMmHgCurve = BeforePending(_configuration.AbpDiastolicCentiMmHgCurve),
                PaSystolicCentiMmHgCurve = BeforePending(_configuration.PaSystolicCentiMmHgCurve),
                PaDiastolicCentiMmHgCurve = BeforePending(_configuration.PaDiastolicCentiMmHgCurve),
                CvpBaselineCentiMmHgCurve = BeforePending(_configuration.CvpBaselineCentiMmHgCurve),
                Co2EndExpiratoryCentiMmHgCurve = BeforePending(_configuration.Co2EndExpiratoryCentiMmHgCurve)
            };
        }
        trial.ConfigurePatientTimelineCore([], null, _programOxygenation, SimulationTimeNs, fork: true);
        trial._timelineOpticalCurve = FreezePatientCurve(_timelineOpticalCurve);
        trial._patientTimeline = _patientTimeline;
        trial._timelineSources = _timelineSources;
        trial._patientTimelineEndNs = _patientTimelineEndNs;
        trial.PatientTimelineTriggeredCount = PatientTimelineTriggeredCount;
        trial.TimelineOriginTimeNs = TimelineOriginTimeNs;
        trial.PatientTimelineStopped = true;
        var beforePacing = _beforePacing is null ? null : trial.FreezePatientConfiguration(_beforePacing);
        var pending = _pendingSource is not null && !_pendingStopsPatientTimeline
            ? trial.CreateCardiacDefinition(_pendingSource.Configuration, _pendingSource.ElectricalTherapy) : _pendingSource;
        var postShock = _postShockSinus is { } sinus
            ? (trial.CreateCardiacDefinition(sinus.Definition.Configuration, sinus.Definition.ElectricalTherapy), sinus.EffectiveNs)
            : ((LocalMonitorPreviewSession, long)?)null;
        var ventilation = _pendingVentilationSource is not null && trial._realtimeOxygenation is { } oxygenation
            ? PhysiologyIllustrationSource.Create(trial._configuration, ventilation: oxygenation.Ventilation) : _pendingVentilationSource;
        PublishTimelineState(trial);
        ManualVitals = trial.ManualVitals;
        _beforePacing = beforePacing;
        _pendingSource = pending;
        _postShockSinus = postShock;
        _pendingVentilationSource = ventilation;
        PatientTimelineStopped = true;
        TimelineGeneration = generation;
        DataRevision++;
    }

    public bool PatientTimelineCompleted => (_patientTimeline.Count > 0 || _timelineProgram is not null) && !PatientTimelineStopped &&
        PatientTimelineTriggeredCount == _patientTimeline.Count &&
        SimulationTimeNs >= _patientTimelineEndNs;

    // Called after startup discard and before the session is exposed to consumers.
    // Prepared template sources have the same stable ordering as Snapshot(events).
    public void ConfigurePatientTimeline(IReadOnlyList<PatientTimelineEvent> events,
        IReadOnlyList<PatientTimelineSource?>? templateSources = null,
        RealtimeOxygenationConfiguration? oxygenationModel = null)
    {
        if (SimulationTimeNs != 0 || _patientTimeline.Count != 0 || _timelineProgram is not null ||
            _pendingSource is not null || _pendingVentilationSource is not null)
        { throw new InvalidOperationException("PatientTimeline.RequiresRestart"); }
        ConfigurePatientTimelineSequence(events, templateSources, oxygenationModel, 0, false);
    }

    private void ConfigurePatientTimelineCore(IReadOnlyList<PatientTimelineEvent> events,
        ReadOnlyCollection<PatientTimelineSource?>? templateSources, RealtimeOxygenationConfiguration? oxygenationModel,
        long originTimeNs, bool fork, bool continueCurrentTargets = false)
    {
        var owned = events;
        if (_timelineProgram is null && owned.Any(item => item.Treatment is { } treatment &&
            (treatment.ShockSuccessTimelineId is not null || treatment.ShockFailureTimelineId is not null || treatment.PacingTimelineId is not null)))
        { throw new ArgumentException("PatientTimeline.InvalidBranch"); }
        if (templateSources is not null && templateSources.Count != owned.Count)
        { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
        long offset = checked(_sourceTimeOffsetNs + originTimeNs);
        var initial = fork && !continueCurrentTargets ? FreezePatientConfiguration(_configuration) : _configuration;
        if (fork)
        {
            initial = initial with
            {
                AbpTarget = initial.AbpSystolicCentiMmHgCurve is { } systolic
                    ? new(systolic.At(offset), initial.AbpDiastolicCentiMmHgCurve!.At(offset)) : initial.AbpTarget,
                PaTarget = initial.PaSystolicCentiMmHgCurve is { } pulmonary
                    ? new(pulmonary.At(offset), initial.PaDiastolicCentiMmHgCurve!.At(offset)) : initial.PaTarget,
                CvpBaselineCentiMmHg = initial.CvpBaselineCentiMmHgCurve?.At(offset) ?? initial.CvpBaselineCentiMmHg
            };
        }
        VitalValueCurve? Curve(int initialValue, Func<PatientVitalTargets, int?> select,
            VitalValueCurve? previous = null, bool preservePhase = false)
        {
            var changes = owned.Where(item => select(item.Targets).HasValue)
                .Select(item => new VitalValueChange(checked(offset + item.StartTimeNs), item.DurationNs, select(item.Targets)!.Value)).ToArray();
            if (continueCurrentTargets && previous is not null)
            {
                return new(previous.InitialValue, previous.Changes.Where(change => change.StartSimTimeNs <= offset)
                    .Concat(changes).ToArray());
            }
            if (fork && previous is not null)
            {
                initialValue = previous.At(offset);
                if (preservePhase)
                {
                    return new(previous.InitialValue, previous.Changes.Where(change => change.StartSimTimeNs < offset)
                        .Append(new(offset, 0, initialValue)).Concat(changes).ToArray());
                }
            }
            return changes.Length == 0 && previous is null ? null : new(initialValue, changes);
        }
        var initialPlan = initial.ResolvePlan();
        long cardiacPeriod = initialPlan.RateAdjustment?.Rate.PeriodNs ?? initialPlan.SeededRate?.PeriodNs ?? initialPlan.HeartPeriodNs;
        int cardiacRate = (int)((60_000_000_000L + cardiacPeriod / 2) / cardiacPeriod);
        var heartRate = Curve(cardiacRate, target => target.HeartRateBpm, initial.CardiacRateClock?.RatePerMinute, true);
        var respiration = Curve((int)((60_000L + initial.BreathPeriodMilliseconds / 2) / initial.BreathPeriodMilliseconds),
            target => target.RespiratoryRatePerMinute, initial.RespiratoryRateClock?.RatePerMinute, true);
        var abpSys = Curve(initial.AbpTarget?.SystolicCentiMmHg ?? 12000, target => target.Abp?.SystolicCentiMmHg, initial.AbpSystolicCentiMmHgCurve);
        var abpDia = Curve(initial.AbpTarget?.DiastolicCentiMmHg ?? 8000, target => target.Abp?.DiastolicCentiMmHg, initial.AbpDiastolicCentiMmHgCurve);
        var paSys = Curve(initial.PaTarget?.SystolicCentiMmHg ?? 2500, target => target.Pa?.SystolicCentiMmHg, initial.PaSystolicCentiMmHgCurve);
        var paDia = Curve(initial.PaTarget?.DiastolicCentiMmHg ?? 1000, target => target.Pa?.DiastolicCentiMmHg, initial.PaDiastolicCentiMmHgCurve);
        var cvp = Curve(initial.CvpBaselineCentiMmHg, target => target.CvpCentiMmHg, initial.CvpBaselineCentiMmHgCurve);
        var co2 = Curve(initial.Co2EndExpiratoryMmHg * 100, target => target.EtCo2MmHg * 100, initial.Co2EndExpiratoryCentiMmHgCurve);
        if (abpSys is not null && initial.AbpTarget is null || paSys is not null && initial.PaTarget is null)
        { throw new ArgumentException("PatientTimeline.PressureBaselineRequired"); }
        var firstTemperature = owned.FirstOrDefault(item => item.Targets.TemperatureDeciCelsius.HasValue);
        var firstNibp = owned.FirstOrDefault(item => item.Targets.Nibp is not null);
        if (ManualVitals.TemperatureDeciCelsius is null && firstTemperature is { DurationNs: > 0 } ||
            ManualVitals.Nibp is null && firstNibp is { DurationNs: > 0 })
        { throw new ArgumentException("PatientTimeline.ManualBaselineRequired"); }
        var temperature = Curve(ManualVitals.TemperatureDeciCelsius ?? firstTemperature?.Targets.TemperatureDeciCelsius ?? 365,
            target => target.TemperatureDeciCelsius, continueCurrentTargets ? _timelineTemperature : null);
        var baselineNibp = ManualVitals.Nibp ?? firstNibp?.Targets.Nibp;
        var nibpSys = Curve(baselineNibp?.SystolicMmHg ?? 120, target => target.Nibp?.SystolicMmHg, continueCurrentTargets ? _timelineNibpSystolic : null);
        var nibpDia = Curve(baselineNibp?.DiastolicMmHg ?? 80, target => target.Nibp?.DiastolicMmHg, continueCurrentTargets ? _timelineNibpDiastolic : null);
        var nibpMean = Curve(baselineNibp?.MeanMmHg ?? 93, target => target.Nibp?.MeanMmHg, continueCurrentTargets ? _timelineNibpMean : null);
        var model = _realtimeConfiguration ?? oxygenationModel;
        var currentRealtime = _realtimeConfiguration is { } configured && _realtimeOxygenation is { } running
            ? configured with { Ventilation = running.Ventilation, OxygenDemandMultiplier = running.Snapshot.OxygenDemandMultiplier }
            : _realtimeConfiguration;
        if (owned.Any(item => item.Ventilation is not null) && model is null)
        { throw new ArgumentException("PatientTimeline.OxygenationModelRequired"); }
        if (owned.Any(item => item.Targets.SaturationMilliPercent is not null) && _measurements is null)
        { throw new ArgumentException("Preview.OpticsRequireMeasurements"); }

        VitalValueCurve? SegmentCurve(VitalValueCurve? curve, long fromSourceTimeNs, long toExclusiveSourceTimeNs)
        {
            if (curve is null) { return null; }
            // The outgoing template never validates an instantaneous target
            // belonging to its successor. Its unfinished ramp reaches the
            // left-hand boundary value, then remains there for retained tails.
            int Before(long timeNs) => new VitalValueCurve(curve.InitialValue,
                curve.Changes.Where(change => change.StartSimTimeNs < timeNs).ToArray()).At(timeNs);
            if (toExclusiveSourceTimeNs == fromSourceTimeNs)
            { return new(fromSourceTimeNs == 0 ? curve.InitialValue : Before(fromSourceTimeNs), []); }
            var changes = new List<VitalValueChange>();
            for (int index = 0; index < curve.Changes.Count; index++)
            {
                var change = curve.Changes[index];
                if (change.StartSimTimeNs >= toExclusiveSourceTimeNs) { break; }
                long next = index + 1 < curve.Changes.Count ? curve.Changes[index + 1].StartSimTimeNs : long.MaxValue;
                if (next <= fromSourceTimeNs) { continue; }
                if (change.DurationNs == 0)
                {
                    if (change.StartSimTimeNs > fromSourceTimeNs) { changes.Add(change); }
                    continue;
                }
                long begin = Math.Max(fromSourceTimeNs, change.StartSimTimeNs);
                long finish = Math.Min(Math.Min(change.StartSimTimeNs + change.DurationNs, next), toExclusiveSourceTimeNs);
                if (finish > begin) { changes.Add(new(begin, finish - begin, Before(finish))); }
            }
            return new(curve.At(fromSourceTimeNs), changes);
        }
        long NextTemplateTime(int afterIndex) => owned.Skip(afterIndex + 1).FirstOrDefault(item => item.ChangesTemplate) is { } next
            ? checked(offset + next.StartTimeNs) : long.MaxValue;
        PhysiologyIllustrationConfiguration WithCurves(PhysiologyIllustrationConfiguration configuration,
            long fromSourceTimeNs, long toExclusiveSourceTimeNs, bool enteringTemplate = false)
        {
            var plan = configuration.ResolvePlan();
            bool supportsHeart = plan.Pacing is null && plan.CardiacActivity != CardiacActivity.Absent &&
                plan.IndependentVentricularPeriodNs is null &&
                plan.ConductionPattern is not (AvConductionPattern.AtrialFlutterIllustration or AvConductionPattern.VariableAtrialFlutterIllustration) &&
                plan.ConductionPattern is not (AvConductionPattern.VentricularFlutterIllustration or
                    AvConductionPattern.VentricularFibrillationCoarseIllustration or AvConductionPattern.VentricularFibrillationFineIllustration);
            long reference = plan.RateAdjustment?.Rate.PeriodNs ?? plan.SeededRate?.PeriodNs ?? plan.HeartPeriodNs;
            return configuration with
            {
                CardiacRateClock = heartRate is null || !supportsHeart ? null : new(reference, heartRate),
                RespiratoryRateClock = respiration is null ? null : new(plan.BreathPeriodNs, respiration),
                AbpTarget = abpSys is not null && enteringTemplate
                    ? new(abpSys.At(fromSourceTimeNs), abpDia!.At(fromSourceTimeNs)) : configuration.AbpTarget,
                PaTarget = paSys is not null && enteringTemplate
                    ? new(paSys.At(fromSourceTimeNs), paDia!.At(fromSourceTimeNs)) : configuration.PaTarget,
                AbpSystolicCentiMmHgCurve = SegmentCurve(abpSys, fromSourceTimeNs, toExclusiveSourceTimeNs),
                AbpDiastolicCentiMmHgCurve = SegmentCurve(abpDia, fromSourceTimeNs, toExclusiveSourceTimeNs),
                PaSystolicCentiMmHgCurve = SegmentCurve(paSys, fromSourceTimeNs, toExclusiveSourceTimeNs),
                PaDiastolicCentiMmHgCurve = SegmentCurve(paDia, fromSourceTimeNs, toExclusiveSourceTimeNs),
                CvpBaselineCentiMmHgCurve = SegmentCurve(cvp, fromSourceTimeNs, toExclusiveSourceTimeNs),
                Co2EndExpiratoryCentiMmHgCurve = SegmentCurve(co2, fromSourceTimeNs, toExclusiveSourceTimeNs)
            };
        }
        LocalMonitorPreviewSession Definition(PhysiologyIllustrationConfiguration configuration,
            Monitor.Application.Therapy.EcgElectricalTherapyProfile? therapy, RealtimeOxygenationConfiguration? realtime)
            => new(configuration, Display, enableMeasurements: _measurements is not null,
                opticalSaturationMilliPercent: realtime is null ? _authoredOpticalSaturationMilliPercent : null,
                opticalModulationPermille: _opticalModulationPermille,
                realtimeOxygenation: realtime, manualVitals: ManualVitals, electricalTherapy: therapy,
                pressureTransducers: PressureTransducers, enableTwelveLead: _ecgSource is not null);

        var configuration = WithCurves(initial, fork ? offset : 0, NextTemplateTime(-1));
        var initialDefinition = Definition(configuration, ElectricalTherapy, currentRealtime);
        ValidateSourceDefinition(initialDefinition);
        var prepared = new LocalMonitorPreviewSession?[owned.Count];
        var currentTherapy = ElectricalTherapy;
        var realtime = currentRealtime;
        for (int index = 0; index < owned.Count; index++)
        {
            var item = owned[index];
            if (item.ChangesTemplate)
            {
                var source = templateSources?[index] ?? throw new ArgumentException("PatientTimeline.InvalidTemplate");
                var template = _timelineProgram is null ? source.Configuration : MergeTimelineTemplate(configuration, source.Configuration, item);
                configuration = WithCurves(template, checked(offset + item.StartTimeNs), NextTemplateTime(index), enteringTemplate: true);
                if (item.EcgTemplateId is not null || _timelineProgram is null) { currentTherapy = source.ElectricalTherapy; }
            }
            if (item.Targets.HeartRateBpm.HasValue && configuration.CardiacRateClock is null)
            { throw new ArgumentException("PatientTimeline.UnsupportedHeartRate"); }
            if (item.Ventilation is { } ventilation)
            { realtime = (realtime ?? model!) with { Ventilation = ventilation }; }
            if (item.ChangesTemplate || item.Ventilation is not null)
            {
                prepared[index] = Definition(configuration, currentTherapy, realtime);
                ValidateSourceDefinition(prepared[index]!);
            }
        }
        // Validate and prepare every definition before publishing even the first curve.
        var sourceTrial = _source.Fork();
        sourceTrial.ContinueWith(initialDefinition._source);
        var ecgTrial = _ecgSource?.Fork();
        ecgTrial?.ContinueWith(initialDefinition._ecgSource!);
        var oxygenTrial = _realtimeOxygenation?.Fork();
        if (oxygenTrial is not null && currentRealtime is { } currentModel)
        {
            oxygenTrial.ChangeTransport(PhysiologyIllustrationSource.CreateTransport(initialDefinition._configuration,
                currentModel.Ventilation, currentModel.ReferenceStrokeVolumeMicroliters), offset, currentModel.OxygenDemandMultiplier);
        }
        _source = sourceTrial;
        _ecgSource = ecgTrial;
        _realtimeOxygenation = oxygenTrial;
        _configuration = initialDefinition._configuration;
        _patientTimeline = owned;
        _patientTimelineEndNs = checked(originTimeNs + TimelineEnd(owned));
        if (continueCurrentTargets)
        {
            long End(VitalValueCurve? curve) => curve is { Changes.Count: > 0 }
                ? checked(curve.Changes[^1].StartSimTimeNs + curve.Changes[^1].DurationNs - _sourceTimeOffsetNs) : 0;
            long continuedEnd = new[] { heartRate, respiration, abpSys, abpDia, paSys, paDia, cvp, co2,
                temperature, nibpSys, nibpDia, nibpMean }.Select(End).Max();
            long opticalEnd = End(_timelineOpticalCurve);
            if (owned.FirstOrDefault(item => item.Ventilation is not null || item.Targets.SaturationMilliPercent is not null) is { } opticalChange)
            { opticalEnd = Math.Min(opticalEnd, checked(originTimeNs + opticalChange.StartTimeNs)); }
            _patientTimelineEndNs = Math.Max(_patientTimelineEndNs, Math.Max(continuedEnd, opticalEnd));
        }
        _timelineSources = prepared;
        _timelineTemperature = temperature;
        _timelineNibpSystolic = nibpSys;
        _timelineNibpDiastolic = nibpDia;
        _timelineNibpMean = nibpMean;
        _timelineTemperatureStartsNs = continueCurrentTargets && _timelineTemperatureStartsNs is { } temperatureStart
            ? temperatureStart : firstTemperature is null ? null : checked(originTimeNs + firstTemperature.StartTimeNs);
        _timelineNibpStartsNs = continueCurrentTargets && _timelineNibpStartsNs is { } nibpStart
            ? nibpStart : firstNibp is null ? null : checked(originTimeNs + firstNibp.StartTimeNs);
        TimelineOriginTimeNs = originTimeNs;
        PatientTimelineTriggeredCount = 0;
        PatientTimelineStopped = false;
    }

    private static long TimelineEnd(IReadOnlyList<PatientTimelineEvent> events)
    {
        if (events.Count == 0) { return 0; }
        Func<PatientVitalTargets, bool>[] hasTarget =
        [
            target => target.HeartRateBpm.HasValue, target => target.RespiratoryRatePerMinute.HasValue,
            target => target.SaturationMilliPercent.HasValue, target => target.EtCo2MmHg.HasValue,
            target => target.Abp is not null, target => target.Pa is not null,
            target => target.CvpCentiMmHg.HasValue, target => target.Nibp is not null,
            target => target.TemperatureDeciCelsius.HasValue
        ];
        long?[] ends = new long?[hasTarget.Length];
        foreach (var item in events)
        {
            for (int index = 0; index < hasTarget.Length; index++)
            {
                if (hasTarget[index](item.Targets)) { ends[index] = item.StartTimeNs + item.DurationNs; }
            }
            if (item.Ventilation is not null) { ends[2] = null; }
        }
        long end = Math.Max(events[^1].StartTimeNs, ends.Where(value => value.HasValue)
            .Select(value => value!.Value).DefaultIfEmpty(0).Max());
        return end;
    }

    private long? NextPatientTimelineTimeNs => !PatientTimelineStopped && PatientTimelineTriggeredCount < _patientTimeline.Count
        ? _timelineEventTimesNs[PatientTimelineTriggeredCount] : null;

    private void AdvancePatientTimeline()
    {
        UpdatePatientTimelineManualValues();
        while (NextPatientTimelineTimeNs is { } start && start <= SimulationTimeNs)
        {
            int index = PatientTimelineTriggeredCount;
            var item = _patientTimeline[index];
            long sourceTime = checked(SimulationTimeNs + _sourceTimeOffsetNs);
            int? saturation = _timelineOpticalCurve?.At(sourceTime) ?? _realtimeOxygenation?.Snapshot.SaturationMilliPercent ??
                _authoredOpticalSaturationMilliPercent;
            if (_timelineSources[index] is { } definition)
            {
                if (_timelineProgram is not null) { definition = PrepareTimelineEventSource(definition, item); }
                var pending = _pendingSource;
                long? pendingTime = PendingSourceTimeNs;
                string pendingDescription = _pendingSourceDescription;
                bool pendingStopsTimeline = _pendingStopsPatientTimeline;
                var postShock = _postShockSinus;
                // A timeline event is already validated and lies on the source boundary.
                definition.ManualVitals = ManualVitals;
                _pendingSource = definition;
                _pendingStopsPatientTimeline = false;
                PendingSourceTimeNs = SimulationTimeNs;
                _pendingSourceDescription = item.Name;
                if (_timelineProgram is null || item.EcgTemplateId is not null) { _beforePacing = null; }
                _postShockSinus = null;
                ActivatePendingSource();
                _pendingSource = pending;
                PendingSourceTimeNs = pendingTime;
                _pendingSourceDescription = pendingDescription;
                _pendingStopsPatientTimeline = pendingStopsTimeline;
                _postShockSinus = postShock;
            }
            if (item.Ventilation is not null) { _timelineOpticalCurve = null; }
            if (item.Targets.SaturationMilliPercent is { } target)
            {
                _timelineOpticalCurve = new(saturation ?? target,
                    [new(sourceTime, item.DurationNs, target)]);
            }
            if (item.Treatment is { } treatment) { _currentTimelineTreatment = treatment; }
            RecordEvent(PreviewRecordEventKind.PatientTimelineEvent, item.Name);
            PatientTimelineTriggeredCount++;
        }
        UpdatePatientTimelineManualValues();
    }

    private void UpdatePatientTimelineManualValues()
    {
        if (PatientTimelineStopped || _patientTimeline.Count == 0) { return; }
        long time = checked(SimulationTimeNs + _sourceTimeOffsetNs);
        int? temperature = _timelineTemperatureStartsNs is { } temperatureStart && SimulationTimeNs >= temperatureStart
            ? _timelineTemperature!.At(time) : ManualVitals.TemperatureDeciCelsius;
        var nibp = _timelineNibpStartsNs is { } nibpStart && SimulationTimeNs >= nibpStart
            ? new ManualNibp(_timelineNibpSystolic!.At(time), _timelineNibpDiastolic!.At(time), _timelineNibpMean!.At(time)) : ManualVitals.Nibp;
        ManualVitals = new(nibp, temperature, ManualVitals.Custom1, ManualVitals.Custom2);
    }

    private void StopPatientTimelineForSourceChange()
    {
        if (_patientTimeline.Count == 0 && _timelineProgram is null) { return; }
        PatientTimelineStopped = true;
        _timelineOpticalCurve = null;
    }

    private VitalValueCurve? FreezePatientCurve(VitalValueCurve? curve)
    {
        if (curve is null) { return null; }
        long time = checked(SimulationTimeNs + _sourceTimeOffsetNs);
        return new(curve.InitialValue, curve.Changes.Where(change => change.StartSimTimeNs < time)
            .Append(new(time, 0, curve.At(time))).ToArray());
    }

    private PhysiologyIllustrationConfiguration FreezePatientConfiguration(PhysiologyIllustrationConfiguration configuration)
    {
        PhysiologyRateClock? FreezeClock(PhysiologyRateClock? clock) => clock is null ? null :
            new(clock.ReferencePeriodNs, FreezePatientCurve(clock.RatePerMinute)!);
        return configuration with
        {
            CardiacRateClock = FreezeClock(configuration.CardiacRateClock),
            RespiratoryRateClock = FreezeClock(_configuration.RespiratoryRateClock),
            AbpSystolicCentiMmHgCurve = FreezePatientCurve(_configuration.AbpSystolicCentiMmHgCurve),
            AbpDiastolicCentiMmHgCurve = FreezePatientCurve(_configuration.AbpDiastolicCentiMmHgCurve),
            PaSystolicCentiMmHgCurve = FreezePatientCurve(_configuration.PaSystolicCentiMmHgCurve),
            PaDiastolicCentiMmHgCurve = FreezePatientCurve(_configuration.PaDiastolicCentiMmHgCurve),
            CvpBaselineCentiMmHgCurve = FreezePatientCurve(_configuration.CvpBaselineCentiMmHgCurve),
            Co2EndExpiratoryCentiMmHgCurve = FreezePatientCurve(_configuration.Co2EndExpiratoryCentiMmHgCurve)
        };
    }

    private PhysiologyIllustrationConfiguration ContinuePatientConfiguration(PhysiologyIllustrationConfiguration configuration)
    {
        var plan = (configuration with { CardiacRateClock = null }).ResolvePlan();
        var cardiac = configuration.CardiacRateClock;
        if (_configuration.CardiacRateClock is { } existing && CardiacRateAdjustment.Supports(plan) &&
            plan.IndependentVentricularPeriodNs is null && !AtrialFlutterReference.IsPattern(plan.ConductionPattern))
        {
            cardiac = new(plan.RateAdjustment?.Rate.PeriodNs ?? plan.SeededRate?.PeriodNs ?? plan.HeartPeriodNs, existing.RatePerMinute);
        }
        return configuration with
        {
            CardiacRateClock = cardiac,
            AbpTarget = _configuration.AbpTarget,
            PaTarget = _configuration.PaTarget,
            CvpBaselineCentiMmHg = _configuration.CvpBaselineCentiMmHg,
            Co2EndExpiratoryMmHg = _configuration.Co2EndExpiratoryMmHg,
            RespiratoryRateClock = _configuration.RespiratoryRateClock,
            AbpSystolicCentiMmHgCurve = _configuration.AbpSystolicCentiMmHgCurve,
            AbpDiastolicCentiMmHgCurve = _configuration.AbpDiastolicCentiMmHgCurve,
            PaSystolicCentiMmHgCurve = _configuration.PaSystolicCentiMmHgCurve,
            PaDiastolicCentiMmHgCurve = _configuration.PaDiastolicCentiMmHgCurve,
            CvpBaselineCentiMmHgCurve = _configuration.CvpBaselineCentiMmHgCurve,
            Co2EndExpiratoryCentiMmHgCurve = _configuration.Co2EndExpiratoryCentiMmHgCurve
        };
    }

    private sealed class TimelineOxygenation(VitalValueCurve curve) : IArterialOxygenationSource
    {
        public ArterialOxygenationSample ReadAt(long sourceSimTimeNs) => new(sourceSimTimeNs, curve.At(sourceSimTimeNs));
    }
}
