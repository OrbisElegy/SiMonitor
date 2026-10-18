// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Monitor.Desktop;

internal sealed partial class LiveMonitorTrace
{
    internal const long MaximumWaveformCacheBytes = 64 * 1024 * 1024;
    private sealed class TileMetrics
    {
        internal long Bytes;
        internal long Builds;
        internal long Draws;
    }

    private readonly TileMetrics _tileMetrics = new();
    private ContourTileCache?[,] _tileCaches = new ContourTileCache?[0, 0];
    private bool _cacheWaveformTiles = true;
    internal long CachedWaveformBytes => Interlocked.Read(ref _tileMetrics.Bytes);
    internal long WaveformTileBuildCount => Interlocked.Read(ref _tileMetrics.Builds);
    internal long WaveformTileDrawCount => Interlocked.Read(ref _tileMetrics.Draws);
    internal bool CacheWaveformTiles
    {
        get => _cacheWaveformTiles;
        set
        {
            if (_cacheWaveformTiles == value) { return; }
            _cacheWaveformTiles = value;
            ClearWaveformCache();
            InvalidateVisual();
        }
    }

    internal void ClearWaveformCache()
    {
        if (_tileCaches.Length == 0 && _ecgRasterCaches.Length == 0) { return; }
        foreach (ContourTileCache? cache in _tileCaches) { cache?.Dispose(); }
        foreach (EcgRasterCache? cache in _ecgRasterCaches) { cache?.Dispose(); }
        _tileCaches = new ContourTileCache?[0, 0];
        _ecgRasterCaches = [];
    }

    private void ReleaseTileCache(int row, int age)
    {
        if (row >= _tileCaches.GetLength(0)) { return; }
        _tileCaches[row, age]?.Dispose();
        _tileCaches[row, age] = null;
    }

    private ContourTileCache GetTileCache(int row, int age)
    {
        int rows = _session.Display.Slots.Count;
        if (_tileCaches.GetLength(0) != rows)
        {
            foreach (ContourTileCache? cache in _tileCaches) { cache?.Dispose(); }
            _tileCaches = new ContourTileCache?[rows, 2];
        }
        return _tileCaches[row, age] ??= new(_tileMetrics, MaximumWaveformCacheBytes / (rows * 2));
    }

    // A submitted operation owns an immutable frame snapshot. The cache itself
    // serializes rendering and retirement, including a UI-thread detach while an
    // older composition frame is still using its operation on the render thread.
    private sealed class ContourTileOperation(ContourTileCache cache, Rect plot, Point[] points,
        ContourBatch[] batches, Color color, double opacity, double begin, double end) : ICustomDrawOperation
    {
        public Rect Bounds => plot;
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is { } feature)
            {
                using var lease = feature.Lease();
                byte alpha = (byte)Math.Clamp(color.A * opacity * lease.CurrentOpacity, 0, 255);
                var ink = new SKColor(color.R, color.G, color.B, alpha);
                if (cache.Draw(lease.SkCanvas, plot, points, batches, ink, begin, end)) { return; }
                using var paint = CreateInkPaint(ink);
                foreach (ContourBatch batch in batches)
                {
                    if (batch.End < begin || batch.Begin > end) { continue; }
                    using SKPath path = CreateStrokePath(plot, points, batch);
                    lease.SkCanvas.DrawPath(path, paint);
                }
                return;
            }
            var pen = new ImmutablePen(new ImmutableSolidColorBrush(color, opacity), 1.2);
            foreach (ContourBatch batch in batches)
            {
                if (batch.End < begin || batch.Begin > end) { continue; }
                for (int index = batch.StartIndex + 1; index < batch.StartIndex + batch.Count; index++)
                {
                    Point a = points[index - 1], b = points[index];
                    context.DrawLine(pen, new(plot.X + a.X * plot.Width, plot.Y + a.Y * plot.Height),
                        new(plot.X + b.X * plot.Width, plot.Y + b.Y * plot.Height));
                }
            }
        }
    }

    private static SKPaint CreateInkPaint(SKColor ink) => new()
    {
        Color = ink,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.2f,
        StrokeCap = SKStrokeCap.Butt,
        StrokeJoin = SKStrokeJoin.Miter,
        StrokeMiter = 10
    };

    private static SKPath CreateStrokePath(Rect plot, Point[] points, ContourBatch batch)
    {
        var path = new SKPath();
        for (int index = batch.StartIndex + 1; index < batch.StartIndex + batch.Count; index++)
        {
            Point a = points[index - 1], b = points[index];
            path.MoveTo((float)(plot.X + a.X * plot.Width), (float)(plot.Y + a.Y * plot.Height));
            path.LineTo((float)(plot.X + b.X * plot.Width), (float)(plot.Y + b.Y * plot.Height));
        }
        return path;
    }

    private sealed class ContourTileCache(TileMetrics metrics, long maximumBytes) : IDisposable
    {
        // Device-pixel tiles bound both the front-edge repaint and the number of
        // bitmap draws. Padding retains antialiasing where strokes cross a tile.
        private const int TileWidthPixels = 64;
        private readonly object _gate = new();
        private bool _disposed;
        private RasterTile?[] _tiles = [];
        private SKMatrix _matrix;
        private Rect _plot;
        private SKColor _ink;
        private SKPaint? _paint;
        private int _leftPixels, _topPixels, _widthPixels, _heightPixels, _paddingPixels;

        private sealed record RasterTile(SKImage Image, StreamGeometry[] Paths, long Bytes);

        internal bool Draw(SKCanvas canvas, Rect plot, Point[] points, ContourBatch[] batches,
            SKColor ink, double begin, double end)
        {
            lock (_gate)
            {
                if (_disposed) { return false; }
                SKMatrix matrix = canvas.TotalMatrix;
                if (!Prepare(matrix, plot, ink)) { return false; }
                double fromPixels = matrix.ScaleX * (plot.X + begin * plot.Width) + matrix.TransX;
                double toPixels = matrix.ScaleX * (plot.X + end * plot.Width) + matrix.TransX;
                int first = Math.Clamp((int)Math.Floor((fromPixels - _leftPixels) / TileWidthPixels), 0, _tiles.Length - 1);
                int last = Math.Clamp((int)Math.Floor((toPixels - _leftPixels) / TileWidthPixels), 0, _tiles.Length - 1);
                int saved = canvas.Save();
                try
                {
                    // A pixel-aligned blit avoids an extra sampling pass under
                    // Viewbox transforms, fractional DPI, or bitmap/print export.
                    canvas.ResetMatrix();
                    for (int index = first; index <= last; index++)
                    {
                        int left = _leftPixels + index * TileWidthPixels;
                        int width = Math.Min(TileWidthPixels, _widthPixels - index * TileWidthPixels);
                        double from = ((left - _paddingPixels - matrix.TransX) / matrix.ScaleX - plot.X) / plot.Width;
                        double to = ((left + width + _paddingPixels - matrix.TransX) / matrix.ScaleX - plot.X) / plot.Width;
                        int start = FirstBatch(batches, from);
                        int count = 0;
                        while (start + count < batches.Length && batches[start + count].Begin <= to) { count++; }
                        RasterTile? tile = _tiles[index];
                        if (!Matches(tile, batches, start, count))
                        {
                            Retire(index);
                            if (count == 0) { continue; }
                            tile = CreateTile(left, width, points, batches, start, count);
                            _tiles[index] = tile;
                        }
                        if (tile is null) { continue; }
                        var source = new SKRect(_paddingPixels, _paddingPixels,
                            _paddingPixels + width, _paddingPixels + _heightPixels);
                        var destination = new SKRect(left, _topPixels, left + width, _topPixels + _heightPixels);
                        canvas.DrawImage(tile.Image, source, destination,
                            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
                        Interlocked.Increment(ref metrics.Draws);
                    }
                }
                finally { canvas.RestoreToCount(saved); }
                return true;
            }
        }

        private bool Prepare(SKMatrix matrix, Rect plot, SKColor ink)
        {
            if (matrix.ScaleX <= 0 || matrix.ScaleY <= 0 || matrix.SkewX != 0 || matrix.SkewY != 0 ||
                matrix.Persp0 != 0 || matrix.Persp1 != 0 || matrix.Persp2 != 1)
            { Clear(); return false; }
            double left = Math.Floor(matrix.ScaleX * plot.Left + matrix.TransX);
            double top = Math.Floor(matrix.ScaleY * plot.Top + matrix.TransY);
            double right = Math.Ceiling(matrix.ScaleX * plot.Right + matrix.TransX);
            double bottom = Math.Ceiling(matrix.ScaleY * plot.Bottom + matrix.TransY);
            double width = right - left, height = bottom - top;
            if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom) ||
                left < int.MinValue / 2 || top < int.MinValue / 2 || right > int.MaxValue / 2 || bottom > int.MaxValue / 2 ||
                width is < 1 or > 32768 || height is < 1 or > 32768)
            { Clear(); return false; }
            int padding = (int)Math.Ceiling(.6 * Math.Max(matrix.ScaleX, matrix.ScaleY)) + 2;
            int count = ((int)width + TileWidthPixels - 1) / TileWidthPixels;
            long bytes = ((long)width + count * 2L * padding) * ((long)height + padding * 2L) * 4;
            if (bytes > maximumBytes) { Clear(); return false; }
            if (_tiles.Length > 0 && _matrix == matrix && _plot == plot && _ink == ink) { return true; }
            Clear();
            _matrix = matrix;
            _plot = plot;
            _ink = ink;
            _leftPixels = (int)left;
            _topPixels = (int)top;
            _widthPixels = (int)width;
            _heightPixels = (int)height;
            _paddingPixels = padding;
            _tiles = new RasterTile?[count];
            _paint = CreateInkPaint(ink);
            return true;
        }

        private RasterTile CreateTile(int left, int width, Point[] points, ContourBatch[] batches, int start, int count)
        {
            int pixelWidth = width + _paddingPixels * 2, pixelHeight = _heightPixels + _paddingPixels * 2;
            using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            SKMatrix transform = _matrix;
            transform.TransX -= left - _paddingPixels;
            transform.TransY -= _topPixels - _paddingPixels;
            canvas.SetMatrix(transform);
            var paths = new StreamGeometry[count];
            for (int index = 0; index < count; index++)
            {
                ContourBatch batch = batches[start + index];
                paths[index] = batch.Path;
                using SKPath path = CreateStrokePath(_plot, points, batch);
                canvas.DrawPath(path, _paint!);
            }
            long bytes = (long)pixelWidth * pixelHeight * 4;
            SKImage image = surface.Snapshot();
            Interlocked.Add(ref metrics.Bytes, bytes);
            Interlocked.Increment(ref metrics.Builds);
            return new(image, paths, bytes);
        }

        private static int FirstBatch(ContourBatch[] batches, double from)
        {
            int low = 0, high = batches.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (batches[middle].End < from) { low = middle + 1; }
                else { high = middle; }
            }
            return low;
        }

        private static bool Matches(RasterTile? tile, ContourBatch[] batches, int start, int count)
        {
            if (tile is null || tile.Paths.Length != count) { return false; }
            for (int index = 0; index < count; index++)
                if (!ReferenceEquals(tile.Paths[index], batches[start + index].Path)) { return false; }
            return true;
        }

        private void Retire(int index)
        {
            if (_tiles[index] is not { } tile) { return; }
            _tiles[index] = null;
            tile.Image.Dispose();
            Interlocked.Add(ref metrics.Bytes, -tile.Bytes);
        }

        private void Clear()
        {
            for (int index = 0; index < _tiles.Length; index++) { Retire(index); }
            _tiles = [];
            _paint?.Dispose();
            _paint = null;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                Clear();
            }
        }
    }
}
