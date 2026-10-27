// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Localization;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal static class PatientTimelineEntrySmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-timeline-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string preferences = Path.Combine(directory, "display.json");
        var window = new DesignPreviewWindow(preferences, startAudioOutput: false);
        window.Show();
        window.Pause();
        try
        {
            window.SelectPage(2);
            window.Settings.Tabs.SelectedIndex = 7;
            window.Settings.AbpTargetEnabled.IsChecked = false;
            window.Settings.PaTargetEnabled.IsChecked = false;
            var timeline = window.Settings.Timeline;
            timeline.RestoreProgram(ExampleProgram());
            timeline.SelectTimeline("main");
            var entry = timeline.EntryState;
            entry.Targets["Temperature"].Enabled.IsChecked = true;
            entry.Targets["Temperature"].Values[0].Value = 39;
            entry.Targets["Saturation"].Enabled.IsChecked = true;
            entry.Targets["Saturation"].Values[0].Value = 92;
            entry.EcgTemplate.SelectedIndex = 24;
            var authored = timeline.ReadProgram();
            Require(authored.Timelines[0].InitialState is { EcgTemplateId: "ecgTemplate.t023", Targets.TemperatureDeciCelsius: 390 } &&
                authored.Timelines[0].Events.Count == 3,
                "the entry inspector authors initial vitals and rhythm separately from scheduled events");
            DesktopViewportSmokeChecks.Layout(window, 1440, 900);
            Require(entry.IsEffectivelyVisible && new Control[] { entry.EventName, entry.Trigger, entry.StartSeconds,
                entry.DurationSeconds, entry.Treatment }.All(control => !control.GetVisualAncestors().Contains(entry) || !control.IsEffectivelyVisible),
                "entry state offers patient settings without event timing or duplicate treatment controls");
            window.RestartSettings();
            window.Pause();
            Require(window.Session.SimulationTimeNs == 0 && window.Session.PatientTimelineTriggeredCount == 0 &&
                window.Session.PatientTimeline.Count == 3 && window.Session.ManualVitals.TemperatureDeciCelsius == 390 &&
                window.Session.ElectricalTherapy?.TemplateId == "ecgTemplate.t023",
                "restart installs the initial patient state at time zero before any event or sample advances");

            entry.Targets["Temperature"].Values[0].Value = 38.5m;
            Require(window.Session.ManualVitals.TemperatureDeciCelsius == 390 && timeline.Status.IsVisible,
                "editing entry values marks a draft while the running patient retains its applied state");
            using var stream = new MemoryStream(PatientTimelineFileFormat.Write(timeline.ReadFile()));
            timeline.Import(PatientTimelineFileFormat.Read(stream));
            Require(timeline.EntryState.Targets["Temperature"].Values[0].Value == 38.5m &&
                timeline.EntryState.EcgTemplate.SelectedIndex == 24 && timeline.Read().Count == 3,
                "export and import preserve entry state without injecting an extra event");
            string main = JsonSerializer.Serialize(timeline.ReadProgram().Timelines[0].InitialState);
            timeline.SelectTimeline("recovery");
            timeline.EntryState.Targets["Temperature"].Enabled.IsChecked = true;
            timeline.EntryState.Targets["Temperature"].Values[0].Value = 37;
            timeline.SelectTimeline("main");
            Require(JsonSerializer.Serialize(timeline.ReadProgram().Timelines[0].InitialState) == main &&
                timeline.ReadProgram().Timelines[1].InitialState?.Targets.TemperatureDeciCelsius == 370,
                "main and branch entry settings are independent");
            CaptureViewports(window);
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
                var timeline = restored.Settings.Timeline;
                Require(timeline.Files.ItemCount == 2 && timeline.ReadProgram().Timelines[0].InitialState?.Targets.TemperatureDeciCelsius == 385 &&
                    timeline.ReadProgram().Timelines[1].InitialState?.Targets.TemperatureDeciCelsius == 370 &&
                    restored.Session.ManualVitals.TemperatureDeciCelsius == 385 && restored.Session.ElectricalTherapy?.TemplateId == "ecgTemplate.t023",
                    "reopening restores the selected file's initial state and applies it to the new patient");
            }
            finally { restored.Close(); }
            Require(saved.AsSpan().SequenceEqual(File.ReadAllBytes(preferences)),
                "viewing persisted entry state does not rewrite preferences");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void CaptureViewports(DesignPreviewWindow window)
    {
        var timeline = window.Settings.Timeline;
        foreach (string locale in new[] { "zh-CN", "en", "zh-TW" })
        {
            window.Settings.Language.SelectedIndex = BuiltInLocalizations.Supported.Select(item => item.Code).ToList().IndexOf(locale);
            DesktopViewportSmokeChecks.Layout(window, 1440, 900);
            timeline.SelectTimeline("main");
            timeline.Graph.ResetView();
            DesktopViewportSmokeChecks.Layout(window, 1440, 900);
            Capture(window, "patient-timeline-entry-wide-" + locale + ".png");
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            timeline.SelectTimeline("main");
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            var entry = timeline.EntryState;
            var scroll = entry.GetVisualAncestors().OfType<ScrollViewer>().First();
            Require(entry.IsEffectivelyVisible && !timeline.Graph.IsVisible && scroll.Viewport.Height >= 180 && timeline.Status.IsEffectivelyVisible,
                "a narrow full window keeps edited-state feedback visible and gives the entry inspector usable space in each language");
            Capture(window, "patient-timeline-entry-narrow-" + locale + ".png");
            timeline.BackToGraph.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Require(timeline.Files.IsEffectivelyVisible && timeline.FileActions.IsEffectivelyVisible &&
                timeline.TimelineEnabled.IsEffectivelyVisible && timeline.Graph.IsVisible && timeline.Graph.ZoomControls.IsEffectivelyVisible,
                "returning to a narrow graph restores file, execution and view controls");
            Capture(window, "patient-timeline-toolbar-narrow-" + locale + ".png");
        }
    }

    private static PatientTimelineProgram ExampleProgram() => new("main",
    [
        new("main", "Main timeline",
        [
            new("Treatment window", 15_000_000_000, 0, new())
            { Trigger = PatientTimelineTrigger.AfterPrevious, Treatment = new(true, true, "recovery", "persistent", "recovery") },
            new("Reassess patient", 0, 0, new()) { Trigger = PatientTimelineTrigger.Manual },
            new("Observation", 20_000_000_000, 10_000_000_000, new() { TemperatureDeciCelsius = 375 })
            { Trigger = PatientTimelineTrigger.AfterPrevious }
        ], new()),
        new("recovery", "Recovery", [new("Improving oxygenation", 10_000_000_000, 10_000_000_000,
            new() { SaturationMilliPercent = 98000 }) { Trigger = PatientTimelineTrigger.AfterPrevious }], new()),
        new("persistent", "Persistent rhythm", [new("Instructor decision", 0, 0, new())
            { Trigger = PatientTimelineTrigger.Manual }], new())
    ]);

    private static void Capture(Window window, string name)
    {
        var root = (Control)window.Content!;
        using var image = new RenderTargetBitmap(new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height));
        image.Render(root);
        Directory.CreateDirectory("artifacts");
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Patient timeline entry: " + message); }
    }
}
