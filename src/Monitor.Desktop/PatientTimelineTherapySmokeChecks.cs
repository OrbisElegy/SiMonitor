// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Presentation;
using Monitor.Domain.Therapy;

namespace Monitor.Desktop;

internal static class PatientTimelineTherapySmokeChecks
{
    internal static void VerifyManualEntry()
    {
        var window = new DesignPreviewWindow(startAudioOutput: false);
        window.Show();
        try
        {
            window.Settings.Timeline.RestoreProgram(new("main",
            [
                new("main", "Teacher gate",
                [
                    new("Teacher enters", 0, 0, new() { TemperatureDeciCelsius = 380 })
                    { Trigger = PatientTimelineTrigger.Manual, Treatment = new(true, false) },
                    new("One second later", 1_000_000_000, 0, new() { TemperatureDeciCelsius = 390 })
                    { Trigger = PatientTimelineTrigger.AfterPrevious }
                ], new(false, false))
            ]));
            window.RestartSettings();
            window.Pause();
            Require(window.Settings.Timeline.TriggerManual.IsVisible && !window.Session.ShockAllowed,
                "the current manual gate is visible and its future treatment rules do not apply early");
            long time = window.Session.SimulationTimeNs;
            window.Settings.Timeline.TriggerManual.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.Session.SimulationTimeNs == time && window.Session.ManualVitals.TemperatureDeciCelsius == 380 &&
                window.Session.ShockAllowed && !window.Settings.Timeline.TriggerManual.IsVisible,
                "a teacher can enter the waiting node while paused without advancing the patient clock");
            window.Settings.Timeline.TriggerManual.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.Session.PatientTimelineTriggeredCount == 1,
                "a stale click cannot enter the following automatic node");
            for (int step = 0; step < 5; step++) { window.Session.Advance(200_000_000); }
            Require(window.Session.ManualVitals.TemperatureDeciCelsius == 390,
                "the next automatic node waits from the manual node's actual entry time");
        }
        finally { window.Close(); }
    }

    internal static void Verify()
    {
        long safetyTimeNs = 0;
        var window = new DesignPreviewWindow(startAudioOutput: false, safetyClock: () => safetyTimeNs);
        window.Show();
        try
        {
            window.Settings.PacingPermissions.Restore(new Dictionary<string, bool> { ["ecgTemplate.t000"] = true });
            window.Settings.Timeline.RestoreProgram(new("main",
                [new("main", "No treatment", [], new(false, false))]));
            window.RestartSettings();
            window.SelectPage(0);
            window.Pulse(window.ActiveTimer, 50_000_000);
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            skin.ShowTherapy.IsChecked = true;
            Require(!window.Session.ShockAllowed && !window.Session.PacingAllowed &&
                !skin.Defibrillator.Charge.IsEnabled && !skin.ApplyPacing.IsEnabled,
                "active timeline permissions disable electrical therapy controls independently of ECG permissions");
            window.RunDefibrillatorCommand(DefibrillatorCommand.Charge);
            skin.ApplyPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.Defibrillator.State.Energy == EnergyState.Idle && window.Session.ActivePacing is null,
                "direct control commands cannot bypass the active timeline permissions");
            Require(skin.Defibrillator.Status.Text == window.Localization.Get("timeline.shockDenied") &&
                skin.PacingStatus.Text == window.Localization.Get("timeline.pacingDenied"),
                "disabled therapy explains the timeline restriction rather than blaming the ECG template");

            window.Settings.Defibrillator.ChargeSeconds.Value = 1;
            window.Settings.Timeline.RestoreProgram(new("main",
            [
                new("main", "Initial", [], new(ShockFailureTimelineId: "after")),
                new("after", "After treatment", [new("Fever", 1_000_000_000, 0,
                    new() { TemperatureDeciCelsius = 390 })], new(false, false))
            ]));
            window.RestartSettings();
            window.SelectPage(0);
            skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            skin.ShowTherapy.IsChecked = true;
            window.RunDefibrillatorCommand(DefibrillatorCommand.Charge);
            safetyTimeNs += 1_000_000_000;
            window.Pulse(window.ActiveTimer, 50_000_000);
            Require(window.Defibrillator.State.Energy == EnergyState.Ready, "permitted treatment reaches the actual device owner");
            window.RunDefibrillatorCommand(DefibrillatorCommand.PressShock);
            safetyTimeNs += 500_000_000;
            window.Pulse(window.ActiveTimer, 50_000_000);
            Require(window.Session.CurrentTimelineId == "after" && window.Session.ShockArtifacts.Count == 1 &&
                !window.Session.ShockAllowed && !skin.Defibrillator.Charge.IsEnabled,
                $"an actual ineffective shock forks once and applies the destination's permissions immediately: " +
                $"line={window.Session.CurrentTimelineId}, artifacts={window.Session.ShockArtifacts.Count}, " +
                $"allowed={window.Session.ShockAllowed}, charge={skin.Defibrillator.Charge.IsEnabled}, " +
                $"attempt={window.Defibrillator.State.Attempt}, error={window.Session.TimelineBranchError}, status={window.Settings.Status.Text}");
            long origin = window.Session.TimelineOriginTimeNs;
            Require(origin == window.Session.SimulationTimeNs && origin > 0 && window.Session.ManualVitals.TemperatureDeciCelsius is null,
                "fork time is treatment-relative and does not reset patient time or apply later targets early");
            while (window.Session.SimulationTimeNs < origin + 1_000_000_000)
            {
                safetyTimeNs += 50_000_000;
                window.Pulse(window.ActiveTimer, 50_000_000);
            }
            Require(window.Session.ManualVitals.TemperatureDeciCelsius == 390 && window.Session.TimelineOriginTimeNs == origin,
                "the desktop continues the inherited session and applies the branch event at T+1 second");
            window.RunDefibrillatorCommand(DefibrillatorCommand.Charge);
            Require(window.Defibrillator.State.Energy == EnergyState.Idle,
                "a newly forbidden branch cannot recharge or replay the previous shock");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
