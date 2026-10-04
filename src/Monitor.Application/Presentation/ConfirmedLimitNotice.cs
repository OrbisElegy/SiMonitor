// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

// One owner per measured channel and live session. Confirmation follows the
// acquired simulation clock, never render counts or wall time.
public sealed class ConfirmedLimitNotice
{
    private const long MaximumObservationGapNs = 500_000_000;
    private readonly MeasurementLimitDescriptor _descriptor;
    private readonly Boundary _warningLow = new();
    private readonly Boundary _criticalLow = new();
    private readonly Boundary _warningHigh = new();
    private readonly Boundary _criticalHigh = new();
    private MeasurementLimits? _limits;
    private MeasurementConfirmationTiming? _timing;
    private long? _lastSampleTimeNs;

    public ConfirmedLimitNotice(MonitorNumeric numeric)
    {
        _descriptor = MeasuredLimitNotice.Describe(numeric);
    }

    public MonitorNotice? Evaluate(MeasurementLimits limits, LiveMeasurementSnapshot snapshot,
        MeasurementConfirmationTiming? timing = null)
    {
        // Validate the complete timing before changing any state.
        timing ??= MeasurementConfirmationTiming.DefaultFor(_descriptor.Numeric);
        timing.ValidateFor(_descriptor.Numeric);
        // Threshold settings faults bypass the delay.
        var instantaneous = MeasuredLimitNotice.Evaluate(_descriptor.Numeric, limits, snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(snapshot.SampleTimeNs);
        var (status, value) = MeasuredLimitNotice.Read(_descriptor.Numeric, snapshot);
        if (!limits.Enabled || instantaneous?.Level == MonitorNoticeLevel.Info ||
            status != WaveformMeasurementStatus.Valid || value is not { } measured ||
            measured < _descriptor.Minimum || measured > _descriptor.Maximum)
        {
            Reset();
            return instantaneous;
        }

        long now = snapshot.SampleTimeNs;
        if (_limits != limits || _timing != timing || _lastSampleTimeNs is { } last &&
            (now < last || now - last > MaximumObservationGapNs))
        { Reset(); }
        _limits = limits;
        _timing = timing;
        _lastSampleTimeNs = now;

        // Each threshold has its own evidence. Alternating Warning/Critical
        // must not restart the timer for an uninterrupted Warning violation.
        _warningLow.Update(measured < limits.WarningLow!.Value, now, timing.WarningLow);
        _criticalLow.Update(measured < limits.CriticalLow!.Value, now, timing.CriticalLow);
        _warningHigh.Update(limits.WarningHigh is { } warningHigh && measured > warningHigh, now, timing.WarningHigh);
        _criticalHigh.Update(limits.CriticalHigh is { } criticalHigh && measured > criticalHigh, now, timing.CriticalHigh);

        if (_criticalLow.Active) { return MeasuredLimitNotice.CreateNotice(_descriptor, low: true, critical: true); }
        if (_criticalHigh.Active) { return MeasuredLimitNotice.CreateNotice(_descriptor, low: false, critical: true); }
        if (_warningLow.Active) { return MeasuredLimitNotice.CreateNotice(_descriptor, low: true, critical: false); }
        if (_warningHigh.Active) { return MeasuredLimitNotice.CreateNotice(_descriptor, low: false, critical: false); }
        return null;
    }

    public void Reset()
    {
        _limits = null;
        _timing = null;
        _lastSampleTimeNs = null;
        _warningLow.Reset();
        _criticalLow.Reset();
        _warningHigh.Reset();
        _criticalHigh.Reset();
    }

    private sealed class Boundary
    {
        public bool Active { get; private set; }
        private long? _pendingSinceNs;

        public void Update(bool breached, long now, BoundaryConfirmationTiming timing)
        {
            if (breached == Active)
            {
                _pendingSinceNs = null;
                return;
            }
            _pendingSinceNs ??= now;
            long delay = (long)(breached ? timing.TriggerMilliseconds : timing.RecoveryMilliseconds) * 1_000_000;
            if (now - _pendingSinceNs.Value < delay) { return; }
            Active = breached;
            _pendingSinceNs = null;
        }

        public void Reset()
        {
            Active = false;
            _pendingSinceNs = null;
        }
    }
}
