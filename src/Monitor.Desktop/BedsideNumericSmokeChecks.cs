// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class BedsideNumericSmokeChecks
{
    private static readonly int[] Channels = [0, 2, 1, 4, 3, 5, 6];
    private static readonly string[] Samples = ["75", "98", "16", "173", "888", "---", "-?-"];

    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        window.Pause();
        try
        {
            window.ChangeMonitorSkin(MonitorSkinPackage.BedsideMonitor);
            foreach (var layout in new[] { MonitorSkin.ThreeRows, MonitorSkin.FourRows, MonitorSkin.FiveRows, MonitorSkin.SevenRows })
            {
                int rows = MonitorDisplayConfiguration.RowCount(layout);
                var slots = Channels.Take(rows).Select(channel => new MonitorDisplaySlot(channel, true,
                    MonitorDisplayConfiguration.ReferenceRange(channel))).ToArray();
                window.Session.UpdateDisplay(new(layout, slots));
                window.MonitorView.RefreshDisplay();
                var primary = window.MonitorView.NumericBlocks.Where((_, index) => index % 2 == 0).ToArray();
                Require(primary.Length == rows, $"{rows} waveform rows have their own primary numeric controls");
                var nibp = window.MonitorView.ManualTiles[3];
                nibp.Update("NIBP", "120/80", "00:35", "(93)");
                foreach (var size in new[] { new Size(960, 640), new Size(1440, 940) })
                {
                    foreach (string sample in Samples)
                    {
                        // These fixtures are isolated from the simulated readings and saved skin screenshots.
                        foreach (var text in primary) { text.Text = sample; }
                        DesktopViewportSmokeChecks.Layout(window, size.Width, size.Height);
                        string context = $"{rows} rows at {size.Width:0}x{size.Height:0}, reading {sample}";
                        foreach (var text in primary)
                        {
                            VerifyInk(text, context);
                            var row = text.GetVisualAncestors().OfType<Grid>().First(grid => grid.ClipToBounds);
                            var railTexts = row.GetVisualDescendants().OfType<TextBlock>()
                                .Where(block => block.Text?.Contains('\n') == true).ToArray();
                            Require(railTexts.Length == 2, "each numeric row displays a two-line label and limits");
                            foreach (var railText in railTexts) { VerifyInk(railText, context + ", " + railText.Text, 2); }
                        }
                        var cuffValues = nibp.GetVisualDescendants().OfType<TextBlock>()
                            .Where(text => text.FontSize >= 40 && text.IsEffectivelyVisible).ToArray();
                        Require(cuffValues.Length == 2 && cuffValues.Any(text => text.Text == "120/80") &&
                            cuffValues.Any(text => text.Text == "(93)"), "the cuff value and mean are both displayed");
                        foreach (var text in cuffValues) { VerifyInk(text, context + ", cuff " + text.Text); }
                    }
                }
            }
            VerifyAccessibleReadings(window);
        }
        finally { window.Close(); }
        Console.WriteLine("ok: bedside numeric glyphs and cuff values fit their text bounds and clipping ancestors across all row counts and viewports");
    }

    private static void VerifyAccessibleReadings(DesignPreviewWindow window)
    {
        var view = window.MonitorView;
        view.RefreshDisplay();
        TextBlock Reading(string key) => view.NumericBlocks.Single(text =>
            AutomationProperties.GetName(text) == window.Localization.Get(key));
        var spo2 = Reading("monitor.labelSpo2");
        var pulse = Reading("monitor.secondaryPr");
        string? previous = AutomationProperties.GetHelpText(spo2);
        window.Start();
        for (int frame = 0; frame < 160; frame++) { window.Pulse(window.ActiveTimer, 50_000_000); }
        window.Pause();
        var row = spo2.GetVisualAncestors().OfType<Grid>().First(grid => grid.ClipToBounds);
        var details = (StackPanel)ToolTip.GetTip(row)!;
        var pi = details.Children.OfType<TextBlock>().Single();
        string help = AutomationProperties.GetHelpText(spo2) ?? "";
        Require(help != previous && help.StartsWith(spo2.Text!, StringComparison.Ordinal) &&
            help.Contains(pulse.Text!, StringComparison.Ordinal) && help.Contains(pi.Text!, StringComparison.Ordinal),
            "the existing primary value exposes current SpO2, pulse rate and PI as acquisition advances");
        Require(!row.Focusable && !spo2.Focusable && !ToolTip.GetIsOpen(row),
            "accessible auxiliary readings add no keyboard stops or visible tooltip");
        var co2 = Reading("monitor.labelEtco2");
        string preference = window.Localization.Preference;
        try
        {
            window.Localization.Select("en");
            Require(ReferenceEquals(co2, Reading("monitor.labelEtco2")) &&
                AutomationProperties.GetHelpText(co2)!.EndsWith("/min", StringComparison.Ordinal),
                "the same primary value follows the English auxiliary respiration units while paused");
            window.Localization.Select("zh-TW");
            Require(AutomationProperties.GetHelpText(co2)!.EndsWith("次/分", StringComparison.Ordinal),
                "auxiliary automation text follows a subsequent language change without reacquisition");
        }
        finally { window.Localization.Select(preference); }
    }

    private static void VerifyInk(TextBlock text, string context, int lines = 1)
    {
        var layout = text.TextLayout;
        using var natural = new TextLayout(text.Text ?? "", new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, Brushes.White);
        double inkBottom = layout.Height + layout.OverhangAfter;
        double inkTop = inkBottom - layout.Extent;
        Require(text.Bounds.Width > 0 && text.Bounds.Height > 0 && layout.TextLines.Count == lines && layout.Extent > 0 &&
            Math.Abs(layout.Extent - natural.Extent) <= 1,
            $"{context}: all lines retain their natural ink height");
        Require(inkTop >= -1 && inkBottom <= text.Bounds.Height + 1,
            $"{context}: glyph ink [{inkTop:0.##}, {inkBottom:0.##}] exceeds text height {text.Bounds.Height:0.##}");
        var ink = new Rect(0, inkTop, layout.Width, layout.Extent);
        // Native window bounds can be smaller than the explicit smoke-test viewport.
        // Check every clipping ancestor inside that viewport, including the scaled monitor screen.
        foreach (var ancestor in text.GetVisualAncestors().TakeWhile(ancestor => ancestor is not Window))
        {
            if (!ancestor.ClipToBounds) { continue; }
            var topLeft = text.TranslatePoint(ink.TopLeft, ancestor);
            var bottomRight = text.TranslatePoint(ink.BottomRight, ancestor);
            Require(topLeft is { } first && bottomRight is { } last &&
                first.X >= -1 && first.Y >= -1 && last.X <= ancestor.Bounds.Width + 1 && last.Y <= ancestor.Bounds.Height + 1,
                $"{context}: glyph ink {topLeft}..{bottomRight} exceeds clipping {ancestor.GetType().Name} {ancestor.Bounds.Size}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Bedside numeric glyphs: " + message); }
    }
}
