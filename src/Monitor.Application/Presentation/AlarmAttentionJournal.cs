// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public enum AlarmLatchingMode
{
    NonLatching,
    UntilAcknowledged
}

public enum AlarmAttentionState
{
    None,
    ActiveUnacknowledged,
    ActiveAcknowledged,
    RecoveredUnacknowledged
}

public enum AlarmAttentionKind
{
    Started,
    Replaced,
    SeverityChanged,
    Acknowledged,
    Recovered,
    Interrupted,
    PolicyChanged
}

public sealed record AlarmAttentionSnapshot(string ConditionId, ulong Revision, AlarmEpisodeId? Episode,
    MonitorNoticeLevel? Level, AlarmAttentionState State, AlarmLatchingMode LatchingMode)
{
    public long? AcknowledgedAtNs { get; init; }
    public bool NeedsAcknowledgement => State is AlarmAttentionState.ActiveUnacknowledged or AlarmAttentionState.RecoveredUnacknowledged;
}

public sealed record AlarmAttentionRecord(ulong Sequence, long SampleTimeNs, AlarmAttentionKind Kind,
    AlarmAttentionSnapshot Previous, AlarmAttentionSnapshot Current, AlarmTransitionReason Reason);

// A separate runtime projection: acknowledging an indication never changes physiological evidence.
public sealed class AlarmAttentionJournal
{
    public const int Capacity = 256;
    private readonly Dictionary<string, AlarmAttentionSnapshot> _conditions = new(StringComparer.Ordinal);
    private readonly Queue<AlarmAttentionRecord> _records = new();
    private long _lastObservationNs;
    private ulong _sequence;
    public ulong DroppedRecordCount { get; private set; }
    public IReadOnlyList<AlarmAttentionSnapshot> Conditions => Array.AsReadOnly(_conditions.Values.OrderBy(s => s.ConditionId, StringComparer.Ordinal).ToArray());
    public IReadOnlyList<AlarmAttentionRecord> Records => Array.AsReadOnly(_records.ToArray());

    internal AlarmAttentionJournal(IEnumerable<string> conditionIds)
    {
        foreach (string id in conditionIds)
        {
            _conditions.Add(id, new(id, 0, null, null, AlarmAttentionState.None, AlarmLatchingMode.NonLatching));
        }
    }

    public void Configure(string conditionId, AlarmLatchingMode mode)
    {
        if (!Enum.IsDefined(mode)) { throw new ArgumentException("AlarmAttention.InvalidLatchingMode", nameof(mode)); }
        var previous = Find(conditionId);
        var next = previous with { LatchingMode = mode };
        if (mode == AlarmLatchingMode.NonLatching && next.State == AlarmAttentionState.RecoveredUnacknowledged)
        { next = Clear(next); }
        Publish(next, AlarmAttentionKind.PolicyChanged, AlarmTransitionReason.ConfigurationChanged);
    }

    // Both identity and revision are required: a delayed click must not acknowledge a new escalation.
    public bool Acknowledge(AlarmEpisodeId episode, ulong expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var previous = Find(episode.ConditionId);
        if (previous.Episode != episode || previous.Revision != expectedRevision || !previous.NeedsAcknowledgement) { return false; }
        var next = previous.State == AlarmAttentionState.RecoveredUnacknowledged
            ? Clear(previous) : previous with { State = AlarmAttentionState.ActiveAcknowledged, AcknowledgedAtNs = _lastObservationNs };
        Publish(next, AlarmAttentionKind.Acknowledged, AlarmTransitionReason.None);
        return true;
    }

    internal void Observe(AlarmConditionSnapshot condition, long sampleTimeNs)
    {
        _lastObservationNs = sampleTimeNs;
        var previous = Find(condition.ConditionId);
        if (condition.State is AlarmConditionState.Unobserved or AlarmConditionState.Disabled or AlarmConditionState.Indeterminate or AlarmConditionState.Suppressed)
        {
            Publish(Clear(previous), AlarmAttentionKind.Interrupted, condition.Reason);
            return;
        }
        if (condition.Episode is { } episode && condition.Level is { } level)
        {
            bool newEpisode = previous.Episode != episode;
            bool escalated = !newEpisode && level > previous.Level;
            var state = !newEpisode && !escalated && previous.State == AlarmAttentionState.ActiveAcknowledged
                ? AlarmAttentionState.ActiveAcknowledged : AlarmAttentionState.ActiveUnacknowledged;
            var kind = newEpisode ? previous.Episode is null ? AlarmAttentionKind.Started : AlarmAttentionKind.Replaced
                : AlarmAttentionKind.SeverityChanged;
            Publish(previous with
            {
                Episode = episode,
                Level = level,
                State = state,
                AcknowledgedAtNs = state == AlarmAttentionState.ActiveAcknowledged ? previous.AcknowledgedAtNs : null
            }, kind, condition.Reason);
        }
        else if (previous.State is AlarmAttentionState.ActiveUnacknowledged or AlarmAttentionState.ActiveAcknowledged)
        {
            var next = previous.State == AlarmAttentionState.ActiveUnacknowledged && previous.LatchingMode == AlarmLatchingMode.UntilAcknowledged
                ? previous with { State = AlarmAttentionState.RecoveredUnacknowledged } : Clear(previous);
            Publish(next, AlarmAttentionKind.Recovered, AlarmTransitionReason.Recovered);
        }
    }

    private AlarmAttentionSnapshot Find(string conditionId)
    {
        if (conditionId is null || !_conditions.TryGetValue(conditionId, out var state))
        { throw new ArgumentException("AlarmAttention.UnknownCondition", nameof(conditionId)); }
        return state;
    }

    private static AlarmAttentionSnapshot Clear(AlarmAttentionSnapshot state) => state with
    {
        Episode = null,
        Level = null,
        State = AlarmAttentionState.None,
        AcknowledgedAtNs = null
    };

    private void Publish(AlarmAttentionSnapshot next, AlarmAttentionKind kind, AlarmTransitionReason reason)
    {
        var previous = _conditions[next.ConditionId];
        if (previous == next) { return; }
        next = next with { Revision = checked(previous.Revision + 1) };
        ulong sequence = checked(_sequence + 1);
        var record = new AlarmAttentionRecord(sequence, _lastObservationNs, kind, previous, next, reason);
        _conditions[next.ConditionId] = next;
        _sequence = sequence;
        if (_records.Count == Capacity)
        {
            _records.Dequeue();
            DroppedRecordCount++;
        }
        _records.Enqueue(record);
    }
}
