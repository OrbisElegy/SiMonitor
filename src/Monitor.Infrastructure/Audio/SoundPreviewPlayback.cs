// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;

namespace Monitor.Infrastructure.Audio;

public interface IPumpedAudioOutput : IAudioOutputFactory, IDisposable
{
    public bool Pump();
}

public enum SoundPreviewResult { Completed, Stopped, Unavailable, Interrupted, StopFailed }

// Explicit settings audition, never a patient beat or alarm. All native calls
// run on the dedicated owner thread; the UI only requests cancellation.
public sealed class SoundPreviewPlayback(Func<IPumpedAudioOutput> createOutput)
{
    private int _busy;
    private IPumpedAudioOutput? _output;
    private AudioOutputLifecycle? _owner;

    public Task<SoundPreviewResult> PlayAsync(int volumePercent, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(volumePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volumePercent, 100);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        { throw new InvalidOperationException("SoundPreview.AlreadyPlaying"); }
        return Task.Factory.StartNew(() => Play(volumePercent, cancellationToken),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private SoundPreviewResult Play(int volume, CancellationToken cancellationToken)
    {
        var result = SoundPreviewResult.Unavailable;
        try
        {
            // Retain ownership after a failed join. Never unload that library
            // or create a second output while its callbacks might still run.
            if (_owner is not null && !Close()) { return SoundPreviewResult.StopFailed; }
            if (cancellationToken.IsCancellationRequested) { return SoundPreviewResult.Stopped; }
            _output = createOutput(); _owner = new(_output);
            if (_owner.Replace(null, 0))
            {
                var session = _owner.Session!;
                var tone = TonePreset.BeatAudition with { GainQ15 = TonePreset.BeatAudition.GainQ15 * volume / 100 };
                for (int i = 0; i < 3; i++)
                { session.Schedule(i + 1, tone, 4800 + i * 38400, 16800 + i * 38400); }
                long start = Stopwatch.GetTimestamp();
                result = SoundPreviewResult.Completed;
                while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(2.4))
                {
                    if (cancellationToken.IsCancellationRequested) { result = SoundPreviewResult.Stopped; break; }
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
        _output?.Dispose(); _output = null; _owner = null;
        return true;
    }
}
