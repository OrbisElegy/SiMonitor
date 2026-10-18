// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;

namespace Monitor.Desktop;

internal static class LiveMonitorTraceSmokeChecks
{
    internal static void Verify()
    {
        MonitorDisplaySlot[] slots = MonitorDisplayConfiguration.Default(MonitorSkin.SevenRows).Slots
            .Select(slot => slot with { Automatic = false, SpeedTenthsMmPerSecond = 125 }).ToArray();
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            new(MonitorSkin.SevenRows, slots));
        while (session.SimulationTimeNs < 26_000_000_000) { session.Advance(50_000_000); }
        var trace = new LiveMonitorTrace(session);
        Layout(trace, 1000, 700);
        _ = CaptureDrawingCount(trace);
        trace = new LiveMonitorTrace(session);
        Layout(trace, 1000, 700);
        long allocatedBeforeCold = GC.GetAllocatedBytesForCurrentThread();
        int drawingCount = CaptureDrawingCount(trace);
        long coldBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeCold;
        Require(drawingCount > slots.Length && drawingCount < slots.Length * 900,
            "long current and previous sweeps bound drawing commands by viewport width");
        byte[] beforeResize = Raster(trace, 1000, 700);
        Require(beforeResize.AsSpan().SequenceEqual(Raster(trace, 1000, 700)) && CaptureDrawingCount(trace) == drawingCount,
            "paused frames preserve their pixels and drawing count");
        _ = Raster(trace, 740, 490);
        byte[] afterResize = Raster(trace, 1000, 700);
        Require(beforeResize.AsSpan().SequenceEqual(afterResize),
            "restoring the viewport restores identical contour pixels and stroke widths");
        VerifyEraseGap(afterResize, 1000, 700, slots, .3);

        session.Advance(50_000_000);
        long allocatedBeforeIncremental = GC.GetAllocatedBytesForCurrentThread();
        _ = CaptureDrawingCount(trace);
        long incrementalBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeIncremental;
        Require(incrementalBytes * 4 < coldBytes * 3,
            $"new samples reuse completed batches instead of reallocating the full sweep: {incrementalBytes} versus {coldBytes} bytes");
        byte[] advanced = Raster(trace, 1000, 700);
        Require(!advanced.AsSpan().SequenceEqual(afterResize), "new samples update cached contours");
        trace.RefreshDisplay();
        Require(advanced.AsSpan().SequenceEqual(Raster(trace, 1000, 700)),
            "display refresh reconstructs identical sweep pixels");

        while (session.SimulationTimeNs < 40_000_000_000) { session.Advance(50_000_000); }
        byte[] wrapped = Raster(trace, 1000, 700);
        Require(CaptureDrawingCount(trace) < slots.Length * 900,
            "drawing commands stay bounded when current sweeps wrap to previous sweeps");
        VerifyEraseGap(wrapped, 1000, 700, slots, 0);
        VerifyCacheInvalidation();
        VerifyRasterReuse();
        trace.ClearWaveformCache();
    }

    private static void VerifyCacheInvalidation()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(MonitorSkin.FourRows), enableTwelveLead: true);
        while (session.SimulationTimeNs < 65_000_000_000) { session.Advance(250_000_000); }
        var trace = new LiveMonitorTrace(session);
        void MatchesFresh(string reason, int width = 1000, int height = 640, double scaling = 1)
        {
            var fresh = new LiveMonitorTrace(session) { Skin = trace.Skin };
            try
            {
                Require(Raster(trace, width, height, scaling).AsSpan().SequenceEqual(Raster(fresh, width, height, scaling)),
                    "cached and fresh traces agree after " + reason);
            }
            finally { fresh.ClearWaveformCache(); }
        }
        MatchesFresh("the first complete sweep");
        session.Advance(16_666_667);
        MatchesFresh("one frame of new data");
        session.SelectDisplayEcgLead(Monitor.Simulation.Physiology.EcgLead.V1);
        trace.RefreshDisplay();
        MatchesFresh("changing the displayed ECG lead");
        session.SelectDisplayEcgLead(Monitor.Simulation.Physiology.EcgLead.II);
        trace.RefreshDisplay();
        MatchesFresh("restoring the displayed ECG lead");
        for (int frame = 0; frame < 4; frame++) { session.Advance(225_000_000); }
        MatchesFresh("a skipped frame interval");

        var changedSlots = session.Display.Slots.Reverse().Select(slot => slot with { Automatic = false }).ToArray();
        session.UpdateDisplay(new(MonitorSkin.FourRows, changedSlots));
        trace.RefreshDisplay();
        MatchesFresh("paused channel reordering");
        changedSlots = changedSlots.Select(slot => slot with
        {
            SpeedTenthsMmPerSecond = 125,
            Range = new(slot.Range.Minimum - 100, slot.Range.Maximum + 200)
        }).ToArray();
        session.UpdateDisplay(new(MonitorSkin.FourRows, changedSlots));
        trace.RefreshDisplay();
        MatchesFresh("paused speed and amplitude changes");

        var skin = new CacheTestSkin();
        trace.Skin = skin;
        MatchesFresh("skin replacement");
        skin.TraceBrush = Brushes.Orange;
        skin.Background = Brushes.MidnightBlue;
        MatchesFresh("in-place skin color changes");
        skin.CompactWaveformLabels = true;
        MatchesFresh("compact label layout");
        MatchesFresh("fractional row heights", 943, 617);
        MatchesFresh("restoring the viewport");
        MatchesFresh("a 150 percent render target", scaling: 1.5);
        MatchesFresh("returning to the original render scale");

        session.UpdateDisplay(MonitorDisplayConfiguration.Default(MonitorSkin.SevenRows));
        trace.RefreshDisplay();
        MatchesFresh("adding waveform rows");
        while (session.SimulationTimeNs < 80_000_000_000) { session.Advance(50_000_000); }
        MatchesFresh("automatic ranges changing at a sweep boundary");
        while (session.SimulationTimeNs < 106_000_000_000) { session.Advance(250_000_000); }
        MatchesFresh("old source data leaving the retention window");
        session.UpdateDisplay(MonitorDisplayConfiguration.Default(MonitorSkin.ThreeRows));
        trace.RefreshDisplay();
        MatchesFresh("removing waveform rows");
        trace.ClearWaveformCache();
    }

    private static void VerifyRasterReuse()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(MonitorSkin.FourRows));
        while (session.SimulationTimeNs < 65_000_000_000) { session.Advance(250_000_000); }
        var trace = new LiveMonitorTrace(session);
        byte[] first = Raster(trace, 1000, 640);
        Require(trace.CachedWaveformBytes > 0 && trace.CachedWaveformBytes <= LiveMonitorTrace.MaximumWaveformCacheBytes,
            "native waveform cache retains a bounded raster working set");
        long built = trace.WaveformTileBuildCount;
        Require(first.AsSpan().SequenceEqual(Raster(trace, 1000, 640)) && trace.WaveformTileBuildCount == built,
            "paused native frames reuse raster tiles without rebuilding them");
        session.Advance(16_666_667);
        _ = Raster(trace, 1000, 640);
        Require(trace.WaveformTileBuildCount - built < built,
            "a new frame rebuilds fewer tiles than the complete initial sweep");
        trace.ClearWaveformCache();
        Require(trace.CachedWaveformBytes == 0, "releasing waveform caches releases their retained raster memory");
        byte[] restored = Raster(trace, 1000, 640);
        var reference = new LiveMonitorTrace(session);
        try
        {
            Require(restored.AsSpan().SequenceEqual(Raster(reference, 1000, 640)),
                "releasing and recreating caches preserves the current waveform");
        }
        finally { reference.ClearWaveformCache(); trace.ClearWaveformCache(); }

        foreach (double opacity in new[] { 1d, .5, .37 })
        {
            var skin = new CacheTestSkin { TraceBrush = new SolidColorBrush(Colors.Lime, opacity) };
            trace.Skin = skin;
            reference = new LiveMonitorTrace(session) { Skin = skin, CacheWaveformTiles = false };
            try
            {
                Require(Raster(trace, 1000, 640).AsSpan().SequenceEqual(Raster(reference, 1000, 640)),
                    "cached strokes preserve the reference pixels and opacity at native scale");
            }
            finally { reference.ClearWaveformCache(); trace.ClearWaveformCache(); }
        }

        var visibilitySkin = new CacheTestSkin();
        trace.Skin = visibilitySkin;
        _ = Raster(trace, 1000, 640);
        Require(trace.CachedWaveformBytes > 0, "visible non-ECG rows populate raster caches");
        visibilitySkin.Channels = MonitorChannels.None;
        _ = Raster(trace, 1000, 640);
        Require(trace.CachedWaveformBytes == 0, "hiding waveform channels releases their raster caches");
        visibilitySkin.Channels = MonitorChannels.All;
        _ = Raster(trace, 1000, 640);
        Require(trace.CachedWaveformBytes > 0, "restored channels repopulate raster caches");
        _ = Raster(trace, 100, 40);
        Require(trace.CachedWaveformBytes == 0, "an unusable viewport releases raster caches");
        _ = Raster(trace, 1000, 640);
        Require(trace.CachedWaveformBytes > 0, "restoring the viewport repopulates raster caches");
        var ecgSlots = session.Display.Slots.Select(slot => slot with { Channel = 0 }).ToArray();
        session.UpdateDisplay(new(MonitorSkin.FourRows, ecgSlots));
        trace.RefreshDisplay();
        reference = new LiveMonitorTrace(session) { Skin = visibilitySkin, CacheWaveformTiles = false };
        try
        {
            Require(Raster(trace, 1000, 640).AsSpan().SequenceEqual(Raster(reference, 1000, 640)),
                "replacing non-ECG rows preserves the reference ECG pixels");
        }
        finally { reference.ClearWaveformCache(); }
        visibilitySkin.Channels = MonitorChannels.None;
        _ = Raster(trace, 1000, 640);
        Require(trace.CachedWaveformBytes == 0, "hiding ECG rows releases all retained raster caches");
    }

    private sealed class CacheTestSkin : IMonitorSkin
    {
        public MonitorChannels Channels { get; set; } = MonitorChannels.All;
        public string Id => "trace-cache-check";
        public IBrush Background { get; set; } = Brushes.Black;
        public IBrush TraceBrush { get; set; } = Brushes.Lime;
        public bool CompactWaveformLabels { get; set; }
        public IBrush ChannelBrush(int channel) => TraceBrush;
        public Control Compose(MonitorSkinContent content) => content.Waveforms;
    }

    private static int CaptureDrawingCount(Control trace)
    {
        var drawing = new DrawingGroup();
        using (DrawingContext context = drawing.Open()) { trace.Render(context); }
        return Descendants(drawing).Count(item => item is not DrawingGroup);
    }

    private static IEnumerable<Drawing> Descendants(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (Drawing child in group.Children)
                foreach (Drawing descendant in Descendants(child)) { yield return descendant; }
    }

    private static void Layout(Control trace, int width, int height)
    {
        trace.Measure(new Size(width, height));
        trace.Arrange(new Rect(0, 0, width, height));
    }

    private static byte[] Raster(Control trace, int width, int height, double scaling = 1)
    {
        Layout(trace, width, height);
        int pixelWidth = (int)Math.Ceiling(width * scaling), pixelHeight = (int)Math.Ceiling(height * scaling);
        using var image = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96 * scaling, 96 * scaling));
        image.Render(trace);
        using var pixels = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = pixels.Lock();
        image.CopyPixels(buffer);
        byte[] data = new byte[pixelWidth * pixelHeight * 4];
        for (int y = 0; y < pixelHeight; y++)
        { Marshal.Copy(buffer.Address + y * buffer.RowBytes, data, y * pixelWidth * 4, pixelWidth * 4); }
        return data;
    }

    private static void VerifyEraseGap(byte[] pixels, int width, int height, MonitorDisplaySlot[] slots, double phase)
    {
        double plotWidth = width - 150;
        int fromX = (int)Math.Ceiling(132 + phase * plotWidth) + 2;
        int toX = (int)Math.Floor(132 + (phase + .012) * plotWidth) - 1;
        for (int row = 0; row < slots.Length; row++)
        {
            if (slots[row].Channel == 0) { continue; }
            for (int y = row * height / slots.Length + 10; y < (row + 1) * height / slots.Length - 11; y++)
                for (int x = fromX; x < toX; x++)
                {
                    int offset = (y * width + x) * 4;
                    Require(pixels[offset] == 0x25 && pixels[offset + 1] == 0x1b && pixels[offset + 2] == 0x10,
                        "sweep erase gap remains clear for every non-ECG channel");
                }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Live monitor trace: " + message); }
    }
}
