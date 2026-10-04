// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Configuration only; active episodes, test notices and sound opt-in are excluded.
public sealed record MonitorAlarmPreferences(MeasurementLimits HeartRate, bool SpO2Enabled,
    int? SpO2Warning, int? SpO2Critical, bool NoExpirationEnabled, int? NoExpirationSeconds,
    IReadOnlyDictionary<MonitorNumeric, MeasurementLimits> Additional, bool NoticeColorEnabled)
{
    public IReadOnlyDictionary<MonitorNumeric, MeasurementConfirmationTiming> ConfirmationTimings { get; init; } =
        new Dictionary<MonitorNumeric, MeasurementConfirmationTiming>();

    public MeasurementConfirmationTiming ConfirmationFor(MonitorNumeric numeric) =>
        ConfirmationTimings.TryGetValue(numeric, out var timing) ? timing : MeasurementConfirmationTiming.DefaultFor(numeric);

    public static MonitorAlarmPreferences Default => new(new(false, 40000, 50000, 120000, 180000),
        false, 92000, 85000, false, 20,
        MeasuredLimitNotice.Descriptors.ToDictionary(d => d.Numeric, d => d.TeachingDefaults), true);

    public void Validate()
    {
        static bool InRange(int? value, int minimum, int maximum) => value is null || value >= minimum && value <= maximum;
        static bool Ordered(MeasurementLimits limits) => !limits.Enabled ||
            limits.CriticalLow is { } cl && limits.WarningLow is { } wl && limits.WarningHigh is { } wh && limits.CriticalHigh is { } ch && cl < wl && wl < wh && wh < ch;
        if (HeartRate is null || !Ordered(HeartRate) ||
            !InRange(HeartRate.CriticalLow, 1000, 298000) || !InRange(HeartRate.WarningLow, 1000, 299000) ||
            !InRange(HeartRate.WarningHigh, 20000, 300000) || !InRange(HeartRate.CriticalHigh, 21000, 350000) ||
            !InRange(SpO2Warning, 1000, 100000) || !InRange(SpO2Critical, 1000, 99000) ||
            SpO2Enabled && (SpO2Warning is null || SpO2Critical is null || SpO2Critical >= SpO2Warning) ||
            !InRange(NoExpirationSeconds, 5, 120) || NoExpirationEnabled && NoExpirationSeconds is null ||
            Additional is null || Additional.Count != MeasuredLimitNotice.Descriptors.Count)
        { throw new ArgumentException("AlarmPreferences.Invalid"); }
        if (ConfirmationTimings is null) { throw new ArgumentException("AlarmConfirmation.MissingConfiguration"); }
        foreach (var (numeric, timing) in ConfirmationTimings)
        {
            _ = MeasuredLimitNotice.Describe(numeric);
            if (timing is null) { throw new ArgumentException("AlarmConfirmation.MissingTiming"); }
            timing.ValidateFor(numeric);
        }
        foreach (var d in MeasuredLimitNotice.Descriptors)
        {
            if (!Additional.TryGetValue(d.Numeric, out var limits) || limits is null || !Ordered(limits) ||
                !InRange(limits.CriticalLow, d.Minimum, d.Maximum) || !InRange(limits.WarningLow, d.Minimum, d.Maximum) ||
                !InRange(limits.WarningHigh, d.Minimum, d.Maximum) || !InRange(limits.CriticalHigh, d.Minimum, d.Maximum))
            { throw new ArgumentException("AlarmPreferences.Invalid"); }
        }
    }
}
