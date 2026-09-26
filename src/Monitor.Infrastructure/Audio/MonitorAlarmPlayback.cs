// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;

namespace Monitor.Infrastructure.Audio;

public sealed record MonitorAlarmSoundRequest(MonitorNoticeLevel Level, int VolumePercent, MonitorSoundTiming Timing);

// Explicitly enabled local preview sound. One worker owns the native stream;
// banner rotation never changes the highest active audible priority.
public sealed class MonitorAlarmPlayback(Func<IPumpedAudioOutput> createOutput)
{
    private MonitorAlarmSoundRequest? _request;
    private int _busy;
    private IPumpedAudioOutput? _output;
    private AudioOutputLifecycle? _owner;
    public void SetRequest(MonitorAlarmSoundRequest? request)
    {
        if (request is not null)
        {
            if (!Enum.IsDefined(request.Level) || request.VolumePercent is < 0 or > 100 || request.Timing is null)
            { throw new ArgumentException("AlarmSound.InvalidRequest"); }
            request.Timing.Validate();
        }
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
                    if (!_output.Pump() || !_owner.CheckHealth()) { result = SoundPreviewResult.Interrupted; break; }
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException)
        { result = SoundPreviewResult.Unavailable; }
        finally
        {
            if (!Close()) { result = SoundPreviewResult.StopFailed; }
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
    private IReadOnlyList<int> _onsets = [];
    public void Update(MonitorAlarmSoundRequest? request)
    {
        if (request != _request)
        {
            if (request is not null)
            {
                if (!Enum.IsDefined(request.Level) || request.VolumePercent is < 0 or > 100 || request.Timing is null)
                { throw new ArgumentException("AlarmSound.InvalidRequest"); }
                request.Timing.Validate();
            }
            foreach (long key in _keys) { session.Cancel(key); }
            _keys.Clear(); _request = request; _origin = session.RenderedThroughFrame + 480; _lastTarget = -1;
            _onsets = request is null ? [] : MonitorSoundPattern.OnsetsMilliseconds(request.Level);
        }
        if (request is null || request.VolumePercent == 0 || request.Level == MonitorNoticeLevel.Info && !request.Timing.InfoTone) { return; }
        long position = session.RenderedThroughFrame;
        long period = request.Timing.Period(request.Level) * 48L;
        long cycle = Math.Max(0, (position - _origin) / period);
        // Cover one queue fill ahead, never replay missed bursts after a stall.
        for (long group = cycle; group <= cycle + 1; group++)
            foreach (int onset in _onsets)
            {
                long target = _origin + group * period + onset * 48L;
                if (target < position || target <= _lastTarget || target > position + session.CapacityFrames + 480) { continue; }
                var tone = new TonePreset("MonitorAlarmTeaching@1", request.Level == MonitorNoticeLevel.Critical ? 1200000 : 880000,
                    144, 2400, request.Level == MonitorNoticeLevel.Critical ? 5760 : 2400, 8192 * request.VolumePercent / 100);
                long key = checked(++_key);
                if (session.Schedule(key, tone, target, target + 2400) == ToneScheduleResult.Accepted)
                {
                    _lastTarget = target; _keys.Enqueue(key);
                    if (_keys.Count > 8) { _keys.Dequeue(); }
                }
            }
    }
}
