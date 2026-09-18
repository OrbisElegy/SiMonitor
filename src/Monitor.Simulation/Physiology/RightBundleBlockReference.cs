// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored complete RBBB shape combined with fixed-PR 4:3 Mobitz II.
public static class RightBundleBlockReference
{
    public const string EvidenceId = "RightBundleBlockIllustrationDraft@1";
    public static EcgCycleTiming Timing { get; } = new(800_000_000, 100_000_000,
        160_000_000, 140_000_000, 400_000_000, 180_000_000);

    public static RegularPhysiologyPlan CreatePlan() => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000,
        VentricularConductionRatio: 4, ConductedBeatsPerGroup: 3,
        ConductionPattern: AvConductionPattern.MobitzTwoRbbbFourToThreeIllustration);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() => CreateElectrodes(Timing);

    internal static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(EcgCycleTiming timing)
    {
        IReadOnlyList<long>[] qrs = [RightBundleBlockTables.RA, RightBundleBlockTables.LA,
            RightBundleBlockTables.RL, RightBundleBlockTables.LL, RightBundleBlockTables.C1,
            RightBundleBlockTables.C2, RightBundleBlockTables.C3, RightBundleBlockTables.C4,
            RightBundleBlockTables.C5, RightBundleBlockTables.C6];
        long peak = TextbookEcgTables.T.Max();
        var st = new EcgStSegmentPlan([0, 0, 0, 0, -50, -50, 0, 0, 0, 0],
            [0, 0, 0, 0, -80, -80, 0, 0, 0, 0]).CreateBands(timing);
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: timing).Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index switch
            {
                1 => band with { TableQ32 = qrs[i] },
                2 => band with
                {
                    TableQ32 = Array.AsReadOnly(TextbookEcgTables.T.Select(value =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * RightBundleBlockTables.TAmplitudesQ32[i], peak))).ToArray())
                },
                _ => band,
            }).Concat(st[i] ?? []).ToArray()),
        }).ToArray());
    }

    // The monitor ECG uses lead II from the same electrode morphology.
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        {
            TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                .Select(values => checked(values.First - values.Second)).ToArray()),
        }).ToArray());
    }
}
