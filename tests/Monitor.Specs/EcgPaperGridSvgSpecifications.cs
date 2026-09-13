// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Xml.Linq;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Specs;

internal static class EcgPaperGridSvgSpecifications
{
    private static readonly EcgPaperGridSvgStyle Style = new("#f0cccc", "#cc9999", 500, 1000);
    public static Specification[] All =>
    [
        new(nameof(GridSvgBatchesAndClipsExplicitStyles), GridSvgBatchesAndClipsExplicitStyles),
        new(nameof(GridSvgSerializesFractionsWithoutCultureDependence), GridSvgSerializesFractionsWithoutCultureDependence),
        new(nameof(GridSvgRejectsInvalidStyleAndLimits), GridSvgRejectsInvalidStyleAndLimits),
        new(nameof(GridSvgCancellationAllowsFreshRendering), GridSvgCancellationAllowsFreshRendering),
    ];

    private static void GridSvgBatchesAndClipsExplicitStyles()
    {
        var root = XElement.Parse(EcgPaperGridSvg.Render(new(10, 20, 10, 10, 10, 20, 2, 1), 10, Style));
        XElement[] paths = root.Elements().ToArray();
        Check.That(root.Name.NamespaceName == "http://www.w3.org/2000/svg" && (string?)root.Attribute("viewBox") == "10 20 10 10" &&
            (string?)root.Attribute("overflow") == "hidden" && (string?)root.Attribute("pointer-events") == "none" && paths.Length == 2,
            "nested viewport clips grid strokes and batches each weight without intercepting pointer input");
        Check.That((string?)paths[0].Attribute("stroke") == Style.MinorColor && (string?)paths[0].Attribute("stroke-width") == "0.5" &&
            (string?)paths[1].Attribute("stroke") == Style.MajorColor && (string?)paths[1].Attribute("d") == "M 10 20 V 30 M 10 20 H 20",
            "minor precedes major with explicit styles and independent line subpaths");
        Check.That(((string)paths[0].Attribute("d")!).Count(c => c == 'M') == 8, "exclusive right/bottom centers create no extra grid lines");
    }

    private static void GridSvgSerializesFractionsWithoutCultureDependence()
    {
        CultureInfo prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var root = XElement.Parse(EcgPaperGridSvg.Render(new(0, 0, 1, 1, 0, 0, 1, 3), 6, Style));
            string path = (string)root.Elements().First().Attribute("d")!;
            Check.That(path.Contains("M 0.333333 0 V 1", StringComparison.Ordinal) && path.Contains("M 0.666667 0 V 1", StringComparison.Ordinal),
                "SVG coordinate rounding uses invariant decimal points at six places");
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    private static void GridSvgRejectsInvalidStyleAndLimits()
    {
        EcgPaperGridPlan plan = new(0, 0, 10, 10, 0, 0, 2, 1);
        foreach (EcgPaperGridSvgStyle invalid in new[] { Style with { MinorColor = "url(https://example.invalid/grid)" }, Style with { MajorStrokeMilliPixels = 0 } })
        {
            ExpectReason(() => EcgPaperGridSvg.Render(plan, 10, invalid), "PaperGrid.InvalidSvgStyle");
        }
        ExpectReason(() => EcgPaperGridSvg.Render(plan, 9, Style), "PaperGrid.LineLimitExceeded");
        Check.That(XElement.Parse(EcgPaperGridSvg.Render(plan, 10, Style)).Elements().Count() == 2,
            "valid retry renders after rejection without persistent partial output");
    }

    private static void GridSvgCancellationAllowsFreshRendering()
    {
        EcgPaperGridPlan plan = new(0, 0, 10, 10, 0, 0, 2, 1);
        string before = EcgPaperGridSvg.Render(plan, 10, Style);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { EcgPaperGridSvg.Render(plan, 10, Style, cancellation.Token); throw new InvalidOperationException("cancelled SVG accepted"); }
        catch (OperationCanceledException exception)
        { Check.That(exception.CancellationToken == cancellation.Token, "render cancellation retains caller token"); }
        Check.That(EcgPaperGridSvg.Render(plan, 10, Style) == before, "fresh rendering remains deterministic after cancelled request");
    }

    private static void ExpectReason(Action action, string expected)
    {
        try { action(); }
        catch (EcgPaperGridException exception)
        {
            Check.That(exception.ReasonCode == expected, "SVG grid rejects with stable reason");
            return;
        }
        throw new InvalidOperationException("invalid SVG request accepted");
    }
}
