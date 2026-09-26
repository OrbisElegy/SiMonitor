// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// One scheduler producer, one callback consumer. No native device is opened.
// A fault is latched: after underrun/retirement this instance is never reused.
// Stop and join old callbacks before opening a replacement device stream.
public sealed class AudioRenderSession
{
    private readonly SampleToneRenderer _renderer;
    private readonly AudioPcmBuffer _buffer;
    private readonly float[] _scratch;
    private int _retired;
    private long _underrunFrames;

    public AudioRenderSession(long initialFrame = 0, int capacityMilliseconds = 40)
    {
        _renderer = new(initialFrame);
        _buffer = new(capacityMilliseconds: capacityMilliseconds);
        _scratch = new float[_buffer.CapacityFrames];
    }

    public bool RequiresReplacement => Volatile.Read(ref _retired) != 0;
    public long UnderrunFrames => Volatile.Read(ref _underrunFrames);
    public int CapacityFrames => _buffer.CapacityFrames;
    // Scheduler-only: native adapter drains this staging buffer immediately.
    public int BufferedFrames => CapacityFrames - _buffer.WritableFrames;
    internal void ReportNativeUnderrun(uint frames)
    {
        Interlocked.Add(ref _underrunFrames, frames);
        Retire();
    }
    // Producer-only position; never report this as a physical device cursor.
    public long RenderedThroughFrame => _renderer.Position;

    public ToneScheduleResult? Schedule(long key, TonePreset preset, long targetFrame, long expiryFrame) =>
        RequiresReplacement ? null : _renderer.Schedule(key, preset, targetFrame, expiryFrame);

    public void Cancel(long key) => _renderer.Cancel(key);

    // Full buffer leaves renderer and cue phase untouched; no pending PCM copy
    // is retained for later replay. Consumer only frees capacity between checks.
    public bool TryProduce(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frames, _scratch.Length);
        if (RequiresReplacement || _buffer.WritableFrames < frames) { return false; }
        _renderer.Render(_scratch.AsSpan(0, frames));
        if (RequiresReplacement) { return false; }
        bool written = _buffer.TryWrite(_scratch.AsSpan(0, frames));
        if (!written) { Retire(); }
        return written && !RequiresReplacement;
    }

    // Callback: actual PCM prefix plus silent tail on first underrun. Latch
    // before returning so a delayed producer cannot refill and replay old PCM.
    public int Read(Span<float> destination)
    {
        if (RequiresReplacement) { destination.Clear(); return 0; }
        int frames = _buffer.Read(destination);
        if (frames < destination.Length)
        {
            Interlocked.Add(ref _underrunFrames, destination.Length - frames);
            Retire();
        }
        if (RequiresReplacement && frames == destination.Length)
        { destination.Clear(); return 0; }
        return frames;
    }

    // May be called by device-control thread. An already-running callback must
    // still be joined by the owner; this is not an OS device stop operation.
    public void Retire() => Interlocked.Exchange(ref _retired, 1);
}
