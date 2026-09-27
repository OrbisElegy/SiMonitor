// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Local monotonic authority time, never patient/sweep time. This projection
// suppresses alarm audio only; it does not acknowledge or clear conditions.
public sealed class MonitorAudioPause
{
    private long _lastTime;
    public long? PausedUntilNs { get; private set; }
    public void Start(long authorityTimeNs, int durationSeconds)
    {
        ValidateTime(authorityTimeNs);
        if (durationSeconds is < 1 or > 3600) { throw new ArgumentOutOfRangeException(nameof(durationSeconds)); }
        long deadline = checked(authorityTimeNs + durationSeconds * 1_000_000_000L);
        _lastTime = authorityTimeNs; PausedUntilNs = deadline;
    }
    public void Resume(long authorityTimeNs)
    {
        ValidateTime(authorityTimeNs);
        _lastTime = authorityTimeNs; PausedUntilNs = null;
    }
    public int RemainingSeconds(long authorityTimeNs)
    {
        ValidateTime(authorityTimeNs);
        _lastTime = authorityTimeNs;
        if (PausedUntilNs is not { } deadline) { return 0; }
        if (authorityTimeNs >= deadline) { PausedUntilNs = null; return 0; }
        return checked((int)((deadline - authorityTimeNs - 1) / 1_000_000_000 + 1));
    }
    private void ValidateTime(long time)
    {
        if (time < _lastTime) { throw new ArgumentException("AudioPause.BackwardsAuthorityTime"); }
    }
}
