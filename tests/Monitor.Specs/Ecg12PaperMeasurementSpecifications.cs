// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class Ecg12PaperMeasurementSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PointsSnapToSamplesAndMeasureInTimeOrder), PointsSnapToSamplesAndMeasureInTimeOrder),
        new(nameof(PointsStayOnOneLeadAndClampToItsSamples), PointsStayOnOneLeadAndClampToItsSamples),
        new(nameof(PointsConvertPlaneScaleExactly), PointsConvertPlaneScaleExactly),
        new(nameof(DraggingPreservesEndpointAndClamps), DraggingPreservesEndpointAndClamps),
        new(nameof(PolicyGatesAndWithdrawsPoints), PolicyGatesAndWithdrawsPoints),
    ];

    private static Guid Channel(int lead) => new(lead + 1, 0, 0, new byte[8]);

    // Three 4 s blocks of 250 Hz planes, one per lead; lead L sample i is (L + 1) * i microvolts.
    private static WaveformEnvelope[] Record(int scaleNumerator = 1, uint scaleDenominator = 1, int offsetNumerator = 0, uint offsetDenominator = 1) =>
        Enumerable.Range(0, 3).Select(block => new WaveformEnvelope(Guid.Empty, Guid.Empty, 1, 1, (ulong)block, 1,
            block * 4_000_000_000L, 4_000_000_000, Enumerable.Range(0, 12).Select(lead => new WaveformPlane(Channel(lead), 250, 1,
                (ulong)block * 1000, scaleNumerator, scaleDenominator, offsetNumerator, offsetDenominator, WaveformQualityEncoding.None,
                Enumerable.Range(block * 1000, 1000).Select(i => (short)((lead + 1) * i % 30000)).ToArray(), [])).ToArray())).ToArray();

    private static Ecg12PaperMeasurement Calipers(WaveformEnvelope[] record, bool sixRows = false,
        SystemViewCommandAssessmentPolicy policy = SystemViewCommandAssessmentPolicy.Enabled) =>
        new(new Ecg12PaperLayout(sixRows), record, Channel, policy);

    private static void PointsSnapToSamplesAndMeasureInTimeOrder()
    {
        var calipers = Calipers(Record());
        Check.That(calipers.Display.ReasonCode == "Ecg12Measurement.Idle" && calipers.Display.Result is null, "no points before a click");
        // Lead II, column 0, row 1: 100 ms is x = 72; 101.5 ms snaps to the 100 ms sample.
        Check.That(calipers.Hover(72.15, 256) && calipers.Display is { ReasonCode: "Ecg12Measurement.Idle", Hover.TimeNs: 100_000_000, Region: null },
            "the hover point follows the trace before any point is placed");
        Check.That(calipers.Place(72.15, 256) && calipers.Display is { ReasonCode: "Ecg12Measurement.Placing", Region.Lead: 1, Start.TimeNs: 100_000_000, End: null },
            "the first click places the first point");
        Check.That(calipers.Hover(92.1, 999) && calipers.Display is { Hover.TimeNs: 300_000_000, HoverResult.ElapsedMilliseconds: var preview } &&
            preview == new EcgMeasurementRatio(200, 1), "while placing, the hover point stays on the first lead and previews the result");
        Check.That(calipers.Place(92.1, 0), "the second click completes the measurement");
        var display = calipers.Display;
        Check.That(display is { ReasonCode: "Ecg12Measurement.Ready", Region.Lead: 1, Start.TimeNs: 100_000_000, End.TimeNs: 300_000_000 },
            "both points snap to acquired samples");
        Check.That(display.Result is { } result && result.ElapsedMilliseconds == new EcgMeasurementRatio(200, 1) &&
            result.AmplitudeChangeMillivolts == new EcgMeasurementRatio(1, 10) && result.AuxiliaryRatePerMinute == new EcgMeasurementRatio(300, 1),
            "horizontal distance, signed vertical distance and auxiliary rate are exact");
        Check.That(calipers.Place(72.15, 256) && calipers.Display.ReasonCode == "Ecg12Measurement.Placing" && calipers.Place(62, 256) &&
            calipers.Display is { Start.TimeNs: 0, End.TimeNs: 100_000_000 } && calipers.Display.Result!.AmplitudeChangeMillivolts == new EcgMeasurementRatio(1, 20),
            "a further click starts a new measurement, and an earlier second point still measures later minus earlier");
        Check.That(calipers.Nudge(3) && calipers.Display is { Start.TimeNs: 12_000_000, End.TimeNs: 100_000_000 },
            "nudging moves only the latest point by whole samples");
    }

    private static void PointsStayOnOneLeadAndClampToItsSamples()
    {
        var calipers = Calipers(Record());
        Check.That(calipers.Place(352, 256) && calipers.Display.Region!.Lead == 4, "column 1 row 1 is aVL");
        Check.That(calipers.Place(5000, 0) && calipers.Display.End!.TimeNs == 5_296_000_000, "a second point past the column clamps to its last sample");
        Check.That(calipers.Nudge(1000) && calipers.Display.End!.TimeNs == 5_296_000_000 && calipers.Nudge(-100_000) &&
            calipers.Display.Start!.TimeNs == 2_800_000_000, "nudges clamp to the lead's sample range");
        Check.That(calipers.Place(1100, 536) && calipers.Place(1200, 536) && calipers.Display is { Region.Lead: 12, Region.SourceLead: 1, Result: not null },
            "the rhythm strip measures lead II samples");
        Check.That(!calipers.Place(20, 20) && calipers.Display is { Region.Lead: 12, Result: not null }, "a click outside every lead keeps the previous measurement");
        calipers.EndHover();
        Check.That(calipers.Display.Hover is null, "leaving the paper removes the hover point");
        var missing = new Ecg12PaperMeasurement(new Ecg12PaperLayout(false), Record(), lead => Guid.Empty, SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(!missing.Hover(72, 256) && !missing.Place(72, 256) && missing.Display.Region is null, "leads without samples cannot be measured");
    }

    private static void PointsConvertPlaneScaleExactly()
    {
        var calipers = Calipers(Record(3, 2, 1, 4));
        Check.That(calipers.Place(72, 136), "lead I is placed");
        // 1.5 * 25 + 0.25 microvolts = (12 * 25 + 2) / 8.
        Check.That(calipers.Display.Start is { NumeratorMicrovolts: 302, Denominator: 8 }, "physical microvolts keep the plane's exact scale and offset");
    }

    private static void DraggingPreservesEndpointAndClamps()
    {
        foreach (bool sixRows in new[] { false, true })
        {
            var calipers = Calipers(Record(), sixRows);
            var lead = calipers.Layout.Region(1);
            calipers.Place(lead.XAt(100_000_000), lead.Baseline);
            calipers.Place(lead.XAt(300_000_000), lead.Baseline);
            var first = calipers.Display.Start!;
            Check.That(!calipers.BeginDrag(double.NaN, first.Y, 12) && !calipers.BeginDrag(first.X, double.PositiveInfinity, 12) &&
                !calipers.BeginDrag(first.X, first.Y + 20, 12), "invalid coordinates and misses cannot grab a point");
            Check.That(calipers.BeginDrag(first.X, first.Y, 12) && calipers.DragTo(lead.XAt(500_000_000)) &&
                calipers.Display is { Start.TimeNs: 300_000_000, End.TimeNs: 500_000_000 }, "dragging the first point across the second preserves the grabbed endpoint");
            Check.That(calipers.DragTo(lead.XAt(200_000_000)) && calipers.Display is { Start.TimeNs: 200_000_000, End.TimeNs: 300_000_000 },
                "crossing back moves the same endpoint");
            calipers.EndDrag();
            Check.That(!calipers.DragTo(lead.Left) && calipers.Nudge(1) && calipers.Display.Start!.TimeNs == 204_000_000,
                "release ends the gesture and keyboard adjustment moves the selected point");
            first = calipers.Display.Start!;
            calipers.BeginDrag(first.X, first.Y, 12);
            Check.That(calipers.DragTo(lead.Left - 1000) && calipers.Display.Start!.TimeNs == lead.StartNs, "drag clamps to the original lead");
            calipers.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
            Check.That(!calipers.DragTo(lead.Right) && calipers.Display.Region is null, "policy withdrawal cancels a captured drag");
        }
    }

    private static void PolicyGatesAndWithdrawsPoints()
    {
        var calipers = Calipers(Record());
        Check.That(calipers.Place(72, 256) && calipers.Hover(80, 256), "enabled policy measures");
        calipers.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(calipers.Display is { ReasonCode: "Ecg12Measurement.CourseLocked", Region: null, Hover: null } && !calipers.CanMeasure &&
            !calipers.Place(72, 256) && !calipers.Hover(80, 256) && !calipers.Nudge(1), "a course lock withdraws the points and refuses new ones");
        calipers.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(calipers.Display.ReasonCode == "Ecg12Measurement.Disabled", "disabled measurement reports its own reason");
        calipers.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(calipers.Place(72, 256), "re-enabling allows new points");
        bool rejected = false;
        try { calipers.UpdatePolicy((SystemViewCommandAssessmentPolicy)9); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected && calipers.CanMeasure, "undefined policies are rejected without changing state");
    }
}
