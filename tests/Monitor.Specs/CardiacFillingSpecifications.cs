// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CardiacFillingSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FillingSeparatesRateFromEffectiveAtrialContraction), FillingSeparatesRateFromEffectiveAtrialContraction),
        new(nameof(FillingTracksIndependentAtrialPhase), FillingTracksIndependentAtrialPhase),
        new(nameof(FillingUsesActualCaptureAndResumedMechanicalIntervals), FillingUsesActualCaptureAndResumedMechanicalIntervals),
        new(nameof(FillingRemainsIndexedAtShiftedAndLateEpochs), FillingRemainsIndexedAtShiftedAndLateEpochs),
        new(nameof(FillingIsSharedAcrossPerfusionChannelsAndRhythmVariants), FillingIsSharedAcrossPerfusionChannelsAndRhythmVariants),
        new(nameof(FillingScalesPressureExcursionOnce), FillingScalesPressureExcursionOnce),
        new(nameof(FillingRejectsConflictingAndUnsupportedModels), FillingRejectsConflictingAndUnsupportedModels)
    ];

    private static void FillingSeparatesRateFromEffectiveAtrialContraction()
    {
        Check.That(CardiacFillingPerfusion.StrokeVolumePermille(800_000_000, 120_000_000) == 1000 &&
            CardiacFillingPerfusion.StrokeVolumePermille(800_000_000, 0) == 750,
            "reference volume requires both adequate time and effective atrial contraction");
        Check.That(CardiacFillingPerfusion.StrokeVolumePermille(600_000_000, 120_000_000) == 840 &&
            CardiacFillingPerfusion.StrokeVolumePermille(600_000_000, 0) == 630,
            "equal RR can produce different stroke volumes with AV dissociation");
        int previous = -1;
        for (long rr = 300_000_000; rr <= 800_000_000; rr += 1_000_000)
        {
            int gain = CardiacFillingPerfusion.StrokeVolumePermille(rr, 0);
            Check.That(gain >= previous && gain <= 750, "passive filling increases monotonically with available diastole");
            previous = gain;
        }
        Check.That(CardiacFillingPerfusion.StrokeVolumePermille(300_000_000, 0) == 0 &&
            CardiacFillingPerfusion.StrokeVolumePermille(long.MaxValue, 120_000_000) == 1000,
            "closed filling window and long pauses have explicit bounded behavior");
        foreach (Action action in new Action[]
        {
            () => CardiacFillingPerfusion.StrokeVolumePermille(0, 0),
            () => CardiacFillingPerfusion.StrokeVolumePermille(600_000_000, -1),
            () => CardiacFillingPerfusion.StrokeVolumePermille(600_000_000, 120_000_001)
        })
        {
            bool rejected = false;
            try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check.That(rejected, "invalid filling durations reject");
        }
    }

    private static void FillingTracksIndependentAtrialPhase()
    {
        var aar = AcceleratedAtrialReference.CreatePlan();
        var ajr = AcceleratedJunctionalReference.CreatePlan();
        var aivr = AcceleratedVentricularReference.CreatePlan();
        var vt = VentricularTachycardiaReference.CreatePlan();
        Check.That(Enumerable.Range(0, 32).All(i => CardiacFillingPerfusion.GainPermille(aar, (ulong)i) == 840),
            "conducted negative P-prime retains effective atrial contribution on every beat");
        Check.That(Enumerable.Range(0, 4).Select(i => CardiacFillingPerfusion.GainPermille(ajr, (ulong)i)).SequenceEqual([840, 630, 630, 805]),
            "AJR atrial contraction contributes fully, not at all, or partially as the two clocks drift");
        Check.That(Enumerable.Range(0, 4).Select(i => CardiacFillingPerfusion.GainPermille(aivr, (ulong)i)).SequenceEqual([969, 868, 767, 727]),
            "slower AIVR preserves more passive filling and varies with AV phase");
        Check.That(Enumerable.Range(0, 3).Select(i => CardiacFillingPerfusion.GainPermille(vt, (ulong)i)).SequenceEqual([346, 286, 342]),
            "short VT diastole admits only the overlapping part of an atrial contraction");
    }

    private static void FillingUsesActualCaptureAndResumedMechanicalIntervals()
    {
        var capture = VentricularTachycardiaReference.CreatePlan(capture: true);
        Check.That(CardiacFillingPerfusion.GainPermille(capture, 11) == 137 &&
            CardiacFillingPerfusion.GainPermille(capture, 12) == 394 &&
            CardiacFillingPerfusion.GainPermille(capture, 13) == 286,
            "capture uses 330ms then 420ms RR and returns to 375ms; no permanent minimum-RR penalty");
        var interrupted = AcceleratedAtrialReference.CreatePlan() with
        { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 2, MechanicalDurationCycles = 3 };
        Check.That(CardiacFillingPerfusion.GainPermille(interrupted, 5) == 1000 &&
            CardiacFillingPerfusion.GainPermille(interrupted, 6) == 840,
            "first resumed beat fills during the pause; following beat uses its actual shorter interval");
        var stride = interrupted with { MechanicalEveryCycles = 2, MechanicalAfterCycles = 3, MechanicalDurationCycles = 4 };
        Check.That(CardiacFillingPerfusion.GainPermille(stride, 8) == 1000,
            "stride and omitted interval select the previous accepted mechanical beat");
    }

    private static void FillingRemainsIndexedAtShiftedAndLateEpochs()
    {
        foreach (var plan in new[] { AcceleratedAtrialReference.CreatePlan(), AcceleratedJunctionalReference.CreatePlan(),
            AcceleratedVentricularReference.CreatePlan(), VentricularTachycardiaReference.CreatePlan(),
            VentricularTachycardiaReference.CreatePlan(capture: true) })
        {
            var shifted = plan with { EpochAnchorSimTimeNs = 9_000_000_000_000_000_000 };
            for (ulong index = 0; index < 64; index++)
            {
                int expected = CardiacFillingPerfusion.GainPermille(plan, index);
                Check.That(expected == CardiacFillingPerfusion.GainPermille(shifted, index) &&
                    expected == CardiacFillingPerfusion.GainPermille(plan, index + 32_000_000_000),
                    "gain depends on bounded local phase, not epoch magnitude or elapsed replay");
            }
        }
    }

    private static void FillingIsSharedAcrossPerfusionChannelsAndRhythmVariants()
    {
        PhysiologyIllustrationConfiguration[] configurations =
        [
            PhysiologyIllustrationConfiguration.AarPreset, PhysiologyIllustrationConfiguration.AjrPreset,
            PhysiologyIllustrationConfiguration.AivrPreset,
            PhysiologyIllustrationConfiguration.AivrPreset with { AivrCapture = true },
            PhysiologyIllustrationConfiguration.AivrPreset with { AivrFusion = true },
            PhysiologyIllustrationConfiguration.VtPreset,
            PhysiologyIllustrationConfiguration.VtPreset with { VtCapture = true },
            PhysiologyIllustrationConfiguration.VtPreset with { VtFusion = true },
            PhysiologyIllustrationConfiguration.VtPreset with { VtBidirectional = true },
            PhysiologyIllustrationConfiguration.VtPreset with { VtTwisting = true }
        ];
        foreach (var configuration in configurations)
        {
            var state = PhysiologyIllustrationSource.Create(configuration).CaptureState();
            Check.That(state.Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(2)).Generator.PlethRunoff!.UseCardiacFillingPerfusion &&
                state.Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(3)).Generator.VascularPressure!.UseCardiacFillingPerfusion &&
                state.Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(5)).Generator.VascularPressure!.UseCardiacFillingPerfusion,
                "all selected rhythms share filling across optical and both pressure channels");
            foreach (int row in new[] { 3, 5 })
            {
                var generator = state.Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(row)).Generator;
                var plan = generator.Timeline.Plan;
                var pressure = generator.VascularPressure!;
                var limited = VascularPressureSource.Create(plan, pressure);
                var full = VascularPressureSource.Create(plan, pressure with { UseCardiacFillingPerfusion = false });
                long limitedSum = 0, fullSum = 0;
                for (long time = 300_000_000_000; time < 312_000_000_000; time += 40_000_000)
                {
                    long value = limited.EvaluateAt(time);
                    Check.That(value > pressure.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One && value < short.MaxValue * FixedPointMath.Q32One,
                        "filling retains finite pulsatile pressure without saturation");
                    limitedSum += value;
                    fullSum += full.EvaluateAt(time);
                }
                Check.That(limitedSum < fullSum, "limited ejected volume reduces mean pressure in each reservoir");
            }
        }
        foreach (var configuration in new[] { PhysiologyIllustrationConfiguration.Default,
            PhysiologyIllustrationConfiguration.SinusArrhythmiaPreset, PhysiologyIllustrationConfiguration.SinusArrestPreset,
            PhysiologyIllustrationConfiguration.AtrialEscapePreset, PhysiologyIllustrationConfiguration.SvtPreset })
        {
            var state = PhysiologyIllustrationSource.Create(configuration).CaptureState();
            Check.That(state.Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(2)).Generator.PlethRunoff!.UseCardiacFillingPerfusion &&
                state.Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(3)).Generator.VascularPressure!.UseCardiacFillingPerfusion &&
                state.Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(5)).Generator.VascularPressure!.UseCardiacFillingPerfusion,
                "regular, escape and SVT sources also apply filling consistently");
        }
    }

    private static void FillingScalesPressureExcursionOnce()
    {
        foreach (var configuration in new[] { PhysiologyIllustrationConfiguration.AarPreset, PhysiologyIllustrationConfiguration.SvtPreset })
        {
            var state = PhysiologyIllustrationSource.Create(configuration).CaptureState();
            foreach (int row in new[] { 3, 5 })
            {
                var generator = state.Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(row)).Generator;
                var plan = generator.Timeline.Plan;
                var pressure = generator.VascularPressure!;
                var limited = VascularPressureSource.Create(plan, pressure);
                var full = VascularPressureSource.Create(plan, pressure with { UseCardiacFillingPerfusion = false });
                int gain = CardiacFillingPerfusion.GainPermille(plan, 1000);
                long asymptote = pressure.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One;
                for (long time = 300_000_000_000; time < 302_000_000_000; time += 1_000_000)
                {
                    long expected = asymptote + (long)FixedPointMath.RoundDivideTiesToEven((Int128)(full.EvaluateAt(time) - asymptote) * gain, 1000);
                    Check.That(Math.Abs(limited.EvaluateAt(time) - expected) <= FixedPointMath.Q32One / 1000,
                        "steady pressure above runoff asymptote scales once with stroke volume, including overlapping pulse contours");
                }
            }
        }
    }

    private static void FillingRejectsConflictingAndUnsupportedModels()
    {
        var vt = VentricularTachycardiaReference.CreatePlan();
        foreach (Action action in new Action[]
        {
            () => VascularPressureSource.Create(vt, VtPerfusionReference.Arterial with { UsePrematureBeatPerfusion = true }),
            () => PlethRunoffSource.Create(vt, VtPerfusionReference.Pleth with { UseConductedFlutterPerfusion = true }),
            () => VascularPressureSource.Create(AtrialFibrillationReference.CreatePlan(), VtPerfusionReference.Arterial),
            () => PlethRunoffSource.Create(AtrialFibrillationReference.CreatePlan(), VtPerfusionReference.Pleth)
        })
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "incompatible rhythm or two competing perfusion models reject before publication");
        }
    }
}
