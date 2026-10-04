// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Specs;

internal static class AlarmAttentionSpecifications
{
    private static readonly LiveMeasurementSnapshot Empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
    private static readonly MeasurementLimits Limits = MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true };
    public static Specification[] All =>
    [
        new(nameof(AcknowledgementPreservesPhysiologyAndNotificationEvidence), AcknowledgementPreservesPhysiologyAndNotificationEvidence),
        new(nameof(LatchingRetainsOnlyUnacknowledgedRecovery), LatchingRetainsOnlyUnacknowledgedRecovery),
        new(nameof(EscalationAndRecurrenceRejectStaleAcknowledgements), EscalationAndRecurrenceRejectStaleAcknowledgements),
        new(nameof(InterruptionsClearAttentionWithoutClaimingRecovery), InterruptionsClearAttentionWithoutClaimingRecovery),
        new(nameof(AttentionPoliciesValidateAtomicallyAndDoNotResurrectEvents), AttentionPoliciesValidateAtomicallyAndDoNotResurrectEvents),
        new(nameof(AttentionDirectionsAndOwnersAreIndependent), AttentionDirectionsAndOwnersAreIndependent),
        new(nameof(AbsenceAttentionUsesAcquisitionEvidence), AbsenceAttentionUsesAcquisitionEvidence),
        new(nameof(AttentionHistoryIsBoundedImmutableAndDeterministic), AttentionHistoryIsBoundedImmutableAndDeterministic)
    ];

    private static MonitorNotice? Read(ConfirmedLimitNotice filter, long milliseconds, int value = 130000,
        WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid, MeasurementConfirmationTiming? timing = null) =>
        filter.Evaluate(Limits, Empty with
        {
            SampleTimeNs = milliseconds * 1_000_000,
            HeartRate = new(status, value, milliseconds * 1_000_000)
        }, timing);

    private static AlarmAttentionSnapshot High(ConfirmedLimitNotice filter) => filter.Lifecycle.Attention.Conditions.Single(c => c.ConditionId == "hr-high");
    private static bool Acknowledge(AlarmAttentionJournal journal, AlarmAttentionSnapshot state) => journal.Acknowledge(state.Episode!, state.Revision);

    private static void AcknowledgementPreservesPhysiologyAndNotificationEvidence()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var timing = new MeasurementConfirmationTiming(new(0, 200), new(0, 200), new(0, 200), new(0, 200));
        Read(filter, 0, timing: timing);
        var journal = filter.Lifecycle.Attention;
        var initial = High(filter);
        var conditions = filter.Lifecycle.Conditions;
        var transitions = filter.Lifecycle.Transitions;
        var notifications = filter.Lifecycle.NotificationRecords;
        Check.That(Acknowledge(journal, initial) && High(filter).State == AlarmAttentionState.ActiveAcknowledged,
            "an explicit acknowledgement changes only the attention state");
        Check.That(filter.Lifecycle.Conditions.SequenceEqual(conditions) && filter.Lifecycle.Transitions.SequenceEqual(transitions) &&
            filter.Lifecycle.NotificationRecords.SequenceEqual(notifications), "acknowledgement never alters physiological or notification evidence");
        int count = journal.Records.Count;
        Check.That(!Acknowledge(journal, initial) && !Acknowledge(journal, High(filter)) && journal.Records.Count == count,
            "repeated acknowledgement does not create a second action");
        Read(filter, 100, 100000, timing: timing);
        Check.That(High(filter).State == AlarmAttentionState.ActiveAcknowledged &&
            filter.Lifecycle.Conditions.Single(c => c.ConditionId == "hr-high").State == AlarmConditionState.PendingRecovery,
            "acknowledgement cannot shortcut recovery confirmation");
        Read(filter, 300, 100000, timing: timing);
        Check.That(High(filter).State == AlarmAttentionState.None && journal.Records[^1].Kind == AlarmAttentionKind.Recovered,
            "confirmed recovery clears an acknowledged condition");
    }

    private static void LatchingRetainsOnlyUnacknowledgedRecovery()
    {
        foreach (var mode in Enum.GetValues<AlarmLatchingMode>())
        {
            var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
            var journal = filter.Lifecycle.Attention;
            journal.Configure("hr-high", mode);
            Read(filter, 0);
            var active = High(filter);
            Read(filter, 100, 100000);
            Check.That(High(filter).State == (mode == AlarmLatchingMode.UntilAcknowledged
                ? AlarmAttentionState.RecoveredUnacknowledged : AlarmAttentionState.None), "latching is independent of physiological recovery");
            Check.That(filter.Lifecycle.Conditions.Single(c => c.ConditionId == "hr-high").Episode is null,
                "a retained indication is not an active physiological episode");
            if (mode == AlarmLatchingMode.UntilAcknowledged)
            {
                Read(filter, 200, 100000);
                Check.That(High(filter).Episode == active.Episode && High(filter).NeedsAcknowledgement,
                    "normal observations do not clear a retained indication");
                Check.That(!Acknowledge(journal, active) && Acknowledge(journal, High(filter)) && High(filter).State == AlarmAttentionState.None,
                    "acknowledging the recovered revision clears the retained indication");
            }
            Read(filter, 300);
            Check.That(Acknowledge(journal, High(filter)), "a new active occurrence can be acknowledged");
            Read(filter, 400, 100000);
            Check.That(High(filter).State == AlarmAttentionState.None, "acknowledgement before recovery also clears latching on recovery");
        }
    }

    private static void EscalationAndRecurrenceRejectStaleAcknowledgements()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var journal = filter.Lifecycle.Attention;
        journal.Configure("hr-high", AlarmLatchingMode.UntilAcknowledged);
        Read(filter, 0);
        var warning = High(filter);
        Acknowledge(journal, warning);
        Read(filter, 100, 180001);
        var critical = High(filter);
        Check.That(critical.Episode == warning.Episode && critical.NeedsAcknowledgement && !Acknowledge(journal, warning),
            "escalation rearms acknowledgement without replacing the episode and rejects an old click");
        Acknowledge(journal, critical);
        Read(filter, 200);
        Check.That(High(filter).State == AlarmAttentionState.ActiveAcknowledged, "a confirmed downgrade keeps acknowledgement");
        Read(filter, 300, 180001);
        Check.That(High(filter).NeedsAcknowledgement, "re-escalation needs a fresh acknowledgement");
        Read(filter, 400, 100000);
        var retained = High(filter);
        Read(filter, 500);
        Check.That(High(filter).Episode != retained.Episode && !Acknowledge(journal, retained) &&
            journal.Records[^1] is { Kind: AlarmAttentionKind.Replaced, Previous.State: AlarmAttentionState.RecoveredUnacknowledged },
            "recurrence replaces the retained indication explicitly and never inherits its acknowledgement");
    }

    private static void InterruptionsClearAttentionWithoutClaimingRecovery()
    {
        foreach (var reason in new[] { AlarmTransitionReason.Disabled, AlarmTransitionReason.ConfigurationChanged,
            AlarmTransitionReason.InvalidConfiguration, AlarmTransitionReason.SessionReset, AlarmTransitionReason.DataUnavailable,
            AlarmTransitionReason.ObservationGap, AlarmTransitionReason.ClockRewind })
        {
            var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
            var journal = filter.Lifecycle.Attention;
            journal.Configure("hr-high", AlarmLatchingMode.UntilAcknowledged);
            Read(filter, 1000);
            var before = High(filter);
            switch (reason)
            {
                case AlarmTransitionReason.DataUnavailable: Read(filter, 1100, status: WaveformMeasurementStatus.PoorSignal); break;
                case AlarmTransitionReason.ObservationGap: Read(filter, 1501); break;
                case AlarmTransitionReason.ClockRewind: Read(filter, 0); break;
                default: filter.Reset(reason); break;
            }
            Check.That(journal.Records.Any(r => r.Kind == AlarmAttentionKind.Interrupted && r.Reason == reason && r.Previous.Episode == before.Episode) &&
                !Acknowledge(journal, before), "interruptions clear the old indication with the real reason and reject its acknowledgement");
            Check.That(High(filter).LatchingMode == AlarmLatchingMode.UntilAcknowledged, "interruptions preserve the configured policy");
        }
        var recovered = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        recovered.Lifecycle.Attention.Configure("hr-high", AlarmLatchingMode.UntilAcknowledged);
        Read(recovered, 0); Read(recovered, 100, 100000);
        recovered.Reset();
        Check.That(High(recovered).State == AlarmAttentionState.None && recovered.Lifecycle.Attention.Records[^1].Reason == AlarmTransitionReason.SessionReset,
            "session reset also clears already recovered retained indications");
    }

    private static void AttentionPoliciesValidateAtomicallyAndDoNotResurrectEvents()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var journal = filter.Lifecycle.Attention;
        Read(filter, 0);
        var before = journal.Conditions;
        var records = journal.Records;
        foreach (Action invalid in new Action[] { () => journal.Configure("missing", AlarmLatchingMode.NonLatching),
            () => journal.Configure("hr-high", (AlarmLatchingMode)99), () => journal.Configure(null!, AlarmLatchingMode.NonLatching),
            () => journal.Acknowledge(null!, 0), () => journal.Acknowledge(new("missing", 1), 0) })
        {
            bool rejected = false;
            try { invalid(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected && journal.Conditions.SequenceEqual(before) && journal.Records.SequenceEqual(records), "invalid operations are atomically rejected");
        }
        var active = High(filter);
        journal.Configure("hr-high", AlarmLatchingMode.UntilAcknowledged);
        Check.That(!Acknowledge(journal, active) && High(filter).Episode == active.Episode, "policy revisions fence old clicks without recreating physiology");
        Read(filter, 100, 100000);
        journal.Configure("hr-high", AlarmLatchingMode.NonLatching);
        Check.That(High(filter).State == AlarmAttentionState.None && journal.Records[^1].Kind == AlarmAttentionKind.PolicyChanged,
            "turning off latching clears retained attention with a policy reason");
        journal.Configure("hr-high", AlarmLatchingMode.UntilAcknowledged);
        Check.That(High(filter).State == AlarmAttentionState.None, "turning on latching never resurrects an old recovered occurrence");
        int count = journal.Records.Count;
        journal.Configure("hr-high", AlarmLatchingMode.UntilAcknowledged);
        Check.That(journal.Records.Count == count, "applying the same policy is idempotent");
    }

    private static void AttentionDirectionsAndOwnersAreIndependent()
    {
        var first = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var second = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var timing = new MeasurementConfirmationTiming(new(0, 400), new(0, 400), new(0, 400), new(0, 400));
        Read(first, 0, 30000, timing: timing); Read(first, 100, timing: timing); Read(second, 100);
        var low = first.Lifecycle.Attention.Conditions.Single(c => c.ConditionId == "hr-low");
        Acknowledge(first.Lifecycle.Attention, High(first));
        Check.That(first.Lifecycle.Attention.Conditions.Single(c => c.ConditionId == "hr-low") == low && High(second).NeedsAcknowledgement,
            "acknowledgement is scoped to this owner and condition, including overlapping high and low episodes");
    }

    private static void AbsenceAttentionUsesAcquisitionEvidence()
    {
        var filter = new ConfirmedNoExpirationNotice();
        var journal = filter.Lifecycle.Attention;
        journal.Configure("co2-no-expiration", AlarmLatchingMode.UntilAcknowledged);
        filter.Evaluate(true, 5, 6000_000_000, new(WaveformMeasurementStatus.Valid, 0, null, 5500_000_000));
        var state = journal.Conditions[0];
        Check.That(state.NeedsAcknowledgement && Acknowledge(journal, state) && journal.Records[^1].SampleTimeNs == 5500_000_000,
            "an operator action is ordered at the latest acquisition evidence, never fabricated wall time");
        filter.Evaluate(true, 5, 6100_000_000, new(WaveformMeasurementStatus.Valid, 1, null, 5600_000_000));
        Check.That(journal.Records.Any(r => r.Kind == AlarmAttentionKind.Interrupted && r.Reason == AlarmTransitionReason.SignalSegmentChanged),
            "CO2 segment replacement interrupts attention independently of returned breath evidence");
    }

    private static void AttentionHistoryIsBoundedImmutableAndDeterministic()
    {
        var first = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var second = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var initial = first.Lifecycle.Attention.Conditions;
        for (int index = 0; index < 360; index++)
        {
            foreach (var filter in new[] { first, second })
            {
                Read(filter, index * 100, index % 2 == 0 ? 130000 : 100000);
                if (index % 2 == 0) { Acknowledge(filter.Lifecycle.Attention, High(filter)); }
                int count = filter.Lifecycle.Attention.Records.Count;
                ulong dropped = filter.Lifecycle.Attention.DroppedRecordCount;
                Read(filter, index * 100, index % 2 == 0 ? 130000 : 100000);
                Check.That(filter.Lifecycle.Attention.Records.Count == count && filter.Lifecycle.Attention.DroppedRecordCount == dropped,
                    "duplicate samples do not repeat attention transitions");
            }
        }
        var journal = first.Lifecycle.Attention;
        Check.That(journal.Records.Count == AlarmAttentionJournal.Capacity && journal.DroppedRecordCount > 0 &&
            journal.Records.SequenceEqual(second.Lifecycle.Attention.Records) && initial.All(s => s.State == AlarmAttentionState.None),
            "bounded detached attention snapshots are deterministic and report evictions");
        var records = journal.Records;
        Read(first, 0);
        Check.That(records.SequenceEqual(second.Lifecycle.Attention.Records) && journal.Records[^1].SampleTimeNs == 0 &&
            journal.Records[^1].Sequence > records[^1].Sequence, "clock rewind preserves audit order by sequence and never mutates old snapshots");
    }
}
