// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Textbook-constrained illustration. QRS uses shared early/main/terminal
// components with independent electrode coefficients, not one QRS scale.
public static class TextbookElectrodeReference
{
    public const string EvidenceId = "TextbookChestProgressionDraft@2";
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        int[,] weights = { { -200, -100 }, { 300, 250 }, { 0, 0 }, { 600, 550 },
            { 150, 0 }, { 250, 100 }, { 350, 350 }, { 450, 650 }, { 500, 800 }, { 450, 750 } };
        IReadOnlyList<long>[] qrs = [TextbookElectrodeQrsTables.RA, TextbookElectrodeQrsTables.LA,
            TextbookElectrodeQrsTables.RL, TextbookElectrodeQrsTables.LL, TextbookElectrodeQrsTables.C1,
            TextbookElectrodeQrsTables.C2, TextbookElectrodeQrsTables.C3, TextbookElectrodeQrsTables.C4,
            TextbookElectrodeQrsTables.C5, TextbookElectrodeQrsTables.C6];
        var bands = TextbookEcgReference.CreateBands();
        return Array.AsReadOnly(Enum.GetValues<EcgElectrode>().Select(electrode => new ElectrodeWaveformPlan(electrode,
            Array.AsReadOnly(bands.Select((band, index) => band with
            {
                TableQ32 = index == 1 ? qrs[(int)electrode] : Array.AsReadOnly(band.TableQ32.Select(value =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * weights[(int)electrode, index == 0 ? 0 : 1], 1000))).ToArray()),
            }).ToArray()))).ToArray());
    }
}
