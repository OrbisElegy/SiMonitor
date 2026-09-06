// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepFrameReconstructionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(StoppingPublicationFencesEveryOwnedTicket), StoppingPublicationFencesEveryOwnedTicket),
        new(nameof(StoppingPumpDiscardsPendingAndRejectsAdmission), StoppingPumpDiscardsPendingAndRejectsAdmission),
        new(nameof(ConcurrentStopLeavesOneFinalImmutableSnapshot), ConcurrentStopLeavesOneFinalImmutableSnapshot),
        new(nameof(RestoredPumpIsIndependentFromStoppedLifecycle), RestoredPumpIsIndependentFromStoppedLifecycle),
        new(nameof(QueuedFramesCoalesceToTheLatestRequest), QueuedFramesCoalesceToTheLatestRequest),
        new(nameof(QueueFailureReleasesWorkerAndPreservesPublishedFrame), QueueFailureReleasesWorkerAndPreservesPublishedFrame),
        new(nameof(ConcurrentQueuePumpsConsumeOnePendingRequest), ConcurrentQueuePumpsConsumeOnePendingRequest),
        new(nameof(QueueRestoreDoesNotRevivePendingWork), QueueRestoreDoesNotRevivePendingWork),
        new(nameof(OlderWorkCannotOverwriteANewerPublication), OlderWorkCannotOverwriteANewerPublication),
        new(nameof(ConcurrentDuplicateCompletionPublishesOnce), ConcurrentDuplicateCompletionPublishesOnce),
        new(nameof(FailedNewestWorkPreservesPublishedSnapshot), FailedNewestWorkPreservesPublishedSnapshot),
        new(nameof(PublicationOwnsInputsAndFencesRestoredWork), PublicationOwnsInputsAndFencesRestoredWork),
        new(nameof(CompleteRebuildPublishesBoundedRegionSegments), CompleteRebuildPublishesBoundedRegionSegments),
        new(nameof(LateFailureRetainsThePreviouslyPublishedFrame), LateFailureRetainsThePreviouslyPublishedFrame),
        new(nameof(ReconstructionCheckpointOwnsAndRevalidatesInputs), ReconstructionCheckpointOwnsAndRevalidatesInputs),
        new(nameof(ResizeRebuildStartsWithoutAnOldPixelPredecessor), ResizeRebuildStartsWithoutAnOldPixelPredecessor),
    ];

    private static void StoppingPublicationFencesEveryOwnedTicket()
    {
        SweepFramePublication publication = new(2, 2);
        SweepFrameWork first = publication.Request(Input());
        publication.Complete(first);
        SweepFrameWork pending = publication.Request(Input(20));
        PublishedSweepFrame? final = publication.Stop();
        Check.That(final is not null && publication.Complete(first) == SweepFramePublicationStatus.Stopped &&
            publication.Complete(pending) == SweepFramePublicationStatus.Stopped &&
            ReferenceEquals(final, publication.Stop()) && ReferenceEquals(final, publication.CapturePublished()),
            "stop fences completed and pending tickets without consuming revisions or changing the final snapshot");
        Check.That(PublicationReason(() => publication.Request(Input())) == "FramePublication.Stopped",
            "a stopped publisher cannot admit fresh work");
    }

    private static void StoppingPumpDiscardsPendingAndRejectsAdmission()
    {
        SweepFrameWorkPump pump = new(2, 2);
        pump.Enqueue(Input());
        Check.That(pump.Stop() is null && pump.ProcessNext() is null && pump.Stop() is null &&
            PublicationReason(() => pump.Enqueue(Input())) == "FramePublication.Stopped",
            "stopping before first processing drops pending work and remains stopped on repeated calls");
    }

    private static void ConcurrentStopLeavesOneFinalImmutableSnapshot()
    {
        SweepFramePublication publication = new(2, 2);
        publication.Complete(publication.Request(Input()));
        SweepFrameWork work = publication.Request(Input(20));
        PublishedSweepFrame? atStop = null;
        Parallel.Invoke(() => publication.Complete(work), () => atStop = publication.Stop());
        Check.That(atStop is not null && ReferenceEquals(atStop, publication.CapturePublished()) &&
            publication.Complete(work) == SweepFramePublicationStatus.Stopped,
            "whether reconstruction wins or loses the race, no publication occurs after Stop returns");
        SweepFrameWorkPump pump = new(2, 2);
        pump.Enqueue(Input());
        pump.ProcessNext();
        pump.Enqueue(Input(20));
        PublishedSweepFrame? pumpStop = null;
        Parallel.Invoke(() => pump.ProcessNext(), () => pumpStop = pump.Stop());
        Check.That(pumpStop is not null && ReferenceEquals(pumpStop, pump.CapturePublished()) && pump.ProcessNext() is null,
            "pump stop fences active work without deadlock or subsequent pending consumption");
    }

    private static void RestoredPumpIsIndependentFromStoppedLifecycle()
    {
        SweepFrameWorkPump original = new(2, 2);
        original.Enqueue(Input());
        original.ProcessNext();
        PublishedSweepFrame saved = original.Stop()!;
        SweepFrameWorkPump restored = SweepFrameWorkPump.Restore(2, 2, saved.Checkpoint);
        restored.Enqueue(Input(20));
        Check.That(restored.ProcessNext() == SweepFramePublicationStatus.Published &&
            restored.CapturePublished()!.Frame.Geometry.PlotWidthPixels == 20 &&
            ReferenceEquals(saved, original.CapturePublished()) && original.ProcessNext() is null,
            "restoring completed input creates a new active lifecycle without reopening the stopped instance");
    }

    private static void QueuedFramesCoalesceToTheLatestRequest()
    {
        SweepFrameWorkPump queue = new(2, 2);
        Check.That(queue.ProcessNext() is null, "empty queue has no worker result");
        queue.Enqueue(Input());
        queue.Enqueue(Input(20));
        ulong latest = queue.Enqueue(Input(30));
        Check.That(queue.ProcessNext() == SweepFramePublicationStatus.Published && queue.ProcessNext() is null &&
            queue.CapturePublished()!.LocalGeneration == latest && queue.CapturePublished()!.Frame.Geometry.PlotWidthPixels == 30,
            "only the latest waiting frame is reconstructed without accumulating stale work");
    }

    private static void QueueFailureReleasesWorkerAndPreservesPublishedFrame()
    {
        SweepFrameWorkPump queue = new(2, 2);
        queue.Enqueue(Input());
        queue.ProcessNext();
        PublishedSweepFrame before = queue.CapturePublished()!;
        queue.Enqueue(Input() with { Samples = new[] { Sample(10, 0), Sample(9, 10) } });
        Check.That(Reason(() => queue.ProcessNext()) == "SweepPath.FrontierReversed" &&
            ReferenceEquals(before, queue.CapturePublished()), "failed reconstruction retains the complete previous frame");
        ulong pending = queue.Enqueue(Input(20));
        Check.That(PublicationReason(() => queue.Enqueue(Input() with { Samples = new[] { Sample(0, 0), Sample(1, 1), Sample(2, 2) } })) ==
            "FramePublication.SampleLimitExceeded" && queue.ProcessNext() == SweepFramePublicationStatus.Published &&
            queue.CapturePublished()!.LocalGeneration == pending,
            "failure releases the worker and invalid admission does not discard valid pending work");
    }

    private static void ConcurrentQueuePumpsConsumeOnePendingRequest()
    {
        SweepFrameWorkPump queue = new(2, 2);
        queue.Enqueue(Input());
        SweepFramePublicationStatus?[] results = new SweepFramePublicationStatus?[16];
        Parallel.For(0, results.Length, index => results[index] = queue.ProcessNext());
        Check.That(results.Count(status => status == SweepFramePublicationStatus.Published) == 1 &&
            results.Count(status => status is null) == 15 && queue.CapturePublished()!.Frame.Segments.Count == 2,
            "concurrent worker pumps cannot consume the same pending slot twice");
    }

    private static void QueueRestoreDoesNotRevivePendingWork()
    {
        SweepFrameWorkPump queue = new(2, 2);
        SweepPathSample[] source = [Sample(10, 0), Sample(11, 10)];
        queue.Enqueue(Input() with { Samples = source });
        source[1] = Sample(0, 0);
        queue.ProcessNext();
        PublishedSweepFrame published = queue.CapturePublished()!;
        queue.Enqueue(Input(20));
        SweepFrameWorkPump restored = SweepFrameWorkPump.Restore(2, 2, published.Checkpoint);
        Check.That(restored.ProcessNext() is null && restored.CapturePublished()!.Frame.Segments.SequenceEqual(published.Frame.Segments),
            "restore reconstructs owned completed inputs without resurrecting transient pending jobs");
    }

    private static void OlderWorkCannotOverwriteANewerPublication()
    {
        SweepFramePublication publication = new(2, 2);
        SweepFrameWork old = publication.Request(Input());
        SweepFrameWork latest = publication.Request(Input(20));
        Check.That(latest.Generation > old.Generation && publication.Complete(latest) == SweepFramePublicationStatus.Published &&
            publication.Complete(old) == SweepFramePublicationStatus.Superseded &&
            publication.CapturePublished()!.Frame.Geometry.PlotWidthPixels == 20,
            "completion order cannot replace newer requested geometry with an older viewport");
        PublishedSweepFrame current = publication.CapturePublished()!;
        Check.That(publication.Complete(latest) == SweepFramePublicationStatus.AlreadyPublished &&
            ReferenceEquals(current, publication.CapturePublished()), "duplicate completion retains the exact immutable snapshot");
    }

    private static void ConcurrentDuplicateCompletionPublishesOnce()
    {
        SweepFramePublication publication = new(2, 2);
        SweepFrameWork work = publication.Request(Input());
        SweepFramePublicationStatus[] results = new SweepFramePublicationStatus[16];
        Parallel.For(0, results.Length, index => results[index] = publication.Complete(work));
        Check.That(results.Count(status => status == SweepFramePublicationStatus.Published) == 1 &&
            results.Count(status => status == SweepFramePublicationStatus.AlreadyPublished) == 15 &&
            publication.CapturePublished()!.Frame.Segments.Count == 2,
            "concurrent workers publish one complete snapshot and duplicate commits are idempotent");
    }

    private static void FailedNewestWorkPreservesPublishedSnapshot()
    {
        SweepFramePublication publication = new(2, 2);
        publication.Complete(publication.Request(Input()));
        PublishedSweepFrame before = publication.CapturePublished()!;
        SweepFrameWork older = publication.Request(Input(20));
        SweepFrameWork invalid = publication.Request(Input() with { Samples = new[] { Sample(10, 0), Sample(9, 10) } });
        Check.That(Reason(() => publication.Complete(invalid)) == "SweepPath.FrontierReversed" &&
            ReferenceEquals(before, publication.CapturePublished()) &&
            publication.Complete(older) == SweepFramePublicationStatus.Superseded,
            "newest reconstruction failure preserves the last complete frame without reviving old work");
        SweepFrameWork retry = publication.Request(Input(30));
        Check.That(publication.Complete(retry) == SweepFramePublicationStatus.Published,
            "a valid subsequent request recovers normally");
        SweepFrameWork valid = publication.Request(Input());
        Check.That(PublicationReason(() => publication.Request(Input() with { Samples = new[] { Sample(0, 0), Sample(1, 1), Sample(2, 2) } })) ==
            "FramePublication.SampleLimitExceeded" && publication.Complete(valid) == SweepFramePublicationStatus.Published,
            "request admission failure must not consume a generation or supersede valid pending work");
    }

    private static void PublicationOwnsInputsAndFencesRestoredWork()
    {
        SweepFramePublication publication = new(2, 2);
        SweepPathSample[] samples = [Sample(10, 0), Sample(11, 10)];
        SweepFrameWork work = publication.Request(Input() with { Samples = samples });
        samples[1] = Sample(0, 0);
        publication.Complete(work);
        PublishedSweepFrame current = publication.CapturePublished()!;
        SweepFramePublication restored = SweepFramePublication.Restore(2, 2, current.Checkpoint);
        Check.That(current.Frame.Segments.SequenceEqual(restored.CapturePublished()!.Frame.Segments) &&
            PublicationReason(() => restored.Complete(work)) == "FramePublication.ForeignWork",
            "request inputs are owned and restored publication rejects tickets from the previous instance");
        Check.That(Reason(() => SweepFramePublication.Restore(2, 1, current.Checkpoint)) == "FrameReconstruction.InvalidCheckpoint",
            "restore rebuilds the frame under current resource limits");
    }

    private static string? PublicationReason(Action action)
    {
        try { action(); return null; }
        catch (SweepFramePublicationException exception) { return exception.ReasonCode; }
    }

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
