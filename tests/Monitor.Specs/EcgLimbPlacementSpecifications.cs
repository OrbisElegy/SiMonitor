// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EcgLimbPlacementSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(LimbWiringPreservesExactProjectionIdentities), LimbWiringPreservesExactProjectionIdentities),
        new(nameof(LimbWiringChangesOnlyLimbSamples), LimbWiringChangesOnlyLimbSamples),
        new(nameof(LimbWiringCheckpointKeepsNativeBytesAndRejectsMismatch), LimbWiringCheckpointKeepsNativeBytesAndRejectsMismatch),
        new(nameof(LimbWiringDefaultsAndAtomicFailure), LimbWiringDefaultsAndAtomicFailure),
    ];
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    private static (int Lead, int Sign)[] Mapping(EcgLimbPlacement placement) => placement switch
    {
        EcgLimbPlacement.SwapRaLa => [(0, -1), (2, 1), (1, 1), (4, 1), (3, 1), (5, 1)],
        EcgLimbPlacement.SwapRaLl => [(2, -1), (1, -1), (0, -1), (5, 1), (4, 1), (3, 1)],
        EcgLimbPlacement.SwapLaLl => [(1, 1), (0, 1), (2, -1), (3, 1), (5, 1), (4, 1)],
        _ => [(0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1)],
    };

    private static void LimbWiringPreservesExactProjectionIdentities()
    {
        EcgElectrodePotentials source = new(long.MinValue, long.MaxValue, 99, 123, 11, 22, 33, 44, 55, 66);
        var normal = EcgLeadProjection.Project(source);
        foreach (EcgLimbPlacement placement in Enum.GetValues<EcgLimbPlacement>())
        {
            var wired = EcgLimbWiring.Apply(source, placement);
            var actual = EcgLeadProjection.Project(wired);
            var mapping = Mapping(placement);
            for (int lead = 0; lead < 12; lead++)
            {
                var expected = lead < 6 ? normal[(EcgLead)mapping[lead].Lead].Numerator * mapping[lead].Sign : normal[(EcgLead)lead].Numerator;
                Check.That(actual[(EcgLead)lead].Numerator == expected, "wiring permutes potentials before exact projection, not rounded lead pixels");
            }
            Check.That(actual.WilsonCentralTerminal == normal.WilsonCentralTerminal && wired.RL == source.RL &&
                actual[EcgLead.I].Numerator + actual[EcgLead.III].Numerator == actual[EcgLead.II].Numerator &&
                actual[EcgLead.AVR].Numerator + actual[EcgLead.AVL].Numerator + actual[EcgLead.AVF].Numerator == 0,
                "limb swaps preserve Wilson, RL, Einthoven and Goldberger even at extreme input values");
        }
    }

    private static ElectrodeSignalGenerator Source(EcgLimbPlacement placement) => ElectrodeSignalGenerator.Start(Plan,
        "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(), placement);
    private static ElectrodeWaveformGroup Group(EcgLimbPlacement placement) => ElectrodeWaveformGroup.Start(
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
        1, 1, 1, 0, 16, Plan, TextbookElectrodeReference.CreateElectrodes(), Enum.GetValues<EcgLead>().Select(lead =>
            new ElectrodeChannelPlan(lead, Guid.Parse($"00000000-0000-4000-8000-{(int)lead + 1:D12}"), 10, 0)).ToArray(), placement);

    private static void LimbWiringChangesOnlyLimbSamples()
    {
        var normal = Source(EcgLimbPlacement.Standard).GenerateBefore(1_600_000_000, 400, 100);
        foreach (EcgLimbPlacement placement in Enum.GetValues<EcgLimbPlacement>())
        {
            var source = Source(placement);
            var actual = source.GenerateBefore(1_600_000_000, 400, 100);
            var mapping = Mapping(placement);
            Check.That(actual.Zip(normal).All(pair => pair.First.Tick == pair.Second.Tick && Enumerable.Range(0, 12).All(lead =>
                pair.First.MicrovoltValues[lead] == (lead < 6 ? pair.Second.MicrovoltValues[mapping[lead].Lead] * mapping[lead].Sign : pair.Second.MicrovoltValues[lead]))),
                "native250Hz limb relationships change as wired while chest samples and all timestamps remain identical");
            Check.That(source.CaptureState().Timeline.Plan == Plan, "wiring does not rewrite latent cardiac or respiratory events");
        }
    }

    private static void LimbWiringCheckpointKeepsNativeBytesAndRejectsMismatch()
    {
        foreach (EcgLimbPlacement placement in new[] { EcgLimbPlacement.SwapRaLa, EcgLimbPlacement.SwapRaLl, EcgLimbPlacement.SwapLaLl })
        {
            var expected = Group(placement).AdvanceTo(1_600_000_000, 400, 8, 100);
            var group = Group(placement);
            List<byte[]> actual = [];
            for (int step = 1; step <= 100; step++)
            {
                actual.AddRange(group.AdvanceTo(step * 16_000_000L, 4, 1, 100));
                group = ElectrodeWaveformGroup.Restore(group.CaptureState());
            }
            Check.That(actual.Count == expected.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)), "wire bytes retain placement across every native frame restore");
            // Place the checkpoint within QRS so both queued and staged limb data
            // distinguish this connection from the standard connection.
            group = Group(placement);
            group.AdvanceTo(201_000_000, 51, 2, 100);
            var checkpoint = group.CaptureState();
            bool rejected = false;
            try { ElectrodeWaveformGroup.Restore(checkpoint with { Generator = checkpoint.Generator with { Placement = EcgLimbPlacement.Standard } }); }
            catch (ElectrodeWaveformGroupException exception) { rejected = exception.ReasonCode == "ElectrodeGroup.InvalidCheckpoint"; }
            Check.That(rejected, "restoring altered wiring cannot silently accept original pending lead samples");
        }
    }

    private static void LimbWiringDefaultsAndAtomicFailure()
    {
        var state = Source(EcgLimbPlacement.Standard).CaptureState();
        var json = JsonSerializer.SerializeToNode(state)!.AsObject();
        json.Remove(nameof(ElectrodeSignalState.Placement));
        Check.That(json.Deserialize<ElectrodeSignalState>()!.Placement == EcgLimbPlacement.Standard, "old checkpoints default to standard placement");
        bool rejected = false;
        try { ElectrodeSignalGenerator.Restore(state with { Placement = (EcgLimbPlacement)99 }); }
        catch (ElectrodeSignalException exception) { rejected = exception.ReasonCode == "ElectrodeSignal.InvalidCheckpoint"; }
        Check.That(rejected, "undefined wiring rejects before source construction");
        var group = Group(EcgLimbPlacement.SwapRaLa);
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(1_000_000_000, 250, 1, 100); }
        catch (ElectrodeWaveformGroupException exception) { limited = exception.ReasonCode == "ElectrodeGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late publication failure retains the accepted wiring and source frontier");
    }
}
