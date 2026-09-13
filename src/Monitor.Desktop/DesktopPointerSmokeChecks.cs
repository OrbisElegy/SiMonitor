// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Avalonia;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

internal static class DesktopPointerSmokeChecks
{
    public static void Verify(MainWindow window)
    {
        RequireCoordinate(-0.0, 0, 1);
        RequireCoordinate(-1.5, -3, 2);
        RequireCoordinate(0.1, 3602879701896397, 36028797018963968);
        RequireCoordinate(double.Epsilon, 1, BigInteger.One << 1074);
        RequireCoordinate(double.MaxValue, ((BigInteger.One << 53) - 1) << 971, 1);

        CapturedRecordSvgPresentation source = DesktopStudySmokeFixture.CreatePresentation(activeInstance: true);
        RecordStudyPresenter presenter = new(window, source);
        Refresh(presenter);
        CapturedRecordSvgInputSession input = source.Current!;
        CapturedRecordSvgPublication before = source.Publication;
        Point origin = new(-7.25, 11.5);
        RequireHit(presenter, input, new(42.75, 61.5), origin, 4, RecordCursorHits.First);
        RequireHit(presenter, input, new(46.75, 61.5), origin, 4, RecordCursorHits.First);
        RequireHit(presenter, input, new(double.BitIncrement(46.75), 61.5), origin, 4, RecordCursorHits.None);
        RequireHit(presenter, input, new(142.75, 81.5), origin, 4, RecordCursorHits.Second);
        RequireHit(presenter, input, new(100, 60), default, 60, RecordCursorHits.First | RecordCursorHits.Second);
        try
        {
            Hit(presenter, input, new(-20, -20), default, 4);
            throw new InvalidOperationException("Out-of-plot native point accepted.");
        }
        catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.InvalidPoint") { }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            ExpectInvalid(() => Hit(presenter, input, new(invalid, 50), default, 4));
            ExpectInvalid(() => Hit(presenter, input, new(50, 50), new(0, invalid), 4));
            ExpectInvalid(() => Hit(presenter, input, new(50, 50), default, invalid));
        }
        ExpectInvalid(() => Hit(presenter, input, new(50, 50), default, 0));
        ExpectInvalid(() => Hit(presenter, input, new(50, 50), default, -1));
        if (!ReferenceEquals(before, source.Publication) || !ReferenceEquals(before, window.CurrentPublication))
        { throw new InvalidOperationException("Hit query or invalid native coordinates changed publication."); }
        Refresh(presenter);
        before = source.Publication;
        try
        {
            Hit(presenter, input, new(50, 50), default, 4);
            throw new InvalidOperationException("Old native hit context accepted.");
        }
        catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.StaleRenderedView")
        {
            if (!ReferenceEquals(before, window.CurrentPublication))
            { throw new InvalidOperationException("Old query removed current native content."); }
        }
        try
        {
            presenter.HitTest(source.Current!, false, DesktopStudySmokeFixture.Layout,
                DesktopStudySmokeFixture.Screen, new(50, 50), default, 4);
            throw new InvalidOperationException("Unsafe native hit accepted.");
        }
        catch (Ecg12ViewAdmissionException)
        {
            if (source.Current is not null || !window.HasNoRecordContent || window.CurrentPublication?.Status != CapturedRecordSvgStatus.Denied)
            { throw new InvalidOperationException("Unsafe hit retained native input."); }
        }
        Refresh(presenter);
        RequireHit(presenter, source.Current!, new(50, 50), default, 4, RecordCursorHits.First);
        presenter.Withdraw();
        window.ApplyPublication(new(CapturedRecordSvgStatus.NotRendered, "SvgPresentation.NotRendered", null));
        Console.WriteLine("ok: exact native coordinates, scaled cursor hits, ambiguity and stale/safety gates");
    }

    private static RecordCursorHits Hit(RecordStudyPresenter presenter, CapturedRecordSvgInputSession input,
        Point point, Point origin, double radius) => presenter.HitTest(input, true,
            DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen, point, origin, radius);

    private static void RequireHit(RecordStudyPresenter presenter, CapturedRecordSvgInputSession input,
        Point point, Point origin, double radius, RecordCursorHits expected)
    {
        if (Hit(presenter, input, point, origin, radius) != expected)
        { throw new InvalidOperationException("Native logical hit did not match the displayed cursor."); }
    }

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Invalid native coordinate/radius accepted.");
    }

    private static void RequireCoordinate(double value, BigInteger numerator, BigInteger denominator)
    {
        ExactPlotCoordinate result = NativeLogicalCoordinate.FromDouble(value);
        if (result.Numerator != numerator || result.Denominator != denominator)
        { throw new InvalidOperationException("Native coordinate was rounded instead of preserved exactly."); }
    }

    private static void Refresh(RecordStudyPresenter presenter) => presenter.Refresh(true,
        DesktopStudySmokeFixture.Layout, DesktopStudySmokeFixture.Screen,
        DesktopStudySmokeFixture.GridStyle, DesktopStudySmokeFixture.CursorStyle, false);
}
