// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class EcgPaperGridSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PaperGridCancellationPreservesAcceptedGeometry), PaperGridCancellationPreservesAcceptedGeometry),
        new(nameof(PaperGridCalibrationMatchesFrozenReferenceScale), PaperGridCalibrationMatchesFrozenReferenceScale),
        new(nameof(PaperGridCalibrationPreservesFractionalZoom), PaperGridCalibrationPreservesFractionalZoom),
        new(nameof(PaperGridCalibrationRejectsInconsistentAndInvalidScale), PaperGridCalibrationRejectsInconsistentAndInvalidScale),
        new(nameof(PaperGridCalibrationRejectsUnrepresentableExactSpacing), PaperGridCalibrationRejectsUnrepresentableExactSpacing),
        new(nameof(PaperGridUsesExactOneToFiveSpacing), PaperGridUsesExactOneToFiveSpacing),
        new(nameof(PaperGridPreservesOriginAcrossClippedWindows), PaperGridPreservesOriginAcrossClippedWindows),
        new(nameof(PaperGridRejectsExcessBeforeGeneratingLines), PaperGridRejectsExcessBeforeGeneratingLines),
        new(nameof(PaperGridValidatesBoundsAndKeepsImmutableResults), PaperGridValidatesBoundsAndKeepsImmutableResults),
    ];

    private static void PaperGridCancellationPreservesAcceptedGeometry()
    {
        EcgPaperGridPlan plan = new(0, 0, 10, 10, 0, 0, 2, 1);
        IReadOnlyList<EcgPaperGridLine> accepted = EcgPaperGridGeometry.Build(plan, 10);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            EcgPaperGridGeometry.Build(plan, 10, cancellation.Token);
            throw new InvalidOperationException("cancelled grid build accepted");
        }
        catch (OperationCanceledException exception)
        { Check.That(exception.CancellationToken == cancellation.Token, "grid cancellation preserves caller token identity"); }
        Check.That(accepted.Count == 10 && accepted.SequenceEqual(EcgPaperGridGeometry.Build(plan, 10)),
            "cancellation never changes a prior result and fresh generation remains deterministic");
    }

    private static void PaperGridCalibrationMatchesFrozenReferenceScale()
    {
        EcgPaperGridPlan plan = EcgPaperGridCalibration.Resolve(10, 100, 2_000_000_000,
            new(0, 100, 60, 20, 1), new(25, 1, 10, 1), 10, 60);
        Check.That(plan.MinorSpacingNumerator == 2 && plan.MinorSpacingDenominator == 1 && plan.OriginYPixels == 60,
            "25 mm/s and 10 mm/mV resolve the same two logical pixels per equivalent millimeter");
        IReadOnlyList<EcgPaperGridLine> lines = EcgPaperGridGeometry.Build(plan, 100);
        Check.That(lines[25].Position == new ExactPlotCoordinate(60, 1) && lines[25].IsMajor &&
            lines.Count(line => !line.IsVertical && line.Position.Numerator >= 40 && line.Position.Numerator < 60) == 10,
            "one second spans 25 minor intervals and one millivolt spans 10 at the frozen reference scale");
        EcgPaperGridPlan faster = EcgPaperGridCalibration.Resolve(10, 200, 2_000_000_000,
            new(0, 100, 60, 20, 1), new(50, 1, 10, 1), 10, 60);
        Check.That(faster.MinorSpacingNumerator == 2 && faster.MinorSpacingDenominator == 1,
            "doubling paper speed and time pixel density preserves square equivalent millimeters");
    }

    private static void PaperGridCalibrationPreservesFractionalZoom()
    {
        EcgVerticalScale vertical = new(0, 100, 60, 5, 1);
        EcgPaperGridPlan plan = EcgPaperGridCalibration.Resolve(0, 25, 2_000_000_000, vertical, new(25, 1, 10, 1), 0, 60);
        EcgPaperGridPlan equivalent = EcgPaperGridCalibration.Resolve(0, 25, 2_000_000_000,
            vertical with { PixelsPerMillivoltNumerator = 10, PixelsPerMillivoltDenominator = 2 }, new(50, 2, 20, 2), 0, 60);
        Check.That(plan.MinorSpacingNumerator == 1 && plan.MinorSpacingDenominator == 2 && plan == equivalent,
            "zoom and equivalent rational scales resolve canonical spacing without rounding");
    }

    private static void PaperGridCalibrationRejectsInconsistentAndInvalidScale()
    {
        EcgVerticalScale vertical = new(0, 100, 60, 20, 1);
        ExpectReason(() => EcgPaperGridCalibration.Resolve(0, 100, 2_000_000_000, vertical, new(50, 1, 10, 1), 0, 60),
            "PaperGrid.InconsistentAxisScale");
        ExpectReason(() => EcgPaperGridCalibration.Resolve(0, 100, 2_000_000_000, vertical, new(25, 0, 10, 1), 0, 60),
            "PaperGrid.InvalidPaperScale");
        ExpectReason(() => EcgPaperGridCalibration.Resolve(0, 100, 2_000_000_000, vertical, new(25, 1, 0, 1), 0, 60),
            "PaperGrid.InvalidPaperScale");
        Check.That(EcgPaperGridCalibration.Resolve(0, 100, 2_000_000_000, vertical, new(25, 1, 10, 1), 0, 60).MinorSpacingNumerator == 2,
            "failed resolution leaves explicit scale reusable for corrected inputs");
    }

    private static void PaperGridCalibrationRejectsUnrepresentableExactSpacing()
    {
        ExpectReason(() => EcgPaperGridCalibration.Resolve(0, 1, uint.MaxValue,
            new(0, 100, 60, 1, uint.MaxValue), new(uint.MaxValue, 1, uint.MaxValue, 1_000_000_000), 0, 60),
            "PaperGrid.UnrepresentableSpacing");
    }

    private static void PaperGridUsesExactOneToFiveSpacing()
    {
        IReadOnlyList<EcgPaperGridLine> lines = EcgPaperGridGeometry.Build(new(0, 0, 11, 11, 0, 0, 2, 1), 12);
        Check.That(lines.Count == 12 && lines[0] == new EcgPaperGridLine(true, new(0, 1), true) &&
            lines[1] == new EcgPaperGridLine(true, new(2, 1), false) && lines[5] == new EcgPaperGridLine(true, new(10, 1), true) &&
            lines[11] == new EcgPaperGridLine(false, new(10, 1), true), "one major interval contains exactly five minor intervals on both axes");
        IReadOnlyList<EcgPaperGridLine> fractional = EcgPaperGridGeometry.Build(new(0, 0, 3, 3, 0, 0, 1, 2), 12);
        Check.That(fractional[1].Position == new ExactPlotCoordinate(1, 2) && fractional[5].Position == new ExactPlotCoordinate(5, 2) &&
            fractional[5].IsMajor, "fractional zoom retains exact positions and major cadence");
    }

    private static void PaperGridPreservesOriginAcrossClippedWindows()
    {
        IReadOnlyList<EcgPaperGridLine> lines = EcgPaperGridGeometry.Build(new(1, 1, 5, 5, 10, 10, 2, 1), 4);
        Check.That(lines.Count == 4 && lines[0].Position == new ExactPlotCoordinate(2, 1) && lines[1].Position == new ExactPlotCoordinate(4, 1) &&
            !lines[0].IsMajor && !lines[1].IsMajor, "negative grid indices use ceiling division and exclude right/bottom boundaries");
        IReadOnlyList<EcgPaperGridLine> shifted = EcgPaperGridGeometry.Build(new(0, 0, 2, 2, 10, 10, 2, 1), 2);
        Check.That(shifted[0].IsMajor && shifted[1].IsMajor, "negative multiples of five retain major classification without resetting phase");
    }

    private static void PaperGridRejectsExcessBeforeGeneratingLines()
    {
        EcgPaperGridPlan dense = new(0, 0, int.MaxValue, int.MaxValue, int.MinValue, int.MaxValue, 1, uint.MaxValue);
        ExpectReason(() => EcgPaperGridGeometry.Build(dense, 100), "PaperGrid.LineLimitExceeded");
        EcgPaperGridPlan normal = new(0, 0, 10, 10, 0, 0, 2, 1);
        ExpectReason(() => EcgPaperGridGeometry.Build(normal, 9), "PaperGrid.LineLimitExceeded");
        Check.That(EcgPaperGridGeometry.Build(normal, 10).Count == 10, "exact limit succeeds after rejected generation without partial state");
    }

    private static void PaperGridValidatesBoundsAndKeepsImmutableResults()
    {
        EcgPaperGridPlan plan = new(0, 0, 10, 10, 0, 0, 2, 1);
        ExpectReason(() => EcgPaperGridGeometry.Build(plan, 0), "PaperGrid.InvalidLimit");
        ExpectReason(() => EcgPaperGridGeometry.Build(plan with { MinorSpacingDenominator = 0 }, 10), "PaperGrid.InvalidPlan");
        ExpectReason(() => EcgPaperGridGeometry.Build(plan with { LeftPixels = int.MaxValue }, 10), "PaperGrid.InvalidPlan");
        IReadOnlyList<EcgPaperGridLine> accepted = EcgPaperGridGeometry.Build(plan, 10);
        Check.That(((ICollection<EcgPaperGridLine>)accepted).IsReadOnly, "caller cannot mutate published grid collection");
        IReadOnlyList<EcgPaperGridLine> rebuilt = EcgPaperGridGeometry.Build(plan with { MinorSpacingNumerator = 4, MinorSpacingDenominator = 2 }, 10);
        Check.That(accepted.SequenceEqual(rebuilt), "equivalent spacing representations rebuild identical canonical geometry");
    }

    private static void ExpectReason(Action action, string expected)
    {
        try { action(); }
        catch (EcgPaperGridException exception)
        {
            Check.That(exception.ReasonCode == expected, "paper grid rejects with stable reason");
            return;
        }
        throw new InvalidOperationException("invalid grid accepted");
    }
}
