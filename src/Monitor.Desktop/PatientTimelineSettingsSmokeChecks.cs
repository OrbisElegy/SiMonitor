// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class PatientTimelineSettingsSmokeChecks
{
    internal static void VerifySession()
    {
        var window = new DesignPreviewWindow(startAudioOutput: false);
        window.Show();
        window.Pause();
        try
        {
            window.SelectPage(2);
            window.Settings.Tabs.SelectedIndex = 7;
            var timeline = window.Settings.Timeline;
            timeline.Restore([
                new("Fever", 0, 0, new() { TemperatureDeciCelsius = 390 }),
                new("Rhythm change", 1_000_000_000, 0, new()) { EcgTemplateId = "ecgTemplate.t023" },
                new("Recovery", 2_000_000_000, 0, new()) { EcgTemplateId = "ecgTemplate.t000" }
            ]);
            var previous = window.Session;
            window.RestartSettings();
            window.Pause();
            Require(ReferenceEquals(previous, window.Session) &&
                window.Settings.Status.Text == window.Localization.Get("validation.pressureTargetUnreachable"),
                "an unreachable target at a future rhythm is rejected before replacing the current session");
            window.Settings.AbpTargetEnabled.IsChecked = false;
            window.Settings.PaTargetEnabled.IsChecked = false;
            window.RestartSettings();
            window.Pause();
            Require(!ReferenceEquals(previous, window.Session) && window.Session.PatientTimeline.Count == 3,
                "restart installs the authored patient timeline in the new session: " + window.Settings.Status.Text);
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Require(!timeline.Status.IsVisible, "successful restart clears the draft indicator after control layout");
            Capture(window, "patient-timeline-zh-CN.png");
            window.Settings.Language.SelectedIndex = 0;
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Capture(window, "patient-timeline-en.png");
            var fever = timeline.Graph.NodeButtons.Single(button => ((Grid)button.Content!).Children.OfType<TextBlock>().First().Text == "Fever");
            fever.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            var scroll = (ScrollViewer)timeline.ActiveEvents.Content!;
            scroll.Offset = new Vector(0, 420);
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Capture(window, "patient-timeline-en-targets.png");
            scroll.Offset = default;
            timeline.BackToGraph.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Settings.Language.SelectedIndex = 1;
            window.Session.Advance(50_000_000);
            Require(window.Session.PatientTimelineTriggeredCount == 1 && window.Session.ManualVitals.TemperatureDeciCelsius == 390,
                "time-zero event reaches real session values");
            for (int step = 0; step < 20; step++) { window.Session.Advance(50_000_000); }
            Require(window.Session.ElectricalTherapy?.TemplateId == "ecgTemplate.t023", "scheduled rhythm switches in the live session");
            for (int step = 0; step < 20; step++) { window.Session.Advance(50_000_000); }
            Require(window.Session.ElectricalTherapy?.TemplateId == "ecgTemplate.t000" && window.Session.PatientTimelineCompleted,
                "the next rhythm event executes and completes the session timeline");
            window.Settings.Timeline.Editors[0].Targets["Temperature"].Values[0].Value = 37;
            Require(window.Session.ManualVitals.TemperatureDeciCelsius == 390 && timeline.Status.IsVisible,
                "editing a timeline leaves the current run unchanged and marks its draft");
            window.Settings.ApplyDelaySeconds.Value = 0;
            window.ApplySettings();
            window.Session.Advance(250_000_000);
            Require(window.Session.PatientTimelineStopped, "manual source changes explicitly stop the current timeline");
            window.RestartSettings();
            window.Pause();
            window.Session.Advance(50_000_000);
            Require(window.Session.ManualVitals.TemperatureDeciCelsius == 370 && !window.Session.PatientTimelineStopped,
                "restart replays the edited timeline from time zero");
            window.Settings.ManualVitals.TemperatureEnabled.IsChecked = true;
            window.Settings.ManualVitals.Temperature.Value = 36.8m;
            var generator = window.Settings.CaptureGenerator();
            window.Settings.ManualVitals.TemperatureEnabled.IsChecked = false;
            window.Settings.RestoreGenerator(generator);
            Require(window.Settings.ManualVitals.Read().TemperatureDeciCelsius == 368 &&
                window.Settings.Timeline.Read().Count == 3,
                "saved generator drafts preserve the manual baseline alongside timeline events");
            previous = window.Session;
            timeline = window.Settings.Timeline;
            timeline.Editors[0].StartSeconds.Value = .1m;
            window.RestartSettings();
            Require(ReferenceEquals(previous, window.Session), "invalid timeline draft cannot replace the current session");
        }
        finally { window.Close(); }
    }

    internal static void Verify()
    {
        var localization = new DesktopLocalization("zh-CN");
        var settings = new PatientTimelineSettings(localization) { Background = DesktopFluentStyle.Surface };
        var window = new Window { Width = 700, Height = 760, Content = settings };
        window.Show();
        try
        {
            Require(settings.Read().Count == 0 && !settings.Remove.IsEnabled, "empty timeline remains optional");
            settings.AddEvent();
            var first = settings.SelectedEditor!;
            Require(!first.Error.IsVisible && first.IsValid,
                "a new automatic node can represent a timed step before targets or treatment rules are added");
            window.UpdateLayout();
            Require(settings.InspectorVisible && !first.Targets["HeartRate"].Values[0].IsEffectivelyVisible,
                "adding a node opens its details, and unselected targets keep their values out of the way");
            first.Trigger.SelectedIndex = (int)PatientTimelineTrigger.Manual;
            Require(!first.StartSeconds.IsEffectivelyVisible && first.IsValid, "manual nodes do not ask for a wait time");
            first.Trigger.SelectedIndex = (int)PatientTimelineTrigger.AfterPrevious;
            first.StartSeconds.Value = 0;
            first.DurationSeconds.Value = 10;
            first.EventName.Text = "Tachycardia";
            first.Targets["HeartRate"].Enabled.IsChecked = true;
            Require(first.Targets["HeartRate"].Values[0].IsEffectivelyVisible, "selecting a target reveals its value");
            first.Targets["HeartRate"].Values[0].Value = 120;
            first.EcgTemplate.SelectedIndex = 24;
            first.Targets["Abp"].Enabled.IsChecked = true;
            first.Targets["Abp"].Values[0].Value = 90;
            first.Targets["Abp"].Values[1].Value = 60;
            var authored = settings.Read();
            Require(authored.Count == 1 && authored[0].StartTimeNs == 0 && authored[0].DurationNs == 10_000_000_000 &&
                authored[0].Targets.HeartRateBpm == 120 && authored[0].Targets.Abp?.SystolicCentiMmHg == 9000 &&
                authored[0].EcgTemplateId == "ecgTemplate.t023", "editor retains timing, targets, units and template identity");
            Require(ReferenceEquals(settings.SelectedEditor, first), "draft validation keeps the selected editor mounted");
            first.StartSeconds.Value = .1m;
            Require(!first.IsValid && first.Error.IsVisible, "sub-boundary times fail visibly");
            first.StartSeconds.Value = 0;
            first.Targets["Abp"].Values[1].Value = 89;
            Require(!first.IsValid && first.Error.Text == localization.Get("timeline.invalidPressure"), "invalid pulse pressure has an actionable error");
            first.Targets["Abp"].Values[1].Value = 60;
            first.Targets["Saturation"].Enabled.IsChecked = true;
            first.Targets["Saturation"].Values[0].Value = 92.5m;
            Require(settings.Read()[0].Targets.SaturationMilliPercent == 92500, "fractional saturation preserves milli-percent units");
            first.ChangeVentilation.IsChecked = true;
            first.InspiredOxygen.Value = 40;
            first.TidalVolume.Value = 350;
            Require(!first.Targets["Saturation"].Enabled.IsChecked!.Value &&
                settings.Read()[0].Ventilation is { InspiredOxygenMillionths: 400000, TidalVolumeMicrolitersBtps: 350000 },
                "ventilation replaces the mutually exclusive optical target using established units");
            first.Targets["Saturation"].Enabled.IsChecked = true;
            Require(first.ChangeVentilation.IsChecked == false && settings.Read()[0].Ventilation is null,
                "selecting a direct optical target clears the ventilation change");
            settings.AddEvent();
            var second = settings.SelectedEditor!;
            second.StartSeconds.Value = 5;
            second.DurationSeconds.Value = 0;
            second.Targets["Temperature"].Enabled.IsChecked = true;
            second.Targets["Temperature"].Values[0].Value = 38.4m;
            second.Targets["Nibp"].Enabled.IsChecked = true;
            second.Targets["Nibp"].Values[0].Value = 110;
            second.Targets["Nibp"].Values[1].Value = 70;
            second.Targets["Nibp"].Values[2].Value = 83;
            authored = settings.Read();
            var restored = new PatientTimelineSettings(localization);
            restored.Restore(authored);
            Require(restored.Read().SequenceEqual(authored), "timeline controls round-trip all authored events");
            localization.Select("en");
            Require(settings.Add.Content?.ToString() == "Add event" && settings.Read()[0].Name == "Tachycardia",
                "language changes translate controls and preserve user event names");
            Dispatcher.UIThread.RunJobs();
            Require(settings.Bounds.Width > 0 && first.Bounds.Width <= settings.Bounds.Width,
                "timeline editor fits the narrow settings viewport");
            settings.RemoveSelected();
            Require(settings.Read().Count == 1 && settings.Read()[0].Name == "Tachycardia", "delete removes only the selected event");
            settings.RemoveSelected();
            Require(settings.Read().Count == 0 && settings.SelectedEditor is null, "removing the final event restores the empty state");
            VerifyBranchDrafts(settings, localization);
        }
        finally { window.Close(); }
    }

    private static void VerifyBranchDrafts(PatientTimelineSettings settings, DesktopLocalization localization)
    {
        settings.AddBranch();
        string branchId = settings.SelectedTimelineId;
        settings.TimelineName.Text = "After shock";
        settings.AllowShock.IsChecked = false;
        settings.AddEvent();
        settings.SelectedEditor!.StartSeconds.Value = 2;
        settings.SelectedEditor.DurationSeconds.Value = 0;
        settings.SelectedEditor.Targets["Temperature"].Enabled.IsChecked = true;
        settings.SelectedEditor.Targets["Temperature"].Values[0].Value = 38;
        settings.SelectTimeline(settings.ReadProgram().Timelines[0].Id);
        settings.ShockSuccessTimeline.SelectedIndex = 2;
        settings.ShockFailureTimeline.SelectedIndex = 1;
        settings.PacingTimeline.SelectedIndex = 2;
        var program = settings.ReadProgram();
        Require(program.Timelines.Count == 2 && program.Timelines[0].Treatment.ShockSuccessTimelineId == branchId &&
            program.Timelines[0].Treatment.ShockFailureTimelineId == program.InitialTimelineId &&
            program.Timelines[0].Treatment.PacingTimelineId == branchId &&
            !program.Timelines[1].Treatment.AllowShock && program.Timelines[1].Events[0].StartTimeNs == 2_000_000_000,
            "named treatment branches retain stable identities, independent permissions and relative events");
        Require(settings.InspectorTitle == settings.TimelineName.Text && settings.ShockSuccessTimeline.IsEffectivelyVisible,
            "choosing a timeline shows its own name and initial treatment rules");
        foreach (string locale in new[] { "zh-CN", "en" })
        {
            localization.Select(locale);
            Dispatcher.UIThread.RunJobs();
            settings.UpdateLayout();
            using var image = new RenderTargetBitmap(new PixelSize((int)settings.Bounds.Width, (int)settings.Bounds.Height));
            image.Render(settings);
            Directory.CreateDirectory("artifacts");
            image.Save(Path.Combine("artifacts", "patient-timeline-branches-" + locale + ".png"), PngBitmapEncoderOptions.Default);
        }
        var restored = new PatientTimelineSettings(localization);
        restored.RestoreProgram(program);
        Require(restored.ReadProgram().Timelines[1].Id == branchId &&
            restored.ReadProgram().Timelines[1].Events.SequenceEqual(program.Timelines[1].Events),
            "branch authoring round-trips without changing IDs or relative times");
        settings.SelectTimeline(settings.ReadProgram().Timelines[1].Id);
        settings.RemoveBranch();
        Require(settings.ReadProgram().Timelines.Count == 2 && settings.Error.IsVisible,
            "referenced branches cannot be silently deleted");
        settings.SelectTimeline(settings.ReadProgram().Timelines[0].Id);
        settings.ShockSuccessTimeline.SelectedIndex = 0;
        settings.ShockFailureTimeline.SelectedIndex = 0;
        settings.PacingTimeline.SelectedIndex = 0;
        settings.SelectTimeline(settings.ReadProgram().Timelines[1].Id);
        settings.RemoveBranch();
        Require(settings.ReadProgram().Timelines.Count == 1 && !settings.RemoveTimeline.IsEnabled,
            "unreferenced branches can be removed and the initial timeline remains protected");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var root = (Control)window.Content!;
        using var image = new RenderTargetBitmap(new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height));
        image.Render(root);
        Directory.CreateDirectory("artifacts");
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }
}
