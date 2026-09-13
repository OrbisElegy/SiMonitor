// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

internal static class DesktopSmokeChecks
{
    public static void VerifyPublicationStates(MainWindow window)
    {
        CapturedRecordSvgPublication ready = DesktopStudySmokeFixture.Create();
        RecordStudyControl control = new(ready, DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle);
        window.ApplyPublication(ready, control);
        window.UpdateLayout();
        if (window.HasNoRecordContent || control.Bounds.Size != new Size(200, 120))
        { throw new InvalidOperationException("Ready study did not produce scaled native content."); }
        Directory.CreateDirectory("artifacts");
        using (RenderTargetBitmap image = new(new PixelSize(200, 120), new Vector(96, 96)))
        {
            image.Render(control);
            VerifyPixel(image, 50, 10, 0, 85, 255);
            VerifyPixel(image, 150, 10, 255, 85, 0);
            VerifyPixel(image, 20, 20, 255, 255, 255);
            image.Save(Path.Combine("artifacts", "desktop-record-geometry.png"), PngBitmapEncoderOptions.Default);
        }
        foreach (bool hidden in new[] { true, false })
        {
            CapturedRecordSvgPublication filtered = DesktopStudySmokeFixture.Create(hidden, !hidden);
            RecordStudyControl filteredControl = new(filtered, DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle);
            window.ApplyPublication(filtered, filteredControl);
            window.UpdateLayout();
            using RenderTargetBitmap image = new(new PixelSize(200, 120), new Vector(96, 96));
            image.Render(filteredControl);
            VerifyPixel(image, 50, 10, 255, 255, 255);
            if (hidden) { VerifyPixel(image, 150, 10, 255, 255, 255); }
            else { VerifyPixel(image, 150, 10, 255, 85, 0); }
        }
        try
        {
            window.ApplyPublication(DesktopStudySmokeFixture.Create(), control);
            throw new InvalidOperationException("Foreign rendered input accepted.");
        }
        catch (ArgumentException)
        {
            if (!window.HasNoRecordContent || window.CurrentPublication is not null)
            { throw new InvalidOperationException("Mismatched input retained native content."); }
        }
        foreach (CapturedRecordSvgStatus status in new[] { CapturedRecordSvgStatus.Refreshing, CapturedRecordSvgStatus.Denied,
            CapturedRecordSvgStatus.Cancelled, CapturedRecordSvgStatus.Failed, CapturedRecordSvgStatus.Withdrawn })
        {
            window.ApplyPublication(ready, control);
            CapturedRecordSvgPublication publication = new(status, "DesktopSmoke.Status", null);
            window.ApplyPublication(publication);
            if (!window.HasNoRecordContent || !ReferenceEquals(window.CurrentPublication, publication))
            { throw new InvalidOperationException("Unavailable publication retained native content."); }
        }
        try
        {
            window.ApplyPublication(new(CapturedRecordSvgStatus.Ready, "SvgPresentation.Ready", null));
            throw new InvalidOperationException("Incomplete ready publication accepted.");
        }
        catch (ArgumentException)
        {
            if (!window.HasNoRecordContent || window.CurrentPublication is not null)
            { throw new InvalidOperationException("Invalid publication retained native state."); }
        }
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        if (!window.HasUnloadedRecordState)
        { throw new InvalidOperationException("Unloaded state did not recover."); }
        Console.WriteLine("ok: native study drawing, input identity, withdrawal and recovery (synthetic test record)");
    }

    private static void VerifyPixel(RenderTargetBitmap image, int x, int y, byte red, byte green, byte blue)
    {
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        int offset = checked(y * buffer.RowBytes + x * 4);
        if (Marshal.ReadByte(buffer.Address, offset) != blue || Marshal.ReadByte(buffer.Address, offset + 1) != green ||
            Marshal.ReadByte(buffer.Address, offset + 2) != red || Marshal.ReadByte(buffer.Address, offset + 3) != 255)
        { throw new InvalidOperationException($"Unexpected native geometry pixel at {x},{y}."); }
    }
}
