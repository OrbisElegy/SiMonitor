// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ElectrodeWaveformGroupSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Session = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Instance = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static ElectrodeWaveformPlan[] Electrodes() => Enum.GetValues<EcgElectrode>()
        .Select((electrode, index) => new ElectrodeWaveformPlan(electrode,
            new EventWaveformBand[] { new(PhysiologyCycleEventKind.VentricularElectrical, index * 1_000_000, 80_000_000,
                new long[] { 0, (index + 1) * 100 * Q + 1, -index * 10 * Q, 0 }) })).ToArray();
    private static ElectrodeChannelPlan[] Channels() => Enum.GetValues<EcgLead>().Select(lead =>
        new ElectrodeChannelPlan(lead, Guid.Parse($"00000000-0000-4000-8000-{12 - (int)lead:D12}"), 10, (uint)lead + 1)).ToArray();
    private static ElectrodeWaveformGroup Start(bool reverse = false) => ElectrodeWaveformGroup.Start(
        Session, Instance, 1, 7, 1, 0, 32, Plan, Electrodes(), reverse ? Channels().Reverse().ToArray() : Channels());
    public static Specification[] All =>
    [
        new(nameof(ProjectedBlocksRespectAvailabilityAndLeadBinding), ProjectedBlocksRespectAvailabilityAndLeadBinding),
        new(nameof(ProjectedWireSurvivesPartitionAndRestore), ProjectedWireSurvivesPartitionAndRestore),
        new(nameof(ProjectedGroupRollsBackLateFailure), ProjectedGroupRollsBackLateFailure),
        new(nameof(ProjectedCheckpointRechecksSamplesAndBindings), ProjectedCheckpointRechecksSamplesAndBindings),
    ];

    private static string Snapshot(ElectrodeWaveformGroup source) => JsonSerializer.Serialize(source.CaptureState());
    private static void Reject(Action action, string reason)
    {
        bool rejected = false;
        try { action(); }
        catch (ElectrodeWaveformGroupException exception) { rejected = exception.ReasonCode == reason; }
        Check.That(rejected, "expected stable projected group failure: " + reason);
    }

    private static void ProjectedBlocksRespectAvailabilityAndLeadBinding()
    {
        var source = Start();
        Check.That(source.AdvanceTo(235_999_999, 59, 1, 100).Count == 0, "last 196ms source sample waits for 236ms availability");
        var block = WaveformEnvelopeCodec.Decode(source.AdvanceTo(236_000_000, 1, 1, 100).Single());
        Check.That(block.Planes.Count == 12 && block.StartSimTimeNs == 0, "one shared block contains all twelve leads at the same origin");
        var expected = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 7, Electrodes()).GenerateBefore(200_000_000, 50, 100);
        foreach (var binding in Channels())
        {
            var plane = block.Planes.Single(item => item.ChannelId == binding.ChannelId);
            Check.That(plane.Samples.SequenceEqual(expected.Select(frame => frame.MicrovoltValues[(int)binding.Lead])) &&
                plane.QualityRanges.Single() == new WaveformQualityRange(0, 50, binding.QualityFlags),
                "channel UUID order must not change lead identity, once-rounded samples or explicit quality");
        }
    }

    private static void ProjectedWireSurvivesPartitionAndRestore()
    {
        var whole = Start();
        var expected = whole.AdvanceTo(2_000_000_000, 500, 10, 100);
        var split = Start(reverse: true);
        List<byte[]> actual = [];
        foreach (long end in new long[] { 173_000_000, 236_000_000, 409_000_000, 811_000_000, 1_333_000_000, 2_000_000_000 })
        {
            actual.AddRange(split.AdvanceTo(end, 500, 10, 100));
            split = ElectrodeWaveformGroup.Restore(split.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)) &&
            Snapshot(whole) == Snapshot(split), "batch splits, caller binding order and recovery preserve wire bytes and pending state");
    }

    private static void ProjectedGroupRollsBackLateFailure()
    {
        var source = Start();
        string before = Snapshot(source);
        Reject(() => source.AdvanceTo(600_000_000, 150, 1, 100), "ElectrodeGroup.BlockLimitExceeded");
        Check.That(Snapshot(source) == before, "late output limit failure rolls back generator, all delay lines and assembler");
        bool cancelled = false;
        try { _ = source.AdvanceTo(200_000_000, 50, 1, 100, new CancellationToken(true)); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && Snapshot(source) == before, "cancelled generation cannot publish or advance any channel");
        byte[] expected = Start().AdvanceTo(236_000_000, 59, 1, 100).Single();
        Check.That(source.AdvanceTo(236_000_000, 59, 1, 100).Single().SequenceEqual(expected), "retry after failed batch retains exact output");
    }

    private static void ProjectedCheckpointRechecksSamplesAndBindings()
    {
        var source = Start();
        _ = source.AdvanceTo(200_000_000, 50, 1, 100);
        var state = source.CaptureState();
        var channels = state.Channels.ToArray();
        var pending = channels[0].Delay.PendingSamples.ToArray();
        pending[0] = pending[0] with { NormalizedValue = 12345 };
        channels[0] = channels[0] with { Delay = channels[0].Delay with { PendingSamples = pending } };
        Reject(() => ElectrodeWaveformGroup.Restore(state with { Channels = channels }), "ElectrodeGroup.InvalidCheckpoint");
        channels = state.Channels.ToArray();
        channels[0] = channels[0] with { Lead = channels[1].Lead };
        Reject(() => ElectrodeWaveformGroup.Restore(state with { Channels = channels }), "ElectrodeGroup.InvalidCheckpoint");
        var planes = state.Assembler.Planes.ToArray();
        pending = planes[0].PendingSamples.ToArray();
        pending[0] = pending[0] with { QualityFlags = 999 };
        planes[0] = planes[0] with { PendingSamples = pending };
        Reject(() => ElectrodeWaveformGroup.Restore(state with { Assembler = state.Assembler with { Planes = planes } }), "ElectrodeGroup.InvalidCheckpoint");
        planes = state.Assembler.Planes.ToArray();
        planes[0] = planes[0] with { Configuration = planes[0].Configuration with { ScaleNumerator = 2 } };
        Reject(() => ElectrodeWaveformGroup.Restore(state with { Assembler = state.Assembler with { Planes = planes } }), "ElectrodeGroup.InvalidCheckpoint");
        Check.That(Snapshot(source) == Snapshot(ElectrodeWaveformGroup.Restore(state)), "invalid trials do not alter valid owned checkpoint data");
    }
}
