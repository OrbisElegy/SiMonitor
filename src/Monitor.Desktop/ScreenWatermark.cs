// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Monitor.Desktop;

// A fixed, public synthetic-content marker, not a signature or authenticity proof.
// Only the screen composition uses it; acquisition, measurements and PDF stay separate.
internal sealed class ScreenWatermark : Control
{
    private static readonly IBrush Tile = CreateTile();

    private ScreenWatermark()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
    }

    internal static void Attach(Grid root)
    {
        var overlay = new ScreenWatermark { ZIndex = int.MaxValue };
        Grid.SetColumnSpan(overlay, Math.Max(1, root.ColumnDefinitions.Count));
        Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count));
        root.Children.Add(overlay);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Tile, new Rect(Bounds.Size));
    }

    private static IBrush CreateTile()
    {
        using var stream = typeof(ScreenWatermark).Assembly.GetManifestResourceStream("Monitor.Watermarks.screen-v1.json")
            ?? throw new InvalidOperationException("Missing screen watermark profile.");
        using var document = JsonDocument.Parse(stream);
        var profile = document.RootElement;
        int period = profile.GetProperty("period").GetInt32();
        int maximumDelta = profile.GetProperty("maximumDelta").GetInt32();
        double amplitude = profile.GetProperty("amplitude").GetDouble();
        double phaseStep = profile.GetProperty("phaseStep").GetDouble();
        var carriers = profile.GetProperty("carriers").EnumerateArray()
            .Select(pair => (X: pair[0].GetInt32(), Y: pair[1].GetInt32())).ToArray();
        byte[] pixels = new byte[period * period * 4];
        for (int y = 0; y < period; y++)
        {
            for (int x = 0; x < period; x++)
            {
                double value = 0;
                for (int index = 0; index < carriers.Length; index++)
                {
                    var carrier = carriers[index];
                    value += amplitude * Math.Cos(2 * Math.PI *
                        ((carrier.X * x + carrier.Y * y) / (double)period + index * index * phaseStep));
                }
                int delta = (int)Math.Clamp(Math.Round(value), -maximumDelta, maximumDelta);
                int offset = (y * period + x) * 4;
                // Premultiplied white/black nudges every RGB channel by at most 2/255.
                // Unlike a white-only overlay, the carrier survives light and dark surfaces.
                byte color = (byte)Math.Max(0, delta);
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = color;
                pixels[offset + 3] = (byte)Math.Abs(delta);
            }
        }
        // One shared tile for the UI lifetime: no timer, frame readback or per-frame FFT.
        var bitmap = new WriteableBitmap(new PixelSize(period, period), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (int y = 0; y < period; y++)
            { Marshal.Copy(pixels, y * period * 4, buffer.Address + y * buffer.RowBytes, period * 4); }
        }
        return new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            DestinationRect = new RelativeRect(0, 0, period, period, RelativeUnit.Absolute)
        }.ToImmutable();
    }
}
