// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Xml.Linq;
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Specs;

internal static class EcgStripSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SvgPreviewLabelsFrozenDataAnchorAcrossNoData), SvgPreviewLabelsFrozenDataAnchorAcrossNoData),
        new(nameof(SvgPreviewLabelsIndependentReviewCursor), SvgPreviewLabelsIndependentReviewCursor),
        new(nameof(SvgPreviewScaleLabelTracksHorizontalResize), SvgPreviewScaleLabelTracksHorizontalResize),
        new(nameof(SvgPreviewScaleLabelKeepsExactGainAcrossRestore), SvgPreviewScaleLabelKeepsExactGainAcrossRestore),
        new(nameof(SvgPreviewReservesNarrowCanvasLabels), SvgPreviewReservesNarrowCanvasLabels),
        new(nameof(SvgPreviewDeclaresLogicalDimensionsWithoutChangingEvidence), SvgPreviewDeclaresLogicalDimensionsWithoutChangingEvidence),
        new(nameof(SvgPreviewExcludesExactRightEdgeBeforeRounding), SvgPreviewExcludesExactRightEdgeBeforeRounding),
        new(nameof(SvgPreviewPreservesInclusiveAndArrivingEdges), SvgPreviewPreservesInclusiveAndArrivingEdges),
        new(nameof(SvgPreviewBatchesDisconnectedSegmentsWithoutBridging), SvgPreviewBatchesDisconnectedSegmentsWithoutBridging),
        new(nameof(SvgPreviewKeepsSeparateRegionClips), SvgPreviewKeepsSeparateRegionClips),
        new(nameof(SvgPreviewSeparatesPatientAndCalibrationLayers), SvgPreviewSeparatesPatientAndCalibrationLayers),
        new(nameof(SvgPreviewNoDataKeepsCalibrationAndStateLabel), SvgPreviewNoDataKeepsCalibrationAndStateLabel),
        new(nameof(SvgPreviewFormatsFractionsIndependentlyOfCulture), SvgPreviewFormatsFractionsIndependentlyOfCulture),
        new(nameof(SvgPreviewRestoreAndFailurePreserveSourceEvidence), SvgPreviewRestoreAndFailurePreserveSourceEvidence),
        new(nameof(StripDisplayRequiresRequestedColumnResults), StripDisplayRequiresRequestedColumnResults),
        new(nameof(StripDisplayReductionRequirementKeepsCurrentSafety), StripDisplayReductionRequirementKeepsCurrentSafety),
        new(nameof(StripDisplayRestoredReductionDoesNotBypassStateChecks), StripDisplayRestoredReductionDoesNotBypassStateChecks),
        new(nameof(ValidatedStripReductionMatchesIndependentReconstruction), ValidatedStripReductionMatchesIndependentReconstruction),
        new(nameof(ValidatedResizeReductionPreservesCapacityAndCheckpointRules), ValidatedResizeReductionPreservesCapacityAndCheckpointRules),
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
        new(nameof(StripResizeCommitsPatientAndCalibrationTogether), StripResizeCommitsPatientAndCalibrationTogether),
        new(nameof(StripRejectsCalibrationFailureWithoutPartialReplacement), StripRejectsCalibrationFailureWithoutPartialReplacement),
        new(nameof(StripRestoreOwnsEvidenceAndRebuildsBothLayers), StripRestoreOwnsEvidenceAndRebuildsBothLayers),
        new(nameof(StripNoDataAndCancellationKeepCalibrationIndependent), StripNoDataAndCancellationKeepCalibrationIndependent),
    ];

    private static void SvgPreviewLabelsFrozenDataAnchorAcrossNoData()
    {
        EcgStripCheckpoint input = Input();
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.EnterFrozen(1_000_000_000, 2_000_000_000);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState).Disconnect(false, 1),
            1_000_000_000, 2_000_000_000);
        machine.Advance(11_000_000_000, 2_000_000_000);
        input = input with { Source = input.Source with { Frame = input.Source.Frame with { Presentation = machine.CaptureState() } } };
        XNamespace svg = "http://www.w3.org/2000/svg";
        string output = EcgStripSvgPreview.Render(input, 2, 2);
        var root = XElement.Parse(output);
        Check.That(root.Elements(svg + "text").Any(text => text.Value == "FROZEN DATA TIME 2000000000 ns") &&
            (string?)root.Attribute("height") == "156" &&
            root.Elements(svg + "text").All(text => (long)text.Attribute("y")! < 156),
            "frozen time remains the data anchor instead of the independently advancing presentation clock");
        Check.That(EcgStripSvgPreview.Render(EcgStripReconstructor.Restore(2, 2, input).CaptureCheckpoint()!, 2, 2) == output,
            "restored frozen preview retains the exact labeled anchor");
    }

    private static void SvgPreviewLabelsIndependentReviewCursor()
    {
        EcgStripCheckpoint input = Input();
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.EnterReview("synthetic-record", 123456789, 0, 0);
        input = input with { Source = input.Source with { Frame = input.Source.Frame with { Presentation = machine.CaptureState() } } };
        XNamespace svg = "http://www.w3.org/2000/svg";
        var before = XElement.Parse(EcgStripSvgPreview.Render(input, 2, 2));
        machine.SeekReview(0);
        EcgStripCheckpoint sought = input with { Source = input.Source with { Frame = input.Source.Frame with { Presentation = machine.CaptureState() } } };
        var after = XElement.Parse(EcgStripSvgPreview.Render(sought, 2, 2));
        Check.That(before.Elements(svg + "text").Any(text => text.Value == "REVIEW DATA TIME 123456789 ns") &&
            after.Elements(svg + "text").Any(text => text.Value == "REVIEW DATA TIME 0 ns") &&
            XNode.DeepEquals(before.Element(svg + "g"), after.Element(svg + "g")),
            "review labels follow the independent cursor including zero without remapping supplied sample evidence");
        Check.That(!XElement.Parse(EcgStripSvgPreview.Render(Input(), 2, 2)).Elements(svg + "text")
            .Any(text => text.Value.Contains("DATA TIME", StringComparison.Ordinal)),
            "Live previews do not invent a pinned time annotation");
        SweepStateProjectionState accepted = machine.CaptureState();
        try { machine.SeekReview(-1); throw new InvalidOperationException("negative review cursor accepted"); }
        catch (SweepStateProjectionException exception) { Check.That(exception.ReasonCode == "SweepState.InvalidViewTransition", "invalid cursor retains the domain rejection"); }
        Check.That(machine.CaptureState() == accepted, "rejected seeking cannot change the labeled review cursor");
    }

    private static void SvgPreviewScaleLabelTracksHorizontalResize()
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        EcgStripReconstructor reconstructor = new(2, 2);
        EcgStripCheckpoint original = reconstructor.Replace(Input()).Checkpoint;
        EcgStripCheckpoint resized = reconstructor.ResizeHorizontal(30, 1000, 0, 5).Checkpoint;
        var before = XElement.Parse(EcgStripSvgPreview.Render(original, 2, 2));
        var after = XElement.Parse(EcgStripSvgPreview.Render(resized, 2, 2));
        Check.That(before.Elements(svg + "text").Last().Value == "CAL 1 mV x 200 ms / 50 px/s / 20 px/mV (logical)" &&
            after.Elements(svg + "text").Last().Value == "CAL 1 mV x 200 ms / 100 px/s / 20 px/mV (logical)" &&
            (string?)after.Elements(svg + "path").Single().Attribute("d") == "M 5 60 L 5 40 L 25 40 L 25 60",
            "resize changes time scale label and pulse width together without changing gain");
    }

    private static void SvgPreviewScaleLabelKeepsExactGainAcrossRestore()
    {
        EcgStripCheckpoint input = Input();
        EcgVerticalScale scale = input.Source.Frame.VerticalScale! with { PixelsPerMillivoltNumerator = 40, PixelsPerMillivoltDenominator = 6 };
        SweepPathSample[] samples = input.Source.Samples.Select(sample => sample with
        {
            Point = sample.Point with { Y = EcgVerticalGeometry.MapMicrovolts(scale, sample.Voltage!.NumeratorMicrovolts, sample.Voltage.Denominator) }
        }).ToArray();
        input = input with { Source = new(input.Source.Frame with { VerticalScale = scale }, samples) };
        string output = EcgStripSvgPreview.Render(input, 2, 2);
        XNamespace svg = "http://www.w3.org/2000/svg";
        var root = XElement.Parse(output);
        Check.That(root.Elements(svg + "text").Last().Value == "CAL 1 mV x 200 ms / 50 px/s / 20/3 px/mV (logical)" &&
            EcgStripSvgPreview.Render(EcgStripReconstructor.Restore(2, 2, input).CaptureCheckpoint()!, 2, 2) == output,
            "gain labels reduce exact fractions rather than rounding or claiming physical millimeters");
        Check.That(Reason(() => EcgStripSvgPreview.Render(input with { PulseLeftPixels = 29 }, 2, 2)) == "EcgCalibration.InsufficientSpace" &&
            EcgStripSvgPreview.Render(input, 2, 2) == output,
            "scale annotation does not bypass calibration rejection or modify accepted evidence");
    }

    private static void SvgPreviewReservesNarrowCanvasLabels()
    {
        EcgStripReconstructor reconstructor = new(2, 2);
        reconstructor.Replace(Input());
        EcgStripCheckpoint input = reconstructor.ResizeHorizontal(30, 50, 0, 5).Checkpoint;
        XNamespace svg = "http://www.w3.org/2000/svg";
        var root = XElement.Parse(EcgStripSvgPreview.Render(input, 2, 2));
        long width = long.Parse(root.Attribute("width")!.Value, CultureInfo.InvariantCulture);
        Check.That(width > 80 && root.Elements(svg + "text").All(label =>
            (long)label.Attribute("x")! + (long)label.Attribute("textLength")! <= width - 8 &&
            (string?)label.Attribute("lengthAdjust") == "spacingAndGlyphs"),
            "narrow plots reserve complete teaching and state label extents with both margins");
        Check.That((string?)root.Elements(svg + "path").Single().Attribute("d") == "M 5 60 L 5 40 L 6 40 L 6 60" &&
            (string?)root.Elements(svg + "g").Single().Elements(svg + "path").Single().Attribute("d") == "M 35 40 L 36 40",
            "annotation canvas expansion does not stretch patient geometry or calibration");
    }

    private static void SvgPreviewDeclaresLogicalDimensionsWithoutChangingEvidence()
    {
        EcgStripCheckpoint input = Input();
        string before = EcgStripSvgPreview.Render(input, 2, 2);
        var root = XElement.Parse(before);
        Check.That((string?)root.Attribute("width") == "530" && (string?)root.Attribute("height") == "144" &&
            (string?)root.Attribute("viewBox") == "0 0 530 144",
            "standalone intrinsic dimensions match the logical viewBox instead of browser defaults");
        Check.That(EcgStripSvgPreview.Render(EcgStripReconstructor.Restore(2, 2, input).CaptureCheckpoint()!, 2, 2) == before &&
            Reason(() => EcgStripSvgPreview.Render(input with { PulseLeftPixels = 29 }, 2, 2)) == "EcgCalibration.InsufficientSpace" &&
            EcgStripSvgPreview.Render(input, 2, 2) == before,
            "annotation layout remains deterministic across restore and rejected reconstruction");
    }

    private static EcgStripCheckpoint SvgEdgeInput(ulong firstOffset, ulong lastOffset)
    {
        EcgStripCheckpoint input = Input();
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.Advance(1_100_000_001, 1_100_000_001);
        SweepFramePathState frame = input.Source.Frame with { Presentation = machine.CaptureState() };
        var builder = SweepFramePathBuilder.Restore(frame);
        builder.AppendVoltageAtOffset(input.Source.Samples[0].Source, 0, 0, true, firstOffset, new(1000, 1));
        SweepPathSample first = builder.CaptureState().Previous!;
        builder.AppendVoltageAtOffset(first.Source, 1, 0, true, lastOffset, new(0, 1));
        return input with { Source = new(frame, new[] { first, builder.CaptureState().Previous! }) };
    }

    private static void SvgPreviewExcludesExactRightEdgeBeforeRounding()
    {
        EcgStripCheckpoint input = SvgEdgeInput(1_100_000_001, 1_100_000_001);
        XNamespace svg = "http://www.w3.org/2000/svg";
        ReconstructedEcgStrip strip = new EcgStripReconstructor(2, 2).Replace(input);
        string output = EcgStripSvgPreview.Render(input, 2, 2);
        var root = XElement.Parse(output);
        Check.That(strip.PatientFrame.Segments.Count == 1 &&
            !root.Elements(svg + "g").Single().Elements().Any() &&
            root.Elements(svg + "path").Single().Attribute("id")!.Value == "calibration",
            "fractional exclusive-edge line remains in exact closed geometry but cannot draw patient pixels");
        Check.That(EcgStripSvgPreview.Render(EcgStripReconstructor.Restore(2, 2, input).CaptureCheckpoint()!, 2, 2) == output,
            "restored boundary evidence preserves exclusion");
        EcgStripCheckpoint inside = SvgEdgeInput(1_100_000_000, 1_100_000_000);
        Check.That(XElement.Parse(EcgStripSvgPreview.Render(inside, 2, 2)).Elements(svg + "g").Single().Elements().Count() == 1,
            "a line just inside the region survives even when serialization rounds both X coordinates identically");
    }

    private static void SvgPreviewPreservesInclusiveAndArrivingEdges()
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        EcgStripCheckpoint inclusive = SvgEdgeInput(1_300_000_001, 1_300_000_001);
        var left = XElement.Parse(EcgStripSvgPreview.Render(inclusive, 2, 2));
        Check.That((string?)left.Elements(svg + "g").Single().Elements(svg + "path").Single().Attribute("d") == "M 95 40 L 95 60",
            "inclusive source left edge retains its vertical path");
        EcgStripCheckpoint arriving = SvgEdgeInput(1_000_000_000, 1_100_000_001);
        string before = EcgStripSvgPreview.Render(arriving, 2, 2);
        Check.That((string?)XElement.Parse(before).Elements(svg + "g").Single().Elements(svg + "path").Single().Attribute("d") == "M 80 40 L 85 60" &&
            Reason(() => EcgStripSvgPreview.Render(arriving, 1, 2)) == "FrameReconstruction.SampleLimitExceeded" &&
            EcgStripSvgPreview.Render(arriving, 2, 2) == before,
            "arriving intersections survive and failed preview validation leaves source evidence unchanged");
    }

    private static void SvgPreviewBatchesDisconnectedSegmentsWithoutBridging()
    {
        EcgStripCheckpoint input = Input();
        var builder = SweepFramePathBuilder.Restore(input.Source.Frame);
        List<SweepPathSample> samples = [];
        for (ulong index = 0; index < 4; index++)
        {
            builder.AppendVoltageAtOffset(input.Source.Samples[0].Source, index < 2 ? index : index + 1,
                0, true, 1_000_000_000 + index * 200_000_000, new(1000, 1));
            samples.Add(builder.CaptureState().Previous!);
        }
        input = input with { Source = input.Source with { Samples = samples } };
        XNamespace svg = "http://www.w3.org/2000/svg";
        string output = EcgStripSvgPreview.Render(input, 4, 2);
        XElement path = XElement.Parse(output).Elements(svg + "g").Single().Elements(svg + "path").Single();
        Check.That((string?)path.Attribute("d") == "M 80 40 L 90 40 M 100 40 L 110 40",
            "one region uses one node while missing sample indices remain separate move-to subpaths");
        Check.That(EcgStripSvgPreview.Render(EcgStripReconstructor.Restore(4, 2, input).CaptureCheckpoint()!, 4, 2) == output &&
            Reason(() => EcgStripSvgPreview.Render(input, 4, 1)) == "FrameReconstruction.SegmentLimitExceeded" &&
            EcgStripSvgPreview.Render(input, 4, 2) == output,
            "batching preserves restored output and segment capacity rejection without modifying evidence");
    }

    private static void SvgPreviewKeepsSeparateRegionClips()
    {
        EcgStripCheckpoint input = Input();
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.Advance(1_100_000_000, 1_100_000_000);
        SweepFramePathState frame = input.Source.Frame with { Presentation = machine.CaptureState() };
        var builder = SweepFramePathBuilder.Restore(frame);
        builder.AppendVoltageAtOffset(input.Source.Samples[0].Source, 0, 0, true, 1_000_000_000, new(1000, 1));
        SweepPathSample first = builder.CaptureState().Previous!;
        builder.AppendVoltageAtOffset(first.Source, 1, 0, true, 1_600_000_000, new(1000, 1));
        input = input with { Source = new(frame, new[] { first, builder.CaptureState().Previous! }) };
        XNamespace svg = "http://www.w3.org/2000/svg";
        XElement[] paths = XElement.Parse(EcgStripSvgPreview.Render(input, 2, 2))
            .Elements(svg + "g").Single().Elements(svg + "path").ToArray();
        Check.That(paths.Length == 2 && (string?)paths[0].Attribute("d") == "M 80 40 L 85 40" &&
            (string?)paths[1].Attribute("d") == "M 95 40 L 110 40" &&
            (string?)paths[0].Attribute("clip-path") != (string?)paths[1].Attribute("clip-path"),
            "a crossing segment retains separate clipped paths on either side of the erase gap");
        EcgStripCheckpoint empty = input with { Source = input.Source with { Samples = Array.Empty<SweepPathSample>() } };
        Check.That(!XElement.Parse(EcgStripSvgPreview.Render(empty, 2, 2)).Elements(svg + "g").Single().Elements().Any(),
            "regions without patient segments emit no empty patient path nodes");
    }

    private static void SvgPreviewSeparatesPatientAndCalibrationLayers()
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        var root = XElement.Parse(EcgStripSvgPreview.Render(Input(), 2, 2));
        XElement trace = root.Elements(svg + "g").Single();
        XElement calibration = root.Elements(svg + "path").Single();
        Check.That(root.Name == svg + "svg" && (string?)root.Attribute("viewBox") == "0 0 530 144" &&
            trace.Elements(svg + "path").Single().Attribute("clip-path") is not null &&
            (string?)calibration.Attribute("clip-path") == "url(#gutter)" &&
            (string?)calibration.Attribute("d") == "M 5 60 L 5 40 L 15 40 L 15 60" &&
            root.Elements().ToList().IndexOf(trace) < root.Elements().ToList().IndexOf(calibration),
            "preview emits clipped patient geometry before the separate persistent scale path");
        Check.That(root.Elements(svg + "text").Any(text => text.Value.Contains("NOT FOR CLINICAL USE", StringComparison.Ordinal)),
            "preview visibly identifies teaching-only diagnostic use");
    }

    private static void SvgPreviewNoDataKeepsCalibrationAndStateLabel()
    {
        EcgStripCheckpoint input = Input();
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState).Disconnect(false, 1), 0, 0);
        machine.Advance(10_000_000_000, 0);
        input = input with { Source = input.Source with { Frame = input.Source.Frame with { Presentation = machine.CaptureState() } } };
        XNamespace svg = "http://www.w3.org/2000/svg";
        var root = XElement.Parse(EcgStripSvgPreview.Render(input, 2, 2));
        Check.That(root.Elements(svg + "g").Single().Elements(svg + "path").All(path => path.Attribute("stroke-dasharray") is not null) &&
            root.Elements(svg + "path").Single().Attribute("id")!.Value == "calibration" &&
            root.Elements(svg + "text").Any(text => text.Value.Contains("NoData", StringComparison.Ordinal)),
            "NoData draws a distinguished baseline, retains calibration and labels the actual availability");
    }

    private static void SvgPreviewFormatsFractionsIndependentlyOfCulture()
    {
        EcgStripCheckpoint input = Input();
        SweepPathSample first = input.Source.Samples[0];
        first = first with
        {
            Voltage = new(1, 3),
            Point = first.Point with
            {
                Y = EcgVerticalGeometry.MapMicrovolts(input.Source.Frame.VerticalScale!, 1, 3),
            }
        };
        input = input with { Source = input.Source with { Samples = new[] { first, input.Source.Samples[1] } } };
        string expected = EcgStripSvgPreview.Render(input, 2, 2);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check.That(EcgStripSvgPreview.Render(input, 2, 2) == expected && expected.Contains("59.993333", StringComparison.Ordinal),
                "fractional geometry serializes at documented precision with invariant decimal syntax");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void SvgPreviewRestoreAndFailurePreserveSourceEvidence()
    {
        EcgStripCheckpoint input = Input();
        EcgStripCheckpoint restored = EcgStripReconstructor.Restore(2, 2, input).CaptureCheckpoint()!;
        string before = EcgStripSvgPreview.Render(input, 2, 2);
        Check.That(before == EcgStripSvgPreview.Render(restored, 2, 2) &&
            Reason(() => EcgStripSvgPreview.Render(input with { PulseLeftPixels = 29 }, 2, 2)) == "EcgCalibration.InsufficientSpace",
            "restored evidence emits identical SVG and invalid gutter fails before returning output");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { _ = EcgStripSvgPreview.Render(input, 2, 2, cancellation.Token); throw new InvalidOperationException("cancelled preview accepted"); }
        catch (OperationCanceledException exception) { Check.That(exception.CancellationToken == cancellation.Token, "preview cancellation retains identity"); }
        Check.That(before == EcgStripSvgPreview.Render(input, 2, 2), "failed previews do not change source geometry");
    }

    private static EcgStripDisplaySelection SelectColumns(ReconstructedEcgStrip strip, SweepStateProjectionState? state = null) =>
        EcgStripDisplayGate.Select(state ?? strip.Checkpoint.Source.Frame.Presentation,
            30, 500, strip.Checkpoint.Source.Frame.VerticalScale!, 0, 5, strip, requireColumnReduction: true);

    private static void StripDisplayRequiresRequestedColumnResults()
    {
        ReconstructedEcgStrip plain = new EcgStripReconstructor(2, 2).Replace(Input());
        ReconstructedEcgStrip reduced = new EcgStripReconstructor(2, 2, new(20, 20)).Replace(Input());
        Check.That(SelectColumns(plain) is { ReasonCode: "EcgStripDisplay.ColumnReductionRequired", Strip: null } &&
            Select(plain).Strip == plain && ReferenceEquals(SelectColumns(reduced).Strip, reduced),
            "column consumers reject otherwise matching unprocessed output while ordinary path consumers remain supported");
        EcgStripCheckpoint empty = Input();
        empty = empty with { Source = empty.Source with { Samples = Array.Empty<SweepPathSample>() } };
        ReconstructedEcgStrip reducedEmpty = new EcgStripReconstructor(2, 2, new(1, 1)).Replace(empty);
        Check.That(reducedEmpty.ColumnReduction!.Envelopes.Count == 0 && SelectColumns(reducedEmpty).Strip == reducedEmpty,
            "a completed empty reduction is valid evidence and is distinct from absent processing");
    }

    private static void StripDisplayReductionRequirementKeepsCurrentSafety()
    {
        EcgStripCheckpoint input = Input();
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState).Disconnect(false, 1), 0, 0);
        machine.Advance(10_000_000_000, 0);
        input = input with { Source = input.Source with { Frame = input.Source.Frame with { Presentation = machine.CaptureState() } } };
        PublishedEcgStrip plain = EcgStripPublication.Restore(2, 2, input).CapturePublished()!;
        EcgStripDisplaySnapshot display = EcgStripDisplayComposition.Compose(machine.CaptureState(), 1,
            Array.Empty<NumericNoDataPolicy>(), 30, 500, input.Source.Frame.VerticalScale!, 0, 5, plain, requireColumnReduction: true);
        Check.That(display.SourceStrip.ReasonCode == "EcgStripDisplay.ColumnReductionRequired" && display.SourceStrip.Strip is null &&
            display.CurrentCalibration.Points.Count == 4 && display.Connectivity.Message == ConnectivityBannerMessage.NoData &&
            display.LiveSafety.PatientAlarms == PatientAlarmSuspension.SuspendedUnknown && !display.LiveSafety.ClearLiveTraceImmediately,
            "missing column results do not hide current calibration or disconnect safety and do not authorize full clearing");
    }

    private static void StripDisplayRestoredReductionDoesNotBypassStateChecks()
    {
        var original = EcgStripPublication.Restore(2, 2, Input());
        PublishedEcgStrip before = original.CapturePublished()!;
        ReconstructedEcgStrip restored = EcgStripReconstructor.Restore(2, 2, before.Strip.Checkpoint, new(20, 20)).Current!;
        Check.That(SelectColumns(restored).Strip == restored && SelectColumns(before.Strip).Strip is null &&
            ReferenceEquals(before, original.CapturePublished()),
            "rebuilding from owned evidence enables column display without modifying old publication");
        var machine = SweepStateProjectionStateMachine.Restore(restored.Checkpoint.Source.Frame.Presentation);
        machine.Advance(1, 0);
        Check.That(SelectColumns(restored, machine.CaptureState()) is { ReasonCode: "FrameDisplay.PresentationMismatch", Strip: null },
            "presence of reduction cannot bypass exact current-phase compatibility");
        Check.That(Reason(() => EcgStripDisplayGate.Select(restored.Checkpoint.Source.Frame.Presentation,
            30, 500, restored.Checkpoint.Source.Frame.VerticalScale!, 0, 29, restored, true)) == "EcgCalibration.InsufficientSpace",
            "target layout validation remains mandatory for a column consumer");
    }

    private static void ValidatedStripReductionMatchesIndependentReconstruction()
    {
        EcgStripCheckpoint input = Input();
        ReconstructedEcgStrip strip = new EcgStripReconstructor(2, 2, new(20, 20)).Replace(input);
        ReducedSweepColumnFrame independent = SweepClippedColumnReduction.Reduce(input.Source, 2, 2, 20, 20);
        Check.That(strip.ColumnReduction!.Envelopes.SequenceEqual(independent.Envelopes) &&
            strip.ColumnReduction.Frame.Pieces.SequenceEqual(independent.Frame.Pieces) &&
            strip.PatientFrame.Segments.SequenceEqual(independent.Frame.SourceFrame.Segments) &&
            ReferenceEquals(strip.PatientFrame, strip.ColumnReduction.Frame.SourceFrame) &&
            ReferenceEquals(strip.Checkpoint.Source, strip.ColumnReduction.Frame.Checkpoint),
            "internal reuse agrees with independent fully validated reconstruction and shares its accepted source pair");
        EcgStripCheckpoint corrupt = input with
        {
            Source = input.Source with
            {
                Samples = new[] { input.Source.Samples[0], input.Source.Samples[1] with { Voltage = new(2000, 1) } },
            }
        };
        Check.That(Reason(() => EcgStripReconstructor.Restore(2, 2, corrupt, new(20, 20))) == "EcgStrip.InvalidCheckpoint",
            "external restore still revalidates voltage evidence rather than using the internal reuse path directly");
        Check.That(strip.ColumnReduction!.Envelopes.Count == 10 &&
            strip.ColumnReduction.Envelopes.All(envelope => envelope.MinimumY.Y == new ExactPlotCoordinate(40, 1)) &&
            strip.Calibration.Points[1].Y.PixelNumerator == 40 &&
            new EcgStripReconstructor(2, 2).Replace(input).ColumnReduction is null,
            "reduced patient geometry shares calibration while reduction remains an explicit capacity choice");
    }

    private static void ValidatedResizeReductionPreservesCapacityAndCheckpointRules()
    {
        EcgStripReconstructor reconstructor = new(2, 2, new(20, 20));
        reconstructor.Replace(Input());
        ReconstructedEcgStrip resized = reconstructor.ResizeHorizontal(30, 1000, 0, 5);
        ReducedSweepColumnFrame independent = SweepClippedColumnReduction.Reduce(resized.Checkpoint.Source, 2, 2, 20, 20);
        Check.That(resized.ColumnReduction!.Frame.Pieces.Count == 20 && resized.ColumnReduction.Envelopes.SequenceEqual(independent.Envelopes),
            "resized validated paths retain exact-capacity intersections and extrema");
        Check.That(Reason(() => reconstructor.ResizeHorizontal(30, 1100, 0, 5)) == "ColumnFrame.OutputLimitExceeded" &&
            ReferenceEquals(resized, reconstructor.Current) && ReferenceEquals(resized.Checkpoint, reconstructor.CaptureCheckpoint()),
            "late subdivision overflow still preserves both the completed strip and owned checkpoint");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { reconstructor.Replace(Input(), cancellation.Token); throw new InvalidOperationException("cancelled reuse accepted"); }
        catch (OperationCanceledException exception)
        { Check.That(exception.CancellationToken == cancellation.Token && ReferenceEquals(resized, reconstructor.Current), "cancellation leaves the complete result intact"); }
    }

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
        var original = EcgStripPublication.Restore(2, 2, Input(), new(20, 20));
        ReconstructedEcgStrip first = original.CapturePublished()!.Strip;
        var restored = EcgStripWorkPump.Restore(2, 2, first.Checkpoint, new(20, 20));
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
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
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
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
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
        var publication = EcgStripPublication.Restore(2, 2, Input());
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
        var pump = EcgStripWorkPump.Restore(2, 2, Input());
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
        var outcomes = new SweepFramePublicationStatus?[16];
        Parallel.For(0, outcomes.Length, index => outcomes[index] = pump.ProcessNext());
        Check.That(outcomes.Count(status => status == SweepFramePublicationStatus.Published) == 1 &&
            outcomes.Count(status => status is null) == 15 && pump.CapturePublished()!.LocalGeneration == generation,
            "concurrent consumers claim one pending request exactly once");
    }

    private static void StripPumpStopAndRestoreExcludePendingWork()
    {
        var pump = EcgStripWorkPump.Restore(2, 2, Input());
        PublishedEcgStrip before = pump.CapturePublished()!;
        pump.Enqueue(Input() with { PulseLeftPixels = 6 });
        Check.That(ReferenceEquals(before, pump.Stop()) && ReferenceEquals(before, pump.Stop()) && pump.ProcessNext() is null &&
            Reason(() => pump.Enqueue(Input())) == "StripPublication.Stopped",
            "stop drops pending work and freezes the complete published snapshot");
        var restored = EcgStripWorkPump.Restore(2, 2, before.Strip.Checkpoint);
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
        var statuses = new SweepFramePublicationStatus[16];
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
        var publication = EcgStripPublication.Restore(2, 2, Input());
        EcgStripWork work = publication.Request(Input());
        PublishedEcgStrip final = publication.Stop()!;
        Check.That(ReferenceEquals(final, publication.Stop()) && publication.Complete(work) == SweepFramePublicationStatus.Stopped &&
            Reason(() => publication.Request(Input())) == "StripPublication.Stopped", "stop is an irreversible idempotent publication fence");
        var restored = EcgStripPublication.Restore(2, 2, final.Strip.Checkpoint);
        Check.That(Reason(() => restored.Complete(work)) == "StripPublication.ForeignWork" &&
            restored.CapturePublished()!.Strip.PatientFrame.Segments.SequenceEqual(final.Strip.PatientFrame.Segments) &&
            restored.CapturePublished()!.Strip.Calibration.Points.SequenceEqual(final.Strip.Calibration.Points) &&
            restored.Complete(restored.Request(Input())) == SweepFramePublicationStatus.Published,
            "restoration revalidates both layers in a new active owner and rejects old lifecycle tickets");
    }

    private static void StripPublicationCancellationAndConcurrentStopAreAtomic()
    {
        var publication = EcgStripPublication.Restore(2, 2, Input());
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
        var machine = SweepStateProjectionStateMachine.Restore(strip.Checkpoint.Source.Frame.Presentation);
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
            var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
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

    private static EcgStripCheckpoint Input()
    {
        SweepStateProjectionState state = SweepStateProjectionStateMachine.Start(
            new("ecg", 4, 5, 0, 10_000_000_000, 200_000_000, 10_200_000_000), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0).CaptureState();
        SweepFramePathState frame = new(state, 30, 500, 0, 100, null, new(0, 100, 60, 20, 1));
        SweepSampleSource source = new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
            Guid.Parse("22222222-2222-4222-8222-222222222222"), Guid.Parse("33333333-3333-4333-8333-333333333333"),
            1, 2, 3, 4, 5, 500, 1);
        var builder = SweepFramePathBuilder.Restore(frame);
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
        var machine = SweepStateProjectionStateMachine.Restore(input.Source.Frame.Presentation);
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
