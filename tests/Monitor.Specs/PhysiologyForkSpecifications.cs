// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PhysiologyForkSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(OwnedForkMatchesValidatedRestoreAcrossFrames), OwnedForkMatchesValidatedRestoreAcrossFrames),
        new(nameof(OwnedForksDoNotShareMutableProgress), OwnedForksDoNotShareMutableProgress),
        new(nameof(FailedForkAdvancePreservesBothOwners), FailedForkAdvancePreservesBothOwners),
    ];

    private static PhysiologyWaveformGroup Source(bool morphology)
    {
        var ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var pleth = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var pressure = Guid.Parse("33333333-3333-4333-8333-333333333333");
        RegularPhysiologyPlan plan = new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000,
            VentricularMechanicalEnabled: false, MechanicalAfterCycles: 2, MechanicalDurationCycles: 3);
        return PhysiologyWaveformGroup.Start(ecg, pleth, 1, 1, 1, 0, 32,
            [new(plan, new(ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
             new(plan, new(pleth, "AcqPleth125@1", 1, 1, 0, 1), new PlethPulsePlan(80_000_000, 512_000_000, 1000).CreateBands(), 250, 0),
             new VascularPressurePlan(80_000_001, 240_000_000, 2_900_000_000, 8000, 1000, 30000,
                 Morphology: morphology ? new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000) : null).CreateChannel(plan, pressure, 7)]);
    }

    private static void OwnedForkMatchesValidatedRestoreAcrossFrames()
    {
        foreach (bool morphology in new[] { false, true })
        {
            var reference = Source(morphology);
            var fast = Source(morphology);
            List<byte[]> expected = [], actual = [];
            for (int step = 1; step <= 100; step++)
            {
                reference = PhysiologyWaveformGroup.Restore(reference.CaptureState());
                expected.AddRange(reference.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            }
            for (int frame = 1; frame <= 1250; frame++)
            {
                fast = fast.Fork();
                actual.AddRange(fast.AdvanceTo(frame * 16_000_000L, 50, 1, 100));
            }
            Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)) &&
                Snapshot(reference) == Snapshot(fast), "20s trusted frame copies preserve full restore bytes, pending samples, fractional transit and pressure recovery");
        }
    }

    private static void OwnedForksDoNotShareMutableProgress()
    {
        var parent = Source(true);
        parent.AdvanceTo(2_400_000_000, 600, 12, 100);
        string before = Snapshot(parent);
        var first = parent.Fork();
        var second = parent.Fork();
        var expected = first.AdvanceTo(2_800_000_000, 100, 2, 100);
        Check.That(Snapshot(parent) == before && Snapshot(second) == before, "advancing one fork leaves parent and sibling pending buffers and clocks unchanged");
        var actual = second.AdvanceTo(2_800_000_000, 100, 2, 100);
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)), "independent forks publish identical blocks");
    }

    private static void FailedForkAdvancePreservesBothOwners()
    {
        var parent = Source(true);
        var fork = parent.Fork();
        string before = Snapshot(parent);
        bool failed = false;
        try { fork.AdvanceTo(4_000_000_000, 1000, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { failed = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(failed && Snapshot(parent) == before && Snapshot(fork) == before, "late publication failure remains atomic on trusted transaction copies");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { fork.AdvanceTo(200_000_000, 50, 1, 100, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && Snapshot(parent) == before && Snapshot(fork) == before, "cancelled fork cannot advance shared pressure or pending state");
    }

    private static string Snapshot(PhysiologyWaveformGroup group) => JsonSerializer.Serialize(group.CaptureState());
}
