// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

// One owner per pressure channel and live session. Confirmation follows the
// acquired simulation clock, never render counts or wall time. See
// docs/pressure-alarm-validation.md for device references and teaching choices.
public sealed class PressureLimitNotice
{
    public const long LowConfirmationNs = 4_000_000_000;
    public const long HighConfirmationNs = 10_000_000_000;
    public const long RecoveryConfirmationNs = 3_000_000_000;
    private const long MaximumObservationGapNs = 500_000_000;
    private readonly MeasurementLimitDescriptor _descriptor;
    private readonly Boundary _warningLow = new();
    private readonly Boundary _criticalLow = new();
    private readonly Boundary _warningHigh = new();
    private readonly Boundary _criticalHigh = new();
    private MeasurementLimits? _limits;
    private long? _lastSampleTimeNs;

    public PressureLimitNotice(MonitorNumeric numeric)
    {
        if (numeric is not (MonitorNumeric.AbpMean or MonitorNumeric.PaMean or MonitorNumeric.CvpMean))
        { throw new ArgumentException("PressureLimit.UnsupportedNumeric", nameof(numeric)); }
        _descriptor = MeasuredLimitNotice.Describe(numeric);
    }

    public MonitorNotice? Evaluate(MeasurementLimits limits, LiveMeasurementSnapshot snapshot)
    {
        // Validate before changing any state. Settings faults bypass the delay.
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
        if (_limits != limits || _lastSampleTimeNs is { } last &&
            (now < last || now - last > MaximumObservationGapNs))
        { Reset(); }
        _limits = limits;
        _lastSampleTimeNs = now;

        // Each threshold has its own evidence. Alternating Warning/Critical
        // must not restart the timer for an uninterrupted Warning violation.
        _warningLow.Update(measured < limits.WarningLow!.Value, now, LowConfirmationNs);
        _criticalLow.Update(measured < limits.CriticalLow!.Value, now, LowConfirmationNs);
        _warningHigh.Update(measured > limits.WarningHigh!.Value, now, HighConfirmationNs);
        _criticalHigh.Update(measured > limits.CriticalHigh!.Value, now, HighConfirmationNs);

        if (_criticalLow.Active) { return MeasuredLimitNotice.CreateNotice(_descriptor, low: true, critical: true); }
        if (_criticalHigh.Active) { return MeasuredLimitNotice.CreateNotice(_descriptor, low: false, critical: true); }
        if (_warningLow.Active) { return MeasuredLimitNotice.CreateNotice(_descriptor, low: true, critical: false); }
        if (_warningHigh.Active) { return MeasuredLimitNotice.CreateNotice(_descriptor, low: false, critical: false); }
        return null;
    }

    public void Reset()
    {
        _limits = null;
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

        public void Update(bool breached, long now, long confirmationNs)
        {
            if (breached == Active)
            {
                _pendingSinceNs = null;
                return;
            }
            _pendingSinceNs ??= now;
            long delay = breached ? confirmationNs : RecoveryConfirmationNs;
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
