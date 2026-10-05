// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Interactivity;
using Avalonia.Threading;
using Monitor.Application.Measurements;
using Monitor.Application.Scenarios;

namespace Monitor.Desktop;

internal static class VitalChangeSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            var settings = window.Settings;
            var panel = settings.VitalChanges;
            window.RestartSettings();
            settings.Tabs.SelectedIndex = 7;
            Dispatcher.UIThread.RunJobs();
            Require(!settings.Apply.IsEffectivelyVisible, "vital changes act at once without the apply footer");
            var heart = Row(panel, VitalSign.HeartRateBpm);
            var saturation = Row(panel, VitalSign.SpO2MilliPercent);
            var arterial = Row(panel, VitalSign.AbpSystolicCentiMmHg);
            Require(heart.Toggle.IsEnabled && !saturation.Toggle.IsEnabled && arterial.Toggle.IsEnabled,
                "the sinus reference can vary heart rate and pressures; SpO₂ needs the teaching source");

            heart.Toggle.IsChecked = true;
            heart.Fields[0].Field.Value = 120;
            panel.Duration.Value = 20;
            panel.Trigger.SelectedIndex = (int)VitalChangeTrigger.Manual;
            Click(panel.Add);
            var scheduler = window.VitalChanges!;
            int id = scheduler.Events.Single().Id;
            Require(scheduler.Events.Single().State == VitalChangeState.Waiting && panel.Queue.ItemCount == 1,
                "a manual change waits in the queue");
            panel.Queue.SelectedIndex = 0;
            Click(panel.TriggerSelected);
            var session = window.Session;
            Pulse(window, 30);
            Require(ReferenceEquals(session, window.Session) && scheduler.Events.Single(entry => entry.Id == id).State == VitalChangeState.Done,
                "the change continues the running session and finishes after its duration");
            var rate = window.Session.Measurements!.HeartRate;
            Require(rate.Status == WaveformMeasurementStatus.Valid && Math.Abs(rate.MilliBeatsPerMinute!.Value - 120_000) <= 2000,
                "the measured heart rate follows the change to its target");

            heart.Toggle.IsChecked = false;
            arterial.Toggle.IsChecked = true;
            arterial.Fields[0].Field.Value = 100;
            arterial.Fields[1].Field.Value = 98;
            Click(panel.Add);
            Require(scheduler.Events.Count == 1 && panel.Status.Text == window.Localization.Get("vitalChanges.pulseTooSmall"),
                "a pressure pair closer than 5 mmHg is rejected before queueing");
            arterial.Fields[1].Field.Value = 60;
            panel.Duration.Value = 0;
            panel.Trigger.SelectedIndex = (int)VitalChangeTrigger.AfterDelay;
            panel.Delay.Value = 2;
            Click(panel.Add);
            Pulse(window, 25);
            var pressure = window.Session.Measurements!.AbpMean.Pulse;
            Require(pressure is { Status: WaveformMeasurementStatus.Valid, SystolicCentiMmHg: { } systolic, DiastolicCentiMmHg: { } diastolic } &&
                Math.Abs(systolic - 10000) <= 200 && Math.Abs(diastolic - 6000) <= 200,
                "a delayed immediate change moves the measured ABP to its targets");

            arterial.Toggle.IsChecked = false;
            heart.Toggle.IsChecked = true;
            heart.Fields[0].Field.Value = 60;
            panel.Duration.Value = 60;
            panel.Trigger.SelectedIndex = (int)VitalChangeTrigger.Manual;
            Click(panel.Add);
            panel.Queue.SelectedIndex = 0;
            Click(panel.TriggerSelected);
            Pulse(window, 10);
            Click(panel.StopAll);
            int held = scheduler.ValuesAt(window.Session.SimulationTimeNs)[VitalSign.HeartRateBpm];
            Pulse(window, 10);
            Require(!scheduler.IsChanging && held is > 60 and < 120 &&
                scheduler.ValuesAt(window.Session.SimulationTimeNs)[VitalSign.HeartRateBpm] == held && panel.Queue.ItemCount == 0,
                "stopping holds the current value and clears the queue");

            Click(panel.Add);
            panel.Queue.SelectedIndex = 0;
            Click(panel.TriggerSelected);
            Pulse(window, 4);
            window.ApplySettings();
            Require(!scheduler.IsChanging && scheduler.Events.All(entry => entry.State is not VitalChangeState.Active),
                "applying settings stops a running change");
        }
        finally { window.Close(); }
    }

    private static VitalChangePanel.Row Row(VitalChangePanel panel, VitalSign sign) =>
        panel.Rows.Single(row => row.Fields.Any(field => field.Sign == sign));

    private static void Click(Avalonia.Controls.Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    private static void Pulse(DesignPreviewWindow window, int seconds)
    {
        for (int step = 0; step < seconds * 20; step++) { window.Pulse(window.ActiveTimer, 50_000_000); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Vital changes: " + message); } }
}
