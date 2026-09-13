// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

internal static class DesktopPresenterSmokeChecks
{
    public static void VerifyClear(MainWindow window)
    {
        CapturedRecordSvgPresentation source = DesktopStudySmokeFixture.CreatePresentation(activeInstance: true);
        RecordStudyPresenter presenter = new(window, source);
        Refresh(presenter);
        CapturedRecordSvgInputSession old = source.Current!;
        Refresh(presenter);
        CapturedRecordSvgPublication current = source.Publication;
        try
        {
            Clear(presenter, old);
            throw new InvalidOperationException("Queued clear targeted a newer picture.");
        }
        catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.StaleRenderedView")
        {
            if (!ReferenceEquals(current, source.Publication) || !ReferenceEquals(current, window.CurrentPublication))
            { throw new InvalidOperationException("Stale command changed current publication."); }
        }
        var evidence = source.Current!.Display.Content.Content.Display.Content.Content.Study.Measurement;
        try
        {
            Clear(presenter, source.Current!, false);
            throw new InvalidOperationException("Clear without safety overlay accepted.");
        }
        catch (Ecg12ViewAdmissionException)
        { RequireEmpty(window, source, CapturedRecordSvgStatus.Denied, "Ecg12Admission.SafetyOverlayUnavailable"); }
        Refresh(presenter);
        if (!Equals(evidence, source.Current!.Display.Content.Content.Display.Content.Content.Study.Measurement))
        { throw new InvalidOperationException("Denied clear changed measurement evidence."); }
        Clear(presenter, source.Current!);
        RequireCleared(window, source);
        Clear(presenter, source.Current!);
        RequireCleared(window, source);
        presenter.Withdraw();

        source = DesktopStudySmokeFixture.CreatePresentation(hideMeasurement: true);
        presenter = new(window, source);
        Refresh(presenter);
        try
        {
            Clear(presenter, source.Current!);
            throw new InvalidOperationException("Disabled measurement clear accepted.");
        }
        catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.Disabled")
        { RequireEmpty(window, source, CapturedRecordSvgStatus.Failed, "RecordMeasurement.Disabled"); }

        source = DesktopStudySmokeFixture.CreatePresentation();
        presenter = new(window, source);
        Refresh(presenter);
        try
        {
            presenter.ClearPair(source.Current!, true, DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
                DesktopStudySmokeFixture.GridStyle with { MinorStrokeMilliPixels = 0 }, DesktopStudySmokeFixture.CursorStyle, false);
            throw new InvalidOperationException("Invalid redraw style accepted.");
        }
        catch (EcgPaperGridException)
        { RequireEmpty(window, source, CapturedRecordSvgStatus.Failed, "SvgPresentation.RenderFailed"); }
        Refresh(presenter);
        RequireCleared(window, source);
        presenter.Withdraw();
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        Console.WriteLine("ok: desktop clear redraw, stale command, policy denial and post-edit failure");
    }

    private static void Clear(RecordStudyPresenter presenter, CapturedRecordSvgInputSession input, bool overlay = true) =>
        presenter.ClearPair(input, overlay, DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
            DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);

    private static void RequireCleared(MainWindow window, CapturedRecordSvgPresentation source)
    {
        RequireReady(window, source);
        var display = source.Current!.Display;
        if (display.CursorOverlaySvg is not null ||
            display.Content.Content.Display.Content.Content.Study.Measurement?.ReasonCode != "RecordMeasurement.NoCursorPair")
        { throw new InvalidOperationException("Accepted clear did not redraw an empty measurement layer."); }
    }

    public static void Verify(MainWindow window)
    {
        CapturedRecordSvgPresentation source = DesktopStudySmokeFixture.CreatePresentation(activeInstance: true);
        RecordStudyPresenter presenter = new(window, source);
        Refresh(presenter);
        RequireReady(window, source);
        CapturedRecordSvgPublication before = window.CurrentPublication!;
        var evidence = source.Current!.Display.Content.Content.Display.Content.Content.Study.Measurement;
        bool backgroundRejected = false;
        try
        {
            Task.Run(presenter.Withdraw).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            backgroundRejected = true;
            if (!ReferenceEquals(before, window.CurrentPublication) || !ReferenceEquals(before, source.Publication))
            { throw new InvalidOperationException("Background call changed publication."); }
        }
        if (!backgroundRejected) { throw new InvalidOperationException("Background withdrawal accepted."); }
        try
        {
            presenter.Refresh(false, DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
                DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);
            throw new InvalidOperationException("Missing safety overlay accepted.");
        }
        catch (Ecg12ViewAdmissionException)
        { RequireEmpty(window, source, CapturedRecordSvgStatus.Denied, "Ecg12Admission.SafetyOverlayUnavailable"); }
        Refresh(presenter);
        RequireReady(window, source);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            presenter.Refresh(true, DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
                DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false, cancellation.Token);
            throw new InvalidOperationException("Cancelled refresh accepted.");
        }
        catch (OperationCanceledException)
        { RequireEmpty(window, source, CapturedRecordSvgStatus.Cancelled, "SvgPresentation.Cancelled"); }
        Refresh(presenter);
        try
        {
            presenter.Refresh(true, DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
                DesktopStudySmokeFixture.GridStyle with { MinorStrokeMilliPixels = 0 }, DesktopStudySmokeFixture.CursorStyle, false);
            throw new InvalidOperationException("Invalid grid style accepted.");
        }
        catch (EcgPaperGridException)
        { RequireEmpty(window, source, CapturedRecordSvgStatus.Failed, "SvgPresentation.RenderFailed"); }
        Refresh(presenter);
        presenter.Withdraw();
        presenter.Withdraw();
        RequireEmpty(window, source, CapturedRecordSvgStatus.Withdrawn, "SvgPresentation.Withdrawn");
        Refresh(presenter);
        RequireReady(window, source);
        if (!Equals(evidence, source.Current!.Display.Content.Content.Display.Content.Content.Study.Measurement))
        { throw new InvalidOperationException("Refresh failures or withdrawal changed measurement evidence."); }
        presenter.Withdraw();

        // Hidden cursors do not require an SVG style, but native construction
        // validates its pens. Exercise a failure after source Ready publication.
        source = DesktopStudySmokeFixture.CreatePresentation(hideMeasurement: true);
        presenter = new(window, source);
        Refresh(presenter);
        try
        {
            presenter.Refresh(true, DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
                DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle with { RadiusMilliPixels = 0 }, false);
            throw new InvalidOperationException("Invalid native cursor style accepted.");
        }
        catch (ArgumentOutOfRangeException)
        { RequireEmpty(window, source, CapturedRecordSvgStatus.Failed, "DesktopStudy.RenderFailed"); }
        Refresh(presenter);
        RequireReady(window, source);
        presenter.Withdraw();
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        Console.WriteLine("ok: desktop presenter refresh, denial, cancellation, native failure and recovery");
    }

    private static void Refresh(RecordStudyPresenter presenter) => presenter.Refresh(true,
        DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
        DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);

    private static void RequireReady(MainWindow window, CapturedRecordSvgPresentation source)
    {
        if (window.HasNoRecordContent || source.Current is null || !ReferenceEquals(window.CurrentPublication, source.Publication))
        { throw new InvalidOperationException("Presenter did not publish coherent native content."); }
    }

    private static void RequireEmpty(MainWindow window, CapturedRecordSvgPresentation source,
        CapturedRecordSvgStatus status, string reason)
    {
        if (!window.HasNoRecordContent || window.MeasurementReadoutText is not null || window.MeasurementAccessibilityText is not null || source.Current is not null || window.CurrentPublication is not { Input: null } publication ||
            publication.Status != status || publication.ReasonCode != reason)
        { throw new InvalidOperationException("Failed presenter retained content/input or lost failure status."); }
    }
}
