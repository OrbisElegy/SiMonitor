// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AnchoredFlowSpecifications
{
    private static RegularPhysiologyPlan Physiology => new(17, 800_000_000, 160_000_000,
        80_000_000, 240_333_333, 4_000_000_000, 2_000_000_000);
    private static VascularPressurePlan Pressure => new(3_777_777, 197_123_457,
        1_500_000_000, 8000, 1000, 26000);
    private static PlethRunoffPlan Pleth => new(3_777_777, 512_000_003, 1000);

    internal static Specification[] All =>
    [
        new(nameof(AnchoredFlowMatchesColdRandomAndConcurrentReads), AnchoredFlowMatchesColdRandomAndConcurrentReads),
        new(nameof(AnchoredFlowPreservesTransitAndSamplingProfiles), AnchoredFlowPreservesTransitAndSamplingProfiles),
        new(nameof(AnchoredFlowRetainsIndependentContinuousReferenceAccuracy), AnchoredFlowRetainsIndependentContinuousReferenceAccuracy),
        new(nameof(AnchoredFlowContinuationAndRejectionPreserveExactState), AnchoredFlowContinuationAndRejectionPreserveExactState),
        new(nameof(AnchoredFlowRejectsPreviousNumericModelVersions), AnchoredFlowRejectsPreviousNumericModelVersions),
        new(nameof(AnchoredFlowSeparatesMaximumHistoryFromFutureEdges), AnchoredFlowSeparatesMaximumHistoryFromFutureEdges),
    ];

    private static void AnchoredFlowMatchesColdRandomAndConcurrentReads()
    {
        var shaped = Pressure with { Morphology = new(VascularPressureMorphologyKind.Arterial, 640_000_000, 4000) };
        var targets = shaped with
        {
            TargetCurves = new(new(12000, [new(1_333_333_337, 2_222_222_223, 14000)]),
                new(8000, [new(1_555_555_557, 1_111_111_113, 9000)]))
        };
        foreach (var plan in new[] { Pressure, shaped, targets })
        {
            var sequential = VascularPressureSource.Create(Physiology, plan);
            var random = VascularPressureSource.Create(Physiology, plan);
            long[] times = Enumerable.Range(0, 400).Select(index => Physiology.EpochAnchorSimTimeNs + index * 17_111_113L)
                .Concat(new[] { 1_003_777_793L, 1_003_777_794L, 1_003_777_795L, long.MaxValue - 1, long.MaxValue }).ToArray();
            long[] expected = times.Select(time => sequential.EvaluateAt(time)).ToArray();
            foreach (int index in Enumerable.Range(0, times.Length).OrderBy(index => index * 313 % times.Length))
            { Check.That(random.EvaluateAt(times[index]) == expected[index], "cold and evicted pressure blocks reconstruct the same source-time trajectory"); }
            Parallel.For(0, times.Length, index => Check.That(random.EvaluateAt(times[index]) == expected[index],
                "shared pressure cache only publishes complete immutable blocks"));
        }
        foreach (bool notch in new[] { false, true })
        {
            var plan = Pleth with { IncludeNotch = notch };
            var source = PlethRunoffSource.Create(Physiology, plan);
            var random = PlethRunoffSource.Create(Physiology, plan);
            long[] times = Enumerable.Range(0, 401).Select(index => Physiology.EpochAnchorSimTimeNs + index * 19_111_117L)
                .Concat(new[] { long.MaxValue - 1, long.MaxValue }).ToArray();
            long[] expected = times.Select(time => source.EvaluateAt(time)).ToArray();
            Parallel.For(0, times.Length, index => Check.That(random.EvaluateAt(times[index]) == expected[index],
                "Pleth random access and concurrent cache eviction retain exact Q32 values"));
        }
    }

    private static void AnchoredFlowPreservesTransitAndSamplingProfiles()
    {
        var pressure = VascularPressureSource.Create(Physiology, Pressure);
        var undelayedPressure = VascularPressureSource.Create(Physiology, Pressure with { TransitDelayNs = 0 });
        var pleth = PlethRunoffSource.Create(Physiology, Pleth);
        var undelayedPleth = PlethRunoffSource.Create(Physiology, Pleth with { TransitDelayNs = 0 });
        for (long time = Physiology.EpochAnchorSimTimeNs; time < 6_000_000_000; time += 7_111_113)
        {
            Check.That(pressure.EvaluateAt(time + Pressure.TransitDelayNs) == undelayedPressure.EvaluateAt(time) &&
                pleth.EvaluateAt(time + Pleth.TransitDelayNs) == undelayedPleth.EvaluateAt(time),
                "off-grid transit shifts canonical source time exactly");
        }
        foreach (bool optical in new[] { false, true })
        {
            var normal = Start(optical);
            var faster = PhysiologySignalGenerator.Start(Physiology, "AcqECGMonitor250@1", 1, [],
                optical ? null : Pressure, optical ? Pleth : null);
            var samples = normal.GenerateBefore(4_000_000_017, 500, 100);
            var dense = faster.GenerateBefore(4_000_000_017, 1000, 100);
            Check.That(samples.Select(sample => sample.ValueQ32).SequenceEqual(
                dense.Where(sample => sample.Tick.SampleIndex % 2 == 0).Select(sample => sample.ValueQ32)),
                "different acquisition rates select identical shared timestamps");
            Check.That(samples.All(sample => sample.ValueQ32 == (optical
                ? pleth.EvaluateAt(sample.Tick.SimTimeNs) : pressure.EvaluateAt(sample.Tick.SimTimeNs))),
                "generator and scalar evaluation use one numeric model");
        }
    }

    private static void AnchoredFlowRetainsIndependentContinuousReferenceAccuracy()
    {
        foreach (long tau in new[] { 100_000_000L, 1_500_000_000L, 10_000_000_000L })
        {
            var plan = Pressure with { TimeConstantNs = tau };
            var source = VascularPressureSource.Create(Physiology, plan);
            for (long time = Physiology.EpochAnchorSimTimeNs; time < 5_000_000_000; time += 11_111_119)
            {
                double sourceTime = Math.Max(0, time - plan.TransitDelayNs - Physiology.EpochAnchorSimTimeNs);
                double expected = plan.AsymptoticPressureCentiMmHg +
                    (plan.InitialPressureCentiMmHg - plan.AsymptoticPressureCentiMmHg) * Math.Exp(-sourceTime / tau);
                for (long beat = Physiology.VentricularMechanicalOffsetNs; beat <= sourceTime; beat += Physiology.HeartPeriodNs)
                {
                    double age = sourceTime - beat;
                    expected += plan.EjectionEquilibriumCentiMmHg * (age < plan.EjectionDurationNs
                        ? 1 - Math.Exp(-age / tau)
                        : Math.Exp(-(age - plan.EjectionDurationNs) / tau) - Math.Exp(-age / tau));
                }
                double actual = (double)source.EvaluateAt(time) / FixedPointMath.Q32One;
                Check.That(Math.Abs(actual - expected) < 0.0001,
                    "anchored RC remains within 0.000001 mmHg of independent continuous rectangular-input solution");
            }
        }
    }

    private static void AnchoredFlowContinuationAndRejectionPreserveExactState()
    {
        foreach (bool optical in new[] { false, true })
        {
            var generator = Start(optical);
            _ = generator.GenerateBefore(1_333_333_337, 200, 100);
            var changed = PhysiologySignalGenerator.Start(Physiology with { MechanicalEveryCycles = 2 },
                optical ? "AcqPleth125@1" : "AcqPressure125@1", 1, [],
                optical ? null : Pressure with { EjectionEquilibriumCentiMmHg = 22000 },
                optical ? Pleth with { IncludeNotch = true, TailTimeConstantNs = 700_000_000 } : null);
            generator = generator.ContinueWith(changed.CaptureState());
            var whole = PhysiologySignalGenerator.Restore(generator.CaptureState());
            var expected = whole.GenerateBefore(5_000_000_017, 700, 100);
            var actual = new List<PhysiologySignalSample>();
            for (long end = 1_500_000_017; end <= 5_000_000_017; end += 250_000_000)
            {
                string before = JsonSerializer.Serialize(generator.CaptureState());
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                bool rejected = false;
                try { _ = generator.GenerateBefore(end, 700, 100, cancellation.Token); }
                catch (OperationCanceledException) { rejected = true; }
                Check.That(rejected && JsonSerializer.Serialize(generator.CaptureState()) == before,
                    "cancelled continuation leaves checkpoint state unchanged");
                var fork = generator.Fork();
                var samples = generator.GenerateBefore(end, 700, 100);
                Check.That(samples.SequenceEqual(fork.GenerateBefore(end, 700, 100)),
                    "forks may share cached anchors without sharing integration progress");
                actual.AddRange(samples);
                generator = PhysiologySignalGenerator.Restore(generator.CaptureState());
            }
            Check.That(expected.SequenceEqual(actual), "continuation tails and new events survive uneven batches and every-block restore exactly");
            foreach (var sample in expected)
            { Check.That(whole.EvaluateAt(sample.Tick.SimTimeNs) == sample.ValueQ32, "continued scalar and batch queries agree exactly"); }
        }
    }

    private static void AnchoredFlowSeparatesMaximumHistoryFromFutureEdges()
    {
        var physiology = new RegularPhysiologyPlan(0, 1_600_000, 160_000, 80_000, 240_000,
            4_000_000_000, 2_000_000_000);
        var pressurePlan = new VascularPressurePlan(0, 1000, 100_000_000, 0, 0, 1000);
        var plethPlan = new PlethRunoffPlan(0, 1_000_000, 1, 100_000_000);
        var pressure = VascularPressureSource.Create(physiology, pressurePlan);
        var pleth = PlethRunoffSource.Create(physiology, plethPlan);
        Check.That(pleth.MaximumHistoryEvents > 4000,
            "accepted dense schedule approaches the per-query history bound");
        foreach (long time in new[] { 8_000_000_000L, 8_999_999_999, 9_000_000_000 })
        {
            Check.That(pressure.EvaluateAt(time) > 0 && pleth.EvaluateAt(time) > 0,
                "full historical replay plus forward anchor edges does not exceed the per-window event budget");
            Check.That(pressure.EvaluateAt(time) == VascularPressureSource.Create(physiology, pressurePlan).EvaluateAt(time) &&
                pleth.EvaluateAt(time) == PlethRunoffSource.Create(physiology, plethPlan).EvaluateAt(time),
                "dense recurrence remains independent of cache reuse");
        }
    }

    private static void AnchoredFlowRejectsPreviousNumericModelVersions()
    {
        bool pressureRejected = false;
        bool plethRejected = false;
        try { _ = VascularPressureSource.Create(Physiology, Pressure with { ModelId = "VascularPressureRcIllustration@1" }); }
        catch (EventWaveformException) { pressureRejected = true; }
        try { _ = PlethRunoffSource.Create(Physiology, Pleth with { ModelId = "PlethSmoothRunoff@1" }); }
        catch (EventWaveformException) { plethRejected = true; }
        Check.That(pressureRejected && plethRejected, "previous checkpoints cannot silently select the revised recurrence arithmetic");
    }

    private static PhysiologySignalGenerator Start(bool optical) => PhysiologySignalGenerator.Start(Physiology,
        optical ? "AcqPleth125@1" : "AcqPressure125@1", 1, [], optical ? null : Pressure, optical ? Pleth : null);
}
