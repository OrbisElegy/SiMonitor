// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Xml.Linq;

namespace Monitor.Specs;

internal static class SvgFixtureSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FixtureCommandEmitsDeterministicSeparatedScenarios), FixtureCommandEmitsDeterministicSeparatedScenarios),
        new(nameof(FixtureCommandRejectsUnknownArgumentsWithoutOutput), FixtureCommandRejectsUnknownArgumentsWithoutOutput),
    ];

    private static void FixtureCommandEmitsDeterministicSeparatedScenarios()
    {
        using StringWriter live = new(CultureInfo.InvariantCulture);
        using StringWriter noData = new(CultureInfo.InvariantCulture);
        using StringWriter replay = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Run(["--svg-fixture", "live"], live, error) == 0 &&
            SvgFixtureCommand.Run(["--svg-fixture", "nodata"], noData, error) == 0 &&
            SvgFixtureCommand.Run(["--svg-fixture", "live"], replay, error) == 0 &&
            live.ToString() == replay.ToString() && error.ToString().Length == 0,
            "fixture stdout is deterministic SVG and successful commands emit no diagnostics");
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement liveRoot = XElement.Parse(live.ToString()), noDataRoot = XElement.Parse(noData.ToString());
        Check.That(liveRoot.Elements(svg + "g").Single().Elements(svg + "path").Single().Attribute("stroke-dasharray") is null &&
            noDataRoot.Elements(svg + "g").Single().Elements(svg + "path").All(path => path.Attribute("stroke-dasharray") is not null) &&
            XNode.DeepEquals(liveRoot.Elements(svg + "path").Single(), noDataRoot.Elements(svg + "path").Single()) &&
            noDataRoot.Elements(svg + "text").Any(text => text.Value.Contains("NoData", StringComparison.Ordinal)) &&
            liveRoot.Element(svg + "desc")!.Value.Contains("not a physiological ECG model", StringComparison.Ordinal),
            "fixtures separate patient triangles from NoData while preserving calibration and synthetic labeling");
    }

    private static void FixtureCommandRejectsUnknownArgumentsWithoutOutput()
    {
        string[][] invalid = [[], ["--svg-fixture"], ["--svg-fixture", "unknown"], ["--unknown", "live"], ["--svg-fixture", "live", "extra"]];
        foreach (string[] args in invalid)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Run(args, output, error) == 2 && output.ToString().Length == 0 &&
                error.ToString().StartsWith("Usage:", StringComparison.Ordinal),
                "invalid command shapes fail before generating any SVG output");
        }
    }
}
