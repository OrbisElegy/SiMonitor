// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Monitor.Desktop;

internal static class PrintPreviewLayoutSmokeChecks
{
    internal static void Verify(Window window, DesktopLocalization localization, Button save, Button open)
    {
        var root = (Control)window.Content!;
        var preview = root.GetVisualDescendants().OfType<Image>().Single();
        var previewPane = (Control)preview.Parent!;
        var splitter = root.GetVisualDescendants().OfType<GridSplitter>().Single();
        var status = root.GetVisualDescendants().OfType<TextBlock>().Single(text =>
            AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite);
        var close = root.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel);
        string? previousStatus = status.Text;
        string previousPreference = localization.Preference;
        try
        {
            foreach (string locale in new[] { "zh-CN", "en", "zh-TW" })
            {
                localization.Select(locale);
                status.Text = string.Join(" ", Enumerable.Repeat(localization.Get("print.openFailed"), 4));
                DesktopViewportSmokeChecks.Layout(window, window.MinWidth, window.MinHeight);
                double statusBottom = status.TranslatePoint(new Point(0, status.Bounds.Height), root)!.Value.Y;
                Require(status.TextLayout.Height <= status.Bounds.Height + 1,
                    "a wrapped export message remains complete at the minimum window size");
                foreach (var button in new[] { close, open, save })
                {
                    var start = button.TranslatePoint(default, root)!.Value;
                    Require(start.Y >= statusBottom && start.X >= 0 && start.X + button.Bounds.Width <= root.Bounds.Width &&
                        start.Y + button.Bounds.Height <= root.Bounds.Height && button.Bounds.Height >= 44,
                        "export actions remain inside the window and do not overlap status text: " + locale);
                }
                Require(preview.Bounds.Width >= 200 && preview.Bounds.Height > 200,
                    "the print preview retains usable space beside the scrolling form");
                Directory.CreateDirectory("artifacts");
                using var image = new RenderTargetBitmap(new PixelSize((int)window.MinWidth, (int)window.MinHeight), new Vector(96, 96));
                image.Render(root);
                image.Save($"artifacts/{window.GetType().Name}-minimum-{locale}.png", PngBitmapEncoderOptions.Default);
            }
            double before = previewPane.Bounds.Width;
            Require(splitter.Focus(), "the preview divider accepts keyboard focus");
            splitter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            DesktopViewportSmokeChecks.Layout(window, window.MinWidth, window.MinHeight);
            Require(previewPane.Bounds.Width < before && previewPane.Bounds.Width >= 250,
                "the keyboard divider resizes both panes while preserving usable preview space");
            splitter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
        }
        finally
        {
            status.Text = previousStatus;
            localization.Select(previousPreference);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Print preview layout: " + message); }
    }
}
