// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Monitor.Application.Presentation;

namespace Monitor.Infrastructure.Audio;

public sealed record MonitorAlarmSoundRequest(MonitorNoticeLevel Level, int VolumePercent, MonitorSoundTiming Timing)
{
    // Zero retains continuous legacy playback; positive values identify one group
    // in a monotonically increasing sequence scoped to this playback owner.
    public ulong NotificationSequence { get; init; }
    public void Validate()
    {
        if (!Enum.IsDefined(Level) || VolumePercent is < 0 or > 100 || Timing is null)
        { throw new ArgumentException("AlarmSound.InvalidRequest"); }
        Timing.Validate();
    }
}

public enum AlarmSoundDispatchStage
{
    Selected,
    CoalescedWhileBusy,
    SkippedSilent,
    Interrupted,
    RenderWindowElapsed
}

public sealed record AlarmSoundDispatchRecord(ulong NotificationSequence, AlarmSoundDispatchStage Stage, long RenderFrame);

// Explicitly enabled local preview sound. One worker owns the native stream;
// banner rotation never changes the highest active audible priority.
public sealed class MonitorAlarmPlayback(Func<IPumpedAudioOutput> createOutput)
{
    private MonitorAlarmSoundRequest? _request;
    private int _busy;
    private IPumpedAudioOutput? _output;
    private AudioOutputLifecycle? _owner;
    private bool _heartbeatEnabled;
    private bool _outputActive;
    // Reports successful output pumping, not physical sound or latency.
    public bool OutputActive => Volatile.Read(ref _outputActive);
    private BeatSubmission? _beat;
    private sealed record BeatSubmission(int Volume, int PitchPercent, long SubmittedAt);
    public void SetHeartbeatEnabled(bool enabled)
    {
        Volatile.Write(ref _heartbeatEnabled, enabled);
        if (!enabled) { Interlocked.Exchange(ref _beat, null); }
    }
    // Single-slot mailbox: a delayed worker drops old cues instead of catching up.
    public void SubmitHeartbeat(int volumePercent, int pitchPercent = 97)
    {
        if (pitchPercent is < 70 or > 97) { throw new ArgumentOutOfRangeException(nameof(pitchPercent)); }
        if (volumePercent is < 0 or > 100) { throw new ArgumentOutOfRangeException(nameof(volumePercent)); }
        if (Volatile.Read(ref _heartbeatEnabled) && Volatile.Read(ref _busy) != 0)
        { Interlocked.Exchange(ref _beat, new(volumePercent, pitchPercent, Stopwatch.GetTimestamp())); }
    }
    public void SetRequest(MonitorAlarmSoundRequest? request)
    {
        request?.Validate();
        Volatile.Write(ref _request, request);
    }
    public Task<SoundPreviewResult> RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { throw new InvalidOperationException("AlarmSound.AlreadyRunning"); }
        return Task.Factory.StartNew(() => Run(cancellationToken), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    private SoundPreviewResult Run(CancellationToken cancellationToken)
    {
        var result = SoundPreviewResult.Unavailable;
        try
        {
            if (!Close()) { return SoundPreviewResult.StopFailed; }
            if (cancellationToken.IsCancellationRequested) { return SoundPreviewResult.Stopped; }
            _output = createOutput(); _owner = new(_output);
            if (_owner.Replace(null, 0))
            {
                var sequencer = new MonitorAlarmSequencer(_owner.Session!);
                result = SoundPreviewResult.Stopped;
                while (!cancellationToken.IsCancellationRequested)
                {
                    sequencer.Update(Volatile.Read(ref _request));
                    var beat = Interlocked.Exchange(ref _beat, null);
                    int? volume = beat is not null && Stopwatch.GetElapsedTime(beat.SubmittedAt).TotalMilliseconds <= 250 ? beat.Volume : null;
                    sequencer.UpdateHeartbeat(Volatile.Read(ref _heartbeatEnabled), volume, beat?.PitchPercent ?? 97);
                    if (!_output.Pump() || !_owner.CheckHealth()) { result = SoundPreviewResult.Interrupted; break; }
                    Volatile.Write(ref _outputActive, true);
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException)
        { result = SoundPreviewResult.Unavailable; }
        finally
        {
            Volatile.Write(ref _outputActive, false);
            if (!Close()) { result = SoundPreviewResult.StopFailed; }
            Interlocked.Exchange(ref _beat, null);
            Volatile.Write(ref _busy, 0);
        }
        return result;
    }
    private bool Close()
    {
        if (_owner is not null && !_owner.Stop()) { return false; }
        _output?.Dispose(); _output = null; _owner = null; return true;
    }
}

public sealed class MonitorAlarmSequencer(AudioRenderSession session)
{
    private MonitorAlarmSoundRequest? _request;
    private long _origin, _lastTarget = -1, _key;
    private readonly Queue<long> _keys = new();
    private readonly Queue<long> _beatKeys = new();
    private long _beatKey;
    private IReadOnlyList<int> _onsets = [];
    private ulong _latestNotificationSequence;
    private long _singleGroupEndFrame;
    private bool _singleGroupFinished;
    private readonly Queue<AlarmSoundDispatchRecord> _dispatches = new();
    public ulong MissedNotificationCount { get; private set; }
    public ulong DroppedDispatchCount { get; private set; }
    public IReadOnlyList<AlarmSoundDispatchRecord> Dispatches => Array.AsReadOnly(_dispatches.ToArray());

    public void Update(MonitorAlarmSoundRequest? request)
    {
        request?.Validate();
        long position = session.RenderedThroughFrame;
        if (_request is { NotificationSequence: > 0 } active && !_singleGroupFinished && position >= _singleGroupEndFrame)
        {
            Record(active.NotificationSequence, AlarmSoundDispatchStage.RenderWindowElapsed);
            _singleGroupFinished = true;
        }
        if (request is { NotificationSequence: > 0 })
        {
            if (request.NotificationSequence > _latestNotificationSequence)
            {
                if (_latestNotificationSequence > 0)
                { MissedNotificationCount += request.NotificationSequence - _latestNotificationSequence - 1; }
                _latestNotificationSequence = request.NotificationSequence;
                if (request.VolumePercent == 0 || request.Level == MonitorNoticeLevel.Info && !request.Timing.InfoTone)
                {
                    Replace(null);
                    Record(request.NotificationSequence, AlarmSoundDispatchStage.SkippedSilent);
                }
                else if (_request is { NotificationSequence: > 0 } current && !_singleGroupFinished && request.Level <= current.Level)
                { Record(request.NotificationSequence, AlarmSoundDispatchStage.CoalescedWhileBusy); }
                else
                {
                    Replace(request);
                    Record(request.NotificationSequence, AlarmSoundDispatchStage.Selected);
                }
            }
            // Stale or repeated requests never replay after completion/cancellation.
        }
        else if (request != _request) { Replace(request); }

        var effective = _request;
        if (effective is null || effective.VolumePercent == 0 || effective.Level == MonitorNoticeLevel.Info && !effective.Timing.InfoTone) { return; }
        bool singleGroup = effective.NotificationSequence > 0;
        if (singleGroup && _singleGroupFinished) { return; }
        long period = effective.Timing.Period(effective.Level) * 48L;
        long cycle = singleGroup ? 0 : Math.Max(0, (position - _origin) / period);
        // Continuous mode covers one queue fill ahead. Single-group mode never
        // repeats, and skipped busy/missed groups are not queued for later replay.
        for (long group = cycle; group <= (singleGroup ? 0 : cycle + 1); group++)
            foreach (int onset in _onsets)
            {
                long target = _origin + group * period + onset * 48L;
                if (target < position || target <= _lastTarget || target > position + session.CapacityFrames + 480) { continue; }
                var tone = SelectedMonitorTones.Alarm(effective.Level, effective.VolumePercent);
                long key = checked(++_key);
                if (session.Schedule(key, tone, target, target + 2400) == ToneScheduleResult.Accepted)
                {
                    _lastTarget = target;
                    _keys.Enqueue(key);
                    if (_keys.Count > 32) { _keys.Dequeue(); }
                }
            }
    }

    private void Replace(MonitorAlarmSoundRequest? request)
    {
        if (_request is { NotificationSequence: > 0 } previous && !_singleGroupFinished)
        { Record(previous.NotificationSequence, AlarmSoundDispatchStage.Interrupted); }
        foreach (long key in _keys) { session.Cancel(key); }
        _keys.Clear();
        _request = request;
        _origin = session.RenderedThroughFrame + 480;
        _lastTarget = -1;
        _onsets = request is null ? [] : MonitorSoundPattern.OnsetsMilliseconds(request.Level);
        _singleGroupFinished = false;
        if (request is { NotificationSequence: > 0 })
        {
            var tone = SelectedMonitorTones.Alarm(request.Level, request.VolumePercent);
            _singleGroupEndFrame = _origin + _onsets[^1] * 48L + SelectedMonitorTones.Get(tone.Sample).Length;
        }
    }

    private void Record(ulong sequence, AlarmSoundDispatchStage stage)
    {
        if (_dispatches.Count == AlarmLifecycleJournal.Capacity)
        {
            _dispatches.Dequeue();
            DroppedDispatchCount++;
        }
        _dispatches.Enqueue(new(sequence, stage, session.RenderedThroughFrame));
    }

    public void UpdateHeartbeat(bool enabled, int? volumePercent, int pitchPercent = 97)
    {
        if (pitchPercent is < 70 or > 97) { throw new ArgumentOutOfRangeException(nameof(pitchPercent)); }
        if (volumePercent is < 0 or > 100) { throw new ArgumentOutOfRangeException(nameof(volumePercent)); }
        if (!enabled)
        {
            foreach (long key in _beatKeys) { session.Cancel(key); }
            _beatKeys.Clear(); return;
        }
        if (volumePercent is not { } volume || volume == 0) { return; }
        long target = session.RenderedThroughFrame + 480;
        long keyValue = checked(--_beatKey); // Separate namespace from alarm keys.
        if (session.Schedule(keyValue, SelectedMonitorTones.Heartbeat(volume, pitchPercent), target, target + 12000) == ToneScheduleResult.Accepted)
        {
            _beatKeys.Enqueue(keyValue);
            if (_beatKeys.Count > 4) { _beatKeys.Dequeue(); }
        }
    }
}
