// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;

namespace Monitor.Desktop;

internal static class EnclosureSkinSmokeChecks
{
    internal static void Verify()
    {
        DeviceRotaryDialSmokeChecks.Verify();
        string directory = Path.Combine(Path.GetTempPath(), "monitor-enclosure-" + Guid.NewGuid().ToString("N"));
        string preferences = Path.Combine(directory, "display.json");
        long safetyNs = 0;
        var window = new DesignPreviewWindow(preferences, () => safetyNs);
        window.Show();
        try
        {
            window.Settings.Skin.SelectedIndex = (int)MonitorSkin.SevenRows;
            window.RestartSettings();
            var session = window.Session;
            window.ChangeMonitorSkin(MonitorSkinPackage.DefibrillatorEnclosure);
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            string?[] printedModes = skin.ModeChoices.Select(button => ((TextBlock)button.Content!).Text).ToArray();
            void Advance(int frames)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    safetyNs += 50_000_000;
                    window.Pulse(window.ActiveTimer, 50_000_000);
                }
            }
            void Mode(DeviceOperatingMode mode)
            {
                Click(skin.ModeChoices[mode switch
                {
                    DeviceOperatingMode.Pacer => 0,
                    DeviceOperatingMode.Off => 1,
                    DeviceOperatingMode.Monitor => 2,
                    DeviceOperatingMode.Aed => 3,
                    _ => 4
                }]);
                Require(skin.ModeChoices.Select(button => ((TextBlock)button.Content!).Text).SequenceEqual(printedModes) &&
                    skin.ModeChoices.All(button => ((TextBlock)button.Content!).FontWeight == Avalonia.Media.FontWeight.Normal && button.BorderThickness == new Thickness(0)),
                    "mode changes leave the printed dial labels and their styling fixed");
                Require(skin.Defibrillator.Charge.Content as string == window.Localization.Get("skin.charge"),
                    "the physical charge key retains its fixed label in every mode");
            }
            Require(ReferenceEquals(window.Session, session) && session.Display.Slots.Count == 3 &&
                !window.Settings.Skin.IsEnabled && !skin.LayoutRows.IsEnabled,
                "enclosure selection preserves the session and fixes three rows at both layout controls");
            window.Settings.Skin.SelectedIndex = (int)MonitorSkin.SevenRows;
            Require(window.Settings.ReadDisplay().Slots.Count == 3, "programmatic selection cannot bypass the enclosure constraint");
            Require(skin.SoftKeys.Count == 3 && skin.ModeChoices.Count == 5 &&
                skin.Energy.ItemCount == 21 && window.Defibrillator.Configuration.EnergyStepsJoules[^1] == 360,
                "the defibrillator enclosure has three softkeys, five selector positions and all 21 energy steps");
            Require(!skin.Defibrillator.Charge.IsEnabled && !skin.Defibrillator.Shock.IsEnabled,
                "monitor mode cannot charge or discharge");
            Advance(120);
            Capture(window, "enclosure-monitor.png");
            VerifySoftKeyAlignment(skin);
            DialKey(skin.ModeSelector!, Avalonia.Input.Key.Right);
            Require(skin.OperatingMode == DeviceOperatingMode.Aed, "the dial keyboard requests an accepted host mode");
            DialKey(skin.ModeSelector!, Avalonia.Input.Key.Left);
            Require(skin.OperatingMode == DeviceOperatingMode.Monitor, "the dial keyboard can return to the preceding mode");
            long freezeAt = session.SimulationTimeNs;
            Click(skin.SoftKeys[0]);
            Advance(20);
            Require(skin.WaveformsFrozen && session.SimulationTimeNs > freezeAt, "freezing the waveform keeps acquisition advancing");
            Click(skin.SoftKeys[0]);
            Require(!skin.WaveformsFrozen, "unfreeze returns to the live trace");
            Require(skin.MenuButton.Focus(), "the device menu accepts keyboard focus");
            Click(skin.MenuButton);
            DesktopViewportSmokeChecks.Layout(window, 1440, 940);
            var screen = skin.Screen!;
            var overlayClose = screen.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "×");
            var overlay = overlayClose.GetVisualAncestors().OfType<Border>().First(border => border.Child is Grid);
            Require(AutomationProperties.GetName(overlayClose) == window.Localization.Get("devicePanel.close") &&
                window.FocusManager!.GetFocusedElement() is Control focused && focused.GetVisualAncestors().Contains(overlay),
                "opening a device overlay moves focus inside and names its icon-only close action");
            overlayClose.Focus();
            overlayClose.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
            Require(skin.MenuButton.IsFocused && !screen.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "×"),
                "Escape dismisses the device overlay and restores its trigger focus");
            Click(skin.SoftKeys[1]);
            DesktopViewportSmokeChecks.Layout(window, 1440, 940);
            Click(skin.MenuButton);
            Click(skin.SoftKeys[1]);
            DesktopViewportSmokeChecks.Layout(window, 1440, 940);
            Click(skin.GainButton!);
            Require(!session.Display.Slots[0].Automatic, "gain changes the actual ECG range");
            Click(skin.EventButton!);
            Require(session.CapturePrintSnapshot().Events.Any(item => item.Kind == PreviewRecordEventKind.OperatorMarked),
                "the event key reaches the printed event history");

            Mode(DeviceOperatingMode.Manual);
            Require(skin.Defibrillator.Charge.IsEnabled && window.Defibrillator.EnergyJoules == 200,
                "manual mode selects its adult default and enables charging");
            skin.Energy.SelectedItem = 360;
            Click(skin.Defibrillator.Charge);
            Advance(70);
            Require(skin.ChargeProgress!.IsVisible && skin.ChargeProgress.Value is > 0 and < 1000 &&
                skin.ChargeProgress.Value == skin.Defibrillator.ChargeProgressPermille &&
                skin.ChargeProgress.GetVisualAncestors().Contains(skin.Screen) &&
                skin.Defibrillator.Charge.Content as string == window.Localization.Get("skin.charge"),
                "charging progress is rendered inside the screen while the hardware label stays fixed");
            Capture(window, "enclosure-charging.png");
            Advance(71);
            Require(window.Defibrillator.State.Energy == EnergyState.Ready && skin.Defibrillator.Shock.IsEnabled,
                "the hardware charge button reaches the shared ready state");
            Click(skin.Defibrillator.Shock);
            Require(session.ShockArtifacts.Count == 0, "a click alone cannot authorize a shock");
            Key(skin.Defibrillator.Shock, true);
            Mode(DeviceOperatingMode.Monitor);
            Advance(20);
            Key(skin.Defibrillator.Shock, false);
            Require(window.Defibrillator.State.Energy == EnergyState.Idle && session.ShockArtifacts.Count == 0,
                "switching modes invalidates a held discharge");
            Mode(DeviceOperatingMode.Manual);
            Click(skin.SoftKeys[0]);
            Click(skin.Defibrillator.Charge);
            Advance(141);
            Key(skin.Defibrillator.Shock, true);
            Advance(40);
            Key(skin.Defibrillator.Shock, false);
            Require(session.ShockArtifacts.Count == 1 && window.Defibrillator.State.Mode == DefibrillationMode.ManualAsynchronous,
                "a synchronized delivery uses real QRS evidence and leaves sync afterward");
            Capture(window, "enclosure-manual.png");
            Advance(80);
            Mode(DeviceOperatingMode.Aed);
            Require(window.Aed.Enabled && window.Defibrillator.Configuration.AutoDisarmSeconds == 30,
                "the selector activates AED with its own disarm timeout");
            Click(skin.SoftKeys[0]);
            Require(window.Aed.Phase == AedPhase.Cpr && window.Defibrillator.State.Energy == EnergyState.Idle,
                "the AED CPR key disarms before entering CPR");
            Click(skin.SoftKeys[0]);
            Require(window.Aed.Phase == AedPhase.Analyzing, "resume analysis starts a complete new analysis");
            window.Pause();
            Require(window.Aed.Phase == AedPhase.Suspended && !skin.Defibrillator.Charge.IsEnabled,
                "pause removes AED treatment availability");
            window.Start();
            Require(skin.Defibrillator.Charge.Content as string == window.Localization.Get("skin.charge") &&
                !skin.Defibrillator.Charge.IsEnabled && skin.SoftKeyLabels[0].Text == window.Localization.Get("devicePanel.aedResume"),
                "the suspended AED exposes resume on the screen softkey while the hardware charge key stays unchanged");
            Click(skin.SoftKeys[0]);
            Require(window.Aed.Phase == AedPhase.Analyzing, "the screen-mapped softkey resumes suspended AED analysis");
            Capture(window, "enclosure-aed.png");
            Mode(DeviceOperatingMode.Off);
            Mode(DeviceOperatingMode.Monitor);
            Require(window.ActiveTimer is not null && !window.Aed.Enabled, "powering back on starts monitoring without retained AED authority");

            window.Settings.PacingPermissions.Allowed.IsChecked = true;
            window.RestartSettings();
            skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            session = window.Session;
            Mode(DeviceOperatingMode.Pacer);
            Click(skin.SoftKeys[1]);
            Click(skin.MenuButton);
            Click(skin.SoftKeys[1]);
            DesktopViewportSmokeChecks.Layout(window, 1440, 940);
            Require(skin.NavigationDial!.Focus(), "the rendered navigation dial accepts keyboard focus");
            decimal originalRate = skin.Rate.Value!.Value;
            DialKey(skin.NavigationDial, Avalonia.Input.Key.Right);
            DialKey(skin.NavigationDial, Avalonia.Input.Key.Enter);
            DialKey(skin.NavigationDial, Avalonia.Input.Key.Right);
            Require(skin.NavigationDial.IsFocused && skin.Rate.Value == originalRate + 5,
                $"navigation keeps dial focus while selecting and editing a pacing parameter: focused={skin.NavigationDial.IsFocused}, rate={skin.Rate.Value}, previous={originalRate}, angle={skin.NavigationDial.AngleDegrees}, area={skin.TherapyArea is not null}");
            DialKey(skin.NavigationDial, Avalonia.Input.Key.Enter);
            skin.Rate.Value = 210;
            skin.Current.Value = 30;
            Click(skin.SoftKeys[0]);
            Advance(10);
            Require(session.ActivePacing is not null && session.Configuration.PacingOutput?.RatePerMinute == 210,
                $"pacing controls produce live output at the defibrillator upper rate: mode={skin.OperatingMode}; allowed={session.PacingAllowed}; apply={skin.ApplyPacing.IsEnabled}; soft={skin.SoftKeys[0].IsEnabled}; status={skin.PacingStatus.Text}; output={session.Configuration.PacingOutput}; pending={session.PendingSourceTimeNs}");
            Key(skin.SoftKeys[2], true);
            Advance(10);
            Require(session.Configuration.PacingOutput?.PulseDivision == 4, "holding 4:1 changes the actual pulse interval");
            Key(skin.SoftKeys[2], false);
            Advance(10);
            Require(session.Configuration.PacingOutput?.PulseDivision == 1, "releasing 4:1 restores the programmed rate");
            Key(skin.SoftKeys[2], true);
            Advance(10);
            window.Pause();
            window.Start();
            Advance(10);
            Require(session.Configuration.PacingOutput?.PulseDivision == 1, "pausing cancels held output even without a key-up event");
            window.Pause();
            Mode(DeviceOperatingMode.Off);
            Mode(DeviceOperatingMode.Monitor);
            Advance(10);
            Require(skin.OperatingMode == DeviceOperatingMode.Monitor && session.ActivePacing is null,
                "powering off paused pacing and powering on can settle the pending stop without locking the selector");
            foreach (string locale in new[] { "en", "zh-TW", "zh-CN" })
            {
                window.Localization.Select(locale);
                DesktopViewportSmokeChecks.Layout(window, 960, 640);
                Require(skin.SoftKeys.All(button => button.Bounds.Width > 0), "all hardware softkeys remain laid out at the minimum window size");
                VerifySoftKeyAlignment(skin);
                foreach (var choice in skin.ModeChoices)
                {
                    Require(choice.Bounds.Width >= ((TextBlock)choice.Content!).Bounds.Width &&
                        choice.Bounds.Height >= ((TextBlock)choice.Content!).Bounds.Height,
                        "printed mode labels fit their reserved area in all locales");
                }
            }
            window.ChangeMonitorSkin(MonitorSkinPackage.Generic);
            Require(window.Session.Display.Slots.Count == 7 && window.Settings.Skin.IsEnabled && window.Settings.Defibrillator.DeviceOverride is null,
                "leaving the enclosure restores the unlocked layout and editable device profile");
            Require(((GenericMonitorSkin)window.MonitorTrace.Skin!).Read().EnergyJoules == 150,
                "device-specific defaults do not overwrite the generic selected energy");
            window.ChangeMonitorSkin(MonitorSkinPackage.DefibrillatorEnclosure);
        }
        finally { window.Close(); }
        try
        {
            var reopened = new DesignPreviewWindow(preferences);
            try
            {
                Require(reopened.CurrentSkinPackage.Defibrillator && reopened.Session.Display.Slots.Count == 3 &&
                    !reopened.Settings.Skin.IsEnabled, "reopening restores the packaged shell and its row constraint");
                reopened.ChangeMonitorSkin(MonitorSkinPackage.Generic);
                Require(reopened.Session.Display.Slots.Count == 7, "the unlocked full layout survives a restart while the shell is selected");
                reopened.ChangeMonitorSkin(MonitorSkinPackage.DefibrillatorEnclosure);
            }
            finally { reopened.Close(); }
            File.WriteAllText(Path.ChangeExtension(preferences, ".skin.simskin"), "{}", System.Text.Encoding.UTF8);
            var recovered = new DesignPreviewWindow(preferences);
            try
            {
                Require(recovered.CurrentSkinPackage.Id == "generic" && recovered.Session.Display.Slots.Count == 7,
                    "an invalid saved enclosure restores the complete unlocked layout during fallback");
            }
            finally { recovered.Close(); }
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
        VerifyProfileWithoutEnclosure();
        VerifyEnergyStepPrecedence();
    }

    private static void VerifySoftKeyAlignment(GenericMonitorSkin skin)
    {
        for (int index = 0; index < skin.SoftKeys.Count; index++)
        {
            var key = skin.SoftKeys[index];
            var label = skin.SoftKeyLabels[index];
            var keyCenter = key.TranslatePoint(new Point(key.Bounds.Width / 2, 0), skin.Screen!)!.Value;
            var labelCenter = label.TranslatePoint(new Point(label.Bounds.Width / 2, 0), skin.Screen!)!.Value;
            // Odd column/text widths may snap to adjacent logical pixels; compare
            // in screen coordinates so scaling cannot magnify that rounding error.
            Require(Math.Abs(keyCenter.X - labelCenter.X) <= 1.01,
                $"each physical softkey is horizontally centered under its screen label at every scale: slot={index}, key={keyCenter.X}, label={labelCenter.X}, screenWidth={skin.Screen!.Bounds.Width}");
            Require(label.Opacity == (key.IsEffectivelyEnabled ? 1 : .4),
                "screen softkey availability matches the corresponding physical key");
        }
    }

    private static void VerifyEnergyStepPrecedence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-skin-energy-" + Guid.NewGuid().ToString("N"));
        string preferences = Path.Combine(directory, "display.json");
        int[] manualSteps = [5, 100, 250];
        try
        {
            var window = new DesignPreviewWindow(preferences);
            window.Show();
            try
            {
                window.ChangeMonitorSkin(MonitorSkinPackage.DefibrillatorEnclosure);
                Require(window.Defibrillator.Configuration.EnergyStepsJoules.SequenceEqual(MonitorSkinPackage.DefibrillatorEnclosure.DefaultEnergyStepsJoules),
                    "without a manual override the active skin supplies its default energy table");
                window.Settings.Defibrillator.UseSkinEnergySteps.IsChecked = false;
                window.Settings.Defibrillator.Steps.Text = "5, 100, 250";
                window.RestartSettings();
                Require(window.Defibrillator.Configuration.EnergyStepsJoules.SequenceEqual(manualSteps) &&
                    window.Defibrillator.Configuration.ChargeDurationMilliseconds == 7000,
                    "a manual table overrides the profile while other device parameters remain fixed");
                var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
                Click(skin.ModeChoices[4]);
                Require(window.Defibrillator.EnergyJoules == 250, "manual mode snaps its default to a legal custom energy");
                Click(skin.ModeChoices[3]);
                Require(window.Aed.Enabled && window.Defibrillator.EnergyJoules == 250 &&
                    window.Defibrillator.Configuration.EnergyStepsJoules.SequenceEqual(manualSteps),
                    "AED maps its sequence onto the custom table without dropping the override");
                window.ChangeMonitorSkin(MonitorSkinPackage.Generic);
                Require(window.Defibrillator.Configuration.EnergyStepsJoules.SequenceEqual(manualSteps),
                    "manual energy steps remain authoritative when changing skins");
                window.ChangeMonitorSkin(MonitorSkinPackage.DefibrillatorEnclosure);
            }
            finally { window.Close(); }
            var reopened = new DesignPreviewWindow(preferences);
            reopened.Show();
            try
            {
                Require(reopened.Settings.Defibrillator.CustomEnergySteps &&
                    reopened.Defibrillator.Configuration.EnergyStepsJoules.SequenceEqual(manualSteps),
                    "reopening restores both the manual table and its override selection");
                reopened.Settings.Defibrillator.UseSkinEnergySteps.IsChecked = true;
                reopened.RestartSettings();
                Require(reopened.Defibrillator.Configuration.EnergyStepsJoules.SequenceEqual(MonitorSkinPackage.DefibrillatorEnclosure.DefaultEnergyStepsJoules),
                    "restoring defaults immediately uses the active skin's table");
                reopened.ChangeMonitorSkin(MonitorSkinPackage.Generic);
                Require(reopened.Defibrillator.Configuration.EnergyStepsJoules.SequenceEqual(MonitorSkinPackage.Generic.DefaultEnergyStepsJoules),
                    "default mode follows subsequent skin changes");
            }
            finally { reopened.Close(); }
            var defaults = new DesignPreviewWindow(preferences);
            try
            {
                Require(!defaults.Settings.Defibrillator.CustomEnergySteps &&
                    defaults.Defibrillator.Configuration.EnergyStepsJoules.SequenceEqual(MonitorSkinPackage.Generic.DefaultEnergyStepsJoules),
                    "the explicit default selection survives reopening even with a retained manual table");
                defaults.Settings.Defibrillator.UseSkinEnergySteps.IsChecked = false;
                Require(defaults.Settings.Defibrillator.Read().EnergyStepsJoules.SequenceEqual(manualSteps),
                    "returning to manual mode restores the previously saved table");
            }
            finally { defaults.Close(); }
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }

    private static void VerifyProfileWithoutEnclosure()
    {
        using var bytes = new MemoryStream();
        MonitorSkinPackage.DefibrillatorEnclosure.Write(bytes);
        bytes.Position = 0;
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("manifest.json")!;
            JsonNode manifest;
            using (var input = entry.Open()) { manifest = JsonNode.Parse(input)!; }
            manifest["layout"] = "generic";
            manifest["fixedWaveformRows"] = null;
            entry.Delete();
            using var output = archive.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(output, manifest);
        }
        bytes.Position = 0;
        var package = MonitorSkinPackage.Read(bytes);
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.ChangeMonitorSkin(package);
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            Require(skin.DevicePanel is null && skin.Defibrillator.Charge.IsEnabled,
                "a device profile without an enclosure does not acquire unreachable selector-mode restrictions");
            Click(skin.Defibrillator.Charge);
            Require(window.Defibrillator.State.Energy == EnergyState.Charging, "the shared device profile remains usable from generic controls");
        }
        finally { window.Close(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Key(Button button, bool down) => button.RaiseEvent(new KeyEventArgs
    { RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent, Key = Avalonia.Input.Key.Space });
    private static void DialKey(DeviceRotaryDial dial, Key key)
    {
        dial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        dial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
    }
    private static void Capture(Window window, string name)
    {
        DesktopViewportSmokeChecks.Layout(window, 1440, 940);
        using var image = new RenderTargetBitmap(new PixelSize(1440, 940), new Vector(96, 96));
        image.Render((Control)window.Content!);
        Directory.CreateDirectory("artifacts");
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }
    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Enclosure skin: " + message); } }
}
