// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;

namespace Monitor.Infrastructure.Audio;

public sealed record AlarmSoundRoutingRecord(MonitorAlarmSoundRequest Request,
    IReadOnlyList<AlarmNotificationDecision> Decisions, bool Resumed)
{
    public IReadOnlyList<MonitorNotice> ContinuousNotices { get; init; } = [];
    public IReadOnlyList<AlarmConditionSnapshot> ContinuousConditions { get; init; } = [];
}

// One owner consumes one fixed set of journals on the UI/scheduler thread.
// The selected request is a mailbox value, not a claim of physical delivery.
public sealed class AlarmNotificationSoundRouter
{
    private readonly Dictionary<AlarmLifecycleJournal, ulong> _cursors = new();
    private readonly Dictionary<string, AlarmNotificationDecision> _latest = new(StringComparer.Ordinal);
    private readonly HashSet<string> _requestConditions = new(StringComparer.Ordinal);
    private readonly Queue<AlarmSoundRoutingRecord> _routes = new();
    public ulong DroppedRouteCount { get; private set; }
    public IReadOnlyList<AlarmSoundRoutingRecord> Routes => Array.AsReadOnly(_routes.ToArray());
    private bool _wasEnabled;
    private bool _bound;
    private MonitorNotice[] _continuousNotices = [];
    private AlarmConditionSnapshot[] _continuousConditions = [];
    private ulong _requestSequence;
    private MonitorAlarmSoundRequest? _request;
    public ulong MissedRecordCount { get; private set; }

    public MonitorAlarmSoundRequest? Update(IReadOnlyList<AlarmLifecycleJournal> journals,
        int volumePercent, MonitorSoundTiming timing, bool enabled, IReadOnlyList<MonitorNotice>? notices = null)
    {
        ArgumentNullException.ThrowIfNull(journals);
        new MonitorAlarmSoundRequest(MonitorNoticeLevel.Critical, volumePercent, timing).Validate();
        if (journals.Any(j => j is null) || journals.Distinct().Count() != journals.Count)
        { throw new ArgumentException("AlarmSound.InvalidJournals", nameof(journals)); }
        var conditions = journals.SelectMany(j => j.Conditions).ToArray();
        if (conditions.Select(c => c.ConditionId).Distinct(StringComparer.Ordinal).Count() != conditions.Length ||
            _cursors.Keys.Any(j => !journals.Contains(j)) ||
            _bound && journals.Any(j => !_cursors.ContainsKey(j)))
        { throw new ArgumentException("AlarmSound.ChangedOwners", nameof(journals)); }
        // Validate even silent/registered notices before consuming any intent.
        var items = notices?.ToArray() ?? [];
        if (items.Length > 64 || items.Any(n => n is null || !Enum.IsDefined(n.Level) ||
            string.IsNullOrWhiteSpace(n.Id) || n.Id.Length > 128 || string.IsNullOrWhiteSpace(n.Text) || n.Text.Length > 512 ||
            n.Numeric is { } numeric && !Enum.IsDefined(numeric)) ||
            items.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
        { throw new ArgumentException("AlarmSound.InvalidNotices", nameof(notices)); }
        var registered = conditions.Select(c => c.ConditionId).ToHashSet(StringComparer.Ordinal);
        var continuous = items.Where(n => !registered.Contains(n.Id) && n.Audible &&
            (n.Level != MonitorNoticeLevel.Info || timing.InfoTone)).ToArray();
        _bound = true;
        var due = new HashSet<string>(StringComparer.Ordinal);
        foreach (var journal in journals)
        {
            ulong cursor = _cursors.GetValueOrDefault(journal);
            var records = journal.NotificationRecords;
            if (records.Count > 0 && records[0].Sequence > cursor + 1)
            { MissedRecordCount += records[0].Sequence - cursor - 1; }
            foreach (var record in records.Where(r => r.Sequence > cursor))
            {
                string id = record.Decision.Episode.ConditionId;
                _latest[id] = record.Decision;
                if (record.Decision.RequestsNotification) { due.Add(id); }
                else { due.Remove(id); }
                cursor = record.Sequence;
            }
            _cursors[journal] = cursor;
        }
        bool canPlay = enabled && volumePercent > 0;
        var active = conditions.Where(c => c.Episode is not null && c.Level is not null).ToArray();
        AlarmNotificationPolicy PolicyFor(string id) => journals.Single(j => j.Conditions.Any(c => c.ConditionId == id)).NotificationPolicyFor(id);
        var longConditions = active.Where(c => PolicyFor(c.ConditionId).SoundDuration == AlarmSoundDuration.Continuous).ToArray();
        bool Eligible(AlarmConditionSnapshot c) => _latest.TryGetValue(c.ConditionId, out var decision) &&
            decision.RequestsNotification && decision.Episode == c.Episode && decision.Level == c.Level &&
            PolicyFor(c.ConditionId) == decision.Policy;
        if (!canPlay)
        {
            _request = null;
            _requestConditions.Clear();
            _continuousNotices = [];
            _continuousConditions = [];
            _wasEnabled = false;
            return null;
        }
        bool resumed = !_wasEnabled;
        if (resumed)
        {
            foreach (var c in active.Where(Eligible)) { due.Add(c.ConditionId); }
        }
        _wasEnabled = true;
        var highest = active.Select(c => c.Level).DefaultIfEmpty(null).Max();
        var continuousHighest = continuous.Select(n => (MonitorNoticeLevel?)n.Level).Concat(longConditions.Select(c => c.Level)).Max();
        if (continuousHighest is { } level && (highest is null || level >= highest))
        {
            var sources = continuous.Where(n => n.Level == level).OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
            var physiological = longConditions.Where(c => c.Level == level).OrderBy(c => c.ConditionId, StringComparer.Ordinal).ToArray();
            var request = new MonitorAlarmSoundRequest(level, volumePercent, timing);
            if (_request != request || !_continuousNotices.SequenceEqual(sources) || !_continuousConditions.SequenceEqual(physiological))
            {
                Record(new(request, [], resumed)
                { ContinuousNotices = Array.AsReadOnly(sources), ContinuousConditions = Array.AsReadOnly(physiological) });
            }
            _request = request;
            _continuousNotices = sources;
            _continuousConditions = physiological;
            _requestConditions.Clear();
            return _request;
        }
        // A withdrawn/preempted continuous source cannot leave its old request
        // cached. Consumed physiological intents are not a deferred sound queue.
        if (_request is { NotificationSequence: 0 }) { _request = null; }
        _continuousNotices = [];
        _continuousConditions = [];
        var selected = active.Where(c => c.Level == highest && due.Contains(c.ConditionId) && Eligible(c)).ToArray();
        if (selected.Length > 0)
        {
            if (_request?.Level != highest) { _requestConditions.Clear(); }
            _request = new(highest!.Value, volumePercent, timing) { NotificationSequence = checked(++_requestSequence) };
            foreach (var c in selected) { _requestConditions.Add(c.ConditionId); }
            Record(new(_request, Array.AsReadOnly(selected.OrderBy(c => c.ConditionId, StringComparer.Ordinal)
                .Select(c => _latest[c.ConditionId]).ToArray()), resumed));
        }
        else if (_request is not null && (_request.Level < highest ||
            !active.Any(c => _requestConditions.Contains(c.ConditionId) && c.Level == _request.Level && Eligible(c))))
        {
            _request = null;
            _requestConditions.Clear();
        }
        return _request;
    }

    private void Record(AlarmSoundRoutingRecord record)
    {
        if (_routes.Count == AlarmLifecycleJournal.Capacity)
        {
            _routes.Dequeue();
            DroppedRouteCount++;
        }
        _routes.Enqueue(record);
    }
}
