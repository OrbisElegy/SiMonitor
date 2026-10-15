// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class SkinPackageSmokeChecks
{
    private static readonly int[] BedsideChannels = [0, 2, 1];
    private static readonly string[] BedsideReadings = ["75", "98", "16"];
    private static readonly string[] BedsideLimits = ["120\n50", "--\n92", "30\n8"];
    private static readonly string[] AlarmReadings = ["173", "92", "30"];
    private static readonly string[] AlarmLimits = ["160\n50", "--\n95", "25\n8"];
    private static readonly MonitorNumeric[] BedsideNumerics = [MonitorNumeric.HeartRate, MonitorNumeric.SpO2, MonitorNumeric.RespirationRate];
    internal static void Verify()
    {
        VerifyPackages();
        string directory = Path.Combine(Path.GetTempPath(), "monitor-skins-" + Guid.NewGuid().ToString("N"));
        string preferences = Path.Combine(directory, "display.json");
        var window = new DesignPreviewWindow(preferences);
        window.Show();
        try
        {
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.OpticalTarget.Value = 98;
            window.RestartSettings();
            for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Pause();
            var session = window.Session;
            var measurements = session.Measurements;
            long time = session.SimulationTimeNs;
            var display = session.Display;
            window.Settings.MonitorAppearance.SelectedIndex = 1;
            Require(window.MonitorTrace.Skin is { Id: "epm12-inspired" } && ReferenceEquals(session, window.Session) &&
                measurements?.HeartRate == session.Measurements?.HeartRate && ReferenceEquals(display, session.Display) &&
                session.SimulationTimeNs == time && window.ActiveTimer is null, "skin switch preserves the paused acquisition and layout");
            Require(window.MonitorTrace.ChannelBrush(2).ToString() == Brush.Parse("#00DFEF").ToString(), "bedside pleth is cyan");
            window.Settings.RecommendedSkinLayout.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(session.Display.Slots.Select(slot => slot.Channel).SequenceEqual(BedsideChannels), "recommended bedside layout is ECG, PLETH, RESP");
            Require(window.MonitorView.NumericTexts.SequenceEqual(BedsideReadings), "skin uses measured values");
            Require(window.MonitorView.WaveformAlarms is { Rows.Count: 3 } alarms &&
                ReferenceEquals(alarms.Parent, window.MonitorTrace.Parent), "host-owned channel banners share the waveform surface");
            VerifyDisplayedLimits(window);
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            skin.NibpStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(session.Nibp.Measuring, "bedside cuff key starts measurement");
            window.Start();
            for (int i = 0; i < 320; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Pause();
            Require(window.MonitorView.ManualNumericTexts[3].Contains("120/80 (93)", StringComparison.Ordinal), "bedside cuff reading comes from completed measurement");
            foreach (var size in new[] { new Size(1440, 940), new Size(960, 640) })
            {
                DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
                var trace = window.MonitorTrace;
                Require(trace.Bounds.Width > 200 && trace.Bounds.Height > 200, "bedside waveform area fits minimum viewport");
                Point Position(Control control) => control.TranslatePoint(default, window.MonitorView)!.Value;
                var nibp = window.MonitorView.ManualTiles[3];
                double traceBottom = trace.TranslatePoint(new Point(0, trace.Bounds.Height), window.MonitorView)!.Value.Y;
                Require(Position(nibp).Y >= traceBottom - 1 && nibp.Bounds.Width > 250,
                    "large cuff reading stays below the waveform rows");
                Directory.CreateDirectory("artifacts");
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                bitmap.Render((Control)window.Content!);
                bitmap.Save($"artifacts/bedside-skin-{size.Width:0}.png", PngBitmapEncoderOptions.Default);
            }
            SaveScreen(skin, "artifacts/bedside-screen.png");
            skin.SetTwelveLeadVisible(true);
            Require(!((Control)window.MonitorView.WaveformAlarms!.Parent!).IsVisible,
                "switching to twelve-lead ECG hides ordinary waveform alarm headers with their waveforms");
            window.ChangeMonitorSkin(MonitorSkinPackage.Generic);
            Require(((GenericMonitorSkin)window.MonitorTrace.Skin!).TwelveLeadVisible && ReferenceEquals(session, window.Session),
                "live twelve-lead view survives skin changes");
            window.ChangeMonitorSkin(MonitorSkinPackage.BedsideMonitor);
            ((GenericMonitorSkin)window.MonitorTrace.Skin!).SetTwelveLeadVisible(false);
            skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            skin.SilenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.Settings.Sound.AlarmAudioPaused, "bedside silence retains host audio behavior");
            skin.NibpAutoEnabled.IsChecked = true;
            if (!session.Nibp.Measuring) { skin.NibpStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            Require(session.Nibp.AutoEnabled && session.Nibp.Measuring, "cuff controls enable automatic cycles and start measurement");
            skin.NibpStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!session.Nibp.AutoEnabled && !session.Nibp.Measuring && skin.NibpAutoEnabled.IsChecked == false,
                "the stop-all cuff key cancels both active measurement and future automatic cycles");
            skin.MenuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.Page == 2, "the bedside main-menu key opens application settings");
            window.SelectPage(0);
            foreach (string locale in new[] { "en", "zh-TW", "zh-CN" })
            {
                window.Localization.Select(locale);
                Require(window.Settings.ImportSkin.Content?.ToString() == window.Localization.Get("skin.import"), "skin actions localize");
                DesktopViewportSmokeChecks.Layout(window, 960, 640);
            }
            skin.ShowTherapy.IsChecked = true;
            DesktopViewportSmokeChecks.Layout(window, 960, 640);
            Require(skin.TherapyArea!.IsVisible && window.MonitorTrace.Bounds.Width > 200, "bedside layout supports optional therapy controls");
            skin.ShowTherapy.IsChecked = false;
        }
        finally { window.Close(); }
        try
        {
            var restored = new DesignPreviewWindow(preferences);
            try { Require(restored.CurrentSkinPackage.Id == "epm12-inspired", "selected package survives reopening without its source file"); }
            finally { restored.Close(); }
            File.WriteAllText(Path.ChangeExtension(preferences, ".skin.simskin"), "{}", Encoding.UTF8);
            var rejected = new DesignPreviewWindow(preferences);
            try
            {
                Require(rejected.CurrentSkinPackage.Id == "generic" && rejected.Settings.SkinStatus.Text == rejected.Localization.Get("skin.loadFailed"),
                    "invalid saved skin falls back with visible feedback");
            }
            finally { rejected.Close(); }
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
        VerifySimultaneousMeasuredAlarms();
        VerifyTechnicalAlarmRouting();
    }

    private static void VerifyTechnicalAlarmRouting()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            window.Settings.Alerts.EcgMonitoringEnabled.IsChecked = false;
            window.ChangeMonitorSkin(MonitorSkinPackage.BedsideMonitor);
            window.ApplyRecommendedSkinLayout();
            long time = window.Session.SimulationTimeNs;
            var snapshot = LiveWaveformMeasurements.CreateIllustration().Read(time) with
            {
                HeartRate = new(WaveformMeasurementStatus.PoorSignal, null, time),
                SpO2 = new(WaveformMeasurementStatus.PoorSignal, null, null, time),
                PulseRate = new(WaveformMeasurementStatus.Valid, 75000, time),
                ImpedanceRespiration = new(WaveformMeasurementStatus.Valid, 16000, time)
            };
            window.MonitorView.RefreshReadings(snapshot);
            var ecgNotice = window.MonitorView.ActiveNotices.Single(notice => notice.Id == "measurement:HR:PoorSignal");
            var plethNotice = window.MonitorView.ActiveNotices.Single(notice => notice.Id == "measurement:SpO₂:PoorSignal");
            Require(ecgNotice is { Level: MonitorNoticeLevel.Info, Numeric: null } &&
                plethNotice is { Level: MonitorNoticeLevel.Info, Numeric: null } &&
                !window.MonitorView.ActiveNotices.Any(notice => notice.Numeric is MonitorNumeric.HeartRate or MonitorNumeric.SpO2),
                "poor signal remains technical information without publishing a physiological numeric alarm");
            var banners = window.MonitorView.WaveformAlarms!;
            var ecg = banners.Rows.Single(row => row.Channel == 0);
            var pleth = banners.Rows.Single(row => row.Channel == 2);
            Require(ecg.Region.IsVisible && ecg.Notice is { Level: MonitorNoticeLevel.Info, Numeric: MonitorNumeric.HeartRate } &&
                ecg.Notice.Id == ecgNotice.Id && ecg.Text.Text == ecgNotice.Message!.Render(window.Localization.Current) &&
                pleth.Region.IsVisible && pleth.Notice is { Level: MonitorNoticeLevel.Info, Numeric: MonitorNumeric.SpO2 } &&
                pleth.Notice.Id == plethNotice.Id && pleth.Text.Text == plethNotice.Message!.Render(window.Localization.Current),
                "host-local source routing simultaneously places ECG and optical quality notices on their own waveform headers");
        }
        finally { window.Close(); }
    }

    private static void VerifyDisplayedLimits(DesignPreviewWindow window)
    {
        var alerts = window.Settings.Alerts;
        Require(window.MonitorView.BedsideLimitTexts.SequenceEqual(BedsideLimits),
            "the numeric rails show the current per-channel warning limits, including SpO2's absent upper limit");
        alerts.WarningHeartRate.Value = 160;
        alerts.WarningLowHeartRate.Value = 45;
        window.MonitorView.Refresh();
        Require(window.MonitorView.BedsideLimitTexts[0] == "160\n45", "editing HR thresholds updates the screen's upper and lower limits");
        alerts.HeartRateEnabled.IsChecked = false;
        window.MonitorView.Refresh();
        Require(window.MonitorView.BedsideLimitTexts[0] == "--\n--" && window.MonitorView.BedsideLimitTexts[1] == "--\n92",
            "disabled HR limits use placeholders without changing the independent SpO2 limits");
        alerts.HeartRateEnabled.IsChecked = true;
        alerts.WarningHeartRate.Value = 120;
        alerts.WarningLowHeartRate.Value = 50;
        window.MonitorView.Refresh();
        Require(window.MonitorView.BedsideLimitTexts.SequenceEqual(BedsideLimits), "reenabling restores the configured warning limits");
    }

    private static void VerifySimultaneousMeasuredAlarms()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.Localization.Select("zh-CN");
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.HeartRate.Value = 173;
            window.Settings.RespiratoryRate.Value = 30;
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.Oxygenation.Realtime.IsChecked = false;
            window.Settings.OpticalTarget.Value = 92;
            var alerts = window.Settings.Alerts;
            alerts.WarningHeartRate.Value = 160;
            alerts.CriticalHeartRate.Value = 200;
            alerts.WarningSpO2.Value = 95;
            alerts.CriticalSpO2.Value = 85;
            // Isolate the three primary measured conditions from secondary PR and rhythm notices.
            alerts.EcgMonitoringEnabled.IsChecked = false;
            alerts.AdditionalLimits.Editors[MonitorNumeric.PulseRate].Enabled.IsChecked = false;
            var respiration = alerts.AdditionalLimits.Editors[MonitorNumeric.RespirationRate];
            respiration.WarningHigh.Value = 25;
            respiration.CriticalHigh.Value = 40;
            window.ChangeMonitorSkin(MonitorSkinPackage.BedsideMonitor);
            window.ApplyRecommendedSkinLayout();
            window.RestartSettings();
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            skin.NibpStartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int index = 0; index < 600; index++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Pause();
            Require(window.Session.SimulationTimeNs == 30_000_000_000 && window.MonitorView.NumericTexts.SequenceEqual(AlarmReadings),
                $"the reference alarm scene uses 30 seconds of generated ECG, pleth and respiration measurements; " +
                $"actual time={window.Session.SimulationTimeNs} ns, readings={string.Join(", ", window.MonitorView.NumericTexts)}, status={window.Settings.Status.Text}");
            Require(window.MonitorView.BedsideLimitTexts.SequenceEqual(AlarmLimits), "the alarm screenshot displays the actual configured limits");
            var banners = window.MonitorView.WaveformAlarms!;
            Require(banners.Rows.Count == 3 && ReferenceEquals(banners.Parent, window.MonitorTrace.Parent),
                "the active skin receives the host-owned per-waveform alarm layer");
            for (int row = 0; row < BedsideNumerics.Length; row++)
            {
                var banner = banners.Rows[row];
                Require(banner.Channel == BedsideChannels[row] && banner.Region.IsVisible &&
                    banner.Notice is { Level: MonitorNoticeLevel.Warning } notice && notice.Numeric == BedsideNumerics[row] &&
                    window.MonitorView.ActiveNotices.Any(active => active.Id == notice.Id) &&
                    Equals(banner.Region.Background, skin.WarningBrush),
                    "each waveform simultaneously shows its own measured warning in the illuminated phase");
            }
            DesktopViewportSmokeChecks.Layout(window, 1440, 940);
            SaveScreen(skin, "artifacts/bedside-screen-alarms.png");
        }
        finally { window.Close(); }
    }

    private static void SaveScreen(GenericMonitorSkin skin, string path)
    {
        var screen = skin.Screen;
        Require(screen is not null && screen.Bounds.Size == new Size(1024, 640), "the reference monitor retains its fixed screen proportions");
        Directory.CreateDirectory("artifacts");
        using var bitmap = new RenderTargetBitmap(new PixelSize(1024, 640), new Vector(96, 96));
        bitmap.Render(screen!);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static void VerifyPackages()
    {
        foreach (var package in new[] { MonitorSkinPackage.Generic, MonitorSkinPackage.BedsideMonitor })
        {
            using var buffer = new MemoryStream();
            package.Write(buffer);
            buffer.Position = 0;
            var restored = MonitorSkinPackage.Read(buffer);
            Require(restored.Id == package.Id && restored.Bedside == package.Bedside &&
                Enumerable.Range(0, 7).All(channel => restored.ChannelBrush(channel).ToString() == package.ChannelBrush(channel).ToString()),
                "single-file package roundtrip retains all channels");
        }
        using var source = new MemoryStream();
        MonitorSkinPackage.BedsideMonitor.Write(source);
        string valid = Encoding.UTF8.GetString(source.ToArray());
        foreach (string invalid in new[]
        {
            "null", "{}", "[]", valid.Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal),
            valid.Replace("\"version\": 1", "\"version\": 1, \"version\": 1", StringComparison.Ordinal),
            valid.Replace("\"bedside\"", "\"external-code\"", StringComparison.Ordinal),
            valid.Replace("\"background\": \"#080A0B\"", "\"background\": null", StringComparison.Ordinal),
            valid.Replace("#00DFEF", "file:///tmp/skin", StringComparison.Ordinal),
            valid.Replace("{", "{\"script\":\"anything\",", StringComparison.Ordinal),
            new string(' ', MonitorSkinPackage.MaximumBytes + 1)
        })
        {
            bool rejected = false;
            try { using var input = new MemoryStream(Encoding.UTF8.GetBytes(invalid)); _ = MonitorSkinPackage.Read(input); }
            catch (Exception error) when (error is ArgumentException or JsonException) { rejected = true; }
            Require(rejected, "malformed or unsupported package rejected before use");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Skin package: " + message); }
    }
}
