// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CentralVenousPressureSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Cvp = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static RegularPhysiologyPlan Timeline => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static CentralVenousPressurePlan Plan => new(600, new(0, 120_000_000, 200),
        new(0, 120_000_000, 80), new(60_000_000, 240_000_000, 100),
        new(160_000_000, 320_000_000, 250), new(400_000_000, 160_000_000, 120), 0);
    public static Specification[] All =>
    [
        new(nameof(CvpComponentsFollowAtrialAndVentricularEvents), CvpComponentsFollowAtrialAndVentricularEvents),
        new(nameof(CvpAtrialSuppressionLeavesOtherWavesIntact), CvpAtrialSuppressionLeavesOtherWavesIntact),
        new(nameof(CvpRespiratoryPressureHasExplicitSignAndTiming), CvpRespiratoryPressureHasExplicitSignAndTiming),
        new(nameof(CvpPressureSurvivesNativeDelayAndRecovery), CvpPressureSurvivesNativeDelayAndRecovery),
        new(nameof(CvpRejectsInvalidSupportAndAmplitudeBudget), CvpRejectsInvalidSupportAndAmplitudeBudget),
        new(nameof(CvpLateFailureAndCancellationPreserveState), CvpLateFailureAndCancellationPreserveState),
    ];

    private static EventWaveformComposition Compose(CentralVenousPressurePlan plan, bool atrial = true)
    {
        var events = RegularPhysiologyTimeline.Start(Timeline).AdvanceBefore(800_000_000, 100)
            .Where(item => atrial || item.Kind != PhysiologyCycleEventKind.AtrialMechanical).ToArray();
        return EventWaveformComposition.Restore(new(plan.CreateChannel(Timeline, Cvp, 0).Bands, events));
    }

    private static void CvpComponentsFollowAtrialAndVentricularEvents()
    {
        var source = Compose(Plan);
        Check.That(source.EvaluateAt(140_000_000) > 150 * Q && source.EvaluateAt(277_500_000) == 80 * Q &&
            source.EvaluateAt(400_000_000) < -80 * Q && source.EvaluateAt(550_000_000) > 200 * Q &&
            source.EvaluateAt(720_000_000) == -120 * Q,
            "a/c/v peaks and x/y troughs follow explicit atrial/ventricular mechanical timing");
        var bands = Plan.CreateChannel(Timeline, Cvp, 0).Bands;
        Check.That(bands[0].Trigger == PhysiologyCycleEventKind.AtrialMechanical && bands.Skip(1).Take(4)
            .All(band => band.Trigger == PhysiologyCycleEventKind.VentricularMechanical), "CVP is composed from distinct events rather than a whole-cycle ECG scale");
    }

    private static void CvpAtrialSuppressionLeavesOtherWavesIntact()
    {
        var absent = Compose(Plan, atrial: false);
        var disabled = Compose(Plan with { A = Plan.A with { MagnitudeCentiMmHg = 0 } });
        var original = Compose(Plan);
        for (long time = 0; time < 800_000_000; time += 8_000_000)
        {
            Check.That(absent.EvaluateAt(time) == disabled.EvaluateAt(time), "no atrial mechanical event means no a wave without disabling ventricular pressure");
            if (time >= 200_000_000)
            { Check.That(original.EvaluateAt(time) == absent.EvaluateAt(time), "a suppression preserves c/x/v/y morphology"); }
        }
        Check.That(absent.EvaluateAt(140_000_000) == 0, "a wave is not synthesized from ventricular events");
    }

    private static void CvpRespiratoryPressureHasExplicitSignAndTiming()
    {
        var plus = Plan with { RespiratoryDeltaCentiMmHg = 100 };
        var minus = Plan with { RespiratoryDeltaCentiMmHg = -100 };
        var a = Compose(plus); var b = Compose(minus); var none = Compose(Plan);
        for (long time = 0; time <= 3_750_000_000; time += 30_000_000)
        { Check.That(a.EvaluateAt(time) + b.EvaluateAt(time) == 2 * none.EvaluateAt(time), "signed respiratory pressure adds independently of cardiac wave amplitude"); }
        Check.That(a.EvaluateAt(1_875_000_000) == 100 * Q && b.EvaluateAt(1_875_000_000) == -100 * Q &&
            a.EvaluateAt(3_750_000_000) == 0, "respiratory extremum aligns to end inspiration and closes at the next breath");
        var shorter = Timeline with { InspirationDurationNs = 1_000_000_000 };
        var channel = plus.CreateChannel(shorter, Cvp, 0);
        var isolated = EventWaveformComposition.Restore(new(channel.Bands, [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));
        Check.That(isolated.EvaluateAt(1_000_000_000) == 100 * Q, "inspiration duration changes respiratory phase without changing its pressure magnitude");
    }

    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Ecg, Cvp, 1, 1, 1, 0, 32,
        [new(Timeline, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
         (Plan with { RespiratoryDeltaCentiMmHg = -100 }).CreateChannel(Timeline, Cvp, 0)]);

    private static void CvpPressureSurvivesNativeDelayAndRecovery()
    {
        var boundary = Group();
        Check.That(boundary.AdvanceTo(271_999_999, 68, 1, 100).Count == 0 && boundary.AdvanceTo(272_000_000, 1, 1, 100).Count == 1,
            "CVP retains native125Hz and80ms processing delay");
        var expected = Group().AdvanceTo(4_000_000_000, 1000, 20, 100);
        var group = Group();
        List<byte[]> actual = [];
        for (int step = 1; step <= 20; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "cardiac components and active respiratory pressure survive native acquisition and group restore");
        var planes = actual.Select(bytes => WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == Cvp)).ToArray();
        var channel = (Plan with { RespiratoryDeltaCentiMmHg = -100 }).CreateChannel(Timeline, Cvp, 0);
        var samples = PhysiologySignalGenerator.Start(Timeline, channel.Plane.ProfileId, 1, channel.Bands).GenerateBefore(3_800_000_000, 475, 100);
        Check.That(planes.All(plane => plane.ScaleNumerator == 1 && plane.ScaleDenominator == 100 && plane.OffsetNumerator == 600 &&
            plane.OffsetDenominator == 100 && plane.SampleRateNumerator == 125) &&
            planes.SelectMany(plane => plane.Samples).SequenceEqual(samples.Select(sample => sample.NormalizedValue)),
            "signed increments and fractional baseline preserve hundredth-mmHg pressure through wire encoding");
    }

    private static void CvpRejectsInvalidSupportAndAmplitudeBudget()
    {
        foreach (var invalid in new[] { Plan with { A = null! }, Plan with { X = Plan.X with { DelayNs = long.MaxValue } },
            Plan with { C = Plan.C with { DurationNs = 0 } }, Plan with { V = Plan.V with { DurationNs = 800_000_001 } },
            Plan with { BaselineCentiMmHg = 32768 }, Plan with { Y = Plan.Y with { MagnitudeCentiMmHg = -1 } },
            Plan with { A = Plan.A with { MagnitudeCentiMmHg = 32767 } }, Plan with { RespiratoryDeltaCentiMmHg = int.MinValue } })
        {
            bool rejected = false;
            try { invalid.CreateChannel(Timeline, Cvp, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "Cvp.InvalidPlan"; }
            Check.That(rejected, "incomplete components, support overflow and conservative overlap budget reject before publication");
        }
    }

    private static void CvpLateFailureAndCancellationPreserveState()
    {
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(4_000_000_000, 1000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        bool cancelled = false;
        try { group.AdvanceTo(200_000_000, 50, 1, 100, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(limited && cancelled && JsonSerializer.Serialize(group.CaptureState()) == before,
            "late block failure and cancellation preserve every cardiac/respiratory source cursor");
    }
}
