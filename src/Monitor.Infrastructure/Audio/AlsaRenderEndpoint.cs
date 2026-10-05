// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Monitor.Infrastructure.Audio;

// Linux render stream through the system alsa-lib, which also reaches
// PipeWire and PulseAudio through their ALSA plugins. The owner thread opens,
// writes, waits and closes it; no managed code runs on an audio thread.
[SupportedOSPlatform("linux")]
internal sealed unsafe partial class AlsaRenderEndpoint : IRenderEndpoint
{
    private const string Library = "libasound.so.2";
    private const int PlaybackStream = 0;
    private const int FormatFloatLittleEndian = 14;
    private const int AccessReadWriteInterleaved = 3;
    private const int StateRunning = 3;
    private const int BrokenPipe = -32;
    private nint _pcm;
    private int _retired;
    private int _wakeQueuedFrames;

    private AlsaRenderEndpoint(nint pcm, int bufferFrames, int periodFrames)
    {
        _pcm = pcm;
        BufferFrames = bufferFrames;
        PeriodFrames = periodFrames;
    }

    public int BufferFrames { get; }
    public int PeriodFrames { get; }
    public bool IsRetired => Volatile.Read(ref _retired) != 0;

    // deviceId is an ALSA PCM name such as "default" or "null". Null when the
    // library, device or 48 kHz mono float configuration is unavailable.
    public static AlsaRenderEndpoint? Open(string? deviceId, int bufferFrames)
    {
        try
        {
            if (snd_pcm_open(out nint pcm, deviceId ?? "default", PlaybackStream, 0) < 0) { return null; }
            uint latencyMicroseconds = (uint)(bufferFrames * 1000L / AudioQueueTarget.FramesPerMillisecond);
            if (snd_pcm_set_params(pcm, FormatFloatLittleEndian, AccessReadWriteInterleaved, 1, ToneVoice.SampleRate, 1, latencyMicroseconds) < 0 ||
                snd_pcm_get_params(pcm, out nuint buffer, out nuint period) < 0 || buffer == 0 || buffer > int.MaxValue || period > int.MaxValue)
            {
                _ = snd_pcm_close(pcm);
                return null;
            }
            return new AlsaRenderEndpoint(pcm, (int)buffer, (int)period);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    // An xrun means the device already played silence: report zero queued frames.
    public bool TryGetPadding(out int queuedFrames)
    {
        queuedFrames = 0;
        nint available = snd_pcm_avail(_pcm);
        if (available == BrokenPipe) { return true; }
        if (available < 0)
        {
            Interlocked.Exchange(ref _retired, 1);
            return false;
        }
        queuedFrames = BufferFrames - (int)Math.Min(available, BufferFrames);
        return true;
    }

    public bool TryWrite(ReadOnlySpan<float> monoFrames)
    {
        fixed (float* frames = monoFrames)
        {
            return snd_pcm_writei(_pcm, frames, (nuint)monoFrames.Length) == monoFrames.Length;
        }
    }

    // Writing primed PCM may already reach the start threshold.
    public bool Start() => snd_pcm_state(_pcm) == StateRunning || snd_pcm_start(_pcm) >= 0;

    public bool Stop()
    {
        // Dropping pending frames cannot fail in a way that keeps audio playing.
        if (_pcm != 0) { _ = snd_pcm_drop(_pcm); }
        return true;
    }

    // PipeWire's ALSA plugin drains in graph quanta and derives its own
    // buffering from software parameters, so avail_min wake-ups are unreliable.
    // Sleep until the queue should reach the threshold; Linux sleeps are
    // sub-millisecond, and the next Pump observes errors.
    public void SetWakeThreshold(int queuedFrames) => _wakeQueuedFrames = Math.Max(0, queuedFrames);

    public void Wait(int timeoutMilliseconds)
    {
        if (!TryGetPadding(out int queuedFrames) || queuedFrames <= _wakeQueuedFrames) { return; }
        double milliseconds = (double)(queuedFrames - _wakeQueuedFrames) / AudioQueueTarget.FramesPerMillisecond;
        Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(milliseconds, timeoutMilliseconds)));
    }

    public NativeAudioClockSample ReadClock() => new(IsRetired ? -4 : -2, 0, 0, 0, 0);

    public void Dispose()
    {
        if (_pcm == 0) { return; }
        _ = snd_pcm_close(_pcm);
        _pcm = 0;
        Interlocked.Exchange(ref _retired, 1);
    }

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int snd_pcm_open(out nint pcm, string name, int stream, int mode);

    [LibraryImport(Library)]
    private static partial int snd_pcm_set_params(nint pcm, int format, int access, uint channels, uint rate, int softResample, uint latencyMicroseconds);

    [LibraryImport(Library)]
    private static partial int snd_pcm_get_params(nint pcm, out nuint bufferFrames, out nuint periodFrames);

    [LibraryImport(Library)]
    private static partial nint snd_pcm_avail(nint pcm);

    [LibraryImport(Library)]
    private static partial nint snd_pcm_writei(nint pcm, void* buffer, nuint frames);

    [LibraryImport(Library)]
    private static partial int snd_pcm_state(nint pcm);

    [LibraryImport(Library)]
    private static partial int snd_pcm_start(nint pcm);

    [LibraryImport(Library)]
    private static partial int snd_pcm_drop(nint pcm);

    [LibraryImport(Library)]
    private static partial int snd_pcm_close(nint pcm);
}
