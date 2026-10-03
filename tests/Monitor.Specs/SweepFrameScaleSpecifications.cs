// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepFrameScaleSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(RestoredScaleRejectsOtherwiseMatchingFrames), RestoredScaleRejectsOtherwiseMatchingFrames),
        new(nameof(EquivalentRationalGainsRemainCompatible), EquivalentRationalGainsRemainCompatible),
        new(nameof(InvalidScaleAdmissionPreservesCompletedFrame), InvalidScaleAdmissionPreservesCompletedFrame),
    ];

    private static EcgVerticalScale Scale => new(0, 100, 60, 20, 1);

    private static SweepFrameReconstructionInput Input(EcgVerticalScale? scale)
    {
        SweepStateProjectionState state = SweepStateProjectionStateMachine.Start(
            new("ecg", 4, 5, 0, 10_000_000_000, 200_000_000, 10_200_000_000), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0).CaptureState();
        return new(new(state, 30, 500, 0, 100, null, scale), Array.Empty<SweepPathSample>());
    }

    private static SweepFrameDisplaySelection Select(PublishedSweepFrame frame, EcgVerticalScale? scale) =>
        SweepFrameDisplayGate.Select(frame.Checkpoint.Frame.Presentation, 30, 500, 0, 100, frame, scale);

    private static void RestoredScaleRejectsOtherwiseMatchingFrames()
    {
        PublishedSweepFrame original = SweepFramePublication.Restore(1, 1, Input(Scale)).CapturePublished()!;
        PublishedSweepFrame frame = SweepFramePublication.Restore(1, 1, original.Checkpoint).CapturePublished()!;
        Check.That(frame.Checkpoint.Frame.VerticalScale == Scale && Select(frame, Scale).ReasonCode == "FrameDisplay.Matched",
            "restoration retains and revalidates the declared voltage mapping");
        EcgCalibrationGeometrySnapshot glyph = EcgCalibrationGeometry.Compose(frame.Checkpoint.Frame.Presentation,
            30, 500, frame.Checkpoint.Frame.VerticalScale!, 0, 5);
        Check.That(glyph.Points[1].Y == EcgVerticalGeometry.MapMicrovolts(Scale, 1000, 1),
            "the restored declaration supplies the exact same calibration gain");
        Check.That(Select(frame, Scale).Frame == frame.Frame &&
            Select(frame, Scale with { PixelsPerMillivoltNumerator = 40 }) is { ReasonCode: "FrameDisplay.ScaleMismatch", Frame: null } &&
            Select(frame, Scale with { ZeroBaselinePixels = 61 }).Frame is null,
            "gain and zero baseline are part of display compatibility even at unchanged phase and viewport");
        Check.That(Select(frame, null).ReasonCode == "FrameDisplay.ScaleMismatch" &&
            Select(SweepFramePublication.Restore(1, 1, Input(null)).CapturePublished()!, Scale).ReasonCode == "FrameDisplay.ScaleMismatch",
            "missing declarations in either direction cannot silently grant scale agreement");
        SweepDisplaySnapshot display = SweepDisplayComposition.Compose(frame.Checkpoint.Frame.Presentation,
            0, Array.Empty<NumericNoDataPolicy>(), 30, 500, 0, 100, frame, Scale with { PixelsPerMillivoltNumerator = 40 });
        Check.That(display.SourceFrame.Frame is null && display.LiveSafety.PreserveCalibrationGutter,
            "composition keeps current safety while refusing old-gain source pixels");
    }

    private static void EquivalentRationalGainsRemainCompatible()
    {
        PublishedSweepFrame frame = SweepFramePublication.Restore(1, 1, Input(Scale)).CapturePublished()!;
        Check.That(Select(frame, Scale with { PixelsPerMillivoltNumerator = 40, PixelsPerMillivoltDenominator = 2 }).Frame == frame.Frame,
            "equivalent fractions represent one voltage mapping");
        EcgVerticalScale large = Scale with { PixelsPerMillivoltNumerator = uint.MaxValue, PixelsPerMillivoltDenominator = uint.MaxValue };
        frame = SweepFramePublication.Restore(1, 1, Input(large)).CapturePublished()!;
        Check.That(Select(frame, large).Frame == frame.Frame &&
            Select(frame, large with { PixelsPerMillivoltNumerator = 1, PixelsPerMillivoltDenominator = 1 }).Frame == frame.Frame &&
            Select(frame, large with { PixelsPerMillivoltNumerator = uint.MaxValue - 1 }).Frame is null,
            "cross-products remain exact at uint bounds without rounded-gain collisions");
    }

    private static void InvalidScaleAdmissionPreservesCompletedFrame()
    {
        var publication = SweepFramePublication.Restore(1, 1, Input(Scale));
        PublishedSweepFrame before = publication.CapturePublished()!;
        foreach (EcgVerticalScale invalid in new[]
        {
            Scale with { PixelsPerMillivoltDenominator = 0 },
            Scale with { PlotHeightPixels = 101 },
            Scale with { ZeroBaselinePixels = 101 },
        })
        {
            try
            {
                _ = publication.Request(Input(invalid));
                throw new InvalidOperationException("invalid scale accepted");
            }
            catch (SweepFramePathException exception)
            {
                Check.That(exception.ReasonCode == "SweepFrame.InvalidCheckpoint", "invalid mapping has stable rejection");
            }
        }
        Check.That(ReferenceEquals(before, publication.CapturePublished()) &&
            publication.Request(Input(Scale)).Generation == before.LocalGeneration + 1,
            "invalid scale cannot replace completed geometry or consume a generation");
    }
}
