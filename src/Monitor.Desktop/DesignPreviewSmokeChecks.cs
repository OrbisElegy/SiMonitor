// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Monitor.Desktop;

internal static class DesignPreviewSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.UpdateLayout();
            Require(window.Page == 1 && window.CurrentTrace?.BlockCount == 50, "complete ten-second paper snapshot");
            double paperWidth = window.CurrentTrace!.Width;
            Capture(window, "ui-preview-paper.png");
            window.Width = 1000; window.Height = 720; window.UpdateLayout();
            Require(window.CurrentTrace.Width == paperWidth && paperWidth == DesignPreviewTrace.PaperWidth, "resize preserves paper scale");
            Capture(window, "ui-preview-compact.png");
            window.Width = 1440; window.Height = 940;
            window.SelectPage(0); window.UpdateLayout();
            Require(window.CurrentTrace?.BlockCount == 30, "complete six-second seven-channel snapshot");
            Capture(window, "ui-preview-monitor.png");
            window.SelectPage(2); window.UpdateLayout();
            Require(window.CurrentTrace is null && !window.Generate.IsEnabled && !window.Preset.IsEnabled, "unavailable audio has an explicit empty state");
            Capture(window, "ui-preview-audio.png");
            window.SelectPage(1);
            foreach (int preset in new[] { 1, 2, 0 })
            {
                window.Preset.SelectedIndex = preset;
                window.Regenerate();
                Require(window.CurrentTrace?.BlockCount == 50, "switching examples builds a complete snapshot");
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native first-round UI preview, source snapshots, navigation and fixed paper sizing");
    }
    private static void Capture(DesignPreviewWindow window, string name)
    {
        Directory.CreateDirectory("artifacts");
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        var root = (Control)window.Content!;
        root.InvalidateMeasure();
        root.Measure(new Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        image.Render(root);
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Design preview: " + message); }
    }
}
