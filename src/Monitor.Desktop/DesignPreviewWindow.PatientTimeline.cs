// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewWindow
{
    private (LocalMonitorPreviewSession Session, ulong Generation, int Index)? _manualTimelineEntry;

    private void EnterManualPatientTimelineNode()
    {
        if (_applying || _manualTimelineEntry is not { } entry || !ReferenceEquals(entry.Session, _session)) { return; }
        _manualTimelineEntry = null;
        try
        {
            if (_session.TriggerPatientTimelineManualEvent(entry.Generation, entry.Index))
            {
                MonitorView.Refresh();
                _monitor.InvalidateVisual();
            }
            UpdateState();
        }
        catch (ArgumentException exception) { SetStatus(TimelineErrorKey(exception)); }
    }
    private sealed record TimelinePresentation(PhysiologyIllustrationConfiguration Physiology,
        ProjectedEcgDemoConfiguration Ecg, WaveformEnvelope[]? Paper, int EcgSelection, int RespirationSelection, int EjectionSelection,
        bool ChangesEcg);
    private sealed record PreparedTimelinePresentation(PatientTimelineProgram Program,
        IReadOnlyDictionary<string, TimelinePresentation?[]> Timelines,
        IReadOnlyDictionary<string, TimelinePresentation?> InitialStates, string FileName);
    private IReadOnlyDictionary<string, TimelinePresentation?[]> _timelinePresentationProgram = new Dictionary<string, TimelinePresentation?[]>();
    private TimelinePresentation?[] _timelinePresentations = [];
    private IReadOnlyDictionary<string, TimelinePresentation?> _timelineInitialPresentations = new Dictionary<string, TimelinePresentation?>();
    private ulong _presentedTimelineGeneration = ulong.MaxValue;
    private int _presentedTimelineCount;
    private string _runningTimelineFileName = "";

    // Capture all authoring controls before the source-preparation worker starts.
    private Func<LocalMonitorPreviewSession, ProjectedEcgDemoConfiguration, PreparedTimelinePresentation> CaptureTimelineInitializer()
    {
        var program = Settings.Timeline.ReadProgramForRestart();
        string fileName = Settings.Timeline.SelectedFileName;
        if (Settings.Timeline.TimelineEnabled.IsChecked != true)
        { return (_, _) => new(program, new Dictionary<string, TimelinePresentation?[]>(), new Dictionary<string, TimelinePresentation?>(), fileName); }
        var allEvents = program.Timelines.SelectMany(item => item.InitialState is { } state ? item.Events.Prepend(state) : item.Events).ToArray();
        int initialEcg = Settings.EcgSelection, initialRespiration = Settings.RespirationSelection,
            initialEjection = Settings.EjectionSelection, initialPleth = Settings.PlethSelection, initialCapnogram = Settings.CapnogramSelection;
        var therapies = allEvents.Where(item => item.EcgTemplateId is not null).Select(item => item.EcgTemplateId!)
            .Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => Settings.ElectricalConversion.Profile(id) with
            { PacingAllowed = Settings.PacingPermissions.Allows(id), AedSettings = Settings.AedConversion.Profile(id).Settings }, StringComparer.Ordinal);
        var oxygenation = allEvents.Any(item => item.Ventilation is not null) ? Settings.Oxygenation.ReadConfiguration() : null;
        return (session, initialEcgConfiguration) =>
        {
            var allSources = new Dictionary<string, IReadOnlyList<PatientTimelineSource?>>(StringComparer.Ordinal);
            var allPresentations = new Dictionary<string, TimelinePresentation?[]>(StringComparer.Ordinal);
            var initialSources = new Dictionary<string, PatientTimelineSource?>(StringComparer.Ordinal);
            var initialPresentations = new Dictionary<string, TimelinePresentation?>(StringComparer.Ordinal);
            foreach (var timeline in program.Timelines)
            {
                var events = timeline.Events;
                var sources = new PatientTimelineSource?[events.Count];
                var presentation = new TimelinePresentation?[events.Count];
                var configuration = session.Configuration;
                var ecgConfiguration = initialEcgConfiguration;
                var therapy = session.ElectricalTherapy;
                int ecg = initialEcg, respiration = initialRespiration, ejection = initialEjection,
                    pleth = initialPleth, capnogram = initialCapnogram;
                bool inheritedCardiacTemplate = timeline.Id != program.InitialTimelineId;
                for (int index = -1; index < events.Count; index++)
                {
                    var item = index < 0 ? timeline.InitialState : events[index];
                    if (item is null || !item.ChangesTemplate) { continue; }
                    int previousEjection = ejection;
                    if (item.EcgTemplateId is { } id)
                    {
                        ecg = Enumerable.Range(0, DesignPreviewSettings.EcgChoiceCount)
                            .FirstOrDefault(value => DesignPreviewSettings.EcgTemplateKey(value) == id, -1);
                        if (ecg < 0) { throw new ArgumentException("PatientTimeline.InvalidTemplate"); }
                        therapy = therapies[id];
                        inheritedCardiacTemplate = false;
                    }
                    respiration = item.RespirationTemplate ?? respiration;
                    ejection = item.EjectionTemplate ?? ejection;
                    pleth = item.PlethTemplate ?? pleth;
                    capnogram = item.CapnogramTemplate ?? capnogram;
                    int referenceEcg = inheritedCardiacTemplate && ejection is 1 or 2 ? (ejection == 1 ? 2 : 0) : ecg;
                    var resolved = ResolveStyle(referenceEcg, respiration, ejection, pleth, capnogram);
                    var next = item.EcgTemplateId is null ? configuration : InheritTimelineVitals(resolved.Physiology, configuration);
                    if (item.EcgTemplateId is not null) { ecgConfiguration = resolved.Ecg; }
                    if (item.RespirationTemplate is not null)
                    {
                        next = next with
                        {
                            RespiratoryPattern = resolved.Physiology.RespiratoryPattern,
                            RespiratoryActivity = resolved.Physiology.RespiratoryActivity,
                            RespirationIllustration = resolved.Physiology.RespirationIllustration,
                            ActivityAfterBreaths = resolved.Physiology.ActivityAfterBreaths,
                            ActivityDurationBreaths = resolved.Physiology.ActivityDurationBreaths
                        };
                    }
                    if (item.EjectionTemplate is not null)
                    {
                        next = next with
                        {
                            PressureMeasurement = resolved.Physiology.PressureMeasurement,
                            VentricularMechanicalEnabled = resolved.Physiology.VentricularMechanicalEnabled
                        };
                        if (previousEjection == 2 || ejection == 2)
                        {
                            next = next with { VentricularConductionRatio = resolved.Physiology.VentricularConductionRatio };
                            ecgConfiguration = ecgConfiguration with { VentricularConductionRatio = resolved.Ecg.VentricularConductionRatio };
                        }
                    }
                    if (item.PlethTemplate is not null) { next = next with { PlethShape = resolved.Physiology.PlethShape }; }
                    if (item.CapnogramTemplate is not null)
                    {
                        next = next with
                        {
                            CapnogramIllustration = resolved.Physiology.CapnogramIllustration,
                            Co2BaselineMmHg = resolved.Physiology.Co2BaselineMmHg,
                            Co2PlateauStartCentiMmHg = resolved.Physiology.Co2PlateauStartCentiMmHg,
                            Co2RiseMilliseconds = resolved.Physiology.Co2RiseMilliseconds,
                            Co2FallMilliseconds = resolved.Physiology.Co2FallMilliseconds
                        };
                    }
                    configuration = next;
                    bool changesPaper = item.EcgTemplateId is not null || item.EjectionTemplate is not null && (previousEjection == 2 || ejection == 2);
                    var shown = new TimelinePresentation(next, ecgConfiguration, changesPaper ? CapturePaper(ecgConfiguration) : null,
                        ecg, respiration, ejection, item.EcgTemplateId is not null);
                    if (index < 0)
                    {
                        initialSources[timeline.Id] = new(next, therapy);
                        initialPresentations[timeline.Id] = shown;
                    }
                    else
                    {
                        sources[index] = new(next, therapy);
                        presentation[index] = shown;
                    }
                }
                allSources[timeline.Id] = sources;
                allPresentations[timeline.Id] = presentation;
            }
            session.ConfigurePatientTimelineProgram(program, allSources, oxygenation, initialSources);
            return new(program, allPresentations, initialPresentations, fileName);
        };
    }

    // Replacing ECG morphology preserves independent respiratory/pressure authoring.
    private static PhysiologyIllustrationConfiguration InheritTimelineVitals(PhysiologyIllustrationConfiguration next,
        PhysiologyIllustrationConfiguration previous) => next with
        {
            BreathPeriodMilliseconds = previous.BreathPeriodMilliseconds,
            InspirationMilliseconds = previous.InspirationMilliseconds,
            RespAmplitudeCounts = previous.RespAmplitudeCounts,
            Co2PlateauStartCentiMmHg = previous.Co2PlateauStartCentiMmHg,
            Co2BaselineMmHg = previous.Co2BaselineMmHg,
            Co2EndExpiratoryMmHg = previous.Co2EndExpiratoryMmHg,
            Co2DeadSpaceMilliseconds = previous.Co2DeadSpaceMilliseconds,
            Co2RiseMilliseconds = previous.Co2RiseMilliseconds,
            Co2FallMilliseconds = previous.Co2FallMilliseconds,
            Co2TransportDelayMilliseconds = previous.Co2TransportDelayMilliseconds,
            Co2DispersionStepMilliseconds = previous.Co2DispersionStepMilliseconds,
            InspiratoryPauseMilliseconds = previous.InspiratoryPauseMilliseconds,
            ExpiratoryPauseMilliseconds = previous.ExpiratoryPauseMilliseconds,
            RespCardiacArtifactCounts = previous.RespCardiacArtifactCounts,
            RespiratoryActivity = previous.RespiratoryActivity,
            ActivityAfterBreaths = previous.ActivityAfterBreaths,
            ActivityDurationBreaths = previous.ActivityDurationBreaths,
            RespiratoryPattern = previous.RespiratoryPattern,
            RespirationIllustration = previous.RespirationIllustration,
            CapnogramIllustration = previous.CapnogramIllustration,
            PlethShape = previous.PlethShape,
            PressureMeasurement = previous.PressureMeasurement,
            AbpPulsePermille = previous.AbpPulsePermille,
            PaPulsePermille = previous.PaPulsePermille,
            AbpTarget = previous.AbpTarget,
            PaTarget = previous.PaTarget,
            PressureVariation = previous.PressureVariation,
            CvpBaselineCentiMmHg = previous.CvpBaselineCentiMmHg,
            SeededCo2 = previous.SeededCo2,
            EcgInterference = previous.EcgInterference
        };

    private void SetTimelinePresentation(PreparedTimelinePresentation presentation)
    {
        _timelinePresentationProgram = presentation.Timelines;
        _timelineInitialPresentations = presentation.InitialStates;
        _runningTimelineFileName = presentation.FileName;
        _presentedTimelineGeneration = ulong.MaxValue;
        _presentedTimelineCount = 0;
        Settings.Timeline.MarkApplied(presentation.Program);
    }

    private void RefreshPatientTimeline()
    {
        var waiting = _session.WaitingPatientTimelineEvent;
        Settings.Timeline.TriggerManual.IsVisible = waiting is not null;
        Settings.Timeline.TriggerManual.IsEnabled = waiting is not null && !_applying;
        _manualTimelineEntry = waiting is null ? null : (_session, _session.TimelineGeneration, _session.WaitingPatientTimelineEventIndex!.Value);
        Settings.Timeline.TriggerManual.Content = Localization.Format("timeline.enterManualNamed", waiting?.Name ?? "");
        if (_session.TimelineGeneration != _presentedTimelineGeneration)
        {
            _presentedTimelineGeneration = _session.TimelineGeneration;
            _presentedTimelineCount = 0;
            _timelinePresentations = _session.CurrentTimelineId is { } id && _timelinePresentationProgram.TryGetValue(id, out var presentations)
                ? presentations : [];
            if (!_session.PatientTimelineStopped && _session.CurrentTimelineId is { } initialId &&
                _timelineInitialPresentations.TryGetValue(initialId, out var initial) && initial is not null)
            { ApplyTimelinePresentation(initial); }
        }
        int count = _session.PatientTimelineTriggeredCount;
        if (!_session.PatientTimelineStopped && count > _presentedTimelineCount)
        {
            for (int index = _presentedTimelineCount; index < count && index < _timelinePresentations.Length; index++)
            {
                if (_timelinePresentations[index] is not { } presentation) { continue; }
                ApplyTimelinePresentation(presentation);
            }
            _presentedTimelineCount = count;
        }
        int total = _session.PatientTimeline.Count;
        Settings.Timeline.Progress.IsVisible = _session.CurrentTimelineId is not null && (total > 0 || _session.TimelineBranchError is not null);
        string execution = _session.PatientTimelineStopped
            ? Localization.Format("timeline.stopped", count, total)
            : _session.PatientTimelineCompleted ? Localization.Format("timeline.finished", total)
            : Localization.Format("timeline.progress", count, total, (_session.SimulationTimeNs - _session.TimelineOriginTimeNs) / 1_000_000_000m);
        Settings.Timeline.Progress.Text = _session.CurrentTimelineId is null ? ""
            : Localization.Format("timeline.runningSummary", _runningTimelineFileName, _session.CurrentTimelineName, execution);
        if (_session.TimelineBranchError is { } reason)
        {
            Settings.Timeline.Progress.Text += "\n" + Localization.Format("timeline.branchRejected",
                _session.TimelineBranchErrorTimelineName, Localization.Get(TimelineErrorKey(reason)));
        }
    }

    private void ApplyTimelinePresentation(TimelinePresentation presentation)
    {
        if (presentation.Paper is { } paper) { _ecg = paper; }
        if (!presentation.ChangesEcg) { return; }
        if (_session.ActivePacing is null)
        {
            _beforePacingPaper = null;
            _pendingPacingPaper = null;
        }
        Settings.MarkParametersApplied(presentation.Ecg, _session.Configuration,
            presentation.EcgSelection, presentation.RespirationSelection, presentation.EjectionSelection);
    }

    private static string TimelineErrorKey(ArgumentException error) => TimelineErrorKey(error.Message);

    private static string TimelineErrorKey(string reason) => reason switch
    {
        "PatientTimeline.UnsupportedHeartRate" => "timeline.errorHeartRate",
        "PatientTimeline.PressureBaselineRequired" => "timeline.errorPressureBaseline",
        "PatientTimeline.ManualBaselineRequired" => "timeline.errorManualBaseline",
        "PatientTimeline.OxygenationModelRequired" or "PatientTimeline.InvalidVentilation" => "timeline.errorOxygenation",
        "PatientTimeline.InvalidTemplate" => "timeline.errorTemplate",
        "PatientTimeline.InvalidDefinition" => "timeline.invalidDefinition",
        "PatientTimeline.InvalidBranch" => "timeline.invalidBranch",
        "PatientTimeline.InvalidProgram" => "timeline.invalidProgram",
        "Physiology.PressureTargetUnreachable" => "timeline.errorPressureUnreachable",
        "Preview.EjectionRequiresPvc" or "Preview.EjectionRequiresSinus" => "timeline.errorEjection",
        "PatientTimeline.InvalidName" => "timeline.invalidName",
        "PatientTimeline.InvalidTime" => "timeline.invalidTime",
        "PatientTimeline.EmptyEvent" => "timeline.emptyEvent",
        _ => "timeline.invalid"
    };
}
