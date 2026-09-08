// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Specs;

internal static class EcgStripSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(StripReductionPublishesMatchingGeometryTogether), StripReductionPublishesMatchingGeometryTogether),
        new(nameof(StripReductionFailurePreservesCompletedResult), StripReductionFailurePreservesCompletedResult),
        new(nameof(StripReductionRestoresUnderExplicitLimits), StripReductionRestoresUnderExplicitLimits),
        new(nameof(StripReductionWorkerRecoversFromOutputOverflow), StripReductionWorkerRecoversFromOutputOverflow),
        new(nameof(StripCompositionKeepsCurrentCalibrationWithoutSource), StripCompositionKeepsCurrentCalibrationWithoutSource),
        new(nameof(StripCompositionUsesCurrentNoDataSafetyWithOldPublication), StripCompositionUsesCurrentNoDataSafetyWithOldPublication),
        new(nameof(StripCompositionKeepsPinnedHistoryAndIndependentNumericClock), StripCompositionKeepsPinnedHistoryAndIndependentNumericClock),
        new(nameof(StripCompositionRestoresAndRejectsWithoutMutation), StripCompositionRestoresAndRejectsWithoutMutation),
        new(nameof(StripWorkerPublishesLatestAndWakesAgain), StripWorkerPublishesLatestAndWakesAgain),
        new(nameof(StripWorkerRecoversFromInvalidSampleEvidence), StripWorkerRecoversFromInvalidSampleEvidence),
        new(nameof(StripWorkerShutdownJoinsAndRejectsAdmission), StripWorkerShutdownJoinsAndRejectsAdmission),
        new(nameof(StripWorkerRebuildsOwnedCheckpointInNewLifecycle), StripWorkerRebuildsOwnedCheckpointInNewLifecycle),
        new(nameof(StripPumpCoalescesAndRejectsInvalidAdmission), StripPumpCoalescesAndRejectsInvalidAdmission),
        new(nameof(StripPumpFailureReleasesSlotWithoutPartialPublication), StripPumpFailureReleasesSlotWithoutPartialPublication),
        new(nameof(StripPumpConcurrentConsumersAndCancellationKeepOneRequest), StripPumpConcurrentConsumersAndCancellationKeepOneRequest),
        new(nameof(StripPumpStopAndRestoreExcludePendingWork), StripPumpStopAndRestoreExcludePendingWork),
        new(nameof(StripPublicationKeepsLatestWholeResult), StripPublicationKeepsLatestWholeResult),
        new(nameof(StripPublicationOwnsInputAndRetainsSuccessOnFailure), StripPublicationOwnsInputAndRetainsSuccessOnFailure),
        new(nameof(StripPublicationRestoreAndStopFenceOldWork), StripPublicationRestoreAndStopFenceOldWork),
        new(nameof(StripPublicationCancellationAndConcurrentStopAreAtomic), StripPublicationCancellationAndConcurrentStopAreAtomic),
        new(nameof(StripDisplaySelectsOnlyWholeMatchingResults), StripDisplaySelectsOnlyWholeMatchingResults),
        new(nameof(StripDisplayRejectsPhaseScaleAndGutterChanges), StripDisplayRejectsPhaseScaleAndGutterChanges),
        new(nameof(StripDisplayPreservesPinnedReuseAcrossBackgroundProgress), StripDisplayPreservesPinnedReuseAcrossBackgroundProgress),
        new(nameof(StripDisplayRestoreAndInvalidLayoutPreserveResults), StripDisplayRestoreAndInvalidLayoutPreserveResults),
        new(nameof(StripResizeCommitsPatientAndCalibrationTogether), StripResizeCommitsPatientAndCalibrationTogether),
        new(nameof(StripRejectsCalibrationFailureWithoutPartialReplacement), StripRejectsCalibrationFailureWithoutPartialReplacement),
        new(nameof(StripRestoreOwnsEvidenceAndRebuildsBothLayers), StripRestoreOwnsEvidenceAndRebuildsBothLayers),
        new(nameof(StripNoDataAndCancellationKeepCalibrationIndependent), StripNoDataAndCancellationKeepCalibrationIndependent),
    ];

    private static EcgStripCheckpoint ShortInput()
    {
        EcgStripCheckpoint input = Input();
        SweepPathSample second = input.Source.Samples[1];
        return input with
        {
            Source = input.Source with
            {
                Samples = new[] { input.Source.Samples[0], second with
        {
            CycleOffsetNs = 1_020_000_000,
            Point = second.Point with { X = new(81, 0, 10_000_000_000) },
        } }
            }
        };
    }

    private static void StripReductionPublishesMatchingGeometryTogether()
    {
        ReconstructedEcgStrip strip = new EcgStripReconstructor(2, 2, new(20, 20)).Replace(Input());
        Check.That(strip.ColumnReduction is not null && strip.ColumnReduction.Envelopes.Count == 10 &&
            ReferenceEquals(strip.PatientFrame, strip.ColumnReduction.Frame.SourceFrame) &&
            ReferenceEquals(strip.Checkpoint.Source, strip.ColumnReduction.Frame.Checkpoint) &&
            strip.ColumnReduction.Envelopes.All(envelope => envelope.MinimumY.Y == new ExactPlotCoordinate(40, 1)) &&
            strip.Calibration.Points[1].Y.PixelNumerator == 40,
            "patient, column summaries and calibration share one validated scale and source checkpoint");
        Check.That(new EcgStripReconstructor(2, 2).Replace(Input()).ColumnReduction is null,
            "column processing requires explicit local capacity configuration");
    }

    private static void StripReductionFailurePreservesCompletedResult()
    {
        EcgStripReconstructor reconstructor = new(2, 2, new(20, 5));
        ReconstructedEcgStrip before = reconstructor.Replace(ShortInput());
        Check.That(Reason(() => reconstructor.Replace(Input())) == "ClippedEnvelope.OutputLimitExceeded" &&
            ReferenceEquals(before, reconstructor.Current) && ReferenceEquals(before.Checkpoint, reconstructor.CaptureCheckpoint()),
            "late summary overflow cannot publish a new patient frame with missing reduction");
        Check.That(Reason(() => { _ = new EcgStripReconstructor(2, 2, new(0, 1)); }) == "EcgStrip.InvalidColumnLimits",
            "enabled reduction requires positive limits before any work is admitted");
    }

    private static void StripReductionRestoresUnderExplicitLimits()
    {
        EcgStripPublication original = EcgStripPublication.Restore(2, 2, Input(), new(20, 20));
        ReconstructedEcgStrip first = original.CapturePublished()!.Strip;
        EcgStripWorkPump restored = EcgStripWorkPump.Restore(2, 2, first.Checkpoint, new(20, 20));
        Check.That(first.ColumnReduction!.Envelopes.SequenceEqual(restored.CapturePublished()!.Strip.ColumnReduction!.Envelopes),
            "restoration rebuilds summaries from source evidence under caller limits");
        Check.That(Reason(() => EcgStripReconstructor.Restore(2, 2, first.Checkpoint, new(2, 2))) == "EcgStrip.InvalidCheckpoint",
            "a restored source exceeding new piece limits is not accepted by trusting old output");
    }

    private static void StripReductionWorkerRecoversFromOutputOverflow()
    {
        EcgStripWorker worker = new(2, 2, new(5, 5));
        try
        {
            worker.Enqueue(ShortInput());
            Wait(worker.WaitForIdleAsync());
            PublishedEcgStrip before = worker.CapturePublished()!;
            worker.Enqueue(Input());
            Wait(worker.WaitForIdleAsync());
            Check.That(worker.LastFailureCode == "ColumnFrame.OutputLimitExceeded" && ReferenceEquals(before, worker.CapturePublished()),
                "worker reports reduction capacity failure without replacing the complete strip");
            worker.Enqueue(ShortInput() with { PulseLeftPixels = 6 });
            Wait(worker.WaitForIdleAsync());
            Check.That(worker.LastFailureCode is null && worker.CapturePublished()!.Strip.ColumnReduction!.Envelopes.Count == 1 &&
                worker.CapturePublished()!.Strip.Checkpoint.PulseLeftPixels == 6,
                "later work publishes summaries and calibration together after capacity failure");
        }
        finally { Wait(worker.DisposeAsync().AsTask()); }
    }

    private static EcgStripDisplaySnapshot ComposeDisplay(SweepStateProjectionState state, PublishedEcgStrip? published,
        long authorityNs = 1, int pulseLeft = 5) => EcgStripDisplayComposition.Compose(state, authorityNs,
            new[] { new NumericNoDataPolicy("HR", Guid.Parse("22222222-2222-4222-8222-222222222222"), 10) },
            30, 500, Input().Source.Frame.VerticalScale!, 0, pulseLeft, published);

    private static void StripCompositionKeepsCurrentCalibrationWithoutSource()
    {
        EcgStripCheckpoint input = Input();
        EcgStripDisplaySnapshot missing = ComposeDisplay(input.Source.Frame.Presentation, null);
        Check.That(missing.SourceStrip is { ReasonCode: "FrameDisplay.Missing", Strip: null } &&
            missing.CurrentCalibration.Points.Count == 4 && missing.LiveSafety.PreserveCalibrationGutter,
            "current calibration is available before any worker publication");
        PublishedEcgStrip old = EcgStripPublication.Restore(2, 2, input).CapturePublished()!;
        EcgStripDisplaySnapshot moved = ComposeDisplay(input.Source.Frame.Presentation, old, pulseLeft: 6);
        Check.That(moved.SourceStrip is { ReasonCode: "EcgStripDisplay.CalibrationMismatch", Strip: null } &&
            moved.CurrentCalibration.Points[0].X.WholePixels == 6 && old.Strip.Calibration.Points[0].X.WholePixels == 5,
            "new layout supplies current scale geometry even when the old strip is rejected");
    }

    private static void StripCompositionUsesCurrentNoDataSafetyWithOldPublication()
    {
        EcgStripCheckpoint input = Input();
        PublishedEcgStrip old = EcgStripPublication.Restore(2, 2, input).CapturePublished()!;
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState).Disconnect(false, 1), 0, 0);
        machine.Advance(5_000_000_000, 0);
        EcgStripDisplaySnapshot result = ComposeDisplay(machine.CaptureState(), old);
        Check.That(result.SourceStrip.Strip is null && result.Connectivity.Visible &&
            result.Connectivity.Message == ConnectivityBannerMessage.NoData &&
            result.LiveSafety.PatientAlarms == PatientAlarmSuspension.SuspendedUnknown &&
            result.CurrentRegions.Regions.Any(region => region.Kind == SweepTraceRegionKind.NoDataBaseline) &&
            result.CurrentRegions.Regions.Any(region => region.Kind == SweepTraceRegionKind.RetainSourceTrace) &&
            !result.LiveSafety.ClearLiveTraceImmediately,
            "stale complete strip cannot hide current NoData coverage or authorize clearing the full trace");
        Check.That(result.CurrentCalibration.Points.SequenceEqual(old.Strip.Calibration.Points),
            "disconnect preserves scale independently of source validity");
    }

    private static void StripCompositionKeepsPinnedHistoryAndIndependentNumericClock()
    {
        EcgStripCheckpoint input = Input();
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.EnterReview("record.one", 0, 0, 0);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState).Disconnect(false, 1), 0, 0);
        PublishedEcgStrip pinned = EcgStripPublication.Restore(2, 2, input with
        {
            Source = input.Source with { Frame = input.Source.Frame with { Presentation = machine.CaptureState() } },
        }).CapturePublished()!;
        machine.Advance(20_000_000_000, 0);
        EcgStripDisplaySnapshot before = ComposeDisplay(machine.CaptureState(), pinned, 10);
        EcgStripDisplaySnapshot expiry = ComposeDisplay(machine.CaptureState(), pinned, 11);
        Check.That(before.SourceStrip.Strip == pinned.Strip && expiry.SourceStrip.Strip == pinned.Strip &&
            expiry.CurrentRegions.Regions.Single().Kind == SweepTraceRegionKind.PinnedHistory &&
            expiry.Presentation.TransientReplayPolicy == TransientReplayPolicy.Suppress &&
            before.LiveSafety.Numerics[0].ValuePresentation == NumericValuePresentation.PreserveLastValue &&
            expiry.LiveSafety.Numerics[0].ValuePresentation == NumericValuePresentation.UnavailableMarker,
            "authority numeric expiry coexists with unchanged pinned geometry and suppressed historical replay");
    }

    private static void StripCompositionRestoresAndRejectsWithoutMutation()
    {
        EcgStripPublication publication = EcgStripPublication.Restore(2, 2, Input());
        PublishedEcgStrip before = publication.CapturePublished()!;
        SweepStateProjectionState state = before.Strip.Checkpoint.Source.Frame.Presentation;
        PublishedEcgStrip restored = EcgStripPublication.Restore(2, 2, before.Strip.Checkpoint).CapturePublished()!;
        EcgStripDisplaySnapshot result = ComposeDisplay(state, restored);
        Check.That(result.SourceStrip.Strip == restored.Strip && result.CurrentCalibration.Points.SequenceEqual(before.Strip.Calibration.Points),
            "owned checkpoint replay matches independently regenerated current calibration");
        Check.That(Reason(() => ComposeDisplay(state, before, pulseLeft: 29)) == "EcgCalibration.InsufficientSpace" &&
            ReferenceEquals(before, publication.CapturePublished()), "invalid target layout never mutates background publication");
    }

    private static void Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

    private static void StripWorkerPublishesLatestAndWakesAgain()
    {
        EcgStripWorker worker = new(2, 2);
        try
        {
            ulong latest = 0;
            for (int index = 0; index < 16; index++) { latest = worker.Enqueue(Input() with { PulseLeftPixels = 5 + index % 3 }); }
            Wait(worker.WaitForIdleAsync());
            PublishedEcgStrip first = worker.CapturePublished()!;
            Check.That(first.LocalGeneration == latest && first.Strip.PatientFrame.Segments.Count == 1 &&
                first.Strip.Calibration.Points[0].X.WholePixels == 5 && worker.LastFailureCode is null,
                "idle includes all previously admitted work and exposes the latest whole strip");
            latest = worker.Enqueue(Input() with { PulseLeftPixels = 8 });
            Wait(worker.WaitForIdleAsync());
            Check.That(worker.CapturePublished()!.LocalGeneration == latest &&
                worker.CapturePublished()!.Strip.Calibration.Points[0].X.WholePixels == 8,
                "a sleeping worker wakes for later requests without per-request tasks");
        }
        finally { Wait(worker.DisposeAsync().AsTask()); }
    }

    private static void StripWorkerRecoversFromInvalidSampleEvidence()
    {
        EcgStripWorker worker = new(2, 2);
        try
        {
            worker.Enqueue(Input());
            Wait(worker.WaitForIdleAsync());
            PublishedEcgStrip before = worker.CapturePublished()!;
            EcgStripCheckpoint bad = Input();
            bad = bad with
            {
                Source = bad.Source with
                {
                    Samples = new[] { bad.Source.Samples[0], bad.Source.Samples[1] with { Voltage = new(1, 0) } },
                }
            };
            worker.Enqueue(bad);
            Wait(worker.WaitForIdleAsync());
            Check.That(worker.LastFailureCode == "SweepFrame.InvalidVoltageAmplitude" &&
                ReferenceEquals(before, worker.CapturePublished()), "invalid sample reports an expected failure and preserves both layers");
            worker.Enqueue(Input() with { PulseLeftPixels = 6 });
            Wait(worker.WaitForIdleAsync());
            Check.That(worker.LastFailureCode is null && worker.CapturePublished()!.Strip.Checkpoint.PulseLeftPixels == 6,
                "later valid work clears the diagnostic and publishes a complete replacement");
        }
        finally { Wait(worker.DisposeAsync().AsTask()); }
    }

    private static void StripWorkerShutdownJoinsAndRejectsAdmission()
    {
        EcgStripWorker worker = new(2, 2);
        worker.Enqueue(Input());
        Task idle = worker.WaitForIdleAsync();
        Wait(worker.DisposeAsync().AsTask());
        PublishedEcgStrip? final = worker.CapturePublished();
        Wait(idle);
        Wait(worker.DisposeAsync().AsTask());
        Check.That(ReferenceEquals(final, worker.CapturePublished()) && worker.LastFailureCode is null,
            "shutdown joins the worker, is repeatable and does not report normal cancellation as input failure");
        try
        {
            worker.Enqueue(Input());
            throw new InvalidOperationException("disposed worker admitted work");
        }
        catch (ObjectDisposedException) { }
    }

    private static void StripWorkerRebuildsOwnedCheckpointInNewLifecycle()
    {
        EcgStripWorker first = new(2, 2);
        PublishedEcgStrip original;
        try
        {
            EcgStripCheckpoint input = Input();
            SweepPathSample[] samples = input.Source.Samples.ToArray();
            first.Enqueue(input with { Source = input.Source with { Samples = samples } });
            samples[1] = samples[1] with { Voltage = new(1, 0) };
            Wait(first.WaitForIdleAsync());
            original = first.CapturePublished()!;
        }
        finally { Wait(first.DisposeAsync().AsTask()); }
        EcgStripWorker restored = new(2, 2);
        try
        {
            restored.Enqueue(original.Strip.Checkpoint);
            Wait(restored.WaitForIdleAsync());
            ReconstructedEcgStrip result = restored.CapturePublished()!.Strip;
            Check.That(result.PatientFrame.Segments.SequenceEqual(original.Strip.PatientFrame.Segments) &&
                result.Calibration.Points.SequenceEqual(original.Strip.Calibration.Points),
                "a new worker revalidates owned completed evidence rather than reviving old pending work");
        }
        finally { Wait(restored.DisposeAsync().AsTask()); }
    }

    private static void StripPumpCoalescesAndRejectsInvalidAdmission()
    {
        EcgStripWorkPump pump = new(2, 2);
        pump.Enqueue(Input());
        ulong latest = pump.Enqueue(Input() with { PulseLeftPixels = 6 });
        Check.That(Reason(() => pump.Enqueue(Input() with { PulseLeftPixels = 29 })) == "EcgCalibration.InsufficientSpace",
            "invalid calibration cannot replace a valid pending strip");
        Check.That(pump.ProcessNext() == SweepFramePublicationStatus.Published && pump.ProcessNext() is null &&
            pump.CapturePublished()!.LocalGeneration == latest && pump.CapturePublished()!.Strip.Checkpoint.PulseLeftPixels == 6,
            "only the latest valid pending strip is reconstructed and published");
    }

    private static void StripPumpFailureReleasesSlotWithoutPartialPublication()
    {
        EcgStripWorkPump pump = EcgStripWorkPump.Restore(2, 2, Input());
        PublishedEcgStrip before = pump.CapturePublished()!;
        EcgStripCheckpoint bad = Input();
        bad = bad with
        {
            Source = bad.Source with
            {
                Samples = new[] { bad.Source.Samples[0], bad.Source.Samples[1] with { Voltage = new(2000, 1) } },
            }
        };
        pump.Enqueue(bad);
        Check.That(Reason(() => pump.ProcessNext()) == "SweepFrame.VoltageMappingMismatch" &&
            ReferenceEquals(before, pump.CapturePublished()) && pump.ProcessNext() is null,
            "failed reconstruction releases and consumes its slot without exposing a partial strip");
        ulong next = pump.Enqueue(Input() with { PulseLeftPixels = 7 });
        Check.That(pump.ProcessNext() == SweepFramePublicationStatus.Published && pump.CapturePublished()!.LocalGeneration == next &&
            pump.CapturePublished()!.Strip.Calibration.Points[0].X.WholePixels == 7,
            "later valid work can publish both layers after failure");
    }

    private static void StripPumpConcurrentConsumersAndCancellationKeepOneRequest()
    {
        EcgStripWorkPump pump = new(2, 2);
        ulong generation = pump.Enqueue(Input());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            pump.ProcessNext(cancellation.Token);
            throw new InvalidOperationException("cancelled pump accepted");
        }
        catch (OperationCanceledException exception)
        {
            Check.That(exception.CancellationToken == cancellation.Token && pump.CapturePublished() is null,
                "pre-cancelled processing cannot consume or publish a pending request");
        }
        SweepFramePublicationStatus?[] outcomes = new SweepFramePublicationStatus?[16];
        Parallel.For(0, outcomes.Length, index => outcomes[index] = pump.ProcessNext());
        Check.That(outcomes.Count(status => status == SweepFramePublicationStatus.Published) == 1 &&
            outcomes.Count(status => status is null) == 15 && pump.CapturePublished()!.LocalGeneration == generation,
            "concurrent consumers claim one pending request exactly once");
    }

    private static void StripPumpStopAndRestoreExcludePendingWork()
    {
        EcgStripWorkPump pump = EcgStripWorkPump.Restore(2, 2, Input());
        PublishedEcgStrip before = pump.CapturePublished()!;
        pump.Enqueue(Input() with { PulseLeftPixels = 6 });
        Check.That(ReferenceEquals(before, pump.Stop()) && ReferenceEquals(before, pump.Stop()) && pump.ProcessNext() is null &&
            Reason(() => pump.Enqueue(Input())) == "StripPublication.Stopped",
            "stop drops pending work and freezes the complete published snapshot");
        EcgStripWorkPump restored = EcgStripWorkPump.Restore(2, 2, before.Strip.Checkpoint);
        Check.That(restored.ProcessNext() is null && restored.CapturePublished()!.Strip.Checkpoint.PulseLeftPixels == 5,
            "restore regenerates only the completed checkpoint, not pending or stopped lifecycle state");
        restored.Enqueue(Input() with { PulseLeftPixels = 8 });
        Check.That(restored.ProcessNext() == SweepFramePublicationStatus.Published &&
            ReferenceEquals(before, pump.CapturePublished()), "restored pump has an independent active lifecycle");
    }

    private static void StripPublicationKeepsLatestWholeResult()
    {
        EcgStripPublication publication = new(2, 2);
        EcgStripWork old = publication.Request(Input());
        EcgStripWork latest = publication.Request(Input() with { PulseLeftPixels = 6 });
        SweepFramePublicationStatus[] statuses = new SweepFramePublicationStatus[16];
        Parallel.For(0, statuses.Length, index => statuses[index] = publication.Complete(latest));
        PublishedEcgStrip result = publication.CapturePublished()!;
        Check.That(statuses.Count(status => status == SweepFramePublicationStatus.Published) == 1 &&
            statuses.Count(status => status == SweepFramePublicationStatus.AlreadyPublished) == 15 &&
            publication.Complete(old) == SweepFramePublicationStatus.Superseded &&
            result.LocalGeneration == latest.Generation && result.Strip.Calibration.Points[0].X.WholePixels == 6 &&
            result.Strip.Checkpoint.PulseLeftPixels == 6 && result.Strip.PatientFrame.Segments.Count == 1,
            "duplicate concurrent completion publishes once and old work cannot replace either layer");
    }

    private static void StripPublicationOwnsInputAndRetainsSuccessOnFailure()
    {
        EcgStripPublication publication = new(2, 2);
        EcgStripCheckpoint input = Input();
        SweepPathSample[] samples = input.Source.Samples.ToArray();
        EcgStripWork work = publication.Request(input with { Source = input.Source with { Samples = samples } });
        samples[1] = samples[1] with { Voltage = new(2000, 1) };
        publication.Complete(work);
        PublishedEcgStrip before = publication.CapturePublished()!;
        Check.That(before.Strip.Checkpoint.Source.Samples[1].Voltage == new EcgSampleVoltage(1000, 1),
            "admission owns caller sample evidence before asynchronous work begins");
        Check.That(Reason(() => publication.Request(input with { PulseLeftPixels = 29 })) == "EcgCalibration.InsufficientSpace",
            "invalid gutter is rejected before allocating a generation");
        EcgStripWork invalid = publication.Request(input with { Source = input.Source with { Samples = samples } });
        Check.That(invalid.Generation == before.LocalGeneration + 1 &&
            Reason(() => publication.Complete(invalid)) == "SweepFrame.VoltageMappingMismatch" &&
            ReferenceEquals(before, publication.CapturePublished()),
            "late sample failure preserves the whole prior publication and rejected layout consumes no generation");
    }

    private static void StripPublicationRestoreAndStopFenceOldWork()
    {
        EcgStripPublication publication = EcgStripPublication.Restore(2, 2, Input());
        EcgStripWork work = publication.Request(Input());
        PublishedEcgStrip final = publication.Stop()!;
        Check.That(ReferenceEquals(final, publication.Stop()) && publication.Complete(work) == SweepFramePublicationStatus.Stopped &&
            Reason(() => publication.Request(Input())) == "StripPublication.Stopped", "stop is an irreversible idempotent publication fence");
        EcgStripPublication restored = EcgStripPublication.Restore(2, 2, final.Strip.Checkpoint);
        Check.That(Reason(() => restored.Complete(work)) == "StripPublication.ForeignWork" &&
            restored.CapturePublished()!.Strip.PatientFrame.Segments.SequenceEqual(final.Strip.PatientFrame.Segments) &&
            restored.CapturePublished()!.Strip.Calibration.Points.SequenceEqual(final.Strip.Calibration.Points) &&
            restored.Complete(restored.Request(Input())) == SweepFramePublicationStatus.Published,
            "restoration revalidates both layers in a new active owner and rejects old lifecycle tickets");
    }

    private static void StripPublicationCancellationAndConcurrentStopAreAtomic()
    {
        EcgStripPublication publication = EcgStripPublication.Restore(2, 2, Input());
        PublishedEcgStrip before = publication.CapturePublished()!;
        EcgStripWork work = publication.Request(Input() with { PulseLeftPixels = 6 });
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            publication.Complete(work, cancellation.Token);
            throw new InvalidOperationException("cancelled publication accepted");
        }
        catch (OperationCanceledException exception)
        {
            Check.That(exception.CancellationToken == cancellation.Token && ReferenceEquals(before, publication.CapturePublished()),
                "cancelled work preserves the entire prior publication");
        }
        PublishedEcgStrip? stopped = null;
        Parallel.Invoke(() => publication.Complete(work), () => stopped = publication.Stop());
        Check.That(ReferenceEquals(stopped, publication.CapturePublished()) &&
            publication.Complete(work) == SweepFramePublicationStatus.Stopped &&
            stopped!.Strip.Checkpoint.PulseLeftPixels == stopped.Strip.Calibration.Points[0].X.WholePixels,
            "completion-versus-stop race yields one final internally consistent strip");
    }

    private static EcgStripDisplaySelection Select(ReconstructedEcgStrip strip, SweepStateProjectionState? state = null,
        int pulseLeft = 5, EcgVerticalScale? scale = null) => EcgStripDisplayGate.Select(
            state ?? strip.Checkpoint.Source.Frame.Presentation, 30, 500,
            scale ?? strip.Checkpoint.Source.Frame.VerticalScale!, 0, pulseLeft, strip);

    private static void StripDisplaySelectsOnlyWholeMatchingResults()
    {
        EcgStripReconstructor reconstructor = new(2, 2);
        ReconstructedEcgStrip strip = reconstructor.Replace(Input());
        Check.That(Select(strip) is { ReasonCode: "EcgStripDisplay.Matched" } && ReferenceEquals(Select(strip).Strip, strip),
            "matched selection returns the complete patient/calibration result intact");
        Check.That(EcgStripDisplayGate.Select(strip.Checkpoint.Source.Frame.Presentation, 30, 500,
            strip.Checkpoint.Source.Frame.VerticalScale!, 0, 5, null) is { ReasonCode: "FrameDisplay.Missing", Strip: null },
            "missing geometry cannot masquerade as a complete strip");
        Check.That(Select(strip, scale: strip.Checkpoint.Source.Frame.VerticalScale! with
        {
            PixelsPerMillivoltNumerator = 40,
            PixelsPerMillivoltDenominator = 2,
        }).Strip == strip, "equivalent gain fractions match both patient and calibration");
    }

    private static void StripDisplayRejectsPhaseScaleAndGutterChanges()
    {
        ReconstructedEcgStrip strip = new EcgStripReconstructor(2, 2).Replace(Input());
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Restore(strip.Checkpoint.Source.Frame.Presentation);
        machine.Advance(1, 0);
        Check.That(Select(strip, machine.CaptureState()) is { ReasonCode: "FrameDisplay.PresentationMismatch", Strip: null } &&
            Select(strip, scale: strip.Checkpoint.Source.Frame.VerticalScale! with { PixelsPerMillivoltNumerator = 40 }) is
            { ReasonCode: "FrameDisplay.ScaleMismatch", Strip: null },
            "old phase or voltage scale rejects the entire strip, not just one layer");
        Check.That(Select(strip, pulseLeft: 6) is { ReasonCode: "EcgStripDisplay.CalibrationMismatch", Strip: null } &&
            EcgStripDisplayGate.Select(strip.Checkpoint.Source.Frame.Presentation, 30, 500,
                strip.Checkpoint.Source.Frame.VerticalScale!, 1, 5, strip).Strip is null,
            "pulse and gutter layout changes reject otherwise compatible patient pixels");
    }

    private static void StripDisplayPreservesPinnedReuseAcrossBackgroundProgress()
    {
        foreach (bool review in new[] { false, true })
        {
            EcgStripCheckpoint input = Input();
            SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
            if (review) { machine.EnterReview("record.one", 0, 0, 0); }
            else { machine.EnterFrozen(0, 0); }
            ReconstructedEcgStrip strip = new EcgStripReconstructor(2, 2).Replace(input with
            {
                Source = input.Source with { Frame = input.Source.Frame with { Presentation = machine.CaptureState() } },
            });
            machine.Advance(10_000_000_000, 20);
            Check.That(ReferenceEquals(Select(strip, machine.CaptureState()).Strip, strip),
                "background progress does not invalidate a fixed original range or its calibration");
            if (review)
            {
                machine.SeekReview(1);
                Check.That(Select(strip, machine.CaptureState()).Strip is null, "review seek rejects the old whole strip");
            }
        }
    }

    private static void StripDisplayRestoreAndInvalidLayoutPreserveResults()
    {
        EcgStripReconstructor reconstructor = new(2, 2);
        ReconstructedEcgStrip before = reconstructor.Replace(Input());
        ReconstructedEcgStrip restored = EcgStripReconstructor.Restore(2, 2, before.Checkpoint).Current!;
        Check.That(Select(restored).ReasonCode == "EcgStripDisplay.Matched" &&
            restored.Calibration.Points.SequenceEqual(before.Calibration.Points), "restored evidence yields matching complete geometry");
        Check.That(Reason(() => Select(before, pulseLeft: 29)) == "EcgCalibration.InsufficientSpace" &&
            ReferenceEquals(reconstructor.Current, before) && ReferenceEquals(reconstructor.CaptureCheckpoint(), before.Checkpoint),
            "invalid target layout rejects without retiring or changing the retained reconstruction");
    }

    private static EcgStripCheckpoint Input()
    {
        SweepStateProjectionState state = SweepStateProjectionStateMachine.Start(
            new("ecg", 4, 5, 0, 10_000_000_000, 200_000_000, 10_200_000_000), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0).CaptureState();
        SweepFramePathState frame = new(state, 30, 500, 0, 100, null, new(0, 100, 60, 20, 1));
        SweepSampleSource source = new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
            Guid.Parse("22222222-2222-4222-8222-222222222222"), Guid.Parse("33333333-3333-4333-8333-333333333333"),
            1, 2, 3, 4, 5, 500, 1);
        SweepFramePathBuilder builder = SweepFramePathBuilder.Restore(frame);
        builder.AppendVoltageAtOffset(source, 0, 0, true, 1_000_000_000, new(1000, 1));
        SweepPathSample first = builder.CaptureState().Previous!;
        builder.AppendVoltageAtOffset(source, 1, 0, true, 1_200_000_000, new(1000, 1));
        return new(new(frame, new[] { first, builder.CaptureState().Previous! }), 0, 5);
    }

    private static void StripResizeCommitsPatientAndCalibrationTogether()
    {
        EcgStripReconstructor strip = new(2, 2);
        ReconstructedEcgStrip first = strip.Replace(Input());
        ReconstructedEcgStrip resized = strip.ResizeHorizontal(30, 1000, 0, 5);
        Check.That(first.Calibration.Points[2].X.WholePixels == 15 && resized.Calibration.Points[2].X.WholePixels == 25 &&
            first.PatientFrame.Segments[0].Segment.Start.X == new ExactPlotCoordinate(80, 1) &&
            resized.PatientFrame.Segments[0].Segment.Start.X == new ExactPlotCoordinate(130, 1),
            "patient and 200 ms calibration width use the same resized time scale");
        Check.That(resized.Calibration.Points[1].Y.PixelNumerator == 40 &&
            resized.PatientFrame.Segments[0].Segment.Start.Y == new ExactPlotCoordinate(40, 1) &&
            ReferenceEquals(strip.Current, resized) && ReferenceEquals(strip.CaptureCheckpoint(), resized.Checkpoint),
            "one completed result retains matching voltage scale and its checkpoint");
    }

    private static void StripRejectsCalibrationFailureWithoutPartialReplacement()
    {
        EcgStripReconstructor strip = new(2, 2);
        ReconstructedEcgStrip before = strip.Replace(Input());
        Check.That(Reason(() => strip.ResizeHorizontal(30, 2000, 0, 5)) == "EcgCalibration.InsufficientSpace" &&
            ReferenceEquals(before, strip.Current) && ReferenceEquals(before.Checkpoint, strip.CaptureCheckpoint()),
            "a successfully rebuilt patient frame cannot commit if the new calibration does not fit");
        EcgStripCheckpoint unscaled = Input();
        unscaled = unscaled with
        {
            Source = unscaled.Source with
            {
                Frame = unscaled.Source.Frame with { VerticalScale = null },
                Samples = Array.Empty<SweepPathSample>(),
            }
        };
        Check.That(Reason(() => strip.Replace(unscaled)) == "EcgStrip.VoltageScaleRequired" && ReferenceEquals(before, strip.Current),
            "ECG strip cannot invent an undeclared calibration scale");
        Check.That(Reason(() => new EcgStripReconstructor(2, 2).ResizeHorizontal(30, 500, 0, 5)) == "EcgStrip.NoFrame",
            "resize requires an accepted source frame");
    }

    private static void StripRestoreOwnsEvidenceAndRebuildsBothLayers()
    {
        EcgStripCheckpoint input = Input();
        SweepPathSample[] callerSamples = input.Source.Samples.ToArray();
        EcgStripReconstructor strip = new(2, 2);
        ReconstructedEcgStrip first = strip.Replace(input with { Source = input.Source with { Samples = callerSamples } });
        callerSamples[1] = callerSamples[1] with { CycleOffsetNs = 2_000_000_000 };
        ReconstructedEcgStrip restored = EcgStripReconstructor.Restore(2, 2, strip.CaptureCheckpoint()!).Current!;
        Check.That(first.PatientFrame.Segments.SequenceEqual(restored.PatientFrame.Segments) &&
            first.Calibration.Points.SequenceEqual(restored.Calibration.Points),
            "owned sample evidence reconstructs both layers despite caller array mutation");
        Check.That(Reason(() => EcgStripReconstructor.Restore(2, 2, first.Checkpoint with { PulseLeftPixels = 29 })) ==
            "EcgStrip.InvalidCheckpoint", "restore revalidates gutter capacity rather than trusting stored glyphs");
    }

    private static void StripNoDataAndCancellationKeepCalibrationIndependent()
    {
        EcgStripReconstructor strip = new(2, 2);
        EcgStripCheckpoint input = Input();
        ReconstructedEcgStrip live = strip.Replace(input);
        SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState).Disconnect(false, 1), 0, 0);
        machine.Advance(10_000_000_000, 0);
        ReconstructedEcgStrip noData = strip.Replace(input with
        {
            Source = input.Source with
            {
                Frame = input.Source.Frame with { Presentation = machine.CaptureState() },
            }
        });
        Check.That(noData.PatientFrame.Segments.Count == 0 && noData.Calibration.Points.SequenceEqual(live.Calibration.Points),
            "fully swept NoData removes patient paths while preserving the independent scale glyph");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            strip.ResizeHorizontal(30, 1000, 0, 5, cancellation.Token);
            throw new InvalidOperationException("cancelled resize accepted");
        }
        catch (OperationCanceledException exception)
        {
            Check.That(exception.CancellationToken == cancellation.Token && ReferenceEquals(noData, strip.Current),
                "cancelled resize preserves the entire accepted strip and cancellation identity");
        }
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (EcgStripException exception) { return exception.ReasonCode; }
        catch (EcgCalibrationGeometryException exception) { return exception.ReasonCode; }
        catch (EcgStripPublicationException exception) { return exception.ReasonCode; }
        catch (SweepFramePathException exception) { return exception.ReasonCode; }
        catch (SweepFrameReconstructionException exception) { return exception.ReasonCode; }
    }
}
