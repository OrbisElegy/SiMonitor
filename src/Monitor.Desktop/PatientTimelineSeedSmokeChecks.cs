// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PatientTimelineSeedSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-timeline-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string legacyPreferences = Path.Combine(directory, "legacy.json");
            byte[] legacyBytes = File.ReadAllBytes("tests/Monitor.Specs/Fixtures/coarse-vf-seed1.json");
            File.WriteAllBytes(legacyPreferences, legacyBytes);
            var legacyWindow = new DesignPreviewWindow(legacyPreferences, startAudioOutput: false);
            string? migratedSeed;
            string migratedFileId;
            try
            {
                legacyWindow.Pause();
                var timeline = legacyWindow.Settings.Timeline;
                migratedSeed = timeline.ReadProgram().SeedHex;
                migratedFileId = timeline.SelectedFileId;
                Require(!legacyWindow.PreferenceNotice.IsVisible && DesignPreviewSettings.IsSeedFormatValid(migratedSeed) &&
                    timeline.RequiresMigrationSave,
                    "opening legacy generator preferences assigns a valid timeline seed and marks the migration for saving");
            }
            finally { legacyWindow.Close(); }
            byte[] migratedBytes = File.ReadAllBytes(legacyPreferences);
            using (var saved = JsonDocument.Parse(migratedBytes))
            {
                string? savedSeed = saved.RootElement.GetProperty("PatientTimelines").GetProperty("Files").EnumerateArray()
                    .Single(file => file.GetProperty("Id").GetString() == migratedFileId)
                    .GetProperty("Program").GetProperty("SeedHex").GetString();
                Require(!legacyBytes.AsSpan().SequenceEqual(migratedBytes) && savedSeed == migratedSeed,
                    "closing an upgraded legacy document persists its generated timeline seed");
            }
            var reopenedLegacy = new DesignPreviewWindow(legacyPreferences, startAudioOutput: false);
            try
            {
                reopenedLegacy.Pause();
                var timeline = reopenedLegacy.Settings.Timeline;
                Require(!reopenedLegacy.PreferenceNotice.IsVisible && timeline.ReadProgram().SeedHex == migratedSeed &&
                    !timeline.RequiresMigrationSave,
                    "reopening the migrated document reuses its saved seed without requesting another migration");
            }
            finally { reopenedLegacy.Close(); }
            Require(migratedBytes.AsSpan().SequenceEqual(File.ReadAllBytes(legacyPreferences)),
                "opening and closing a migrated document without edits preserves its bytes");

            string preferences = Path.Combine(directory, "display.json");
            string globalSeed = new('1', 64), initialSeed = new('a', 64), editedSeed = new('b', 64);
            var initialWindow = new DesignPreviewWindow(preferences, startAudioOutput: false);
            initialWindow.Show();
            initialWindow.Pause();
            string? createdSeed;
            try
            {
                createdSeed = initialWindow.Settings.Timeline.Seed.Text;
                Require(DesignPreviewSettings.IsSeedFormatValid(createdSeed),
                    "a new default timeline receives a valid random seed before any edit");
            }
            finally { initialWindow.Close(); }
            var window = new DesignPreviewWindow(preferences, startAudioOutput: false);
            window.Show();
            window.Pause();
            try
            {
                window.SelectPage(2);
                var settings = window.Settings;
                Require(settings.Timeline.Seed.Text == createdSeed,
                    "closing and reopening an unedited default timeline preserves its generated seed");
                settings.Tabs.SelectedIndex = 7;
                settings.RateSeed.Text = globalSeed;
                settings.CardiacRateEnabled.IsChecked = true;
                settings.HeartRate.Value = 75;
                settings.RateVariation.Value = 5;
                settings.EtCo2Variation.Value = 2;
                settings.AbpVariation.Value = 2;
                Restart(window);
                VerifySources(window, globalSeed, globalSeed);
                var timeline = settings.Timeline;
                timeline.RestoreProgram(Program(initialSeed));
                timeline.TimelineEnabled.IsChecked = true;
                DesktopViewportSmokeChecks.Layout(window);

                Restart(window);
                VerifySources(window, initialSeed, globalSeed);
                long[] originalBeats = CardiacTimes(window.Session);
                Require(originalBeats.Length > 50, "the applied source produces a complete seeded cardiac sequence");
                var original = window.Session;
                Restart(window);
                Require(!ReferenceEquals(original, window.Session) && originalBeats.SequenceEqual(CardiacTimes(window.Session)),
                    "restarting the same timeline seed reproduces its cardiac event times");

                var running = window.Session;
                timeline.Seed.Text = editedSeed;
                Dispatcher.UIThread.RunJobs();
                Require(ReferenceEquals(running, window.Session) && timeline.Status.IsVisible &&
                    timeline.ReadProgram().SeedHex == editedSeed && !timeline.SeedError.IsVisible,
                    "editing a valid timeline seed changes the draft without replacing the running patient");
                VerifySources(window, initialSeed, globalSeed);
                Restart(window);
                VerifySources(window, editedSeed, globalSeed);
                Require(!ReferenceEquals(running, window.Session) && !originalBeats.SequenceEqual(CardiacTimes(window.Session)),
                    "restarting applies the edited seed and changes actual cardiac event times");

                foreach (string invalid in new[] { "", editedSeed.ToUpperInvariant(), editedSeed[..63] })
                {
                    running = window.Session;
                    var timer = window.ActiveTimer;
                    timeline.Seed.Text = invalid;
                    Require(timeline.SeedError.IsVisible && timeline.SeedDetails.IsExpanded &&
                        !string.IsNullOrWhiteSpace(timeline.SeedError.Text),
                        "empty, uppercase and short timeline seeds synchronously reveal an actionable error");
                    Dispatcher.UIThread.RunJobs();
                    Restart(window);
                    Require(ReferenceEquals(running, window.Session) && ReferenceEquals(timer, window.ActiveTimer),
                        "an invalid enabled timeline seed cannot replace the patient or its timer");
                    VerifySources(window, editedSeed, globalSeed);
                }

                running = window.Session;
                timeline.TimelineEnabled.IsChecked = false;
                Restart(window);
                Require(!ReferenceEquals(running, window.Session) && window.Session.CurrentTimelineId is null &&
                    timeline.Seed.Text == editedSeed[..63] && timeline.SeedError.IsVisible,
                    "disabling a timeline permits an ordinary restart while preserving its invalid draft");
                VerifySources(window, globalSeed, globalSeed);

                timeline.Seed.Text = editedSeed;
                timeline.TimelineEnabled.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Require(!timeline.SeedError.IsVisible, "repairing a timeline seed clears its validation error");
                Restart(window);
                VerifySources(window, editedSeed, globalSeed);

                settings.CardiacRateEnabled.IsChecked = false;
                timeline.RestoreProgram(Program(editedSeed, "ecgTemplate.t006"));
                Restart(window);
                Require(AtrialFibrillationReference.IsPattern(window.Session.Configuration.ConductionPattern) &&
                    window.Session.Configuration.RhythmSchedule?.SeedHex == editedSeed && settings.RateSeed.Text == globalSeed,
                    "an authored initial coarse AF template uses the timeline seed for its irregular rhythm");

                settings.CardiacRateEnabled.IsChecked = true;
                timeline.RestoreProgram(Program(editedSeed));
                Restart(window);
                VerifySources(window, editedSeed, globalSeed);
            }
            finally { window.Close(); }

            Require(File.Exists(preferences), "closing the editor saves its selected timeline and generator settings");
            var restored = new DesignPreviewWindow(preferences, startAudioOutput: false);
            restored.Show();
            restored.Pause();
            try
            {
                Require(restored.Settings.Timeline.TimelineEnabled.IsChecked == true &&
                    restored.Settings.Timeline.Seed.Text == editedSeed && restored.Settings.Timeline.ReadProgram().SeedHex == editedSeed &&
                    restored.Session.CurrentTimelineId == "main",
                    "reopening restores the edited timeline seed and starts the selected enabled timeline");
                VerifySources(restored, editedSeed, globalSeed);
            }
            finally { restored.Close(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static PatientTimelineProgram Program(string seedHex, string? ecgTemplateId = null) => new("main",
    [
        new("main", "Seeded patient", [new("Temperature", 1_000_000_000, 0,
            new() { TemperatureDeciCelsius = 390 })], new())
        {
            InitialState = ecgTemplateId is null ? null :
                new("Initial rhythm", 0, 0, new()) { EcgTemplateId = ecgTemplateId }
        }
    ])
    { SeedHex = seedHex };

    private static void Restart(DesignPreviewWindow window)
    {
        window.RestartSettings();
        window.Pause();
        Dispatcher.UIThread.RunJobs();
    }

    private static long[] CardiacTimes(LocalMonitorPreviewSession session) =>
        RegularPhysiologyTimeline.Start(session.Configuration.ResolvePlan()).AdvanceBefore(60_000_000_000, 2000)
            .Where(item => item.Kind == PhysiologyCycleEventKind.VentricularElectrical)
            .Select(item => item.SimTimeNs).ToArray();

    private static void VerifySources(DesignPreviewWindow window, string appliedSeed, string globalSeed)
    {
        var configuration = window.Session.Configuration;
        Require(configuration.SeededRate?.SeedHex == appliedSeed && configuration.PressureVariation?.SeedHex == appliedSeed,
            "cardiac and pressure variation use the selected seed");
        var co2 = configuration.SeededCo2;
        Require(co2 is not null && co2.PreparedState ==
            new SeededExpirationPressure(co2.TargetMmHg, co2.AmplitudeCentiMmHg, appliedSeed).PreparedState,
            "CO2 variation prepares the deterministic stream from the selected seed");
        Require(window.Settings.RateSeed.Text == globalSeed,
            "timeline seed application preserves the ordinary generator seed draft");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Patient timeline seed: " + message); }
    }
}
