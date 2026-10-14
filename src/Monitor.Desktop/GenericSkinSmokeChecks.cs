// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal static class GenericSkinSmokeChecks
{
    private static readonly int[] DefaultChannels = [0, 2, 1, 4];
    private static readonly string[] ExpectedReadings = ["75", "98", "16", "40"];

    internal static void Verify()
    {
        PlethIndicatorSmokeChecks.Verify();
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.Pause();
            Require(window.Session.Display.Slots.Select(slot => slot.Channel).SequenceEqual(DefaultChannels), "generic starts ECG / PLETH / RESP / CO2");
            Require(window.MonitorTrace.Skin is { Id: "generic" }, "host installs the generic skin through the composition port");
            Require(window.MonitorTrace.ChannelBrush(2).ToString() == Brush.Parse("#49BFFF").ToString() &&
                window.MonitorTrace.ChannelBrush(1).ToString() == Brush.Parse("#FFE16A").ToString(), "PLETH blue and RESP yellow");
            var view = window.MonitorView;
            Require(view.ManualNumericTexts.Count == 4 && view.ManualNumericTexts.All(string.IsNullOrEmpty), "unavailable manual parameters retain their slots");
            foreach (var size in new[] { new Size(1440, 940), new Size(960, 640) })
            {
                DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
                var trace = window.MonitorTrace;
                var nibp = view.ManualTiles[3];
                Point Position(Control control) => control.TranslatePoint(default, view)!.Value;
                Require(trace.Bounds.Width > 200 && trace.Bounds.Height > 200, "waveforms remain usable at the minimum viewport");
                Require(Position(nibp).X >= Position(trace).X + trace.Bounds.Width &&
                    Position(nibp).Y >= Position(trace).Y + trace.Bounds.Height, "NIBP occupies the lower numeric column");
                Require(view.ManualTiles.All(tile => tile.Bounds.Width > 60 && tile.Bounds.Height >= 80), "all manual slots stay visible");
                Directory.CreateDirectory("artifacts");
                using var image = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                image.Render((Control)window.Content!);
                image.Save($"artifacts/generic-skin-{size.Width:0}.png", PngBitmapEncoderOptions.Default);
            }
            var session = window.Session;
            long time = session.SimulationTimeNs;
            var manual = session.ManualVitals;
            var buttons = view.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("skin-dummy")).ToArray();
            Require(buttons.Length == 0, "generic toolbar has no remaining placeholder actions");
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            Require(skin.AlarmsButton.MinHeight >= 44 && skin.SilenceButton.MinHeight >= 44, "alarm actions retain keyboard and pointer targets");
            skin.AlarmsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            skin.SilenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(session.SimulationTimeNs == time && session.ManualVitals == manual && window.ActiveTimer is null &&
                ReferenceEquals(session, window.Session), "alarm controls do not advance the paused session");
            Require(window.Settings.Sound.AlarmAudioPaused, "silence pauses alarm audio from the toolbar");
            window.Settings.Language.SelectedIndex = 0;
            Require(skin.ShowTherapy.Content?.ToString() == "Show therapy controls" && skin.ShowTherapy.OnContent?.ToString() == "Visible" &&
                skin.LayoutRows.Items[0]?.ToString() == window.Localization.Get("skin.threeRows"), "layout choices localize");
            Require(view.ManualNumericTexts.All(string.IsNullOrEmpty) &&
                ((TextBlock)skin.SilenceButton.Content!).Text == window.Localization.Get("sound.resume"), "paused alarm actions localize");
            string path = Path.Combine(Path.GetTempPath(), "generic-display-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new DisplayPreferenceStore(path);
                Require(store.Save(new DisplayPreferences(session.Display, 0)), "four-row preferences save");
                var loaded = store.Load(out bool rejected);
                Require(!rejected && loaded!.Display.Skin == MonitorSkin.FourRows &&
                    loaded.Display.Slots.SequenceEqual(session.Display.Slots), "four-row preference roundtrip retains slots");
            }
            finally { File.Delete(path); }
            window.Settings.Language.SelectedIndex = 1;
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.OpticalTarget.Value = 98;
            window.Settings.ManualVitals.NibpEnabled.IsChecked = true;
            window.Settings.ManualVitals.TemperatureEnabled.IsChecked = true;
            window.RestartSettings();
            for (int i = 0; i < 600; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Pause();
            Require(window.MonitorView.NumericTexts.SequenceEqual(ExpectedReadings),
                "four visible rows show acquired values in the requested order");
            VerifyLayout(window);
            VerifyNumericScaling(window);
            foreach (var size in new[] { new Size(1440, 940), new Size(960, 640) })
            {
                DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
                using var image = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                image.Render((Control)window.Content!);
                image.Save($"artifacts/generic-skin-live-{size.Width:0}.png", PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
        VerifyLayoutPreferences();
    }

    private static void VerifyNumericScaling(DesignPreviewWindow window)
    {
        var view = window.MonitorView;
        var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
        var session = window.Session;
        long time = session.SimulationTimeNs;
        double FontHeight(TextBlock text) =>
            text.TranslatePoint(new Point(0, text.FontSize), view)!.Value.Y - text.TranslatePoint(default, view)!.Value.Y;
        foreach (int rows in new[] { 0, 1 })
        {
            skin.LayoutRows.SelectedIndex = rows;
            foreach (bool therapy in new[] { true, false })
            {
                skin.ShowTherapy.IsChecked = therapy;
                foreach (string locale in new[] { "zh-CN", "en" })
                {
                    window.Localization.Select(locale);
                    double compactHeight = 0, previousHeight = 0;
                    foreach (var size in new[] { new Size(960, 640), new Size(1920, 1080), new Size(2560, 1440), new Size(960, 640) })
                    {
                        DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
                        var qt = view.NumericBlocks[1];
                        double height = FontHeight(qt);
                        if (compactHeight == 0) { compactHeight = height; }
                        if (size.Width > 960)
                        {
                            Require(height > previousHeight && height >= compactHeight * 1.5,
                                "QT/QTc visibly enlarge with the viewport instead of remaining fixed-size text");
                        }
                        else { Require(Math.Abs(height - compactHeight) < .01, "restoring a compact window restores its readable scale"); }
                        previousHeight = height;
                        foreach (var text in view.NumericBlocks.Where((_, index) => index % 2 == 1).Where(text => text.IsVisible))
                        {
                            Require(Math.Abs(FontHeight(text) - height) < .01, "all secondary readings share the numeric scale");
                            Require(text.TextLayout.Width <= text.Bounds.Width + 1 && text.TextLayout.Height <= text.Bounds.Height + 1,
                                "auxiliary readings fit without clipping, including QT/QTc and localized units");
                        }
                        var pi = view.GetVisualDescendants().OfType<TextBlock>().Single(text =>
                            AutomationProperties.GetName(text) == window.Localization.Get("monitor.piName"));
                        Require(Math.Abs(FontHeight(pi) - height) < .01 && pi.TextLayout.Width <= pi.Bounds.Width + 1,
                            "PI grows alongside PR and fits its column");
                        var nibp = view.ManualTiles[3];
                        double numericLeft = nibp.TranslatePoint(default, view)!.Value.X;
                        double traceRight = window.MonitorTrace.TranslatePoint(new Point(window.MonitorTrace.Bounds.Width, 0), view)!.Value.X;
                        Require(Math.Abs(numericLeft - traceRight) <= 1 && window.MonitorTrace.Bounds.Width > 200,
                            "the enlarged numeric column stays aligned with NIBP and leaves room for waveforms");
                        if (locale == "zh-CN" && size.Width > 960)
                        {
                            using var image = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                            image.Render((Control)window.Content!);
                            image.Save($"artifacts/generic-numerics-{rows + 3}-{(therapy ? "therapy" : "monitor")}-{size.Width:0}.png", PngBitmapEncoderOptions.Default);
                        }
                    }
                }
            }
        }
        skin.ShowTherapy.IsChecked = true;
        window.Localization.Select("zh-CN");
        var original = session.Display;
        foreach (var layout in new[] { MonitorSkin.FiveRows, MonitorSkin.SevenRows })
        {
            session.UpdateDisplay(MonitorDisplayConfiguration.Default(layout));
            view.RefreshDisplay();
            DesktopViewportSmokeChecks.Layout(window, 1920, 1080);
            Require(FontHeight(view.NumericBlocks[0]) >= 24,
                $"legacy dense layouts reserve enough height for the primary ECG reading: {layout}, font {FontHeight(view.NumericBlocks[0])}, bounds {view.NumericBlocks[0].Bounds}, QT {view.NumericBlocks[1].Bounds}, trace {window.MonitorTrace.Bounds}");
        }
        session.UpdateDisplay(original);
        view.RefreshDisplay();
        Require(ReferenceEquals(session, window.Session) && session.SimulationTimeNs == time && window.ActiveTimer is null,
            "resizing and changing numeric layout preserve the paused acquisition session");
    }

    private static void VerifyLayout(DesignPreviewWindow window)
    {
        var session = window.Session;
        var trace = window.MonitorTrace;
        var view = window.MonitorView;
        var skin = (GenericMonitorSkin)trace.Skin!;
        var defibrillator = window.Defibrillator;
        var original = session.Display;
        var customized = original.Slots.ToArray();
        customized[0] = customized[0] with { SpeedTenthsMmPerSecond = 125 };
        customized[3] = new(3, false, new(-10, 180), 500);
        session.UpdateDisplay(new(MonitorSkin.FourRows, customized));
        view.RefreshDisplay();
        long time = session.SimulationTimeNs;
        ulong revision = session.DataRevision;
        skin.Rate.Value = 85;
        Require(!skin.LayoutButton.Classes.Contains("skin-dummy") && skin.LayoutButton.Flyout is Flyout,
            "layout is an active toolbar action with a flyout");
        foreach (var size in new[] { new Size(1440, 940), new Size(960, 640) })
        {
            foreach (bool visible in new[] { false, true })
            {
                skin.ShowTherapy.IsChecked = visible;
                foreach (int rows in new[] { 0, 1 })
                {
                    skin.LayoutRows.SelectedIndex = rows;
                    DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
                    Require(session.Display.Slots.Count == rows + 3 && view.NumericTexts.Count == rows + 3 &&
                        window.Settings.ReadDisplay().Slots.SequenceEqual(session.Display.Slots), "layout updates waveforms, numerics and settings together");
                    Require(session.Display.Slots.SequenceEqual(customized.Take(rows + 3)), "roundtrip preserves channel, scale and speed including the hidden fourth row");
                    Require(skin.TherapyArea!.IsVisible == visible && trace.Bounds.Width > (visible ? 200 : 398),
                        "therapy visibility and waveform space respond at both viewport sizes");
                    using var image = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                    image.Render((Control)window.Content!);
                    image.Save($"artifacts/generic-layout-{rows + 3}-{(visible ? "therapy" : "monitor")}-{size.Width:0}.png", PngBitmapEncoderOptions.Default);
                }
            }
            double narrow = trace.Bounds.Width;
            skin.ShowTherapy.IsChecked = false;
            DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
            Require(Math.Abs(trace.Bounds.Width - narrow - 198) <= 1, "hiding therapy returns the whole sidebar and gutter to waveforms");
            skin.ShowTherapy.IsChecked = true;
        }
        DesktopViewportSmokeChecks.Layout(window);
        skin.LayoutButton.Flyout!.ShowAt(skin.LayoutButton);
        DesktopViewportSmokeChecks.Layout(window);
        Require(skin.LayoutButton.Flyout.IsOpen && skin.ShowTherapy.IsEffectivelyVisible && skin.LayoutRows.IsEffectivelyVisible,
            "layout flyout exposes both controls");
        if (TopLevel.GetTopLevel(skin.ShowTherapy) is { } popup)
        {
            using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(popup.Bounds.Width), (int)Math.Ceiling(popup.Bounds.Height)), new Vector(96, 96));
            image.Render(popup);
            image.Save("artifacts/generic-layout-popup.png", PngBitmapEncoderOptions.Default);
        }
        skin.LayoutButton.Flyout.Hide();
        Require(ReferenceEquals(session, window.Session) && ReferenceEquals(trace, window.MonitorTrace) && ReferenceEquals(view, window.MonitorView) &&
            ReferenceEquals(defibrillator, window.Defibrillator) && session.SimulationTimeNs == time && session.DataRevision == revision &&
            window.ActiveTimer is null && skin.Rate.Value == 85, "paused layout changes preserve runtime, therapy drafts and acquired history");
        window.Start();
        var timer = window.ActiveTimer;
        skin.LayoutRows.SelectedIndex = 0;
        skin.ShowTherapy.IsChecked = false;
        window.Pulse(timer, 50_000_000);
        Require(ReferenceEquals(timer, window.ActiveTimer) && session.SimulationTimeNs > time, "live acquisition continues through layout changes");
        window.Pause();
        skin.LayoutRows.SelectedIndex = 1;
        skin.ShowTherapy.IsChecked = true;
        session.UpdateDisplay(original);
        window.Settings.RestoreDisplay(original, 0);
        view.RefreshDisplay();
    }

    private static void VerifyLayoutPreferences()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-layout-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "display.json");
        try
        {
            var window = new DesignPreviewWindow(path);
            window.Show();
            try
            {
                window.Pause();
                // A quick layout change must not apply or persist unrelated settings drafts.
                window.Settings.PaperLayout.SelectedIndex = 1;
                var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
                skin.LayoutRows.SelectedIndex = 0;
                skin.ShowTherapy.IsChecked = false;
                var saved = new DisplayPreferenceStore(path).Load(out bool rejected);
                Require(!rejected && saved.Display.Skin == MonitorSkin.ThreeRows && !saved.TherapyControlsVisible &&
                    saved.PaperLayout == 0 && saved.Generator is null, "quick layout saves only layout preferences");
            }
            finally { window.Close(); }
            var reopened = new DesignPreviewWindow(path);
            reopened.Show();
            try
            {
                reopened.Pause();
                var skin = (GenericMonitorSkin)reopened.MonitorTrace.Skin!;
                Require(reopened.Session.Display.Skin == MonitorSkin.ThreeRows && skin.ShowTherapy.IsChecked == false &&
                    skin.TherapyArea!.IsVisible == false, "saved layout and therapy visibility restore on reopen");
                reopened.RestartSettings();
                Require(((GenericMonitorSkin)reopened.MonitorTrace.Skin!).ShowTherapy.IsChecked == false, "settings rebuild retains therapy visibility");
            }
            finally { reopened.Close(); }
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            legacy.Remove("TherapyControlsVisible");
            File.WriteAllText(path, legacy.ToJsonString());
            Require(new DisplayPreferenceStore(path).Load(out bool rejectedLegacy).TherapyControlsVisible && !rejectedLegacy,
                "existing preference files retain visible therapy controls");
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
