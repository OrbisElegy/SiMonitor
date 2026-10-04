// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

// The detection window belongs to NoExpirationNotice. Only additional trigger
// and recovery confirmation is timed here, using the last acquired CO2 sample.
public sealed class ConfirmedNoExpirationNotice
{
    private readonly BoundaryConfirmation _confirmation = new();
    private BoundaryConfirmationTiming? _timing;
    private int? _delaySeconds;
    private long? _continuousUsableSinceNs;
    private long? _lastSampleNs;
    private long? _lastObservationNs;
    private static readonly BoundaryConfirmationTiming DefaultTiming = new(0, 0);
    private static readonly MonitorNotice ActiveNotice = new("co2-no-expiration", MonitorNoticeLevel.Critical, "CO₂ 未检出呼吸")
    { Numeric = MonitorNumeric.Co2RespirationRate };

    public AlarmLifecycleJournal Lifecycle { get; } = new("co2-no-expiration");

    public MonitorNotice? Evaluate(bool enabled, int? delaySeconds, long nowNs, CapnographyActivity? activity,
        BoundaryConfirmationTiming? timing = null)
    {
        timing ??= DefaultTiming;
        timing.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(nowNs);
        var instantaneous = NoExpirationNotice.Evaluate(enabled, delaySeconds, nowNs, activity);
        if (!enabled || instantaneous?.Level == MonitorNoticeLevel.Info || !NoExpirationNotice.HasUsableActivity(nowNs, activity))
        {
            var reason = !enabled ? AlarmTransitionReason.Disabled
                : instantaneous?.Level == MonitorNoticeLevel.Info ? AlarmTransitionReason.InvalidConfiguration : AlarmTransitionReason.DataUnavailable;
            Interrupt(nowNs, reason);
            return instantaneous;
        }

        long sampleNs = activity!.LastSampleNs!.Value;
        if (_timing is not null && (_delaySeconds != delaySeconds || _timing != timing))
        { Interrupt(sampleNs, AlarmTransitionReason.ConfigurationChanged); }
        else if (_lastSampleNs is { } last && sampleNs < last || _lastObservationNs is { } observed && nowNs < observed)
        { Interrupt(sampleNs, AlarmTransitionReason.ClockRewind); }
        else if (_lastSampleNs is { } previous && sampleNs - previous > 500_000_000 ||
            _lastObservationNs is { } observation && nowNs - observation > 500_000_000)
        { Interrupt(sampleNs, AlarmTransitionReason.ObservationGap); }
        else if (_continuousUsableSinceNs is not null && _continuousUsableSinceNs != activity.ContinuousUsableSinceNs)
        { Interrupt(sampleNs, AlarmTransitionReason.SignalSegmentChanged); }
        _delaySeconds = delaySeconds;
        _timing = timing;
        _continuousUsableSinceNs = activity.ContinuousUsableSinceNs;
        _lastSampleNs = sampleNs;
        _lastObservationNs = nowNs;
        _confirmation.Update(instantaneous is not null, sampleNs, timing);
        Lifecycle.Observe("co2-no-expiration", sampleNs, _confirmation.Active ? MonitorNoticeLevel.Critical : null,
            _confirmation.Pending, _confirmation.Active && _confirmation.Pending);
        return _confirmation.Active ? ActiveNotice : null;
    }

    public void Reset() => Reset(AlarmTransitionReason.SessionReset);

    public void Reset(AlarmTransitionReason reason)
    {
        if (reason is not (AlarmTransitionReason.SessionReset or AlarmTransitionReason.ConfigurationChanged or
            AlarmTransitionReason.InvalidConfiguration or AlarmTransitionReason.Disabled))
        { throw new ArgumentException("AlarmLifecycle.InvalidResetReason", nameof(reason)); }
        Interrupt(Lifecycle.LastObservationNs, reason);
    }

    private void Interrupt(long nowNs, AlarmTransitionReason reason)
    {
        Lifecycle.Interrupt(nowNs, reason);
        _confirmation.Reset();
        _timing = null;
        _delaySeconds = null;
        _continuousUsableSinceNs = null;
        _lastSampleNs = null;
        _lastObservationNs = null;
    }
}
