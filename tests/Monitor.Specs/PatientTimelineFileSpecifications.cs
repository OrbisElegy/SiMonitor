// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class PatientTimelineFileSpecifications
{
    private const string SeedHex = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";

    public static Specification[] All =>
    [
        new(nameof(TimelineFilesRoundTripCompleteGraphs), TimelineFilesRoundTripCompleteGraphs),
        new(nameof(TimelineFileSeedsPreserveLegacyCompatibility), TimelineFileSeedsPreserveLegacyCompatibility),
        new(nameof(TimelineFileSeedsRejectInvalidValues), TimelineFileSeedsRejectInvalidValues),
        new(nameof(TimelineFilesRejectInvalidAndOversizedDocuments), TimelineFilesRejectInvalidAndOversizedDocuments),
        new(nameof(TimelineLibrariesPersistSelectionAndDisabledState), TimelineLibrariesPersistSelectionAndDisabledState),
        new(nameof(DisablingTimelineStopsPreparedCurvesAndManualNodes), DisablingTimelineStopsPreparedCurvesAndManualNodes)
    ];

    private static PatientTimelineFile File(string? seedHex = null) => new("training", "电击后恢复", new("main",
    [
        new("main", "Main", [new("Wait for teacher", 0, 0, new()) { Trigger = PatientTimelineTrigger.Manual,
            Treatment = new(true, false, "recovery", "main", "recovery") }], new()),
        new("recovery", "Recovery", [new("Recovery rhythm", 1_000_000_000, 2_000_000_000,
            new() { HeartRateBpm = 90, SaturationMilliPercent = 97000 })
            { Trigger = PatientTimelineTrigger.AfterPrevious, EcgTemplateId = "ecgTemplate.t000" }], new(false, true))
    ])
    { SeedHex = seedHex });

    private static void TimelineFilesRoundTripCompleteGraphs()
    {
        var file = File(SeedHex);
        byte[] serialized = PatientTimelineFileFormat.Write(file);
        using var stream = new MemoryStream(serialized);
        var restored = PatientTimelineFileFormat.Read(stream);
        Check.That(JsonSerializer.Serialize(restored) == JsonSerializer.Serialize(file),
            "portable files preserve names, stable graph IDs, node triggers, targets, rhythm, treatment routes and authored seed");
        Check.That(restored.Program.SeedHex == SeedHex && file.Program.Snapshot().SeedHex == SeedHex,
            "export, import and owned program snapshots preserve the exact authored seed");
        Check.That(!Encoding.UTF8.GetString(serialized).Contains("ChangesTemplate", StringComparison.Ordinal),
            "portable files omit computed and runtime values");
    }

    private static void TimelineFileSeedsPreserveLegacyCompatibility()
    {
        var document = JsonNode.Parse(PatientTimelineFileFormat.Write(File(SeedHex)))!;
        Check.That(document["Version"]!.GetValue<int>() == 1,
            "an optional authoring seed retains the existing portable document version");
        (bool OmitSeed, string? Seed)[] legacyForms = [(true, null), (false, null), (false, "")];
        foreach (var legacyForm in legacyForms)
        {
            var legacy = document.DeepClone();
            var program = legacy["Timeline"]!["Program"]!.AsObject();
            if (legacyForm.OmitSeed) { program.Remove("SeedHex"); }
            else { program["SeedHex"] = legacyForm.Seed; }
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(legacy.ToJsonString()));
            var restored = PatientTimelineFileFormat.Read(input);
            Check.That(restored.Program.SeedHex is null && restored.Program.Snapshot().SeedHex is null,
                "missing, null and empty legacy seeds normalize to an unset seed before desktop migration");
        }
        var emptySeed = File("").Program;
        Check.That(emptySeed.Snapshot().SeedHex is null && emptySeed.SeedHex == "",
            "snapshot normalization preserves the input and does not generate a random seed in the model");
    }

    private static void TimelineFileSeedsRejectInvalidValues()
    {
        foreach (string seed in new[] { new string('0', 63), new string('0', 65), SeedHex.ToUpperInvariant(),
            new string('g', 64), new string(' ', 64), new string('０', 64) })
        {
            var file = File(seed);
            var document = JsonNode.Parse(PatientTimelineFileFormat.Write(File(SeedHex)))!;
            document["Timeline"]!["Program"]!["SeedHex"] = seed;
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));
            foreach (Action validate in new Action[] { () => file.Program.Validate(), () => file.Program.Snapshot(),
                () => PatientTimelineFileFormat.Write(file), () => PatientTimelineFileFormat.Read(input) })
            {
                bool rejected = false;
                try { validate(); }
                catch (ArgumentException error) { rejected = error.Message == "PatientTimeline.InvalidSeed"; }
                Check.That(rejected, "malformed seeds are rejected before model, export or import state is published");
            }
        }
    }

    private static void TimelineFilesRejectInvalidAndOversizedDocuments()
    {
        var original = JsonNode.Parse(PatientTimelineFileFormat.Write(File()))!;
        var invalidVersion = original.DeepClone();
        invalidVersion["Version"] = 2;
        var missingField = original.DeepClone();
        missingField.AsObject().Remove("Timeline");
        var brokenBranch = original.DeepClone();
        brokenBranch["Timeline"]!["Program"]!["Timelines"]![0]!["Events"]![0]!["Treatment"]!["ShockSuccessTimelineId"] = "missing";
        var unknown = original.DeepClone();
        unknown["RuntimeCursor"] = 1;
        foreach (string invalid in new[] { "null", "{", invalidVersion.ToJsonString(), missingField.ToJsonString(), brokenBranch.ToJsonString(), unknown.ToJsonString() })
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(invalid));
            bool rejected = false;
            try { _ = PatientTimelineFileFormat.Read(input); }
            catch (Exception error) when (error is ArgumentException or JsonException) { rejected = true; }
            Check.That(rejected, "invalid portable document is rejected before use");
        }
        byte[] bytes = new byte[PatientTimelineFileFormat.MaximumBytes + 1];
        using var oversized = new MemoryStream(bytes);
        bool tooLarge = false;
        try { _ = PatientTimelineFileFormat.Read(oversized); }
        catch (ArgumentException error) { tooLarge = error.Message == "PatientTimeline.FileTooLarge"; }
        Check.That(tooLarge, "portable reads are bounded before JSON parsing");
    }

    private static void TimelineLibrariesPersistSelectionAndDisabledState()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-timeline-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = File(SeedHex);
            var second = File() with { Id = "other", Name = "Second patient" };
            var library = new PatientTimelineLibrary(second.Id, false, [first, second]);
            var store = new DisplayPreferenceStore(Path.Combine(directory, "display.json"));
            Check.That(store.Save(new(MonitorDisplayConfiguration.Default(), 0) { PatientTimelines = library }), "library saves");
            var restored = store.Load(out bool rejected);
            Check.That(!rejected && JsonSerializer.Serialize(restored.PatientTimelines) == JsonSerializer.Serialize(library),
                "independent file contents, authored and legacy unset seeds, selection and disabled setting persist even without generator preferences");
            bool invalid = false;
            try { _ = (library with { Files = [first, first] }).Snapshot(); }
            catch (ArgumentException) { invalid = true; }
            Check.That(invalid, "duplicate library IDs cannot make file selection ambiguous");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void DisablingTimelineStopsPreparedCurvesAndManualNodes()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), enableMeasurements: true, opticalSaturationMilliPercent: 98000,
            manualVitals: new(new(120, 80, 93), 365),
            electricalTherapy: new(EcgElectricalTherapy.SinusTemplateId, new(false, 300, 150)) { PacingAllowed = true });
        session.ConfigurePatientTimelineProgram(new("main", [new("main", "Main",
        [
            new("Ramp", 0, 10_000_000_000, new() { HeartRateBpm = 100, TemperatureDeciCelsius = 390, SaturationMilliPercent = 90000 }),
            new("Later", 20_000_000_000, 0, new() { HeartRateBpm = 120, TemperatureDeciCelsius = 410 })
        ], new(false, false))]));
        for (int step = 0; step < 8; step++) { session.Advance(250_000_000); }
        long time = session.SimulationTimeNs;
        int temperature = session.ManualVitals.TemperatureDeciCelsius!.Value;
        int heartRate = session.Configuration.CardiacRateClock!.RatePerMinute.At(time);
        session.StopPatientTimeline();
        ulong generation = session.TimelineGeneration;
        session.StopPatientTimeline();
        Check.That(session.PatientTimelineStopped && session.CurrentTimelineTreatment is null && session.ShockAllowed && session.PacingAllowed &&
            session.TimelineGeneration == generation && session.SimulationTimeNs == time,
            "disabling is immediate and idempotent, releases node treatment restrictions and preserves the session clock");
        for (int step = 0; step < 100; step++) { session.Advance(250_000_000); }
        Check.That(session.PatientTimelineTriggeredCount == 1 && session.ManualVitals.TemperatureDeciCelsius == temperature &&
            session.Configuration.CardiacRateClock!.RatePerMinute.At(session.SimulationTimeNs) == heartRate,
            "prepared future targets and active curves cannot continue after disabling");

        var waiting = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default());
        waiting.ConfigurePatientTimelineProgram(new("main", [new("main", "Main",
            [new("Manual", 0, 0, new()) { Trigger = PatientTimelineTrigger.Manual }], new())]));
        generation = waiting.TimelineGeneration;
        waiting.StopPatientTimeline();
        Check.That(waiting.WaitingPatientTimelineEvent is null && !waiting.TriggerPatientTimelineManualEvent(generation, 0),
            "disabling clears manual gates and invalidates captured teacher actions");

        var zero = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), manualVitals: new(null, 365));
        zero.ConfigurePatientTimelineProgram(new("main", [new("main", "Main",
            [new("Not entered", 0, 0, new() { HeartRateBpm = 90, TemperatureDeciCelsius = 400 })], new())]));
        int originalRate = zero.Configuration.CardiacRateClock!.RatePerMinute.InitialValue;
        zero.StopPatientTimeline();
        zero.Advance(250_000_000);
        Check.That(zero.PatientTimelineTriggeredCount == 0 && zero.ManualVitals.TemperatureDeciCelsius == 365 &&
            zero.Configuration.CardiacRateClock!.RatePerMinute.At(zero.SimulationTimeNs) == originalRate,
            "disabling before the first tick does not accidentally apply prepared time-zero targets");
    }
}
