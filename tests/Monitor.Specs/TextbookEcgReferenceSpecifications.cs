// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class TextbookEcgReferenceSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static EventWaveformComposition Composition()
    {
        var timing = TextbookEcgReference.Timing;
        var plan = new RegularPhysiologyPlan(0, timing.RrIntervalNs, timing.PrIntervalNs,
            80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
        return EventWaveformComposition.Restore(new(TextbookEcgReference.CreateBands(),
            RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_600_000_000, 100)));
    }
    public static Specification[] All =>
    [
        new(nameof(TextbookTimingUsesOnsetsAndEndpoints), TextbookTimingUsesOnsetsAndEndpoints),
        new(nameof(TextbookBandsPreserveNormalReferenceRelationships), TextbookBandsPreserveNormalReferenceRelationships),
        new(nameof(TextbookTWaveHasUnequalLimbs), TextbookTWaveHasUnequalLimbs),
        new(nameof(EcgTimingRejectsOverlapsAndReferenceRestores), EcgTimingRejectsOverlapsAndReferenceRestores),
    ];

    private static void TextbookTimingUsesOnsetsAndEndpoints()
    {
        var timing = TextbookEcgReference.Timing;
        timing.Validate();
        var bands = TextbookEcgReference.CreateBands();
        Check.That(timing.RrIntervalNs == 800_000_000 && timing.PDurationNs == 100_000_000 && timing.PrIntervalNs == 160_000_000 &&
            timing.QrsDurationNs == 80_000_000 && timing.QtIntervalNs == 360_000_000 && timing.StDurationNs == 100_000_000 &&
            bands[2].DelayNs + bands[2].DurationNs == timing.QtIntervalNs, "reference intervals must derive from actual band boundaries");
        var source = Composition();
        Check.That(source.EvaluateAt(120_000_000) == 0 && source.EvaluateAt(240_000_000) == 0 &&
            source.EvaluateAt(300_000_000) == 0 && source.EvaluateAt(340_000_000) == 0 &&
            source.EvaluateAt(519_999_999) > 0 && source.EvaluateAt(520_000_000) == 0,
            "PR segment, J/ST baseline and T end must agree with declared PR/QRS/QT");
    }

    private static void TextbookBandsPreserveNormalReferenceRelationships()
    {
        var source = Composition();
        Check.That(source.EvaluateAt(50_000_000) == 150 * Q && source.EvaluateAt(170_000_000) == -120 * Q &&
            source.EvaluateAt(180_000_000) == 0 && source.EvaluateAt(195_000_000) == 1000 * Q &&
            source.EvaluateAt(215_000_000) == -200 * Q, "rounded P and separate short q, R and S landmarks retain microvolt units");
        var bands = TextbookEcgReference.CreateBands();
        Check.That(bands.All(band => band.TableQ32.Count == 128) &&
            bands[0].TableQ32.Max() < 250 * Q && -bands[1].TableQ32[16] * 4 <= bands[1].TableQ32.Max() &&
            bands[2].TableQ32.Max() * 10 >= bands[1].TableQ32.Max(),
            "the selected positive-dominant reference satisfies cited P, q/R and T/R constraints");
    }

    private static void TextbookTWaveHasUnequalLimbs()
    {
        var t = TextbookEcgReference.CreateBands()[2];
        long peakOffset = t.DurationNs * 80 / 128;
        long risingHalf = peakOffset / 2;
        long fallingHalf = (t.DurationNs - peakOffset) / 2;
        long onset = TextbookEcgReference.Timing.PrIntervalNs + t.DelayNs;
        var source = Composition();
        Check.That(risingHalf > fallingHalf && source.EvaluateAt(onset + peakOffset) == 300 * Q &&
            source.EvaluateAt(onset + risingHalf) == 150 * Q &&
            source.EvaluateAt(onset + peakOffset + fallingHalf) == 150 * Q,
            "equal amplitude changes take longer on the rising T limb than the falling limb");
    }

    private static void EcgTimingRejectsOverlapsAndReferenceRestores()
    {
        var timing = TextbookEcgReference.Timing;
        EcgCycleTiming[] invalid = [timing with { PDurationNs = 170_000_000 }, timing with { QtIntervalNs = 200_000_000 },
            timing with { QtIntervalNs = 700_000_000 }, timing with { TDurationNs = -1 }, timing with { RrIntervalNs = long.MinValue }];
        foreach (var item in invalid)
        {
            bool rejected = false;
            try { item.Validate(); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EcgTiming.InconsistentIntervals"; }
            Check.That(rejected, "inconsistent interval plans must fail before waveform construction");
        }
        var source = Composition();
        var restored = EventWaveformComposition.Restore(source.CaptureState());
        for (long time = 0; time < 800_000_000; time += 4_000_000)
        {
            Check.That(source.EvaluateAt(time) == restored.EvaluateAt(time) && source.EvaluateAt(time) == source.EvaluateAt(time + 800_000_000),
                "reference repeats at shared cardiac rate and survives owned recovery at native ECG sample times");
        }
    }
}
