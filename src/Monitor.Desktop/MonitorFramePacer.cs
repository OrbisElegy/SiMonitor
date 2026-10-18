// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

// UI pacing only: simulation time continues to use the measured elapsed time.
internal sealed class MonitorFramePacer
{
    private readonly long _timestampFrequency;
    private readonly int _framesPerSecond;
    private long _deadline;
    private long _fractionalTicks;

    internal MonitorFramePacer(long timestampFrequency, int framesPerSecond = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        if (framesPerSecond is < 1 or > 120) { throw new ArgumentOutOfRangeException(nameof(framesPerSecond)); }
        _timestampFrequency = timestampFrequency;
        _framesPerSecond = framesPerSecond;
    }

    internal TimeSpan Start(long timestamp)
    {
        _deadline = timestamp;
        _fractionalTicks = 0;
        AdvanceDeadline();
        return RemainingDelay(timestamp);
    }

    internal TimeSpan CompleteFrame(long timestamp)
    {
        AdvanceDeadline();
        if (_deadline <= timestamp)
        {
            // Drop missed deadlines without replaying frames or waiting for
            // another full period after work that exceeded the frame budget.
            _deadline = timestamp + _timestampFrequency / 1000 + (_timestampFrequency % 1000 == 0 ? 0 : 1);
            _fractionalTicks = 0;
            return TimeSpan.FromMilliseconds(1);
        }
        return RemainingDelay(timestamp);
    }

    private void AdvanceDeadline()
    {
        _deadline += _timestampFrequency / _framesPerSecond;
        _fractionalTicks += _timestampFrequency % _framesPerSecond;
        _deadline += _fractionalTicks / _framesPerSecond;
        _fractionalTicks %= _framesPerSecond;
    }

    private TimeSpan RemainingDelay(long timestamp) =>
        TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling((_deadline - timestamp) * 1000.0 / _timestampFrequency)));
}
