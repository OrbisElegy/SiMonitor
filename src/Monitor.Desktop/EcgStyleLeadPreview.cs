// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed class EcgStyleLeadPreview : Control
{
    private readonly StreamGeometry _path = new();
    private EcgStyleLeadPreview(WaveformEnvelope[] blocks, EcgLead lead)
    {
        Width = 206; Height = 80;
        using var geometry = _path.Open();
        bool started = false;
        foreach (var block in blocks.Where(b => b.StartSimTimeNs < 3_000_000_000))
        {
            var plane = block.Planes.Single(p => p.ChannelId == ProjectedEcgDemoSource.ChannelId(lead));
            for (int i = 0; i < plane.Samples.Count; i++)
            {
                long time = block.StartSimTimeNs + i * 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
                if (time >= 3_000_000_000) { break; }
                Point point = new(time / 3e9 * 206, 48 - plane.Samples[i] * .025);
                if (!started) { geometry.BeginFigure(point, false); started = true; } else { geometry.LineTo(point); }
            }
        }
        if (started) { geometry.EndFigure(false); }
    }
    public override void Render(DrawingContext context)
    {
        using var clip = context.PushClip(new Rect(Bounds.Size));
        context.DrawGeometry(null, new Pen(Brush.Parse("#71E9AF"), 1), _path);
    }
    internal static Control Create(WaveformEnvelope[] blocks)
    {
        var panel = new StackPanel { Spacing = 8 };
        for (int i = 0; i < 12; i++)
        {
            var row = new StackPanel();
            row.Children.Add(new TextBlock { Text = ProjectedEcgDemoSource.LeadNames[i], Foreground = Brushes.White });
            row.Children.Add(new EcgStyleLeadPreview(blocks, (EcgLead)i));
            panel.Children.Add(new Border { Background = Brushes.Black, Padding = new Thickness(8), Child = row });
        }
        return panel;
    }
}
