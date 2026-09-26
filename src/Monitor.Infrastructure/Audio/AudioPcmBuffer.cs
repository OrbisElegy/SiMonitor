// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// One scheduler producer and one device consumer per stream. Construct outside
// callbacks; retire the entire queue on stream replacement, never reset a live
// queue. This is PCM transport, not cue scheduling, expiry or clock recovery.
public sealed class AudioPcmBuffer
{
    private readonly float[] _samples;
    private long _writtenFrames;
    private long _readFrames;

    public AudioPcmBuffer(int sampleRate = ToneVoice.SampleRate, int channels = 1, int capacityMilliseconds = 40)
    {
        if (sampleRate is < 8_000 or > 192_000) { throw new ArgumentOutOfRangeException(nameof(sampleRate)); }
        if (channels is < 1 or > 8) { throw new ArgumentOutOfRangeException(nameof(channels)); }
        if (capacityMilliseconds is < 20 or > 100) { throw new ArgumentOutOfRangeException(nameof(capacityMilliseconds)); }
        SampleRate = sampleRate; Channels = channels;
        CapacityFrames = checked(sampleRate * capacityMilliseconds / 1000);
        _samples = new float[CapacityFrames * channels];
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int CapacityFrames { get; }

    // Producer-only snapshot. The consumer can only increase this capacity.
    public int WritableFrames => CapacityFrames - (int)(_writtenFrames - Volatile.Read(ref _readFrames));

    // Producer only. Whole frames and whole submissions: rejection never
    // overwrites unread audio or publishes a partial block. No retry/wait here.
    public bool TryWrite(ReadOnlySpan<float> interleaved)
    {
        if (interleaved.Length % Channels != 0) { return false; }
        int frames = interleaved.Length / Channels;
        long written = _writtenFrames;
        long read = Volatile.Read(ref _readFrames);
        if (frames > CapacityFrames - (written - read)) { return false; }
        for (int i = 0; i < interleaved.Length; i++)
        {
            if (!float.IsFinite(interleaved[i]) || Math.Abs(interleaved[i]) > 1) { return false; }
        }
        int offset = (int)(written % CapacityFrames) * Channels;
        int first = Math.Min(interleaved.Length, _samples.Length - offset);
        interleaved[..first].CopyTo(_samples.AsSpan(offset));
        interleaved[first..].CopyTo(_samples);
        Volatile.Write(ref _writtenFrames, written + frames);
        return true;
    }

    // Consumer only. Return actual PCM frames so the adapter can account for
    // underruns; silence always fills the remainder. Invalid device buffer
    // geometry produces silence without consuming data or throwing in callback.
    public int Read(Span<float> destination)
    {
        if (destination.Length % Channels != 0) { destination.Clear(); return 0; }
        long read = _readFrames;
        long written = Volatile.Read(ref _writtenFrames);
        int frames = (int)Math.Min(destination.Length / Channels, written - read);
        int count = frames * Channels;
        int offset = (int)(read % CapacityFrames) * Channels;
        int first = Math.Min(count, _samples.Length - offset);
        _samples.AsSpan(offset, first).CopyTo(destination);
        _samples.AsSpan(0, count - first).CopyTo(destination[first..]);
        destination[count..].Clear();
        Volatile.Write(ref _readFrames, read + frames);
        return frames;
    }
}
