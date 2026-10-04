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

    public MonitorNotice? Evaluate(bool enabled, int? delaySeconds, long nowNs, CapnographyActivity? activity,
        BoundaryConfirmationTiming? timing = null)
    {
        timing ??= DefaultTiming;
        timing.Validate();
        var instantaneous = NoExpirationNotice.Evaluate(enabled, delaySeconds, nowNs, activity);
        if (!enabled || instantaneous?.Level == MonitorNoticeLevel.Info || !NoExpirationNotice.HasUsableActivity(nowNs, activity))
        {
            Reset();
            return instantaneous;
        }

        long sampleNs = activity!.LastSampleNs!.Value;
        if (_delaySeconds != delaySeconds || _timing != timing || _continuousUsableSinceNs != activity.ContinuousUsableSinceNs ||
            _lastSampleNs is { } last && (sampleNs < last || sampleNs - last > 500_000_000) ||
            _lastObservationNs is { } observed && (nowNs < observed || nowNs - observed > 500_000_000))
        { Reset(); }
        _delaySeconds = delaySeconds;
        _timing = timing;
        _continuousUsableSinceNs = activity.ContinuousUsableSinceNs;
        _lastSampleNs = sampleNs;
        _lastObservationNs = nowNs;
        _confirmation.Update(instantaneous is not null, sampleNs, timing);
        return _confirmation.Active ? ActiveNotice : null;
    }

    public void Reset()
    {
        _confirmation.Reset();
        _timing = null;
        _delaySeconds = null;
        _continuousUsableSinceNs = null;
        _lastSampleNs = null;
        _lastObservationNs = null;
    }
}
