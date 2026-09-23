// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Morphology is separate from AV conduction. These named examples use 1:1.
public enum EcgBundleBlockIllustration { Reference, CompleteRight, IncompleteRight, CompleteLeft, IncompleteLeft, LeftAnteriorFascicular }

public static class BundleBlockReference
{
    public const string EvidenceId = "StandaloneBundleBlockIllustrationDraft@1";
    public static EcgCycleTiming Timing(EcgBundleBlockIllustration mode) => mode switch
    {
        EcgBundleBlockIllustration.CompleteRight => RightBundleBlockReference.Timing,
        EcgBundleBlockIllustration.IncompleteRight => RightBundleBlockReference.Timing with { QrsDurationNs = 110_000_000 },
        EcgBundleBlockIllustration.CompleteLeft => LeftBundleBlockReference.Timing,
        EcgBundleBlockIllustration.IncompleteLeft => LeftBundleBlockReference.Timing with { QrsDurationNs = 110_000_000 },
        EcgBundleBlockIllustration.LeftAnteriorFascicular => LeftAnteriorFascicularReference.Timing,
        _ => throw Invalid(),
    };

    public static RegularPhysiologyPlan CreatePlan(EcgBundleBlockIllustration mode)
    {
        var timing = Timing(mode);
        return new(0, timing.RrIntervalNs, timing.PrIntervalNs, 80_000_000,
            timing.PrIntervalNs + 80_000_000, 3_750_000_000, 1_875_000_000);
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(EcgBundleBlockIllustration mode)
    {
        var timing = Timing(mode);
        if (mode == EcgBundleBlockIllustration.LeftAnteriorFascicular) { return LeftAnteriorFascicularReference.CreateElectrodes(); }
        return mode is EcgBundleBlockIllustration.CompleteLeft or EcgBundleBlockIllustration.IncompleteLeft
            ? LeftBundleBlockReference.CreateElectrodes(timing) : RightBundleBlockReference.CreateElectrodes(timing);
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(EcgBundleBlockIllustration mode)
    {
        var electrodes = CreateElectrodes(mode);
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        {
            TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                .Select(values => checked(values.First - values.Second)).ToArray()),
        }).ToArray());
    }

    private static EventWaveformException Invalid() => new("EcgBundleBlock.InvalidMode", "mode");
}
