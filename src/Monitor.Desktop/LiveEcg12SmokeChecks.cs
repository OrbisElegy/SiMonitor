// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class LiveEcg12SmokeChecks
{
    internal static void Verify()
    {
        VerifySources();
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.Pause();
            var session = window.Session;
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            skin.ShowTherapy.IsChecked = true;
            var trace = window.MonitorView.TwelveLeadTrace;
            var slots = session.Display.Slots.ToArray();
            skin.EcgButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.Page == 0 && ReferenceEquals(window.Session, session) && window.ActiveTimer is null &&
                trace.IsVisible && !window.MonitorTrace.IsVisible && skin.TwelveLeadVisible, "button changes the monitor surface without navigating or restarting");
            Require(trace.StripCount == 13 && skin.TherapyArea!.IsVisible && window.MonitorView.NumericTexts.Count == 4,
                "four rows include long II and retain numerics and therapy");
            skin.LayoutRows.SelectedIndex = 0;
            Require(trace.StripCount == 12 && skin.TwelveLeadVisible && session.Display.Slots.Count == 3,
                "three rows hide only long II");
            skin.ShowTherapy.IsChecked = false;
            Require(!skin.TherapyArea!.IsVisible && trace.IsVisible, "therapy visibility remains independent");
            window.Start();
            window.Pulse(window.ActiveTimer, 16_000_000);
            foreach (var lead in Enum.GetValues<EcgLead>())
            { Require(session.EcgSamples(lead, 0, long.MaxValue).Last().TimeNs == 12_000_000, "every live strip has the same current sample"); }
            for (int index = 0; index < 150; index++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Pause();
            long time = session.SimulationTimeNs;
            foreach (int rows in new[] { 3, 4 })
            {
                skin.LayoutRows.SelectedIndex = rows - 3;
                foreach (var size in new[] { new Size(960, 640), new Size(1440, 940) })
                {
                    DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
                    Require(trace.Bounds.Width > 240 && trace.Bounds.Height > 180, "live ECG remains visible at minimum size");
                    Require(trace.StripBounds(0).Top == trace.StripBounds(3).Top && trace.StripBounds(0).Left < trace.StripBounds(3).Left &&
                        trace.StripBounds(2).Bottom <= trace.Bounds.Bottom, "twelve leads follow the three-by-four arrangement");
                    if (rows == 4) { Require(trace.StripBounds(12).Width == trace.Bounds.Width, "long II spans the waveform area"); }
                    Directory.CreateDirectory("artifacts");
                    using var image = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                    image.Render((Control)window.Content!);
                    image.Save($"artifacts/monitor-live-12-{rows}-{size.Width:0}.png", PngBitmapEncoderOptions.Default);
                }
            }
            window.Settings.Language.SelectedIndex = 0;
            Require(AutomationProperties.GetName(skin.EcgButton) == "12 leads (selected)" && session.SimulationTimeNs == time,
                "selected state localizes without advancing paused data");
            window.SelectPage(1);
            Require(window.CurrentPaper is not null, "sidebar still opens captured paper");
            window.SelectPage(0);
            Require(trace.IsVisible && skin.TwelveLeadVisible, "returning to the monitor retains its live layout");
            skin.EcgButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!trace.IsVisible && window.MonitorTrace.IsVisible && session.Display.Slots.SequenceEqual(slots),
                "second click restores the original waveform slots");
            skin.EcgButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.ApplySettings();
            Require(window.MonitorView.TwelveLeadTrace.IsVisible && ReferenceEquals(session, window.Session),
                "applying settings preserves the live view and session");
            window.RestartSettings();
            Require(window.MonitorView.TwelveLeadTrace.IsVisible && !ReferenceEquals(session, window.Session),
                "explicit restart preserves the chosen view on the new session");
            skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            var display = window.Session.Display;
            window.Session.UpdateDisplay(new(display.Skin, display.Slots.Select(slot => slot.Channel == 0 ? slot with { Channel = 1 } : slot).ToArray()));
            window.MonitorView.RefreshDisplay();
            skin.SetTwelveLeadVisible(false);
            Require(!window.MonitorTrace.MonitoredChannels.HasFlag(MonitorChannels.Ecg), "ordinary non-ECG slots do not monitor ECG");
            skin.EcgButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.MonitorTrace.MonitoredChannels.HasFlag(MonitorChannels.Ecg), "live twelve-lead view connects ECG monitoring even without an ordinary ECG slot");
        }
        finally { window.Close(); }
    }

    private static void VerifySources()
    {
        for (int selection = 0; selection < DesignPreviewSettings.EcgChoiceCount; selection++)
        {
            var (physiology, ecg) = DesignPreviewWindow.ResolveStyle(selection, 0, 0);
            var live = LiveEcgIllustrationSource.Create(physiology);
            var reference = ElectrodeSignalGenerator.Restore(ProjectedEcgDemoSource.Create(ecg).CaptureState().Generator);
            for (long time = 50_000_000; time <= 2_000_000_000; time += 50_000_000)
            {
                var actual = live.GenerateBefore(time, 50, 100);
                var expected = reference.GenerateBefore(time, 50, 100);
                Require(actual.Count == expected.Count && actual.Zip(expected).All(pair =>
                    pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)), $"template {selection} retains all twelve authored lead shapes");
            }
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) { throw new InvalidOperationException("Live 12-lead: " + message); }
    }
}
