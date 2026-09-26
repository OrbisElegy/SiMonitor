// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// Scheduler-owned affine mapping from supplied monotonic ticks to the 48kHz
// engine timeline. Observations must be paired positions on that same timeline,
// not callback arrival timestamps or native frames at another sample rate.
// This primitive estimates slope from the latest observation interval; device
// timestamp qualification/filtering and resampler compensation remain upstream.
public sealed class AudioClockBridge
{
    private long _ticks;
    private long _frame;
    private long _intervalTicks;
    private long _intervalFrames;

    public AudioClockBridge(long ticksPerSecond, long initialTicks, long initialFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegative(initialTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(initialFrame);
        _ticks = initialTicks; _frame = initialFrame;
        _intervalTicks = ticksPerSecond; _intervalFrames = ToneVoice.SampleRate;
    }

    public void Observe(long monotonicTicks, long engineFrame)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(monotonicTicks, _ticks);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(engineFrame, _frame);
        _intervalTicks = monotonicTicks - _ticks;
        _intervalFrames = engineFrame - _frame;
        _ticks = monotonicTicks; _frame = engineFrame;
    }

    // Ceiling maps to the first sample at/after the requested time. The same
    // mapping works for an exclusive expiry deadline, without double rounding.
    public long MapToFrame(long monotonicTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(monotonicTicks);
        Int128 numerator = ((Int128)monotonicTicks - _ticks) * _intervalFrames;
        Int128 delta = numerator / _intervalTicks;
        if (numerator % _intervalTicks > 0) { delta++; }
        long result = checked((long)(_frame + delta));
        ArgumentOutOfRangeException.ThrowIfNegative(result);
        return result;
    }
}
