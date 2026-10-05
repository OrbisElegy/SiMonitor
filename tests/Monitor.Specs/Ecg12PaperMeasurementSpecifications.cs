// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class Ecg12PaperMeasurementSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(CalipersSnapToSamplesAndMeasureInTimeOrder), CalipersSnapToSamplesAndMeasureInTimeOrder),
        new(nameof(CalipersStayOnOneLeadAndClampToItsSamples), CalipersStayOnOneLeadAndClampToItsSamples),
        new(nameof(CalipersConvertPlaneScaleExactly), CalipersConvertPlaneScaleExactly),
        new(nameof(PolicyGatesAndWithdrawsCalipers), PolicyGatesAndWithdrawsCalipers),
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

    private static void CalipersSnapToSamplesAndMeasureInTimeOrder()
    {
        var calipers = Calipers(Record());
        Check.That(calipers.Display.ReasonCode == "Ecg12Measurement.Idle" && calipers.Display.Result is null, "no calipers before a drag");
        // Lead II, column 0, row 1: 100 ms is x = 72; 101.5 ms snaps to the 100 ms sample.
        Check.That(calipers.Begin(72.15, 256) && calipers.Extend(92.1), "drag starts and extends on lead II");
        var display = calipers.Display;
        Check.That(display is { ReasonCode: "Ecg12Measurement.Ready", Region.Lead: 1, Start.TimeNs: 100_000_000, End.TimeNs: 300_000_000 },
            "both ends snap to acquired samples");
        Check.That(display.Result is { } result && result.ElapsedMilliseconds == new EcgMeasurementRatio(200, 1) &&
            result.AmplitudeChangeMillivolts == new EcgMeasurementRatio(1, 10) && result.AuxiliaryRatePerMinute == new EcgMeasurementRatio(300, 1),
            "elapsed time, signed amplitude and auxiliary rate are exact");
        Check.That(calipers.Extend(62) && calipers.Display is { Start.TimeNs: 0, End.TimeNs: 100_000_000 } &&
            calipers.Display.Result!.AmplitudeChangeMillivolts == new EcgMeasurementRatio(1, 20),
            "dragging left of the anchor still measures later minus earlier");
        Check.That(calipers.Nudge(3) && calipers.Display is { Start.TimeNs: 12_000_000, End.TimeNs: 100_000_000 },
            "nudging moves only the moving end by whole samples");
    }

    private static void CalipersStayOnOneLeadAndClampToItsSamples()
    {
        var calipers = Calipers(Record());
        Check.That(calipers.Begin(352, 256) && calipers.Display.Region!.Lead == 4, "column 1 row 1 is aVL");
        Check.That(calipers.Extend(5000) && calipers.Display.End!.TimeNs == 5_296_000_000, "dragging past the column clamps to its last sample");
        Check.That(calipers.Nudge(1000) && calipers.Display.End!.TimeNs == 5_296_000_000 && calipers.Nudge(-100_000) &&
            calipers.Display.Start!.TimeNs == 2_800_000_000, "nudges clamp to the lead's sample range");
        Check.That(calipers.Begin(1100, 536) && calipers.Display is { Region.Lead: 12, Region.SourceLead: 1 }, "the rhythm strip measures lead II samples");
        Check.That(!calipers.Begin(20, 20) && calipers.Display.Region!.Lead == 12, "a press outside every lead keeps the previous calipers");
        var missing = new Ecg12PaperMeasurement(new Ecg12PaperLayout(false), Record(), lead => Guid.Empty, SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(!missing.Begin(72, 256) && missing.Display.Region is null, "leads without samples cannot be measured");
    }

    private static void CalipersConvertPlaneScaleExactly()
    {
        var calipers = Calipers(Record(3, 2, 1, 4));
        Check.That(calipers.Begin(72, 136), "lead I starts");
        // 1.5 * 25 + 0.25 microvolts = (12 * 25 + 2) / 8.
        Check.That(calipers.Display.Start is { NumeratorMicrovolts: 302, Denominator: 8 }, "physical microvolts keep the plane's exact scale and offset");
    }

    private static void PolicyGatesAndWithdrawsCalipers()
    {
        var calipers = Calipers(Record());
        Check.That(calipers.Begin(72, 256), "enabled policy measures");
        calipers.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(calipers.Display is { ReasonCode: "Ecg12Measurement.CourseLocked", Region: null } && !calipers.CanMeasure &&
            !calipers.Begin(72, 256) && !calipers.Extend(80) && !calipers.Nudge(1), "a course lock withdraws calipers and refuses new ones");
        calipers.UpdatePolicy(SystemViewCommandAssessmentPolicy.Disabled);
        Check.That(calipers.Display.ReasonCode == "Ecg12Measurement.Disabled", "disabled measurement reports its own reason");
        calipers.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(calipers.Begin(72, 256), "re-enabling allows a new drag");
        bool rejected = false;
        try { calipers.UpdatePolicy((SystemViewCommandAssessmentPolicy)9); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected && calipers.CanMeasure, "undefined policies are rejected without changing state");
    }
}
