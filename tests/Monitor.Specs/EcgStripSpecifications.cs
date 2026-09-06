// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class EcgStripSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(StripDisplaySelectsOnlyWholeMatchingResults), StripDisplaySelectsOnlyWholeMatchingResults),
        new(nameof(StripDisplayRejectsPhaseScaleAndGutterChanges), StripDisplayRejectsPhaseScaleAndGutterChanges),
        new(nameof(StripDisplayPreservesPinnedReuseAcrossBackgroundProgress), StripDisplayPreservesPinnedReuseAcrossBackgroundProgress),
        new(nameof(StripDisplayRestoreAndInvalidLayoutPreserveResults), StripDisplayRestoreAndInvalidLayoutPreserveResults),
        new(nameof(StripResizeCommitsPatientAndCalibrationTogether), StripResizeCommitsPatientAndCalibrationTogether),
        new(nameof(StripRejectsCalibrationFailureWithoutPartialReplacement), StripRejectsCalibrationFailureWithoutPartialReplacement),
        new(nameof(StripRestoreOwnsEvidenceAndRebuildsBothLayers), StripRestoreOwnsEvidenceAndRebuildsBothLayers),
        new(nameof(StripNoDataAndCancellationKeepCalibrationIndependent), StripNoDataAndCancellationKeepCalibrationIndependent),
    ];

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
    }
}
