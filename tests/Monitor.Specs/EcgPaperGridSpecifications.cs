// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class EcgPaperGridSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PaperGridUsesExactOneToFiveSpacing), PaperGridUsesExactOneToFiveSpacing),
        new(nameof(PaperGridPreservesOriginAcrossClippedWindows), PaperGridPreservesOriginAcrossClippedWindows),
        new(nameof(PaperGridRejectsExcessBeforeGeneratingLines), PaperGridRejectsExcessBeforeGeneratingLines),
        new(nameof(PaperGridValidatesBoundsAndKeepsImmutableResults), PaperGridValidatesBoundsAndKeepsImmutableResults),
    ];

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
