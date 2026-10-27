// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal static class PatientTimelineFileSmokeChecks
{
    internal static void VerifyFiles()
    {
        var localization = new DesktopLocalization("zh-CN");
        VerifySeedMigration(localization);
        var settings = new PatientTimelineSettings(localization) { Background = DesktopFluentStyle.Surface };
        var window = new Window { Width = 1000, Height = 760, Content = settings };
        window.Show();
        try
        {
            string initialSeed = settings.Seed.Text!;
            Require(DesignPreviewSettings.IsSeedFormatValid(initialSeed) && settings.ReadProgram().SeedHex == initialSeed,
                "the initial timeline file receives a valid generated seed");
            settings.RestoreProgram(Program("Original", 380) with { SeedHex = initialSeed });
            string originalId = settings.SelectedFileId;
            settings.SelectTimeline("branch");
            Require(settings.Files.SelectedIndex == 0 && settings.Files.ItemCount == 1,
                "selecting a treatment branch does not change the patient file selector");
            settings.CreateFile();
            Require(settings.Files.ItemCount == 2 && settings.ReadProgram().Timelines.Count == 1 && settings.Read().Count == 0,
                "new files contain an independent empty patient graph");
            Require(DesignPreviewSettings.IsSeedFormatValid(settings.Seed.Text) && settings.Seed.Text != initialSeed,
                "each new timeline file receives its own generated seed");
            settings.RestoreProgram(Program("Second", 390));
            string editedSeed = new('a', 64);
            settings.Seed.Text = editedSeed;
            Dispatcher.UIThread.RunJobs();
            Require(settings.Status.IsVisible && settings.ReadProgram().SeedHex == editedSeed,
                "a manually edited seed is part of the unapplied file draft");
            string secondId = settings.SelectedFileId;
            settings.Files.SelectedIndex = 0;
            Require(settings.SelectedFileId == originalId && settings.Read()[0].Name == "Original" && settings.ReadProgram().Timelines.Count == 2 &&
                settings.Seed.Text == initialSeed,
                "switching files restores the complete graph and its treatment branches");
            settings.Files.SelectedIndex = 1;
            Require(settings.SelectedFileId == secondId && settings.Read()[0].Name == "Second" && settings.Seed.Text == editedSeed,
                "other files retain their graph and edited seed");

            using var bytes = new MemoryStream(PatientTimelineFileFormat.Write(settings.ReadFile()));
            settings.Import(PatientTimelineFileFormat.Read(bytes));
            Require(settings.Files.ItemCount == 3 && settings.SelectedFileId != secondId && settings.Read()[0].Name == "Second" &&
                settings.Seed.Text == editedSeed,
                "importing an existing ID creates a separate file instead of overwriting another draft");
            settings.Seed.Text = new string('A', 64);
            DesktopViewportSmokeChecks.Layout(window, 1000, 760);
            Require(settings.SeedError.IsEffectivelyVisible && settings.SeedDetails.IsExpanded &&
                AutomationProperties.GetHelpText(settings.Seed) == localization.Get("timeline.invalidSeed"),
                "an invalid edited seed exposes an actionable inline and accessible error");
            int seedSelection = settings.Files.SelectedIndex;
            settings.Files.SelectedIndex = 0;
            Require(settings.Files.SelectedIndex == seedSelection && settings.FileStatus.IsVisible &&
                settings.ReadLastValidLibrary().Files.Single(file => file.Id == settings.SelectedFileId).Program.SeedHex == editedSeed,
                "an invalid seed blocks file switching and preserves the last valid saved seed");
            settings.Seed.Text = editedSeed;
            Dispatcher.UIThread.RunJobs();
            Require(!settings.SeedError.IsVisible, "correcting the seed clears its inline error");
            string before = JsonSerializer.Serialize(settings.ReadLibrary());
            bool rejected = false;
            try
            {
                settings.Import(new("bad", "Unknown rhythm", new("main", [new("main", "Main",
                    [new("Unknown", 0, 0, new()) { EcgTemplateId = "ecgTemplate.unknown" }], new())])));
            }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && JsonSerializer.Serialize(settings.ReadLibrary()) == before,
                "unknown imported templates are rejected without changing current contents or file selection");
            settings.Editors[0].StartSeconds.Value = .1m;
            int selected = settings.Files.SelectedIndex;
            settings.Files.SelectedIndex = 0;
            Require(settings.Files.SelectedIndex == selected && settings.Editors[0].StartSeconds.Value == .1m && settings.FileStatus.IsVisible,
                "an invalid draft remains intact and visibly blocks file switching");
            settings.Editors[0].StartSeconds.Value = 0;
            settings.TimelineEnabled.IsChecked = false;
            var library = settings.ReadLibrary();
            var restored = new PatientTimelineSettings(localization);
            restored.RestoreLibrary(library);
            Require(JsonSerializer.Serialize(restored.ReadLibrary()) == JsonSerializer.Serialize(library),
                "library restoration preserves all documents, selected file and enabled state");
            settings.RemoveSelectedFile();
            Require(settings.Files.ItemCount == 2 && settings.SelectedFileId == originalId,
                "removing a file selects another complete graph without affecting it");
            foreach (string locale in new[] { "zh-CN", "en", "zh-TW" })
            {
                localization.Select(locale);
                DesktopViewportSmokeChecks.Layout(window, 1000, 760);
                Require(settings.Files.IsEffectivelyVisible && settings.FileActions.IsEffectivelyVisible &&
                    settings.TimelineEnabled.IsEffectivelyVisible, "file controls remain accessible in every language");
                settings.FileActions.Flyout!.ShowAt(settings.FileActions);
                DesktopViewportSmokeChecks.Layout(window, 1000, 760);
                Require(settings.FileActions.Flyout.IsOpen && settings.NewFile.IsEffectivelyVisible &&
                    settings.ImportFile.IsEffectivelyVisible && settings.ExportFile.IsEffectivelyVisible && settings.RemoveFile.IsEffectivelyVisible,
                    "the file menu exposes all document actions without occupying permanent toolbar space");
                if (TopLevel.GetTopLevel(settings.NewFile) is { } popup)
                { Capture(popup, "patient-timeline-file-menu-" + locale + ".png"); }
                settings.FileActions.Flyout.Hide();
                Capture(window, "patient-timeline-files-" + locale + ".png");
            }
        }
        finally { window.Close(); }
    }

    private static void VerifySeedMigration(DesktopLocalization localization)
    {
        string preservedSeed = new('c', 64);
        var legacy = new PatientTimelineLibrary("missing", true,
        [
            new("missing", "Missing seed", Program("Missing", 380)),
            new("empty", "Empty seed", Program("Empty", 380) with { SeedHex = "" }),
            new("authored", "Authored seed", Program("Authored", 380) with { SeedHex = preservedSeed })
        ]);
        var editor = new PatientTimelineSettings(localization);
        editor.RestoreLibrary(legacy);
        var upgraded = editor.ReadLibrary();
        Require(editor.RequiresMigrationSave && upgraded.Files.All(file => DesignPreviewSettings.IsSeedFormatValid(file.Program.SeedHex)) &&
            upgraded.Files[0].Program.SeedHex != upgraded.Files[1].Program.SeedHex &&
            upgraded.Files[2].Program.SeedHex == preservedSeed,
            "loading an old library fills each missing seed once and preserves already authored seeds");
        editor.Files.SelectedIndex = 1;
        editor.Files.SelectedIndex = 0;
        Require(editor.Seed.Text == upgraded.Files[0].Program.SeedHex,
            "switching migrated files does not generate another seed");
        var reopened = new PatientTimelineSettings(localization);
        reopened.RestoreLibrary(upgraded);
        Require(!reopened.RequiresMigrationSave && JsonSerializer.Serialize(reopened.ReadLibrary()) == JsonSerializer.Serialize(upgraded),
            "loading an upgraded library keeps every seed stable and requests no further migration save");
        int changes = 0;
        reopened.DraftChanged += () => changes++;
        using var bytes = new MemoryStream(PatientTimelineFileFormat.Write(new("legacy", "Imported legacy", Program("Imported", 380))));
        reopened.Import(PatientTimelineFileFormat.Read(bytes));
        string? importedSeed = reopened.ReadFile().Program.SeedHex;
        Require(DesignPreviewSettings.IsSeedFormatValid(importedSeed) && changes > 0,
            "importing an old portable file fills its seed and schedules the normal library save");
        using var exported = new MemoryStream(PatientTimelineFileFormat.Write(reopened.ReadFile()));
        Require(PatientTimelineFileFormat.Read(exported).Program.SeedHex == importedSeed,
            "exporting a migrated portable file retains its generated seed");
    }

    internal static void VerifyExecution()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-timeline-desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string preferences = Path.Combine(directory, "display.json");
        var window = new DesignPreviewWindow(preferences, startAudioOutput: false);
        window.Show();
        window.Pause();
        try
        {
            var editor = window.Settings.Timeline;
            // This fixture exercises an already upgraded file; legacy migration has separate coverage.
            editor.RestoreProgram(Program("Immediate fever", 390) with { SeedHex = new string('0', 63) + "1" });
            window.RestartSettings();
            window.Pause();
            window.Session.Advance(250_000_000);
            Require(window.Session.ManualVitals.TemperatureDeciCelsius == 390, "selected file starts with the patient session");
            var session = window.Session;
            editor.TimelineEnabled.IsChecked = false;
            for (int step = 0; step < 10; step++) { session.Advance(250_000_000); }
            Require(ReferenceEquals(session, window.Session) && session.PatientTimelineStopped && session.PatientTimelineTriggeredCount == 1 &&
                session.ManualVitals.TemperatureDeciCelsius == 390 && !editor.TriggerManual.IsVisible,
                "disable immediately stops future events while preserving the session and current values");
            editor.TimelineEnabled.IsChecked = true;
            session.Advance(250_000_000);
            Require(session.PatientTimelineStopped && editor.Status.IsVisible, "enabling does not resume or catch up the old timeline");
            window.RestartSettings();
            window.Pause();
            for (int step = 0; step < 10; step++) { window.Session.Advance(250_000_000); }
            Require(!ReferenceEquals(session, window.Session) && !window.Session.PatientTimelineStopped &&
                window.Session.PatientTimelineTriggeredCount == 2 && window.Session.ManualVitals.TemperatureDeciCelsius == 370,
                "restart replays the enabled selected file from the beginning");
            session = window.Session;
            editor.Import(new("another", "Another patient", Program("Other file", 380)));
            Require(ReferenceEquals(session, window.Session) && session.PatientTimeline[0].Name == "Immediate fever",
                "import and file selection do not replace the active patient");
            editor.TimelineEnabled.IsChecked = false;
            editor.Editors[0].StartSeconds.Value = .1m;
            window.RestartSettings();
            window.Pause();
            Require(window.Session.CurrentTimelineId is null && window.Session.PatientTimeline.Count == 0 &&
                editor.Editors[0].StartSeconds.Value == .1m,
                "a disabled timeline installs no events or rules, and an invalid draft cannot block the normal patient restart");
        }
        finally { window.Close(); }
        try
        {
            byte[] saved = File.ReadAllBytes(preferences);
            var restored = new DesignPreviewWindow(preferences, startAudioOutput: false);
            restored.Show();
            restored.Pause();
            try
            {
                Require(restored.Settings.Timeline.Files.ItemCount == 2 && restored.Settings.Timeline.SelectedFileName == "Another patient" &&
                    restored.Settings.Timeline.TimelineEnabled.IsChecked == false && restored.Session.CurrentTimelineId is null &&
                    !restored.Settings.Timeline.RequiresMigrationSave,
                    "reopening remembers the files, selected document and disabled state without starting it");
            }
            finally { restored.Close(); }
            Require(saved.AsSpan().SequenceEqual(File.ReadAllBytes(preferences)),
                "opening and closing a saved file without edits does not rewrite preferences");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static PatientTimelineProgram Program(string name, int temperature) => new("main",
    [
        new("main", "Main", [new(name, 0, 0, new() { TemperatureDeciCelsius = temperature }),
            new("Later", 1_000_000_000, 0, new() { TemperatureDeciCelsius = 370 })], new(true, true, "branch")),
        new("branch", "After treatment", [], new())
    ]);

    private static void Capture(Window window, string name) => Capture((Control)window.Content!, name);

    private static void Capture(Control root, string name)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height));
        image.Render(root);
        Directory.CreateDirectory("artifacts");
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Patient timeline files: " + message); }
    }
}
