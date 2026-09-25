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
    private readonly bool _paper;
    internal int BlockCount => _blocks.Length;
    internal const double PaperWidth = 1184;
    internal const double PixelsPerSecond = 100;
    internal const double PixelsPerMillivolt = 40;
    internal DesignPreviewTrace(WaveformEnvelope[] blocks, bool paper)
    {
        _blocks = blocks;
        _paper = paper;
        Width = paper ? PaperWidth : 920;
        Height = paper ? 620 : 616;
        Avalonia.Automation.AutomationProperties.SetName(this, paper
            ? "十二导联十秒合成快照，三行四列加II导联节律条" : "七通道六秒合成波形快照，测量数值未启用");
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(_paper ? Brushes.White : Brush.Parse("#101B25"), new Rect(Bounds.Size));
        using var clip = context.PushClip(new Rect(Bounds.Size));
        if (_paper) { DrawPaper(context); } else { DrawMonitor(context); }
    }
    private void DrawPaper(DrawingContext context)
    {
        for (int x = 32; x <= 1152; x += 4)
        { context.DrawLine(new Pen(Brush.Parse((x - 32) % 20 == 0 ? "#E5A8B4" : "#F4DCE2"), .6), new(x, 52), new(x, 580)); }
        for (int y = 52; y <= 580; y += 4)
        { context.DrawLine(new Pen(Brush.Parse((y - 52) % 20 == 0 ? "#E5A8B4" : "#F4DCE2"), .6), new(32, y), new(1152, y)); }
        Label(context, "十二导联心电图", 32, 15, Brushes.Black, 17);
        Label(context, "25 mm/s    10 mm/mV    ·    10 s 合成快照", 675, 18, Brushes.Black, 14);
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 3; row++)
            {
                int lead = column * 3 + row;
                double x = 32 + column * 280;
                double baseline = 136 + row * 120;
                Label(context, ProjectedEcgDemoSource.LeadNames[lead], x + 4, baseline - 67, Brushes.Black, 15);
                Calibration(context, x + 3, baseline);
                DrawSamples(context, ProjectedEcgDemoSource.ChannelId((EcgLead)lead), column * 2_500_000_000L,
                    (column + 1) * 2_500_000_000L, x + 30, baseline, PixelsPerSecond, .04, Brushes.Black);
            }
        Label(context, "II", 36, 465, Brushes.Black, 15);
        Calibration(context, 35, 536);
        DrawSamples(context, ProjectedEcgDemoSource.ChannelId(EcgLead.II), 0, 10_000_000_000L, 62, 536, PixelsPerSecond, .04, Brushes.Black);
        Label(context, "固定纸格比例 · 屏幕物理毫米未校准 · 监护采样快照，非诊断记录", 32, 592, Brush.Parse("#535E69"), 12);
    }
    private static void Calibration(DrawingContext context, double x, double y)
    {
        Point[] points = [new(x, y), new(x + 3, y), new(x + 3, y - PixelsPerMillivolt),
            new(x + 23, y - PixelsPerMillivolt), new(x + 23, y), new(x + 26, y)];
        for (int i = 1; i < points.Length; i++) { context.DrawLine(new Pen(Brushes.Black, 1), points[i - 1], points[i]); }
    }
    private void DrawMonitor(DrawingContext context)
    {
        string[] names = ["ECG · II", "RESP", "PLETH", "ABP", "CO₂", "PA", "CVP"];
        string[] units = ["μV", "相对量", "相对量", "mmHg", "mmHg", "mmHg", "mmHg"];
        string[] colors = ["#71E9AF", "#F0D68A", "#8ADAE5", "#F39199", "#E7ECF2", "#CAA7EA", "#F2B67D"];
        double[] low = [-1200, -1200, 0, 0, 0, 0, -5];
        double[] high = [1500, 1200, 1600, 160, 80, 40, 15];
        for (int row = 0; row < 7; row++)
        {
            double top = row * 88;
            var color = Brush.Parse(colors[row]);
            Label(context, names[row], 16, top + 14, color, 14);
            Label(context, units[row], 16, top + 40, Brush.Parse("#AAB8C5"), 12);
            context.DrawLine(new Pen(Brush.Parse("#293644"), .6), new(114, top + 84), new(900, top + 84));
            double factor = 60 / (high[row] - low[row]);
            DrawSamples(context, PhysiologyDemoSource.ChannelId(row), 0, 6_000_000_000L,
                128, top + 72 + low[row] * factor, PixelsPerSecond, factor, color, physical: true);
            Label(context, "—", 805, top + 18, color, 24);
            Label(context, "未启用测量", 794, top + 51, Brush.Parse("#AAB8C5"), 12);
        }
    }
    private void DrawSamples(DrawingContext context, Guid channel, long from, long to,
        double left, double baseline, double speed, double gain, IBrush color, bool physical = false)
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
                if (physical) { value = value * plane.ScaleNumerator / plane.ScaleDenominator + (double)plane.OffsetNumerator / plane.OffsetDenominator; }
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
