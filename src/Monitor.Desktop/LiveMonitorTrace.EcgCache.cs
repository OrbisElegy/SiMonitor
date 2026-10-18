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
    private EcgRasterCache?[] _ecgRasterCaches = [];

    private void ReleaseEcgCache(int row)
    {
        if (row >= _ecgRasterCaches.Length) { return; }
        _ecgRasterCaches[row]?.Dispose();
        _ecgRasterCaches[row] = null;
    }

    private bool DrawCachedPreviousEcg(DrawingContext context, int row, Point[] points, Rect plot, IBrush brush)
    {
        if (!CacheWaveformTiles || brush is not ISolidColorBrush solid || points.Length == 0)
        { return false; }
        int rows = _session.Display.Slots.Count;
        if (_ecgRasterCaches.Length != rows)
        {
            foreach (EcgRasterCache? cache in _ecgRasterCaches) { cache?.Dispose(); }
            _ecgRasterCaches = new EcgRasterCache?[rows];
        }
        EcgRasterCache raster = _ecgRasterCaches[row] ??= new(_tileMetrics, MaximumWaveformCacheBytes / (rows * 2));
        var operation = new EcgRasterOperation(raster, points, plot, solid.Color, solid.Opacity);
        try
        {
            context.Custom(operation);
            return true;
        }
        catch (NotSupportedException)
        {
            operation.Dispose();
            return false;
        }
    }

    // Keep the original ECG conversion order: normalized coordinates are first
    // stored as floats, then the geometry is transformed into logical pixels.
    // One continuous figure preserves joins at the original sample vertices.
    private static SKPath CreateEcgPath(Point[] points, Rect plot)
    {
        var path = new SKPath();
        if (points.Length == 0) { return path; }
        path.MoveTo((float)points[0].X, (float)points[0].Y);
        for (int index = 1; index < points.Length; index++)
        { path.LineTo((float)points[index].X, (float)points[index].Y); }
        var transform = new SKMatrix
        {
            ScaleX = (float)plot.Width,
            ScaleY = (float)plot.Height,
            TransX = (float)plot.X,
            TransY = (float)plot.Y,
            Persp2 = 1
        };
        path.Transform(in transform);
        return path;
    }

    private sealed class EcgRasterOperation(EcgRasterCache cache, Point[] points, Rect plot,
        Color color, double opacity) : ICustomDrawOperation
    {
        public Rect Bounds => plot;
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            // The application's native platforms use Skia. The public immediate
            // context has no geometry API; a different backend can still draw
            // the samples, though its individual strokes may use different joins.
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                var pen = new ImmutablePen(new ImmutableSolidColorBrush(color, opacity), 1.2);
                for (int index = 1; index < points.Length; index++)
                {
                    Point a = points[index - 1], b = points[index];
                    context.DrawLine(pen, new(plot.X + a.X * plot.Width, plot.Y + a.Y * plot.Height),
                        new(plot.X + b.X * plot.Width, plot.Y + b.Y * plot.Height));
                }
                return;
            }
            using var lease = feature.Lease();
            byte alpha = (byte)Math.Clamp(color.A * opacity * lease.CurrentOpacity, 0, 255);
            var ink = new SKColor(color.R, color.G, color.B, alpha);
            if (cache.Draw(lease.SkCanvas, points, plot, ink)) { return; }
            using SKPath path = CreateEcgPath(points, plot);
            using SKPaint paint = CreateInkPaint(ink);
            lease.SkCanvas.DrawPath(path, paint);
        }
    }

    private sealed class EcgRasterCache(TileMetrics metrics, long maximumBytes) : IDisposable
    {
        private readonly object _gate = new();
        private bool _disposed;
        private Point[]? _points;
        private Rect _plot;
        private SKPath? _path;
        private SKPaint? _paint;
        private SKColor _ink;
        private SKMatrix _matrix;
        private SKImage? _image;
        private SKRect _imageRegion;
        private long _bytes;

        internal bool Draw(SKCanvas canvas, Point[] points, Rect plot, SKColor ink)
        {
            lock (_gate)
            {
                if (_disposed) { return false; }
                if (!ReferenceEquals(_points, points) || _plot != plot)
                {
                    RetireImage();
                    _path?.Dispose();
                    _path = CreateEcgPath(points, plot);
                    _points = points;
                    _plot = plot;
                }
                if (_paint is null || _ink != ink)
                {
                    RetireImage();
                    _paint?.Dispose();
                    _paint = CreateInkPaint(ink);
                    _ink = ink;
                }
                SKMatrix matrix = canvas.TotalMatrix;
                if (!PrepareImage(matrix, plot))
                {
                    canvas.DrawPath(_path!, _paint);
                    return true;
                }
                int saved = canvas.Save();
                try
                {
                    canvas.ResetMatrix();
                    canvas.DrawImage(_image!, _imageRegion, _imageRegion,
                        new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
                    Interlocked.Increment(ref metrics.Draws);
                }
                finally { canvas.RestoreToCount(saved); }
                return true;
            }
        }

        private bool PrepareImage(SKMatrix matrix, Rect plot)
        {
            // Fractional transforms can change Skia's edge coverage when a
            // moving sweep clip is applied after rasterization. Preserve the
            // vector path for those transforms, including 150 percent DPI.
            if (matrix.ScaleX is not (1 or 2) || matrix.ScaleY != matrix.ScaleX ||
                matrix.TransX != MathF.Truncate(matrix.TransX) || matrix.TransY != MathF.Truncate(matrix.TransY) ||
                matrix.SkewX != 0 || matrix.SkewY != 0 || matrix.Persp0 != 0 || matrix.Persp1 != 0 || matrix.Persp2 != 1)
            { RetireImage(); return false; }
            double left = Math.Floor(matrix.ScaleX * plot.Left + matrix.TransX);
            double top = Math.Floor(matrix.ScaleY * plot.Top + matrix.TransY);
            double right = Math.Ceiling(matrix.ScaleX * plot.Right + matrix.TransX);
            double bottom = Math.Ceiling(matrix.ScaleY * plot.Bottom + matrix.TransY);
            if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom) ||
                left < 0 || top < 0 || right is < 1 or > 32768 || bottom is < 1 or > 32768)
            { RetireImage(); return false; }
            int width = (int)right + 2, height = (int)bottom + 2;
            long bytes = (long)width * height * 4;
            if (bytes > maximumBytes) { RetireImage(); return false; }
            if (_image is not null && _matrix == matrix) { return true; }
            RetireImage();
            // Retain the target's device origin. Translating a cropped surface
            // introduces another float rounding step at steep ECG slopes.
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(in matrix);
            canvas.ClipRect(new SKRect((float)plot.Left, (float)plot.Top, (float)plot.Right, (float)plot.Bottom));
            canvas.DrawPath(_path!, _paint!);
            _image = surface.Snapshot();
            _matrix = matrix;
            _imageRegion = new((float)left, (float)top, (float)right, (float)bottom);
            _bytes = bytes;
            Interlocked.Add(ref metrics.Bytes, bytes);
            Interlocked.Increment(ref metrics.Builds);
            return true;
        }

        private void RetireImage()
        {
            _image?.Dispose();
            _image = null;
            Interlocked.Add(ref metrics.Bytes, -_bytes);
            _bytes = 0;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                RetireImage();
                _path?.Dispose();
                _path = null;
                _paint?.Dispose();
                _paint = null;
                _points = null;
            }
        }
    }
}
