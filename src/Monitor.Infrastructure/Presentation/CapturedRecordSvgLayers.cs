// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

public sealed record CapturedRecordSvgLayout(int PlotLeftPixels, int PlotWidthPixels,
    EcgVerticalScale VerticalScale, EcgPaperScale PaperScale,
    int GridOriginXPixels, int GridOriginYPixels, int MaximumGridLines);
public sealed record CapturedRecordSvgLayersResult(GridCapturedRecordPageDisplay Display, string? GridSvg);
public sealed record CapturedRecordSvgScreenLayers(CapturedRecordSvgLayersResult Content, string? CursorOverlaySvg);
public sealed record ZoomedCapturedRecordSvgScreenLayers(CapturedRecordSvgScreenLayers Content,
    Ecg12ZoomDisplay? Zoom, Ecg12ScreenTransform? Transform, string? GridSvg, string? CursorOverlaySvg);

// Serialized screen-layer composition, not a waveform renderer or print/export document.
public static class CapturedRecordSvgLayers
{
    public static ZoomedCapturedRecordSvgScreenLayers RenderZoomedScreen(CapturedRecordStudyView view,
        CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom,
        bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen,
        EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(zoom);
        CapturedRecordSvgScreenLayers content = RenderScreen(view, navigation, theme, canPreserveGlobalSafetyOverlay,
            layout, gridStyle, cursorStyle, allowAuxiliaryRate, cancellationToken);
        if (!content.Content.Display.Content.Content.Study.Admission.MayEnter)
        { return new(content, null, null, null, null); }
        ArgumentNullException.ThrowIfNull(screen);
        Ecg12ZoomDisplay selection = zoom.CaptureDisplay();
        Ecg12ScreenTransform transform = Ecg12ScreenTransform.Resolve(selection.Selection,
            screen.PageWidth, screen.PageHeight, screen.AvailableWidth, screen.AvailableHeight);
        if (layout.PlotLeftPixels < 0 || layout.VerticalScale.PlotTopPixels < 0 ||
            (long)layout.PlotLeftPixels + layout.PlotWidthPixels > screen.PageWidth ||
            (long)layout.VerticalScale.PlotTopPixels + layout.VerticalScale.PlotHeightPixels > screen.PageHeight)
        { throw new Ecg12ZoomSelectionException("Ecg12Zoom.PlotOutsidePage", nameof(screen)); }
        if (SvgLogicalNumber.Format(transform.Width.Numerator, transform.Width.Denominator) == "0" ||
            SvgLogicalNumber.Format(transform.Height.Numerator, transform.Height.Denominator) == "0")
        { throw new Ecg12ZoomSelectionException("Ecg12Zoom.UnrepresentableSvgSize", nameof(screen)); }
        string? grid = WrapScreenLayer(content.Content.GridSvg, screen, transform);
        string? cursors = WrapScreenLayer(content.CursorOverlaySvg, screen, transform);
        cancellationToken.ThrowIfCancellationRequested();
        return new(content, selection, transform, grid, cursors);
    }

    // Only internally generated layers enter this wrapper; it is screen-only.
    private static string? WrapScreenLayer(string? layer, RecordScreenZoomLayout screen, Ecg12ScreenTransform transform)
    {
        if (layer is null) { return null; }
        string width = SvgLogicalNumber.Format(transform.Width.Numerator, transform.Width.Denominator);
        string height = SvgLogicalNumber.Format(transform.Height.Numerator, transform.Height.Denominator);
        return string.Create(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {screen.PageWidth} {screen.PageHeight}\" preserveAspectRatio=\"xMinYMin meet\" overflow=\"hidden\" pointer-events=\"none\" aria-hidden=\"true\" data-layer=\"zoomed-screen\">{layer}</svg>");
    }

    public static CapturedRecordSvgScreenLayers RenderScreen(CapturedRecordStudyView view,
        CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle,
        bool allowAuxiliaryRate, CancellationToken cancellationToken = default)
    {
        CapturedRecordSvgLayersResult content = Render(view, navigation, theme, canPreserveGlobalSafetyOverlay,
            layout, gridStyle, allowAuxiliaryRate, cancellationToken);
        string? cursors = EcgManualCursorSvg.Render(content.Display.Content.Content, layout.VerticalScale, cursorStyle, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(content, cursors);
    }

    public static CapturedRecordSvgLayersResult Render(CapturedRecordStudyView view,
        CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, EcgPaperGridSvgStyle gridStyle, bool allowAuxiliaryRate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(layout);
        GridCapturedRecordPageDisplay display = view.CaptureGridPageDisplay(navigation, theme, canPreserveGlobalSafetyOverlay,
            layout.PlotLeftPixels, layout.PlotWidthPixels, layout.VerticalScale, layout.PaperScale,
            layout.GridOriginXPixels, layout.GridOriginYPixels, layout.MaximumGridLines, allowAuxiliaryRate, cancellationToken);
        string? grid = display.GridPlan is { } plan
            ? EcgPaperGridSvg.RenderValidated(plan, display.GridLines, gridStyle, cancellationToken) : null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(display, grid);
    }
}
