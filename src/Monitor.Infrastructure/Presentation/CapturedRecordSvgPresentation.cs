// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

public enum CapturedRecordSvgStatus { NotRendered, Refreshing, Ready, Denied, Cancelled, Failed, Withdrawn }
public sealed record CapturedRecordSvgPublication(CapturedRecordSvgStatus Status, string ReasonCode,
    CapturedRecordSvgInputSession? Input);

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

    public CapturedRecordSvgPublication Publication { get; private set; } =
        new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null);
    public CapturedRecordSvgInputSession? Current => Publication.Input;

    // Trusted shell lifecycle action; command authorization is enforced by its caller.
    // Data and retained drag objects are not deleted or implicitly committed/cancelled.
    public void Withdraw()
    {
        if (Publication.Status == CapturedRecordSvgStatus.Withdrawn) { return; }
        Publication = new(CapturedRecordSvgStatus.Withdrawn, "SvgPresentation.Withdrawn", null);
    }

    public CapturedRecordSvgInputSession Refresh(bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle,
        EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default)
    {
        // Clear before validation/cancellation so any failure cannot retain obsolete output.
        Publication = new(CapturedRecordSvgStatus.Refreshing, "SvgPresentation.Refreshing", null);
        try
        {
            CapturedRecordSvgInputSession candidate = new(_view, _navigation, _theme, _zoom,
                canPreserveGlobalSafetyOverlay, layout, screen, gridStyle, cursorStyle, allowAuxiliaryRate, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Publication = new(CapturedRecordSvgStatus.Ready, "SvgPresentation.Ready", candidate);
            return candidate;
        }
        catch (Ecg12ViewAdmissionException exception)
        {
            Publication = new(CapturedRecordSvgStatus.Denied, exception.ReasonCode, null);
            throw;
        }
        catch (OperationCanceledException exception) when
            (cancellationToken.IsCancellationRequested && exception.CancellationToken == cancellationToken)
        {
            Publication = new(CapturedRecordSvgStatus.Cancelled, "SvgPresentation.Cancelled", null);
            throw;
        }
        finally
        {
            if (Publication.Status == CapturedRecordSvgStatus.Refreshing)
            { Publication = new(CapturedRecordSvgStatus.Failed, "SvgPresentation.RenderFailed", null); }
        }
    }
}
