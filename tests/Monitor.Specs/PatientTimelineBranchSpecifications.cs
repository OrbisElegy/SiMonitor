// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;
using Monitor.Simulation.Therapy;

namespace Monitor.Specs;

internal static class PatientTimelineBranchSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(TimelineTreatmentPolicyRejectsBeforeDeliveryOrPacingMutation), TimelineTreatmentPolicyRejectsBeforeDeliveryOrPacingMutation),
        new(nameof(FailedActualShockForksFromCurrentStateAndPreservesHistory), FailedActualShockForksFromCurrentStateAndPreservesHistory),
        new(nameof(RealtimeOxygenationForkRetainsActualTreatmentTimeBetweenSolverSamples), RealtimeOxygenationForkRetainsActualTreatmentTimeBetweenSolverSamples),
        new(nameof(TreatmentForkCancelsPressureTargetsInRetainedReservoirHistory), TreatmentForkCancelsPressureTargetsInRetainedReservoirHistory),
        new(nameof(SuccessfulShockForksAtFinalConversionAfterItsPause), SuccessfulShockForksAtFinalConversionAfterItsPause),
        new(nameof(FailureForkCancelsAnOlderPendingSuccessConversion), FailureForkCancelsAnOlderPendingSuccessConversion),
        new(nameof(IncompatibleTreatmentBranchPreservesActualTreatmentAndCurrentTimeline), IncompatibleTreatmentBranchPreservesActualTreatmentAndCurrentTimeline),
        new(nameof(PacingForksOnlyOnActualStartAndCanForkAfterRestart), PacingForksOnlyOnActualStartAndCanForkAfterRestart),
        new(nameof(MissingTreatmentRoutesContinueTheCurrentTimeline), MissingTreatmentRoutesContinueTheCurrentTimeline),
        new(nameof(NonCardiacTimelineEventsPreserveAnEarlierTreatmentResponse), NonCardiacTimelineEventsPreserveAnEarlierTreatmentResponse),
        new(nameof(InvalidTimelineProgramsLeaveTheSessionUntouched), InvalidTimelineProgramsLeaveTheSessionUntouched),
        new(nameof(TimelineProgramsPersistRoutesAndRetainLegacyFlatEvents), TimelineProgramsPersistRoutesAndRetainLegacyFlatEvents),
    ];

    private static EcgElectricalTherapyProfile Profile(bool fibrillation, int pauseMilliseconds = 0) =>
        new(fibrillation ? "ecgTemplate.t021" : EcgElectricalTherapy.SinusTemplateId,
            new(true, 300, 150) { PostShockPauseMilliseconds = pauseMilliseconds })
        { PacingAllowed = true };

    private static LocalMonitorPreviewSession Session(bool fibrillation = false, int pauseMilliseconds = 0, bool twelveLead = false) =>
        new(fibrillation
                ? PhysiologyIllustrationConfiguration.Disorganized(AvConductionPattern.VentricularFibrillationCoarseIllustration)
                : PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), enableMeasurements: true, opticalSaturationMilliPercent: 98000,
            manualVitals: new(new(120, 80, 93), 365), electricalTherapy: Profile(fibrillation, pauseMilliseconds),
            enableTwelveLead: twelveLead);

    private static PatientTimelineDefinition Line(string id, IReadOnlyList<PatientTimelineEvent>? events = null,
        PatientTimelineTreatmentPolicy? policy = null) => new(id, "Line " + id, events ?? [], policy ?? new());

    private static PatientTimelineProgram CreateProgram(PatientTimelineTreatmentPolicy policy,
        IReadOnlyList<PatientTimelineEvent> events, params PatientTimelineDefinition[] branches) =>
        new("main", new[] { Line("main", events, policy) }.Concat(branches).ToArray());

    private static DeliveredElectricalShock Shock(LocalMonitorPreviewSession session, ulong sequence = 1, int energyJoules = 200) =>
        new(sequence, session.SimulationTimeNs, DefibrillationWaveformKind.BiphasicTruncatedExponential,
            DefibrillationMode.ManualAsynchronous, energyJoules);

    private static PatientTimelineEvent Warm(long durationNs = 4_000_000_000) =>
        new("original warming", 0, durationNs, new() { TemperatureDeciCelsius = 405 });

    private static void RealtimeOxygenationForkRetainsActualTreatmentTimeBetweenSolverSamples()
    {
        LocalMonitorPreviewSession Prepare(bool initialRealtime)
        {
            var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
                MonitorDisplayConfiguration.Default(), enableMeasurements: true,
                opticalSaturationMilliPercent: initialRealtime ? null : 98000,
                realtimeOxygenation: initialRealtime ? RealtimeOxygenationConfiguration.ReferenceAdult : null,
                manualVitals: new(new(120, 80, 93), 365), electricalTherapy: Profile(false));
            session.DiscardStartup();
            session.ConfigurePatientTimelineProgram(CreateProgram(new(ShockFailureTimelineId: "recovery"), [],
                Line("recovery", [new("relative warming", 200_000_000, 0, new() { TemperatureDeciCelsius = 390 })
                {
                    Ventilation = initialRealtime ? null : RealtimeOxygenationConfiguration.ReferenceAdult.Ventilation
                }], new(AllowShock: false))), oxygenationModel: RealtimeOxygenationConfiguration.ReferenceAdult);
            session.Advance(100_000_000);
            var oxygenation = session.Oxygenation;
            var result = session.ApplyElectricalShock(Shock(session), session.PrepareSinusAfterShock(Profile(false)));
            Check.That(result.Outcome == ElectricalConversionOutcome.NotShockable && session.CurrentTimelineId == "recovery" &&
                session.TimelineOriginTimeNs == 100_000_000 && session.TimelineBranchError is null && !session.ShockAllowed &&
                session.Oxygenation == oxygenation,
                "a treatment between oxygen solver samples enters its branch at the exact treatment time without resetting oxygen stores");
            return session;
        }
        foreach (bool initialRealtime in new[] { true, false })
        {
            var large = Prepare(initialRealtime);
            var small = Prepare(initialRealtime);
            large.Advance(200_000_000);
            for (int index = 0; index < 20; index++) { small.Advance(10_000_000); }
            large.Advance(1_000_000);
            small.Advance(1_000_000);
            Check.That(large.PatientTimelineTriggeredCount == 1 && small.PatientTimelineTriggeredCount == 1 &&
                large.ManualVitals.TemperatureDeciCelsius == 390 && small.ManualVitals.TemperatureDeciCelsius == 390 &&
                large.Oxygenation == small.Oxygenation && large.Oxygenation!.Value.SourceSimTimeNs > 100_000_000,
                "relative events retain their exact fork clock while continued or newly enabled oxygen transport stays on its deterministic solver grid");
        }
    }

    private static void TimelineTreatmentPolicyRejectsBeforeDeliveryOrPacingMutation()
    {
        var session = Session(fibrillation: true);
        session.ConfigurePatientTimelineProgram(CreateProgram(new(false, false), []));
        ulong generation = session.TimelineGeneration;
        var configuration = session.Configuration;
        var records = session.CapturePrintSnapshot().Events.ToArray();
        Check.That(!session.ShockAllowed && !session.PacingAllowed,
            "an empty named timeline can independently deny shock and pacing");
        var query = session.EvaluateElectricalShock(DefibrillationWaveformKind.BiphasicTruncatedExponential,
            DefibrillationMode.ManualAsynchronous, 200);
        var result = session.ApplyElectricalShock(Shock(session), session.PrepareSinusAfterShock(Profile(false)));
        bool pacingRejected = false;
        try { session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60)); }
        catch (InvalidOperationException error) when (error.Message == "Pacing.TemplateDisabled") { pacingRejected = true; }
        Check.That(query.Outcome == ElectricalConversionOutcome.TimelineDisabled &&
            result.Outcome == ElectricalConversionOutcome.TimelineDisabled && pacingRejected,
            "treatment gates apply to the application APIs as well as the displayed permission");
        Check.That(session.Configuration == configuration && session.SimulationTimeNs == 0 &&
            session.CurrentTimelineId == "main" && session.TimelineGeneration == generation &&
            session.ShockArtifacts.Count == 0 && session.PendingSourceTimeNs is null &&
            session.CapturePrintSnapshot().Events.SequenceEqual(records),
            "denied treatment publishes no artifact, fork, source change, record, or clock movement");
        var unlocked = Session(fibrillation: true);
        unlocked.ConfigurePatientTimelineProgram(CreateProgram(new(AllowShock: false, PacingTimelineId: "unlocked"), [], Line("unlocked")));
        _ = unlocked.ApplyElectricalShock(Shock(unlocked), unlocked.PrepareSinusAfterShock(Profile(false)));
        unlocked.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
        unlocked.Advance(200_000_000);
        var actual = unlocked.ApplyElectricalShock(Shock(unlocked), unlocked.PrepareSinusAfterShock(Profile(false)));
        Check.That(unlocked.ShockAllowed && actual.Outcome is not (ElectricalConversionOutcome.DuplicateDelivery or
            ElectricalConversionOutcome.TimelineDisabled) && unlocked.ShockArtifacts.Count == 1,
            "a denied delivery does not consume a sequence number before a later permitted physical delivery");
    }

    private static void FailedActualShockForksFromCurrentStateAndPreservesHistory()
    {
        var branch = Line("failure", [new("branch cooling", 1_000_000_000, 2_000_000_000,
            new() { TemperatureDeciCelsius = 345 })]);
        var session = Session(fibrillation: true, twelveLead: true);
        var reference = Session(fibrillation: true, twelveLead: true);
        session.DiscardStartup();
        reference.DiscardStartup();
        session.ConfigurePatientTimelineProgram(CreateProgram(new(ShockFailureTimelineId: "failure"), [Warm()], branch));
        reference.ConfigurePatientTimelineProgram(CreateProgram(new(), [Warm()]));
        AdvanceTo(session, 2_000_000_000);
        AdvanceTo(reference, 2_000_000_000);
        ulong generation = session.TimelineGeneration;
        var history = session.Blocks.ToArray();
        Check.That(session.EvaluateElectricalShock(DefibrillationWaveformKind.BiphasicTruncatedExponential,
            DefibrillationMode.ManualAsynchronous, 150).Outcome == ElectricalConversionOutcome.EnergyTooLow &&
            session.CurrentTimelineId == "main" && session.TimelineGeneration == generation,
            "checking shock eligibility never forks a timeline");
        var delivery = Shock(session, energyJoules: 150);
        var result = session.ApplyElectricalShock(delivery, session.PrepareSinusAfterShock(Profile(false)));
        _ = reference.ApplyElectricalShock(Shock(reference, energyJoules: 150), reference.PrepareSinusAfterShock(Profile(false)));
        Check.That(result.Outcome == ElectricalConversionOutcome.EnergyTooLow &&
            session.CurrentTimelineId == "failure" && session.TimelineGeneration == generation + 1 &&
            session.TimelineOriginTimeNs == 2_000_000_000 && session.SimulationTimeNs == 2_000_000_000 &&
            session.ManualVitals.TemperatureDeciCelsius == 385 && session.Blocks.SequenceEqual(history),
            "an actual ineffective shock forks at release, inheriting the reached patient state and retained recording");
        Check.That(session.ApplyElectricalShock(delivery with { EnergyJoules = 200 },
            session.PrepareSinusAfterShock(Profile(false))).Outcome == ElectricalConversionOutcome.DuplicateDelivery &&
            session.TimelineGeneration == generation + 1 && session.ShockArtifacts.Count == 1,
            "reinterpreting the same physical delivery cannot create another fork or artifact");
        AdvanceTo(session, 2_800_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 385 && session.PatientTimelineTriggeredCount == 0,
            "the old unfinished ramp is frozen and branch events wait for T plus their relative delay");
        AdvanceTo(session, 4_000_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 365 && session.PatientTimelineTriggeredCount == 1,
            "the branch ramp starts from the fork value rather than the session's original value");
        AdvanceTo(session, 6_000_000_000);
        AdvanceTo(reference, 6_000_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 345 &&
            session.Samples(0, 0, 6_000_000_000).SequenceEqual(reference.Samples(0, 0, 6_000_000_000)),
            "forking a scalar-only branch preserves ECG phase, original time coordinates, and the identical delivered artifact");
        foreach (var lead in Enum.GetValues<EcgLead>())
        {
            Check.That(session.EcgSamples(lead, 0, 6_000_000_000).SequenceEqual(reference.EcgSamples(lead, 0, 6_000_000_000)),
                "the twelve-lead source retains the same phase and pre-fork history through a treatment branch");
        }
        var events = session.CapturePrintSnapshot().Events.Where(item => item.Kind == PreviewRecordEventKind.PatientTimelineEvent).ToArray();
        Check.That(events.Length == 2 && events[0].TimeNs == 0 && events[1].TimeNs == 3_000_000_000,
            "both pre-fork and branch timeline events remain recorded at their absolute session times");
    }


    private static void TreatmentForkCancelsPressureTargetsInRetainedReservoirHistory()
    {
        static LocalMonitorPreviewSession PressureSession() => new(
            PhysiologyIllustrationConfiguration.Default with { AbpTarget = new(12000, 8000) },
            MonitorDisplayConfiguration.Default(), enableMeasurements: true,
            electricalTherapy: Profile(false));
        var forked = PressureSession();
        var reference = PressureSession();
        forked.DiscardStartup();
        reference.DiscardStartup();
        var ramp = new PatientTimelineEvent("pressure ramp", 0, 8_000_000_000, new() { Abp = new(16000, 10000) });
        forked.ConfigurePatientTimelineProgram(CreateProgram(new(ShockFailureTimelineId: "hold"), [ramp], Line("hold")));
        reference.ConfigurePatientTimelineProgram(CreateProgram(new(ShockFailureTimelineId: "reference-hold"), [ramp,
            new("planned hold", 2_000_000_000, 0, new() { Abp = new(13000, 8500) })], Line("reference-hold")));
        AdvanceTo(forked, 2_000_000_000);
        AdvanceTo(reference, 2_000_000_000);
        var result = forked.ApplyElectricalShock(Shock(forked), forked.PrepareSinusAfterShock(Profile(false)));
        _ = reference.ApplyElectricalShock(Shock(reference), reference.PrepareSinusAfterShock(Profile(false)));
        Check.That(result.Outcome == ElectricalConversionOutcome.NotShockable && forked.CurrentTimelineId == "hold",
            "an actual ineffective shock can enter a hold branch without changing the intrinsic rhythm");
        AdvanceTo(forked, 6_000_000_000);
        AdvanceTo(reference, 6_000_000_000);
        var actual = forked.Samples(3, 4_000_000_000, 6_000_000_000).ToArray();
        var expected = reference.Samples(3, 4_000_000_000, 6_000_000_000).ToArray();
        Check.That(actual.Length > 0 && actual.Length == expected.Length &&
            actual.Zip(expected).All(pair => pair.First.TimeNs == pair.Second.TimeNs &&
                Math.Abs(pair.First.Value - pair.Second.Value) <= 0.05),
            "retained ejection tails must not continue the canceled line's future BP ramp after a treatment fork");
    }

    private static void SuccessfulShockForksAtFinalConversionAfterItsPause()
    {
        var session = Session(fibrillation: true, pauseMilliseconds: 600);
        session.DiscardStartup();
        session.ConfigurePatientTimelineProgram(CreateProgram(new(ShockSuccessTimelineId: "success", ShockFailureTimelineId: "failure"),
            [Warm(), new("superseded future", 7_000_000_000, 0, new() { TemperatureDeciCelsius = 450 })],
            Line("success", [new("recovery", 1_000_000_000, 2_000_000_000, new() { TemperatureDeciCelsius = 365 })]),
            Line("failure")));
        AdvanceTo(session, 2_000_000_000);
        ulong generation = session.TimelineGeneration;
        var history = session.Blocks.ToArray();
        var delivery = Shock(session);
        var sinus = session.PrepareSinusAfterShock(Profile(false));
        var result = session.ApplyElectricalShock(delivery, sinus);
        long effective = result.EffectiveSimTimeNs!.Value;
        Check.That(session.ApplyElectricalShock(delivery, sinus).Outcome == ElectricalConversionOutcome.DuplicateDelivery &&
            session.TimelineGeneration == generation,
            "replaying a successful physical delivery cannot schedule an extra success fork during the pause");
        Check.That(result.Outcome == ElectricalConversionOutcome.ConversionScheduled && effective > 2_200_000_000 &&
            session.CurrentTimelineId == "main" && session.TimelineGeneration == generation,
            "successful delivery preserves the authored post-shock pause and waits for actual sinus activation");
        AdvanceTo(session, effective - 200_000_000);
        Check.That(session.CurrentTimelineId == "main" && session.Configuration.CardiacActivity == CardiacActivity.Absent,
            "the temporary quiet source does not trigger the success branch early");
        AdvanceTo(session, effective);
        int reached = 365 + (int)(40 * effective / 4_000_000_000);
        Check.That(session.CurrentTimelineId == "success" && session.TimelineGeneration == generation + 1 &&
            session.TimelineOriginTimeNs == effective && session.ElectricalTherapy?.TemplateId == EcgElectricalTherapy.SinusTemplateId &&
            session.ManualVitals.TemperatureDeciCelsius == reached && session.Blocks.Take(history.Length).SequenceEqual(history),
            "the success fork inherits actual sinus state and current values at final conversion without clearing prior history");
        AdvanceTo(session, effective + 800_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == reached && session.PatientTimelineTriggeredCount == 0,
            "the success branch delay is relative to conversion, not delivery or session startup");
        AdvanceTo(session, 8_000_000_000);
        Check.That(session.CurrentTimelineId == "success" && session.ManualVitals.TemperatureDeciCelsius == 365 &&
            session.TimelineGeneration == generation + 1 && session.PatientTimelineCompleted,
            "the selected success branch replaces the original timeline's later events");
    }


    private static void FailureForkCancelsAnOlderPendingSuccessConversion()
    {
        var session = Session(fibrillation: true, pauseMilliseconds: 600);
        session.ConfigurePatientTimelineProgram(CreateProgram(new(ShockSuccessTimelineId: "success", ShockFailureTimelineId: "failure"),
            [Warm()], Line("success"), Line("failure")));
        AdvanceTo(session, 2_000_000_000);
        ulong generation = session.TimelineGeneration;
        var first = session.ApplyElectricalShock(Shock(session), session.PrepareSinusAfterShock(Profile(false)));
        Check.That(first.Outcome == ElectricalConversionOutcome.ConversionScheduled && session.PendingSourceTimeNs is not null,
            "the first successful shock prepares the old quiet and final sinus sources");
        AdvanceTo(session, 2_100_000_000);
        var second = session.ApplyElectricalShock(Shock(session, sequence: 2), session.PrepareSinusAfterShock(Profile(false)));
        Check.That(second.Outcome == ElectricalConversionOutcome.SourceChangePending && session.CurrentTimelineId == "failure" &&
            session.TimelineOriginTimeNs == 2_100_000_000 && session.TimelineGeneration == generation + 1 &&
            session.PendingSourceTimeNs is null,
            "a second ineffective physical shock enters its failure branch and replaces the older conversion queue");
        AdvanceTo(session, 4_000_000_000);
        Check.That(session.CurrentTimelineId == "failure" && session.TimelineGeneration == generation + 1 &&
            session.Configuration.ConductionPattern == AvConductionPattern.VentricularFibrillationCoarseIllustration &&
            session.ManualVitals.TemperatureDeciCelsius == 386 && session.PendingSourceTimeNs is null &&
            session.CapturePrintSnapshot().Events.Count(item => item.Kind == PreviewRecordEventKind.ShockDelivered) == 2,
            "neither the superseded quiet source nor its delayed sinus and branch intent can revive after the new fork");
    }

    private static void IncompatibleTreatmentBranchPreservesActualTreatmentAndCurrentTimeline()
    {
        var session = Session();
        var incompatible = Line("incompatible", [new("intrinsic rate target", 0, 0, new() { HeartRateBpm = 100 })]);
        session.ConfigurePatientTimelineProgram(CreateProgram(new(PacingTimelineId: incompatible.Id), [Warm()], incompatible));
        AdvanceTo(session, 2_000_000_000);
        ulong generation = session.TimelineGeneration;
        session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
        session.Advance(200_000_000);
        Check.That(session.ActivePacing == PacingIllustration.DualChamberDdd && session.CurrentTimelineId == "main" &&
            session.TimelineGeneration == generation && !string.IsNullOrEmpty(session.TimelineBranchError) &&
            session.TimelineBranchErrorTimelineName == incompatible.Name,
            "an intrinsic HR target incompatible with inherited pacing reports the branch error while retaining actual pacing and the current line");
        AdvanceTo(session, 4_000_000_000);
        Check.That(session.ActivePacing == PacingIllustration.DualChamberDdd && session.ManualVitals.TemperatureDeciCelsius == 405 &&
            !session.PatientTimelineStopped && session.CapturePrintSnapshot().Events.Count(item =>
                item.Kind == PreviewRecordEventKind.PatientTimelineBranchRejected) == 1,
            "a rejected branch neither pauses acquisition nor retries on every frame or cancels the original timeline");
    }

    private static void PacingForksOnlyOnActualStartAndCanForkAfterRestart()
    {
        var session = Session();
        session.ConfigurePatientTimelineProgram(CreateProgram(new(PacingTimelineId: "paced"),
            [new("old future", 10_000_000_000, 0, new() { TemperatureDeciCelsius = 450 })],
            Line("paced", [new("paced branch", 1_000_000_000, 0, new() { TemperatureDeciCelsius = 375 })],
                new(PacingTimelineId: "restarted")),
            Line("restarted", [new("restart baseline", 0, 0, new() { TemperatureDeciCelsius = 360 })],
                new(PacingTimelineId: "paced"))));
        AdvanceTo(session, 2_100_000_000);
        ulong generation = session.TimelineGeneration;
        long effective = session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
        Check.That(session.CurrentTimelineId == "main" && session.TimelineGeneration == generation,
            "preparing a future pacing source does not count as actual pacing activation");
        AdvanceTo(session, effective - 10_000_000, 17_000_000);
        Check.That(session.CurrentTimelineId == "main" && session.ActivePacing is null,
            "the initial timeline remains active before the scheduled output boundary");
        AdvanceTo(session, effective);
        Check.That(session.CurrentTimelineId == "paced" && session.TimelineGeneration == generation + 1 &&
            session.TimelineOriginTimeNs == effective && session.ActivePacing == PacingIllustration.DualChamberDdd,
            "first actual pacing activation enters its branch while retaining the pacing source");
        AdvanceTo(session, effective + 200_000_000);
        session.SchedulePacing(PacingIllustration.DualChamberDdd, new(110, 70));
        AdvanceTo(session, effective + 400_000_000);
        Check.That(session.CurrentTimelineId == "paced" && session.TimelineGeneration == generation + 1 &&
            session.TimelineOriginTimeNs == effective,
            "adjusting rate and current during pacing neither forks nor resets the branch clock");
        session.StopPacing();
        AdvanceTo(session, effective + 600_000_000);
        Check.That(session.ActivePacing is null && session.CurrentTimelineId == "paced" &&
            session.TimelineGeneration == generation + 1, "stopping pacing does not enter the pacing branch again");
        AdvanceTo(session, effective + 1_000_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 375 && !session.PatientTimelineStopped,
            "branch events continue through pacing adjustment and stopping");
        long restarted = session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
        session.Advance(200_000_000);
        Check.That(session.CurrentTimelineId == "restarted" && session.TimelineGeneration == generation + 2 &&
            session.TimelineOriginTimeNs == restarted,
            "a later off-to-on activation follows the current timeline's route exactly once");
        session.StopPacing();
        session.Advance(200_000_000);
        long cycleOrigin = session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
        session.Advance(200_000_000);
        Check.That(session.CurrentTimelineId == "paced" && session.TimelineGeneration == generation + 3 &&
            session.TimelineOriginTimeNs == cycleOrigin && session.ManualVitals.TemperatureDeciCelsius == 360 &&
            session.PatientTimelineTriggeredCount == 0,
            "a treatment-driven route cycle creates a fresh relative clock while inheriting the current state");
        AdvanceTo(session, cycleOrigin + 1_000_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 375 && session.PatientTimelineTriggeredCount == 1,
            "re-entered branch events run once at the new origin plus their delay");
    }

    private static void MissingTreatmentRoutesContinueTheCurrentTimeline()
    {
        PatientTimelineEvent[] events = [new("ongoing", 0, 4_000_000_000,
            new() { TemperatureDeciCelsius = 405, CvpCentiMmHg = 1400 })];
        foreach (bool pacing in new[] { false, true })
        {
            var session = Session(fibrillation: !pacing);
            session.ConfigurePatientTimelineProgram(CreateProgram(new(), events));
            AdvanceTo(session, 2_000_000_000);
            ulong generation = session.TimelineGeneration;
            if (pacing) { session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60)); }
            else
            {
                var result = session.ApplyElectricalShock(Shock(session), session.PrepareSinusAfterShock(Profile(false)));
                Check.That(result.Outcome == ElectricalConversionOutcome.ConversionScheduled,
                    "a missing success route retains the existing default conversion response");
            }
            AdvanceTo(session, 10_000_000_000);
            Check.That(session.CurrentTimelineId == "main" && session.TimelineGeneration == generation &&
                session.TimelineOriginTimeNs == 0 && !session.PatientTimelineStopped && session.PatientTimelineCompleted &&
                session.ManualVitals.TemperatureDeciCelsius == 405 && session.Measurements!.CvpMean.MeanCentiMmHg >= 1300,
                "without a selected route, treatment preserves the timeline and its unfinished actual pressure ramp");
        }
    }


    private static void NonCardiacTimelineEventsPreserveAnEarlierTreatmentResponse()
    {
        foreach (bool pacing in new[] { false, true })
        {
            var session = Session(fibrillation: !pacing);
            var oldCardiacSource = session.Configuration with { PlethShape = PlethIllustration.LowPerfusion };
            var program = CreateProgram(new(), [Warm(),
                new("pleth only", 2_000_000_000, 0, new()) { PlethTemplate = 1 }]);
            var sources = new Dictionary<string, IReadOnlyList<PatientTimelineSource?>>(StringComparer.Ordinal)
            { ["main"] = [null, new(oldCardiacSource, Profile(!pacing))] };
            session.ConfigurePatientTimelineProgram(program, sources);
            AdvanceTo(session, 1_000_000_000);
            if (pacing) { session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60)); }
            else { _ = session.ApplyElectricalShock(Shock(session), session.PrepareSinusAfterShock(Profile(false))); }
            AdvanceTo(session, 3_000_000_000);
            Check.That(session.Configuration.PlethShape == PlethIllustration.LowPerfusion && session.CurrentTimelineId == "main" &&
                session.PatientTimelineTriggeredCount == 2 && !session.PatientTimelineStopped,
                "the later noncardiac event still applies on the continuing timeline");
            Check.That(pacing ? session.ActivePacing == PacingIllustration.DualChamberDdd :
                session.Configuration.CardiacActivity == CardiacActivity.AtrialAndVentricular &&
                session.Configuration.ConductionPattern == AvConductionPattern.FixedPr &&
                session.ElectricalTherapy?.TemplateId == EcgElectricalTherapy.SinusTemplateId,
                "a preprepared PLETH-only event cannot restore its old cardiac template or undo actual pacing or conversion");
        }
    }

    private static void InvalidTimelineProgramsLeaveTheSessionUntouched()
    {
        var session = Session();
        var configuration = session.Configuration;
        ulong generation = session.TimelineGeneration;
        Reject(() => session.ConfigurePatientTimelineProgram(new("missing", [Line("main")])));
        Reject(() => session.ConfigurePatientTimelineProgram(new("main", [Line("main"), Line("main")])));
        Reject(() => session.ConfigurePatientTimelineProgram(CreateProgram(new(ShockFailureTimelineId: "missing"), [])));
        Reject(() => session.ConfigurePatientTimelineProgram(CreateProgram(new(PacingTimelineId: "invalid"), [],
            Line("invalid", [new("bad time", 1, 0, new() { TemperatureDeciCelsius = 375 })]))));
        Reject(() => session.ConfigurePatientTimelineProgram(CreateProgram(new(PacingTimelineId: "unprepared"), [],
            Line("unprepared", [new("missing source", 0, 0, new()) { EcgTemplateId = "test.unprepared" }]))));
        Check.That(session.Configuration == configuration && session.SimulationTimeNs == 0 &&
            session.CurrentTimelineId is null && session.TimelineGeneration == generation && session.PatientTimeline.Count == 0,
            "all invalid or unprepared programs reject before publishing an initial line or changing the patient");
        PatientTimelineEvent[] events = [new("owned event", 400_000_000, 0, new() { TemperatureDeciCelsius = 375 })];
        session.ConfigurePatientTimelineProgram(CreateProgram(new(), events));
        events[0] = new("caller replacement", 0, 0, new() { TemperatureDeciCelsius = 490 });
        AdvanceTo(session, 400_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 375,
            "successful setup owns immutable event snapshots rather than the caller's mutable arrays");
    }

    private static void TimelineProgramsPersistRoutesAndRetainLegacyFlatEvents()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-timeline-program-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var program = CreateProgram(new(AllowShock: true, AllowPacing: false,
                    ShockSuccessTimelineId: "success", ShockFailureTimelineId: "failure"), [Warm()],
                Line("success", [], new(AllowShock: false, AllowPacing: true, PacingTimelineId: "failure")),
                Line("failure", [new("delayed state", 1_000_000_000, 0, new() { TemperatureDeciCelsius = 350 })]));
            var preferences = new MonitorGeneratorPreferences(0, "sinus", 0, 0, "",
                new Dictionary<string, decimal?>(), new Dictionary<string, bool>(), new Dictionary<string, int>())
            { TimelineProgram = program };
            string path = Path.Combine(directory, "preferences.json");
            var store = new DisplayPreferenceStore(path);
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, Generator: preferences)), "named timeline program saves");
            var restored = store.Load(out bool rejected).Generator!;
            Check.That(!rejected && JsonSerializer.Serialize(restored.TimelineProgram) == JsonSerializer.Serialize(program),
                "names, initial line, independent treatment gates, branch routes and relative events survive round trip");
            PatientTimelineEvent[] legacy = [new("legacy event", 2_000_000_000, 0, new() { TemperatureDeciCelsius = 380 })];
            var document = JsonNode.Parse(File.ReadAllText(path))!;
            document["Version"] = 19;
            document["Generator"]!.AsObject().Remove("TimelineProgram");
            document["Generator"]!["PatientTimeline"] = JsonSerializer.SerializeToNode(legacy);
            File.WriteAllText(path, document.ToJsonString());
            restored = store.Load(out rejected).Generator!;
            Check.That(!rejected && JsonSerializer.Serialize(restored.PatientTimeline) == JsonSerializer.Serialize(legacy),
                "legacy flat timelines remain available for the editor's default-program migration");
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
        throw new InvalidOperationException("Invalid timeline program accepted.");
    }
}
