// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepPathBuilderSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AdjacentSamplesProduceClippedSegments), AdjacentSamplesProduceClippedSegments),
        new(nameof(GapsCyclesAndQualityBreakThePath), GapsCyclesAndQualityBreakThePath),
        new(nameof(SourceChangesCannotBridgePaths), SourceChangesCannotBridgePaths),
        new(nameof(PathCheckpointAndFailuresPreserveFrontier), PathCheckpointAndFailuresPreserveFrontier),
    ];
    private static SweepPathBuilder Start() => SweepPathBuilder.Start(
        new(new(0, 0, 1), new(10, 0, 1), SweepTraceRegionKind.RetainSourceTrace), 0, 10);
    private static SweepPathSample Sample(ulong index, int x, int y = 5) => new(
        new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            Guid.Parse("33333333-3333-4333-8333-333333333333"), 1, 2, 3, 4, 5, 500, 1),
        index, 0, true, new(new(x, 0, 1), new(y, 1, VerticalPlotRelation.WithinPlot)));

    private static void AdjacentSamplesProduceClippedSegments()
    {
        SweepPathBuilder builder = Start();
        Check.That(builder.Append(Sample(10, 0, -10)).ReasonCode == "SweepPath.FirstPoint", "first point cannot invent a predecessor");
        SweepPathAppendResult result = builder.Append(Sample(11, 10, 20));
        Check.That(result.ReasonCode == "SweepPath.Segment" && result.Segment?.Start.X == new ExactPlotCoordinate(10, 3) &&
            result.Segment.End.X == new ExactPlotCoordinate(20, 3), "adjacent samples use exact clipping rather than endpoint clamping");
    }

    private static void GapsCyclesAndQualityBreakThePath()
    {
        foreach ((SweepPathSample next, string reason) in new[]
        {
            (Sample(12, 2), "SweepPath.SampleGap"),
            (Sample(11, 0) with { CycleIndex = 1 }, "SweepPath.CycleChanged"),
            (Sample(11, 2) with { Drawable = false }, "SweepPath.NotDrawable"),
        })
        {
            SweepPathBuilder builder = Start();
            builder.Append(Sample(10, 1));
            SweepPathAppendResult result = builder.Append(next);
            Check.That(result.ReasonCode == reason && result.Segment is null, "a gap, wrap or quality break must never bridge samples");
        }
        SweepPathBuilder quality = Start();
        quality.Append(Sample(10, 0) with { Drawable = false });
        Check.That(quality.Append(Sample(11, 1)).Segment is null && quality.Append(Sample(12, 2)).Segment is not null,
            "two consecutive drawable samples are required after suppressed input");
    }

    private static void SourceChangesCannotBridgePaths()
    {
        SweepPathSample first = Sample(10, 1);
        foreach (SweepSampleSource source in new[]
        {
            first.Source with { SessionId = first.Source.ChannelId }, first.Source with { InstanceId = first.Source.ChannelId },
            first.Source with { ChannelId = first.Source.SessionId }, first.Source with { TimebaseEpoch = 2 },
            first.Source with { StreamEpoch = 3 }, first.Source with { ConfigurationRevision = 4 },
            first.Source with { SweepEpoch = 5 }, first.Source with { PresentationClockRevision = 6 },
            first.Source with { SampleRateNumerator = 250 }, first.Source with { SampleRateDenominator = 2 },
        })
        {
            SweepPathBuilder builder = Start();
            builder.Append(first);
            SweepPathAppendResult result = builder.Append(Sample(0, 0) with { Source = source });
            Check.That(result.ReasonCode == "SweepPath.SourceChanged" && result.Segment is null,
                "every source and sampling identity change begins an independent path");
        }
    }

    private static void PathCheckpointAndFailuresPreserveFrontier()
    {
        SweepPathBuilder builder = Start();
        builder.Append(Sample(10, 5));
        SweepPathState checkpoint = builder.CaptureState();
        var restored = SweepPathBuilder.Restore(checkpoint);
        foreach (SweepPathSample invalid in new[]
        {
            Sample(10, 6), Sample(9, 6), Sample(11, 4),
            Sample(11, 6) with { Source = Sample(11, 6).Source with { ChannelId = Guid.Empty } },
            Sample(11, 6) with { Drawable = false, Point = new(new(6, 0, 0), new(5, 1, VerticalPlotRelation.WithinPlot)) },
        })
        {
            Check.That(Reason(() => restored.Append(invalid)) is not null && restored.CaptureState() == checkpoint,
                "invalid or reversed points leave the pending predecessor unchanged");
        }
        Check.That(builder.Append(Sample(11, 6)) == restored.Append(Sample(11, 6)), "split-run path output is identical");
        Check.That(Reason(() => SweepPathBuilder.Restore(checkpoint with
        {
            Previous = checkpoint.Previous! with { Source = checkpoint.Previous.Source with { SampleRateDenominator = 0 } },
        })) == "SweepPath.InvalidCheckpoint", "restore revalidates the retained source frontier");
        SweepPathBuilder maximum = Start();
        maximum.Append(Sample(ulong.MaxValue, 1));
        Check.That(Reason(() => maximum.Append(Sample(0, 2))) == "SweepPath.FrontierReversed",
            "sample index exhaustion cannot wrap into an adjacent segment");
    }
    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (SweepPathException exception) { return exception.ReasonCode; }
        catch (SweepSegmentClipException exception) { return exception.ReasonCode; }
    }
}
