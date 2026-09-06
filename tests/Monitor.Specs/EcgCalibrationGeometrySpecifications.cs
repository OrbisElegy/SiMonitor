// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class EcgCalibrationGeometrySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(CalibrationUsesPatientTimeAndVoltageScales), CalibrationUsesPatientTimeAndVoltageScales),
        new(nameof(CalibrationRetainsFractionalAndExactBoundaryGeometry), CalibrationRetainsFractionalAndExactBoundaryGeometry),
        new(nameof(CalibrationSurvivesSweepNoDataAndPinnedViews), CalibrationSurvivesSweepNoDataAndPinnedViews),
        new(nameof(CalibrationRejectsInsufficientSpaceWithoutChangingInputs), CalibrationRejectsInsufficientSpaceWithoutChangingInputs),
    ];

    private static SweepStateProjectionStateMachine Start() => SweepStateProjectionStateMachine.Start(
        new("ecg", 4, 5, 0, 10_000_000_000, 200_000_000, 10_200_000_000), 1, 1, SessionRunState.Running,
        DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0);

    private static EcgVerticalScale Scale => new(0, 100, 60, 20, 1);

    private static EcgCalibrationGeometrySnapshot Compose(SweepStateProjectionState state, int width = 500) =>
        EcgCalibrationGeometry.Compose(state, 30, width, Scale, 0, 5);

    private static void CalibrationUsesPatientTimeAndVoltageScales()
    {
        SweepStateProjectionState state = Start().CaptureState();
        // Two logical pixels/mm: 25 mm/s * 10 s = 500 px, 10 mm/mV = 20 px/mV.
        EcgCalibrationGeometrySnapshot normal = Compose(state);
        EcgCalibrationGeometrySnapshot fast = Compose(state, 1000);
        Check.That(normal.Points.Count == 4 && normal.Points[0].X.WholePixels == 5 &&
            normal.Points[2].X.WholePixels == 15 && fast.Points[2].X.WholePixels == 25 &&
            normal.Points[0].Y.PixelNumerator == 60 && normal.Points[1].Y.PixelNumerator == 40,
            "200 ms spans 5 or 10 equivalent mm and one mV spans 10 equivalent mm");
        Check.That(normal.Points[0].Y == EcgVerticalGeometry.MapMicrovolts(Scale, 0, 1) &&
            normal.Points[1].Y == EcgVerticalGeometry.MapMicrovolts(Scale, 1000, 1) &&
            normal.GutterRightPixels == 30 && state.Plan.VisibleDurationNs == 10_000_000_000,
            "glyph shares the patient gain and consumes no patient time window");
    }

    private static void CalibrationRetainsFractionalAndExactBoundaryGeometry()
    {
        SweepStateProjectionState state = Start().CaptureState();
        EcgCalibrationGeometrySnapshot fractional = Compose(state, 501);
        Check.That(fractional.Points[2].X == new SweepPixelPosition(15, 200_000_000, 10_000_000_000),
            "fractional calibration width is never rounded to raster pixels");
        EcgCalibrationGeometrySnapshot edge = EcgCalibrationGeometry.Compose(state, 30, 1250,
            Scale with { ZeroBaselinePixels = 20 }, 0, 5);
        Check.That(edge.Points[2].X.WholePixels == 30 && edge.Points[1].Y.PixelNumerator == 0,
            "exact closed centerline boundaries fit without clipping or shrinking");
        EcgCalibrationGeometrySnapshot gain = EcgCalibrationGeometry.Compose(state, 30, 500,
            Scale with { PixelsPerMillivoltNumerator = 40 }, 0, 5);
        Check.That(gain.Points[1].Y.PixelNumerator == 20, "doubling patient gain doubles glyph height");
    }

    private static void CalibrationSurvivesSweepNoDataAndPinnedViews()
    {
        SweepStateProjectionStateMachine machine = Start();
        EcgCalibrationGeometrySnapshot first = Compose(machine.CaptureState());
        machine.Advance(11_000_000_000, 0);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState)
            .Disconnect(false, 1), 11_000_000_000, 0);
        machine.Advance(21_000_000_000, 0);
        Check.That(first.Points.SequenceEqual(Compose(machine.CaptureState()).Points),
            "wraparound and a full NoData sweep leave the independent scale glyph intact");
        machine.EnterFrozen(21_000_000_000, 0);
        Check.That(first.Points.SequenceEqual(Compose(machine.CaptureState()).Points), "Frozen retains calibration");
        machine.ExitFrozen(21_000_000_000, 0);
        machine.EnterReview("record.one", 0, 21_000_000_000, 0);
        SweepStateProjectionState checkpoint = machine.CaptureState();
        Check.That(first.Points.SequenceEqual(Compose(SweepStateProjectionStateMachine.Restore(checkpoint).CaptureState()).Points),
            "restored Review rebuilds calibration from scale rather than a stored bitmap");
    }

    private static void CalibrationRejectsInsufficientSpaceWithoutChangingInputs()
    {
        SweepStateProjectionStateMachine machine = Start();
        SweepStateProjectionState before = machine.CaptureState();
        EcgCalibrationGeometrySnapshot accepted = Compose(before);
        Check.That(Reason(() => Compose(before, 1251)) == "EcgCalibration.InsufficientSpace" &&
            Reason(() => EcgCalibrationGeometry.Compose(before, 30, 500, Scale with { ZeroBaselinePixels = 19 }, 0, 5)) ==
                "EcgCalibration.InsufficientSpace" &&
            Reason(() => EcgCalibrationGeometry.Compose(before, 30, 500, Scale, 5, 4)) == "EcgCalibration.InvalidGutter",
            "horizontal or vertical overflow and misplaced pulse fail explicitly");
        Check.That(machine.CaptureState() == before && accepted.Points.SequenceEqual(Compose(before).Points),
            "failed glyph construction changes neither patient checkpoint nor accepted geometry");
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (EcgCalibrationGeometryException exception) { return exception.ReasonCode; }
    }
}
