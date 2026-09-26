// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public enum MonitorNoticeLevel { Info, Notice, Warning, Critical }
public enum MonitorNumeric { HeartRate, RespirationRate, SpO2, PulseRate, EtCo2, Co2RespirationRate, AbpMean, PaMean, CvpMean }
public sealed record MonitorNotice(string Id, MonitorNoticeLevel Level, string Text)
{
    public MonitorNumeric? Numeric { get; init; }
    public bool Audible { get; init; } = true;
}

// Local presentation arbitration, not patient alarm episode/acknowledgement state.
// Each active level gets one turn (2/4/6/8 seconds); rotate messages within that
// level. Population of low-level messages cannot dilute Critical's time share.
public sealed class MonitorNoticeRotation
{
    private readonly int[] _next = new int[4];
    private MonitorNotice[] _active = [];
    private long _lastTime, _until;
    public MonitorNotice? Current { get; private set; }
    public MonitorNoticeLevel? Highest => _active.Length == 0 ? null : _active.Max(n => n.Level);
    public void Update(IEnumerable<MonitorNotice> notices, long simulationTimeNs)
    {
        ArgumentNullException.ThrowIfNull(notices);
        if (simulationTimeNs < _lastTime || simulationTimeNs > long.MaxValue - 8_000_000_000) { throw new ArgumentException("Notice.TimeRegression"); }
        var items = notices.Take(65).ToArray();
        if (items.Length > 64 || items.Any(n => n is null || !Enum.IsDefined(n.Level) || string.IsNullOrWhiteSpace(n.Id) ||
            string.IsNullOrWhiteSpace(n.Text) || n.Id.Length > 128 || n.Text.Length > 512 || n.Numeric is { } numeric && !Enum.IsDefined(numeric)) || items.Select(n => n.Id).Distinct().Count() != items.Length)
        { throw new ArgumentException("Notice.InvalidSet"); }
        _active = items.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray(); _lastTime = simulationTimeNs;
        if (Highest is not { } highest) { Current = null; _until = simulationTimeNs; _previousHighest = MonitorNoticeLevel.Info; return; }
        bool lost = Current is null || !_active.Any(n => n.Id == Current.Id && n.Level == Current.Level);
        if (lost || highest > _previousHighest)
        { Select(highest, simulationTimeNs); }
        else if (simulationTimeNs >= _until)
        {
            int level = (int)Current!.Level;
            do { level = (level + 3) % 4; } while (!_active.Any(n => (int)n.Level == level));
            Select((MonitorNoticeLevel)level, simulationTimeNs);
        }
        else { Current = _active.Single(n => n.Id == Current!.Id); }
        _previousHighest = highest;
    }
    private MonitorNoticeLevel _previousHighest;
    private void Select(MonitorNoticeLevel level, long now)
    {
        var group = _active.Where(n => n.Level == level).ToArray();
        int index = _next[(int)level] % group.Length;
        Current = group[index]; _next[(int)level] = (index + 1) % group.Length;
        _until = checked(now + (2 + 2L * (int)level) * 1_000_000_000);
    }
}
