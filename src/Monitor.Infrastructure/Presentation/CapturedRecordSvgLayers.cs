// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

public sealed record CapturedRecordSvgLayout(int PlotLeftPixels, int PlotWidthPixels,
    EcgVerticalScale VerticalScale, EcgPaperScale PaperScale,
    int GridOriginXPixels, int GridOriginYPixels, int MaximumGridLines);
public sealed record CapturedRecordSvgLayersResult(GridCapturedRecordPageDisplay Display, string? GridSvg);
public sealed record CapturedRecordSvgScreenLayers(CapturedRecordSvgLayersResult Content, string? CursorOverlaySvg);

// Serialized screen-layer composition, not a waveform renderer or print/export document.
public static class CapturedRecordSvgLayers
{
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
