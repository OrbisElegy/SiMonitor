// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

public sealed record EcgPaperGridSvgStyle(string MinorColor, string MajorColor,
    uint MinorStrokeMilliPixels, uint MajorStrokeMilliPixels);

public static class EcgPaperGridSvg
{
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");
    public static string Render(EcgPaperGridPlan plan, int maximumLines, EcgPaperGridSvgStyle style,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(style);
        if (!IsColor(style.MinorColor) || !IsColor(style.MajorColor) ||
            style.MinorStrokeMilliPixels == 0 || style.MajorStrokeMilliPixels == 0)
        { throw new EcgPaperGridException("PaperGrid.InvalidSvgStyle", nameof(style)); }
        IReadOnlyList<EcgPaperGridLine> lines = EcgPaperGridGeometry.Build(plan, maximumLines, cancellationToken);
        StringBuilder minor = new(), major = new();
        string left = Integer(plan.LeftPixels), top = Integer(plan.TopPixels);
        string right = Integer((long)plan.LeftPixels + plan.WidthPixels), bottom = Integer((long)plan.TopPixels + plan.HeightPixels);
        foreach (EcgPaperGridLine line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StringBuilder path = line.IsMajor ? major : minor;
            if (path.Length != 0) { path.Append(' '); }
            string position = SvgLogicalNumber.Format(line.Position.Numerator, line.Position.Denominator);
            path.Append(line.IsVertical ? $"M {position} {top} V {bottom}" : $"M {left} {position} H {right}");
        }
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement root = new(svg + "svg", new XAttribute("x", left), new XAttribute("y", top),
            new XAttribute("width", Integer(plan.WidthPixels)), new XAttribute("height", Integer(plan.HeightPixels)),
            new XAttribute("viewBox", $"{left} {top} {Integer(plan.WidthPixels)} {Integer(plan.HeightPixels)}"),
            new XAttribute("overflow", "hidden"), new XAttribute("fill", "none"),
            new XAttribute("pointer-events", "none"), new XAttribute("aria-hidden", "true"));
        AddPath(root, svg, minor, style.MinorColor, style.MinorStrokeMilliPixels);
        AddPath(root, svg, major, style.MajorColor, style.MajorStrokeMilliPixels);
        string result = root.ToString(SaveOptions.DisableFormatting);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static void AddPath(XElement root, XNamespace svg, StringBuilder path, string color, uint width)
    {
        if (path.Length == 0) { return; }
        root.Add(new XElement(svg + "path", new XAttribute("d", path.ToString()),
            new XAttribute("stroke", color), new XAttribute("stroke-width", SvgLogicalNumber.Format(width, 1000))));
    }

    private static bool IsColor(string value) => value is { Length: 7 } && value[0] == '#' && !value.AsSpan(1).ContainsAnyExcept(HexDigits);
    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
}
