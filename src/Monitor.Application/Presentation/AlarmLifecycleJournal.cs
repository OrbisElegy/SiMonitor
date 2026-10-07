// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public enum AlarmConditionState
{
    Unobserved,
    Disabled,
    Normal,
    PendingTrigger,
    Active,
    PendingRecovery,
    Indeterminate,
    Suppressed
}

public enum AlarmTransitionKind
{
    StateChanged,
    Started,
    SeverityChanged,
    Ended
}

public enum AlarmTransitionReason
{
    None,
    Confirmed,
    Recovered,
    Disabled,
    ConfigurationChanged,
    InvalidConfiguration,
    DataUnavailable,
    ObservationGap,
    ClockRewind,
    SignalSegmentChanged,
    SessionReset,
    Superseded
}

// Identity is scoped to the owning live confirmation filter, not globally unique.
public sealed record AlarmEpisodeId(string ConditionId, ulong Occurrence);
public sealed record AlarmConditionSnapshot(string ConditionId, AlarmConditionState State,
    AlarmEpisodeId? Episode, MonitorNoticeLevel? Level, long ChangedAtNs, AlarmTransitionReason Reason);
public sealed record AlarmLifecycleTransition(ulong Sequence, long SampleTimeNs, string ConditionId,
    AlarmEpisodeId? Episode, AlarmTransitionKind Kind, AlarmConditionState PreviousState,
    AlarmConditionState State, MonitorNoticeLevel? PreviousLevel, MonitorNoticeLevel? Level, AlarmTransitionReason Reason);

// Local runtime audit only; never serialized into preferences or continuity capsules.
// Callers receive immutable copies, and eviction is explicit rather than unbounded.
public sealed class AlarmLifecycleJournal
{
    public const int Capacity = 256;
    public AlarmAttentionJournal Attention { get; }
    private readonly Dictionary<string, AlarmConditionSnapshot> _conditions = new(StringComparer.Ordinal);
    private readonly Queue<AlarmLifecycleTransition> _transitions = new();
    private readonly Dictionary<string, AlarmNotificationController> _notifications = new(StringComparer.Ordinal);
    private readonly Queue<AlarmNotificationRecord> _notificationRecords = new();
    private ulong _notificationSequence;
    private ulong _eventSequence;
    private ulong _occurrence;
    internal long LastObservationNs { get; private set; }
    public ulong DroppedNotificationCount { get; private set; }
    public IReadOnlyList<AlarmNotificationRecord> NotificationRecords => Array.AsReadOnly(_notificationRecords.ToArray());
    public ulong DroppedTransitionCount { get; private set; }
    public IReadOnlyList<AlarmConditionSnapshot> Conditions => Array.AsReadOnly(_conditions.Values.OrderBy(s => s.ConditionId, StringComparer.Ordinal).ToArray());
    public IReadOnlyList<AlarmLifecycleTransition> Transitions => Array.AsReadOnly(_transitions.ToArray());

    internal AlarmLifecycleJournal(params string[] conditionIds)
    {
        Attention = new(conditionIds);
        foreach (string id in conditionIds)
        {
            _conditions.Add(id, new(id, AlarmConditionState.Unobserved, null, null, 0, AlarmTransitionReason.None));
            _notifications.Add(id, new());
        }
    }

    public AlarmNotificationPolicy NotificationPolicyFor(string conditionId) => Controller(conditionId).Policy;

    public void ConfigureNotifications(string conditionId, AlarmNotificationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        Controller(conditionId).Configure(policy);
    }

    private AlarmNotificationController Controller(string conditionId)
    {
        if (conditionId is null || !_notifications.TryGetValue(conditionId, out var controller))
        { throw new ArgumentException("AlarmNotification.UnknownCondition", nameof(conditionId)); }
        return controller;
    }

    private void RecordObservation(string id, long sampleTimeNs)
    {
        Attention.Observe(_conditions[id], sampleTimeNs);
        if (_notifications[id].Observe(_conditions[id], sampleTimeNs) is not { } decision) { return; }
        if (_notificationRecords.Count == Capacity)
        {
            _notificationRecords.Dequeue();
            DroppedNotificationCount++;
        }
        _notificationRecords.Enqueue(new(++_notificationSequence, decision));
    }

    internal void Observe(string id, long sampleTimeNs, MonitorNoticeLevel? level, bool pendingTrigger, bool pendingRecovery)
    {
        var state = level is not null
            ? pendingRecovery ? AlarmConditionState.PendingRecovery : AlarmConditionState.Active
            : pendingTrigger ? AlarmConditionState.PendingTrigger : AlarmConditionState.Normal;
        var previous = _conditions[id];
        var reason = level is not null && (previous.Episode is null || previous.Level != level) ? AlarmTransitionReason.Confirmed
            : level is null && previous.Episode is not null ? AlarmTransitionReason.Recovered : AlarmTransitionReason.None;
        Set(id, state, level, sampleTimeNs, reason);
    }

    internal void Interrupt(long sampleTimeNs, AlarmTransitionReason reason)
    {
        foreach (string id in _conditions.Keys.ToArray()) { Interrupt(id, sampleTimeNs, reason); }
    }

    internal void Interrupt(string id, long sampleTimeNs, AlarmTransitionReason reason)
    {
        var state = reason switch
        {
            AlarmTransitionReason.Superseded => AlarmConditionState.Suppressed,
            AlarmTransitionReason.Disabled => AlarmConditionState.Disabled,
            AlarmTransitionReason.ConfigurationChanged or AlarmTransitionReason.SessionReset => AlarmConditionState.Unobserved,
            _ => AlarmConditionState.Indeterminate
        };
        Set(id, state, null, sampleTimeNs, reason);
    }

    private void Set(string id, AlarmConditionState state, MonitorNoticeLevel? level, long sampleTimeNs, AlarmTransitionReason reason)
    {
        LastObservationNs = sampleTimeNs;
        var previous = _conditions[id];
        if (previous.State == state && previous.Level == level &&
            (state != AlarmConditionState.Indeterminate || previous.Reason == reason))
        {
            RecordObservation(id, sampleTimeNs);
            return;
        }
        bool active = level is not null;
        var episode = active ? previous.Episode ?? new(id, ++_occurrence) : null;
        var kind = previous.Episode is null && active ? AlarmTransitionKind.Started
            : previous.Episode is not null && !active ? AlarmTransitionKind.Ended
            : active && previous.Level != level ? AlarmTransitionKind.SeverityChanged : AlarmTransitionKind.StateChanged;
        _conditions[id] = new(id, state, episode, level, sampleTimeNs, reason);
        if (_transitions.Count == Capacity)
        {
            _transitions.Dequeue();
            DroppedTransitionCount++;
        }
        _transitions.Enqueue(new(++_eventSequence, sampleTimeNs, id, episode ?? previous.Episode, kind,
            previous.State, state, previous.Level, level, reason));
        RecordObservation(id, sampleTimeNs);
    }
}
