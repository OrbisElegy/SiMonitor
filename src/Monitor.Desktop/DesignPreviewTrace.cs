// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Frozen design-preview samples only. No detector, live clock or diagnostic export.
internal sealed class DesignPreviewTrace : Control
{
    private readonly WaveformEnvelope[] _blocks;
    internal int BlockCount => _blocks.Length;
    internal const double PaperWidth = 1094;
    internal const double PixelsPerSecond = 100;
    internal const double PixelsPerMillivolt = 40;
    internal DesignPreviewTrace(WaveformEnvelope[] blocks)
    {
        _blocks = blocks;
        Width = PaperWidth;
        Height = 596;
        Avalonia.Automation.AutomationProperties.SetName(this,
            "十二导联十秒合成快照，三行四列加II导联节律条");
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        using var clip = context.PushClip(new Rect(Bounds.Size));
        DrawPaper(context);
    }
    private void DrawPaper(DrawingContext context)
    {
        for (int x = 32; x <= 1062; x += 4)
        { context.DrawLine(new Pen(Brush.Parse((x - 32) % 20 == 0 ? "#E5A8B4" : "#F4DCE2"), .6), new(x, 52), new(x, 580)); }
        for (int y = 52; y <= 580; y += 4)
        { context.DrawLine(new Pen(Brush.Parse((y - 52) % 20 == 0 ? "#E5A8B4" : "#F4DCE2"), .6), new(32, y), new(1062, y)); }
        Label(context, "25 mm/s    ·    10 mm/mV    ·    10 s", 32, 18, Brushes.Black, 14);
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 3; row++)
            {
                int lead = column * 3 + row;
                double x = 62 + column * 250;
                double baseline = 136 + row * 120;
                Label(context, ProjectedEcgDemoSource.LeadNames[lead], x + 4, baseline - 67, Brushes.Black, 15);
                if (column == 0) { Calibration(context, 35, baseline); }
                DrawSamples(context, ProjectedEcgDemoSource.ChannelId((EcgLead)lead), column * 2_500_000_000L,
                    (column + 1) * 2_500_000_000L, x, baseline, PixelsPerSecond, .04, Brushes.Black);
            }
        Label(context, "II", 36, 465, Brushes.Black, 15);
        Calibration(context, 35, 536);
        DrawSamples(context, ProjectedEcgDemoSource.ChannelId(EcgLead.II), 0, 10_000_000_000L, 62, 536, PixelsPerSecond, .04, Brushes.Black);
    }
    private static void Calibration(DrawingContext context, double x, double y)
    {
        Point[] points = [new(x, y), new(x + 3, y), new(x + 3, y - PixelsPerMillivolt),
            new(x + 23, y - PixelsPerMillivolt), new(x + 23, y), new(x + 26, y)];
        for (int i = 1; i < points.Length; i++) { context.DrawLine(new Pen(Brushes.Black, 1), points[i - 1], points[i]); }
    }
    private void DrawSamples(DrawingContext context, Guid channel, long from, long to,
        double left, double baseline, double speed, double gain, IBrush color)
    {
        Point? previous = null;
        long previousTime = -1;
        var pen = new Pen(color, 1.15);
        foreach (var block in _blocks)
        {
            var plane = block.Planes.Single(p => p.ChannelId == channel);
            long step = checked(1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator);
            for (int i = 0; i < plane.Samples.Count; i++)
            {
                long time = block.StartSimTimeNs + i * step;
                if (time < from || time >= to) { continue; }
                double value = plane.Samples[i];
                Point point = new(left + (time - from) / 1e9 * speed, baseline - value * gain);
                if (previous is { } p && time - previousTime == step) { context.DrawLine(pen, p, point); }
                previous = point;
                previousTime = time;
            }
        }
    }
    private static void Label(DrawingContext context, string text, double x, double y, IBrush color, double size) =>
        context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(DesignPreviewWindow.PreviewFont), size, color), new(x, y));
}
