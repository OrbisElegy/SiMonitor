// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepFrameReconstructionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(CompleteRebuildPublishesBoundedRegionSegments), CompleteRebuildPublishesBoundedRegionSegments),
        new(nameof(LateFailureRetainsThePreviouslyPublishedFrame), LateFailureRetainsThePreviouslyPublishedFrame),
        new(nameof(ReconstructionCheckpointOwnsAndRevalidatesInputs), ReconstructionCheckpointOwnsAndRevalidatesInputs),
        new(nameof(ResizeRebuildStartsWithoutAnOldPixelPredecessor), ResizeRebuildStartsWithoutAnOldPixelPredecessor),
    ];

    private static SweepFrameReconstructionInput Input(int width = 10)
    {
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Start(
            new("ecg", 4, 5, 0, 10, 2, 12), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(), 0, 0);
        machine.Advance(4, 0);
        return new(new(machine.CaptureState(), 0, width, 0, 10, null), new[] { Sample(10, 0), Sample(11, width) });
    }
    private static SweepPathSample Sample(ulong index, int x) => new(
        new(Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
            Guid.Parse("33333333-3333-4333-8333-333333333333"), 1, 2, 3, 4, 5, 500, 1),
        index, 0, true, new(new(x, 0, 1), new(5, 1, VerticalPlotRelation.WithinPlot)));

    private static void CompleteRebuildPublishesBoundedRegionSegments()
    {
        SweepFrameReconstructor reconstructor = new(2, 2);
        ReconstructedSweepFrame frame = reconstructor.Replace(Input());
        Check.That(frame.Segments.Count == 2 && frame.Segments[0].RegionIndex == 0 && frame.Segments[1].RegionIndex == 2 &&
            frame.Segments.All(segment => segment.EndSampleIndex == 11 && segment.Source == Sample(11, 10).Source),
            "exact capacity publishes separated segments with provenance and no gap bridge");
        Check.That(reconstructor.Replace(Input() with { Samples = Array.Empty<SweepPathSample>() }).Segments.Count == 0,
            "empty input yields no invented patient segment");
    }

    private static void LateFailureRetainsThePreviouslyPublishedFrame()
    {
        SweepFrameReconstructor reconstructor = new(3, 1);
        ReconstructedSweepFrame first = reconstructor.Replace(Input() with { Samples = new[] { Sample(0, 0) } });
        SweepFrameReconstructionInput? checkpoint = reconstructor.CaptureCheckpoint();
        Check.That(Reason(() => reconstructor.Replace(Input())) == "FrameReconstruction.SegmentLimitExceeded" &&
            ReferenceEquals(first, reconstructor.Current) && ReferenceEquals(checkpoint, reconstructor.CaptureCheckpoint()),
            "output overflow after one built segment cannot publish a partial replacement");
        Check.That(Reason(() => reconstructor.Replace(Input() with
        {
            Samples = new[] { Sample(0, 0), Sample(1, 1), Sample(1, 2) },
        })) == "SweepPath.FrontierReversed" && ReferenceEquals(first, reconstructor.Current),
            "invalid final sample leaves the complete prior frame intact");
        Check.That(Reason(() => new SweepFrameReconstructor(1, 2).Replace(Input())) == "FrameReconstruction.SampleLimitExceeded",
            "input capacity is checked before reconstruction");
    }

    private static void ReconstructionCheckpointOwnsAndRevalidatesInputs()
    {
        SweepFrameReconstructor reconstructor = new(2, 2);
        SweepPathSample[] samples = [Sample(10, 0), Sample(11, 10)];
        ReconstructedSweepFrame frame = reconstructor.Replace(Input() with { Samples = samples });
        samples[1] = Sample(0, 0);
        SweepFrameReconstructionInput checkpoint = reconstructor.CaptureCheckpoint()!;
        SweepFrameReconstructor restored = SweepFrameReconstructor.Restore(2, 2, checkpoint);
        Check.That(frame.Segments.SequenceEqual(restored.Current!.Segments) && checkpoint.Samples[1].SampleIndex == 11,
            "caller array mutation cannot alter checkpoint inputs or restored geometry");
        Check.That(Reason(() => SweepFrameReconstructor.Restore(2, 1, checkpoint)) == "FrameReconstruction.InvalidCheckpoint" &&
            Reason(() => SweepFrameReconstructor.Restore(2, 2, checkpoint with
            {
                Samples = new[] { Sample(10, 0), Sample(9, 10) },
            })) == "FrameReconstruction.InvalidCheckpoint",
            "restore reruns capacity and adjacency validation rather than trusting stored output");
    }

    private static void ResizeRebuildStartsWithoutAnOldPixelPredecessor()
    {
        SweepFrameReconstructor reconstructor = new(2, 2);
        ReconstructedSweepFrame first = reconstructor.Replace(Input());
        ReconstructedSweepFrame resized = reconstructor.Replace(Input(20));
        Check.That(first.Segments[0].Segment.End.X == new ExactPlotCoordinate(4, 1) &&
            resized.Segments[0].Segment.End.X == new ExactPlotCoordinate(8, 1) &&
            first.Geometry.SweepEpoch == resized.Geometry.SweepEpoch && first.Geometry.VisibleDurationNs == resized.Geometry.VisibleDurationNs,
            "fresh reconstruction at a new width preserves time identity without joining old pixel endpoints");
        SweepFrameReconstructionInput input = Input();
        Check.That(Reason(() => reconstructor.Replace(input with { Frame = input.Frame with { Previous = Sample(9, 0) } })) ==
            "FrameReconstruction.InvalidInput" && ReferenceEquals(resized, reconstructor.Current),
            "a full rebuild must not import an old predecessor");
        Check.That(Reason(() => { _ = new SweepFrameReconstructor(0, 2); }) == "FrameReconstruction.InvalidLimits",
            "resource limits must be explicitly positive");
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (SweepFrameReconstructionException exception) { return exception.ReasonCode; }
        catch (SweepPathException exception) { return exception.ReasonCode; }
    }
}
