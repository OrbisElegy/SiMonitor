// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PatientTimelineSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(TimelineValidatesBeforeChangingSession), TimelineValidatesBeforeChangingSession),
        new(nameof(TimelineManualRampsRespectBoundariesAndInterruptions), TimelineManualRampsRespectBoundariesAndInterruptions),
        new(nameof(TimelineIsIndependentOfAdvancePartitionAndStartup), TimelineIsIndependentOfAdvancePartitionAndStartup),
        new(nameof(TimelineSwitchesTemplatesAndOxygenationModes), TimelineSwitchesTemplatesAndOxygenationModes),
        new(nameof(TimelinePreservesDelayedManualSourceAcrossTemplateEvents), TimelinePreservesDelayedManualSourceAcrossTemplateEvents),
        new(nameof(TimelinePacingPreservesReachedIndependentVitals), TimelinePacingPreservesReachedIndependentVitals),
        new(nameof(TimelineCompletesAfterInterruptedTargetsSettle), TimelineCompletesAfterInterruptedTargetsSettle),
        new(nameof(TimelinePreferencesRoundTripAndOlderFilesDefaultEmpty), TimelinePreferencesRoundTripAndOlderFilesDefaultEmpty)
    ];

    private static LocalMonitorPreviewSession Session(bool realtime = false) => new(
        PhysiologyIllustrationConfiguration.Default with { AbpTarget = new(12000, 8000), PaTarget = new(2500, 1000) },
        MonitorDisplayConfiguration.Default(), enableMeasurements: true,
        opticalSaturationMilliPercent: realtime ? null : 98000,
        realtimeOxygenation: realtime ? RealtimeOxygenationConfiguration.ReferenceAdult : null,
        manualVitals: new(new(120, 80, 93), 365), enableTwelveLead: true);

    private static void TimelineValidatesBeforeChangingSession()
    {
        var session = Session();
        var original = session.Configuration;
        Reject(() => session.ConfigurePatientTimeline([new("invalid", 1, 0, new() { HeartRateBpm = 90 })]));
        Reject(() => session.ConfigurePatientTimeline([new("empty", 0, 0, new())]));
        Reject(() => session.ConfigurePatientTimeline([new("conflicting oxygen", 0, 0, new() { SaturationMilliPercent = 90000 })
            { Ventilation = new(450000, 150000, 500000) }]));
        Reject(() => session.ConfigurePatientTimeline([new("missing template", 0, 0, new()) { EcgTemplateId = "missing" }]));
        Check.That(session.Configuration == original && session.PatientTimeline.Count == 0 && session.SimulationTimeNs == 0,
            "invalid complete drafts must preserve the running source and timeline");
    }

    private static void TimelineManualRampsRespectBoundariesAndInterruptions()
    {
        var session = Session();
        PatientTimelineEvent[] events =
        [
            new("warm", 1_000_000_000, 4_000_000_000, new() { TemperatureDeciCelsius = 405, Nibp = new(80, 40, 53) }),
            new("recover", 3_000_000_000, 2_000_000_000, new() { TemperatureDeciCelsius = 365 })
        ];
        session.ConfigurePatientTimeline(events);
        events[0] = new("external replacement", 0, 0, new() { TemperatureDeciCelsius = 100 });
        AdvanceTo(session, 1_000_000_000);
        Check.That(session.PatientTimelineTriggeredCount == 1 && session.ManualVitals.TemperatureDeciCelsius == 365,
            "event fires at its exact boundary and does not jump to the target at ramp start");
        AdvanceTo(session, 3_000_000_000);
        Check.That(session.PatientTimelineTriggeredCount == 2 && session.ManualVitals.TemperatureDeciCelsius == 385 &&
            session.ManualVitals.Nibp == new ManualNibp(100, 60, 73), "overlap begins from the value reached and unrelated targets continue");
        AdvanceTo(session, 5_000_000_000);
        Check.That(session.ManualVitals.TemperatureDeciCelsius == 365 && session.ManualVitals.Nibp == new ManualNibp(80, 40, 53) &&
            session.PatientTimelineCompleted, "ramp endpoints and completed status use session time");
        var records = session.CapturePrintSnapshot().Events.Where(item => item.Kind == PreviewRecordEventKind.PatientTimelineEvent).ToArray();
        Check.That(records.Length == 2 && records[0].TimeNs == 1_000_000_000 && records[1].TimeNs == 3_000_000_000,
            "timeline triggers are recorded exactly once at their authored boundaries");
        session.ScheduleSource(Session(), 0);
        session.Advance(200_000_000);
        Check.That(session.PatientTimelineStopped, "explicit source edits stop the authored timeline");
    }

    private static void TimelineIsIndependentOfAdvancePartitionAndStartup()
    {
        var first = Session();
        var second = Session();
        first.DiscardStartup();
        second.DiscardStartup();
        PatientTimelineEvent[] events =
        [
            new("deteriorate", 400_000_000, 3_000_000_000, new()
            { HeartRateBpm = 100, RespiratoryRatePerMinute = 22, TemperatureDeciCelsius = 385, SaturationMilliPercent = 90000 }),
            new("settle", 4_000_000_000, 0, new() { TemperatureDeciCelsius = 375 })
        ];
        first.ConfigurePatientTimeline(events);
        second.ConfigurePatientTimeline(events);
        AdvanceTo(first, 6_000_000_000, 200_000_000);
        AdvanceTo(second, 6_000_000_000, 17_000_000);
        Check.That(first.SimulationTimeNs == second.SimulationTimeNs && first.PatientTimelineTriggeredCount == 2 &&
            first.ManualVitals == second.ManualVitals, "startup is excluded and caller partition does not change event state");
        for (int channel = 0; channel < 7; channel++)
        {
            Check.That(first.Samples(channel, 0, 6_000_000_000).SequenceEqual(second.Samples(channel, 0, 6_000_000_000)),
                "all acquired source samples remain identical across advance partitions");
        }
        Check.That(first.Measurements == second.Measurements, "measurements share the continuously changing source samples");
    }

    private static void TimelineSwitchesTemplatesAndOxygenationModes()
    {
        var session = Session(realtime: true);
        PatientTimelineEvent[] events =
        [
            new("manual oxygen", 400_000_000, 600_000_000, new() { SaturationMilliPercent = 90000 }),
            new("ventilate", 1_200_000_000, 0, new()) { Ventilation = new(600000, 150000, 500000) },
            new("absent cardiac activity", 1_600_000_000, 0, new()) { EcgTemplateId = "test.absent" }
        ];
        var absent = session.Configuration with { CardiacActivity = CardiacActivity.Absent, VentricularMechanicalEnabled = false };
        session.ConfigurePatientTimeline(events, [null, null, new(absent, null)]);
        AdvanceTo(session, 400_000_000);
        Check.That(session.PatientTimelineTriggeredCount == 1, "direct oxygen target triggers while the model remains available");
        AdvanceTo(session, 1_200_000_000);
        Check.That(session.PatientTimelineTriggeredCount == 2 && session.Oxygenation is not null,
            "ventilation returns optical sampling to the model");
        AdvanceTo(session, 1_600_000_000);
        Check.That(session.Configuration.CardiacActivity == CardiacActivity.Absent && session.PatientTimelineTriggeredCount == 3,
            "template changes are discrete and use the same session clock");
        AdvanceTo(session, 2_000_000_000);
        Check.That(session.CapturePrintSnapshot().Events.Count(item => item.Kind == PreviewRecordEventKind.PatientTimelineEvent) == 3,
            "source swaps do not clear the timeline or duplicate its events");
    }


    private static void TimelinePreservesDelayedManualSourceAcrossTemplateEvents()
    {
        var session = Session();
        var absent = session.Configuration with { CardiacActivity = CardiacActivity.Absent, VentricularMechanicalEnabled = false };
        PatientTimelineEvent[] events =
        [
            new("temperature", 400_000_000, 1_000_000_000, new() { TemperatureDeciCelsius = 385 }),
            new("template", 800_000_000, 0, new()) { EcgTemplateId = "test.absent" },
            new("same-time event", 1_400_000_000, 0, new() { TemperatureDeciCelsius = 450 }),
            new("later event", 1_800_000_000, 0, new() { CvpCentiMmHg = 2000 })
        ];
        session.ConfigurePatientTimeline(events, [null, new(absent, null), null, null]);
        AdvanceTo(session, 200_000_000);
        long effective = session.ScheduleSource(Session(), 1_200_000_000, "manual source");
        AdvanceTo(session, 800_000_000);
        Check.That(!session.PatientTimelineStopped && session.PatientTimelineTriggeredCount == 2 &&
            session.Configuration.CardiacActivity == CardiacActivity.Absent && session.PendingSourceTimeNs == effective &&
            session.ManualVitals.TemperatureDeciCelsius == 373,
            "a timeline template preserves the pending manual source and current independent values");
        AdvanceTo(session, effective);
        Check.That(session.PatientTimelineStopped && session.PatientTimelineTriggeredCount == 2 &&
            session.ManualVitals.TemperatureDeciCelsius == 365,
            "a timeline event tied to the manual activation boundary cannot change values or run first at a partition endpoint");
        AdvanceTo(session, 2_200_000_000);
        Check.That(session.PatientTimelineStopped && session.PatientTimelineTriggeredCount == 2 &&
            session.Configuration.CardiacActivity == CardiacActivity.AtrialAndVentricular &&
            session.Configuration.CvpBaselineCentiMmHgCurve is null && session.PendingSourceTimeNs is null,
            "the delayed manual source activates and excludes all later timeline changes");
        var records = session.CapturePrintSnapshot().Events.Where(item => item.Kind == PreviewRecordEventKind.PatientTimelineEvent).ToArray();
        Check.That(records.Length == 2, "suppressed same-time and later timeline events produce no records");
    }

    private static void TimelinePacingPreservesReachedIndependentVitals()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        { AbpTarget = new(12000, 8000), PaTarget = new(2500, 1000) };
        var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true,
            opticalSaturationMilliPercent: 98000, manualVitals: new(new(120, 80, 93), 365),
            electricalTherapy: new("ecgTemplate.t000", ElectricalConversionSettings.Default) { PacingAllowed = true });
        session.ConfigurePatientTimeline([new("change", 0, 4_000_000_000, new()
        {
            HeartRateBpm = 105,
            RespiratoryRatePerMinute = 24,
            CvpCentiMmHg = 1400,
            EtCo2MmHg = 20,
            SaturationMilliPercent = 90000,
            TemperatureDeciCelsius = 405,
            Nibp = new(80, 40, 53)
        })]);
        AdvanceTo(session, 2_000_000_000);
        var manual = session.ManualVitals;
        session.SchedulePacing(PacingIllustration.DualChamberDdd, new(90, 60));
        AdvanceTo(session, 2_200_000_000);
        Check.That(session.PatientTimelineStopped && session.ActivePacing == PacingIllustration.DualChamberDdd &&
            session.ManualVitals == manual && session.Configuration.CvpBaselineCentiMmHgCurve!.At(10_000_000_000) == 1000 &&
            session.Configuration.Co2EndExpiratoryCentiMmHgCurve!.At(10_000_000_000) == 3000 &&
            session.Configuration.RespiratoryRateClock!.RatePerMinute.At(10_000_000_000) == 20,
            "pacing preserves the reached independent targets and freezes their future timeline changes");
        AdvanceTo(session, 10_000_000_000);
        Check.That(session.ManualVitals == manual &&
            Math.Abs(session.Measurements!.SpO2.SaturationMilliPercent!.Value - 94000) <= 500,
            "manual values and measured optical saturation remain at intervention-time targets");
        session.StopPacing();
        AdvanceTo(session, 10_200_000_000);
        Check.That(session.Configuration.CardiacRateClock!.RatePerMinute.At(20_000_000_000) == 90,
            "stopping pacing restores the intrinsic rate reached at intervention, without reviving its canceled future ramp");
    }


    private static void TimelineCompletesAfterInterruptedTargetsSettle()
    {
        var manual = Session();
        manual.ConfigurePatientTimeline([
            new("long ramp", 0, 10_000_000_000, new() { TemperatureDeciCelsius = 405 }),
            new("cancel ramp", 2_000_000_000, 0, new() { TemperatureDeciCelsius = 365 })]);
        AdvanceTo(manual, 2_000_000_000);
        Check.That(manual.PatientTimelineCompleted && manual.ManualVitals.TemperatureDeciCelsius == 365,
            "an interrupted ramp does not keep the timeline running past the replacement's endpoint");
        var oxygenation = Session(realtime: true);
        oxygenation.ConfigurePatientTimeline([
            new("long manual saturation", 0, 10_000_000_000, new() { SaturationMilliPercent = 80000 }),
            new("return to model", 2_000_000_000, 0, new()) { Ventilation = new(450000, 150000, 400000) }]);
        AdvanceTo(oxygenation, 2_000_000_000);
        Check.That(oxygenation.PatientTimelineCompleted,
            "switching back to ventilation ends the superseded manual saturation ramp");
    }

    private static void TimelinePreferencesRoundTripAndOlderFilesDefaultEmpty()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-timeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preferences.json");
            var store = new DisplayPreferenceStore(path);
            var generator = new MonitorGeneratorPreferences(0, "sinus", 0, 0, new string('0', 63) + "1",
                new Dictionary<string, decimal?>(), new Dictionary<string, bool>(), new Dictionary<string, int>())
            {
                PatientTimeline = [new("state", 0, 0, new() { Nibp = new(110, 70, 83), Abp = new(11000, 7000) }),
                    new("ventilation", 2_000_000_000, 0, new()) { Ventilation = new(450000, 150000, 400000) }]
            };
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0, Generator: generator)), "timeline authoring data saves");
            var restored = store.Load(out bool rejected);
            Check.That(!rejected && JsonSerializer.Serialize(restored.Generator!.PatientTimeline) == JsonSerializer.Serialize(generator.PatientTimeline),
                "timeline targets, units and ventilation survive serialization");
            var data = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            data["Version"] = 18;
            data["Generator"]!.AsObject().Remove("PatientTimeline");
            File.WriteAllText(path, data.ToJsonString());
            restored = store.Load(out rejected);
            Check.That(!rejected && restored.Generator!.PatientTimeline.Count == 0, "old preferences retain an empty timeline");
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
        throw new InvalidOperationException("Invalid timeline accepted.");
    }
}
