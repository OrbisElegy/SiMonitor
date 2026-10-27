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

internal static class PatientTimelineInitialStateSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(InitialPatientStateAppliesBeforeTheFirstTickAndSuppliesRampBaselines), InitialPatientStateAppliesBeforeTheFirstTickAndSuppliesRampBaselines),
        new(nameof(InitialPatientTemplatesAndVentilationApplyWithoutAnExtraNode), InitialPatientTemplatesAndVentilationApplyWithoutAnExtraNode),
        new(nameof(InitialPatientStatePreservesManualIndexesAndSurvivesImmediateDisable), InitialPatientStatePreservesManualIndexesAndSurvivesImmediateDisable),
        new(nameof(TreatmentBranchAppliesItsInitialStateAtTheActualTreatmentTime), TreatmentBranchAppliesItsInitialStateAtTheActualTreatmentTime),
        new(nameof(InvalidInitialPatientStatesRejectWithoutPublishing), InvalidInitialPatientStatesRejectWithoutPublishing),
        new(nameof(InitialPatientStatesRoundTripAndOlderFilesInheritThePatient), InitialPatientStatesRoundTripAndOlderFilesInheritThePatient)
    ];

    private static EcgElectricalTherapyProfile Profile() => new(EcgElectricalTherapy.SinusTemplateId, new(false, 300, 150))
    { PacingAllowed = true };

    private static LocalMonitorPreviewSession Session() => new(PhysiologyIllustrationConfiguration.Default,
        MonitorDisplayConfiguration.Default(), enableMeasurements: true, opticalSaturationMilliPercent: 98000,
        electricalTherapy: Profile(), enableTwelveLead: true);

    private static PatientTimelineProgram Program(PatientTimelineEvent initial, params PatientTimelineEvent[] events) =>
        new("main", [new("main", "Main", events, new()) { InitialState = initial }]);

    private static void InitialPatientStateAppliesBeforeTheFirstTickAndSuppliesRampBaselines()
    {
        foreach (bool discardStartup in new[] { false, true })
        {
            var session = Session();
            if (discardStartup) { session.DiscardStartup(); }
            long offset = discardStartup ? LocalMonitorPreviewSession.StartupDiscardNs : 0;
            var initial = new PatientTimelineEvent("initial", 0, 0, new()
            {
                HeartRateBpm = 90,
                RespiratoryRatePerMinute = 20,
                SaturationMilliPercent = 92000,
                EtCo2MmHg = 30,
                Abp = new(11000, 7000),
                Pa = new(2400, 900),
                CvpCentiMmHg = 600,
                Nibp = new(110, 70, 83),
                TemperatureDeciCelsius = 380
            });
            session.ConfigurePatientTimelineProgram(Program(initial,
                new PatientTimelineEvent("cooling", 1_000_000_000, 2_000_000_000, new()
                { TemperatureDeciCelsius = 360, Nibp = new(130, 80, 97), HeartRateBpm = 100 })));
            Check.That(session.SimulationTimeNs == 0 && session.PatientTimelineTriggeredCount == 0 && session.PatientTimeline.Count == 1 &&
                session.ManualVitals.TemperatureDeciCelsius == 380 && session.ManualVitals.Nibp == new ManualNibp(110, 70, 83) &&
                session.Configuration.CardiacRateClock!.RatePerMinute.At(offset) == 90 &&
                session.Configuration.RespiratoryRateClock!.RatePerMinute.At(offset) == 20 &&
                session.Configuration.AbpSystolicCentiMmHgCurve!.At(offset) == 11000 &&
                session.Configuration.PaDiastolicCentiMmHgCurve!.At(offset) == 900 &&
                session.Configuration.CvpBaselineCentiMmHgCurve!.At(offset) == 600 &&
                session.Configuration.Co2EndExpiratoryCentiMmHgCurve!.At(offset) == 3000,
                "entry immediately supplies authored physiology and previously absent manual/pressure baselines without advancing time or consuming a node");
            Check.That(session.CapturePrintSnapshot().Events.All(item => item.Kind != PreviewRecordEventKind.PatientTimelineEvent),
                "entry state does not invent an additional timeline event in the record");
            AdvanceTo(session, 2_000_000_000);
            Check.That(session.ManualVitals.TemperatureDeciCelsius == 370 && session.ManualVitals.Nibp == new ManualNibp(120, 75, 90) &&
                session.Configuration.CardiacRateClock!.RatePerMinute.At(offset + session.SimulationTimeNs) == 95,
                "future gradual targets start from the authored entry state");
            AdvanceTo(session, 12_000_000_000);
            Check.That(session.PatientTimelineTriggeredCount == 1 &&
                Math.Abs(session.Measurements!.SpO2.SaturationMilliPercent!.Value - 92000) <= 500,
                "entry oxygen target reaches the same optical measurement chain and stays active after later nodes");
        }
    }

    private static void InitialPatientTemplatesAndVentilationApplyWithoutAnExtraNode()
    {
        var session = Session();
        session.DiscardStartup();
        var initial = new PatientTimelineEvent("initial", 0, 0, new() { TemperatureDeciCelsius = 390 })
        {
            EcgTemplateId = "test.absent",
            RespirationTemplate = 1,
            PlethTemplate = 1,
            CapnogramTemplate = 1,
            Ventilation = new(600000, 150000, 500000)
        };
        var configuration = PhysiologyIllustrationConfiguration.Default with
        {
            CardiacActivity = CardiacActivity.Absent,
            VentricularMechanicalEnabled = false,
            RespiratoryActivity = RespiratoryActivity.Absent,
            PlethShape = PlethIllustration.LowPerfusion,
            CapnogramIllustration = CapnogramIllustration.Obstructive
        };
        session.ConfigurePatientTimelineProgram(Program(initial), oxygenationModel: RealtimeOxygenationConfiguration.ReferenceAdult,
            initialSources: new Dictionary<string, PatientTimelineSource?> { ["main"] = new(configuration, null) });
        Check.That(session.Configuration.CardiacActivity == CardiacActivity.Absent &&
            session.Configuration.RespiratoryActivity == RespiratoryActivity.Absent && session.Configuration.PlethShape == configuration.PlethShape &&
            session.Configuration.CapnogramIllustration == configuration.CapnogramIllustration && session.ElectricalTherapy is null &&
            session.Oxygenation is not null && session.ManualVitals.TemperatureDeciCelsius == 390 &&
            session.PatientTimeline.Count == 0 && session.PatientTimelineTriggeredCount == 0,
            "entry rhythm, waveform selections and ventilation are current before any timed event or simulation tick");
        AdvanceTo(session, 1_000_000_000);
        Check.That(session.PatientTimelineCompleted && session.ManualVitals.TemperatureDeciCelsius == 390 && session.Oxygenation is not null,
            "a state-only timeline keeps its entry physiology without requiring a synthetic scheduled event");
    }

    private static void InitialPatientStatePreservesManualIndexesAndSurvivesImmediateDisable()
    {
        var initial = new PatientTimelineEvent("initial", 0, 0, new() { HeartRateBpm = 90, TemperatureDeciCelsius = 380 });
        var manual = Session();
        manual.ConfigurePatientTimelineProgram(Program(initial,
            new PatientTimelineEvent("wait", 0, 0, new() { TemperatureDeciCelsius = 390 }) { Trigger = PatientTimelineTrigger.Manual }));
        Check.That(manual.WaitingPatientTimelineEventIndex == 0 && manual.PatientTimelineTriggeredCount == 0 &&
            manual.TriggerPatientTimelineManualEvent(manual.TimelineGeneration, 0) && manual.ManualVitals.TemperatureDeciCelsius == 390,
            "the first visible manual node retains index zero after entry state application");
        var stopped = Session();
        stopped.ConfigurePatientTimelineProgram(Program(initial,
            new PatientTimelineEvent("unentered zero-time event", 0, 0, new() { HeartRateBpm = 100, TemperatureDeciCelsius = 400 })));
        stopped.StopPatientTimeline();
        stopped.Advance(200_000_000);
        Check.That(stopped.PatientTimelineTriggeredCount == 0 && stopped.ManualVitals.TemperatureDeciCelsius == 380 &&
            stopped.Configuration.CardiacRateClock!.RatePerMinute.At(stopped.SimulationTimeNs) == 90,
            "immediate disable retains the already applied entry state while suppressing an unentered time-zero event");
    }

    private static void TreatmentBranchAppliesItsInitialStateAtTheActualTreatmentTime()
    {
        var session = Session();
        session.DiscardStartup();
        session.ConfigurePatientTimelineProgram(new("main",
        [
            new("main", "Main", [], new(ShockFailureTimelineId: "branch"))
            { InitialState = new("initial", 0, 0, new() { HeartRateBpm = 90, TemperatureDeciCelsius = 380 }) },
            new("branch", "Branch", [new("warm", 1_000_000_000, 2_000_000_000, new() { TemperatureDeciCelsius = 400 })], new())
            { InitialState = new("initial", 0, 0, new() { TemperatureDeciCelsius = 360 }) }
        ]));
        AdvanceTo(session, 2_100_000_000);
        var samples = session.Samples(0, 0, session.SimulationTimeNs).ToArray();
        var delivery = new DeliveredElectricalShock(1, session.SimulationTimeNs,
            DefibrillationWaveformKind.BiphasicTruncatedExponential, DefibrillationMode.ManualAsynchronous, 200);
        _ = session.ApplyElectricalShock(delivery, session.PrepareSinusAfterShock(Profile()));
        Check.That(session.CurrentTimelineId == "branch" && session.TimelineOriginTimeNs == 2_100_000_000 &&
            session.ManualVitals.TemperatureDeciCelsius == 360 && session.PatientTimelineTriggeredCount == 0 &&
            session.Configuration.CardiacRateClock!.RatePerMinute.At(LocalMonitorPreviewSession.StartupDiscardNs + session.SimulationTimeNs) == 90 &&
            session.Samples(0, 0, session.SimulationTimeNs).SequenceEqual(samples),
            "branch entry overrides only authored fields at the actual treatment instant and preserves prior acquisition");
        AdvanceTo(session, 4_100_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 380 && session.PatientTimelineTriggeredCount == 1,
            "branch events keep T+ timing and ramp from the branch entry state");
    }

    private static void InvalidInitialPatientStatesRejectWithoutPublishing()
    {
        var session = Session();
        var configuration = session.Configuration;
        var initial = new PatientTimelineEvent("initial", 0, 0, new() { TemperatureDeciCelsius = 380 });
        foreach (var invalid in new[]
        {
            initial with { StartTimeNs = 200_000_000 }, initial with { DurationNs = 200_000_000 },
            initial with { Trigger = PatientTimelineTrigger.Manual }, initial with { Treatment = new() },
            initial with { Targets = new() { TemperatureDeciCelsius = 501 } },
            initial with { EcgTemplateId = "test.missing" },
            initial with { Ventilation = new(600000, 150000, 500000) }
        })
        {
            bool rejected = false;
            try { session.ConfigurePatientTimelineProgram(Program(invalid)); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected && session.Configuration == configuration && session.ManualVitals == ManualVitalSigns.Empty &&
                session.CurrentTimelineId is null && session.SimulationTimeNs == 0 && session.CapturePrintSnapshot().Events.Count == 0,
                "invalid entry timing, content, templates or missing oxygen model leave the patient and history untouched");
        }
        bool futureRejected = false;
        try
        {
            session.ConfigurePatientTimelineProgram(Program(initial,
                new PatientTimelineEvent("incompatible future source", 1_000_000_000, 0, new() { HeartRateBpm = 100 }) { EcgTemplateId = "test.absent" }),
                new Dictionary<string, IReadOnlyList<PatientTimelineSource?>>
                { ["main"] = [new(PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent }, null)] });
        }
        catch (ArgumentException) { futureRejected = true; }
        Check.That(futureRejected && session.ManualVitals == ManualVitalSigns.Empty && session.CurrentTimelineId is null &&
            session.Configuration == configuration, "a failed following event cannot partially publish a valid initial state");
    }

    private static void InitialPatientStatesRoundTripAndOlderFilesInheritThePatient()
    {
        var initial = new PatientTimelineEvent("initial", 0, 0, new() { TemperatureDeciCelsius = 380, SaturationMilliPercent = 92000 })
        { EcgTemplateId = "ecgTemplate.t000", RespirationTemplate = 2, PlethTemplate = 1 };
        var file = new PatientTimelineFile("patient", "Patient", Program(initial));
        byte[] bytes = PatientTimelineFileFormat.Write(file);
        using var stream = new MemoryStream(bytes);
        var restored = PatientTimelineFileFormat.Read(stream);
        Check.That(JsonSerializer.Serialize(file) == JsonSerializer.Serialize(restored),
            "portable files preserve entry targets and waveform selections independently of timed nodes");
        var document = JsonNode.Parse(bytes)!;
        document["Timeline"]!["Program"]!["Timelines"]![0]!.AsObject().Remove("InitialState");
        using var legacyBytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString()));
        var legacy = PatientTimelineFileFormat.Read(legacyBytes);
        var session = Session();
        var configuration = session.Configuration;
        session.ConfigurePatientTimelineProgram(legacy.Program);
        Check.That(legacy.Program.Timelines[0].InitialState is null && session.ManualVitals == ManualVitalSigns.Empty &&
            session.Configuration == configuration, "files without entry state preserve the inherited patient baseline");
    }

    private static void AdvanceTo(LocalMonitorPreviewSession session, long timeNs)
    {
        while (session.SimulationTimeNs < timeNs) { session.Advance(Math.Min(200_000_000, timeNs - session.SimulationTimeNs)); }
    }
}
