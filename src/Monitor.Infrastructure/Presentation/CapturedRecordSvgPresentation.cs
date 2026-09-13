// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

// Serialized publication boundary. The shell reads Current for both image and new input.
// Retained immutable snapshots are not revoked; native controls must bind this current slot.
public sealed class CapturedRecordSvgPresentation
{
    private readonly CapturedRecordStudyView _view;
    private readonly CapturedRecordNavigation _navigation;
    private readonly Ecg12ThemeSelection _theme;
    private readonly Ecg12ZoomSelection _zoom;

    public CapturedRecordSvgPresentation(CapturedRecordStudyView view, CapturedRecordNavigation navigation,
        Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(zoom);
        _view = view;
        _navigation = navigation;
        _theme = theme;
        _zoom = zoom;
    }

    public CapturedRecordSvgInputSession? Current { get; private set; }

    public CapturedRecordSvgInputSession Refresh(bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle,
        EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default)
    {
        // Clear before validation/cancellation so any failure cannot retain obsolete output.
        Current = null;
        CapturedRecordSvgInputSession candidate = new(_view, _navigation, _theme, _zoom,
            canPreserveGlobalSafetyOverlay, layout, screen, gridStyle, cursorStyle, allowAuxiliaryRate, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Current = candidate;
        return candidate;
    }
}
