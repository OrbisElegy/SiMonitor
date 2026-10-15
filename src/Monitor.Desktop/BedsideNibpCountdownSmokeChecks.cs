// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Monitor.Desktop;

internal static class BedsideNibpCountdownSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.ChangeMonitorSkin(MonitorSkinPackage.BedsideMonitor);
            window.ApplyRecommendedSkinLayout();
            window.Settings.OpticalEnabled.IsChecked = true;
            window.RestartSettings();
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            var countdown = window.MonitorView.ManualTiles[3].Countdown;
            Require(!countdown.IsActive && !countdown.Track.IsVisible && !countdown.RemainingText.IsVisible,
                "manual mode has no automatic countdown");
            skin.NibpInterval.SelectedIndex = 0;
            skin.NibpAutoEnabled.IsChecked = true;
            Require(countdown.IsActive && countdown.RemainingSeconds == 60 && countdown.RemainingFraction == 1,
                "automatic measurement begins with the configured one-minute interval");

            void Advance(int ticks)
            {
                for (int index = 0; index < ticks; index++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            }
            Advance(400);
            window.Pause();
            Require(countdown.RemainingSeconds == 40 && Math.Abs(countdown.RemainingFraction - 2d / 3) < .0001,
                "the remaining bar decreases after measurement without waiting for a new cuff reading");
            DesktopViewportSmokeChecks.Layout(window, 1440, 940);
            Require(Math.Abs(countdown.Fill.Bounds.Width - countdown.Track.Bounds.Width * countdown.RemainingFraction) <= 1,
                "the actual rendered fill follows the remaining interval");
            Directory.CreateDirectory("artifacts");
            using (var screenshot = new RenderTargetBitmap(new PixelSize(1024, 640), new Vector(96, 96)))
            {
                screenshot.Render(skin.Screen!);
                screenshot.Save("artifacts/bedside-countdown.png", PngBitmapEncoderOptions.Default);
            }
            string pausedText = countdown.RemainingText.Text!;
            for (int refresh = 0; refresh < 5; refresh++) { window.MonitorView.Refresh(); }
            Require(countdown.RemainingSeconds == 40 && countdown.RemainingText.Text == pausedText,
                "refreshing a paused simulation cannot advance the countdown");
            window.Start();
            Advance(795);
            Require(countdown.RemainingSeconds == 1 && countdown.RemainingFraction > 0 && countdown.RemainingFraction < .01,
                "fractional seconds round up until the next cycle actually starts");
            Advance(5);
            window.Pause();
            Require(window.Session.Nibp.Measuring && countdown.RemainingSeconds == 60 && countdown.RemainingFraction == 1,
                "the next automatic start resets the bar to the new scheduled interval");
            skin.NibpInterval.SelectedIndex = 1;
            Require(countdown.RemainingSeconds == 120 && countdown.RemainingFraction == 1,
                "changing the interval immediately reflects the host's new scheduled time");
            skin.NibpStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!countdown.IsActive && !countdown.Track.IsVisible && !countdown.RemainingText.IsVisible && countdown.RemainingFraction == 0,
                "stop all removes the automatic countdown instead of leaving a full static line");
            skin.NibpAutoEnabled.IsChecked = true;
            window.Settings.ApplyDelaySeconds.Value = 0;
            window.Settings.ManualVitals.NibpEnabled.IsChecked = false;
            window.ApplySettings();
            window.Start();
            Advance(1);
            Require(!window.MonitorView.ManualTiles[3].Countdown.IsActive && window.MonitorView.ManualNumericTexts[3] == "",
                "disabling NIBP clears both countdown and cuff reading");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Bedside NIBP countdown: " + message); } }
}
