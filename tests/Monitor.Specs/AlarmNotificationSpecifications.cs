// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Specs;

internal static class AlarmNotificationSpecifications
{
    private static readonly LiveMeasurementSnapshot Empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
    private static readonly MeasurementLimits Limits = MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true };
    public static Specification[] All =>
    [
        new(nameof(NotificationPolicyDoesNotDelayFirstConfirmation), NotificationPolicyDoesNotDelayFirstConfirmation),
        new(nameof(RepeatedEpisodesDeferOnlyNotification), RepeatedEpisodesDeferOnlyNotification),
        new(nameof(EscalationAndIndependentConditionsBypassSuppression), EscalationAndIndependentConditionsBypassSuppression),
        new(nameof(RemindersUseAcquisitionWithoutReplay), RemindersUseAcquisitionWithoutReplay),
        new(nameof(NotificationConfigurationAndInterruptionsResetOnlyIntent), NotificationConfigurationAndInterruptionsResetOnlyIntent),
        new(nameof(NotificationJournalIsBoundedAndDeterministic), NotificationJournalIsBoundedAndDeterministic)
    ];

    private static LiveMeasurementSnapshot Snapshot(long milliseconds, int value = 180001,
        WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid) =>
        Empty with { SampleTimeNs = milliseconds * 1_000_000, HeartRate = new(status, value, milliseconds * 1_000_000) };
    private static AlarmNotificationDecision[] Decisions(AlarmLifecycleJournal journal) => journal.NotificationRecords.Select(r => r.Decision).ToArray();

    private static void NotificationPolicyDoesNotDelayFirstConfirmation()
    {
        foreach (int delay in new[] { 0, 1, 400 })
        {
            var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
            filter.Lifecycle.ConfigureNotifications("hr-high", new(3600000, 1));
            var timing = new MeasurementConfirmationTiming(new(0, 0), new(0, 0), new(delay, 0), new(delay, 0));
            if (delay > 0)
            {
                filter.Evaluate(Limits, Snapshot(0), timing);
                filter.Evaluate(Limits, Snapshot(delay - 1), timing);
                Check.That(filter.Lifecycle.NotificationRecords.Count == 0, "pending first confirmation never emits an intent");
            }
            var notice = filter.Evaluate(Limits, Snapshot(delay), timing);
            var first = Decisions(filter.Lifecycle).Single();
            Check.That(notice?.Level == MonitorNoticeLevel.Critical && first is { Kind: AlarmNotificationKind.FirstOccurrence, RequestsNotification: true } &&
                first.SampleTimeNs == delay * 1_000_000L, "suppression never adds delay to a first confirmed alarm");
            filter.Evaluate(Limits, Snapshot(delay), timing);
            Check.That(filter.Lifecycle.NotificationRecords.Count == 1, "same sample cannot duplicate first notification");
            filter.Evaluate(Limits, Snapshot(delay + 1), timing);
            Check.That(Decisions(filter.Lifecycle)[^1].Kind == AlarmNotificationKind.Reminder, "reminder duration is independent of confirmation duration");
        }
    }

    private static void RepeatedEpisodesDeferOnlyNotification()
    {
        foreach (bool recoverBeforeDeadline in new[] { false, true })
        {
            var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
            filter.Lifecycle.ConfigureNotifications("hr-high", new(1000, 100));
            filter.Evaluate(Limits, Snapshot(0));
            var firstEpisode = Decisions(filter.Lifecycle)[0].Episode;
            filter.Evaluate(Limits, Snapshot(100, 100000));
            var notice = filter.Evaluate(Limits, Snapshot(200));
            var suppression = Decisions(filter.Lifecycle)[^1];
            Check.That(notice?.Level == MonitorNoticeLevel.Critical && suppression.Kind == AlarmNotificationKind.RepeatSuppressed &&
                !suppression.RequestsNotification && suppression.SuppressionRemainingNs == 800_000_000 && suppression.Episode != firstEpisode &&
                suppression.Policy == new AlarmNotificationPolicy(1000, 100),
                "real recurrence is recorded and remains visible while only its notification is deferred");
            for (int time = 300; time < 1000; time += 100)
            { filter.Evaluate(Limits, Snapshot(time, recoverBeforeDeadline && time >= 900 ? 100000 : 180001)); }
            Check.That(filter.Lifecycle.NotificationRecords.Count == 2, "suppressed recurrence cannot leak through the shorter reminder interval");
            filter.Evaluate(Limits, Snapshot(999, recoverBeforeDeadline ? 100000 : 180001));
            Check.That(filter.Lifecycle.NotificationRecords.Count == 2, "repeat notification does not fire one millisecond early");
            filter.Evaluate(Limits, Snapshot(1000, recoverBeforeDeadline ? 100000 : 180001));
            Check.That(filter.Lifecycle.NotificationRecords.Count == (recoverBeforeDeadline ? 2 : 3), "deferred intent is emitted at expiry only if the episode still exists");
            if (!recoverBeforeDeadline)
            {
                var deferred = Decisions(filter.Lifecycle)[^1];
                Check.That(deferred.Kind == AlarmNotificationKind.DeferredRepeat && deferred.Episode == suppression.Episode,
                    "expiry never creates a new physiological episode");
            }
        }
    }

    private static void EscalationAndIndependentConditionsBypassSuppression()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        filter.Lifecycle.ConfigureNotifications("hr-high", new(1000, 0));
        filter.Evaluate(Limits, Snapshot(0, 130000));
        filter.Evaluate(Limits, Snapshot(100, 100000));
        filter.Evaluate(Limits, Snapshot(200, 130000));
        var repeated = Decisions(filter.Lifecycle)[^1];
        filter.Evaluate(Limits, Snapshot(300));
        var escalated = Decisions(filter.Lifecycle)[^1];
        Check.That(repeated.Kind == AlarmNotificationKind.RepeatSuppressed && escalated.Kind == AlarmNotificationKind.SeverityEscalation &&
            escalated.Episode == repeated.Episode && escalated.Level == MonitorNoticeLevel.Critical, "confirmed deterioration bypasses same-episode suppression");
        filter.Evaluate(Limits, Snapshot(400, 130000));
        Check.That(filter.Lifecycle.NotificationRecords.Count == 3, "downgrade alone is not a new notification");
        filter.Evaluate(Limits, Snapshot(500));
        Check.That(Decisions(filter.Lifecycle)[^1].Kind == AlarmNotificationKind.SeverityEscalation, "re-escalation is not hidden by earlier Critical notification");
        filter.Evaluate(Limits, Snapshot(600, 39999));
        Check.That(Decisions(filter.Lifecycle)[^1] is { Kind: AlarmNotificationKind.FirstOccurrence, Episode.ConditionId: "hr-low" },
            "opposite direction owns independent recency");
        var independent = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        independent.Evaluate(Limits, Snapshot(600));
        Check.That(Decisions(independent.Lifecycle).Single().Kind == AlarmNotificationKind.FirstOccurrence, "different owners cannot suppress each other");
        var immediate = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        immediate.Lifecycle.ConfigureNotifications("hr-high", new(1000, 0));
        immediate.Evaluate(Limits, Snapshot(0, 130000)); immediate.Evaluate(Limits, Snapshot(100, 100000)); immediate.Evaluate(Limits, Snapshot(200));
        Check.That(Decisions(immediate.Lifecycle)[^1] is { Kind: AlarmNotificationKind.Recurrence, RequestsNotification: true, Level: MonitorNoticeLevel.Critical },
            "more severe new occurrence also bypasses a previous lower-level notification");
    }

    private static void RemindersUseAcquisitionWithoutReplay()
    {
        var filter = new ConfirmedNoExpirationNotice();
        filter.Lifecycle.ConfigureNotifications("co2-no-expiration", new(0, 100));
        void Read(long sampleMs, long? observationMs = null) => filter.Evaluate(true, 5, (observationMs ?? sampleMs) * 1_000_000,
            new(WaveformMeasurementStatus.Valid, 0, null, sampleMs * 1_000_000));
        Read(5000);
        Read(5000, 5400);
        Check.That(filter.Lifecycle.NotificationRecords.Count == 1, "presentation time cannot advance CO2 reminders");
        Read(5400, 5400);
        Check.That(filter.Lifecycle.NotificationRecords.Count == 2 && Decisions(filter.Lifecycle)[^1].SampleTimeNs == 5400_000_000,
            "late observation emits one current reminder without replaying missed periods");
        Read(5499); Check.That(filter.Lifecycle.NotificationRecords.Count == 2, "next interval starts at actual intent time");
        Read(5500);
        Check.That(filter.Lifecycle.NotificationRecords.Count == 3, "exact reminder boundary");
        var noReminders = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        for (int time = 0; time <= 5000; time += 100) { noReminders.Evaluate(Limits, Snapshot(time)); }
        Check.That(noReminders.Lifecycle.NotificationRecords.Count == 1, "zero reminder disables extra periodic intents without disabling the alarm");
    }

    private static void NotificationConfigurationAndInterruptionsResetOnlyIntent()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        filter.Evaluate(Limits, Snapshot(0));
        var before = filter.Lifecycle.Conditions;
        var transitions = filter.Lifecycle.Transitions;
        filter.Lifecycle.ConfigureNotifications("hr-high", new(1000, 500));
        Check.That(filter.Lifecycle.Conditions.SequenceEqual(before) && filter.Lifecycle.Transitions.SequenceEqual(transitions),
            "notification configuration never mutates physiological state or its record");
        filter.Evaluate(Limits, Snapshot(0));
        Check.That(Decisions(filter.Lifecycle)[^1].Kind == AlarmNotificationKind.PolicyChanged &&
            Decisions(filter.Lifecycle)[^1].Episode == Decisions(filter.Lifecycle)[0].Episode, "active policy changes get fresh intent without a new episode");
        var records = filter.Lifecycle.NotificationRecords;
        foreach (var invalid in new[] { new AlarmNotificationPolicy(-1, 0), new(0, -1), new(3600001, 0), new(0, 3600001), null! })
        {
            bool rejected = false;
            try { filter.Lifecycle.ConfigureNotifications("hr-high", invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected && filter.Lifecycle.NotificationRecords.SequenceEqual(records) &&
                filter.Lifecycle.NotificationPolicyFor("hr-high") == new AlarmNotificationPolicy(1000, 500), "invalid configuration is rejected atomically");
        }
        bool unknownRejected = false;
        try { filter.Lifecycle.ConfigureNotifications("missing", new()); } catch (ArgumentException) { unknownRejected = true; }
        Check.That(unknownRejected, "unknown condition cannot allocate an unregistered policy");
        filter.Evaluate(Limits, Snapshot(100, status: WaveformMeasurementStatus.PoorSignal));
        filter.Evaluate(Limits, Snapshot(200));
        Check.That(Decisions(filter.Lifecycle)[^1].Kind == AlarmNotificationKind.FirstOccurrence, "data-loss interruption clears repeat suppression");
        filter.Reset(); filter.Evaluate(Limits, Snapshot(300));
        Check.That(Decisions(filter.Lifecycle)[^1].Kind == AlarmNotificationKind.FirstOccurrence, "reset clears intent history without clearing configured durations");
        filter.Evaluate(Limits, Snapshot(0));
        Check.That(Decisions(filter.Lifecycle)[^1].Kind == AlarmNotificationKind.FirstOccurrence, "rewind cannot retain a future suppression deadline");
    }

    private static void NotificationJournalIsBoundedAndDeterministic()
    {
        var first = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var second = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        first.Lifecycle.ConfigureNotifications("hr-high", new(0, 1));
        second.Lifecycle.ConfigureNotifications("hr-high", new(0, 1));
        var empty = first.Lifecycle.NotificationRecords;
        for (int time = 0; time <= 400; time++)
        {
            first.Evaluate(Limits, Snapshot(time)); second.Evaluate(Limits, Snapshot(time));
            first.Evaluate(Limits, Snapshot(time));
        }
        Check.That(empty.Count == 0 && first.Lifecycle.NotificationRecords.Count == AlarmLifecycleJournal.Capacity &&
            first.Lifecycle.DroppedNotificationCount == 145 && first.Lifecycle.NotificationRecords.SequenceEqual(second.Lifecycle.NotificationRecords),
            "notification retention is bounded, explicit, immutable and deterministic");
        Check.That(first.Lifecycle.Transitions.Count == 2, "reminder records never become extra physiological transitions");
        var snapshot = first.Lifecycle.NotificationRecords;
        first.Reset(); first.Evaluate(Limits, Snapshot(0));
        Check.That(snapshot.SequenceEqual(second.Lifecycle.NotificationRecords), "published records cannot mutate after owner resets");
    }
}
