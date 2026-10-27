// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PatientRateTimelineSpecifications
{
    private const long SecondNs = 1_000_000_000;
    internal static readonly Specification[] All =
    [
        new(nameof(InterruptedVitalCurvesStartAtTheirCurrentValue), InterruptedVitalCurvesStartAtTheirCurrentValue),
        new(nameof(RateRampsIntegratePhaseWithoutRestartingBeats), RateRampsIntegratePhaseWithoutRestartingBeats),
        new(nameof(RespiratoryPhaseAndTransportFollowTheSameClock), RespiratoryPhaseAndTransportFollowTheSameClock),
        new(nameof(RateSourcesRetainSampleAndCheckpointDeterminism), RateSourcesRetainSampleAndCheckpointDeterminism),
        new(nameof(FutureRateEventsLeaveEveryEarlierChannelUnchanged), FutureRateEventsLeaveEveryEarlierChannelUnchanged),
        new(nameof(UnsupportedRatePlansRejectBeforeAcquisition), UnsupportedRatePlansRejectBeforeAcquisition),
    ];

    private static void InterruptedVitalCurvesStartAtTheirCurrentValue()
    {
        VitalValueChange[] changes = [new(2 * SecondNs, 10 * SecondNs, 100), new(6 * SecondNs, 4 * SecondNs, 40)];
        var curve = new VitalValueCurve(50, changes);
        changes[0] = new(0, 0, 999);
        Check.That(curve.At(0) == 50 && curve.At(2 * SecondNs) == 50 && curve.At(6 * SecondNs) == 70 &&
            curve.At(8 * SecondNs) == 55 && curve.At(10 * SecondNs) == 40 && curve.At(long.MaxValue) == 40,
            "owned ramps retain their baseline, interpolate, and reverse from the reached value");
        var tied = new VitalValueCurve(0, [new(SecondNs, 0, 20), new(SecondNs, SecondNs, 40)]);
        Check.That(tied.At(SecondNs) == 20 && tied.At(2 * SecondNs) == 40,
            "same-time events retain stable authored order");
        var longRamp = new VitalValueCurve(30, [new(0, long.MaxValue, 180)]);
        var clock = new PhysiologyRateClock(800_000_000, longRamp);
        Check.That(clock.ReferenceAt(SecondNs) > 0, "a valid long-duration ramp does not overflow its integral");
    }

    private static void RateRampsIntegratePhaseWithoutRestartingBeats()
    {
        var clock = new PhysiologyRateClock(800_000_000,
            new(75, [new(2 * SecondNs, 10 * SecondNs, 120)]));
        var plan = PhysiologyIllustrationConfiguration.Default.ResolvePlan() with { CardiacRateClock = clock };
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var whole = timeline.AdvanceBefore(20 * SecondNs, 1000);
        var partitioned = RegularPhysiologyTimeline.Start(plan);
        List<PhysiologyCycleEvent> pieces = [];
        for (long to = 37_000_000; to < 20 * SecondNs; to += 37_000_000)
        { pieces.AddRange(partitioned.AdvanceBefore(to, 100)); }
        pieces.AddRange(partitioned.AdvanceBefore(20 * SecondNs, 100));
        Check.That(whole.SequenceEqual(pieces), "event times and ordinals do not depend on advance partitioning");
        var ventricular = whole.Where(item => item.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
        Check.That(ventricular.Select(item => item.CycleIndex).SequenceEqual(Enumerable.Range(0, ventricular.Length).Select(value => (ulong)value)),
            "a ramp never skips or duplicates a beat ordinal");
        foreach (var beat in ventricular)
        {
            long reference = checked((long)beat.CycleIndex * plan.HeartPeriodNs);
            long time = beat.SimTimeNs - plan.VentricularElectricalOffsetNs;
            Check.That(clock.ReferenceAt(time) >= reference && (time == 0 || clock.ReferenceAt(time - 1) < reference),
                "every beat is the first real nanosecond at its integrated reference phase");
        }
        long[] intervals = ventricular.Zip(ventricular.Skip(1), (first, second) => second.SimTimeNs - first.SimTimeNs).ToArray();
        Check.That(intervals[0] == 800_000_000 && intervals[^1] == 500_000_000 &&
            intervals.Skip(3).Take(8).Zip(intervals.Skip(4).Take(8)).All(pair => pair.First > pair.Second),
            "RR intervals shorten progressively and settle exactly at the requested rate");
    }

    private static void RespiratoryPhaseAndTransportFollowTheSameClock()
    {
        foreach (var pattern in new[] { RespiratoryPattern.Regular, RespiratoryPattern.SpontaneousIllustration })
        {
            var clock = new PhysiologyRateClock(3_750_000_000,
                new(16, [new(2 * SecondNs, 8 * SecondNs, 24)]));
            var configuration = PhysiologyIllustrationConfiguration.Default with
            { RespiratoryPattern = pattern, RespiratoryRateClock = clock };
            var dynamic = PhysiologyIllustrationSource.Create(configuration).CaptureState();
            var reference = PhysiologyIllustrationSource.Create(configuration with { RespiratoryRateClock = null }).CaptureState();
            foreach (int row in new[] { 1, 4 })
            {
                var actualState = dynamic.Channels.Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(row)).Generator;
                var referenceState = reference.Channels.Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(row)).Generator;
                for (long time = SecondNs; time < 20 * SecondNs; time += 137_000_000)
                {
                    long phase = clock.ReferenceAt(time);
                    var actualEvents = RegularPhysiologyTimeline.Start(actualState.Timeline.Plan).AdvanceBefore(time + 1, 1000);
                    var referenceEvents = RegularPhysiologyTimeline.Start(referenceState.Timeline.Plan).AdvanceBefore(phase + 1, 1000);
                    long actual = EventWaveformComposition.Restore(new(actualState.Bands, actualEvents)).EvaluateAt(time);
                    long expected = EventWaveformComposition.Restore(new(referenceState.Bands, referenceEvents)).EvaluateAt(phase);
                    Check.That(Math.Abs(actual - expected) < 1_000_000,
                        "respiratory and CO2 contours use the integrated phase without broken or overlapping breaths");
                }
            }
            var ventilation = new VentilationTransportPlan(500_000, 150_000, 210_000);
            var transport = PhysiologyIllustrationSource.CreateTransport(configuration, ventilation, 70_000);
            var whole = transport.Integrate(0, 20 * SecondNs);
            long delivered = 0, alveolar = 0, blood = 0;
            for (long from = 0; from < 20 * SecondNs; from += 54_321_000)
            {
                var part = transport.Integrate(from, Math.Min(20 * SecondNs, from + 54_321_000));
                delivered += part.DeliveredVolumeNanolitersBtps;
                alveolar += part.AlveolarVentilationNanolitersBtps;
                blood += part.EffectiveBloodVolumeNanoliters;
            }
            var expectedVolume = PhysiologyIllustrationSource.CreateTransport(configuration with { RespiratoryRateClock = null }, ventilation, 70_000)
                .Integrate(0, clock.ReferenceAt(20 * SecondNs));
            Check.That(delivered == whole.DeliveredVolumeNanolitersBtps && alveolar == whole.AlveolarVentilationNanolitersBtps &&
                blood == whole.EffectiveBloodVolumeNanoliters && delivered == expectedVolume.DeliveredVolumeNanolitersBtps &&
                alveolar == expectedVolume.AlveolarVentilationNanolitersBtps,
                "ventilation is conserved across arbitrary caller partitions and follows the waveform respiratory phase");
        }
    }

    private static void RateSourcesRetainSampleAndCheckpointDeterminism()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        {
            CardiacRateClock = new(800_000_000, new(75, [new(SecondNs, 3 * SecondNs, 100)])),
            RespiratoryRateClock = new(3_750_000_000, new(16, [new(SecondNs, 3 * SecondNs, 24)]))
        };
        var source = PhysiologyIllustrationSource.Create(configuration);
        var liveEcg = LiveEcgIllustrationSource.Create(configuration);
        for (long to = 50_000_000; to <= 2 * SecondNs; to += 50_000_000)
        { source.AdvanceTo(to, 50, 1, 100); liveEcg.GenerateBefore(to, 50, 100); }
        var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
        var ecgRestored = liveEcg.Fork();
        for (long to = 2 * SecondNs + 50_000_000; to <= 6 * SecondNs; to += 50_000_000)
        {
            var actual = source.AdvanceTo(to, 50, 1, 100);
            var replay = restored.AdvanceTo(to, 50, 1, 100);
            Check.That(actual.Count == replay.Count && actual.Zip(replay).All(pair => pair.First.SequenceEqual(pair.Second)),
                "waveform packets replay byte-exactly through a live rate ramp");
            var ecg = liveEcg.GenerateBefore(to, 50, 100);
            var replayEcg = ecgRestored.GenerateBefore(to, 50, 100);
            Check.That(ecg.Zip(replayEcg).All(pair => pair.First.Tick == pair.Second.Tick &&
                pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
                "all twelve ECG leads retain the same acquisition clock and deterministic ramp");
        }
    }

    private static void UnsupportedRatePlansRejectBeforeAcquisition()
    {
        foreach (var configuration in new[]
        {
            PhysiologyIllustrationConfiguration.Default with { CardiacActivity = CardiacActivity.Absent },
            PhysiologyIllustrationConfiguration.JunctionalEscape,
            PhysiologyIllustrationConfiguration.Flutter(2)
        })
        {
            bool rejected = false;
            try
            {
                _ = PhysiologyIllustrationSource.Create(configuration with
                { CardiacRateClock = new(800_000_000, new(75, [new(SecondNs, SecondNs, 100)])) });
            }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "incompatible rate/template combinations reject before a source is published");
        }
    }

    private static void FutureRateEventsLeaveEveryEarlierChannelUnchanged()
    {
        var baseline = PhysiologyIllustrationConfiguration.Default with
        { BreathPeriodMilliseconds = 3529, InspirationMilliseconds = 1764 };
        var scheduled = baseline with
        {
            CardiacRateClock = new(800_000_000, new(75, [new(10 * SecondNs, 3 * SecondNs, 100)])),
            RespiratoryRateClock = new(3_529_000_000, new(17, [new(10 * SecondNs, 3 * SecondNs, 24)]))
        };
        var control = PhysiologyIllustrationSource.Create(baseline);
        var actual = PhysiologyIllustrationSource.Create(scheduled);
        for (long to = 50_000_000; to <= 3 * SecondNs; to += 50_000_000)
        {
            var expectedBlocks = control.AdvanceTo(to, 50, 1, 100);
            var actualBlocks = actual.AdvanceTo(to, 50, 1, 100);
            Check.That(expectedBlocks.Count == actualBlocks.Count && expectedBlocks.Zip(actualBlocks).All(pair => pair.First.SequenceEqual(pair.Second)),
                "a future ramp cannot alter waveform morphology, pressure baseline, respiratory phase, or sample clocks before its start");
        }
        Check.That(scheduled.RespiratoryRateClock.ReferenceAt(9 * SecondNs + 123) == 9 * SecondNs + 123,
            "rounding an authored respiratory period to a displayed integer rate cannot move the baseline phase");
    }
}
