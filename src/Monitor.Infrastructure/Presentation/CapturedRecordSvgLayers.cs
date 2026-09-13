// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

public sealed record CapturedRecordSvgLayout(int PlotLeftPixels, int PlotWidthPixels,
    EcgVerticalScale VerticalScale, EcgPaperScale PaperScale,
    int GridOriginXPixels, int GridOriginYPixels, int MaximumGridLines);
public sealed record CapturedRecordSvgLayersResult(GridCapturedRecordPageDisplay Display, string? GridSvg);

// Serialized screen-layer composition, not a waveform renderer or print/export document.
public static class CapturedRecordSvgLayers
{
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
