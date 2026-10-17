// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// The same editable defaults feed cards and the applied source. The morphology
// remains an independent selection when the user subsequently adjusts levels.
internal static class SignalTemplatePresets
{
    internal const long PreviewDurationNs = 30_000_000_000;

    internal static bool SupportsCustomPlateau(int index) => index is 0 or 8 or 9 or 10;

    internal static PhysiologyDemoConfiguration Capnogram(int index)
    {
        if (index < 0 || index >= DesignPreviewSettings.CapnogramChoiceCount)
        { throw new ArgumentOutOfRangeException(nameof(index)); }
        return PhysiologyDemoConfiguration.Default with
        {
            CapnogramIllustration = (CapnogramIllustration)index,
            Co2BaselineMmHg = index switch { 8 => 5, 9 => 8, _ => 0 },
            Co2RiseMilliseconds = index == 11 ? 600 : 250,
            Co2FallMilliseconds = index == 11 ? 600 : 200
        };
    }

    internal static (long TimeNs, double Value)[] CreatePreview(int channel, int index)
    {
        var configuration = channel switch
        {
            2 => DesignPreviewWindow.ResolveStyle(0, 0, 0, index).Physiology,
            4 => Capnogram(index),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
        var source = PhysiologyDemoSource.Create(configuration);
        List<(long, double)> samples = [];
        for (long end = 200_000_000; end <= PreviewDurationNs; end += 200_000_000)
        {
            foreach (byte[] wire in source.AdvanceTo(end, 50, 1, 100))
            {
                var block = WaveformEnvelopeCodec.Decode(wire);
                var plane = block.Planes.Single(p => p.ChannelId == PhysiologyDemoSource.ChannelId(channel));
                long interval = 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
                for (int i = 0; i < plane.Samples.Count; i++)
                {
                    samples.Add((block.StartSimTimeNs + i * interval,
                        (double)plane.Samples[i] * plane.ScaleNumerator / plane.ScaleDenominator + (double)plane.OffsetNumerator / plane.OffsetDenominator));
                }
            }
        }
        return samples.ToArray();
    }
}

internal sealed class SignalTemplateThumbnail(int channel, int index, DesktopLocalization localization) : Control
{
    private StreamGeometry? _geometry;
    protected override Size MeasureOverride(Size availableSize) => new(210, 74);

    public override void Render(DrawingContext context)
    {
        if (_geometry is null)
        {
            var samples = StylePreviewCatalog.Signal(channel, index);
            _geometry = new StreamGeometry();
            using var path = _geometry.Open();
            // A shared vertical scale keeps low perfusion and raised inspired CO2
            // visible; fitting every card independently would hide those changes.
            double minimum = channel == 2 ? -250 : 0;
            double span = channel == 2 ? 1750 : 80;
            long duration = channel == 2 ? 6_000_000_000 : SignalTemplatePresets.PreviewDurationNs;
            var shown = samples.Where(s => s.TimeNs < duration).ToArray();
            for (int i = 0; i < shown.Length; i++)
            {
                var sample = shown[i];
                Point point = new(sample.TimeNs / (double)duration * 210,
                    8 + (1 - Math.Clamp((sample.Value - minimum) / span, 0, 1)) * 55);
                if (i == 0) { path.BeginFigure(point, false); }
                else { path.LineTo(point); }
            }
            if (shown.Length > 0) { path.EndFigure(false); }
        }
        context.DrawGeometry(null, new Pen(Brush.Parse(LiveMonitorTrace.Colors[channel]), 1.2), _geometry);
        string caption = localization.Get(channel == 2 ? "generation.thumbnailPleth" : "generation.thumbnailCapnogram");
        context.DrawText(new FormattedText(caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(DesignPreviewWindow.PreviewFont), 10, Brushes.White), new Point(0, 64));
    }
}
