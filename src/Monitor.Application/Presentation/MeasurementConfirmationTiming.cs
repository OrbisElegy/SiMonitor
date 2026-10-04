// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public sealed record BoundaryConfirmationTiming(int TriggerMilliseconds, int RecoveryMilliseconds)
{
    public const int MaximumMilliseconds = 600_000;

    public void Validate()
    {
        if (TriggerMilliseconds is < 0 or > MaximumMilliseconds ||
            RecoveryMilliseconds is < 0 or > MaximumMilliseconds)
        { throw new ArgumentException("AlarmConfirmation.InvalidDuration"); }
    }
}

public sealed record MeasurementConfirmationTiming(
    BoundaryConfirmationTiming CriticalLow,
    BoundaryConfirmationTiming WarningLow,
    BoundaryConfirmationTiming WarningHigh,
    BoundaryConfirmationTiming CriticalHigh)
{
    public static MeasurementConfirmationTiming DefaultFor(MonitorNumeric numeric)
    {
        _ = MeasuredLimitNotice.Describe(numeric);
        return numeric is MonitorNumeric.AbpMean or MonitorNumeric.PaMean or MonitorNumeric.CvpMean
            ? new(new(4000, 3000), new(4000, 3000), new(10000, 3000), new(10000, 3000))
            : new(new(0, 0), new(0, 0), new(0, 0), new(0, 0));
    }

    public void ValidateFor(MonitorNumeric numeric)
    {
        _ = MeasuredLimitNotice.Describe(numeric);
        Validate();
        if (numeric == MonitorNumeric.SpO2 &&
            (WarningHigh != new BoundaryConfirmationTiming(0, 0) || CriticalHigh != new BoundaryConfirmationTiming(0, 0)))
        { throw new ArgumentException("AlarmConfirmation.UnsupportedSpO2HighTiming"); }
    }

    public void Validate()
    {
        if (CriticalLow is null || WarningLow is null || WarningHigh is null || CriticalHigh is null)
        { throw new ArgumentException("AlarmConfirmation.MissingBoundary"); }
        CriticalLow.Validate();
        WarningLow.Validate();
        WarningHigh.Validate();
        CriticalHigh.Validate();
    }
}
