// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Project-authored electrode illustration, not validated body-surface morphology.
// Independent P/QRS/T electrode weights reuse the authored textbook band shapes.
// This does not derive twelve leads by scaling one II waveform; within-band
// vector evolution and independent physiological validation remain future work.
internal static class ProjectedEcgDemoSource
{
    internal static string[] LeadNames => ["I", "II", "III", "aVR", "aVL", "aVF", "V1", "V2", "V3", "V4", "V5", "V6"];
    internal static Guid ChannelId(EcgLead lead) => Guid.Parse($"00000000-0000-4000-8000-{12 - (int)lead:D12}");

    internal static ElectrodeWaveformGroup Create()
    {
        // Rows: RA, LA, RL, LL, C1-C6. Columns: P, QRS, T; denominator 1000.
        int[,] weights = { { -200, -200, -100 }, { 300, 400, 250 }, { 0, 0, 0 }, { 600, 800, 550 },
            { 150, -500, 0 }, { 250, -250, 100 }, { 350, 100, 350 },
            { 450, 700, 650 }, { 500, 1100, 800 }, { 450, 1000, 750 } };
        var bands = TextbookEcgReference.CreateBands();
        var electrodes = Enum.GetValues<EcgElectrode>().Select(electrode => new ElectrodeWaveformPlan(electrode,
            bands.Select((band, index) => band with
            {
                TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value =>
                checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * weights[(int)electrode, index], 1000))).ToArray())
            }).ToArray())).ToArray();
        var timing = TextbookEcgReference.Timing;
        RegularPhysiologyPlan plan = new(0, timing.RrIntervalNs, timing.PrIntervalNs,
            80_000_000, timing.PrIntervalNs + 80_000_000, 3_750_000_000, 1_875_000_000);
        return ElectrodeWaveformGroup.Start(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), 1, 1, 1, 0, 16, plan, electrodes,
            Enum.GetValues<EcgLead>().Select(lead => new ElectrodeChannelPlan(lead, ChannelId(lead), 10, 0)).ToArray());
    }
}
