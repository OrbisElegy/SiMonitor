// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VascularPressureSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Abp = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Pa = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static RegularPhysiologyPlan Physiology => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    private static VascularPressurePlan Arterial => new(80_000_000, 240_000_000,
        1_500_000_000, 10_000, 1200, 26_000);
    private static VascularPressurePlan Pulmonary => new(40_000_000, 200_000_000,
        700_000_000, 2000, 500, 6000);
    private static RegularPhysiologyPlan Interrupted => Physiology with
    {
        VentricularMechanicalEnabled = false,
        MechanicalAfterCycles = 1,
        MechanicalDurationCycles = 2,
    };

    public static Specification[] All =>
    [
        new(nameof(VascularRunoffRetainsPressureWithoutMechanicalEvents), VascularRunoffRetainsPressureWithoutMechanicalEvents),
        new(nameof(VascularResumptionAddsToRemainingPressure), VascularResumptionAddsToRemainingPressure),
        new(nameof(VascularFixedPointTrajectoryMatchesRectangularRcReference), VascularFixedPointTrajectoryMatchesRectangularRcReference),
        new(nameof(VascularMechanicsKeepConductionStrideAndOriginalIndices), VascularMechanicsKeepConductionStrideAndOriginalIndices),
        new(nameof(VascularSubsampleTransitAndSamplingProfilesKeepOneTrajectory), VascularSubsampleTransitAndSamplingProfilesKeepOneTrajectory),
        new(nameof(VascularChannelsKeepIndependentPressureAndWireUnits), VascularChannelsKeepIndependentPressureAndWireUnits),
        new(nameof(VascularSamplingAndEveryBlockRestorePreserveExactBytes), VascularSamplingAndEveryBlockRestorePreserveExactBytes),
        new(nameof(VascularCheckpointRecomputesPendingPressure), VascularCheckpointRecomputesPendingPressure),
        new(nameof(VascularBoundsAndLegacyDefaultsRejectAmbiguousModels), VascularBoundsAndLegacyDefaultsRejectAmbiguousModels),
        new(nameof(VascularCancellationAndLatePublicationFailureAreAtomic), VascularCancellationAndLatePublicationFailureAreAtomic),
    ];

    private static void VascularRunoffRetainsPressureWithoutMechanicalEvents()
    {
        var physiology = Physiology with { VentricularMechanicalEnabled = false };
        var source = VascularPressureSource.Create(physiology, Arterial);
        Check.That(source.EvaluateAt(0) == 10_000 * Q && source.EvaluateAt(79_999_999) == 10_000 * Q,
            "explicit initial pressure is held until the delayed epoch without invented earlier ejection");
        long previous = source.EvaluateAt(80_000_000);
        for (long time = 180_000_000; time <= 10_080_000_000; time += 100_000_000)
        {
            long value = source.EvaluateAt(time);
            Check.That(value < previous && value > 1200 * Q,
                "electrical activity without mechanical ejection gives bounded monotonic pressure runoff");
            previous = value;
        }
        Check.That(source.EvaluateAt(13_000_000_000) > 1200 * Q &&
            source.EvaluateAt(Arterial.TransitDelayNs + 64 * Arterial.TimeConstantNs) == 1200 * Q,
            "absence of pulsatility does not erase residual pressure and the defined tail horizon reaches its asymptote");
        foreach (var activity in new[] { CardiacActivity.AtrialOnly, CardiacActivity.Absent })
        {
            var other = VascularPressureSource.Create(Physiology with { CardiacActivity = activity }, Arterial);
            Check.That(other.EvaluateAt(1_000_000_000) == source.EvaluateAt(1_000_000_000),
                "atrial events and electrical events cannot inject ventricular pressure");
        }
    }

    private static void VascularResumptionAddsToRemainingPressure()
    {
        var delayed = Arterial with { TransitDelayNs = 2_480_000_000 };
        var actual = VascularPressureSource.Create(Interrupted, delayed);
        var permanent = VascularPressureSource.Create(Interrupted with { MechanicalDurationCycles = null }, delayed);
        var normal = VascularPressureSource.Create(Physiology, delayed);
        const long firstArrival = 2_720_000_000;
        const long resumeArrival = 5_120_000_000;
        Check.That(actual.EvaluateAt(firstArrival + 100_000_000) > actual.EvaluateAt(firstArrival) &&
            actual.EvaluateAt(firstArrival + 100_000_000) == normal.EvaluateAt(firstArrival + 100_000_000),
            "the first delayed ejection completes even when transit is longer than the suppression interval");
        Check.That(actual.EvaluateAt(resumeArrival) == permanent.EvaluateAt(resumeArrival) &&
            Math.Abs(actual.EvaluateAt(resumeArrival + 1) - actual.EvaluateAt(resumeArrival - 1)) < Q,
            "resumption is continuous at the retained original mechanical time plus transit");
        Check.That(actual.EvaluateAt(resumeArrival + 80_000_000) > permanent.EvaluateAt(resumeArrival + 80_000_000) &&
            actual.EvaluateAt(resumeArrival + 80_000_000) < normal.EvaluateAt(resumeArrival + 80_000_000),
            "the first resumed ejection adds to depleted pressure without resetting to the normal-beating baseline");
        long tailEnd = firstArrival + delayed.EjectionDurationNs;
        long previous = actual.EvaluateAt(tailEnd);
        for (long time = tailEnd + 40_000_000; time < resumeArrival; time += 40_000_000)
        {
            long next = actual.EvaluateAt(time);
            Check.That(next < previous, "the pulse-free interval has no repeated notch or artificial pressure rebound");
            previous = next;
        }
    }

    private static void VascularFixedPointTrajectoryMatchesRectangularRcReference()
    {
        var physiology = Interrupted with { EpochAnchorSimTimeNs = 17, VentricularMechanicalOffsetNs = 240_333_333 };
        foreach (var pressure in new[]
        {
            Arterial,
            Arterial with { TransitDelayNs = 3_777_777, TimeConstantNs = 100_000_000,
                EjectionDurationNs = 197_123_457, InitialPressureCentiMmHg = 32767,
                AsymptoticPressureCentiMmHg = 0, EjectionEquilibriumCentiMmHg = 32767 },
            Arterial with { TimeConstantNs = 10_000_000_000, EjectionDurationNs = 800_000_000 },
        })
        {
            var source = VascularPressureSource.Create(physiology, pressure);
            long[] events = MechanicalTimes(physiology, 8_000_000_000);
            for (long time = 17; time <= 8_000_000_000; time += 7_123_457)
            {
                double actual = (double)source.EvaluateAt(time) / Q;
                double expected = Reference(physiology, pressure, time, events);
                Check.That(Math.Abs(actual - expected) <= 2,
                    "fixed microsecond RC decay and fractional phases stay within 0.02mmHg of the continuous rectangular-input solution");
                Check.That(actual >= pressure.AsymptoticPressureCentiMmHg && actual <=
                    Math.Max(pressure.InitialPressureCentiMmHg, pressure.AsymptoticPressureCentiMmHg + pressure.EjectionEquilibriumCentiMmHg),
                    "accepted configurations keep the exact physical pressure inside the declared raw amplitude budget");
            }
        }
    }

    private static void VascularMechanicsKeepConductionStrideAndOriginalIndices()
    {
        foreach (int conduction in new[] { 1, 3 })
        {
            foreach (int stride in new[] { 1, 2, 4 })
            {
                var physiology = Interrupted with { VentricularConductionRatio = conduction, MechanicalEveryCycles = stride };
                var source = VascularPressureSource.Create(physiology, Arterial);
                long period = physiology.HeartPeriodNs * conduction;
                long[] selected = Enumerable.Range(0, 20).Where(index => index % stride == 0 && (index < 1 || index >= 3))
                    .Select(index => index * period + physiology.VentricularMechanicalOffsetNs).ToArray();
                for (long time = 0; time <= 12_000_000_000; time += 31_111_111)
                {
                    Check.That(Math.Abs((double)source.EvaluateAt(time) / Q - Reference(physiology, Arterial, time, selected)) <= 2,
                        "conduction, stride and suppression select original mechanical indices without compensating ejection strength");
                }
            }
        }
    }

    private static void VascularSubsampleTransitAndSamplingProfilesKeepOneTrajectory()
    {
        var physiology = Physiology with { EpochAnchorSimTimeNs = 17, VentricularMechanicalOffsetNs = 240_333_333 };
        var pressure = Arterial with { TransitDelayNs = 3_777_777, EjectionDurationNs = 197_123_457 };
        var source = VascularPressureSource.Create(physiology, pressure);
        var undelayed = VascularPressureSource.Create(physiology, pressure with { TransitDelayNs = 0 });
        for (long time = physiology.EpochAnchorSimTimeNs; time <= 3_000_000_000; time += 13_357_913)
        {
            Check.That(source.EvaluateAt(time + pressure.TransitDelayNs) == undelayed.EvaluateAt(time),
                "nanosecond transit shifts the underlying trajectory exactly without rounding to the sampling grid");
        }
        var pressureSamples = Start(physiology, pressure).GenerateBefore(3_000_000_017, 375, 100);
        var fasterSamples = Start(physiology, pressure, "AcqECGMonitor250@1").GenerateBefore(3_000_000_017, 750, 100);
        Check.That(pressureSamples.Select(sample => (sample.Tick.SimTimeNs, sample.ValueQ32))
            .SequenceEqual(fasterSamples.Where(sample => sample.Tick.SampleIndex % 2 == 0)
                .Select(sample => (sample.Tick.SimTimeNs, sample.ValueQ32))),
            "acquisition profile changes only sample selection, with exact Q32 agreement at shared timestamps");
        long trigger = physiology.EpochAnchorSimTimeNs + physiology.VentricularMechanicalOffsetNs + pressure.TransitDelayNs;
        var silent = VascularPressureSource.Create(physiology with { VentricularMechanicalEnabled = false }, pressure);
        Check.That(source.EvaluateAt(trigger) == silent.EvaluateAt(trigger) &&
            source.EvaluateAt(trigger + 1) > silent.EvaluateAt(trigger + 1),
            "an off-grid mechanical arrival contributes immediately after its exact trigger, including before the next sample");
    }

    private static void VascularChannelsKeepIndependentPressureAndWireUnits()
    {
        var first = Group(Interrupted, Pulmonary).AdvanceTo(4_000_000_000, 1000, 20, 100);
        var second = Group(Interrupted, Pulmonary with { InitialPressureCentiMmHg = 2500, TimeConstantNs = 900_000_000 })
            .AdvanceTo(4_000_000_000, 1000, 20, 100);
        Check.That(Samples(first, Ecg).SequenceEqual(Samples(second, Ecg)) &&
            Samples(first, Abp).SequenceEqual(Samples(second, Abp)) && !Samples(first, Pa).SequenceEqual(Samples(second, Pa)),
            "changing PA reservoir parameters leaves both ABP and ECG byte-identical");
        var planes = first.SelectMany(bytes => WaveformEnvelopeCodec.Decode(bytes).Planes.Where(plane => plane.ChannelId != Ecg)).ToArray();
        Check.That(planes.All(plane => plane.ScaleNumerator == 1 && plane.ScaleDenominator == 100 &&
            plane.OffsetNumerator == 0 && plane.OffsetDenominator == 1 && plane.SampleRateNumerator == 125) &&
            Samples(first, Abp)[0] == Arterial.InitialPressureCentiMmHg && Samples(first, Pa)[0] == Pulmonary.InitialPressureCentiMmHg,
            "native pressure wire samples carry absolute hundredth-mmHg pressure with zero affine baseline");
    }

    private static void VascularSamplingAndEveryBlockRestorePreserveExactBytes()
    {
        var wholeSource = Start(Interrupted, Arterial);
        var expectedSamples = wholeSource.GenerateBefore(4_800_000_000, 600, 100);
        var splitSource = Start(Interrupted, Arterial);
        List<PhysiologySignalSample> samples = [];
        var whole = Group(Interrupted, Pulmonary);
        var expected = whole.AdvanceTo(4_800_000_000, 1200, 24, 100);
        var split = Group(Interrupted, Pulmonary);
        List<byte[]> actual = [];
        for (int step = 1; step <= 24; step++)
        {
            samples.AddRange(splitSource.GenerateBefore(step * 200_000_000L, 25, 100));
            splitSource = PhysiologySignalGenerator.Restore(splitSource.CaptureState());
            actual.AddRange(split.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            split = PhysiologyWaveformGroup.Restore(split.CaptureState());
        }
        Check.That(expectedSamples.SequenceEqual(samples) && Snapshot(wholeSource) == Snapshot(splitSource),
            "pressure runoff and refilling preserve Q32 values and source cursors under every-batch restoration");
        Check.That(expected.Count == 23 && actual.Count == expected.Count &&
            expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)) && Snapshot(whole) == Snapshot(split),
            "ECG, ABP, PA and their pending native samples retain canonical bytes and exact state after every block restore");
        Check.That(Samples(actual, Abp).SequenceEqual(expectedSamples.Take(575).Select(sample => sample.NormalizedValue)),
            "encoded absolute pressure matches source quantization through interruption and recovery");
    }

    private static void VascularCheckpointRecomputesPendingPressure()
    {
        var group = Group(Interrupted, Pulmonary);
        _ = group.AdvanceTo(400_000_000, 100, 2, 100);
        var state = group.CaptureState();
        var arterial = state.Channels.Single(channel => channel.ChannelId == Abp);
        var pending = arterial.Delay.PendingSamples.ToArray();
        pending[0] = pending[0] with { NormalizedValue = (short)(pending[0].NormalizedValue + 1) };
        RejectGroup(() => PhysiologyWaveformGroup.Restore(state with
        {
            Channels = state.Channels.Select(channel => channel.ChannelId == Abp
                ? arterial with { Delay = arterial.Delay with { PendingSamples = pending } } : channel).ToArray(),
        }), "PhysiologyGroup.InvalidCheckpoint");
        var plane = state.Assembler.Planes.Single(item => item.Configuration.ChannelId == Abp);
        var assembled = plane.PendingSamples.ToArray();
        assembled[0] = assembled[0] with { NormalizedValue = (short)(assembled[0].NormalizedValue + 1) };
        RejectGroup(() => PhysiologyWaveformGroup.Restore(state with
        {
            Assembler = state.Assembler with
            {
                Planes = state.Assembler.Planes.Select(item => item.Configuration.ChannelId == Abp
                    ? plane with { PendingSamples = assembled } : item).ToArray(),
            },
        }), "PhysiologyGroup.InvalidCheckpoint");
        RejectGroup(() => PhysiologyWaveformGroup.Restore(state with
        {
            Channels = state.Channels.Select(channel => channel.ChannelId == Abp ? arterial with
            {
                Generator = arterial.Generator with { VascularPressure = Arterial with { InitialPressureCentiMmHg = 10_100 } },
            } : channel).ToArray(),
        }), "PhysiologyGroup.InvalidCheckpoint");
        RejectGroup(() => PhysiologyWaveformGroup.Restore(state with
        {
            Assembler = state.Assembler with
            {
                Planes = state.Assembler.Planes.Select(item => item.Configuration.ChannelId == Abp
                    ? plane with { Configuration = plane.Configuration with { OffsetNumerator = 80 } } : item).ToArray(),
            },
        }), "PhysiologyGroup.InvalidCheckpoint");
        Check.That(Snapshot(group) == Snapshot(PhysiologyWaveformGroup.Restore(state)),
            "rejecting plausible pending pressure or changed model evidence leaves the valid checkpoint recoverable");
    }

    private static void VascularBoundsAndLegacyDefaultsRejectAmbiguousModels()
    {
        foreach (var invalid in new[]
        {
            Arterial with { TransitDelayNs = -1 }, Arterial with { TransitDelayNs = long.MaxValue },
            Arterial with { EjectionDurationNs = 0 }, Arterial with { EjectionDurationNs = 800_000_001 },
            Arterial with { TimeConstantNs = 99_999_999 }, Arterial with { TimeConstantNs = 10_000_000_001 },
            Arterial with { InitialPressureCentiMmHg = -1 }, Arterial with { InitialPressureCentiMmHg = 32768 },
            Arterial with { InitialPressureCentiMmHg = 1199 }, Arterial with { AsymptoticPressureCentiMmHg = -1 },
            Arterial with { EjectionEquilibriumCentiMmHg = -1 }, Arterial with { EjectionEquilibriumCentiMmHg = 31568 },
            Arterial with { ModelId = "VascularPressureRcIllustration@2" }, Arterial with { ModelId = null! },
        })
        {
            RejectPlan(() => invalid.CreateChannel(Physiology, Abp, 0), "VascularPressure.InvalidPlan");
        }
        var fast = Physiology with
        {
            HeartPeriodNs = 100_000,
            VentricularElectricalOffsetNs = 10_000,
            AtrialMechanicalOffsetNs = 5000,
            VentricularMechanicalOffsetNs = 20_000
        };
        RejectPlan(() => VascularPressureSource.Create(fast, Arterial with { EjectionDurationNs = 100_000 }), "VascularPressure.InvalidPlan");
        var maximum = Arterial with
        {
            InitialPressureCentiMmHg = 32767,
            AsymptoticPressureCentiMmHg = 0,
            EjectionEquilibriumCentiMmHg = 32767,
            EjectionDurationNs = 800_000_000
        };
        Check.That(VascularPressureSource.Create(Physiology, maximum).EvaluateAt(0) == 32767 * Q,
            "maximum absolute raw pressure and nonoverlap boundary are valid");
        Check.That(VascularPressureSource.Create(Physiology, Arterial with
        {
            InitialPressureCentiMmHg = 1200,
            EjectionEquilibriumCentiMmHg = 0
        }).EvaluateAt(2_000_000_000) == 1200 * Q,
            "zero ejection strength and equilibrium initial pressure produce the configured constant pressure");
        RejectPlan(() => VascularPressureSource.Create(Physiology with { EpochAnchorSimTimeNs = 17 }, Arterial).EvaluateAt(16),
            "VascularPressure.InvalidTime");
        RejectSignal(() => PhysiologySignalGenerator.Start(Physiology, "AcqPressure125@1", 1, []));
        RejectSignal(() => PhysiologySignalGenerator.Start(Physiology, "AcqPressure125@1", 1,
            TextbookEcgReference.CreateBands(), Arterial));
        var pressureState = Start(Physiology, Arterial).CaptureState();
        RejectSignal(() => PhysiologySignalGenerator.Restore(pressureState with
        {
            VascularPressure = Arterial with { ModelId = "unknown" },
        }));
        var dense = Physiology with
        {
            HeartPeriodNs = 2_000_000,
            VentricularElectricalOffsetNs = 400_000,
            AtrialMechanicalOffsetNs = 200_000,
            VentricularMechanicalOffsetNs = 600_000
        };
        var bounded = Arterial with { EjectionDurationNs = 1, TimeConstantNs = 127_999_999 };
        Check.That(VascularPressureSource.Create(dense, bounded).EvaluateAt(8_300_000_000) >= 1200 * Q,
            "the maximum 4096 retained mechanical inputs can be evaluated without replaying earlier history");
        RejectPlan(() => VascularPressureSource.Create(dense, bounded with { TimeConstantNs = 128_000_000 }),
            "VascularPressure.InvalidPlan");
        Check.That(VascularPressureSource.Create(Physiology with { EpochAnchorSimTimeNs = long.MaxValue - 10 }, Arterial)
            .EvaluateAt(long.MaxValue) == Arterial.InitialPressureCentiMmHg * Q,
            "a delayed source near the final representable time holds its initial pressure without overflowing event endpoints");
        var legacy = new ArterialPulsePlan(80_000_000, 600_000_000, 80, 40).CreateChannel(Physiology, Abp, 0);
        var json = JsonSerializer.SerializeToNode(legacy)!.AsObject();
        json.Remove(nameof(PhysiologyWaveformChannelPlan.VascularPressure));
        var oldChannel = json.Deserialize<PhysiologyWaveformChannelPlan>()!;
        Check.That(oldChannel.VascularPressure is null && legacy.VascularPressure is null,
            "old JSON and legacy pulse plans retain the absent reservoir default");
        var oldSource = PhysiologySignalGenerator.Start(Physiology, oldChannel.Plane.ProfileId, 1, oldChannel.Bands);
        var checkpoint = JsonSerializer.SerializeToNode(oldSource.CaptureState())!.AsObject();
        checkpoint.Remove(nameof(PhysiologySignalState.VascularPressure));
        var restored = PhysiologySignalGenerator.Restore(checkpoint.Deserialize<PhysiologySignalState>()!);
        Check.That(oldSource.GenerateBefore(1_600_000_000, 200, 100).SequenceEqual(restored.GenerateBefore(1_600_000_000, 200, 100)),
            "pre-reservoir source checkpoints preserve legacy event-band samples exactly");
    }

    private static void VascularCancellationAndLatePublicationFailureAreAtomic()
    {
        var generator = Start(Interrupted, Arterial);
        var group = Group(Interrupted, Pulmonary);
        string sourceBefore = Snapshot(generator);
        string groupBefore = Snapshot(group);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Cancelled(() => VascularPressureSource.Create(Interrupted, Arterial).EvaluateAt(4_000_000_000, cancellation.Token));
        Cancelled(() => generator.GenerateBefore(4_000_000_000, 500, 100, cancellation.Token));
        Cancelled(() => group.AdvanceTo(4_000_000_000, 1000, 20, 100, cancellation.Token));
        RejectGroup(() => group.AdvanceTo(4_000_000_000, 1000, 1, 100), "PhysiologyGroup.BlockLimitExceeded");
        Check.That(sourceBefore == Snapshot(generator) && groupBefore == Snapshot(group),
            "cancellation and late output capacity failure preserve all source, delay and assembler frontiers");
        Check.That(group.AdvanceTo(4_000_000_000, 1000, 20, 100).Count == 19,
            "the same pressure generation remains available after rejected publication");
    }

    // Floating point is confined to this independent continuous-time test oracle.
    private static double Reference(RegularPhysiologyPlan physiology, VascularPressurePlan pressure,
        long time, IReadOnlyList<long> mechanicalTimes)
    {
        double elapsed = time - (double)physiology.EpochAnchorSimTimeNs - pressure.TransitDelayNs;
        if (elapsed <= 0) { return pressure.InitialPressureCentiMmHg; }
        double result = pressure.AsymptoticPressureCentiMmHg +
            (pressure.InitialPressureCentiMmHg - pressure.AsymptoticPressureCentiMmHg) * Math.Exp(-elapsed / pressure.TimeConstantNs);
        foreach (long mechanicalTime in mechanicalTimes)
        {
            double age = time - (double)mechanicalTime - pressure.TransitDelayNs;
            if (age <= 0) { continue; }
            result += pressure.EjectionEquilibriumCentiMmHg *
                (Math.Exp(-Math.Max(0, age - pressure.EjectionDurationNs) / pressure.TimeConstantNs) - Math.Exp(-age / pressure.TimeConstantNs));
        }
        return result;
    }

    private static long[] MechanicalTimes(RegularPhysiologyPlan physiology, long until) =>
        RegularPhysiologyTimeline.Start(physiology).AdvanceBefore(until, 100)
            .Where(item => item.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(item => item.SimTimeNs).ToArray();
    private static PhysiologySignalGenerator Start(RegularPhysiologyPlan physiology, VascularPressurePlan pressure,
        string profile = "AcqPressure125@1") => PhysiologySignalGenerator.Start(physiology, profile, 1, [], pressure);
    private static PhysiologyWaveformGroup Group(RegularPhysiologyPlan physiology, VascularPressurePlan pulmonary) =>
        PhysiologyWaveformGroup.Start(Ecg, Abp, 1, 1, 1, 0, 32,
        [new(physiology, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
         Arterial.CreateChannel(physiology, Abp, 7), pulmonary.CreateChannel(physiology, Pa, 3)]);
    private static short[] Samples(IReadOnlyList<byte[]> blocks, Guid channel) => blocks.SelectMany(bytes =>
        WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == channel).Samples).ToArray();
    private static string Snapshot(PhysiologySignalGenerator source) => JsonSerializer.Serialize(source.CaptureState());
    private static string Snapshot(PhysiologyWaveformGroup source) => JsonSerializer.Serialize(source.CaptureState());
    private static void RejectPlan(Action action, string reason)
    {
        bool rejected = false;
        try { action(); }
        catch (EventWaveformException exception) { rejected = exception.ReasonCode == reason; }
        Check.That(rejected, reason);
    }
    private static void RejectSignal(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (PhysiologySignalException exception) { rejected = exception.ReasonCode == "PhysiologySignal.InvalidCheckpoint"; }
        Check.That(rejected, "ambiguous or missing pressure source rejects as PhysiologySignal.InvalidCheckpoint");
    }
    private static void RejectGroup(Action action, string reason)
    {
        bool rejected = false;
        try { action(); }
        catch (PhysiologyWaveformGroupException exception) { rejected = exception.ReasonCode == reason; }
        Check.That(rejected, reason);
    }
    private static void Cancelled(Action action)
    {
        bool cancelled = false;
        try { action(); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled, "cancelled pressure evaluation must not publish a result");
    }
}
