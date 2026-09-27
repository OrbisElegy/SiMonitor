// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Owned presentation samples only: never a generator/measurement checkpoint.
// No dependency on the live ring remains after capture.
public sealed record FrozenMonitorRow(long Cycle, bool ShowPrevious, MonitorAmplitudeRange Range,
    MonitorAmplitudeRange PreviousRange, IReadOnlyList<(long TimeNs, double Value)> Samples);
public sealed class FrozenMonitorTrace
{
    public long FrontierNs { get; }
    public IReadOnlyList<FrozenMonitorRow> Rows { get; }
    private FrozenMonitorTrace(long frontier, FrozenMonitorRow[] rows)
    { FrontierNs = frontier; Rows = Array.AsReadOnly(rows); }
    public static FrozenMonitorTrace Capture(LocalMonitorPreviewSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var rows = session.Display.Slots.Select((slot, row) =>
        {
            long cycle = session.Ranges.RowCycle(row);
            long from = Math.Max(0, cycle - 1) * slot.DurationNs;
            var samples = session.Samples(slot.Channel, from, session.FrontierNs).ToArray();
            return new FrozenMonitorRow(cycle, session.Ranges.ShowPrevious(row), session.Ranges.Range(row),
                session.Ranges.PreviousRange(row), Array.AsReadOnly(samples));
        }).ToArray();
        return new(session.FrontierNs, rows);
    }
}
