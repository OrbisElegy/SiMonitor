// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Intent scheduling only; neither duration changes physiological confirmation.
public sealed record AlarmNotificationPolicy(int RepeatSuppressionMilliseconds = 0, int ReminderMilliseconds = 0)
{
    public AlarmSoundDuration SoundDuration { get; init; }
    public const int MaximumMilliseconds = 3_600_000;
    public static AlarmNotificationPolicy Default { get; } = new();

    public void Validate()
    {
        if (!Enum.IsDefined(SoundDuration) || RepeatSuppressionMilliseconds is < 0 or > MaximumMilliseconds || ReminderMilliseconds is < 0 or > MaximumMilliseconds ||
            SoundDuration == AlarmSoundDuration.Continuous && (RepeatSuppressionMilliseconds != 0 || ReminderMilliseconds != 0))
        { throw new ArgumentException("AlarmNotification.InvalidDuration"); }
    }
}

public enum AlarmNotificationKind
{
    FirstOccurrence,
    Recurrence,
    SeverityEscalation,
    RepeatSuppressed,
    DeferredRepeat,
    Reminder,
    PolicyChanged
}

public sealed record AlarmNotificationDecision(AlarmEpisodeId Episode, MonitorNoticeLevel Level,
    long SampleTimeNs, AlarmNotificationKind Kind, AlarmNotificationPolicy Policy, long SuppressionRemainingNs = 0)
{
    // An intent is not proof of audible delivery or user acknowledgement.
    public bool RequestsNotification => Kind != AlarmNotificationKind.RepeatSuppressed;
}

public sealed record AlarmNotificationRecord(ulong Sequence, AlarmNotificationDecision Decision);

internal sealed class AlarmNotificationController
{
    private AlarmEpisodeId? _episode;
    private MonitorNoticeLevel? _previousLevel;
    private MonitorNoticeLevel? _lastIssuedLevel;
    private long? _lastIssuedNs;
    private long? _lastObservationNs;
    private bool _pendingRepeat;
    private bool _policyChangedWhileActive;
    internal AlarmNotificationPolicy Policy { get; private set; } = AlarmNotificationPolicy.Default;

    internal void Configure(AlarmNotificationPolicy policy)
    {
        if (Policy == policy) { return; }
        bool active = _episode is not null || _policyChangedWhileActive;
        Reset();
        Policy = policy;
        _policyChangedWhileActive = active;
    }

    internal AlarmNotificationDecision? Observe(AlarmConditionSnapshot condition, long sampleTimeNs)
    {
        if (_lastObservationNs is { } last && sampleTimeNs < last) { Reset(); }
        _lastObservationNs = sampleTimeNs;
        if (condition.State is AlarmConditionState.Unobserved or AlarmConditionState.Disabled or AlarmConditionState.Indeterminate)
        {
            Reset();
            return null;
        }
        // A superseded indication has no episode, but retains its repeat timer.
        // Only unavailable/disabled/reset observations discard that history.
        if (condition.Episode is not { } episode || condition.Level is not { } level)
        {
            _episode = null;
            _previousLevel = null;
            _pendingRepeat = false;
            _policyChangedWhileActive = false;
            return null;
        }

        bool changedEpisode = _episode != episode;
        bool escalated = !changedEpisode && _previousLevel is { } previous && level > previous;
        _episode = episode;
        _previousLevel = level;
        if (_policyChangedWhileActive) { return Issue(AlarmNotificationKind.PolicyChanged); }
        if (changedEpisode)
        {
            if (_lastIssuedNs is not { } issued) { return Issue(AlarmNotificationKind.FirstOccurrence); }
            long remainingNs = Policy.RepeatSuppressionMilliseconds * 1_000_000L - (sampleTimeNs - issued);
            if (level <= _lastIssuedLevel && remainingNs > 0)
            {
                _pendingRepeat = true;
                return new(episode, level, sampleTimeNs, AlarmNotificationKind.RepeatSuppressed, Policy, remainingNs);
            }
            return Issue(AlarmNotificationKind.Recurrence);
        }
        if (escalated) { return Issue(AlarmNotificationKind.SeverityEscalation); }
        if (_pendingRepeat && sampleTimeNs - _lastIssuedNs!.Value >= Policy.RepeatSuppressionMilliseconds * 1_000_000L)
        { return Issue(AlarmNotificationKind.DeferredRepeat); }
        if (!_pendingRepeat && Policy.ReminderMilliseconds > 0 && _lastIssuedNs is { } reminderStart &&
            sampleTimeNs - reminderStart >= Policy.ReminderMilliseconds * 1_000_000L)
        { return Issue(AlarmNotificationKind.Reminder); }
        return null;

        AlarmNotificationDecision Issue(AlarmNotificationKind kind)
        {
            _lastIssuedNs = sampleTimeNs;
            _lastIssuedLevel = level;
            _pendingRepeat = false;
            _policyChangedWhileActive = false;
            return new(episode, level, sampleTimeNs, kind, Policy);
        }
    }

    private void Reset()
    {
        _episode = null;
        _previousLevel = null;
        _lastIssuedLevel = null;
        _lastIssuedNs = null;
        _lastObservationNs = null;
        _pendingRepeat = false;
        _policyChangedWhileActive = false;
    }
}
