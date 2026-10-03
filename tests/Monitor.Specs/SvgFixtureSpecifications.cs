// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Xml.Linq;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Specs;

internal static class SvgFixtureSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FixtureExecutionReportsFlushFailureWithoutRetry), FixtureExecutionReportsFlushFailureWithoutRetry),
        new(nameof(FixtureExecutionFlushesOnlySuccessfulOutput), FixtureExecutionFlushesOnlySuccessfulOutput),
        new(nameof(FixtureExecutionReportsPartialWriteFailureWithoutRetry), FixtureExecutionReportsPartialWriteFailureWithoutRetry),
        new(nameof(FixtureExecutionDistinguishesCancellationUsageAndSuccess), FixtureExecutionDistinguishesCancellationUsageAndSuccess),
        new(nameof(FixtureCancellationPreventsOutputAndAllowsRetry), FixtureCancellationPreventsOutputAndAllowsRetry),
        new(nameof(FixtureCancellationAfterWriteEntryDoesNotReportRollback), FixtureCancellationAfterWriteEntryDoesNotReportRollback),
        new(nameof(FixtureCatalogRendersEachScenarioWithExpectedState), FixtureCatalogRendersEachScenarioWithExpectedState),
        new(nameof(FixtureCatalogRejectsAmbiguousCommandsWithoutOutput), FixtureCatalogRejectsAmbiguousCommandsWithoutOutput),
        new(nameof(FixtureCommandRejectsUnknownArgumentsWithoutOutput), FixtureCommandRejectsUnknownArgumentsWithoutOutput),
    ];

    private sealed class FlushTrackingWriter(bool fail) : StringWriter(CultureInfo.InvariantCulture)
    {
        public int Flushes { get; private set; }
        public int Writes { get; private set; }
        public override void WriteLine(string? value) { Writes++; base.WriteLine(value); }
        public override void Flush()
        {
            Flushes++;
            if (fail) { throw new IOException("simulated buffered flush failure"); }
            base.Flush();
        }
    }

    private static void FixtureExecutionReportsFlushFailureWithoutRetry()
    {
        string[][] commands = [["--svg-fixture", "nodata"], ["--list-svg-fixtures"]];
        foreach (string[] args in commands)
        {
            using FlushTrackingWriter output = new(true);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Execute(args, output, error) == 1 && output.Writes == 1 && output.Flushes == 1 &&
                output.ToString().Length > 0 && error.ToString() == "Fixture command I/O failed; output may be incomplete." + Environment.NewLine,
                "buffered failure prevents success without retrying either write or flush");
        }
    }

    private static void FixtureExecutionFlushesOnlySuccessfulOutput()
    {
        using FlushTrackingWriter output = new(false);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Execute(["--svg-fixture", "nodata"], output, error) == 0 && output.Flushes == 1 &&
            output.Writes == 1 && error.ToString().Length == 0, "success requires one completed flush");
        output.Write("still caller owned");
        Check.That(output.ToString().EndsWith("still caller owned", StringComparison.Ordinal), "execution does not close the caller's writer");
        using FlushTrackingWriter rejected = new(true);
        Check.That(SvgFixtureCommand.Execute(["--invalid"], rejected, error) == 2 && rejected.Flushes == 0 && rejected.Writes == 0,
            "usage rejection does not flush unrelated buffered stdout");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Check.That(SvgFixtureCommand.Execute(["--list-svg-fixtures"], rejected, error, cancellation.Token) == 130 &&
            rejected.Flushes == 0 && rejected.Writes == 0, "pre-cancellation does not flush stdout");
    }

    private sealed class FailingWriter : StringWriter
    {
        public FailingWriter() : base(CultureInfo.InvariantCulture) { }
        public int Writes { get; private set; }
        public override void WriteLine(string? value)
        {
            Writes++;
            Write(value![..10]);
            throw new IOException("simulated partial write");
        }
    }

    private static void FixtureExecutionReportsPartialWriteFailureWithoutRetry()
    {
        string[][] commands = [["--svg-fixture", "live"], ["--list-svg-fixtures"]];
        foreach (string[] args in commands)
        {
            using FailingWriter output = new();
            using StringWriter error = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Execute(args, output, error) == 1 && output.Writes == 1 && output.ToString().Length == 10 &&
                error.ToString() == "Fixture command I/O failed; output may be incomplete." + Environment.NewLine,
                "partial output failure returns 1, reports incomplete output and never retries the write");
            using StringWriter fresh = new(CultureInfo.InvariantCulture);
            using StringWriter freshError = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Execute(args, fresh, freshError) == 0 && freshError.ToString().Length == 0,
                "an independent invocation can succeed after output failure");
        }
    }

    private static void FixtureExecutionDistinguishesCancellationUsageAndSuccess()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Execute(["--svg-fixture", "live"], output, error, cancellation.Token) == 130 &&
            output.ToString().Length == 0 && error.ToString() == "Fixture command cancelled." + Environment.NewLine,
            "recognized cancellation returns 130 without stdout");
        error.GetStringBuilder().Clear();
        Check.That(SvgFixtureCommand.Execute(["--invalid"], output, error) == 2 && output.ToString().Length == 0 &&
            error.ToString().StartsWith("Usage:", StringComparison.Ordinal), "usage errors retain exit code 2");
        error.GetStringBuilder().Clear();
        Check.That(SvgFixtureCommand.Execute(["--svg-fixture", "nodata"], output, error) == 0 &&
            error.ToString().Length == 0 && XElement.Parse(output.ToString()).Name.LocalName == "svg",
            "successful execution still returns standalone SVG and exit code zero");
    }

    private static void FixtureCancellationPreventsOutputAndAllowsRetry()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        string[][] commands = [["--svg-fixture", "live"], ["--list-svg-fixtures"]];
        foreach (string[] args in commands)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            try { _ = SvgFixtureCommand.Run(args, output, error, cancellation.Token); throw new InvalidOperationException("cancelled command accepted"); }
            catch (OperationCanceledException exception) { Check.That(exception.CancellationToken == cancellation.Token, "command retains cancellation token"); }
            Check.That(output.ToString().Length == 0 && error.ToString().Length == 0 &&
                SvgFixtureCommand.Run(args, output, error) == 0 && output.ToString().Length > 0,
                "cancelled commands produce no output and a fresh invocation succeeds");
        }
        try { _ = SvgFixtureCommand.Create(false, TemporalViewMode.LiveSweep, cancellationToken: cancellation.Token); throw new InvalidOperationException("cancelled generation accepted"); }
        catch (OperationCanceledException exception) { Check.That(exception.CancellationToken == cancellation.Token, "fixture generation also observes cancellation"); }
    }

    private sealed class CancellingWriter(CancellationTokenSource cancellation) : StringWriter(CultureInfo.InvariantCulture)
    {
        public int Writes { get; private set; }
        public override void WriteLine(string? value)
        {
            Writes++;
            cancellation.Cancel();
            base.WriteLine(value);
        }
    }

    private static void FixtureCancellationAfterWriteEntryDoesNotReportRollback()
    {
        using CancellationTokenSource cancellation = new();
        using CancellingWriter output = new(cancellation);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Run(["--svg-fixture", "nodata"], output, error, cancellation.Token) == 0 &&
            cancellation.IsCancellationRequested && output.Writes == 1 && error.ToString().Length == 0 &&
            XElement.Parse(output.ToString()).Name.LocalName == "svg",
            "once stream writing starts, a complete successful write is not falsely reported as rolled back by cancellation");
    }

    private static void FixtureCatalogRendersEachScenarioWithExpectedState()
    {
        using StringWriter listing = new(CultureInfo.InvariantCulture);
        using StringWriter replay = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Run(["--list-svg-fixtures"], listing, error) == 0 &&
            SvgFixtureCommand.Run(["--list-svg-fixtures"], replay, error) == 0 && listing.ToString() == replay.ToString(),
            "fixture listing is deterministic");
        string[] expectedNames = ["live", "nodata", "frozen", "frozen-nodata", "review", "review-nodata",
            "paused", "paused-nodata", "stopped", "stopped-nodata", "frozen-return", "review-return"];
        string[] names = listing.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t')[0]).ToArray();
        Check.That(names.Order().SequenceEqual(expectedNames.Order()), "catalog advertises each supported scenario exactly once");
        XNamespace svg = "http://www.w3.org/2000/svg";
        Dictionary<string, XElement> rendered = [];
        foreach (string name in names)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Run(["--svg-fixture", name], output, error) == 0, $"{name}: command succeeds");
            var document = XElement.Parse(output.ToString());
            rendered.Add(name, document);
            bool noData = name == "nodata" || name.EndsWith("-nodata", StringComparison.Ordinal) || name.EndsWith("-return", StringComparison.Ordinal);
            bool pinned = (name.StartsWith("frozen", StringComparison.Ordinal) || name.StartsWith("review", StringComparison.Ordinal)) &&
                !name.EndsWith("-return", StringComparison.Ordinal);
            string run = name.StartsWith("paused", StringComparison.Ordinal) ? "Paused" : name.StartsWith("stopped", StringComparison.Ordinal) ? "Stopped" : "Running";
            string view = pinned ? name.StartsWith("frozen", StringComparison.Ordinal) ? "FrozenSnapshot" : "HistoricalReview" : "LiveSweep";
            string continuity = noData ? "NoData" : "Authoritative";
            XElement[] paths = document.Element(svg + "g")!.Elements(svg + "path").ToArray();
            Check.That(document.Element(svg + "title")!.Value.EndsWith(" - " + name, StringComparison.Ordinal) &&
                document.Element(svg + "desc")!.Value.Contains("not a physiological ECG model", StringComparison.Ordinal) &&
                document.Elements(svg + "text").Any(text => text.Value == $"{run} / {view} / {continuity}"),
                $"{name}: title, synthetic-source declaration and state label match");
            Check.That(paths.Length > 0 && paths.All(path => (path.Attribute("stroke-dasharray") is not null) == (noData && !pinned)) &&
                document.Elements(svg + "path").Single().Attribute("id")!.Value == "calibration" &&
                document.Elements(svg + "text").Any(text => text.Value.Contains("DATA TIME", StringComparison.Ordinal)) == pinned,
                $"{name}: patient/NoData style, independent calibration and pinned-time label match");
        }
        foreach (string mode in new[] { "frozen", "review" })
        {
            Check.That(XNode.DeepEquals(rendered[mode].Element(svg + "g"), rendered[mode + "-nodata"].Element(svg + "g")) &&
                XNode.DeepEquals(rendered[mode].Element(svg + "defs"), rendered[mode + "-nodata"].Element(svg + "defs")),
                $"{mode}: background NoData preserves pinned patient paths and clips");
        }
        foreach (XElement document in rendered.Values)
        {
            Check.That(XNode.DeepEquals(rendered["live"].Elements(svg + "path").Single(), document.Elements(svg + "path").Single()),
                "calibration remains unchanged across run, view and continuity states");
        }
        using StringWriter repeated = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Run(["--svg-fixture", "live"], repeated, error) == 0 &&
            XNode.DeepEquals(rendered["live"], XElement.Parse(repeated.ToString())) && error.ToString().Length == 0,
            "successful rendering is repeatable and emits no diagnostics");
    }

    private static void FixtureCatalogRejectsAmbiguousCommandsWithoutOutput()
    {
        string[][] invalid = [["--list-svg-fixtures", "live"], ["--svg-fixture", "LIVE"],
            ["--svg-fixture", "live "], ["--svg-fixture", "frozen-ret"], ["--list-svg-fixtures", "--svg-fixture", "live"]];
        foreach (string[] args in invalid)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Run(args, output, error) == 2 && output.ToString().Length == 0 &&
                error.ToString().Contains("--list-svg-fixtures", StringComparison.Ordinal),
                "ambiguous or malformed command shapes reject without partial listing or SVG");
        }
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
