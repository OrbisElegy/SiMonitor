// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PacingAlarmSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PacingTemplatesReachMeasuredAlarms), PacingTemplatesReachMeasuredAlarms),
        new(nameof(PacingSourceSwitchUsesAcquisitionTime), PacingSourceSwitchUsesAcquisitionTime),
        new(nameof(PacingNotificationSuppressionIsBounded), PacingNotificationSuppressionIsBounded),
    ];

    private static LocalMonitorPreviewSession Session(PacingIllustration? mode) =>
        new(PhysiologyIllustrationConfiguration.Default with { Pacing = mode }, MonitorDisplayConfiguration.Default(), true);

    private static void PacingTemplatesReachMeasuredAlarms()
    {
        foreach (var mode in Enum.GetValues<PacingIllustration>())
        {
            var session = Session(mode);
            session.DiscardStartup();
            var alarms = new EcgAlarmNotices();
            List<DetectedEcgMonitoringEvent> events = [];
            var shown = new HashSet<string>();
            for (int step = 0; step < 180; step++)
            {
                session.Advance(200_000_000);
                events.AddRange(session.DetectedMonitoringEvents);
                foreach (var notice in alarms.Evaluate(true, session.Measurements!, session.DetectedMonitoringEvents, session.DetectedRhythmEvents))
                { shown.Add(notice.Id); }
            }
            var snapshot = session.Measurements!;
            bool stopped = mode is PacingIllustration.VentricularNoncapture or PacingIllustration.VentricularOutputFailure;
            bool intermittent = mode is PacingIllustration.IntermittentVentricularNoncapture or PacingIllustration.VentricularOversensing;
            var expected = mode is PacingIllustration.VentricularNoncapture or PacingIllustration.IntermittentVentricularNoncapture
                ? EcgMonitoringConditions.PacerNotCaptured : EcgMonitoringConditions.PacerNotPacing;
            var failures = events.Where(e => e.Condition is EcgMonitoringConditions.PacerNotCaptured or EcgMonitoringConditions.PacerNotPacing &&
                e.Transition == EcgMonitoringTransition.Started).ToArray();
            Check.That(snapshot.EcgMonitoring.PacingEvidenceOrigin == EcgPacingEvidenceOrigin.Simulation, mode + " sideband provenance");
            if (stopped || intermittent)
            {
                Check.That(failures.Length > 0 && failures.All(e => e.Condition == expected), mode + " distinguishes output from capture: " + string.Join(',', failures));
                Check.That(shown.Contains(expected == EcgMonitoringConditions.PacerNotCaptured ? "ecg-pacer-capture" : "ecg-pacer-pacing"), mode + " reaches current notices");
            }
            else
            {
                Check.That(failures.Length == 0 && snapshot.HeartRate.MilliBeatsPerMinute is >= 59000 and <= 61000, mode + " counts QRS, not pulses: " + snapshot.HeartRate);
                Check.That(snapshot.EcgMonitoring.PvcsLastMinute == 0, mode + " stable paced complexes are not PVCs: " + snapshot.EcgMonitoring.LastBeat);
            }
            Check.That(!shown.Contains("ecg-missed") && !shown.Contains("ecg-vf") && !shown.Contains("ecg-vt"), mode + " no false missed/VF/VT");
            if (stopped)
            {
                Check.That(shown.Contains("ecg-asystole") && snapshot.HeartRate.MilliBeatsPerMinute is null, mode + " pulse/P waves never mask ventricular standstill");
                Check.That(failures[0].ConfirmedAtNs < 4_000_000_000, mode + " startup failure does not wait for a learned QRS");
            }
            if (mode != PacingIllustration.AtrialAai)
            { Check.That(snapshot.EcgRhythm.SuspectedAtrialFibrillation is null, mode + " paced RR does not establish atrial rhythm"); }
            if (!stopped && !intermittent && mode is not (PacingIllustration.AtrialAai or PacingIllustration.VentricularUndersensing))
            {
                Check.That(snapshot.EcgMonitoring.LastBeat?.Label == EcgBeatLabel.Paced && snapshot.EcgMonitoring.Repolarization.QtMilliseconds is null,
                    mode + " captures have paced morphology and unavailable QT: " + snapshot.EcgMonitoring.LastBeat);
            }
            if (mode == PacingIllustration.VentricularUndersensing)
            { Check.That(snapshot.EcgMonitoring.LastBeat?.Label != EcgBeatLabel.Paced, "pulse after QRS onset is not proof of capture"); }
        }
    }

    private static void PacingSourceSwitchUsesAcquisitionTime()
    {
        var session = Session(null);
        session.DiscardStartup();
        for (int i = 0; i < 100; i++) { session.Advance(200_000_000); }
        long effective = session.ScheduleSource(Session(PacingIllustration.VentricularOutputFailure), 0);
        bool sawOld = false, sawNew = false;
        List<DetectedEcgMonitoringEvent> events = [];
        for (int i = 0; i < 50; i++)
        {
            session.Advance(200_000_000);
            var snapshot = session.Measurements!;
            if (session.Blocks[^1].StartSimTimeNs < effective)
            {
                sawOld = true;
                Check.That(!snapshot.EcgMonitoring.PacingEvidenceAvailable, "delayed old packet keeps old pulse context");
            }
            else
            {
                sawNew = true;
                Check.That(snapshot.EcgMonitoring.PacingEvidenceOrigin == EcgPacingEvidenceOrigin.Simulation, "new packet uses rebased sideband");
            }
            events.AddRange(session.DetectedMonitoringEvents);
        }
        Check.That(sawOld && sawNew && events.Any(e => e.Condition == EcgMonitoringConditions.PacerNotPacing && e.Transition == EcgMonitoringTransition.Started),
            "switch preserves acquisition delay and detects failure after complete evidence");
        session.ScheduleSource(Session(null), 0);
        for (int i = 0; i < 100; i++) { session.Advance(200_000_000); }
        Check.That(!session.Measurements!.EcgMonitoring.PacingEvidenceAvailable && session.Measurements.HeartRate.Status == WaveformMeasurementStatus.Valid,
            "leaving pacing clears sideband and resumes intrinsic measurement");
    }

    private static void PacingNotificationSuppressionIsBounded()
    {
        var session = Session(PacingIllustration.VentricularNoncapture);
        for (int i = 0; i < 20; i++) { session.Advance(200_000_000); }
        var template = session.Measurements!;
        var alarms = new EcgAlarmNotices();
        for (int tick = 0; tick <= 200; tick++)
        {
            long time = tick * 200_000_000L;
            bool active = tick % 10 < 5;
            var conditions = active ? EcgMonitoringConditions.PacerNotCaptured : EcgMonitoringConditions.None;
            if (tick == 50) { conditions = EcgMonitoringConditions.Asystole | EcgMonitoringConditions.PacerNotCaptured; }
            var snapshot = template with
            {
                SampleTimeNs = time,
                EcgMonitoring = template.EcgMonitoring with { Learning = false, Status = WaveformMeasurementStatus.Valid, ActiveConditions = conditions }
            };
            var notices = alarms.Evaluate(true, snapshot);
            if (tick == 50)
            { Check.That(notices.Any(n => n.Id == "ecg-asystole") && notices.All(n => n.Id != "ecg-pacer-capture"), "critical alarm replaces pacing warning during suppression"); }
        }
        var decisions = alarms.Lifecycle.NotificationRecords.Select(r => r.Decision).ToArray();
        var pacing = decisions.Where(d => d.Episode.ConditionId == "ecg-pacer-capture").ToArray();
        var audible = pacing.Where(d => d.RequestsNotification).ToArray();
        Check.That(audible.Length == 2 && audible[0].SampleTimeNs == 0 && audible[1].SampleTimeNs == 30_000_000_000,
            "recurrences cannot extend fixed 30 second suppression: " + string.Join(',', audible.Select(d => d.SampleTimeNs)));
        Check.That(pacing.Any(d => d.Kind == AlarmNotificationKind.RepeatSuppressed), "repeated episodes remain visible but are not reannounced");
        Check.That(decisions.Any(d => d.Episode.ConditionId == "ecg-asystole" && d.RequestsNotification && d.Policy.SoundDuration == AlarmSoundDuration.Continuous),
            "critical sound remains continuous and independent");
    }
}
