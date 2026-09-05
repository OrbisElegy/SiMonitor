// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class EcgVerticalGeometrySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(EcgVoltageUsesExplicitMillivoltGain), EcgVoltageUsesExplicitMillivoltGain),
        new(nameof(VerticalEdgesRemainExactWithoutClamping), VerticalEdgesRemainExactWithoutClamping),
        new(nameof(EquivalentInputsAndExtremeWidthsStayDeterministic), EquivalentInputsAndExtremeWidthsStayDeterministic),
        new(nameof(InvalidVerticalPlansFailWithoutChangingAcceptedGeometry), InvalidVerticalPlansFailWithoutChangingAcceptedGeometry),
    ];

    private static EcgVerticalScale Scale() => new(10, 100, 60, 20, 1);

    private static void EcgVoltageUsesExplicitMillivoltGain()
    {
        EcgVerticalScale scale = Scale();
        Check.That(EcgVerticalGeometry.MapMicrovolts(scale, 1000, 1) == new EcgVerticalPosition(40, 1, VerticalPlotRelation.WithinPlot) &&
            EcgVerticalGeometry.MapMicrovolts(scale, -1000, 1) == new EcgVerticalPosition(80, 1, VerticalPlotRelation.WithinPlot) &&
            EcgVerticalGeometry.MapMicrovolts(scale, 0, 1) == new EcgVerticalPosition(60, 1, VerticalPlotRelation.WithinPlot),
            "one millivolt shifts by resolved gain, positive upward and negative downward");
        Check.That(EcgVerticalGeometry.MapMicrovolts(scale, 1, 3) == new EcgVerticalPosition(8999, 150, VerticalPlotRelation.WithinPlot),
            "fractional microvolts retain exact subpixel position");
        Check.That(EcgVerticalGeometry.MapMicrovolts(scale with { PixelsPerMillivoltNumerator = 40 }, 1000, 1).PixelNumerator == 20,
            "explicit doubled gain doubles displacement from zero baseline");
    }

    private static void VerticalEdgesRemainExactWithoutClamping()
    {
        Check.That(EcgVerticalGeometry.MapMicrovolts(Scale(), 2500, 1) == new EcgVerticalPosition(10, 1, VerticalPlotRelation.WithinPlot) &&
            EcgVerticalGeometry.MapMicrovolts(Scale(), -2500, 1) == new EcgVerticalPosition(110, 1, VerticalPlotRelation.WithinPlot),
            "geometric top and bottom endpoints lie on the closed clipping boundary");
        Check.That(EcgVerticalGeometry.MapMicrovolts(Scale(), 2501, 1) == new EcgVerticalPosition(499, 50, VerticalPlotRelation.AbovePlot) &&
            EcgVerticalGeometry.MapMicrovolts(Scale(), -2501, 1) == new EcgVerticalPosition(5501, 50, VerticalPlotRelation.BelowPlot),
            "points one microvolt outside remain outside rather than becoming flat boundary samples");
    }

    private static void EquivalentInputsAndExtremeWidthsStayDeterministic()
    {
        EcgVerticalScale scale = Scale();
        EcgVerticalPosition original = EcgVerticalGeometry.MapMicrovolts(scale, 1, 3);
        Check.That(original == EcgVerticalGeometry.MapMicrovolts(scale with
        {
            PixelsPerMillivoltNumerator = 40,
            PixelsPerMillivoltDenominator = 2,
        }, 2, 6), "equivalent gain and amplitude fractions canonicalize identically");
        EcgVerticalScale large = new(0, int.MaxValue, int.MaxValue, uint.MaxValue, uint.MaxValue);
        EcgVerticalPosition high = EcgVerticalGeometry.MapMicrovolts(large, long.MaxValue, 1);
        EcgVerticalPosition low = EcgVerticalGeometry.MapMicrovolts(large, long.MinValue, 1);
        Check.That(high.Relation == VerticalPlotRelation.AbovePlot && low.Relation == VerticalPlotRelation.BelowPlot &&
            EcgVerticalGeometry.MapMicrovolts(large, 0, uint.MaxValue) == new EcgVerticalPosition(int.MaxValue, 1, VerticalPlotRelation.WithinPlot),
            "maximum input widths retain signs and normalize zero without arithmetic overflow");
        Check.That(EcgVerticalGeometry.MapMicrovolts(scale with { }, 1, 3) == original,
            "reconstruction from immutable resolved inputs reproduces exact geometry without accumulated state");
    }

    private static void InvalidVerticalPlansFailWithoutChangingAcceptedGeometry()
    {
        EcgVerticalScale scale = Scale();
        EcgVerticalPosition accepted = EcgVerticalGeometry.MapMicrovolts(scale, 1000, 1);
        foreach (EcgVerticalScale invalid in new[]
        {
            scale with { PlotTopPixels = -1 }, scale with { PlotHeightPixels = 0 },
            scale with { PlotTopPixels = int.MaxValue }, scale with { ZeroBaselinePixels = 9 },
            scale with { ZeroBaselinePixels = 111 }, scale with { PixelsPerMillivoltNumerator = 0 },
            scale with { PixelsPerMillivoltDenominator = 0 },
        })
        {
            Check.That(Reason(() => EcgVerticalGeometry.MapMicrovolts(invalid, 1, 1)) == "EcgGeometry.InvalidScale",
                "invalid bounds, baseline and unresolved gain fail closed");
        }
        Check.That(Reason(() => EcgVerticalGeometry.MapMicrovolts(scale, 1, 0)) == "EcgGeometry.InvalidAmplitude" &&
            EcgVerticalGeometry.MapMicrovolts(scale, 1000, 1) == accepted,
            "invalid denominator cannot change previously accepted geometry");
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (EcgVerticalGeometryException exception) { return exception.ReasonCode; }
    }
}
