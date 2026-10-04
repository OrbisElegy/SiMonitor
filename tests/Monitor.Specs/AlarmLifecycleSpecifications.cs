// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Specs;

internal static class AlarmLifecycleSpecifications
{
    private static readonly LiveMeasurementSnapshot Empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
    public static Specification[] All =>
    [
        new(nameof(AlarmEpisodesRetainIdentityThroughSeverityAndRecovery), AlarmEpisodesRetainIdentityThroughSeverityAndRecovery),
        new(nameof(AlarmInterruptionsNeverMasqueradeAsRecovery), AlarmInterruptionsNeverMasqueradeAsRecovery),
        new(nameof(AlarmDirectionsRetainIndependentEpisodes), AlarmDirectionsRetainIndependentEpisodes),
        new(nameof(AbsenceLifecycleSeparatesInvalidEvidenceAndRecovery), AbsenceLifecycleSeparatesInvalidEvidenceAndRecovery),
        new(nameof(AlarmJournalIsBoundedImmutableAndDeterministic), AlarmJournalIsBoundedImmutableAndDeterministic)
    ];

    private static LiveMeasurementSnapshot Snapshot(long milliseconds, int value, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid) =>
        Empty with { SampleTimeNs = milliseconds * 1_000_000, HeartRate = new(status, value, milliseconds * 1_000_000) };
    private static AlarmConditionSnapshot High(ConfirmedLimitNotice filter) => filter.Lifecycle.Conditions.Single(s => s.ConditionId == "hr-high");

    private static void AlarmEpisodesRetainIdentityThroughSeverityAndRecovery()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var limits = MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true };
        var timing = new MeasurementConfirmationTiming(new(0, 0), new(0, 0), new(200, 200), new(400, 300));
        MonitorNotice? At(long milliseconds, int value = 180001) => filter.Evaluate(limits, Snapshot(milliseconds, value), timing);
        At(0);
        Check.That(High(filter) is { State: AlarmConditionState.PendingTrigger, Episode: null }, "pending trigger is not a confirmed episode");
        At(200);
        var first = High(filter).Episode;
        Check.That(first is not null && High(filter).Level == MonitorNoticeLevel.Warning, "Warning begins an episode");
        At(400);
        Check.That(High(filter).Episode == first && High(filter).Level == MonitorNoticeLevel.Critical, "Critical upgrades the same occurrence");
        At(500, 180000);
        Check.That(High(filter).State == AlarmConditionState.PendingRecovery && High(filter).Episode == first, "partial recovery retains active identity");
        At(600);
        Check.That(High(filter).State == AlarmConditionState.Active && High(filter).Episode == first, "interrupted recovery does not restart occurrence");
        At(700, 180000); At(1000, 180000);
        Check.That(High(filter).Level == MonitorNoticeLevel.Warning && High(filter).Episode == first, "confirmed downgrade retains identity");
        At(1100, 120000); At(1300, 120000);
        var ended = filter.Lifecycle.Transitions.Last(t => t.ConditionId == "hr-high");
        Check.That(High(filter) is { State: AlarmConditionState.Normal, Episode: null } &&
            ended is { Kind: AlarmTransitionKind.Ended, Reason: AlarmTransitionReason.Recovered } && ended.Episode == first,
            "only confirmed recovery ends with Recovered");
        At(1400); At(1600);
        Check.That(High(filter).Episode != first && High(filter).Episode!.Occurrence > first!.Occurrence, "recurrence allocates a new identity");
        Check.That(filter.Lifecycle.Transitions.Count(t => t.Kind == AlarmTransitionKind.SeverityChanged) == 2, "upgrades and downgrades are explicit transitions");
    }

    private static void AlarmInterruptionsNeverMasqueradeAsRecovery()
    {
        foreach (var reason in new[] { AlarmTransitionReason.Disabled, AlarmTransitionReason.ConfigurationChanged,
            AlarmTransitionReason.InvalidConfiguration, AlarmTransitionReason.DataUnavailable, AlarmTransitionReason.ObservationGap,
            AlarmTransitionReason.ClockRewind, AlarmTransitionReason.SessionReset })
        {
            var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
            var limits = MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true };
            filter.Evaluate(limits, Snapshot(1000, 180001));
            var first = High(filter).Episode;
            switch (reason)
            {
                case AlarmTransitionReason.Disabled:
                    filter.Evaluate(limits with { Enabled = false }, Snapshot(1100, 180001)); break;
                case AlarmTransitionReason.ConfigurationChanged:
                    filter.Evaluate(limits with { CriticalHigh = 190000 }, Snapshot(1100, 180001)); break;
                case AlarmTransitionReason.InvalidConfiguration:
                    filter.Evaluate(limits with { CriticalHigh = 100000 }, Snapshot(1100, 180001)); break;
                case AlarmTransitionReason.DataUnavailable:
                    filter.Evaluate(limits, Snapshot(1100, 180001, WaveformMeasurementStatus.PoorSignal)); break;
                case AlarmTransitionReason.ObservationGap:
                    filter.Evaluate(limits, Snapshot(1501, 180001)); break;
                case AlarmTransitionReason.ClockRewind:
                    filter.Evaluate(limits, Snapshot(0, 180001)); break;
                default:
                    filter.Reset(); break;
            }
            var ended = filter.Lifecycle.Transitions.Single(t => t.Kind == AlarmTransitionKind.Ended);
            Check.That(ended.Episode == first && ended.Reason == reason && ended.Reason != AlarmTransitionReason.Recovered,
                "interruption preserves the actual end reason even when immediate reconfirmation follows");
        }
        var atomic = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var enabled = MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true };
        atomic.Evaluate(enabled, Snapshot(0, 180001));
        var before = atomic.Lifecycle.Transitions;
        bool rejected = false;
        try { atomic.Evaluate(enabled, Snapshot(100, 180001), new(new(-1, 0), new(0, 0), new(0, 0), new(0, 0))); }
        catch (ArgumentException) { rejected = true; }
        Check.That(rejected && atomic.Lifecycle.Transitions.SequenceEqual(before), "invalid confirmation is rejected before changing lifecycle");
        foreach (var status in Enum.GetValues<WaveformMeasurementStatus>().Where(s => s != WaveformMeasurementStatus.Valid))
        {
            atomic.Evaluate(enabled, Snapshot(200, 180001, status));
            Check.That(High(atomic).State == AlarmConditionState.Indeterminate, "unusable measurement is never Normal");
        }
    }

    private static void AlarmDirectionsRetainIndependentEpisodes()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var limits = MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true };
        var timing = new MeasurementConfirmationTiming(new(0, 400), new(0, 400), new(0, 400), new(0, 400));
        filter.Evaluate(limits, Snapshot(0, 39999), timing);
        var low = filter.Lifecycle.Conditions.Single(s => s.ConditionId == "hr-low").Episode;
        var selected = filter.Evaluate(limits, Snapshot(100, 180001), timing);
        Check.That(selected?.Id == "hr-low" && High(filter).Episode is not null &&
            filter.Lifecycle.Conditions.Single(s => s.ConditionId == "hr-low").Episode == low,
            "presentation priority does not conceal a separately confirmed opposite boundary episode");
        filter.Evaluate(limits, Snapshot(500, 180001), timing);
        Check.That(filter.Lifecycle.Transitions.Single(t => t.Kind == AlarmTransitionKind.Ended).Episode == low &&
            High(filter).State == AlarmConditionState.Active, "low recovery cannot end the high episode");
        var saturation = new ConfirmedLimitNotice(MonitorNumeric.SpO2);
        Check.That(saturation.Lifecycle.Conditions.Count == 1 && saturation.Lifecycle.Conditions[0].ConditionId == "spo2-low",
            "unsupported upper saturation conditions are not invented");
    }

    private static void AbsenceLifecycleSeparatesInvalidEvidenceAndRecovery()
    {
        var filter = new ConfirmedNoExpirationNotice();
        var timing = new BoundaryConfirmationTiming(200, 200);
        MonitorNotice? At(long milliseconds, long? expiration = null, long since = 0) => filter.Evaluate(true, 5, milliseconds * 1_000_000,
            new(WaveformMeasurementStatus.Valid, since * 1_000_000, expiration * 1_000_000, milliseconds * 1_000_000), timing);
        At(6000); At(6200);
        var first = filter.Lifecycle.Conditions[0].Episode;
        At(6300, 6300); At(6500, 6500);
        Check.That(filter.Lifecycle.Transitions[^1] is { Kind: AlarmTransitionKind.Ended, Reason: AlarmTransitionReason.Recovered } &&
            filter.Lifecycle.Transitions[^1].Episode == first, "CO2 recovery requires confirmed returned expiration evidence");
        At(6600); At(6800);
        At(6900, since: 1);
        Check.That(filter.Lifecycle.Transitions.Any(t => t.Kind == AlarmTransitionKind.Ended && t.Reason == AlarmTransitionReason.SignalSegmentChanged),
            "changed usable segment explicitly interrupts the episode");
        At(7100, since: 1);
        filter.Evaluate(true, 5, 7200_000_000, null, timing);
        Check.That(filter.Lifecycle.Conditions[0].State == AlarmConditionState.Indeterminate &&
            filter.Lifecycle.Transitions[^1].Reason == AlarmTransitionReason.DataUnavailable, "missing CO2 activity is not recovered physiology");
    }

    private static void AlarmJournalIsBoundedImmutableAndDeterministic()
    {
        var limits = MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true };
        var first = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var second = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var initial = first.Lifecycle.Conditions;
        for (int i = 0; i < 400; i++)
        {
            var snapshot = Snapshot(i * 100, i % 2 == 0 ? 180001 : 100000);
            first.Evaluate(limits, snapshot); second.Evaluate(limits, snapshot);
            int count = first.Lifecycle.Transitions.Count;
            first.Evaluate(limits, snapshot);
            Check.That(first.Lifecycle.Transitions.Count == count, "duplicate refresh cannot duplicate transitions");
        }
        Check.That(first.Lifecycle.Transitions.Count == AlarmLifecycleJournal.Capacity && first.Lifecycle.DroppedTransitionCount > 0 &&
            first.Lifecycle.Transitions.SequenceEqual(second.Lifecycle.Transitions), "bounded journal is deterministic and reports eviction");
        Check.That(initial.All(s => s.State == AlarmConditionState.Unobserved), "published snapshots are detached from future mutation");
        var retained = first.Lifecycle.Transitions;
        first.Reset();
        Check.That(retained.SequenceEqual(second.Lifecycle.Transitions), "journal snapshots remain immutable after reset");
        first.Evaluate(limits, Snapshot(0, 180001));
        Check.That(High(first).Episode!.Occurrence > 200, "session reset and clock rewind never reuse an occurrence within this owner");
    }
}
