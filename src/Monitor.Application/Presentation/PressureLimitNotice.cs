// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

// Retains the pressure-specific entry point and its existing teaching defaults.
public sealed class PressureLimitNotice
{
    public const long LowConfirmationNs = 4_000_000_000;
    public const long HighConfirmationNs = 10_000_000_000;
    public const long RecoveryConfirmationNs = 3_000_000_000;
    private readonly ConfirmedLimitNotice _notice;

    public PressureLimitNotice(MonitorNumeric numeric)
    {
        if (numeric is not (MonitorNumeric.AbpMean or MonitorNumeric.PaMean or MonitorNumeric.CvpMean))
        { throw new ArgumentException("PressureLimit.UnsupportedNumeric", nameof(numeric)); }
        _notice = new(numeric);
    }

    public MonitorNotice? Evaluate(MeasurementLimits limits, LiveMeasurementSnapshot snapshot) =>
        _notice.Evaluate(limits, snapshot);

    public void Reset() => _notice.Reset();
}
