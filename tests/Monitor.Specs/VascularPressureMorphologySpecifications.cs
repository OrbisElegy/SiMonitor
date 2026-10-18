// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VascularPressureMorphologySpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Abp = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Pa = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static RegularPhysiologyPlan Physiology => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    private static VascularPressurePlan Arterial => new(80_000_000, 240_000_000,
        2_900_000_000, 8000, 1000, 30000,
        Morphology: new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000));
    private static VascularPressurePlan Pulmonary => new(40_000_000, 200_000_000,
        700_000_000, 1000, 500, 5000,
        Morphology: new(VascularPressureMorphologyKind.PulmonaryArtery, 640_000_000, 1500));

    public static Specification[] All =>
    [
        new(nameof(VascularMorphologyMatchesAuthoredContoursAtSteadyState), VascularMorphologyMatchesAuthoredContoursAtSteadyState),
        new(nameof(VascularLegacyMorphologyRetainsFrozenValuesAndPendingSamples), VascularLegacyMorphologyRetainsFrozenValuesAndPendingSamples),
        new(nameof(VascularMorphologyPreservesFirstAndResumedNotches), VascularMorphologyPreservesFirstAndResumedNotches),
        new(nameof(VascularMorphologyCompletesDelayedTailAndJoinsPassiveRunoff), VascularMorphologyCompletesDelayedTailAndJoinsPassiveRunoff),
        new(nameof(VascularMorphologyKeepsConductionStrideAndSubsampleTransit), VascularMorphologyKeepsConductionStrideAndSubsampleTransit),
        new(nameof(VascularMorphologyBlocksAndRestoredPendingSamplesKeepExactBytes), VascularMorphologyBlocksAndRestoredPendingSamplesKeepExactBytes),
        new(nameof(VascularMorphologyRejectsUnsafeOrUnknownPlansAtomically), VascularMorphologyRejectsUnsafeOrUnknownPlansAtomically),
        new(nameof(VascularMorphologyDefaultsKeepOriginalSourcesUnchanged), VascularMorphologyDefaultsKeepOriginalSourcesUnchanged),
        new(nameof(VascularWeakStrokeTailsDoNotDrift), VascularWeakStrokeTailsDoNotDrift),
        new(nameof(VascularPreviousMorphologyRetainsWeakStrokeSamples), VascularPreviousMorphologyRetainsWeakStrokeSamples),
        new(nameof(VascularWeakStrokeVersionsRestorePendingSamples), VascularWeakStrokeVersionsRestorePendingSamples),
        new(nameof(VascularWeakStrokeCompletesBeforePassiveRunoff), VascularWeakStrokeCompletesBeforePassiveRunoff),
    ];

    private static void VascularMorphologyMatchesAuthoredContoursAtSteadyState()
    {
        foreach (var pressure in new[] { Arterial, Pulmonary })
        {
            var source = VascularPressureSource.Create(Physiology, pressure);
            long eventTime = Physiology.VentricularMechanicalOffsetNs + 100 * Physiology.HeartPeriodNs;
            var contour = Contour(Physiology, pressure, eventTime, 100);
            // Independent continuous-time periodic RC solution; the pulse
            // channel supplies its own table scaling and interpolation.
            double period = Physiology.HeartPeriodNs;
            double baseline = pressure.EjectionEquilibriumCentiMmHg *
                (Math.Exp(-(period - pressure.EjectionDurationNs) / pressure.TimeConstantNs) -
                 Math.Exp(-period / pressure.TimeConstantNs)) / (1 - Math.Exp(-period / pressure.TimeConstantNs));
            for (long age = 0; age <= Physiology.HeartPeriodNs; age += 2_000_000)
            {
                long time = eventTime + pressure.TransitDelayNs + age;
                double expected = pressure.AsymptoticPressureCentiMmHg + baseline + (double)contour.EvaluateAt(time) / Q;
                Check.That(Math.Abs((double)source.EvaluateAt(time) / Q - expected) < 0.02,
                    "periodic steady pressure preserves each independent authored pressure contour within 0.0002 mmHg");
            }
        }
    }

    private static void VascularLegacyMorphologyRetainsFrozenValuesAndPendingSamples()
    {
        int[] indices = [0, 44, 100, 124, 132, 200];
        long[][] frozenQ32 =
        [
            [0, 4294967296, 2851287859, 1788451226, 2111412634, 375809638],
            [0, 4234800728, 1254241527, 1464824514, 1217215947, 242980370],
        ];
        var legacy = new[] { Arterial, Pulmonary }.Select(pressure => pressure with
        {
            Morphology = pressure.Morphology! with { ModelId = VascularPressureMorphologyPlan.LegacyEvidenceId },
        }).ToArray();
        for (int channel = 0; channel < legacy.Length; channel++)
        {
            var pressure = legacy[channel];
            var source = VascularPressureSource.Create(Physiology, pressure);
            double period = Physiology.HeartPeriodNs;
            double baseline = pressure.EjectionEquilibriumCentiMmHg *
                (Math.Exp(-(period - pressure.EjectionDurationNs) / pressure.TimeConstantNs) -
                 Math.Exp(-period / pressure.TimeConstantNs)) / (1 - Math.Exp(-period / pressure.TimeConstantNs));
            for (int point = 0; point < indices.Length; point++)
            {
                long time = Physiology.VentricularMechanicalOffsetNs + 100 * Physiology.HeartPeriodNs +
                    pressure.TransitDelayNs + pressure.Morphology!.DurationNs * indices[point] / 256;
                double expected = pressure.AsymptoticPressureCentiMmHg + baseline +
                    (double)frozenQ32[channel][point] / Q * pressure.Morphology.PulseHeightCentiMmHg;
                Check.That(Math.Abs((double)source.EvaluateAt(time) / Q - expected) < 0.02,
                    "version 2 retains frozen upstream contour samples after the authored default migration");
            }
        }
        var group = PhysiologyWaveformGroup.Start(Abp, Pa, 1, 1, 1, 0, 32,
            [legacy[0].CreateChannel(Physiology, Abp, 0), legacy[1].CreateChannel(Physiology, Pa, 0)]);
        _ = group.AdvanceTo(4_100_000_000, 600, 32, 100);
        string serialized = JsonSerializer.Serialize(group.CaptureState());
        var restored = PhysiologyWaveformGroup.Restore(JsonSerializer.Deserialize<PhysiologyWaveformGroupState>(serialized)!);
        var expectedBlocks = group.AdvanceTo(4_800_000_000, 100, 8, 100);
        var restoredBlocks = restored.AdvanceTo(4_800_000_000, 100, 8, 100);
        Check.That(expectedBlocks.Count == restoredBlocks.Count && expectedBlocks.Zip(restoredBlocks)
            .All(pair => pair.First.SequenceEqual(pair.Second)),
            "legacy version 2 checkpoints preserve pending pressure samples and subsequent exact wire bytes");
    }

    private static void VascularMorphologyPreservesFirstAndResumedNotches()
    {
        var interrupted = Physiology with
        { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1, MechanicalDurationCycles = 100 };
        foreach (var pressure in new[] { Arterial, Pulmonary })
        {
            var source = VascularPressureSource.Create(interrupted, pressure);
            var raw = VascularPressureSource.Create(interrupted, pressure with { Morphology = null });
            long[] landmarks = pressure.Morphology!.Kind == VascularPressureMorphologyKind.Arterial
                ? [112, 124, 132] : [96, 108, 120];
            foreach (long cycle in new[] { 0L, 101L })
            {
                long arrival = Physiology.VentricularMechanicalOffsetNs + cycle * Physiology.HeartPeriodNs + pressure.TransitDelayNs;
                long[] times = landmarks.Select(index => arrival + index * pressure.Morphology.DurationNs / 256).ToArray();
                long before = source.EvaluateAt(times[0]), notch = source.EvaluateAt(times[1]), rebound = source.EvaluateAt(times[2]);
                Check.That(before > notch && rebound > notch + Q,
                    "both the first beat and first beat after long suppression retain a measurable original notch and rebound");
                Check.That(raw.EvaluateAt(times[2]) < raw.EvaluateAt(times[1]),
                    "the independent raw RC control has no notch rebound at the same pressure landmarks");
                Check.That(source.EvaluateAt(arrival) == raw.EvaluateAt(arrival) &&
                    Math.Abs(source.EvaluateAt(arrival + 1) - source.EvaluateAt(arrival - 1)) < Q / 100,
                    "returning morphology starts at the depleted reservoir pressure without a discontinuous reset");
            }
        }
    }

    private static void VascularMorphologyCompletesDelayedTailAndJoinsPassiveRunoff()
    {
        var stopped = Physiology with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 };
        foreach (var original in new[] { Arterial, Pulmonary })
        {
            var pressure = original with { TransitDelayNs = 2_480_000_003 };
            var source = VascularPressureSource.Create(stopped, pressure);
            var normal = VascularPressureSource.Create(Physiology, pressure);
            var raw = VascularPressureSource.Create(stopped, pressure with { Morphology = null });
            long arrival = stopped.VentricularMechanicalOffsetNs + pressure.TransitDelayNs;
            for (long time = 0; time < arrival + stopped.HeartPeriodNs; time += 7_123_457)
            {
                Check.That(source.EvaluateAt(time) == normal.EvaluateAt(time),
                    "an accepted last pulse completes its shape even when transit exceeds the mechanical suppression boundary");
            }
            foreach (long boundary in new[] { arrival + pressure.Morphology!.DurationNs, arrival + stopped.HeartPeriodNs })
            {
                Check.That(Math.Abs(source.EvaluateAt(boundary + 1) - source.EvaluateAt(boundary - 1)) < Q / 100,
                    "LUT-end and nominal-cycle-end joins do not introduce pressure jumps");
            }
            long previous = source.EvaluateAt(arrival + stopped.HeartPeriodNs);
            for (long time = arrival + stopped.HeartPeriodNs; time < arrival + 10_000_000_000; time += 8_000_000)
            {
                long next = source.EvaluateAt(time);
                Check.That(next == raw.EvaluateAt(time) && next <= previous && next >= pressure.AsymptoticPressureCentiMmHg * Q,
                    "after finite morphology support, passive pressure is exactly the old RC trajectory with no ghost notches");
                previous = next;
            }
        }
    }

    private static void VascularMorphologyKeepsConductionStrideAndSubsampleTransit()
    {
        foreach (int ratio in new[] { 1, 2, 4 })
        {
            var physiology = Physiology with { VentricularConductionRatio = ratio, MechanicalEveryCycles = 3 };
            foreach (var pressure in new[] { Arterial, Pulmonary })
            {
                var source = VascularPressureSource.Create(physiology, pressure);
                var sparseRaw = VascularPressureSource.Create(physiology, pressure with { Morphology = null });
                var shifted = VascularPressureSource.Create(physiology, pressure with { TransitDelayNs = pressure.TransitDelayNs + 3_777_777 });
                long arrival = physiology.VentricularMechanicalOffsetNs + pressure.TransitDelayNs;
                long period = physiology.HeartPeriodNs * ratio;
                Check.That(source.EvaluateAt(arrival + period) == sparseRaw.EvaluateAt(arrival + period),
                    "a dropped nominal ventricular cycle does not extend the previous morphology or compensate its pressure");
                for (long time = arrival; time < arrival + period * 4; time += 17_123_457)
                {
                    Check.That(source.EvaluateAt(time) == shifted.EvaluateAt(time + 3_777_777),
                        "fractional acquisition-sample transit changes only the source observation time");
                }
            }
        }
    }

    private static void VascularMorphologyBlocksAndRestoredPendingSamplesKeepExactBytes()
    {
        var physiology = Physiology with
        { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1, MechanicalDurationCycles = 2 };
        var expected = Group(physiology).AdvanceTo(4_800_000_000, 1000, 32, 100);
        var split = Group(physiology);
        List<byte[]> actual = [];
        for (long time = 200_000_000; time <= 4_800_000_000; time += 200_000_000)
        {
            actual.AddRange(split.AdvanceTo(time, 100, 2, 100));
            split = PhysiologyWaveformGroup.Restore(split.CaptureState());
        }
        Check.That(actual.Count == expected.Count && actual.Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)),
            "morphology, pressure state and fractional pending samples preserve canonical wire bytes across every block restore");
        _ = split.AdvanceTo(4_900_000_000, 25, 1, 100);
        var checkpoint = split.CaptureState();
        var channel = checkpoint.Channels[0];
        var changed = channel.Generator.VascularPressure!;
        bool rejected = false;
        try
        {
            _ = PhysiologyWaveformGroup.Restore(checkpoint with
            {
                Channels = checkpoint.Channels.Select(item => item.ChannelId == channel.ChannelId ? item with
                {
                    Generator = item.Generator with
                    { VascularPressure = changed with { Morphology = changed.Morphology! with { PulseHeightCentiMmHg = 2000 } } },
                } : item).ToArray(),
            });
        }
        catch (PhysiologyWaveformGroupException exception) { rejected = exception.ReasonCode == "PhysiologyGroup.InvalidCheckpoint"; }
        Check.That(rejected, "restore recomputes pending morphology instead of accepting a changed shape configuration");
    }

    private static void VascularMorphologyRejectsUnsafeOrUnknownPlansAtomically()
    {
        var shape = Arterial.Morphology!;
        foreach (var invalid in new[]
        {
            Arterial with { Morphology = shape with { Kind = (VascularPressureMorphologyKind)99 } },
            Arterial with { Morphology = shape with { ModelId = "unknown" } },
            Arterial with { Morphology = shape with { ModelId = null! } },
            Arterial with { Morphology = shape with { DurationNs = 0 } },
            Arterial with { Morphology = shape with { DurationNs = 800_000_001 } },
            Arterial with { Morphology = shape with { PulseHeightCentiMmHg = -1 } },
            Arterial with { Morphology = shape with { PulseHeightCentiMmHg = 32768 } },
            Arterial with { Morphology = shape with { PulseHeightCentiMmHg = 30000 } },
            Arterial with { EjectionEquilibriumCentiMmHg = 0 },
            Arterial with { EjectionEquilibriumCentiMmHg = 1, EjectionDurationNs = 1 },
        })
        {
            bool rejected = false;
            try { _ = VascularPressureSource.Create(Physiology, invalid); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "VascularPressure.InvalidPlan"; }
            Check.That(rejected, "invalid or unsafe morphology rejects with the stable pressure-plan reason");
        }
        var group = Group(Physiology);
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limitRejected = false;
        try { _ = group.AdvanceTo(4_000_000_000, 1000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limitRejected = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { _ = group.AdvanceTo(4_000_000_000, 1000, 32, 100, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(limitRejected && cancelled && before == JsonSerializer.Serialize(group.CaptureState()),
            "late publication failure and cancellation leave all accepted morphology source state unchanged");
    }

    private static void VascularMorphologyDefaultsKeepOriginalSourcesUnchanged()
    {
        const string oldJson = """
            {"TransitDelayNs":80000000,"EjectionDurationNs":240000000,"TimeConstantNs":2900000000,
             "InitialPressureCentiMmHg":8000,"AsymptoticPressureCentiMmHg":1000,
             "EjectionEquilibriumCentiMmHg":30000,"ModelId":"VascularPressureRcIllustration@1"}
            """;
        var oldPlan = JsonSerializer.Deserialize<VascularPressurePlan>(oldJson)!;
        Check.That(oldPlan.Morphology is null && oldPlan == Arterial with { Morphology = null },
            "old pressure plans deserialize with absent morphology and retain their original model identity");
        var raw = VascularPressureSource.Create(Physiology, oldPlan);
        var explicitNull = VascularPressureSource.Create(Physiology, Arterial with { Morphology = null });
        foreach (var pressure in new[] { Arterial, Pulmonary })
        {
            var absent = Physiology with { VentricularMechanicalEnabled = false };
            var plain = VascularPressureSource.Create(absent, pressure with { Morphology = null });
            var shaped = VascularPressureSource.Create(absent, pressure);
            for (long time = 0; time < 5_000_000_000; time += 7_777_777)
            {
                Check.That(raw.EvaluateAt(time) == explicitNull.EvaluateAt(time) && plain.EvaluateAt(time) == shaped.EvaluateAt(time),
                    "old RC plans and no-mechanical-event trajectories remain bit-identical under optional shape support");
            }
        }
    }

    private static void VascularWeakStrokeTailsDoNotDrift()
    {
        foreach (var configuration in new[]
        {
            PhysiologyIllustrationConfiguration.JunctionalEscape,
            PhysiologyIllustrationConfiguration.VentricularEscape,
            PhysiologyIllustrationConfiguration.AivrPreset,
            PhysiologyIllustrationConfiguration.Default with { SeededRate = new(30, new string('a', 64), 50) },
        })
        {
            var state = PhysiologyIllustrationSource.Create(configuration).CaptureState();
            foreach (int row in new[] { 3, 5 })
            {
                var generator = state.Channels.Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(row)).Generator;
                var physiology = generator.Timeline.Plan;
                var pressure = generator.VascularPressure!;
                var source = VascularPressureSource.Create(physiology, pressure);
                var raw = VascularPressureSource.Create(physiology, pressure with { Morphology = null });
                var beats = new List<PhysiologyCycleEvent>();
                RegularPhysiologyTimeline.VisitVentricularMechanical(physiology, 0, 70_000_000_000,
                    200, beats.Add, CancellationToken.None);
                foreach (int index in new[] { 0, 1, 2, beats.Count - 3, beats.Count - 2 })
                {
                    long arrival = beats[index].SimTimeNs + pressure.TransitDelayNs;
                    long nextArrival = beats[index + 1].SimTimeNs + pressure.TransitDelayNs;
                    int reboundIndex = row == 3 ? 132 : 120;
                    long startAge = Math.Max(pressure.EjectionDurationNs, pressure.Morphology!.DurationNs * reboundIndex / 256);
                    long previous = source.EvaluateAt(arrival + startAge);
                    for (long time = arrival + startAge + 1_000_000; time < nextArrival; time += 1_000_000)
                    {
                        long value = source.EvaluateAt(time);
                        Check.That(value <= previous + 16 && value >= pressure.AsymptoticPressureCentiMmHg * Q,
                            "after the authored notch rebound, strong and weak beats decay without an extra rise or undershoot");
                        if (time >= arrival + physiology.VentricularPeriodNs)
                        {
                            Check.That(value == raw.EvaluateAt(time),
                                "the nominal cycle endpoint rejoins the original RC history without changing ejected volume");
                        }
                        previous = value;
                    }
                    foreach (long age in new[] { pressure.EjectionDurationNs, pressure.Morphology.DurationNs })
                    {
                        Check.That(Math.Abs(source.EvaluateAt(arrival + age + 1) - source.EvaluateAt(arrival + age - 1)) < Q / 100,
                            "ejection and contour endpoints remain continuous at nanosecond resolution");
                    }
                    long notchAge = pressure.Morphology.DurationNs * (row == 3 ? 124 : 108) / 256;
                    long reboundAge = pressure.Morphology.DurationNs * reboundIndex / 256;
                    Check.That(source.EvaluateAt(arrival + reboundAge) > source.EvaluateAt(arrival + notchAge) + Q,
                        "settling the baseline during ejection preserves the authored dicrotic notch rebound");
                }
            }
        }
    }

    private static void VascularPreviousMorphologyRetainsWeakStrokeSamples()
    {
        foreach (var (configuration, expectedQ32) in new[]
        {
            (PhysiologyIllustrationConfiguration.JunctionalEscape, new long[] { 23420640371540, 23126679421443 }),
            (PhysiologyIllustrationConfiguration.VentricularEscape, new long[] { 14026057820999, 14155281595894 }),
        })
        {
            var generator = PhysiologyIllustrationSource.Create(configuration).CaptureState().Channels
                .Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(3)).Generator;
            var physiology = generator.Timeline.Plan;
            var pressure = generator.VascularPressure!;
            var legacy = VascularPressureSource.Create(physiology, pressure with
            { Morphology = pressure.Morphology! with { ModelId = VascularPressureMorphologyPlan.PreviousEvidenceId } });
            for (int index = 0; index < expectedQ32.Length; index++)
            {
                long time = physiology.VentricularMechanicalOffsetNs +
                    (30 + index) * (long)physiology.VentricularPeriodNs + pressure.TransitDelayNs + 700_000_000;
                Check.That(legacy.EvaluateAt(time) == expectedQ32[index],
                    "version 3 retains frozen weak-stroke samples instead of reinterpreting saved source definitions");
            }
        }
    }

    private static void VascularWeakStrokeVersionsRestorePendingSamples()
    {
        var physiology = CompleteAvBlockVentricularReference.CreatePlan();
        foreach (string modelId in new[] { VascularPressureMorphologyPlan.PreviousEvidenceId, VascularPressureMorphologyPlan.EvidenceId })
        {
            var pressure = Arterial with
            {
                UseCardiacFillingPerfusion = true,
                Morphology = Arterial.Morphology! with { ModelId = modelId },
            };
            var group = PhysiologyWaveformGroup.Start(Abp, Pa, 1, 1, 1, 0, 32,
                [pressure.CreateChannel(physiology, Abp, 0)]);
            _ = group.AdvanceTo(4_100_000_000, 600, 32, 100);
            var restored = PhysiologyWaveformGroup.Restore(JsonSerializer.Deserialize<PhysiologyWaveformGroupState>(
                JsonSerializer.Serialize(group.CaptureState()))!);
            var expected = group.AdvanceTo(8_800_000_000, 600, 32, 100);
            var actual = restored.AdvanceTo(8_800_000_000, 600, 32, 100);
            Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
                "both morphology versions restore pending samples and alternating weak strokes with exact wire bytes");
        }
    }

    private static void VascularWeakStrokeCompletesBeforePassiveRunoff()
    {
        var physiology = CompleteAvBlockVentricularReference.CreatePlan() with
        { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 2 };
        var pressure = Arterial with { UseCardiacFillingPerfusion = true, TransitDelayNs = 2_480_000_003 };
        var source = VascularPressureSource.Create(physiology, pressure);
        var raw = VascularPressureSource.Create(physiology, pressure with { Morphology = null });
        var previousVersion = VascularPressureSource.Create(physiology, pressure with
        { Morphology = pressure.Morphology! with { ModelId = VascularPressureMorphologyPlan.PreviousEvidenceId } });
        long arrival = physiology.VentricularMechanicalOffsetNs + (long)physiology.VentricularPeriodNs + pressure.TransitDelayNs;
        long endpoint = arrival + (long)physiology.VentricularPeriodNs;
        Check.That(source.EvaluateAt(arrival) == previousVersion.EvaluateAt(arrival) &&
            source.EvaluateAt(endpoint) == previousVersion.EvaluateAt(endpoint),
            "the shaped baseline preserves both original endpoint pressures for a weak last beat");
        long level = source.EvaluateAt(arrival + pressure.Morphology!.DurationNs);
        for (long time = arrival + pressure.Morphology.DurationNs; time < endpoint; time += 8_000_000)
        {
            Check.That(Math.Abs(source.EvaluateAt(time) - level) <= 16,
                "the completed weak pulse has no additional baseline excursion before the nominal cycle endpoint");
        }
        long previous = source.EvaluateAt(endpoint);
        for (long time = endpoint; time < endpoint + 10_000_000_000; time += 8_000_000)
        {
            long value = source.EvaluateAt(time);
            Check.That(value == raw.EvaluateAt(time) && value <= previous,
                "a missing next ejection leaves exactly the original passive runoff after the completed contour");
            previous = value;
        }
        Check.That(Math.Abs(source.EvaluateAt(endpoint + 1) - source.EvaluateAt(endpoint - 1)) < Q / 100,
            "the completed baseline adjustment rejoins passive runoff continuously");
    }

    private static EventWaveformComposition Contour(RegularPhysiologyPlan physiology,
        VascularPressurePlan pressure, long eventTime, ulong cycle)
    {
        PhysiologyWaveformChannelPlan channel = pressure.Morphology!.Kind == VascularPressureMorphologyKind.Arterial
            ? new ArterialPulsePlan(pressure.TransitDelayNs, pressure.Morphology.DurationNs, 0,
                pressure.Morphology.PulseHeightCentiMmHg / 100).CreateChannel(physiology, Abp, 0)
            : new PulmonaryArteryPulsePlan(pressure.TransitDelayNs, pressure.Morphology.DurationNs, 0,
                pressure.Morphology.PulseHeightCentiMmHg / 100).CreateChannel(physiology, Pa, 0);
        return EventWaveformComposition.Restore(new(channel.Bands,
            [new(eventTime, PhysiologyCycleEventKind.VentricularMechanical, cycle)]));
    }

    private static PhysiologyWaveformGroup Group(RegularPhysiologyPlan physiology) =>
        PhysiologyWaveformGroup.Start(Abp, Pa, 1, 1, 1, 0, 32,
            [Arterial.CreateChannel(physiology, Abp, 0), Pulmonary.CreateChannel(physiology, Pa, 0)]);
}
