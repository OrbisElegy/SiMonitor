// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class EcgAlarmNoticeSpecifications
{
    private static readonly LiveMeasurementSnapshot Empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
    public static Specification[] All =>
    [
        new(nameof(EcgAlarmMappingsAndEvidenceGates), EcgAlarmMappingsAndEvidenceGates),
        new(nameof(EcgAlarmOccurrenceIsVisibleAndConsumedOnce), EcgAlarmOccurrenceIsVisibleAndConsumedOnce),
        new(nameof(EcgAlarmAttentionAndSoundFollowConfirmedEvidence), EcgAlarmAttentionAndSoundFollowConfirmedEvidence),
        new(nameof(EcgAlarmRecoveryAndInterruptionsAreDistinct), EcgAlarmRecoveryAndInterruptionsAreDistinct),
        new(nameof(EcgAlarmInvalidBatchDoesNotConsumeEvents), EcgAlarmInvalidBatchDoesNotConsumeEvents),
        new(nameof(EcgAlarmChainsSuppressRedundantIndications), EcgAlarmChainsSuppressRedundantIndications),
        new(nameof(EcgAlarmSupersessionClearsSoundAndRetention), EcgAlarmSupersessionClearsSoundAndRetention),
        new(nameof(EcgAlarmSuppressedPulsesDoNotReplay), EcgAlarmSuppressedPulsesDoNotReplay)
    ];

    private static LiveMeasurementSnapshot Snapshot(long milliseconds, EcgMonitoringConditions conditions = EcgMonitoringConditions.None) => Empty with
    {
        SampleTimeNs = milliseconds * 1_000_000,
        EcgMonitoring = new(WaveformMeasurementStatus.Valid, false, conditions, 0, null,
            new(WaveformMeasurementStatus.Valid, 0, WaveformMeasurementStatus.Valid, 400, 450, 20))
        { PacingEvidenceAvailable = true },
        EcgRhythm = new(WaveformMeasurementStatus.Valid, false, false, null)
    };
    private static AlarmAttentionSnapshot Attention(EcgAlarmNotices alarms, string id) => alarms.Lifecycle.Attention.Conditions.Single(c => c.ConditionId == id);

    private static void EcgAlarmMappingsAndEvidenceGates()
    {
        var flags = Enum.GetValues<EcgMonitoringConditions>().Aggregate((a, b) => a | b);
        var alarms = new EcgAlarmNotices();
        var snapshot = Snapshot(0, flags) with { EcgRhythm = new(WaveformMeasurementStatus.Valid, true, true, null) };
        var notices = alarms.Evaluate(true, snapshot);
        Check.That(notices.Count == 5 && notices.Count(n => n.Level == MonitorNoticeLevel.Critical) == 1 &&
            notices.All(n => n.Message is not null && n.Numeric == MonitorNumeric.HeartRate), "the top red alarm supersedes related conditions while ST/QT remains independently visible");
        foreach (var descriptor in EcgAlarmNotices.Descriptors.Where(d => d.Condition != EcgMonitoringConditions.None))
        {
            var isolated = new EcgAlarmNotices().Evaluate(true, Snapshot(0, descriptor.Condition));
            Check.That(isolated.Single().Id == descriptor.Id, "each supported condition remains available without a superseding alarm");
        }
        Check.That(EcgAlarmNotices.Descriptors.Count == 25 && !notices.Any(n => n.Id.EndsWith("-end", StringComparison.Ordinal)), "rhythm ending notices require explicit detector recovery");
        snapshot = snapshot with
        {
            SampleTimeNs = 100_000_000,
            EcgMonitoring = snapshot.EcgMonitoring with
            {
                PacingEvidenceAvailable = false,
                Repolarization = new(WaveformMeasurementStatus.Uncountable, 100, WaveformMeasurementStatus.NoData, 400, 900, 300)
            }
        };
        notices = alarms.Evaluate(true, snapshot);
        Check.That(!notices.Any(n => n.Id is "ecg-st-high" or "ecg-st-low" or "ecg-qtc" or "ecg-delta-qtc" or "ecg-pacer-capture" or "ecg-pacer-pacing"), "missing evidence hides even a stale flagged value");
        notices = alarms.Evaluate(true, snapshot with { SampleTimeNs = 200_000_000, EcgMonitoring = snapshot.EcgMonitoring with { Learning = true }, EcgRhythm = Empty.EcgRhythm });
        Check.That(notices.Count == 1 && notices.Single().Id == "ecg-asystole", "learning preserves only supported urgent evidence");
    }

    private static void EcgAlarmOccurrenceIsVisibleAndConsumedOnce()
    {
        var alarms = new EcgAlarmNotices();
        var pulse = new DetectedEcgMonitoringEvent(EcgMonitoringConditions.PairPvcs, EcgMonitoringTransition.Occurred, 0, 0);
        alarms.Evaluate(true, Snapshot(0));
        Check.That(alarms.Evaluate(true, Snapshot(0), [pulse]).Single().Id == "ecg-pvc-pair", "a completed brief event gets a presentation interval");
        var episode = Attention(alarms, "ecg-pvc-pair").Episode;
        for (int time = 0; time <= 10000; time += 200)
        {
            var notices = alarms.Evaluate(true, Snapshot(time), [pulse]);
            Check.That(notices.Count == (time < 10000 ? 1 : 0), "repeated frames never extend the ten-second interval");
        }
        Check.That(alarms.Lifecycle.NotificationRecords.Count == 1, "one sound notification per occurrence");
        alarms.Evaluate(true, Snapshot(10200), [pulse with { EvidenceFromNs = 10_200_000_000, ConfirmedAtNs = 10_200_000_000 }]);
        Check.That(Attention(alarms, "ecg-pvc-pair").Episode != episode && alarms.Lifecycle.NotificationRecords.Count == 2, "new evidence creates a fresh occurrence");
        alarms.Reset();
        Check.That(alarms.Evaluate(true, Snapshot(200),
        [
            new(EcgMonitoringConditions.VentricularTachycardia, EcgMonitoringTransition.Started, 0, 100_000_000),
            new(EcgMonitoringConditions.VentricularTachycardia, EcgMonitoringTransition.Ended, 0, 180_000_000)
        ]).Single().Id == "ecg-vt", "a start and end inside one UI frame remain visible");
        alarms.Evaluate(false, Snapshot(400));
        Check.That(alarms.Evaluate(true, Snapshot(400), [pulse]).Count == 0, "disabled group cannot replay an old event");
    }

    private static void EcgAlarmAttentionAndSoundFollowConfirmedEvidence()
    {
        var alarms = new EcgAlarmNotices();
        alarms.Lifecycle.Attention.Configure("ecg-asystole", AlarmLatchingMode.UntilAcknowledged);
        var router = new AlarmNotificationSoundRouter();
        var notices = alarms.Evaluate(true, Snapshot(0, EcgMonitoringConditions.Asystole));
        var request = router.Update([alarms.Lifecycle], 50, new MonitorSoundTiming(), true, notices);
        Check.That(request?.Level == MonitorNoticeLevel.Critical && alarms.Lifecycle.NotificationPolicyFor("ecg-asystole").SoundDuration == AlarmSoundDuration.Continuous,
            "critical detector evidence immediately reaches the sound router without a second trigger delay");
        var state = Attention(alarms, "ecg-asystole");
        Check.That(alarms.Lifecycle.Attention.Acknowledge(state.Episode!, state.Revision), "alarm can be acknowledged");
        notices = alarms.Evaluate(true, Snapshot(200, EcgMonitoringConditions.Asystole));
        Check.That(notices.Count == 1 && router.Update([alarms.Lifecycle], 50, new MonitorSoundTiming(), true, notices) is null,
            "acknowledgement silences sound while the condition remains active");
        alarms.Evaluate(true, Snapshot(400));
        Check.That(Attention(alarms, "ecg-asystole").State == AlarmAttentionState.None, "acknowledged recovery clears attention");
        alarms.Evaluate(true, Snapshot(600, EcgMonitoringConditions.Asystole));
        alarms.Evaluate(true, Snapshot(800));
        Check.That(Attention(alarms, "ecg-asystole").State == AlarmAttentionState.RecoveredUnacknowledged, "unacknowledged critical recovery retains a silent indication");
        alarms.Reset();
        Check.That(Attention(alarms, "ecg-asystole").State == AlarmAttentionState.None, "session reset clears retained indications");
    }

    private static void EcgAlarmRecoveryAndInterruptionsAreDistinct()
    {
        var alarms = new EcgAlarmNotices();
        alarms.Evaluate(true, Snapshot(0) with { EcgRhythm = new(WaveformMeasurementStatus.Valid, true, true, null) });
        var ended = new DetectedEcgRhythmEvent(EcgRhythmEventKind.SuspectedAtrialFibrillation, EcgRhythmTransition.Ended, 0, 200_000_000);
        Check.That(alarms.Evaluate(true, Snapshot(200), rhythmEvents: [ended]).Single().Id == "ecg-af-end", "confirmed rhythm recovery has its own brief notice");
        alarms.Evaluate(true, Snapshot(400), rhythmEvents: [ended with { Transition = EcgRhythmTransition.Interrupted, ConfirmedAtNs = 400_000_000, Interruption = EcgRhythmInterruption.StreamDiscontinuity }]);
        Check.That(Attention(alarms, "ecg-af-end").State == AlarmAttentionState.None, "signal interruption clears recovery display");
        alarms.Evaluate(true, Snapshot(600, EcgMonitoringConditions.Asystole));
        alarms.Evaluate(true, Snapshot(800) with { EcgMonitoring = EcgMonitoringReading.NoData });
        Check.That(alarms.Lifecycle.Transitions.Any(t => t.ConditionId == "ecg-asystole" && t.Reason == AlarmTransitionReason.DataUnavailable && t.Kind == AlarmTransitionKind.Ended) &&
            Attention(alarms, "ecg-asystole").State == AlarmAttentionState.None, "unknown evidence interrupts instead of retaining a false recovery");
        alarms.Evaluate(true, Snapshot(1000, EcgMonitoringConditions.Asystole));
        alarms.Evaluate(true, Snapshot(2000));
        Check.That(alarms.Lifecycle.Transitions.Any(t => t.ConditionId == "ecg-asystole" && t.Reason == AlarmTransitionReason.ObservationGap), "unobserved intervals interrupt stale alarms");
        alarms.Evaluate(true, Snapshot(0));
        Check.That(Attention(alarms, "ecg-asystole").State == AlarmAttentionState.None, "clock rewind clears old attention");
    }

    private static void EcgAlarmInvalidBatchDoesNotConsumeEvents()
    {
        var alarms = new EcgAlarmNotices();
        var valid = new DetectedEcgMonitoringEvent(EcgMonitoringConditions.RunPvcs, EcgMonitoringTransition.Occurred, 0, 0);
        bool rejected = false;
        try { alarms.Evaluate(true, Snapshot(0), [valid, valid with { ConfirmedAtNs = 1 }]); }
        catch (ArgumentException) { rejected = true; }
        Check.That(rejected && alarms.Lifecycle.Transitions.Count == 0 && alarms.Evaluate(true, Snapshot(0), [valid]).Count == 1,
            "invalid later evidence does not partially mutate or consume the valid prefix");
    }
    private static void EcgAlarmChainsSuppressRedundantIndications()
    {
        var alarms = new EcgAlarmNotices();
        var ventricular = EcgMonitoringConditions.VentricularTachycardia | EcgMonitoringConditions.RunPvcs |
            EcgMonitoringConditions.PairPvcs | EcgMonitoringConditions.PvcsPerMinuteHigh;
        var snapshot = Snapshot(0, ventricular | EcgMonitoringConditions.StHigh | EcgMonitoringConditions.QtcHigh) with
        { EcgRhythm = new(WaveformMeasurementStatus.Valid, true, true, null) };
        var notices = alarms.Evaluate(true, snapshot);
        Check.That(notices.Count == 3 && notices.Any(n => n.Id == "ecg-vt") && notices.Any(n => n.Id == "ecg-st-high") &&
            notices.Any(n => n.Id == "ecg-qtc"), "VT blocks lower arrhythmia chains but never independent ST/QT alarms");
        Check.That(snapshot.EcgMonitoring.ActiveConditions.HasFlag(EcgMonitoringConditions.PvcsPerMinuteHigh) &&
            snapshot.EcgRhythm.IrregularRhythm == true, "suppression never changes detector evidence");
        snapshot = Snapshot(200, EcgMonitoringConditions.VentricularBigeminy | EcgMonitoringConditions.VentricularTrigeminy |
            EcgMonitoringConditions.PvcsPerMinuteHigh | EcgMonitoringConditions.MultiformPvcs | EcgMonitoringConditions.Pause |
            EcgMonitoringConditions.MissedBeat | EcgMonitoringConditions.PacerNotCaptured | EcgMonitoringConditions.PacerNotPacing) with
        { EcgRhythm = new(WaveformMeasurementStatus.Valid, true, true, null) };
        notices = alarms.Evaluate(true, snapshot);
        Check.That(notices.Count == 3 && notices.Any(n => n.Id == "ecg-bigeminy") && notices.Any(n => n.Id == "ecg-pause") &&
            notices.Any(n => n.Id == "ecg-af"), "each independent chain keeps its highest eligible indication");
        notices = alarms.Evaluate(true, Snapshot(400, EcgMonitoringConditions.NonSustainedVentricularTachycardia |
            EcgMonitoringConditions.VentricularRhythm | EcgMonitoringConditions.RunPvcs | EcgMonitoringConditions.PairPvcs));
        Check.That(notices.Count == 2 && notices.Any(n => n.Id == "ecg-nsvt") && notices.Any(n => n.Id == "ecg-ventricular"),
            "equal-priority siblings do not suppress one another");
        notices = alarms.Evaluate(true, Snapshot(600, EcgMonitoringConditions.SuspectedVentricularFibrillation | ventricular));
        Check.That(notices.Single().Id == "ecg-vf", "VF supersedes VT and its descendants");
        notices = alarms.Evaluate(true, Snapshot(800, EcgMonitoringConditions.Asystole | EcgMonitoringConditions.SuspectedVentricularFibrillation));
        Check.That(notices.Single().Id == "ecg-asystole", "asystole heads the red chain");
    }

    private static void EcgAlarmSupersessionClearsSoundAndRetention()
    {
        var alarms = new EcgAlarmNotices();
        var router = new AlarmNotificationSoundRouter();
        alarms.Lifecycle.Attention.Configure("ecg-pvc-rate", AlarmLatchingMode.UntilAcknowledged);
        alarms.Lifecycle.ConfigureNotifications("ecg-pvc-rate", new(2000, 0));
        var notices = alarms.Evaluate(true, Snapshot(0, EcgMonitoringConditions.PvcsPerMinuteHigh));
        Check.That(router.Update([alarms.Lifecycle], 50, new(), true, notices)?.Level == MonitorNoticeLevel.Warning,
            "lower alarm can sound before supersession");
        var combined = Snapshot(200, EcgMonitoringConditions.PvcsPerMinuteHigh | EcgMonitoringConditions.VentricularTachycardia);
        notices = alarms.Evaluate(true, combined);
        var state = Attention(alarms, "ecg-vt");
        Check.That(alarms.Lifecycle.Conditions.Single(c => c.ConditionId == "ecg-pvc-rate") is
        { State: AlarmConditionState.Suppressed, Reason: AlarmTransitionReason.Superseded } &&
            Attention(alarms, "ecg-pvc-rate").State == AlarmAttentionState.None &&
            !alarms.Lifecycle.Transitions.Any(t => t.ConditionId == "ecg-pvc-rate" && t.Reason == AlarmTransitionReason.Recovered),
            "supersession clears lower attention without claiming recovery or retaining a duplicate banner");
        Check.That(router.Update([alarms.Lifecycle], 50, new(), true, notices)?.Level == MonitorNoticeLevel.Critical,
            "sound follows the superseding alarm");
        alarms.Lifecycle.Attention.Acknowledge(state.Episode!, state.Revision);
        notices = alarms.Evaluate(true, combined with { SampleTimeNs = 400_000_000 });
        Check.That(notices.Single().Id == "ecg-vt" && router.Update([alarms.Lifecycle], 50, new(), true, notices) is null,
            "acknowledging the higher alarm cannot uncover or sound its lower alarms");
        notices = alarms.Evaluate(true, Snapshot(600, EcgMonitoringConditions.PvcsPerMinuteHigh));
        Check.That(notices.Single().Id == "ecg-pvc-rate" && router.Update([alarms.Lifecycle], 50, new(), true, notices) is null,
            "persistent lower evidence returns when unblocked, preserving its own repeat-suppression timer");
        for (int time = 800; time <= 2000; time += 200)
        {
            notices = alarms.Evaluate(true, Snapshot(time, EcgMonitoringConditions.PvcsPerMinuteHigh));
            var sound = router.Update([alarms.Lifecycle], 50, new(), true, notices);
            Check.That((sound is not null) == (time == 2000), "supersession does not reset or bypass repeat suppression");
        }
        alarms.Evaluate(true, Snapshot(2200));
        Check.That(Attention(alarms, "ecg-pvc-rate").State == AlarmAttentionState.RecoveredUnacknowledged, "explicit retention still works on real recovery");
        alarms.Evaluate(true, Snapshot(2400, EcgMonitoringConditions.VentricularTachycardia));
        Check.That(Attention(alarms, "ecg-pvc-rate").State == AlarmAttentionState.None, "superseding alarm also removes a previously retained lower indication");
    }

    private static void EcgAlarmSuppressedPulsesDoNotReplay()
    {
        var alarms = new EcgAlarmNotices();
        var pulse = new DetectedEcgMonitoringEvent(EcgMonitoringConditions.PairPvcs, EcgMonitoringTransition.Occurred, 0, 0);
        var notices = alarms.Evaluate(true, Snapshot(0, EcgMonitoringConditions.VentricularTachycardia), [pulse]);
        Check.That(notices.Single().Id == "ecg-vt" && alarms.Lifecycle.NotificationRecords.All(r => r.Decision.Episode.ConditionId != "ecg-pvc-pair"),
            "a simultaneous lower pulse never creates a sound intent");
        Check.That(alarms.Evaluate(true, Snapshot(200), [pulse]).Count == 0, "suppressed short events are consumed without later replay");
        alarms.Reset();
        var ended = new DetectedEcgRhythmEvent(EcgRhythmEventKind.SuspectedAtrialFibrillation, EcgRhythmTransition.Ended, 0, 0);
        notices = alarms.Evaluate(true, Snapshot(0), rhythmEvents:
        [ended, ended with { Kind = EcgRhythmEventKind.IrregularRhythm }]);
        Check.That(notices.Single().Id == "ecg-af-end", "AF ending supersedes the related irregular-rhythm ending pulse");
        for (int time = 200; time <= 10000; time += 200)
        {
            notices = alarms.Evaluate(true, Snapshot(time));
            Check.That(notices.All(n => n.Id != "ecg-irregular-end"), "hidden ending pulses cannot reappear after the higher display interval expires");
        }
        alarms.Reset();
        var af = Snapshot(0) with { EcgRhythm = new(WaveformMeasurementStatus.Valid, true, true, null) };
        Check.That(alarms.Evaluate(true, af).Single().Id == "ecg-af", "AF supersedes an equally yellow irregular-rhythm warning");
        Check.That(alarms.Evaluate(true, af with { SampleTimeNs = 200_000_000, EcgRhythm = af.EcgRhythm with { SuspectedAtrialFibrillation = null } }).Single().Id == "ecg-irregular",
            "unknown AF evidence cannot suppress valid irregular-rhythm evidence");
    }

}
