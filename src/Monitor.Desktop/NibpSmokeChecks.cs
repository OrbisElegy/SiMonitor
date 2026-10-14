// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class NibpSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            GenericMonitorSkin Skin() => (GenericMonitorSkin)window.MonitorTrace.Skin!;
            string Reading() => window.MonitorView.ManualNumericTexts[^1];
            void Advance(int milliseconds)
            {
                for (int time = 0; time < milliseconds; time += 50) { window.Pulse(window.ActiveTimer, 50_000_000); }
            }
            void ClickStart() => Skin().NibpStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!Skin().NibpStartButton.IsEnabled && !Skin().NibpAutoButton.IsEnabled, "NIBP actions start disabled");
            Require(((TextBlock)Skin().NibpAutoButton.Content!).Text == window.Localization.Get("skin.nibpAuto") &&
                AutomationProperties.GetName(Skin().NibpAutoButton) == window.Localization.Get("skin.nibpAuto"),
                "inactive automatic mode has a visible and accessible button label");
            var settings = window.Settings.ManualVitals;
            settings.NibpEnabled.IsChecked = true;
            Require(!Skin().NibpStartButton.IsEnabled, "an unapplied draft cannot enable NIBP actions");
            window.RestartSettings();
            Require(Skin().NibpStartButton.IsEnabled && Skin().NibpAutoButton.IsEnabled && Reading().Contains("--- / ---", StringComparison.Ordinal),
                "applied NIBP enables actions but does not display the target as a reading");
            ClickStart();
            Require(window.Session.Nibp.Phase == NibpPhase.Inflating && window.Settings.Sound.PublishedNibp?.Phase == NibpPhase.Inflating,
                "manual start begins the cuff and publishes inflation sound");
            Advance(6000);
            Require(window.Session.Nibp.Phase == NibpPhase.Deflating && window.Settings.Sound.PublishedNibp?.Phase == NibpPhase.Deflating &&
                Reading().Contains("--- / ---", StringComparison.Ordinal), "deflation sound precedes numeric results");
            var timer = window.ActiveTimer;
            long timeNs = window.Session.SimulationTimeNs;
            window.Pause();
            window.Pulse(timer, 250_000_000);
            Require(window.Settings.Sound.PublishedNibp is null && window.Session.SimulationTimeNs == timeNs,
                "pause stops sound and measurement time together");
            window.Start();
            Require(window.Settings.Sound.PublishedNibp?.Phase == NibpPhase.Deflating, "resume continues the current phase");
            Advance(9000);
            Require(Reading().Contains("120 / 80", StringComparison.Ordinal) && window.Settings.Sound.PublishedNibp is null,
                "the completed cycle publishes its reading and stops sound");
            ClickStart();
            ClickStart();
            Require(!window.Session.Nibp.Measuring && window.Settings.Sound.PublishedNibp is null, "a second click stops an active cuff");
            Skin().NibpInterval.SelectedIndex = 0;
            Skin().NibpAutoEnabled.IsChecked = true;
            Require(window.Session.Nibp.AutoEnabled && window.Session.Nibp.Measuring, "automatic mode immediately measures");
            Require(((TextBlock)Skin().NibpAutoButton.Content!).Text == window.Localization.Format("nibp.autoActive", 1),
                "active automatic mode shows the selected interval");
            Advance(15000);
            Require(!window.Session.Nibp.Measuring, "automatic mode rests after completing a cycle");
            Advance(45000);
            Require(window.Session.Nibp.Measuring && window.Settings.Sound.PublishedNibp?.Phase == NibpPhase.Inflating,
                "the selected one-minute start interval triggers the next measurement");
            Skin().NibpAutoEnabled.IsChecked = false;
            Require(window.Session.Nibp.Measuring && window.Session.Nibp.NextMeasurementAtNs is null,
                "turning off automatic mode leaves the current cycle available to finish or stop");
            window.Settings.ApplyDelaySeconds.Value = 0;
            settings.NibpEnabled.IsChecked = false;
            window.ApplySettings();
            Advance(50);
            Require(!Skin().NibpStartButton.IsEnabled && !Skin().NibpAutoButton.IsEnabled &&
                !window.Session.Nibp.Measuring && window.Settings.Sound.PublishedNibp is null && Reading() == "",
                "disabling NIBP cancels the cuff, clears the reading and disables both actions");
            settings.NibpEnabled.IsChecked = true;
            window.RestartSettings();
            ClickStart();
            Advance(15000);
            window.Pause();
            window.Session.Nibp.Advance(window.Session.Nibp.MeasuredAtNs!.Value + NibpMeasurement.ReadingLifetimeNs, window.Session.ManualVitals.Nibp);
            window.MonitorView.Refresh();
            Require(Reading().Contains("--- / ---", StringComparison.Ordinal) && Reading().Contains("过期", StringComparison.Ordinal),
                "expired NIBP renders placeholders and an expiry status");
            window.Localization.Select("en");
            Require(Reading().Contains("expired", StringComparison.Ordinal) &&
                ((TextBlock)Skin().NibpAutoButton.Content!).Text == "Auto NIBP", "expiry status and automatic button follow the selected language");
            window.Localization.Select("zh-CN");
            DesktopViewportSmokeChecks.Layout(window, 1440, 940);
            Directory.CreateDirectory("artifacts");
            using var screenshot = new RenderTargetBitmap(new PixelSize(1440, 940), new Vector(96, 96));
            screenshot.Render((Control)window.Content!);
            screenshot.Save("artifacts/nibp-expired.png", PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("NIBP: " + message); }
    }
}
