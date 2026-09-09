// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

// Diagnostic logical-pixel preview, not a skin or physically calibrated export.
public static class EcgStripSvgPreview
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public static string Render(EcgStripCheckpoint checkpoint, int maximumSamples, int maximumSegments,
        CancellationToken cancellationToken = default)
    {
        ReconstructedEcgStrip strip = new EcgStripReconstructor(maximumSamples, maximumSegments).Replace(checkpoint, cancellationToken);
        SweepFramePathState seed = strip.Checkpoint.Source.Frame;
        const string teachingLabel = "TEACHING SIMULATION / GEOMETRY PREVIEW / NOT FOR CLINICAL USE";
        string stateLabel = $"{seed.Presentation.SessionRunState} / {seed.Presentation.TemporalViewMode} / {seed.Presentation.ContinuityState.DataAvailability}";
        // Reserve annotation space without resizing the patient plot or glyph.
        long width = Math.Max((long)seed.PlotLeftPixels + seed.PlotWidthPixels,
            Math.Max(teachingLabel.Length, stateLabel.Length) * 5L + 16);
        long bottom = (long)seed.PlotTopPixels + seed.PlotHeightPixels;
        XElement root = new(Svg + "svg", new XAttribute("viewBox", $"0 0 {Integer(width)} {Integer(bottom + 32)}"),
            new XAttribute("width", Integer(width)), new XAttribute("height", Integer(bottom + 32)),
            new XAttribute("role", "img"), new XElement(Svg + "title", "ECG teaching geometry preview"),
            new XElement(Svg + "desc", "Logical pixels only; not physically calibrated. Diagnostic preview, not a product skin."));
        root.Add(new XElement(Svg + "rect", new XAttribute("width", Integer(width)), new XAttribute("height", Integer(bottom + 32)),
            new XAttribute("fill", "#ffffff")));
        XElement definitions = new(Svg + "defs");
        definitions.Add(new XElement(Svg + "clipPath", new XAttribute("id", "gutter"), new XAttribute("clipPathUnits", "userSpaceOnUse"),
            new XElement(Svg + "rect", new XAttribute("x", Integer(strip.Calibration.GutterLeftPixels)),
                new XAttribute("y", Integer(seed.PlotTopPixels)),
                new XAttribute("width", Integer(strip.Calibration.GutterRightPixels - strip.Calibration.GutterLeftPixels)),
                new XAttribute("height", Integer(seed.PlotHeightPixels)))));
        XElement trace = new(Svg + "g", new XAttribute("id", "trace"), new XAttribute("fill", "none"),
            new XAttribute("stroke", "#000000"), new XAttribute("stroke-width", "1"));
        for (int index = 0; index < strip.PatientFrame.Geometry.Regions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SweepPlotRegion region = strip.PatientFrame.Geometry.Regions[index];
            string id = "region-" + Integer(index);
            string left = X(region.StartX), right = X(region.EndExclusiveX);
            definitions.Add(new XElement(Svg + "clipPath", new XAttribute("id", id), new XAttribute("clipPathUnits", "userSpaceOnUse"),
                new XElement(Svg + "path", new XAttribute("d", $"M {left} {Integer(seed.PlotTopPixels)} H {right} V {Integer(bottom)} H {left} Z"))));
            if (region.Kind == SweepTraceRegionKind.NoDataBaseline)
            {
                trace.Add(new XElement(Svg + "path", new XAttribute("d", $"M {left} {Integer(seed.VerticalScale!.ZeroBaselinePixels)} H {right}"),
                    new XAttribute("stroke-dasharray", "2 2"), new XAttribute("clip-path", $"url(#{id})")));
            }
        }
        StringBuilder?[] patientPaths = new StringBuilder?[strip.PatientFrame.Geometry.Regions.Count];
        foreach (SweepFrameSegment segment in strip.PatientFrame.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Closed clipping retains boundary-only geometry; the right edge
            // is exclusive. Decide ownership before rounding SVG coordinates.
            SweepPixelPosition right = strip.PatientFrame.Geometry.Regions[segment.RegionIndex].EndExclusiveX;
            if (AtRightEdge(segment.Segment.Start.X, right) && AtRightEdge(segment.Segment.End.X, right)) { continue; }
            StringBuilder path = patientPaths[segment.RegionIndex] ??= new StringBuilder();
            if (path.Length != 0) { path.Append(' '); }
            // Keep every segment independent: batching must never bridge a source gap.
            path.Append("M ").Append(Point(segment.Segment.Start)).Append(" L ").Append(Point(segment.Segment.End));
        }
        for (int index = 0; index < patientPaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (patientPaths[index] is not { } path) { continue; }
            trace.Add(new XElement(Svg + "path", new XAttribute("d", path.ToString()),
                new XAttribute("clip-path", $"url(#region-{Integer(index)})")));
        }
        root.Add(definitions, trace);
        string pulse = string.Join(" ", strip.Calibration.Points.Select((point, index) =>
            $"{(index == 0 ? "M" : "L")} {X(point.X)} {Number((BigInteger)point.Y.PixelNumerator, (BigInteger)point.Y.PixelDenominator)}"));
        root.Add(new XElement(Svg + "path", new XAttribute("id", "calibration"), new XAttribute("d", pulse),
            new XAttribute("clip-path", "url(#gutter)"),
            new XAttribute("fill", "none"), new XAttribute("stroke", "#000000"), new XAttribute("stroke-width", "1")));
        root.Add(Label(teachingLabel, bottom + 12), Label(stateLabel, bottom + 24));
        cancellationToken.ThrowIfCancellationRequested();
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static XElement Label(string value, long baseline) => new(Svg + "text",
        new XAttribute("x", "8"), new XAttribute("y", Integer(baseline)),
        new XAttribute("font-size", "8"), new XAttribute("font-family", "monospace"),
        new XAttribute("textLength", Integer(value.Length * 5L)),
        new XAttribute("lengthAdjust", "spacingAndGlyphs"), value);
    private static bool AtRightEdge(ExactPlotCoordinate x, SweepPixelPosition right) =>
        x.Numerator * right.FractionDenominator ==
        ((BigInteger)right.WholePixels * right.FractionDenominator + right.FractionNumerator) * x.Denominator;
    private static string Point(ClippedSweepPoint p) => $"{Number(p.X.Numerator, p.X.Denominator)} {Number(p.Y.Numerator, p.Y.Denominator)}";
    private static string X(SweepPixelPosition x) => Number((BigInteger)x.WholePixels * x.FractionDenominator + x.FractionNumerator, x.FractionDenominator);

    // Raster-adapter serialization only: six decimal pixel places, nearest with
    // ties away from zero. Exact domain/checkpoint coordinates remain untouched.
    private static string Number(BigInteger numerator, BigInteger denominator)
    {
        BigInteger scaled = BigInteger.DivRem(BigInteger.Abs(numerator) * 1_000_000, denominator, out BigInteger remainder);
        if (remainder * 2 >= denominator) { scaled++; }
        string sign = numerator.Sign < 0 && scaled != 0 ? "-" : "";
        string fraction = (scaled % 1_000_000).ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');
        return sign + (scaled / 1_000_000).ToString(CultureInfo.InvariantCulture) + (fraction.Length == 0 ? "" : "." + fraction);
    }
}
