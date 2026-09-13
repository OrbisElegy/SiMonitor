// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

internal static class DesktopDragSmokeChecks
{
    public static void Verify(MainWindow window)
    {
        CapturedRecordSvgPresentation source = DesktopStudySmokeFixture.CreatePresentation(activeInstance: true);
        RecordStudyPresenter presenter = new(window, source);
        RecordStudyCommandContext context = new(true, DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
            DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);
        presenter.Refresh(context);
        CapturedRecordSvgPublication before = source.Publication;
        try
        {
            presenter.BeginDrag(source.Current!, context, new(100, 60), default, 60);
            throw new InvalidOperationException("Ambiguous native drag selected a cursor.");
        }
        catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.AmbiguousCursorHit") { }
        if (!ReferenceEquals(before, window.CurrentPublication)) { throw new InvalidOperationException("Ambiguous entry changed the picture."); }
        NativeStudyDrag drag = presenter.BeginDrag(source.Current!, context, new(52, 50), default, 4);
        drag.Preview(context, new(62, 50), default);
        RequireX(source, 30);
        before = source.Publication;
        try
        {
            drag.Preview(context, new(-20, 50), default);
            throw new InvalidOperationException("Out-of-plot drag accepted.");
        }
        catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.InvalidPoint") { }
        try
        {
            drag.Preview(context, new(162, 50), default);
            throw new InvalidOperationException("Reversed native time cursors accepted.");
        }
        catch (EcgManualMeasurementException exception) when (exception.ReasonCode == "ManualMeasurement.TimeReversed") { }
        if (drag.IsFinished || !ReferenceEquals(before, window.CurrentPublication))
        { throw new InvalidOperationException("Invalid preview consumed the gesture or changed output."); }
        // Release carries a new position even without a preceding move event.
        drag.Commit(context, new(72, 50), default);
        RequireX(source, 35);
        if (!drag.IsFinished) { throw new InvalidOperationException("Release left the native gesture open."); }
        try
        {
            drag.Cancel(context);
            throw new InvalidOperationException("Finished native gesture replayed.");
        }
        catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.DragFinished") { }
        RequireX(source, 35);

        drag = presenter.BeginDrag(source.Current!, context, new(70, 50), default, 4);
        drag.Preview(context, new(80, 50), default);
        drag.Preview(context, new(90, 50), default);
        RequireX(source, 45);
        presenter.Withdraw();
        bool rejected = false;
        try
        {
            drag.Preview(context, new(100, 50), default);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
            if (source.Current is not null) { throw new InvalidOperationException("Withdrawn preview reopened the picture."); }
        }
        if (!rejected) { throw new InvalidOperationException("Withdrawn gesture preview accepted."); }
        drag.Cancel(context);
        if (!drag.IsFinished || !window.HasNoRecordContent || source.Current is not null)
        { throw new InvalidOperationException("Cancel after withdrawal reopened content."); }
        presenter.Refresh(context);
        RequireX(source, 35);

        drag = presenter.BeginDrag(source.Current!, context, new(70, 50), default, 4);
        try
        {
            drag.Preview(context with { CanPreserveGlobalSafetyOverlay = false }, new(80, 50), default);
            throw new InvalidOperationException("Unsafe native preview accepted.");
        }
        catch (Ecg12ViewAdmissionException)
        {
            if (window.CurrentPublication?.Status != CapturedRecordSvgStatus.Denied || source.Current is not null)
            { throw new InvalidOperationException("Unsafe preview retained input."); }
        }
        drag.Cancel(context);
        presenter.Refresh(context);
        RequireX(source, 35);
        drag = presenter.BeginDrag(source.Current!, context, new(70, 50), default, 4);
        try
        {
            drag.Preview(context with { GridStyle = context.GridStyle with { MinorStrokeMilliPixels = 0 } }, new(80, 50), default);
            throw new InvalidOperationException("Bad preview redraw accepted.");
        }
        catch (EcgPaperGridException)
        {
            if (source.Current is not null || drag.IsFinished) { throw new InvalidOperationException("Paint failure lost retained gesture state."); }
        }
        drag.Cancel(context);
        presenter.Refresh(context);
        RequireX(source, 35);
        drag = presenter.BeginDrag(source.Current!, context, new(70, 50), default, 4);
        try
        {
            drag.Commit(context with { GridStyle = context.GridStyle with { MinorStrokeMilliPixels = 0 } }, new(80, 50), default);
            throw new InvalidOperationException("Bad release redraw accepted.");
        }
        catch (EcgPaperGridException)
        {
            if (source.Current is not null || !drag.IsFinished)
            { throw new InvalidOperationException("Release paint failure reopened the committed gesture."); }
        }
        presenter.Refresh(context);
        RequireX(source, 40);
        presenter.Withdraw();
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        Console.WriteLine("ok: native drag offset, retained redraw, final release, rollback and safety recovery");
    }

    private static void RequireX(CapturedRecordSvgPresentation source, int expected)
    {
        if (source.Current?.Display.Content.Content.Display.Content.Content.Study.Measurement?.First?.X.WholePixels != expected)
        { throw new InvalidOperationException("Native drag redraw did not preserve the expected data coordinate."); }
    }
}
