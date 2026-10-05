// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Scenarios;

// Each member names the integer unit its values use.
public enum VitalSign
{
    HeartRateBpm,
    RespiratoryRatePerMinute,
    SpO2MilliPercent,
    EtCo2MmHg,
    AbpSystolicCentiMmHg,
    AbpDiastolicCentiMmHg,
    PaSystolicCentiMmHg,
    PaDiastolicCentiMmHg,
    CvpCentiMmHg,
}

public enum VitalChangeTrigger { Manual, AfterDelay, AfterPrevious }

public enum VitalChangeState { Waiting, Active, Done, Cancelled }

public sealed record VitalChangeEvent(IReadOnlyDictionary<VitalSign, int> Targets, long DurationNs,
    VitalChangeTrigger Trigger, long DelayNs = 0);

public sealed record VitalChangeEntry(int Id, VitalChangeEvent Event, VitalChangeState State, long QueuedSimTimeNs,
    long? StartSimTimeNs);

// Queued teaching changes of individual vital signs over simulation time. Every
// sign ramps linearly from its value when an event starts to the event target,
// independently of other signs, so a later event takes over only the signs it
// names. The host turns emitted values into source continuations; the scheduler
// owns no waveform state and reads no clock.
public sealed class VitalChangeScheduler
{
    public const long StepNs = 2_000_000_000;
    public const long MaximumDurationNs = 3_600_000_000_000;
    public const long MaximumDelayNs = 3_600_000_000_000;
    public const int MaximumEventCount = 32;

    private sealed record Ramp(int OwnerId, int FromValue, int ToValue, long StartSimTimeNs, long DurationNs)
    {
        internal long EndSimTimeNs => StartSimTimeNs + DurationNs;

        internal int At(long simTimeNs) => simTimeNs >= EndSimTimeNs ? ToValue : simTimeNs <= StartSimTimeNs ? FromValue :
            FromValue + (int)FixedPointMath.RoundDivideTiesToEven(
                (Int128)(ToValue - FromValue) * (simTimeNs - StartSimTimeNs), DurationNs);
    }

    private readonly Dictionary<VitalSign, int> _baseline;
    private readonly Dictionary<VitalSign, Ramp> _ramps = [];
    private readonly List<VitalChangeEntry> _entries = [];
    private Dictionary<VitalSign, int> _published;
    private long _timeNs;
    private long _nextStepNs;
    private bool _changed;
    private int _nextId = 1;

    public VitalChangeScheduler(IReadOnlyDictionary<VitalSign, int> baseline, long simTimeNs)
    {
        _baseline = Validated(baseline, nameof(baseline));
        ArgumentOutOfRangeException.ThrowIfNegative(simTimeNs);
        _timeNs = simTimeNs;
        _published = new(_baseline);
    }

    // Supported ranges in each sign's unit; the generator may reject combinations
    // inside them, such as a pressure pair closer than its minimum pulse.
    public static (int Minimum, int Maximum) Range(VitalSign sign) => sign switch
    {
        VitalSign.HeartRateBpm => (30, 180),
        VitalSign.RespiratoryRatePerMinute => (6, 60),
        VitalSign.SpO2MilliPercent => (0, 100_000),
        VitalSign.EtCo2MmHg => (5, 80),
        VitalSign.AbpSystolicCentiMmHg => (5000, 25000),
        VitalSign.AbpDiastolicCentiMmHg => (2000, 15000),
        VitalSign.PaSystolicCentiMmHg => (1000, 10000),
        VitalSign.PaDiastolicCentiMmHg => (600, 6000),
        VitalSign.CvpCentiMmHg => (-500, 3000),
        _ => throw new ArgumentOutOfRangeException(nameof(sign)),
    };

    public IReadOnlyList<VitalChangeEntry> Events => _entries.AsReadOnly();
    public IReadOnlyCollection<VitalSign> Signs => _baseline.Keys;
    public bool IsChanging => _ramps.Values.Any(ramp => ramp.EndSimTimeNs > _timeNs);

    public IReadOnlyDictionary<VitalSign, int> ValuesAt(long simTimeNs) =>
        _baseline.Keys.ToDictionary(sign => sign, sign => _ramps.TryGetValue(sign, out var ramp) ? ramp.At(simTimeNs) : _baseline[sign]);

    public int Add(VitalChangeEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var targets = Validated(change.Targets, nameof(change));
        if (targets.Count == 0 || targets.Keys.Any(sign => !_baseline.ContainsKey(sign)))
        { throw new ArgumentException("VitalChange.UnsupportedSign", nameof(change)); }
        if (change.DurationNs is < 0 or > MaximumDurationNs || change.DelayNs is < 0 or > MaximumDelayNs ||
            !Enum.IsDefined(change.Trigger) || change.Trigger != VitalChangeTrigger.AfterDelay && change.DelayNs != 0)
        { throw new ArgumentException("VitalChange.InvalidTiming", nameof(change)); }
        if (_entries.Count(entry => entry.State is VitalChangeState.Waiting or VitalChangeState.Active) >= MaximumEventCount)
        { throw new InvalidOperationException("VitalChange.QueueFull"); }
        int id = _nextId++;
        _entries.Add(new(id, change with { Targets = targets }, VitalChangeState.Waiting, _timeNs, null));
        return id;
    }

    // Starts a waiting event now, whatever its trigger.
    public void Trigger(int id)
    {
        int index = IndexOf(id);
        if (_entries[index].State != VitalChangeState.Waiting) { throw new InvalidOperationException("VitalChange.NotWaiting"); }
        Start(index, _timeNs);
    }

    // Removes a waiting event; an active one is cancelled and its signs hold their current values.
    public void Remove(int id)
    {
        int index = IndexOf(id);
        var entry = _entries[index];
        if (entry.State == VitalChangeState.Active)
        { Freeze(_ramps.Where(pair => pair.Value.OwnerId == id).Select(pair => pair.Key).ToArray()); }
        if (entry.State is VitalChangeState.Waiting or VitalChangeState.Active)
        { _entries[index] = entry with { State = VitalChangeState.Cancelled }; }
    }

    // Holds every sign at its current value and cancels all unfinished events.
    public void Stop()
    {
        Freeze(_ramps.Keys.ToArray());
        CancelUnfinished(_ => true);
    }

    // Applied settings replace the baseline: running changes stop, and waiting events
    // that name signs the new settings cannot vary are cancelled. A restart moves
    // the clock back, so delays then count from the restart.
    public void Rebase(IReadOnlyDictionary<VitalSign, int> baseline, long simTimeNs)
    {
        var replacement = Validated(baseline, nameof(baseline));
        ArgumentOutOfRangeException.ThrowIfNegative(simTimeNs);
        _ramps.Clear();
        _baseline.Clear();
        foreach (var (sign, value) in replacement) { _baseline[sign] = value; }
        for (int index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            if (entry.State == VitalChangeState.Active ||
                entry.State == VitalChangeState.Waiting && entry.Event.Targets.Keys.Any(sign => !_baseline.ContainsKey(sign)))
            { _entries[index] = entry with { State = VitalChangeState.Cancelled }; }
            else if (entry.State == VitalChangeState.Waiting && simTimeNs < _timeNs)
            { _entries[index] = entry with { QueuedSimTimeNs = simTimeNs }; }
        }
        _timeNs = simTimeNs;
        _published = new(_baseline);
        _nextStepNs = simTimeNs;
        _changed = false;
    }

    // Starts due events and returns values to publish: at most once per step while
    // signs are changing, and once more with the exact targets when ramps end.
    // Returns null when nothing changed since the last publication.
    public IReadOnlyDictionary<VitalSign, int>? Advance(long simTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(simTimeNs, _timeNs);
        long previousNs = _timeNs;
        _timeNs = simTimeNs;
        bool started = StartDueEvents(simTimeNs);
        bool ended = _ramps.Values.Any(ramp => ramp.EndSimTimeNs > previousNs && ramp.EndSimTimeNs <= simTimeNs);
        if (!started && !ended && !_changed && (!IsChanging || simTimeNs < _nextStepNs)) { return null; }
        _changed = false;
        var values = ValuesAt(simTimeNs);
        _nextStepNs = simTimeNs + StepNs;
        if (values.All(pair => _published.TryGetValue(pair.Key, out int published) && published == pair.Value)) { return null; }
        _published = new(values);
        return values;
    }

    private bool StartDueEvents(long simTimeNs)
    {
        bool started = false;
        for (bool progress = true; progress;)
        {
            progress = false;
            // Finish events first so that events chained to them can start in this pass.
            for (int index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                if (entry.State == VitalChangeState.Active && entry.StartSimTimeNs + entry.Event.DurationNs <= simTimeNs)
                { _entries[index] = entry with { State = VitalChangeState.Done }; }
            }
            for (int index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                if (entry.State != VitalChangeState.Waiting) { continue; }
                long? due = entry.Event.Trigger switch
                {
                    VitalChangeTrigger.AfterDelay => entry.QueuedSimTimeNs + entry.Event.DelayNs,
                    VitalChangeTrigger.AfterPrevious => PreviousEndNs(index),
                    _ => null,
                };
                if (due is not { } startNs || startNs > simTimeNs) { continue; }
                Start(index, startNs);
                started = progress = true;
            }
        }
        return started;
    }

    // An event chained to its predecessor starts when that one finishes; a cancelled
    // or missing predecessor releases it when it was queued.
    private long? PreviousEndNs(int index)
    {
        if (index == 0) { return _entries[0].QueuedSimTimeNs; }
        var previous = _entries[index - 1];
        return previous.State switch
        {
            VitalChangeState.Done => Math.Max(previous.StartSimTimeNs!.Value + previous.Event.DurationNs, _entries[index].QueuedSimTimeNs),
            VitalChangeState.Cancelled => _entries[index].QueuedSimTimeNs,
            _ => null,
        };
    }

    private void Start(int index, long startNs)
    {
        var entry = _entries[index];
        startNs = Math.Max(startNs, entry.QueuedSimTimeNs);
        foreach (var (sign, target) in entry.Event.Targets)
        {
            int from = _ramps.TryGetValue(sign, out var ramp) ? ramp.At(startNs) : _baseline[sign];
            _ramps[sign] = new(entry.Id, from, target, startNs, entry.Event.DurationNs);
        }
        _changed = true;
        _entries[index] = entry with { State = VitalChangeState.Active, StartSimTimeNs = startNs };
    }

    private void Freeze(IEnumerable<VitalSign> signs)
    {
        foreach (var sign in signs)
        {
            if (!_ramps.Remove(sign, out var ramp)) { continue; }
            _baseline[sign] = ramp.At(_timeNs);
        }
        _changed = true;
    }

    private void CancelUnfinished(Func<VitalChangeEntry, bool> predicate)
    {
        for (int index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            if (entry.State is VitalChangeState.Waiting or VitalChangeState.Active && predicate(entry))
            { _entries[index] = entry with { State = VitalChangeState.Cancelled }; }
        }
    }

    private int IndexOf(int id)
    {
        int index = _entries.FindIndex(entry => entry.Id == id);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(id));
    }

    private static Dictionary<VitalSign, int> Validated(IReadOnlyDictionary<VitalSign, int> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        foreach (var (sign, value) in values)
        {
            if (!Enum.IsDefined(sign)) { throw new ArgumentException("VitalChange.UnknownSign", parameterName); }
            var (minimum, maximum) = Range(sign);
            if (value < minimum || value > maximum) { throw new ArgumentException("VitalChange.OutOfRange", parameterName); }
        }
        return new(values);
    }
}
