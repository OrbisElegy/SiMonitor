// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Monitor.Application.Presentation;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class LiveMonitorPrintSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            for (int index = 0; index < 160; index++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Localization.Select("zh-CN");
            window.Settings.ApplyDelaySeconds.Value = 0;
            window.Settings.AbpTargetEnabled.IsChecked = false;
            window.Settings.PaTargetEnabled.IsChecked = false;
            window.Settings.EcgSelection = 2;
            window.ApplySettings();
            Require(window.Session.PendingSourceTimeNs == 8_000_000_000, "waveform fixture applies: " + window.Settings.Status.Text);
            for (int index = 0; index < 80; index++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Session.SelectDisplayEcgLead(EcgLead.V1);
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            var timer = window.ActiveTimer;
            skin.PrintButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = window.LivePrintWindow ?? throw new InvalidOperationException("Live Print did not open.");
            try
            {
                Require(dialog.Document.Options.Lead == EcgLead.V1 && !dialog.Document.Options.TwelveLead &&
                    dialog.Document.Options.StartTimeNs == 2_000_000_000 && dialog.Document.EndTimeNs == 12_000_000_000,
                    "ordinary monitor captures the selected lead and the preceding ten seconds");
                Require(dialog.Document.Events.Any(item => item.Kind == PreviewRecordEventKind.LeadChanged && item.TimeNs == 12_000_000_000),
                    "an event exactly at capture time is preserved");
                Require(dialog.Document.Events.Any(item => item.Kind == PreviewRecordEventKind.SourceChanged &&
                    item.Detail.Contains('室')), "waveform selection is printed with its applied template description: " + string.Join("; ", dialog.Snapshot.Events));
                Require(ReferenceEquals(timer, window.ActiveTimer), "opening the nonmodal preview keeps the live timer");
                byte[] before = dialog.Document.PreviewPng(0);
                var frozen = dialog.Snapshot.Leads[(int)EcgLead.II].ToArray();
                window.Pulse(window.ActiveTimer, 50_000_000);
                window.Session.SelectDisplayEcgLead(EcgLead.V6);
                dialog.RefreshPreview();
                Require(before.SequenceEqual(dialog.Document.PreviewPng(0)) &&
                    frozen.SequenceEqual(dialog.Snapshot.Leads[(int)EcgLead.II]) && window.Session.SimulationTimeNs > dialog.Snapshot.CapturedAtNs,
                    "live advancement and lead edits cannot mutate the captured data or output");
                dialog.RecordType.SelectedIndex = 1;
                dialog.Speed.SelectedIndex = 1;
                dialog.Gain.SelectedIndex = 0;
                dialog.Color.SelectedIndex = 1;
                dialog.RefreshPreview();
                Require(dialog.Document.PageCount == 5 && !dialog.Lead.IsEnabled && dialog.Document.Options.GainMmPerMillivolt == 5,
                    "ten seconds at 50 mm/s gives two time segments with two six-lead pages each, plus events");
                Require(LiveMonitorPrintDocument.SampleX(1_000_000_000, 0, 50) - LiveMonitorPrintDocument.TraceLeftMm == 50,
                    "physical paper speed is independent of page or viewport size");
                dialog.Duration.SelectedIndex = 2;
                dialog.RefreshPreview();
                Require(dialog.Document.Options.StartTimeNs == 0 && dialog.Document.EndTimeNs == 12_000_000_000,
                    "insufficient history is not repeated to fill thirty seconds");
                dialog.Page.Value = dialog.Document.PageCount;
                Directory.CreateDirectory("artifacts");
                using (var pdf = File.Create("artifacts/live-monitor-print.pdf")) { dialog.Document.WritePdf(pdf); }
                File.WriteAllBytes("artifacts/live-monitor-print-events.png", dialog.Document.PreviewPng(dialog.Document.PageCount - 1));
                File.WriteAllBytes("artifacts/live-monitor-print-waves.png", dialog.Document.PreviewPng(0));
                DesktopViewportSmokeChecks.Layout(dialog, 1180, 800);
                using (var screenshot = new RenderTargetBitmap(new PixelSize(1180, 800), new Vector(96, 96)))
                {
                    screenshot.Render((Control)dialog.Content!);
                    screenshot.Save("artifacts/live-monitor-print-dialog.png", PngBitmapEncoderOptions.Default);
                }
                var crowded = dialog.Snapshot with
                {
                    Events = Enumerable.Range(0, 75).Select(index => new PreviewRecordEvent(5_000_000_000,
                        PreviewRecordEventKind.SourceChanged, $"event {index}: " + new string('测', 160))).ToArray()
                };
                using var crowdedDocument = new LiveMonitorPrintDocument(crowded, dialog.Document.Options, window.Localization);
                Require(crowdedDocument.Events.Count == 75 && crowdedDocument.PageCount > dialog.Document.PageCount + 4,
                    "dense, long event descriptions wrap and continue on additional pages without losing events");
                dialog.Start.Value = 9;
                dialog.RefreshPreview();
                Require(dialog.Document.Events.All(item => item.TimeNs >= 9_000_000_000),
                    "changing the print interval filters the event report with the waveform interval");
                window.Localization.Select("en");
                dialog.RefreshPreview();
                Require(dialog.Title == "Live monitor print", "the live dialog follows the selected language");
            }
            finally { dialog.Close(); }
            skin.SetTwelveLeadVisible(true);
            skin.PrintButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.LivePrintWindow!.Document.Options.TwelveLead, "twelve-lead view defaults to twelve-lead printing");
            window.LivePrintWindow.Close();
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Live printing: " + message); }
    }
}
