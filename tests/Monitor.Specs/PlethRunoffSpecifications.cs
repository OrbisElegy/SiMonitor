// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PlethRunoffSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static PlethRunoffPlan Pulse => new(80_000_000, 512_000_000, 1000);
    private static readonly Guid Id = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static Specification[] All =>
    [
        new(nameof(PlethRunoffPreservesShoulderAndDecaysAcrossLongRr), PlethRunoffPreservesShoulderAndDecaysAcrossLongRr),
        new(nameof(PlethRunoffSuperposesOnlyEffectiveMechanicalEvents), PlethRunoffSuperposesOnlyEffectiveMechanicalEvents),
        new(nameof(PlethRunoffRestoresPendingSamplesAndRejectsAtomically), PlethRunoffRestoresPendingSamplesAndRejectsAtomically),
        new(nameof(PlethRunoffHistoryAndCostStayBounded), PlethRunoffHistoryAndCostStayBounded),
    ];
    private static void PlethRunoffPreservesShoulderAndDecaysAcrossLongRr()
    {
        var isolated = Plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 };
        foreach (bool notch in new[] { false, true })
        {
            var source = PlethRunoffSource.Create(isolated, Pulse with { IncludeNotch = notch });
            var old = EventWaveformComposition.Restore(new(new PlethPulsePlan(80_000_000, 512_000_000, 1000, notch).CreateBands(),
                [new(240_000_000, PhysiologyCycleEventKind.VentricularMechanical, 0)]));
            long join = 320_000_000 + 512_000_000L * (notch ? 72 : 88) / 128;
            for (long t = 0; t <= join; t += 1_000_000)
            { Check.That(source.EvaluateAt(t) == old.EvaluateAt(t), $"preserve rise, peak and shoulder before smooth runoff join: notch={notch}, t={t}, actual={source.EvaluateAt(t)}, old={old.EvaluateAt(t)}"); }
            long previous = source.EvaluateAt(join);
            for (long t = join + 1_000_000; t <= 8_000_000_000; t += 1_000_000)
            {
                long actual = source.EvaluateAt(t);
                double u = (t - join) / 400_000_000.0;
                double expected = (double)source.EvaluateAt(join) * (1 + u) * Math.Exp(-u);
                Check.That(actual <= previous && Math.Abs(actual - expected) < 100,
                    "monotone fixed-point runoff agrees with independent smooth exponential reference");
                previous = actual;
            }
            Check.That(source.EvaluateAt(832_000_000) > 0 && source.EvaluateAt(1_600_000_000) > 0 &&
                source.EvaluateAt(30_000_000_000) == 0, "old support does not truncate the tail; very long absence eventually reaches baseline");
        }
        foreach (int ratio in new[] { 2, 4 })
        {
            var source = PlethRunoffSource.Create(Plan with { VentricularConductionRatio = ratio }, Pulse);
            long arrival = 320_000_000 + ratio * 800_000_000L;
            Check.That(source.EvaluateAt(arrival - 8_000_000) > source.EvaluateAt(arrival) &&
                source.EvaluateAt(arrival + 160_000_000) > source.EvaluateAt(arrival), "long RR decays then receives next pulse");
        }
    }
    private static void PlethRunoffSuperposesOnlyEffectiveMechanicalEvents()
    {
        var source = PlethRunoffSource.Create(Plan, Pulse);
        var single = PlethRunoffSource.Create(Plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 }, Pulse);
        for (long t = 0; t < 6_000_000_000; t += 8_000_000)
        {
            long expected = 0;
            for (long shift = 0; shift <= t; shift += 800_000_000) { expected += single.EvaluateAt(t - shift); }
            Check.That(source.EvaluateAt(t) == expected, "old tails and new ejection superpose without resetting baseline");
        }
        var none = PlethRunoffSource.Create(Plan with { VentricularMechanicalEnabled = false }, Pulse);
        Check.That(none.EvaluateAt(4_000_000_000) == 0, "electrical activity alone cannot synthesize Pleth");
        var ectopicPlan = Plan with { ConductionPattern = AvConductionPattern.ShortCoupledRonTPvcIllustration };
        var ectopic = PlethRunoffSource.Create(ectopicPlan, Pulse with { AmplitudeCounts = 1250, UsePrematureBeatPerfusion = true });
        for (long t = 2_100_000_000; t < 3_360_000_000; t += 8_000_000)
        { Check.That(ectopic.EvaluateAt(t) >= ectopic.EvaluateAt(t + 1_000_000), "zero-gain short PVC does not erase or restart preceding runoff"); }
    }
    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Id, Id, 1, 1, 1, 0, 32,
        [new(Plan, new(Id, "AcqPleth125@1", 1, 1, 0, 1), [], 250, 0, PlethRunoff: Pulse)]);
    private static void PlethRunoffRestoresPendingSamplesAndRejectsAtomically()
    {
        var expected = Group().AdvanceTo(4_000_000_000, 500, 20, 100);
        var group = Group();
        List<byte[]> actual = [];
        for (int i = 1; i <= 20; i++)
        {
            actual.AddRange(group.AdvanceTo(i * 200_000_000L, 25, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(p => p.First.SequenceEqual(p.Second)), "pending125Hz samples and wire bytes survive restore");
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool rejected = false;
        try { group.AdvanceTo(6_000_000_000, 250, 1, 100); }
        catch (PhysiologyWaveformGroupException) { rejected = true; }
        Check.That(rejected && before == JsonSerializer.Serialize(group.CaptureState()), "failed block budget preserves all cursors");
        var state = group.CaptureState();
        rejected = false;
        try { PhysiologyWaveformGroup.Restore(state with { Channels = state.Channels.Select(c => c with { Generator = c.Generator with { PlethRunoff = Pulse with { AmplitudeCounts = 900 } } }).ToArray() }); }
        catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "changing source while retaining pending samples rejects");
        foreach (var invalid in new[] { Pulse with { TailTimeConstantNs = 99_999_999 }, Pulse with { TailTimeConstantNs = 2_000_000_001 },
            Pulse with { PulseDurationNs = 0 }, Pulse with { PulseDurationNs = long.MaxValue }, Pulse with { TransitDelayNs = -1 },
            Pulse with { AmplitudeCounts = 32767 }, Pulse with { UsePrematureBeatPerfusion = true }, Pulse with { ModelId = "unknown" } })
        {
            rejected = false;
            try { PlethRunoffSource.Create(Plan, invalid); } catch (EventWaveformException) { rejected = true; }
            Check.That(rejected, "invalid parameters and amplitude bounds fail closed");
        }
        rejected = false;
        try { PhysiologySignalGenerator.Start(Plan, "AcqPleth125@1", 1, new PlethPulsePlan(0, 512_000_000, 1000).CreateBands(), plethRunoff: Pulse); }
        catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "runoff cannot double count legacy bands");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        rejected = false;
        try { PlethRunoffSource.Create(Plan, Pulse).EvaluateAt(1_000_000_000, cancellation.Token); }
        catch (OperationCanceledException) { rejected = true; }
        Check.That(rejected, "cancelled bounded replay stops");
    }
    private static void PlethRunoffHistoryAndCostStayBounded()
    {
        foreach (long tau in new[] { 100_000_000L, 2_000_000_000L })
        {
            var isolated = PlethRunoffSource.Create(Plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 }, Pulse with { TailTimeConstantNs = tau });
            Check.That(isolated.EvaluateAt(672_000_000) > 0 && isolated.EvaluateAt(672_000_000 + 64 * tau) == 0,
                "inclusive tau bounds retain shoulder and terminate at deterministic horizon");
        }
        var source = PlethRunoffSource.Create(Plan, Pulse);
        Check.That(source.MaximumHistoryEvents == 33 && source.SupportNs == 25_952_000_000, "fixed history bound is independent of runtime");
        foreach (long origin in new[] { 40_000_000_000L, 3_600_000_000_000L, long.MaxValue - 16_000_000_000 })
        {
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                long t = origin + i * 8_000_000L;
                long samePhase = 40_000_000_000 + t % 800_000_000;
                Check.That(source.EvaluateAt(t) == source.EvaluateAt(samePhase), "late indexed query equals bounded steady-state phase");
            }
            Console.WriteLine($"Pleth bounded replay: origin={origin}ns, 2000 evaluations={watch.Elapsed.TotalMilliseconds:F1}ms, max events={source.MaximumHistoryEvents}");
        }
    }
}
