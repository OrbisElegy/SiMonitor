// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Twelve simultaneous short strips; a fourth monitor row adds a long II strip.
// This is a live monitor surface, independent of the captured ECG paper page.
internal sealed class LiveEcg12Trace : Control
{
    internal const long ShortDurationNs = 2_500_000_000;
    internal const long LongDurationNs = 10_000_000_000;
    private readonly LiveMonitorTrace _monitor;
    private readonly StreamGeometry?[,] _paths = new StreamGeometry?[13, 2];
    private ulong _revision = ulong.MaxValue;
    private long _cycle = -1;
    internal bool ShowRhythmStrip => _monitor.Session.Display.Slots.Count >= 4;
    internal int StripCount => ShowRhythmStrip ? 13 : 12;

    internal LiveEcg12Trace(LiveMonitorTrace monitor, DesktopLocalization localization)
    {
        _monitor = monitor;
        ClipToBounds = true;
        localization.Bind(this, AutomationProperties.NameProperty, "skin.ecg");
    }

    internal Rect StripBounds(int strip)
    {
        double rowHeight = Bounds.Height / (ShowRhythmStrip ? 4 : 3);
        return strip == 12 ? new Rect(0, 3 * rowHeight, Bounds.Width, rowHeight)
            : new Rect(strip / 3 * Bounds.Width / 4, strip % 3 * rowHeight, Bounds.Width / 4, rowHeight);
    }

    private void Build()
    {
        var session = _monitor.Session;
        if (_revision == session.DataRevision && _cycle == session.SimulationTimeNs / ShortDurationNs) { return; }
        _revision = session.DataRevision;
        _cycle = session.SimulationTimeNs / ShortDurationNs;
        for (int strip = 0; strip < 13; strip++)
        {
            long duration = strip == 12 ? LongDurationNs : ShortDurationNs;
            long cycle = session.SimulationTimeNs / duration;
            for (int age = 0; age < 2; age++)
            {
                _paths[strip, age] = null;
                if (cycle < age) { continue; }
                long from = (cycle - age) * duration;
                var path = new StreamGeometry();
                using (var geometry = path.Open())
                {
                    bool started = false;
                    foreach (var sample in session.EcgSamples(strip == 12 ? EcgLead.II : (EcgLead)strip, from, from + duration))
                    {
                        var point = new Point((sample.TimeNs - from) / (double)duration, .5 - sample.Value / 4000);
                        if (!started) { geometry.BeginFigure(point, false); started = true; }
                        else { geometry.LineTo(point); }
                    }
                    if (started) { geometry.EndFigure(false); }
                }
                _paths[strip, age] = path;
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(_monitor.Skin?.Background ?? Brushes.Black, new Rect(Bounds.Size));
        if (Bounds.Width < 120 || Bounds.Height < 90) { return; }
        Build();
        var color = _monitor.ChannelBrush(0);
        var pen = new Pen(color, 1.2);
        long frontier = _monitor.Session.SimulationTimeNs;
        for (int strip = 0; strip < StripCount; strip++)
        {
            var cell = StripBounds(strip);
            long duration = strip == 12 ? LongDurationNs : ShortDurationNs;
            double phase = (frontier % duration) / (double)duration;
            double labelHeight = Math.Clamp(cell.Height * .16, 16, 28);
            Label(context, strip == 12 ? "II" : ProjectedEcgDemoSource.LeadNames[strip],
                new(cell.X + 6, cell.Y + 4), color, Math.Clamp(cell.Height * .1, 11, 18));
            var plot = new Rect(cell.X + 6, cell.Y + labelHeight, Math.Max(1, cell.Width - 12), Math.Max(1, cell.Height - labelHeight - 6));
            for (int age = 1; age >= 0; age--)
            {
                if (_paths[strip, age] is not { } path) { continue; }
                double begin = age == 0 ? 0 : Math.Min(1, phase + 6 / plot.Width);
                double end = age == 0 ? phase : 1;
                if (end <= begin) { continue; }
                using var clip = context.PushClip(new Rect(plot.X + begin * plot.Width, plot.Y, (end - begin) * plot.Width, plot.Height));
                path.Transform = new MatrixTransform(new Matrix(plot.Width, 0, 0, plot.Height, plot.X, plot.Y));
                context.DrawGeometry(null, pen, path);
                foreach (long peak in _monitor.SynchronizationPeaks)
                {
                    double position = (peak - (frontier / duration - age) * duration) / (double)duration;
                    if (position < begin || position >= end) { continue; }
                    double x = plot.X + position * plot.Width;
                    context.DrawLine(new Pen(color, 2), new(x, plot.Y), new(x, plot.Y + 7));
                }
            }
            if (strip == 0)
            {
                // A visible voltage reference scales with the trace; no physical
                // paper speed is claimed for a freely resizable monitor window.
                double x = plot.Right - 18, y = plot.Bottom - 3;
                context.DrawLine(pen, new(x, y), new(x, y - plot.Height / 4));
                context.DrawLine(pen, new(x, y - plot.Height / 4), new(x + 8, y - plot.Height / 4));
                context.DrawLine(pen, new(x + 8, y - plot.Height / 4), new(x + 8, y));
                Label(context, "1 mV", new(cell.Right - 42, cell.Y + 4), color, 10);
            }
        }
    }

    private static void Label(DrawingContext context, string text, Point at, IBrush color, double size) =>
        context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(DesignPreviewWindow.PreviewFont), size, color), at);
}
