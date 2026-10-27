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
        VerifyFlatEcgBrightness();
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

    private static void VerifyFlatEcgBrightness()
    {
        var range = new MonitorAmplitudeRange(-1200, 1200);
        var samples = Enumerable.Range(0, 2500).Select(index => ((long)index * 4_000_000, 0d)).ToArray();
        Point[] flat = LiveMonitorTrace.BuildEcgPoints(0, 10_000_000_000, range, samples);
        Require(flat.Length == 2 && flat[0] == new Point(0, .5) && flat[1] == new Point(.9996, .5),
            "long flat ECG runs retain exact endpoints without dense coincident stroke joins");
        samples[1250].Item2 = .001;
        Point[] detail = LiveMonitorTrace.BuildEcgPoints(0, 10_000_000_000, range, samples);
        Require(detail.Length == 5 && detail[1].X == .4996 && detail[2].X == .5 && detail[2].Y < .5 && detail[3].X == .5004,
            "even a tiny single-sample ECG detail retains both adjacent segments and its peak");
        Require(LiveMonitorTrace.BuildEcgPoints(0, 10_000_000_000, range, []).Length == 0 &&
            LiveMonitorTrace.BuildEcgPoints(0, 10_000_000_000, range, [(0, 0)]).Length == 1,
            "empty and first-sample ECG sweeps remain safe");

        MonitorDisplaySlot[] slots = [new(0, false, range, 125), new(0, false, range, 250), new(0, false, range, 500)];
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default with
        { CardiacActivity = Monitor.Simulation.Physiology.CardiacActivity.Absent }, new(MonitorSkin.ThreeRows, slots));
        var trace = new LiveMonitorTrace(session) { Skin = new CacheTestSkin { CompactWaveformLabels = true } };
        byte[] reference = Raster(new FlatEcgReference(), 1900, 600);
        try
        {
            foreach (long timeNs in new[] { 2_500_000_000L, 5_000_000_000, 9_000_000_000, 12_500_000_000, 19_000_000_000, 25_000_000_000 })
            {
                while (session.SimulationTimeNs < timeNs) { session.Advance(Math.Min(250_000_000, timeNs - session.SimulationTimeNs)); }
                foreach (bool cache in new[] { true, false })
                {
                    trace.CacheWaveformTiles = cache;
                    byte[] pixels = Raster(trace, 1900, 600);
                    for (int row = 0; row < slots.Length; row++)
                    {
                        long durationNs = slots[row].DurationNs;
                        double phase = (timeNs % durationNs) / (double)durationNs;
                        double frontier = 12 + 1870 * phase;
                        double previousStart = 12 + 1870 * Math.Min(1, phase + .012);
                        double calibration = 12 + 1870 * 200_000_000d / durationNs;
                        for (int x = 16; x < 1878; x++)
                        {
                            if (Math.Abs(x - calibration) < 3 || Math.Abs(x - frontier) < 4 || Math.Abs(x - previousStart) < 3)
                            { continue; }
                            bool visible = x < frontier || (timeNs >= durationNs && x > previousStart);
                            for (int y = row * 200 + 108; y < row * 200 + 115; y++)
                            {
                                int offset = (y * 1900 + x) * 4 + 1;
                                int expected = visible ? reference[offset] : 0;
                                if (Math.Abs(pixels[offset] - expected) > 1)
                                {
                                    Require(false, $"flat ECG differs from a single stroke or clear sweep gap at {timeNs}, row {row}, " +
                                        $"cache {cache}, ({x}, {y}): {pixels[offset]} versus {expected}");
                                }
                            }
                        }
                    }
                }
            }
        }
        finally { trace.ClearWaveformCache(); }
    }

    private sealed class FlatEcgReference : Control
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            for (int row = 0; row < 3; row++)
            {
                var path = new StreamGeometry();
                using (var geometry = path.Open())
                {
                    geometry.BeginFigure(new(0, .5), false);
                    geometry.LineTo(new(1, .5));
                    geometry.EndFigure(false);
                }
                path.Transform = new MatrixTransform(new Matrix(1870, 0, 0, 154, 12, row * 200 + 34));
                context.DrawGeometry(null, new Pen(Brushes.Lime, 1.2), path);
            }
        }
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
