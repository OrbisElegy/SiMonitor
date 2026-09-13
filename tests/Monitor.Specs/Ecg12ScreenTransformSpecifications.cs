// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class Ecg12ScreenTransformSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ViewportMappingValidatesAndUsesUniformLimitingAxis), ViewportMappingValidatesAndUsesUniformLimitingAxis),
        new(nameof(WindowCoordinatesTranslateBeforeInverseZoom), WindowCoordinatesTranslateBeforeInverseZoom),
        new(nameof(WindowCoordinatesKeepNegativeScrolledOriginsExact), WindowCoordinatesKeepNegativeScrolledOriginsExact),
        new(nameof(WindowCoordinateFailurePreservesTransform), WindowCoordinateFailurePreservesTransform),

        new(nameof(FitTransformUsesLimitingAxis), FitTransformUsesLimitingAxis),
        new(nameof(ExplicitTransformRoundTripsExactCoordinates), ExplicitTransformRoundTripsExactCoordinates),
        new(nameof(TransformRejectsMalformedInputs), TransformRejectsMalformedInputs),
        new(nameof(RestoredZoomRecomputesForCurrentArea), RestoredZoomRecomputesForCurrentArea),
    ];

    private static void ViewportMappingValidatesAndUsesUniformLimitingAxis()
    {
        var transform = Ecg12ScreenTransform.ResolveViewport(100, 200, new(50, 1), new(150, 1));
        Check.That(transform.Factor == new ExactPlotCoordinate(1, 2) && transform.Height == new ExactPlotCoordinate(100, 1),
            "viewport mapping preserves aspect ratio with unused height");
        Check.That(Reason(() => Ecg12ScreenTransform.ResolveViewport(0, 200, new(50, 1), new(150, 1))) == "Ecg12Zoom.InvalidGeometry" &&
            Reason(() => Ecg12ScreenTransform.ResolveViewport(100, 200, new(0, 1), new(150, 1))) == "Ecg12Zoom.InvalidGeometry" &&
            Reason(() => Ecg12ScreenTransform.ResolveViewport(100, 200, new(1, 0), new(150, 1))) == "Ecg12Zoom.InvalidCoordinate",
            "invalid page or serialized viewport cannot create mapping");
    }

    private static void WindowCoordinatesTranslateBeforeInverseZoom()
    {
        var transform = Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ExplicitScale, 3, 2), 100, 100, 200, 200);
        Check.That(transform.ForwardAt(new(50, 1), new(10, 1)) == new ExactPlotCoordinate(85, 1) &&
            transform.InverseAt(new(85, 1), new(10, 1)) == new ExactPlotCoordinate(50, 1),
            "window origin is translated after forward scale and before inverse scale");
        Check.That(transform.InverseAt(new(10, 1), new(10, 1)) == new ExactPlotCoordinate(0, 1), "page origin maps to exact logical zero");
    }

    private static void WindowCoordinatesKeepNegativeScrolledOriginsExact()
    {
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 5, 3), SystemViewCommandAssessmentPolicy.Enabled);
        var restored = Ecg12ZoomSelection.Restore(zoom.CaptureState(), SystemViewCommandAssessmentPolicy.Disabled);
        var transform = Ecg12ScreenTransform.Resolve(restored.Selection, 100, 100, 50, 50);
        ExactPlotCoordinate origin = new(-17, 7);
        ExactPlotCoordinate logical = new(123, 11);
        Check.That(transform.InverseAt(transform.ForwardAt(logical, origin), origin) == logical,
            "fractional negative scrolled origin preserves exact recovered coordinates");
        Check.That(transform.InverseAt(new(-24, 7), origin) == new ExactPlotCoordinate(-3, 5),
            "outside-page coordinates remain outside for downstream hit bounds checks");
        Check.That(transform.InverseAt(transform.ForwardAt(logical, new(19, 7)), new(19, 7)) == logical && !restored.CaptureDisplay().CanSelect,
            "current window origin is independent of saved zoom and selection permission");
    }

    private static void WindowCoordinateFailurePreservesTransform()
    {
        var transform = Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ActualSize, 1, 1), 100, 100, 50, 50);
        Check.That(Reason(() => transform.ForwardAt(new(1, 1), new(1, 0))) == "Ecg12Zoom.InvalidCoordinate" &&
            Reason(() => transform.InverseAt(null!, new(1, 1))) == "Ecg12Zoom.InvalidCoordinate" &&
            Reason(() => transform.InverseAt(new(1, 1), null!)) == "Ecg12Zoom.InvalidCoordinate",
            "malformed window points and origins reject consistently");
        Check.That(transform.ForwardAt(new(int.MaxValue, 1), new(int.MaxValue, 1)) == new ExactPlotCoordinate(2L * int.MaxValue, 1) &&
            transform.Width == new ExactPlotCoordinate(100, 1), "wide translated coordinates do not overflow or mutate the transform");
    }

    private static void FitTransformUsesLimitingAxis()
    {
        Ecg12ZoomState fit = new(Ecg12ZoomMode.FitPage, 1, 1);
        var width = Ecg12ScreenTransform.Resolve(fit, 300, 200, 100, 100);
        Check.That(width.Factor == new ExactPlotCoordinate(1, 3) && width.Width == new ExactPlotCoordinate(100, 1) &&
            width.Height == new ExactPlotCoordinate(200, 3), "width-limited fit retains fractional height without distortion");
        var height = Ecg12ScreenTransform.Resolve(fit, 300, 200, 600, 100);
        Check.That(height.Factor == new ExactPlotCoordinate(1, 2) && height.Width == new ExactPlotCoordinate(150, 1), "height-limited fit uses one factor for both axes");
        var large = Ecg12ScreenTransform.Resolve(fit, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
        Check.That(large.Factor == new ExactPlotCoordinate(1, 1), "equal extreme bounds compare without overflow");
    }

    private static void ExplicitTransformRoundTripsExactCoordinates()
    {
        var transform = Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ExplicitScale, 3, 2), 300, 200, 100, 100);
        Check.That(transform.Width == new ExactPlotCoordinate(450, 1) && transform.Height == new ExactPlotCoordinate(300, 1), "explicit zoom may exceed available area without implicit fit");
        foreach (ExactPlotCoordinate original in new ExactPlotCoordinate[] { new(0, 1), new(-7, 3), new(17, 11), new(int.MaxValue, 1) })
        { Check.That(transform.Inverse(transform.Forward(original)) == original, "pointer inverse preserves exact coordinates including outside-page values"); }
        var actual = Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ActualSize, 1, 1), 300, 200, 1, 1);
        Check.That(actual.Width == new ExactPlotCoordinate(300, 1), "100 percent does not shrink to available area");
    }

    private static void TransformRejectsMalformedInputs()
    {
        Ecg12ZoomState fit = new(Ecg12ZoomMode.FitPage, 1, 1);
        foreach (int[] bounds in new int[][] { [0, 1, 1, 1], [1, -1, 1, 1], [1, 1, 0, 1], [1, 1, 1, 0] })
        { Check.That(Reason(() => Ecg12ScreenTransform.Resolve(fit, bounds[0], bounds[1], bounds[2], bounds[3])) == "Ecg12Zoom.InvalidGeometry", "all geometry dimensions must be positive"); }
        var accepted = Ecg12ScreenTransform.Resolve(fit, 10, 10, 5, 5);
        Check.That(Reason(() => accepted.Forward(new(1, 0))) == "Ecg12Zoom.InvalidCoordinate" &&
            Reason(() => accepted.Inverse(null!)) == "Ecg12Zoom.InvalidCoordinate" && accepted.Factor == new ExactPlotCoordinate(1, 2),
            "invalid point conversion leaves immutable transform usable");
        Check.That(Reason(() => Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ExplicitScale, 0, 1), 1, 1, 1, 1)) == "Ecg12Zoom.InvalidSelection", "invalid zoom state cannot enter geometry");
    }

    private static void RestoredZoomRecomputesForCurrentArea()
    {
        Ecg12ZoomSelection original = new(new(Ecg12ZoomMode.FitPage, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        var restored = Ecg12ZoomSelection.Restore(original.CaptureState(), SystemViewCommandAssessmentPolicy.CourseLocked);
        var before = Ecg12ScreenTransform.Resolve(original.Selection, 300, 200, 300, 200);
        var after = Ecg12ScreenTransform.Resolve(restored.Selection, 300, 200, 150, 100);
        Check.That(before.Factor == new ExactPlotCoordinate(1, 1) && after.Factor == new ExactPlotCoordinate(1, 2) && !restored.CaptureDisplay().CanSelect,
            "current geometry can resolve saved intent while selection stays locked");
        Check.That(after.Inverse(after.Forward(new(123, 7))) == new ExactPlotCoordinate(123, 7), "resized recovered view retains logical cursor coordinate");
    }

    private static string Reason(Action action)
    {
        try { action(); }
        catch (Ecg12ZoomSelectionException exception) { return exception.ReasonCode; }
        throw new InvalidOperationException("Expected zoom geometry rejection.");
    }
}
