// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

public sealed partial class LocalMonitorPreviewSession
{
    private long?[] _timelineEventTimesNs = [];
    private IReadOnlyList<PatientTimelineSource?> _timelineAuthoredSources = [];
    private RealtimeOxygenationConfiguration? _timelineNodesOxygenation;

    public int? WaitingPatientTimelineEventIndex => !PatientTimelineStopped &&
        PatientTimelineTriggeredCount < _patientTimeline.Count &&
        _patientTimeline[PatientTimelineTriggeredCount].Trigger == PatientTimelineTrigger.Manual &&
        _timelineEventTimesNs[PatientTimelineTriggeredCount] is null ? PatientTimelineTriggeredCount : null;

    public PatientTimelineEvent? WaitingPatientTimelineEvent => WaitingPatientTimelineEventIndex is { } index
        ? _patientTimeline[index] : null;

    public bool TriggerPatientTimelineManualEvent(ulong expectedTimelineGeneration, int expectedEventIndex)
    {
        if (expectedTimelineGeneration != TimelineGeneration || WaitingPatientTimelineEventIndex != expectedEventIndex)
        { return false; }
        // No clock advance is needed: the teacher can enter a waiting node
        // while paused. Prepare every newly reachable source before publication.
        var trial = (LocalMonitorPreviewSession)MemberwiseClone();
        trial.PrepareTimelineNodeSegment(_patientTimeline, _timelineAuthoredSources, _timelineNodesOxygenation,
            expectedEventIndex, SimulationTimeNs, fork: true, continueCurrentTargets: true);
        PublishTimelineState(trial);
        AdvancePatientTimeline();
        DataRevision++;
        return true;
    }

    private void ConfigurePatientTimelineSequence(IReadOnlyList<PatientTimelineEvent> events,
        IReadOnlyList<PatientTimelineSource?>? templateSources, RealtimeOxygenationConfiguration? oxygenationModel,
        long originTimeNs, bool fork)
    {
        var owned = PatientTimelineEvent.Snapshot(events);
        if (templateSources is not null && templateSources.Count != owned.Count)
        { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
        var sources = Array.AsReadOnly(templateSources?.ToArray() ?? new PatientTimelineSource?[owned.Count]);
        for (int index = 0; index < owned.Count; index++)
        {
            var item = owned[index];
            if (item.ChangesTemplate && sources[index] is null)
            { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
            if (sources[index] is { } source)
            {
                ArgumentNullException.ThrowIfNull(source.Configuration);
                _ = source.Configuration.ResolvePlan();
                source.ElectricalTherapy?.Validate();
            }
            if (_timelineProgram is null && item.Treatment is { } treatment &&
                (treatment.ShockSuccessTimelineId is not null || treatment.ShockFailureTimelineId is not null || treatment.PacingTimelineId is not null))
            { throw new ArgumentException("PatientTimeline.InvalidBranch"); }
            if (item.Ventilation is not null && _realtimeConfiguration is null && oxygenationModel is null)
            { throw new ArgumentException("PatientTimeline.OxygenationModelRequired"); }
            if (item.Targets.SaturationMilliPercent is not null && _measurements is null)
            { throw new ArgumentException("Preview.OpticsRequireMeasurements"); }
        }
        PrepareTimelineNodeSegment(owned, sources, oxygenationModel, 0, originTimeNs, fork);
    }

    private void PrepareTimelineNodeSegment(IReadOnlyList<PatientTimelineEvent> events,
        IReadOnlyList<PatientTimelineSource?> templateSources, RealtimeOxygenationConfiguration? oxygenationModel,
        int firstIndex, long originTimeNs, bool fork, bool continueCurrentTargets = false)
    {
        var segment = new List<PatientTimelineEvent>();
        var sources = new List<PatientTimelineSource?>();
        long previousTimeNs = 0;
        for (int index = firstIndex; index < events.Count; index++)
        {
            var item = events[index];
            if (item.Trigger == PatientTimelineTrigger.Manual && !(continueCurrentTargets && index == firstIndex))
            { break; }
            long relativeTimeNs = item.Trigger switch
            {
                PatientTimelineTrigger.Manual => 0,
                PatientTimelineTrigger.AfterPrevious => checked(previousTimeNs + item.StartTimeNs),
                _ => item.StartTimeNs
            };
            segment.Add(item with { StartTimeNs = relativeTimeNs });
            sources.Add(templateSources[index]);
            previousTimeNs = relativeTimeNs;
        }
        long?[] eventTimes = continueCurrentTargets ? _timelineEventTimesNs.ToArray() : new long?[events.Count];
        var definitions = continueCurrentTargets ? _timelineSources.ToArray() : new LocalMonitorPreviewSession?[events.Count];
        ConfigurePatientTimelineCore(segment.AsReadOnly(), sources.AsReadOnly(), oxygenationModel,
            originTimeNs, fork, continueCurrentTargets);
        for (int index = 0; index < segment.Count; index++)
        {
            eventTimes[firstIndex + index] = checked(originTimeNs + segment[index].StartTimeNs);
            definitions[firstIndex + index] = _timelineSources[index];
        }
        _patientTimeline = events;
        _timelineSources = definitions;
        _timelineEventTimesNs = eventTimes;
        _timelineAuthoredSources = templateSources;
        _timelineNodesOxygenation = oxygenationModel;
        PatientTimelineTriggeredCount = firstIndex;
    }
}
