// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Specs;

internal static class PatientTimelineNodeSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(NodeTreatmentRulesFollowActualNodeOrder), NodeTreatmentRulesFollowActualNodeOrder),
        new(nameof(LateForkUsesRelativeNodeRulesAndResetsDestinationRules), LateForkUsesRelativeNodeRulesAndResetsDestinationRules),
        new(nameof(AcceptedShockRetainsItsRouteAcrossLaterPolicyNodes), AcceptedShockRetainsItsRouteAcrossLaterPolicyNodes),
        new(nameof(DisabledPacingNodeStillAllowsStopAndKeepsTheTimeline), DisabledPacingNodeStillAllowsStopAndKeepsTheTimeline),
        new(nameof(PacingUsesTheRouteOfTheLastEnteredNode), PacingUsesTheRouteOfTheLastEnteredNode),
        new(nameof(WaitingNodesPreserveSourcesAndResumeAtTheManualTime), WaitingNodesPreserveSourcesAndResumeAtTheManualTime),
        new(nameof(ManualNodeGuardsRejectDuplicatesAndSupersededBranches), ManualNodeGuardsRejectDuplicatesAndSupersededBranches),
        new(nameof(NodeProgramValidationRejectsBeforeChangingTheSession), NodeProgramValidationRejectsBeforeChangingTheSession),
        new(nameof(NodeProgramsPersistOrderedTriggersAndLegacyDefaults), NodeProgramsPersistOrderedTriggersAndLegacyDefaults),
    ];

    private static EcgElectricalTherapyProfile Profile(bool fibrillation = false, int pauseMilliseconds = 0) =>
        new(fibrillation ? "ecgTemplate.t021" : EcgElectricalTherapy.SinusTemplateId,
            new(true, 300, 150) { PostShockPauseMilliseconds = pauseMilliseconds })
        { PacingAllowed = true };

    private static LocalMonitorPreviewSession Session(bool fibrillation = false, int pauseMilliseconds = 0) =>
        new(fibrillation
                ? PhysiologyIllustrationConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration)
                : PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), enableMeasurements: true, opticalSaturationMilliPercent: 98000,
            manualVitals: new(new(120, 80, 93), 365), electricalTherapy: Profile(fibrillation, pauseMilliseconds));

    private static PatientTimelineDefinition Line(string id, IReadOnlyList<PatientTimelineEvent>? events = null,
        PatientTimelineTreatmentPolicy? policy = null) => new(id, "Line " + id, events ?? [], policy ?? new());

    private static PatientTimelineProgram Program(PatientTimelineTreatmentPolicy policy,
        IReadOnlyList<PatientTimelineEvent> events, params PatientTimelineDefinition[] branches) =>
        new("main", new[] { Line("main", events, policy) }.Concat(branches).ToArray());

    private static PatientTimelineEvent Rule(string name, long timeNs, PatientTimelineTreatmentPolicy policy) =>
        new(name, timeNs, 0, new()) { Treatment = policy };

    private static DeliveredElectricalShock Shock(LocalMonitorPreviewSession session, ulong sequence = 1) =>
        new(sequence, session.SimulationTimeNs, DefibrillationWaveformKind.BiphasicTruncatedExponential,
            DefibrillationMode.ManualAsynchronous, 200);

    private static void NodeTreatmentRulesFollowActualNodeOrder()
    {
        var session = Session();
        session.ConfigurePatientTimelineProgram(Program(new(false, true),
        [
            Rule("initial node", 0, new(true, false)),
            Rule("first simultaneous rule", 1_000_000_000, new(false, false)),
            new("simultaneous inherited state", 1_000_000_000, 0, new() { TemperatureDeciCelsius = 375 }),
            Rule("last simultaneous rule", 1_000_000_000, new(false, true)),
            new("later inherited state", 1_200_000_000, 0, new() { TemperatureDeciCelsius = 380 }),
            Rule("reopen", 1_400_000_000, new())
        ]));
        Check.That(!session.ShockAllowed && session.PacingAllowed && session.PatientTimelineTriggeredCount == 0,
            "the line's initial rule applies until the time-zero node actually enters");
        AdvanceTo(session, 900_000_000);
        Check.That(session.ShockAllowed && !session.PacingAllowed && session.PatientTimelineTriggeredCount == 1,
            "future policy nodes never change the currently available treatments early");
        bool denied = false;
        try { session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60)); }
        catch (InvalidOperationException error) when (error.Message == "Pacing.TemplateDisabled") { denied = true; }
        Check.That(denied && session.PendingSourceTimeNs is null, "the entered node's pacing gate reaches the application command");
        AdvanceTo(session, 1_000_000_000);
        Check.That(!session.ShockAllowed && session.PacingAllowed && session.PatientTimelineTriggeredCount == 4 &&
            session.EvaluateElectricalShock(DefibrillationWaveformKind.BiphasicTruncatedExponential,
                DefibrillationMode.ManualAsynchronous, 200).Outcome == ElectricalConversionOutcome.TimelineDisabled,
            "same-time nodes execute in author order and the last explicit policy controls actual shock eligibility");
        AdvanceTo(session, 1_200_000_000);
        Check.That(!session.ShockAllowed && session.PacingAllowed && session.ManualVitals.TemperatureDeciCelsius == 380,
            "a node without a policy preserves the last entered policy while changing its own targets");
        AdvanceTo(session, 1_400_000_000);
        Check.That(session.ShockAllowed && session.PacingAllowed, "a later explicit rule can reopen both treatments");
        var records = session.CapturePrintSnapshot().Events.Where(item => item.Kind == PreviewRecordEventKind.PatientTimelineEvent).ToArray();
        Check.That(records.Length == 6 && records[1].Detail == "first simultaneous rule" &&
            records[2].Detail == "simultaneous inherited state" && records[3].Detail == "last simultaneous rule",
            "pure policy nodes are recorded exactly once in the same stable order as other nodes");
    }

    private static void LateForkUsesRelativeNodeRulesAndResetsDestinationRules()
    {
        foreach (bool afterNode in new[] { false, true })
        {
            var session = Session();
            session.DiscardStartup();
            session.ConfigurePatientTimelineProgram(Program(new(ShockFailureTimelineId: "late"), [],
                Line("late", [Rule("change route", 1_000_000_000, new(ShockFailureTimelineId: "after")),
                    new("inherit route", 1_200_000_000, 0, new() { TemperatureDeciCelsius = 370 })],
                    new(ShockFailureTimelineId: "before")),
                Line("before", policy: new(false, false)), Line("after", policy: new(false, false))));
            AdvanceTo(session, 4_100_000_000);
            _ = session.ApplyElectricalShock(Shock(session), session.PrepareSinusAfterShock(Profile()));
            Check.That(session.CurrentTimelineId == "late" && session.TimelineOriginTimeNs == 4_100_000_000 &&
                session.PatientTimelineTriggeredCount == 0 && session.ShockAllowed,
                "a late fork starts with its destination's initial rule rather than a future relative node");
            AdvanceTo(session, afterNode ? 5_300_000_000 : 4_900_000_000);
            long treatmentTime = session.SimulationTimeNs;
            _ = session.ApplyElectricalShock(Shock(session, 2), session.PrepareSinusAfterShock(Profile()));
            Check.That(session.CurrentTimelineId == (afterNode ? "after" : "before") &&
                session.TimelineOriginTimeNs == treatmentTime && !session.ShockAllowed && !session.PacingAllowed,
                "actual treatment follows only entered relative nodes, and the next fork resets to its destination's initial permissions");
        }
    }

    private static void AcceptedShockRetainsItsRouteAcrossLaterPolicyNodes()
    {
        var session = Session(fibrillation: true, pauseMilliseconds: 600);
        session.ConfigurePatientTimelineProgram(Program(new(ShockSuccessTimelineId: "accepted"),
            [Rule("revoke and replace", 2_200_000_000, new(false, false, ShockSuccessTimelineId: "replacement"))],
            Line("accepted", policy: new(true, false)), Line("replacement", policy: new(false, false))));
        AdvanceTo(session, 2_000_000_000);
        var result = session.ApplyElectricalShock(Shock(session), session.PrepareSinusAfterShock(Profile()));
        Check.That(result.Outcome == ElectricalConversionOutcome.ConversionScheduled, "the actual shock is accepted under the current node rule");
        long effective = result.EffectiveSimTimeNs!.Value;
        AdvanceTo(session, effective - 200_000_000);
        Check.That(session.CurrentTimelineId == "main" && !session.ShockAllowed,
            "a later node applies its new gate during the already accepted post-shock response");
        AdvanceTo(session, effective);
        Check.That(session.CurrentTimelineId == "accepted" && session.TimelineOriginTimeNs == effective &&
            session.ShockAllowed && !session.PacingAllowed,
            "a later policy cannot reinterpret an earlier physical delivery or replace its captured success route");
    }

    private static void DisabledPacingNodeStillAllowsStopAndKeepsTheTimeline()
    {
        var session = Session();
        session.ConfigurePatientTimelineProgram(Program(new(),
        [
            new("warming", 0, 4_000_000_000, new() { TemperatureDeciCelsius = 405 }),
            Rule("disable further pacing", 2_000_000_000, new(AllowPacing: false))
        ]));
        AdvanceTo(session, 1_000_000_000);
        ulong generation = session.TimelineGeneration;
        session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
        AdvanceTo(session, 2_000_000_000);
        Check.That(session.ActivePacing == PacingIllustration.DualChamberDdd && !session.PacingAllowed,
            "a new gate restricts future operations without silently removing ongoing pacing");
        bool denied = false;
        try { session.SchedulePacing(PacingIllustration.DualChamberDdd, new(100, 70)); }
        catch (InvalidOperationException error) when (error.Message == "Pacing.TemplateDisabled") { denied = true; }
        Check.That(denied, "a node can deny further pacing adjustments");
        session.StopPacing();
        AdvanceTo(session, 4_000_000_000);
        Check.That(session.ActivePacing is null && session.CurrentTimelineId == "main" &&
            session.TimelineGeneration == generation && !session.PatientTimelineStopped &&
            session.ManualVitals.TemperatureDeciCelsius == 405 && !session.PacingAllowed,
            "stopping remains available under a closed pacing gate, and no-route treatment leaves the current timeline running");
    }

    private static void PacingUsesTheRouteOfTheLastEnteredNode()
    {
        foreach (bool afterNode in new[] { false, true })
        {
            var session = Session();
            session.ConfigurePatientTimelineProgram(Program(new(PacingTimelineId: "before"),
                [Rule("new pacing route", 1_000_000_000, new(PacingTimelineId: "after"))],
                Line("before", policy: new(AllowPacing: false)), Line("after", policy: new(AllowPacing: false))));
            AdvanceTo(session, afterNode ? 1_200_000_000 : 600_000_000);
            long activation = session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
            session.Advance(200_000_000);
            Check.That(session.CurrentTimelineId == (afterNode ? "after" : "before") &&
                session.TimelineOriginTimeNs == activation && session.ActivePacing == PacingIllustration.DualChamberDdd &&
                !session.PacingAllowed,
                "pacing takes the current node's route and inherits the destination's gate without undoing actual pacing");
            session.StopPacing();
            session.Advance(200_000_000);
            Check.That(session.ActivePacing is null, "the destination's closed gate also permits stopping inherited pacing");
        }
    }

    private static void WaitingNodesPreserveSourcesAndResumeAtTheManualTime()
    {
        var session = Session();
        var reference = Session();
        session.DiscardStartup();
        reference.DiscardStartup();
        var ongoing = new PatientTimelineEvent("ongoing ramp", 0, 10_000_000_000,
            new() { TemperatureDeciCelsius = 405, CvpCentiMmHg = 1400 });
        session.ConfigurePatientTimelineProgram(Program(new(),
        [
            ongoing,
            new("manual transition", 0, 2_000_000_000, new() { HeartRateBpm = 120, CvpCentiMmHg = 600, EtCo2MmHg = 20 })
            { Trigger = PatientTimelineTrigger.Manual },
            new("after manual", 600_000_000, 0, new() { TemperatureDeciCelsius = 375 })
            { Trigger = PatientTimelineTrigger.AfterPrevious },
            new("delay only", 200_000_000, 0, new()) { Trigger = PatientTimelineTrigger.AfterPrevious },
            new("after delay", 200_000_000, 0, new() { TemperatureDeciCelsius = 380 })
            { Trigger = PatientTimelineTrigger.AfterPrevious }
        ]));
        reference.ConfigurePatientTimelineProgram(Program(new(), [ongoing]));
        AdvanceTo(session, 5_000_000_000);
        AdvanceTo(reference, 5_000_000_000);
        Check.That(session.WaitingPatientTimelineEvent?.Name == "manual transition" && session.WaitingPatientTimelineEventIndex == 1 &&
            session.ManualVitals.TemperatureDeciCelsius == 385,
            "waiting stops node progression while an already started ramp continues to its current value");
        for (int channel = 0; channel < 7; channel++)
        {
            Check.That(session.Samples(channel, 0, 5_000_000_000).SequenceEqual(reference.Samples(channel, 0, 5_000_000_000)),
                "unknown manual timing cannot change any pre-wait acquisition, including future HR, pressure and gas targets");
        }
        ulong generation = session.TimelineGeneration;
        Check.That(session.TriggerPatientTimelineManualEvent(generation, 1) && session.SimulationTimeNs == 5_000_000_000 &&
            session.TimelineOriginTimeNs == 5_000_000_000 && session.TimelineGeneration == generation &&
            session.CurrentTimelineId == "main" && session.PatientTimelineTriggeredCount == 2,
            "manual activation is legal without advancing the paused clock and establishes a new T within the same line");
        Check.That(!session.TriggerPatientTimelineManualEvent(generation, 1), "the same manual node cannot be triggered twice");
        AdvanceTo(session, 5_400_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 387 &&
            session.Configuration.CvpBaselineCentiMmHgCurve!.At(LocalMonitorPreviewSession.StartupDiscardNs + 5_400_000_000) == 920,
            "manual targets ramp from the reached value while untouched pre-wait targets continue their original ramp");
        AdvanceTo(session, 5_600_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 375 && session.PatientTimelineTriggeredCount == 3,
            "the next delay is measured from the actual manual activation time");
        AdvanceTo(session, 6_000_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 380 && session.PatientTimelineTriggeredCount == 5,
            "pure delay nodes retain author order and anchor the following delay at their own entry time");
        var records = session.CapturePrintSnapshot().Events.Where(item => item.Kind == PreviewRecordEventKind.PatientTimelineEvent).ToArray();
        Check.That(records.Select(item => item.TimeNs).SequenceEqual(new long[]
            { 0, 5_000_000_000, 5_600_000_000, 5_800_000_000, 6_000_000_000 }),
            "manual and delayed node records use their actual absolute session times");
    }

    private static void ManualNodeGuardsRejectDuplicatesAndSupersededBranches()
    {
        var session = Session();
        session.ConfigurePatientTimelineProgram(Program(new(ShockFailureTimelineId: "branch"),
            [new("old wait", 0, 0, new()) { Trigger = PatientTimelineTrigger.Manual }],
            Line("branch", [new("new wait", 0, 0, new()) { Trigger = PatientTimelineTrigger.Manual },
                new("after new wait", 200_000_000, 0, new() { TemperatureDeciCelsius = 375 })
                { Trigger = PatientTimelineTrigger.AfterPrevious }], new(AllowShock: false))));
        session.Advance(200_000_000);
        ulong previousGeneration = session.TimelineGeneration;
        int previousIndex = session.WaitingPatientTimelineEventIndex!.Value;
        int recordsBefore = session.CapturePrintSnapshot().Events.Count;
        Check.That(!session.TriggerPatientTimelineManualEvent(previousGeneration, previousIndex + 1) &&
            session.SimulationTimeNs == 200_000_000 && session.CapturePrintSnapshot().Events.Count == recordsBefore,
            "a stale editor index has no effect on the waiting node, clock or record history");
        _ = session.ApplyElectricalShock(Shock(session), session.PrepareSinusAfterShock(Profile()));
        session.Advance(1_000_000);
        Check.That(session.CurrentTimelineId == "branch" && session.WaitingPatientTimelineEvent?.Name == "new wait" &&
            !session.TriggerPatientTimelineManualEvent(previousGeneration, previousIndex) && session.PatientTimelineTriggeredCount == 0,
            "a treatment fork cancels the old wait and its generation guard cannot trigger a different branch's same-numbered node");
        ulong generation = session.TimelineGeneration;
        int index = session.WaitingPatientTimelineEventIndex!.Value;
        long actualTime = session.SimulationTimeNs;
        Check.That(session.TriggerPatientTimelineManualEvent(generation, index), "the current waiting node accepts its own guard");
        int enteredRecords = session.CapturePrintSnapshot().Events.Count;
        Check.That(!session.TriggerPatientTimelineManualEvent(generation, index) && session.SimulationTimeNs == actualTime &&
            session.CapturePrintSnapshot().Events.Count == enteredRecords,
            "a duplicate click never advances time, duplicates records or consumes the following node");
        AdvanceTo(session, actualTime + 200_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 375 && !session.ShockAllowed,
            "the guarded manual entry resumes its own branch without restoring the old branch's treatment policy");
    }

    private static void NodeProgramValidationRejectsBeforeChangingTheSession()
    {
        var session = Session();
        var configuration = session.Configuration;
        Reject(() => session.ConfigurePatientTimelineProgram(Program(new(),
            [Rule("missing route", 1_000_000_000, new(ShockFailureTimelineId: "missing"))])));
        Reject(() => session.ConfigurePatientTimelineProgram(Program(new(),
            [new("bad manual offset", 200_000_000, 0, new()) { Trigger = PatientTimelineTrigger.Manual }])));
        Reject(() => session.ConfigurePatientTimelineProgram(Program(new(),
            [new("empty legacy node", 0, 0, new())])));
        Reject(() => session.ConfigurePatientTimeline([Rule("route without program", 0,
            new(PacingTimelineId: "unavailable"))]));
        Check.That(session.CurrentTimelineId is null && session.Configuration == configuration &&
            session.SimulationTimeNs == 0 && session.PatientTimeline.Count == 0 && session.ShockAllowed,
            "invalid node routes or trigger forms reject before changing the source or current permissions");
    }

    private static void NodeProgramsPersistOrderedTriggersAndLegacyDefaults()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-timeline-nodes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var program = Program(new(false, false),
            [
                new("automatic", 400_000_000, 0, new() { TemperatureDeciCelsius = 370 }) { Treatment = new(true, false) },
                new("manual", 0, 0, new() { TemperatureDeciCelsius = 380 }) { Trigger = PatientTimelineTrigger.Manual },
                new("delayed", 200_000_000, 0, new() { TemperatureDeciCelsius = 390 })
                { Trigger = PatientTimelineTrigger.AfterPrevious, Treatment = new(false, true) }
            ]);
            var preferences = new MonitorGeneratorPreferences(0, "sinus", 0, 0, "",
                new Dictionary<string, decimal?>(), new Dictionary<string, bool>(), new Dictionary<string, int>())
            { TimelineProgram = program };
            string path = Path.Combine(directory, "preferences.json");
            var store = new DisplayPreferenceStore(path);
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, Generator: preferences)), "ordered node program saves");
            string saved = File.ReadAllText(path);
            var invalid = preferences with { TimelineProgram = Program(new(), [Rule("bad route", 0, new(PacingTimelineId: "missing"))]) };
            Reject(() => store.Save(new(MonitorDisplayConfiguration.Default(), 0, Generator: invalid)));
            Check.That(File.ReadAllText(path) == saved,
                "a missing node route cannot replace an already saved valid program");
            var restored = store.Load(out bool rejected).Generator!.TimelineProgram!;
            Check.That(!rejected, "the saved ordered program remains readable");
            var session = Session();
            session.ConfigurePatientTimelineProgram(restored);
            AdvanceTo(session, 400_000_000);
            Check.That(session.WaitingPatientTimelineEvent?.Name == "manual" && session.ShockAllowed && !session.PacingAllowed,
                "saved author order keeps a manual wait after the automatic node despite its zero delay field");
            Check.That(session.TriggerPatientTimelineManualEvent(session.TimelineGeneration, session.WaitingPatientTimelineEventIndex!.Value),
                "the restored manual node enters normally");
            Check.That(session.ManualVitals.TemperatureDeciCelsius == 380 && session.ShockAllowed && !session.PacingAllowed,
                "an omitted saved node policy inherits the last entered rule");
            AdvanceTo(session, 600_000_000);
            Check.That(session.ManualVitals.TemperatureDeciCelsius == 390 && !session.ShockAllowed && session.PacingAllowed,
                "saved delayed triggers and explicit policy overrides retain their behavior");
            var document = JsonNode.Parse(saved)!;
            foreach (var line in document["Generator"]!["TimelineProgram"]!["Timelines"]!.AsArray())
            {
                foreach (var item in line!["Events"]!.AsArray())
                {
                    item!.AsObject().Remove("Trigger");
                    item.AsObject().Remove("Treatment");
                }
            }
            File.WriteAllText(path, document.ToJsonString());
            var legacy = store.Load(out rejected).Generator!.TimelineProgram!;
            Check.That(!rejected, "older programs without the new node fields still load");
            var oldSession = Session();
            oldSession.ConfigurePatientTimelineProgram(legacy);
            AdvanceTo(oldSession, 600_000_000);
            Check.That(oldSession.WaitingPatientTimelineEvent is null && oldSession.PatientTimelineTriggeredCount == 3 &&
                oldSession.ManualVitals.TemperatureDeciCelsius == 370 && !oldSession.ShockAllowed && !oldSession.PacingAllowed,
                "missing trigger fields preserve legacy absolute-time sorting and missing policies preserve the line's initial rule");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void AdvanceTo(LocalMonitorPreviewSession session, long timeNs, long stepNs = 200_000_000)
    {
        while (session.SimulationTimeNs < timeNs) { session.Advance(Math.Min(stepNs, timeNs - session.SimulationTimeNs)); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid node program accepted.");
    }
}
