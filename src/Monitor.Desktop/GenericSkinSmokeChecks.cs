// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
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
            Require(buttons.Length == 13 && buttons.All(button => button.MinHeight >= 44), "dummy actions retain keyboard and pointer targets");
            foreach (var button in buttons) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            Require(session.SimulationTimeNs == time && session.ManualVitals == manual && window.ActiveTimer is null &&
                ReferenceEquals(session, window.Session), "dummy buttons do not mutate the paused session");
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            Require(skin.Feedback.Text!.Contains("功能尚未接入", StringComparison.Ordinal), "dummy feedback is explicit");
            window.Settings.Language.SelectedIndex = 0;
            Require(view.ManualNumericTexts.All(string.IsNullOrEmpty) &&
                skin.Feedback.Text!.Contains("not connected yet", StringComparison.Ordinal), "paused skin placeholders and feedback localize");
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
            foreach (var size in new[] { new Size(1440, 940), new Size(960, 640) })
            {
                DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
                using var image = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                image.Render((Control)window.Content!);
                image.Save($"artifacts/generic-skin-live-{size.Width:0}.png", PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
