// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class Ecg12ScreenTransformSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FitTransformUsesLimitingAxis), FitTransformUsesLimitingAxis),
        new(nameof(ExplicitTransformRoundTripsExactCoordinates), ExplicitTransformRoundTripsExactCoordinates),
        new(nameof(TransformRejectsMalformedInputs), TransformRejectsMalformedInputs),
        new(nameof(RestoredZoomRecomputesForCurrentArea), RestoredZoomRecomputesForCurrentArea),
    ];

    private static void FitTransformUsesLimitingAxis()
    {
        Ecg12ZoomState fit = new(Ecg12ZoomMode.FitPage, 1, 1);
        Ecg12ScreenTransform width = Ecg12ScreenTransform.Resolve(fit, 300, 200, 100, 100);
        Check.That(width.Factor == new ExactPlotCoordinate(1, 3) && width.Width == new ExactPlotCoordinate(100, 1) &&
            width.Height == new ExactPlotCoordinate(200, 3), "width-limited fit retains fractional height without distortion");
        Ecg12ScreenTransform height = Ecg12ScreenTransform.Resolve(fit, 300, 200, 600, 100);
        Check.That(height.Factor == new ExactPlotCoordinate(1, 2) && height.Width == new ExactPlotCoordinate(150, 1), "height-limited fit uses one factor for both axes");
        Ecg12ScreenTransform large = Ecg12ScreenTransform.Resolve(fit, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
        Check.That(large.Factor == new ExactPlotCoordinate(1, 1), "equal extreme bounds compare without overflow");
    }

    private static void ExplicitTransformRoundTripsExactCoordinates()
    {
        Ecg12ScreenTransform transform = Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ExplicitScale, 3, 2), 300, 200, 100, 100);
        Check.That(transform.Width == new ExactPlotCoordinate(450, 1) && transform.Height == new ExactPlotCoordinate(300, 1), "explicit zoom may exceed available area without implicit fit");
        foreach (ExactPlotCoordinate original in new ExactPlotCoordinate[] { new(0, 1), new(-7, 3), new(17, 11), new(int.MaxValue, 1) })
        { Check.That(transform.Inverse(transform.Forward(original)) == original, "pointer inverse preserves exact coordinates including outside-page values"); }
        Ecg12ScreenTransform actual = Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ActualSize, 1, 1), 300, 200, 1, 1);
        Check.That(actual.Width == new ExactPlotCoordinate(300, 1), "100 percent does not shrink to available area");
    }

    private static void TransformRejectsMalformedInputs()
    {
        Ecg12ZoomState fit = new(Ecg12ZoomMode.FitPage, 1, 1);
        foreach (int[] bounds in new int[][] { [0, 1, 1, 1], [1, -1, 1, 1], [1, 1, 0, 1], [1, 1, 1, 0] })
        { Check.That(Reason(() => Ecg12ScreenTransform.Resolve(fit, bounds[0], bounds[1], bounds[2], bounds[3])) == "Ecg12Zoom.InvalidGeometry", "all geometry dimensions must be positive"); }
        Ecg12ScreenTransform accepted = Ecg12ScreenTransform.Resolve(fit, 10, 10, 5, 5);
        Check.That(Reason(() => accepted.Forward(new(1, 0))) == "Ecg12Zoom.InvalidCoordinate" &&
            Reason(() => accepted.Inverse(null!)) == "Ecg12Zoom.InvalidCoordinate" && accepted.Factor == new ExactPlotCoordinate(1, 2),
            "invalid point conversion leaves immutable transform usable");
        Check.That(Reason(() => Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ExplicitScale, 0, 1), 1, 1, 1, 1)) == "Ecg12Zoom.InvalidSelection", "invalid zoom state cannot enter geometry");
    }

    private static void RestoredZoomRecomputesForCurrentArea()
    {
        Ecg12ZoomSelection original = new(new(Ecg12ZoomMode.FitPage, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomSelection restored = Ecg12ZoomSelection.Restore(original.CaptureState(), SystemViewCommandAssessmentPolicy.CourseLocked);
        Ecg12ScreenTransform before = Ecg12ScreenTransform.Resolve(original.Selection, 300, 200, 300, 200);
        Ecg12ScreenTransform after = Ecg12ScreenTransform.Resolve(restored.Selection, 300, 200, 150, 100);
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
