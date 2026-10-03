// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class Ecg12ScreenTransformSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ViewportMappingValidatesAndUsesUniformLimitingAxis), ViewportMappingValidatesAndUsesUniformLimitingAxis),
        new(nameof(ExplicitTransformRoundTripsExactCoordinates), ExplicitTransformRoundTripsExactCoordinates),
        new(nameof(TransformRejectsMalformedInputs), TransformRejectsMalformedInputs),
        new(nameof(RestoredFitTransformUsesCurrentLimitingAxis), RestoredFitTransformUsesCurrentLimitingAxis),
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

    private static void ExplicitTransformRoundTripsExactCoordinates()
    {
        (uint Numerator, uint Denominator, ExactPlotCoordinate WidthPixels, ExactPlotCoordinate HeightPixels,
            ExactPlotCoordinate Translated, ExactPlotCoordinate Outside)[] cases =
        [
            (3, 2, new(450, 1), new(300, 1), new(85, 1), new(-2, 3)),
            (5, 3, new(500, 1), new(1000, 3), new(280, 3), new(-3, 5)),
        ];
        foreach (var item in cases)
        {
            Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, item.Numerator, item.Denominator), SystemViewCommandAssessmentPolicy.Enabled);
            var restored = Ecg12ZoomSelection.Restore(zoom.CaptureState(), SystemViewCommandAssessmentPolicy.Disabled);
            var transform = Ecg12ScreenTransform.Resolve(restored.Selection, 300, 200, 100, 100);
            Check.That(transform.Width == item.WidthPixels && transform.Height == item.HeightPixels && !restored.CaptureDisplay().CanSelect,
                "saved explicit zoom retains exact dimensions beyond the viewport while selection stays disabled");
            foreach (ExactPlotCoordinate logical in new ExactPlotCoordinate[] { new(0, 1), new(-7, 3), new(17, 11), new(123, 11), new(int.MaxValue, 1) })
            {
                Check.That(transform.Inverse(transform.Forward(logical)) == logical, "local coordinate round trip stays exact");
                foreach (ExactPlotCoordinate origin in new ExactPlotCoordinate[] { new(10, 1), new(-17, 7), new(19, 7) })
                    Check.That(transform.InverseAt(transform.ForwardAt(logical, origin), origin) == logical,
                        "window coordinate round trip retains fractional positive and negative scrolled origins");
            }
            Check.That(transform.InverseAt(new(10, 1), new(10, 1)) == new ExactPlotCoordinate(0, 1), "page origin maps to logical zero");
            Check.That(transform.ForwardAt(new(50, 1), new(10, 1)) == item.Translated &&
                transform.InverseAt(item.Translated, new(10, 1)) == new ExactPlotCoordinate(50, 1),
                "translation occurs after forward scale and before inverse scale");
            Check.That(transform.InverseAt(new(-24, 7), new(-17, 7)) == item.Outside,
                "outside-page points remain outside for downstream hit bounds checks");
        }
        var actual = Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ActualSize, 1, 1), 300, 200, 1, 1);
        Check.That(actual.Width == new ExactPlotCoordinate(300, 1) &&
            actual.ForwardAt(new(int.MaxValue, 1), new(int.MaxValue, 1)) == new ExactPlotCoordinate(2L * int.MaxValue, 1),
            "actual size ignores available area and wide translated coordinates do not overflow");
    }

    private static void TransformRejectsMalformedInputs()
    {
        Ecg12ZoomState fit = new(Ecg12ZoomMode.FitPage, 1, 1);
        foreach (int[] bounds in new int[][] { [0, 1, 1, 1], [1, -1, 1, 1], [1, 1, 0, 1], [1, 1, 1, 0] })
        { Check.That(Reason(() => Ecg12ScreenTransform.Resolve(fit, bounds[0], bounds[1], bounds[2], bounds[3])) == "Ecg12Zoom.InvalidGeometry", "all geometry dimensions must be positive"); }
        var accepted = Ecg12ScreenTransform.Resolve(fit, 10, 10, 5, 5);
        Check.That(Reason(() => accepted.Forward(new(1, 0))) == "Ecg12Zoom.InvalidCoordinate" &&
            Reason(() => accepted.Inverse(null!)) == "Ecg12Zoom.InvalidCoordinate" &&
            Reason(() => accepted.ForwardAt(new(1, 1), new(1, 0))) == "Ecg12Zoom.InvalidCoordinate" &&
            Reason(() => accepted.InverseAt(null!, new(1, 1))) == "Ecg12Zoom.InvalidCoordinate" &&
            Reason(() => accepted.InverseAt(new(1, 1), null!)) == "Ecg12Zoom.InvalidCoordinate" &&
            accepted.Factor == new ExactPlotCoordinate(1, 2) && accepted.Width == new ExactPlotCoordinate(5, 1),
            "invalid point conversion leaves immutable transform usable");
        Check.That(Reason(() => Ecg12ScreenTransform.Resolve(new(Ecg12ZoomMode.ExplicitScale, 0, 1), 1, 1, 1, 1)) == "Ecg12Zoom.InvalidSelection", "invalid zoom state cannot enter geometry");
    }

    private static void RestoredFitTransformUsesCurrentLimitingAxis()
    {
        Ecg12ZoomSelection original = new(new(Ecg12ZoomMode.FitPage, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        var restored = Ecg12ZoomSelection.Restore(original.CaptureState(), SystemViewCommandAssessmentPolicy.CourseLocked);
        var before = Ecg12ScreenTransform.Resolve(original.Selection, 300, 200, 300, 200);
        Check.That(before.Factor == new ExactPlotCoordinate(1, 1) && !restored.CaptureDisplay().CanSelect,
            "saved fit intent remains usable while current selection permission is locked");
        (int AreaWidthPixels, int AreaHeightPixels, ExactPlotCoordinate Factor, ExactPlotCoordinate WidthPixels, ExactPlotCoordinate HeightPixels)[] cases =
        [
            (100, 100, new(1, 3), new(100, 1), new(200, 3)),
            (600, 100, new(1, 2), new(150, 1), new(100, 1)),
            (150, 100, new(1, 2), new(150, 1), new(100, 1)),
        ];
        foreach (var item in cases)
        {
            var after = Ecg12ScreenTransform.Resolve(restored.Selection, 300, 200, item.AreaWidthPixels, item.AreaHeightPixels);
            Check.That(after.Factor == item.Factor && after.Width == item.WidthPixels && after.Height == item.HeightPixels,
                "current width, height or tied limiting axes retain one exact aspect ratio");
            Check.That(after.Inverse(after.Forward(new(123, 7))) == new ExactPlotCoordinate(123, 7),
                "resized recovered view retains logical cursor coordinates");
        }
        var large = Ecg12ScreenTransform.Resolve(restored.Selection, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
        Check.That(large.Factor == new ExactPlotCoordinate(1, 1), "equal extreme bounds compare without overflow");
    }

    private static string Reason(Action action)
    {
        try { action(); }
        catch (Ecg12ZoomSelectionException exception) { return exception.ReasonCode; }
        throw new InvalidOperationException("Expected zoom geometry rejection.");
    }
}
