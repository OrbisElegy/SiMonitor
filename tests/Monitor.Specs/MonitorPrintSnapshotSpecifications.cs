// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Specs;

internal static class MonitorPrintSnapshotSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PrintSnapshotPreservesLiveSamplesAndAppliedEvents), PrintSnapshotPreservesLiveSamplesAndAppliedEvents),
        new(nameof(PrintSnapshotHandlesEmptyAndExpiredHistory), PrintSnapshotHandlesEmptyAndExpiredHistory),
        new(nameof(PrintSnapshotRecordsPacingAtActivation), PrintSnapshotRecordsPacingAtActivation),
        new(nameof(PrintSnapshotPreservesShockGapsAndDelivery), PrintSnapshotPreservesShockGapsAndDelivery)
    ];

    private static LocalMonitorPreviewSession Create() => new(PhysiologyIllustrationConfiguration.Default,
        MonitorDisplayConfiguration.Default(), enableTwelveLead: true);

    private static void Advance(LocalMonitorPreviewSession session, long untilNs)
    {
        while (session.SimulationTimeNs < untilNs) { session.Advance(Math.Min(50_000_000, untilNs - session.SimulationTimeNs)); }
    }

    private static void PrintSnapshotPreservesLiveSamplesAndAppliedEvents()
    {
        var session = Create();
        session.DiscardStartup();
        Advance(session, 1_000_000_000);
        session.ScheduleSource(Create(), 2_000_000_000, "cancelled");
        long effective = session.ScheduleSource(Create(), 400_000_000, "applied");
        Advance(session, effective);
        Check.That(session.CapturePrintSnapshot().Events.Count == 0, "a scheduled source is not reported as already active");
        session.Advance(50_000_000);
        session.SelectDisplayEcgLead(EcgLead.V1);
        session.SelectDisplayEcgLead(EcgLead.V1);
        var snapshot = session.CapturePrintSnapshot();
        Check.That(snapshot.Events.Count == 2 && snapshot.Events[0] == new PreviewRecordEvent(effective, PreviewRecordEventKind.SourceChanged, "applied"),
            "only the actual replacement is recorded, at activation rather than request time");
        Check.That(snapshot.Leads.Count == 12 && snapshot.DisplayLead == EcgLead.V1, "all twelve leads are retained for later selection");
        foreach (var lead in Enum.GetValues<EcgLead>())
        {
            Check.That(snapshot.Leads[(int)lead].Select(sample => (sample.TimeNs, sample.Microvolts))
                .SequenceEqual(session.EcgSamples(lead, 0, snapshot.CapturedAtNs)), "print samples exactly match the live stream");
        }
        var saved = snapshot.Leads[(int)EcgLead.II].ToArray();
        Advance(session, 3_500_000_000);
        session.SelectDisplayEcgLead(EcgLead.V6);
        Check.That(snapshot.CapturedAtNs == effective + 50_000_000 && snapshot.Events.Count == 2 &&
            saved.SequenceEqual(snapshot.Leads[(int)EcgLead.II]), "later source advancement and edits leave the snapshot unchanged");
        long markedAt = session.SimulationTimeNs;
        session.MarkOperatorEvent();
        Check.That(session.SimulationTimeNs == markedAt && snapshot.Events.Count == 2 &&
            session.CapturePrintSnapshot().Events[^1] == new PreviewRecordEvent(markedAt, PreviewRecordEventKind.OperatorMarked, ""),
            "the panel event key marks the current session clock without advancing or changing captured snapshots");
    }

    private static void PrintSnapshotHandlesEmptyAndExpiredHistory()
    {
        var session = Create();
        var empty = session.CapturePrintSnapshot();
        Check.That(empty.AvailableFromNs == 0 && empty.CapturedAtNs == 0 && empty.Leads.All(lead => lead.Count == 0),
            "an empty session does not synthesize print data");
        Advance(session, 200_000_000);
        session.SelectDisplayEcgLead(EcgLead.V1);
        var shortHistory = session.CapturePrintSnapshot();
        Check.That(shortHistory.Leads.All(lead => lead.Count == 50), "short history has only the acquired samples");
        Advance(session, 42_000_000_000);
        var retained = session.CapturePrintSnapshot();
        Check.That(retained.AvailableFromNs == 1_600_000_000 && retained.Events.Count == 0 &&
            retained.Leads.All(lead => lead.All(sample => sample.TimeNs >= retained.AvailableFromNs)),
            "events and samples expire on the same retention clock");
    }

    private static void PrintSnapshotPreservesShockGapsAndDelivery()
    {
        var session = Create();
        Advance(session, 1_000_000_000);
        var delivery = new DeliveredElectricalShock(1, session.SimulationTimeNs,
            DefibrillationWaveformKind.BiphasicTruncatedExponential, DefibrillationMode.ManualAsynchronous, 200);
        session.ApplyElectricalShock(delivery, Create());
        session.ApplyElectricalShock(delivery, Create());
        Advance(session, 1_200_000_000);
        var snapshot = session.CapturePrintSnapshot();
        Check.That(snapshot.Events.Count(item => item.Kind == PreviewRecordEventKind.ShockDelivered) == 1 &&
            snapshot.Events.Single(item => item.Kind == PreviewRecordEventKind.ShockDelivered).TimeNs == 1_000_000_000,
            "duplicate electrical delivery does not duplicate the printed event");
        Check.That(snapshot.Leads.All(lead => lead.Any(sample => sample.TimeNs >= 1_000_000_000 && double.IsNaN(sample.Microvolts))),
            "unusable shock recovery is a print gap on every lead, never a joined or fabricated trace");
    }

    private static void PrintSnapshotRecordsPacingAtActivation()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), enableTwelveLead: true,
            electricalTherapy: new(EcgElectricalTherapy.SinusTemplateId, ElectricalConversionSettings.Default) { PacingAllowed = true });
        session.SchedulePacing(PacingIllustration.RightVentricularVvi, new(60, 60));
        Check.That(session.CapturePrintSnapshot().Events.Count == 0, "pacing request is not reported before activation");
        session.Advance(200_000_000);
        session.StopPacing();
        session.Advance(50_000_000);
        var events = session.CapturePrintSnapshot().Events.Where(item => item.Kind == PreviewRecordEventKind.PacingChanged).ToArray();
        Check.That(events.Length == 2 && events[0].TimeNs == 0 && events[1].TimeNs == 200_000_000,
            "pacing start and stop are recorded at the actual source boundaries");
    }
}
