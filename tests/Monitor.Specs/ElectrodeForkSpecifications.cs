// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ElectrodeForkSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ElectrodeForkMatchesValidatedRestoreForTwentySeconds), ElectrodeForkMatchesValidatedRestoreForTwentySeconds),
        new(nameof(ElectrodeForksIsolateMutableProgressAndFailures), ElectrodeForksIsolateMutableProgressAndFailures),
        new(nameof(ElectrodeForkRetainsExternalCheckpointValidation), ElectrodeForkRetainsExternalCheckpointValidation),
    ];

    private static ElectrodeWaveformGroup Source(bool rich)
    {
        var plan = new RegularPhysiologyPlan(0, 800_000_000, 900_000_000, 80_000_000, 980_000_000, 4_000_000_000, 2_000_000_000,
            IndependentVentricularPeriodNs: 1_100_000_000);
        var electrodes = rich ? TextbookElectrodeReference.CreateElectrodes(new(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]),
            tWave: new([1000, 1000, 1000, 1000, -1000, 0, 1250, 1000, 1000, 1000]), tShape: new(500),
            stSegment: new([0, 0, 0, 0, 200, 200, 200, 200, 200, 200], [0, 0, 0, 0, -100, -100, -100, -100, -100, -100])) : TextbookElectrodeReference.CreateElectrodes();
        var channels = Enum.GetValues<EcgLead>().Select(lead => new ElectrodeChannelPlan(lead,
            Guid.Parse($"00000000-0000-4000-8000-{(int)lead + 1:D12}"), 10, 0)).ToArray();
        return ElectrodeWaveformGroup.Start(channels[0].ChannelId, channels[1].ChannelId, 1, 1, 1, 0, 16, plan, electrodes, channels, EcgLimbPlacement.SwapRaLa);
    }

    private static string Snapshot(ElectrodeWaveformGroup group) => JsonSerializer.Serialize(group.CaptureState());

    private static void ElectrodeForkMatchesValidatedRestoreForTwentySeconds()
    {
        foreach (bool rich in new[] { false, true })
        {
            var reference = Source(rich);
            var actual = Source(rich);
            List<byte[]> expected = [], observed = [];
            for (int step = 1; step <= 100; step++)
            {
                reference = ElectrodeWaveformGroup.Restore(reference.CaptureState());
                expected.AddRange(reference.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            }
            for (int frame = 1; frame <= 1250; frame++)
            {
                actual = actual.Fork();
                observed.AddRange(actual.AdvanceTo(frame * 16_000_000L, 50, 1, 100));
            }
            Check.That(expected.Count == 99 && expected.Count == observed.Count && expected.Zip(observed).All(pair => pair.First.SequenceEqual(pair.Second)) &&
                Snapshot(reference) == Snapshot(actual), "trusted frame copies preserve20s bytes, pending values, wiring, independent phase and all optional morphology");
        }
    }

    private static void ElectrodeForksIsolateMutableProgressAndFailures()
    {
        var parent = Source(true);
        parent.AdvanceTo(1_001_000_000, 251, 6, 100);
        var first = parent.Fork();
        var second = parent.Fork();
        string before = Snapshot(parent);
        var expected = first.AdvanceTo(1_600_000_000, 150, 3, 100);
        Check.That(Snapshot(parent) == before && Snapshot(second) == before, "advancing a fork cannot change its parent's or sibling's pending buffers");
        var actual = second.AdvanceTo(1_600_000_000, 150, 3, 100);
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)), "independent forks publish identical blocks");
        var failed = parent.Fork();
        bool rejected = false;
        try { failed.AdvanceTo(2_800_000_000, 450, 1, 100); }
        catch (ElectrodeWaveformGroupException e) { rejected = e.ReasonCode == "ElectrodeGroup.BlockLimitExceeded"; }
        Check.That(rejected && Snapshot(parent) == before && Snapshot(failed) == before, "late publication failure leaves parent and fork unchanged");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool stopped = false;
        try { failed.AdvanceTo(1_600_000_000, 150, 3, 100, cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Check.That(stopped && Snapshot(failed) == before, "cancelled fork advancement retains source progress");
    }

    private static void ElectrodeForkRetainsExternalCheckpointValidation()
    {
        var source = Source(true);
        source.AdvanceTo(1_001_000_000, 251, 6, 100);
        var state = source.CaptureState();
        var altered = state.Generator.Electrodes.Select(item => item with
        {
            Bands = item.Bands.Select(band => band with { TableQ32 = band.TableQ32.Select(_ => 0L).ToArray() }).ToArray(),
        }).ToArray();
        bool rejected = false;
        try { ElectrodeWaveformGroup.Restore(state with { Generator = state.Generator with { Electrodes = altered } }); }
        catch (ElectrodeWaveformGroupException e) { rejected = e.ReasonCode == "ElectrodeGroup.InvalidCheckpoint"; }
        Check.That(rejected, "external checkpoints still replay pending samples against electrode definitions");
        var child = source.Fork();
        var table = (IList<long>)child.CaptureState().Generator.Electrodes[4].Bands[2].TableQ32;
        bool readOnly = false;
        try { table[1] = 0; }
        catch (NotSupportedException) { readOnly = true; }
        Check.That(readOnly && Snapshot(source) == Snapshot(child), "shared electrode tables are owned and immutable");
    }
}
