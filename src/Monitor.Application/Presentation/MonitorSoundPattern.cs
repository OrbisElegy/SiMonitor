// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public sealed record MonitorSoundTiming(bool InfoTone = false, int InfoMilliseconds = 30000,
    int NoticeMilliseconds = 10000, int WarningMilliseconds = 5000, int CriticalMilliseconds = 500)
{
    public void Validate()
    {
        if (InfoMilliseconds is < 5000 or > 120000 || NoticeMilliseconds is < 1500 or > 60000 ||
            WarningMilliseconds is < 3500 or > 60000 || CriticalMilliseconds is < 250 or > 2000)
        { throw new ArgumentException("AlarmSound.InvalidTiming"); }
    }
    public int Period(MonitorNoticeLevel level) => level switch
    {
        MonitorNoticeLevel.Info => InfoMilliseconds,
        MonitorNoticeLevel.Notice => NoticeMilliseconds,
        MonitorNoticeLevel.Warning => WarningMilliseconds,
        MonitorNoticeLevel.Critical => CriticalMilliseconds,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}
public static class MonitorSoundPattern
{
    private static readonly IReadOnlyList<int> Single = Array.AsReadOnly(new[] { 0 });
    private static readonly IReadOnlyList<int> Triple = Array.AsReadOnly(new[] { 0, 250, 500 });
    private static readonly IReadOnlyList<int> DoubleFive = Array.AsReadOnly(new[] { 0, 200, 400, 800, 1000, 1600, 1800, 2000, 2400, 2600 });
    // Authored timing, not a manufacturer recording or medical alarm certification.
    public static IReadOnlyList<int> OnsetsMilliseconds(MonitorNoticeLevel level) => level switch
    {
        MonitorNoticeLevel.Info or MonitorNoticeLevel.Critical => Single,
        MonitorNoticeLevel.Notice => Triple,
        MonitorNoticeLevel.Warning => DoubleFive,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}
