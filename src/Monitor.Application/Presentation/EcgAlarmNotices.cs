// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

public sealed record EcgAlarmDescriptor(string Id, string Text, string MessageKey, MonitorNoticeLevel Level,
    EcgMonitoringConditions Condition = EcgMonitoringConditions.None)
{
    public TextMessage Message => new(MessageKey);
    public AlarmNotificationSettings DefaultNotification => AlarmNotificationSettings.Default with
    {
        SoundMode = Level == MonitorNoticeLevel.Critical ? AlarmSoundMode.Continuous : AlarmSoundMode.SingleGroup,
        LatchingMode = AlarmLatchingMode.NonLatching
    };
}

// Presentation owner for confirmed detector evidence. HR limit events deliberately
// remain with ConfirmedLimitNotice and its user-configured thresholds/delays.
public sealed class EcgAlarmNotices
{
    public const long OccurrenceDisplayNs = 10_000_000_000;
    public static IReadOnlyList<EcgAlarmDescriptor> Descriptors { get; } = Array.AsReadOnly(new[]
    {
        D("asystole", "ECG · 心搏停止", EcgMonitoringConditions.Asystole, MonitorNoticeLevel.Critical),
        D("vf", "ECG · 疑似心室颤动", EcgMonitoringConditions.SuspectedVentricularFibrillation, MonitorNoticeLevel.Critical),
        D("vt", "ECG · 室性心动过速", EcgMonitoringConditions.VentricularTachycardia, MonitorNoticeLevel.Critical),
        D("pause", "ECG · 心搏暂停", EcgMonitoringConditions.Pause),
        D("missed", "ECG · 漏搏", EcgMonitoringConditions.MissedBeat),
        D("nsvt", "ECG · 非持续性室速", EcgMonitoringConditions.NonSustainedVentricularTachycardia),
        D("ventricular", "ECG · 室性节律", EcgMonitoringConditions.VentricularRhythm),
        D("pvc-run", "ECG · 成串室早", EcgMonitoringConditions.RunPvcs),
        D("pvc-pair", "ECG · 成对室早", EcgMonitoringConditions.PairPvcs),
        D("bigeminy", "ECG · 室早二联律", EcgMonitoringConditions.VentricularBigeminy),
        D("trigeminy", "ECG · 室早三联律", EcgMonitoringConditions.VentricularTrigeminy),
        D("multiform", "ECG · 多形室早", EcgMonitoringConditions.MultiformPvcs),
        D("pvc-rate", "ECG · 室早频发", EcgMonitoringConditions.PvcsPerMinuteHigh),
        D("ron-t", "ECG · R-on-T 室早", EcgMonitoringConditions.RonTPvc),
        D("svt", "ECG · 室上性心动过速", EcgMonitoringConditions.SupraventricularTachycardia),
        D("st-high", "ECG · ST 段抬高", EcgMonitoringConditions.StHigh),
        D("st-low", "ECG · ST 段压低", EcgMonitoringConditions.StLow),
        D("qtc", "ECG · QTc 延长", EcgMonitoringConditions.QtcHigh),
        D("delta-qtc", "ECG · ΔQTc 超限", EcgMonitoringConditions.DeltaQtcHigh),
        D("pacer-capture", "ECG · 起搏未夺获", EcgMonitoringConditions.PacerNotCaptured),
        D("pacer-pacing", "ECG · 未检出起搏", EcgMonitoringConditions.PacerNotPacing),
        D("af", "ECG · 疑似心房颤动"),
        D("irregular", "ECG · 心律不齐"),
        D("af-end", "ECG · 疑似房颤已结束", level: MonitorNoticeLevel.Notice),
        D("irregular-end", "ECG · 心律不齐已结束", level: MonitorNoticeLevel.Notice)
    });
    private static EcgAlarmDescriptor D(string id, string text, EcgMonitoringConditions condition = EcgMonitoringConditions.None,
        MonitorNoticeLevel level = MonitorNoticeLevel.Warning) => new("ecg-" + id, text, "alarm.ecg." + id, level, condition);

    private readonly Dictionary<string, long> _occurrences = new(StringComparer.Ordinal);
    private long? _lastSampleNs;
    private long _eventFrontierNs = -1;
    public AlarmLifecycleJournal Lifecycle { get; } = new(Descriptors.Select(d => d.Id).ToArray());

    public EcgAlarmNotices()
    {
        foreach (var descriptor in Descriptors)
        {
            Lifecycle.ConfigureNotifications(descriptor.Id, descriptor.DefaultNotification.ToPolicy());
            Lifecycle.Attention.Configure(descriptor.Id, descriptor.DefaultNotification.LatchingMode);
        }
    }

    public void Reset(AlarmTransitionReason reason = AlarmTransitionReason.SessionReset)
    {
        _occurrences.Clear();
        Lifecycle.Interrupt(_lastSampleNs ?? 0, reason);
        if (reason is AlarmTransitionReason.SessionReset or AlarmTransitionReason.ClockRewind)
        {
            _lastSampleNs = null;
            _eventFrontierNs = -1;
        }
    }

    public IReadOnlyList<MonitorNotice> Evaluate(bool enabled, LiveMeasurementSnapshot snapshot,
        IReadOnlyList<DetectedEcgMonitoringEvent>? monitoringEvents = null,
        IReadOnlyList<DetectedEcgRhythmEvent>? rhythmEvents = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(snapshot.SampleTimeNs);
        var monitoring = monitoringEvents?.ToArray() ?? [];
        var rhythms = rhythmEvents?.ToArray() ?? [];
        if (monitoring.Any(e => e is null || !Enum.IsDefined(e.Condition) || e.Condition == EcgMonitoringConditions.None ||
            !Enum.IsDefined(e.Transition) || !Enum.IsDefined(e.Interruption) || e.EvidenceFromNs < 0 || e.EvidenceFromNs > e.ConfirmedAtNs || e.ConfirmedAtNs > snapshot.SampleTimeNs) ||
            rhythms.Any(e => e is null || !Enum.IsDefined(e.Kind) || !Enum.IsDefined(e.Transition) || !Enum.IsDefined(e.Interruption) ||
                e.EvidenceFromNs < 0 || e.EvidenceFromNs > e.ConfirmedAtNs || e.ConfirmedAtNs > snapshot.SampleTimeNs))
        { throw new ArgumentException("EcgAlarm.InvalidEvents"); }
        if (_lastSampleNs is { } last && snapshot.SampleTimeNs < last) { Reset(AlarmTransitionReason.ClockRewind); }
        if (_lastSampleNs is { } previous && snapshot.SampleTimeNs - previous > 500_000_000)
        { Reset(AlarmTransitionReason.ObservationGap); }
        _lastSampleNs = snapshot.SampleTimeNs;
        if (!enabled)
        {
            Reset(AlarmTransitionReason.Disabled);
            _eventFrontierNs = snapshot.SampleTimeNs;
            return [];
        }
        // Consume only fresh events; language changes, pause and repeated UI
        // reads cannot replay an old pulse or extend its display interval.
        var interrupted = new HashSet<string>(StringComparer.Ordinal);
        var started = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in monitoring.Where(e => e.ConfirmedAtNs > _eventFrontierNs).OrderBy(e => e.ConfirmedAtNs))
        {
            if (Descriptors.SingleOrDefault(d => d.Condition == e.Condition) is not { } descriptor) { continue; }
            switch (e.Transition)
            {
                case EcgMonitoringTransition.Started:
                    started.Add(descriptor.Id);
                    break;
                case EcgMonitoringTransition.Occurred:
                    Pulse(descriptor.Id, e.ConfirmedAtNs);
                    break;
                case EcgMonitoringTransition.Ended:
                    Lifecycle.Observe(descriptor.Id, e.ConfirmedAtNs, null, false, false);
                    if (started.Contains(descriptor.Id)) { Pulse(descriptor.Id, e.ConfirmedAtNs); }
                    break;
                case EcgMonitoringTransition.Interrupted:
                    Interrupt(descriptor.Id, e.ConfirmedAtNs, Reason(e.Interruption));
                    interrupted.Add(descriptor.Id);
                    break;
            }
        }
        foreach (var e in rhythms.Where(e => e.ConfirmedAtNs > _eventFrontierNs).OrderBy(e => e.ConfirmedAtNs))
        {
            string id = e.Kind == EcgRhythmEventKind.SuspectedAtrialFibrillation ? "ecg-af" : "ecg-irregular";
            if (e.Transition == EcgRhythmTransition.Interrupted)
            {
                Interrupt(id, e.ConfirmedAtNs, Reason(e.Interruption));
                Interrupt(id + "-end", e.ConfirmedAtNs, Reason(e.Interruption));
                interrupted.Add(id);
                interrupted.Add(id + "-end");
            }
            else if (e.Transition == EcgRhythmTransition.Ended) { Pulse(id + "-end", e.ConfirmedAtNs); }
            else { _occurrences.Remove(id + "-end"); }
        }
        _eventFrontierNs = monitoring.Select(e => e.ConfirmedAtNs).Concat(rhythms.Select(e => e.ConfirmedAtNs))
            .Append(_eventFrontierNs).Max();
        // Determine the full eligible set before publishing any indication. This
        // also covers pulses whose detector condition has already ended.
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var activity = Descriptors.ToDictionary(d => d.Id, d => Active(d, snapshot), StringComparer.Ordinal);
        foreach (var descriptor in Descriptors)
        {
            bool occurrence = _occurrences.TryGetValue(descriptor.Id, out long until) && snapshot.SampleTimeNs < until;
            if (!occurrence) { _occurrences.Remove(descriptor.Id); }
            if (activity[descriptor.Id] is { } active && (active || occurrence)) { candidates.Add(descriptor.Id); }
        }
        List<MonitorNotice> notices = [];
        foreach (var descriptor in Descriptors)
        {
            // Clear even an already retained or briefly displayed lower alarm.
            // The detector evidence remains untouched; supersession is not recovery.
            if (IsSuperseded(descriptor.Id, candidates))
            {
                Interrupt(descriptor.Id, snapshot.SampleTimeNs, AlarmTransitionReason.Superseded);
                continue;
            }
            if (activity[descriptor.Id] is null)
            {
                Interrupt(descriptor.Id, snapshot.SampleTimeNs, AlarmTransitionReason.DataUnavailable);
                continue;
            }
            bool show = candidates.Contains(descriptor.Id);
            if (interrupted.Contains(descriptor.Id) && !show) { continue; }
            Lifecycle.Observe(descriptor.Id, snapshot.SampleTimeNs, show ? descriptor.Level : null, false, false);
            if (show)
            { notices.Add(new(descriptor.Id, descriptor.Level, descriptor.Text) { Numeric = MonitorNumeric.HeartRate, Message = descriptor.Message }); }
        }
        return notices.AsReadOnly();
    }

    // Adapted from PIC iX 4.4 IFU 7-10 for the detector-owned conditions.
    // Equal ranks are siblings, not suppressors of each other. ST/QT belongs
    // to independent monitoring and never participates in arrhythmia chains.
    private static (int Chain, int Rank) Priority(string id) => id switch
    {
        "ecg-asystole" => (0, 0),
        "ecg-vf" => (0, 1),
        "ecg-vt" => (0, 2),
        "ecg-nsvt" or "ecg-ventricular" => (1, 0),
        "ecg-pvc-run" => (1, 1),
        "ecg-pvc-pair" => (1, 2),
        "ecg-ron-t" => (1, 3),
        "ecg-bigeminy" => (1, 4),
        "ecg-trigeminy" => (1, 5),
        "ecg-pvc-rate" => (1, 6),
        "ecg-multiform" => (1, 7),
        "ecg-pause" => (2, 0),
        "ecg-missed" or "ecg-pacer-capture" or "ecg-pacer-pacing" => (2, 1),
        "ecg-af" or "ecg-af-end" => (3, 0),
        "ecg-irregular" or "ecg-irregular-end" => (3, 1),
        "ecg-svt" => (4, 0),
        _ => (-1, 0)
    };

    private static bool IsSuperseded(string id, HashSet<string> candidates)
    {
        var (chain, rank) = Priority(id);
        if (chain < 0) { return false; }
        return candidates.Any(candidate =>
        {
            var (higherChain, higherRank) = Priority(candidate);
            return higherChain == 0 && (chain != 0 || higherRank < rank) ||
                higherChain == chain && higherRank < rank;
        });
    }

    private void Pulse(string id, long timeNs)
    {
        Lifecycle.Observe(id, timeNs, null, false, false);
        _occurrences[id] = timeNs > long.MaxValue - OccurrenceDisplayNs ? long.MaxValue : timeNs + OccurrenceDisplayNs;
    }

    private void Interrupt(string id, long timeNs, AlarmTransitionReason reason)
    {
        _occurrences.Remove(id);
        Lifecycle.Interrupt(id, timeNs, reason);
    }

    private static AlarmTransitionReason Reason(EcgRhythmInterruption reason) => reason switch
    {
        EcgRhythmInterruption.StreamDiscontinuity => AlarmTransitionReason.SignalSegmentChanged,
        EcgRhythmInterruption.Relearning => AlarmTransitionReason.ConfigurationChanged,
        _ => AlarmTransitionReason.DataUnavailable
    };

    private static bool? Active(EcgAlarmDescriptor descriptor, LiveMeasurementSnapshot snapshot)
    {
        if (descriptor.Condition == EcgMonitoringConditions.None)
        {
            bool? rhythm = descriptor.Id.StartsWith("ecg-af", StringComparison.Ordinal)
                ? snapshot.EcgRhythm.SuspectedAtrialFibrillation : snapshot.EcgRhythm.IrregularRhythm;
            if (snapshot.EcgRhythm.Status != WaveformMeasurementStatus.Valid || rhythm is null) { return null; }
            return descriptor.Id.EndsWith("-end", StringComparison.Ordinal) ? false : rhythm;
        }
        var reading = snapshot.EcgMonitoring;
        if (reading.Status != WaveformMeasurementStatus.Valid) { return null; }
        if (descriptor.Condition is EcgMonitoringConditions.PacerNotCaptured or EcgMonitoringConditions.PacerNotPacing && !reading.PacingEvidenceAvailable)
        { return null; }
        if (descriptor.Condition is EcgMonitoringConditions.StHigh or EcgMonitoringConditions.StLow && reading.Repolarization.StStatus != WaveformMeasurementStatus.Valid ||
            descriptor.Condition is EcgMonitoringConditions.QtcHigh or EcgMonitoringConditions.DeltaQtcHigh && reading.Repolarization.QtStatus != WaveformMeasurementStatus.Valid)
        { return null; }
        if (reading.Learning && descriptor.Condition is not (EcgMonitoringConditions.Asystole or EcgMonitoringConditions.SuspectedVentricularFibrillation))
        { return null; }
        return (reading.ActiveConditions & descriptor.Condition) != 0;
    }
}
