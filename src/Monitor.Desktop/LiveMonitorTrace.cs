// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class LiveMonitorTrace(LocalMonitorPreviewSession session) : Control
{
    internal static readonly string[] Names = ["ECG · II", "RESP", "PLETH", "ABP", "CO₂", "PA", "CVP"];
    internal static readonly string[] Units = ["μV", "相对量", "相对量", "mmHg", "mmHg", "mmHg", "mmHg"];
    internal static readonly string[] Colors = ["#71E9AF", "#F0D68A", "#8ADAE5", "#F39199", "#E7ECF2", "#CAA7EA", "#F2B67D"];
    private ulong _revision = ulong.MaxValue;
    private long _cycle = -1;
    private StreamGeometry?[,] _paths = new StreamGeometry?[0, 0];
    internal LocalMonitorPreviewSession Session => session;
    private void Build()
    {
        if (_revision == session.DataRevision && _cycle == session.Ranges.Cycle) { return; }
        _revision = session.DataRevision; _cycle = session.Ranges.Cycle;
        _paths = new StreamGeometry?[session.Display.Slots.Count, 2];
        for (int row = 0; row < session.Display.Slots.Count; row++)
            for (int age = 0; age < 2; age++)
            {
                long cycle = _cycle - age;
                if (cycle < 0 || (age == 1 && !session.Ranges.ShowPrevious(row))) { continue; }
                long from = cycle * MonitorDisplayConfiguration.SweepDurationNs;
                var path = new StreamGeometry();
                using (var geometry = path.Open())
                {
                    bool started = false;
                    int channel = session.Display.Slots[row].Channel;
                    var samples = session.Samples(channel, from, from + MonitorDisplayConfiguration.SweepDurationNs).ToArray();
                    double tolerance = channel is 3 or 4 or 5 && samples.Length > 0
                        ? Math.Min(.75, (samples.Max(s => s.Value) - samples.Min(s => s.Value)) * .015) : 0;
                    foreach (var sample in PreviewContour.Simplify(samples, tolerance))
                    {
                        Point point = new((sample.TimeNs - from) / (double)MonitorDisplayConfiguration.SweepDurationNs,
                            1 - session.Ranges.Range(row).Normalize(sample.Value));
                        if (!started) { geometry.BeginFigure(point, false); started = true; }
                        else { geometry.LineTo(point); }
                    }
                    if (started) { geometry.EndFigure(false); }
                }
                _paths[row, age] = path;
            }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brush.Parse("#101B25"), new Rect(Bounds.Size));
        if (Bounds.Width < 200 || Bounds.Height < 60) { return; }
        Build();
        int rows = session.Display.Slots.Count;
        double rowHeight = Bounds.Height / rows;
        double left = 132, width = Math.Max(1, Bounds.Width - left - 18);
        double phase = (session.FrontierNs % MonitorDisplayConfiguration.SweepDurationNs) / (double)MonitorDisplayConfiguration.SweepDurationNs;
        for (int row = 0; row < rows; row++)
        {
            int channel = session.Display.Slots[row].Channel;
            var color = Brush.Parse(Colors[channel]);
            var range = session.Ranges.Range(row);
            double top = row * rowHeight;
            context.DrawLine(new Pen(Brush.Parse("#607080"), 1), new(0, top + rowHeight - 1), new(Bounds.Width, top + rowHeight - 1));
            Label(context, Names[channel], 12, top + 10, color, 14);
            Label(context, string.Create(CultureInfo.InvariantCulture, $"{range.Minimum:0.##} – {range.Maximum:0.##}"), 12, top + 32, color, 11);
            Label(context, Units[channel] + (session.Display.Slots[row].Automatic ? " · 自动" : " · 固定"), 12, top + 49, color, 11);
            double gutter = width * .2 / 10.2;
            Rect plot = new(left + gutter, top + 9, width - gutter, Math.Max(1, rowHeight - 20));
            if (channel == 0)
            {
                if (range.Minimum <= 0 && range.Maximum >= 1000)
                {
                    double x = left + gutter / 2;
                    context.DrawLine(new Pen(color, 1.2), new(x, plot.Bottom - range.Normalize(0) * plot.Height),
                        new(x, plot.Bottom - range.Normalize(1000) * plot.Height));
                    Label(context, "1 mV", 12, top + 66, color, 11);
                }
                else { Label(context, "1 mV 超量程", 12, top + 66, color, 11); }
            }
            for (int age = 1; age >= 0; age--)
            {
                if (_paths[row, age] is not { } path) { continue; }
                double begin = age == 0 ? 0 : Math.Min(1, phase + .012);
                double end = age == 0 ? phase : 1;
                if (end <= begin) { continue; }
                using var clip = context.PushClip(new Rect(plot.X + begin * plot.Width, plot.Y, (end - begin) * plot.Width, plot.Height));
                // Geometry is cached in normalized units. Scale with the window;
                // draw a device-space stroke so resize does not thicken the trace.
                var matrix = new Matrix(plot.Width, 0, 0, plot.Height, plot.X, plot.Y);
                if (path.Transform is null || !path.Transform.Value.Equals(matrix)) { path.Transform = new MatrixTransform(matrix); }
                context.DrawGeometry(null, new Pen(color, 1.2), path);
            }

        }
    }
    private static void Label(DrawingContext context, string value, double x, double y, IBrush color, double size) =>
        context.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(DesignPreviewWindow.PreviewFont), size, color), new(x, y));
}
