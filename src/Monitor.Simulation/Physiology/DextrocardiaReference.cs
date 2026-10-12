// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Mirror-image anatomy at standard electrode positions, independent of wiring.
// Authored amplitudes; see eng/physiology/mirror-image-dextrocardia.json.
public static class DextrocardiaReference
{
    public const string EvidenceId = "MirrorImageDextrocardiaIllustration@1";

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        IReadOnlyList<long>[] qrs = [DextrocardiaTables.RA, DextrocardiaTables.LA,
            DextrocardiaTables.RL, DextrocardiaTables.LL, DextrocardiaTables.C1,
            DextrocardiaTables.C2, DextrocardiaTables.C3, DextrocardiaTables.C4,
            DextrocardiaTables.C5, DextrocardiaTables.C6];
        var bands = TextbookEcgReference.CreateBands();
        return Array.AsReadOnly(Enum.GetValues<EcgElectrode>().Select(electrode => new ElectrodeWaveformPlan(electrode,
            Array.AsReadOnly(bands.Select((band, index) => band with
            {
                TableQ32 = index == 1 ? qrs[(int)electrode] : Array.AsReadOnly(band.TableQ32.Select(value =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value *
                        (index == 0 ? DextrocardiaTables.PWeightsQ32 : DextrocardiaTables.TWeightsQ32)[(int)electrode],
                        1000 * (Int128)FixedPointMath.Q32One))).ToArray()),
            }).ToArray()))).ToArray());
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Concat(
            electrodes[(int)EcgElectrode.RA].Bands.Select(band => band with
            { TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value => checked(-value)).ToArray()) })).ToArray());
    }
}
