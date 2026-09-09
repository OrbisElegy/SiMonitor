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
        new(nameof(FixtureCancellationPreventsOutputAndAllowsRetry), FixtureCancellationPreventsOutputAndAllowsRetry),
        new(nameof(FixtureCancellationAfterWriteEntryDoesNotReportRollback), FixtureCancellationAfterWriteEntryDoesNotReportRollback),
        new(nameof(FixtureCatalogListsEveryRunnableScenario), FixtureCatalogListsEveryRunnableScenario),
        new(nameof(FixtureCatalogRejectsAmbiguousCommandsWithoutOutput), FixtureCatalogRejectsAmbiguousCommandsWithoutOutput),
        new(nameof(ReturnFixturesJoinCurrentLiveNoData), ReturnFixturesJoinCurrentLiveNoData),
        new(nameof(ReturnFixturesRestoreAndRejectWithoutChangingPinnedInput), ReturnFixturesRestoreAndRejectWithoutChangingPinnedInput),
        new(nameof(HeldRunFixturesKeepPatientClockWhileNoDataSweeps), HeldRunFixturesKeepPatientClockWhileNoDataSweeps),
        new(nameof(HeldRunFixturesRestoreAndRejectInvalidRunState), HeldRunFixturesRestoreAndRejectInvalidRunState),
        new(nameof(ReviewFixtureKeepsHistoryDuringNoData), ReviewFixtureKeepsHistoryDuringNoData),
        new(nameof(ReviewFixtureRestoresSuppressedReplayPolicy), ReviewFixtureRestoresSuppressedReplayPolicy),
        new(nameof(FrozenFixtureKeepsPatientPathsDuringNoData), FrozenFixtureKeepsPatientPathsDuringNoData),
        new(nameof(FrozenFixtureRestoreRetainsPinnedClockAndEvidence), FrozenFixtureRestoreRetainsPinnedClockAndEvidence),
        new(nameof(FixtureCommandEmitsDeterministicSeparatedScenarios), FixtureCommandEmitsDeterministicSeparatedScenarios),
        new(nameof(FixtureCommandRejectsUnknownArgumentsWithoutOutput), FixtureCommandRejectsUnknownArgumentsWithoutOutput),
    ];

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

    private static void FixtureCatalogListsEveryRunnableScenario()
    {
        using StringWriter listing = new(CultureInfo.InvariantCulture);
        using StringWriter replay = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Run(["--list-svg-fixtures"], listing, error) == 0 &&
            SvgFixtureCommand.Run(["--list-svg-fixtures"], replay, error) == 0 &&
            listing.ToString() == replay.ToString() && error.ToString().Length == 0,
            "listing is deterministic and emits no stderr");
        string[] entries = listing.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Check.That(entries.Length == 12 && entries.Select(line => line.Split('\t')[0]).Distinct(StringComparer.Ordinal).Count() == 12 &&
            entries[0] == "live\tRunning / LiveSweep / Authoritative" &&
            entries[^1] == "review-return\tRunning / HistoricalReview / NoData -> LiveSweep at 10500000000 ns presentation time",
            "catalog preserves distinct names and explains initial axes and return transitions");
        XNamespace svg = "http://www.w3.org/2000/svg";
        foreach (string entry in entries)
        {
            string name = entry.Split('\t')[0];
            using StringWriter rendered = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Run(["--svg-fixture", name], rendered, error) == 0 &&
                XElement.Parse(rendered.ToString()).Element(svg + "title")!.Value.EndsWith(" - " + name, StringComparison.Ordinal),
                "every advertised scenario exports the matching standalone SVG");
        }
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

    private static void ReturnFixturesJoinCurrentLiveNoData()
    {
        foreach (string mode in new[] { "frozen-return", "review-return" })
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Run(["--svg-fixture", mode], output, error) == 0 && error.ToString().Length == 0,
                "return scenarios export complete SVG");
            XNamespace svg = "http://www.w3.org/2000/svg";
            XElement root = XElement.Parse(output.ToString());
            Check.That(root.Element(svg + "g")!.Elements(svg + "path").Any() &&
                root.Element(svg + "g")!.Elements(svg + "path").All(path => path.Attribute("stroke-dasharray") is not null) &&
                root.Elements(svg + "text").Any(text => text.Value == "Running / LiveSweep / NoData") &&
                !root.Elements(svg + "text").Any(text => text.Value.Contains("DATA TIME", StringComparison.Ordinal)) &&
                root.Elements(svg + "path").Single().Attribute("id")!.Value == "calibration",
                "return removes pinned labels and source paths in favor of current NoData while retaining calibration");
        }
    }

    private static void ReturnFixturesRestoreAndRejectWithoutChangingPinnedInput()
    {
        EcgStripCheckpoint live = SvgFixtureCommand.Create(true, TemporalViewMode.LiveSweep);
        SweepStateProjectionStateMachine current = SweepStateProjectionStateMachine.Restore(live.Source.Frame.Presentation);
        current.Advance(10_500_000_000, 0);
        foreach (TemporalViewMode view in new[] { TemporalViewMode.FrozenSnapshot, TemporalViewMode.HistoricalReview })
        {
            EcgStripCheckpoint pinned = SvgFixtureCommand.Create(true, view);
            string before = EcgStripSvgPreview.Render(pinned, 5000, 5000);
            EcgStripCheckpoint returned = SvgFixtureCommand.ReturnToLive(pinned, 10_500_000_000);
            SweepStateProjectionSnapshot actual = SweepStateProjectionStateMachine.Restore(returned.Source.Frame.Presentation).CaptureProjection();
            Check.That(actual.WriteHeadPhasePpm == current.CaptureProjection().WriteHeadPhasePpm &&
                actual.CycleIndex == current.CaptureProjection().CycleIndex && actual.PlayheadDataSimTimeNs == 0 &&
                returned.Source.Samples.SequenceEqual(pinned.Source.Samples),
                "return joins independently advanced Live phase without changing the supplied data frontier or samples");
            Check.That(EcgStripSvgPreview.Render(returned, 5000, 5000) ==
                EcgStripSvgPreview.Render(EcgStripReconstructor.Restore(5000, 5000, returned).CaptureCheckpoint()!, 5000, 5000),
                "returned checkpoint restores deterministically");
            try { _ = SvgFixtureCommand.ReturnToLive(pinned, 9_000_000_000); throw new InvalidOperationException("reversed return accepted"); }
            catch (SweepStateProjectionException exception) { Check.That(exception.ReasonCode == "SweepState.TimeReversed", "reversed return rejects"); }
            Check.That(EcgStripSvgPreview.Render(pinned, 5000, 5000) == before, "return and rejection preserve original pinned input");
        }
    }

    private static void HeldRunFixturesKeepPatientClockWhileNoDataSweeps()
    {
        foreach (SessionRunState run in new[] { SessionRunState.Paused, SessionRunState.Stopped })
        {
            string mode = run == SessionRunState.Paused ? "paused" : "stopped";
            using StringWriter held = new(CultureInfo.InvariantCulture);
            using StringWriter disconnected = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            Check.That(SvgFixtureCommand.Run(["--svg-fixture", mode], held, error) == 0 &&
                SvgFixtureCommand.Run(["--svg-fixture", mode + "-nodata"], disconnected, error) == 0 && error.ToString().Length == 0,
                "held run scenarios export through the command");
            XNamespace svg = "http://www.w3.org/2000/svg";
            XElement before = XElement.Parse(held.ToString()), after = XElement.Parse(disconnected.ToString());
            Check.That(before.Element(svg + "g")!.Elements(svg + "path").Single().Attribute("stroke-dasharray") is null &&
                after.Element(svg + "g")!.Elements(svg + "path").Single().Attribute("stroke-dasharray") is not null &&
                XNode.DeepEquals(before.Elements(svg + "path").Single(), after.Elements(svg + "path").Single()) &&
                after.Elements(svg + "text").Any(text => text.Value == $"{run} / LiveSweep / NoData") &&
                !after.Elements(svg + "text").Any(text => text.Value.Contains("DATA TIME", StringComparison.Ordinal)),
                "NoData sweeps held Live trace while retaining calibration and distinguishing held runs from frozen views");
        }
    }

    private static void HeldRunFixturesRestoreAndRejectInvalidRunState()
    {
        foreach (SessionRunState run in new[] { SessionRunState.Paused, SessionRunState.Stopped })
        {
            EcgStripCheckpoint before = SvgFixtureCommand.Create(false, TemporalViewMode.LiveSweep, run);
            EcgStripCheckpoint after = SvgFixtureCommand.Create(true, TemporalViewMode.LiveSweep, run);
            SweepStateProjectionState state = after.Source.Frame.Presentation;
            Check.That(state.LastPresentationNs == 10_000_000_000 && state.LiveSweepClockNs == 10_000_000_000 &&
                before.Source.Frame.Presentation.LiveSweepClockNs == 0 && state.LivePlayheadDataSimTimeNs == 0 &&
                state.ViewPlayheadDataSimTimeNs == 0 && before.Source.Samples.SequenceEqual(after.Source.Samples),
                "presentation time progresses independently of held patient clock and sample evidence");
            string output = EcgStripSvgPreview.Render(after, 5000, 5000);
            Check.That(EcgStripSvgPreview.Render(EcgStripReconstructor.Restore(5000, 5000, after).CaptureCheckpoint()!, 5000, 5000) == output,
                "held NoData checkpoints restore deterministically");
            try { _ = SvgFixtureCommand.Create(false, TemporalViewMode.LiveSweep, (SessionRunState)999); throw new InvalidOperationException("invalid run accepted"); }
            catch (SweepStateProjectionException exception) { Check.That(exception.ReasonCode == "SweepState.InvalidRunState", "invalid run state fails closed"); }
            Check.That(EcgStripSvgPreview.Render(after, 5000, 5000) == output, "rejected construction leaves accepted evidence unchanged");
        }
    }

    private static void ReviewFixtureKeepsHistoryDuringNoData()
    {
        using StringWriter review = new(CultureInfo.InvariantCulture);
        using StringWriter disconnected = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Run(["--svg-fixture", "review"], review, error) == 0 &&
            SvgFixtureCommand.Run(["--svg-fixture", "review-nodata"], disconnected, error) == 0 && error.ToString().Length == 0,
            "review scenarios export complete SVG without diagnostics");
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement before = XElement.Parse(review.ToString()), after = XElement.Parse(disconnected.ToString());
        Check.That(XNode.DeepEquals(before.Element(svg + "g"), after.Element(svg + "g")) &&
            XNode.DeepEquals(before.Element(svg + "defs"), after.Element(svg + "defs")) &&
            XNode.DeepEquals(before.Elements(svg + "path").Single(), after.Elements(svg + "path").Single()) &&
            after.Element(svg + "g")!.Elements(svg + "path").Single().Attribute("stroke-dasharray") is null &&
            after.Elements(svg + "text").Any(text => text.Value == "Running / HistoricalReview / NoData"),
            "NoData cannot replace historical source paths, region clips or persistent calibration");
    }

    private static void ReviewFixtureRestoresSuppressedReplayPolicy()
    {
        EcgStripCheckpoint before = SvgFixtureCommand.Create(false, TemporalViewMode.HistoricalReview);
        EcgStripCheckpoint after = SvgFixtureCommand.Create(true, TemporalViewMode.HistoricalReview);
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Restore(after.Source.Frame.Presentation);
        SweepStateProjectionSnapshot projection = machine.CaptureProjection();
        Check.That(projection.ReviewSegmentRef == "synthetic-triangle-record" && projection.PlayheadDataSimTimeNs == 0 &&
            projection.TransientReplayPolicy == TransientReplayPolicy.Suppress && projection.NoDataCoverage is null &&
            before.Source.Samples.SequenceEqual(after.Source.Samples),
            "restored review retains history identity and suppresses live coverage and transient replay");
        EcgStripCheckpoint restored = EcgStripReconstructor.Restore(5000, 5000, after).CaptureCheckpoint()!;
        string output = EcgStripSvgPreview.Render(after, 5000, 5000);
        Check.That(EcgStripSvgPreview.Render(restored, 5000, 5000) == output,
            "restored review fixture produces the same complete geometry");
        try { _ = SvgFixtureCommand.Create(false, TemporalViewMode.CapturedRecord); throw new InvalidOperationException("unsupported fixture accepted"); }
        catch (ArgumentOutOfRangeException exception) { Check.That(exception.ParamName == "view", "unsupported fixture view is rejected explicitly"); }
        Check.That(EcgStripSvgPreview.Render(after, 5000, 5000) == output,
            "rejected fixture construction leaves accepted historical evidence unchanged");
    }

    private static void FrozenFixtureKeepsPatientPathsDuringNoData()
    {
        using StringWriter frozen = new(CultureInfo.InvariantCulture);
        using StringWriter disconnected = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Check.That(SvgFixtureCommand.Run(["--svg-fixture", "frozen"], frozen, error) == 0 &&
            SvgFixtureCommand.Run(["--svg-fixture", "frozen-nodata"], disconnected, error) == 0 && error.ToString().Length == 0,
            "both frozen scenarios are available through the diagnostic command");
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement before = XElement.Parse(frozen.ToString()), after = XElement.Parse(disconnected.ToString());
        Check.That(XNode.DeepEquals(before.Element(svg + "g"), after.Element(svg + "g")) &&
            XNode.DeepEquals(before.Element(svg + "defs"), after.Element(svg + "defs")) &&
            XNode.DeepEquals(before.Elements(svg + "path").Single(), after.Elements(svg + "path").Single()) &&
            after.Element(svg + "g")!.Elements(svg + "path").Single().Attribute("stroke-dasharray") is null &&
            after.Elements(svg + "text").Any(text => text.Value == "Running / FrozenSnapshot / NoData"),
            "background NoData changes the state label but preserves frozen patient paths, clips and calibration");
    }

    private static void FrozenFixtureRestoreRetainsPinnedClockAndEvidence()
    {
        EcgStripCheckpoint before = SvgFixtureCommand.Create(false, TemporalViewMode.FrozenSnapshot);
        EcgStripCheckpoint after = SvgFixtureCommand.Create(true, TemporalViewMode.FrozenSnapshot);
        SweepStateProjectionState state = after.Source.Frame.Presentation;
        Check.That(state.LastPresentationNs == 10_000_000_000 && state.FreezeAnchorSimTimeNs == 0 &&
            before.Source.Samples.SequenceEqual(after.Source.Samples),
            "NoData advances presentation time while frozen sample evidence and anchor stay pinned");
        EcgStripCheckpoint restored = EcgStripReconstructor.Restore(5000, 5000, after).CaptureCheckpoint()!;
        string rendered = EcgStripSvgPreview.Render(after, 5000, 5000);
        Check.That(EcgStripSvgPreview.Render(restored, 5000, 5000) == rendered,
            "restoring the disconnected frozen checkpoint preserves complete preview output");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { _ = EcgStripSvgPreview.Render(after, 5000, 5000, cancellation.Token); throw new InvalidOperationException("cancelled fixture rendered"); }
        catch (OperationCanceledException exception) { Check.That(exception.CancellationToken == cancellation.Token, "fixture cancellation retains token identity"); }
        Check.That(EcgStripSvgPreview.Render(after, 5000, 5000) == rendered,
            "cancelled rendering does not alter frozen evidence");
    }

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
