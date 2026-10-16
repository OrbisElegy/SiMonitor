// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

public enum PreviewRecordEventKind
{
    SourceChanged,
    LeadChanged,
    VentilationChanged,
    PacingChanged,
    ShockDelivered,
    RhythmChanged,
    MonitoringChanged,
    NibpStarted,
    NibpCompleted,
    NibpCancelled,
    OperatorMarked
}

public sealed record PreviewRecordEvent(long TimeNs, PreviewRecordEventKind Kind, string Detail);
public sealed record MonitorPrintSample(long TimeNs, double Microvolts);

// Detached, read-only data on the session clock. Capturing never advances or pauses the source.
public sealed record MonitorPrintSnapshot(long CapturedAtNs, long AvailableFromNs, EcgLead DisplayLead,
    IReadOnlyList<IReadOnlyList<MonitorPrintSample>> Leads, IReadOnlyList<PreviewRecordEvent> Events);

public sealed partial class LocalMonitorPreviewSession
{
    private readonly List<PreviewRecordEvent> _recordEvents = [];
    private string _pendingSourceDescription = "";

    private void RecordEvent(PreviewRecordEventKind kind, string detail) =>
        _recordEvents.Add(new(SimulationTimeNs, kind, detail));

    public void MarkOperatorEvent() => RecordEvent(PreviewRecordEventKind.OperatorMarked, "");

    public MonitorPrintSnapshot CapturePrintSnapshot()
    {
        long from = Math.Max(0, SimulationTimeNs - RetainedBlockCount * 200_000_000L);
        var leads = Enum.GetValues<EcgLead>().Select(lead =>
        {
            // Keep unusable samples as explicit gaps, so printing cannot join across poor signal.
            var usableTimes = ReadEcgSamples(lead, from, SimulationTimeNs, usableOnly: true)
                .Select(sample => sample.TimeNs).ToHashSet();
            return (IReadOnlyList<MonitorPrintSample>)Array.AsReadOnly(EcgSamples(lead, from, SimulationTimeNs)
                .Select(sample => new MonitorPrintSample(sample.TimeNs,
                    usableTimes.Contains(sample.TimeNs) ? sample.Value : double.NaN)).ToArray());
        }).ToArray();
        long availableFrom = leads.SelectMany(lead => lead).Select(sample => sample.TimeNs).DefaultIfEmpty(SimulationTimeNs).Min();
        return new(SimulationTimeNs, availableFrom, DisplayEcgLead, Array.AsReadOnly(leads),
            Array.AsReadOnly(_recordEvents.Where(item => item.TimeNs >= availableFrom && item.TimeNs <= SimulationTimeNs)
                .OrderBy(item => item.TimeNs).ToArray()));
    }
}
