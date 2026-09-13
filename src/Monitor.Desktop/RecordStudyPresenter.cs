// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

// One serialized presenter owns a window's study updates. It does not own data
// or authorize navigation, and retained gestures still require explicit resolution.
public sealed class RecordStudyPresenter
{
    private readonly MainWindow _window;
    private readonly CapturedRecordSvgPresentation _presentation;

    public RecordStudyPresenter(MainWindow window, CapturedRecordSvgPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(presentation);
        _window = window;
        _presentation = presentation;
    }

    public void Refresh(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout,
        RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle,
        EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        _window.ApplyPublication(new(CapturedRecordSvgStatus.Refreshing, "SvgPresentation.Refreshing", null));
        try
        {
            _presentation.Refresh(canPreserveGlobalSafetyOverlay, layout, screen,
                gridStyle, cursorStyle, allowAuxiliaryRate, cancellationToken);
            CapturedRecordSvgPublication ready = _presentation.Publication;
            RecordStudyControl control = new(ready, gridStyle, cursorStyle);
            cancellationToken.ThrowIfCancellationRequested();
            _window.ApplyPublication(ready, control);
        }
        catch (Exception exception)
        {
            CapturedRecordSvgPublication failed = _presentation.Publication;
            if (failed.Status == CapturedRecordSvgStatus.Ready)
            {
                // Native construction can fail after the adapter has published.
                // Withdraw its input as well as the native content.
                _presentation.Withdraw();
                bool cancelled = exception is OperationCanceledException cancellation &&
                    cancellationToken.IsCancellationRequested && cancellation.CancellationToken == cancellationToken;
                failed = cancelled ? new(CapturedRecordSvgStatus.Cancelled, "SvgPresentation.Cancelled", null)
                    : new(CapturedRecordSvgStatus.Failed, "DesktopStudy.RenderFailed", null);
            }
            _window.ApplyPublication(failed);
            throw;
        }
    }

    public void Withdraw()
    {
        Dispatcher.UIThread.VerifyAccess();
        _presentation.Withdraw();
        _window.ApplyPublication(_presentation.Publication);
    }
}
