// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

// Serialized input for one rendered screen. A shell must replace this after layout/view changes.
public sealed class CapturedRecordSvgInputSession
{
    private readonly CapturedRecordStudyView _view;
    private readonly CapturedRecordNavigation _navigation;
    private readonly Ecg12ZoomSelection _zoom;
    private readonly CapturedRecordMeasurement _measurement;
    private readonly CapturedRecordCursorPair? _renderedPair;
    private readonly CapturedRecordPage _page;
    private readonly Ecg12ZoomState _selection;
    private readonly CapturedRecordSvgLayout _layout;
    private readonly RecordScreenZoomLayout _screen;
    private bool _stale;

    public CapturedRecordSvgInputSession(CapturedRecordStudyView view, CapturedRecordNavigation navigation,
        Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle,
        EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default)
    {
        Display = CapturedRecordSvgLayers.RenderZoomedScreen(view, navigation, theme, zoom,
            canPreserveGlobalSafetyOverlay, layout, screen, gridStyle, cursorStyle, allowAuxiliaryRate, cancellationToken);
        if (Display.RenderedTransform is null)
        { throw new Ecg12ViewAdmissionException(Display.Content.Content.Display.Content.Content.Study.Admission.ReasonCode, nameof(canPreserveGlobalSafetyOverlay)); }
        _view = view;
        _navigation = navigation;
        _zoom = zoom;
        _measurement = view.Measurement;
        _renderedPair = _measurement.CurrentPair;
        _page = navigation.CurrentPage;
        _selection = zoom.Selection;
        _layout = layout;
        _screen = screen;
    }

    public ZoomedCapturedRecordSvgScreenLayers Display { get; }

    public CapturedRecordCursorPair PlacePair(bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen,
        ExactPlotCoordinate firstWindowX, ExactPlotCoordinate firstWindowY,
        ExactPlotCoordinate secondWindowX, ExactPlotCoordinate secondWindowY,
        ExactPlotCoordinate originX, ExactPlotCoordinate originY)
    {
        ValidateCurrent(layout, screen);
        Ecg12ScreenTransform transform = Display.RenderedTransform!;
        return _view.PlacePairOnCurrentPage(_navigation, canPreserveGlobalSafetyOverlay,
            layout.PlotLeftPixels, layout.PlotWidthPixels, layout.VerticalScale,
            transform.InverseAt(firstWindowX, originX), transform.InverseAt(firstWindowY, originY),
            transform.InverseAt(secondWindowX, originX), transform.InverseAt(secondWindowY, originY));
    }

    // Window points and current scrolled origins are in the same logical-pixel space.
    public CapturedRecordCursorPair MoveCursor(bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, RecordCursorEnd end,
        ExactPlotCoordinate windowX, ExactPlotCoordinate windowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY)
    {
        ValidateCurrent(layout, screen);
        Ecg12ScreenTransform transform = Display.RenderedTransform!;
        return _view.MoveCursorOnCurrentPage(_navigation, canPreserveGlobalSafetyOverlay,
            layout.PlotLeftPixels, layout.PlotWidthPixels, layout.VerticalScale, end,
            transform.InverseAt(windowX, originX), transform.InverseAt(windowY, originY));
    }

    private void ValidateCurrent(CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen)
    {
        if (_stale || !ReferenceEquals(_view.Measurement, _measurement) || !ReferenceEquals(_navigation.CurrentPage, _page) ||
            !ReferenceEquals(_measurement.CurrentPair, _renderedPair) ||
            !ReferenceEquals(_zoom.Selection, _selection) || layout != _layout || screen != _screen)
        {
            _stale = true;
            throw new CapturedRecordMeasurementException("RecordMeasurement.StaleRenderedView", nameof(layout));
        }
    }
}
