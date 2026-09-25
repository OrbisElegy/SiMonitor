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
    internal bool SixRows { get; }
    internal double PaperWidth => SixRows ? 1124 : 1184;
    private int Rows => SixRows ? 6 : 3;
    private int Columns => SixRows ? 2 : 4;
    private int ColumnWidth => SixRows ? 530 : 280;
    internal long LongDurationNs => SixRows ? 10_300_000_000 : 10_900_000_000;
    internal const double PixelsPerSecond = 100;
    internal const double PixelsPerMillivolt = 40;
    internal DesignPreviewTrace(WaveformEnvelope[] blocks, bool sixRows = false)
    {
        _blocks = blocks; SixRows = sixRows;
        Width = PaperWidth;
        Height = SixRows ? 956 : 596;
        Avalonia.Automation.AutomationProperties.SetName(this,
            sixRows ? "十二导联六行两列，五秒短导联及10.3秒长II" : "十二导联三行四列，2.5秒短导联及10.9秒长II");
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
        for (int x = 32; x <= PaperWidth - 32; x += 4)
        { context.DrawLine(new Pen(Brush.Parse((x - 32) % 20 == 0 ? "#E5A8B4" : "#F4DCE2"), .6), new(x, 52), new(x, Height - 16)); }
        for (int y = 52; y <= Height - 16; y += 4)
        { context.DrawLine(new Pen(Brush.Parse((y - 52) % 20 == 0 ? "#E5A8B4" : "#F4DCE2"), .6), new(32, y), new(PaperWidth - 32, y)); }
        Label(context, SixRows ? "25 mm/s · 10 mm/mV · 短导联 5 s / 长Ⅱ 10.3 s" : "25 mm/s · 10 mm/mV · 短导联 2.5 s / 长Ⅱ 10.9 s", 32, 18, Brushes.Black, 10);
        for (int column = 0; column < Columns; column++)
            for (int row = 0; row < Rows; row++)
            {
                int lead = column * Rows + row;
                double x = 32 + column * ColumnWidth;
                double baseline = 136 + row * 120;
                Label(context, ProjectedEcgDemoSource.LeadNames[lead], x + 4, baseline - 67, Brushes.Black, 15);
                Calibration(context, x + 26, baseline);
                DrawSamples(context, ProjectedEcgDemoSource.ChannelId((EcgLead)lead), column * (10_000_000_000L / Columns),
                    (column + 1) * (10_000_000_000L / Columns), x + 30, baseline, PixelsPerSecond, .04, Brushes.Black);
            }
        Label(context, "II", 36, Height - 131, Brushes.Black, 15);
        Calibration(context, 58, Height - 60);
        DrawSamples(context, ProjectedEcgDemoSource.ChannelId(EcgLead.II), 0, LongDurationNs, 62, Height - 60, PixelsPerSecond, .04, Brushes.Black);
    }
    private static void Calibration(DrawingContext context, double x, double y)
    {
        // Paper ECG uses a 200ms-wide, 1mV-high square pulse for each lead.
        // This is deliberately distinct from the monitor's overlay line.
        var pen = new Pen(Brushes.Black, 1);
        double start = x - .2 * PixelsPerSecond;
        context.DrawLine(pen, new(start, y), new(start, y - PixelsPerMillivolt));
        context.DrawLine(pen, new(start, y - PixelsPerMillivolt), new(x, y - PixelsPerMillivolt));
        context.DrawLine(pen, new(x, y - PixelsPerMillivolt), new(x, y));
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
