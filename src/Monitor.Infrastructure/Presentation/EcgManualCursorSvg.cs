// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

public sealed record EcgManualCursorSvgStyle(string FirstColor, string SecondColor,
    uint StrokeMilliPixels, uint RadiusMilliPixels);

// Screen-only overlay, never a print/export layer. Inputs come from fresh admitted composition.
internal static class EcgManualCursorSvg
{
    internal static string? Render(CapturedRecordPageDisplay display, EcgVerticalScale scale,
        EcgManualCursorSvgStyle style, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecordMeasurementDisplay? measurement = display.Study.Measurement;
        if (measurement?.ReasonCode != "RecordMeasurement.Ready") { return null; }
        bool firstVisible = Visible(measurement.First), secondVisible = Visible(measurement.Second);
        if (!firstVisible && !secondVisible) { return null; }
        ArgumentNullException.ThrowIfNull(style);
        if (!EcgPaperGridSvg.IsColor(style.FirstColor) || !EcgPaperGridSvg.IsColor(style.SecondColor) ||
            style.StrokeMilliPixels == 0 || style.RadiusMilliPixels == 0)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidSvgStyle", nameof(style)); }
        RecordCursorViewport viewport = display.Viewport!;
        string left = Integer(viewport.PlotLeftPixels), top = Integer(scale.PlotTopPixels);
        string right = Integer((long)viewport.PlotLeftPixels + viewport.PlotWidthPixels), bottom = Integer((long)scale.PlotTopPixels + scale.PlotHeightPixels);
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement root = new(svg + "svg", new XAttribute("x", left), new XAttribute("y", top),
            new XAttribute("width", Integer(viewport.PlotWidthPixels)), new XAttribute("height", Integer(scale.PlotHeightPixels)),
            new XAttribute("viewBox", $"{left} {top} {Integer(viewport.PlotWidthPixels)} {Integer(scale.PlotHeightPixels)}"),
            new XAttribute("overflow", "hidden"), new XAttribute("fill", "none"),
            new XAttribute("pointer-events", "none"), new XAttribute("aria-hidden", "true"),
            new XAttribute("data-layer", "manual-measurement"),
            new XAttribute("stroke-width", SvgLogicalNumber.Format(style.StrokeMilliPixels, 1000)));
        if (firstVisible) { AddCursor(root, svg, measurement.First!, "first", style.FirstColor, style.RadiusMilliPixels, left, top, right, bottom); }
        if (secondVisible) { AddCursor(root, svg, measurement.Second!, "second", style.SecondColor, style.RadiusMilliPixels, left, top, right, bottom); }
        string result = root.ToString(SaveOptions.DisableFormatting);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static bool Visible(ProjectedRecordCursor? cursor) => cursor?.Y.Relation == VerticalPlotRelation.WithinPlot;

    private static void AddCursor(XElement root, XNamespace svg, ProjectedRecordCursor cursor, string name,
        string color, uint radius, string left, string top, string right, string bottom)
    {
        string x = SvgLogicalNumber.Format((BigInteger)cursor.X.WholePixels * cursor.X.FractionDenominator + cursor.X.FractionNumerator, cursor.X.FractionDenominator);
        string y = SvgLogicalNumber.Format((BigInteger)cursor.Y.PixelNumerator, (BigInteger)cursor.Y.PixelDenominator);
        root.Add(new XElement(svg + "g", new XAttribute("data-cursor", name), new XAttribute("stroke", color),
            new XElement(svg + "path", new XAttribute("d", $"M {x} {top} V {bottom} M {left} {y} H {right}")),
            new XElement(svg + "circle", new XAttribute("cx", x), new XAttribute("cy", y),
                new XAttribute("r", SvgLogicalNumber.Format(radius, 1000)))));
    }

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
}
