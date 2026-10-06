// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;

namespace Monitor.Specs;

internal static class Ecg12PaperLayoutSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PaperRegionsMatchTheDrawnLeadGrid), PaperRegionsMatchTheDrawnLeadGrid),
    ];

    private static void PaperRegionsMatchTheDrawnLeadGrid()
    {
        var layout = new Ecg12PaperLayout(false);
        Check.That(layout.Region(0) == new Ecg12PaperRegion(0, 0, 62, 136, 0, 2_500_000_000) &&
            layout.Region(4) == new Ecg12PaperRegion(4, 4, 342, 256, 2_800_000_000, 5_300_000_000) &&
            layout.Region(Ecg12PaperLayout.LongLeadIndex) == new Ecg12PaperRegion(12, 1, 62, 536, 0, 10_900_000_000),
            "three-row regions follow column time offsets and the long lead II strip");
        var six = new Ecg12PaperLayout(true);
        Check.That(six.Region(7) == new Ecg12PaperRegion(7, 7, 592, 256, 5_300_000_000, 10_300_000_000) &&
            six.Region(12).Right == 1092 && layout.Region(12).Right == 1152, "six-row regions and long strips end at the grid edge");
        Check.That(layout.HitTest(50, 136) is null && layout.HitTest(100, 136)?.Lead == 0 && layout.HitTest(100, 536)?.Lead == 12 &&
            layout.HitTest(100, 450) is null && layout.HitTest(400, 380)?.Lead == 5, "calibration pulses and margins are outside every lead");
        bool rejected = false;
        try { layout.Region(13); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected, "unknown lead indices are rejected");
    }
}
