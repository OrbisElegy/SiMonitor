// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// One authored established ventricular escape morphology; no focus localization.
public static class CompleteAvBlockVentricularReference
{
    public const string EvidenceId = "CompleteAvBlockVentricularIllustrationDraft@1";
    public static EcgCycleTiming Timing { get; } = new(2_000_000_000, 100_000_000,
        160_000_000, 160_000_000, 480_000_000, 220_000_000);

    public static RegularPhysiologyPlan CreatePlan(long atrialPeriodNs = 800_000_000,
        long escapePeriodNs = 2_000_000_000, long firstQrsNs = 400_000_000)
    {
        if (firstQrsNs < 0 || firstQrsNs > long.MaxValue - 80_000_000)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidState", nameof(firstQrsNs)); }
        RegularPhysiologyPlan plan = new(0, atrialPeriodNs, firstQrsNs, 80_000_000,
            firstQrsNs + 80_000_000, 3_750_000_000, 1_875_000_000,
            IndependentVentricularPeriodNs: escapePeriodNs,
            ConductionPattern: AvConductionPattern.CompleteAvBlockVentricularIllustration);
        _ = RegularPhysiologyTimeline.Start(plan);
        return plan;
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        IReadOnlyList<long>[] qrs = [VentricularEscapeTables.RA, VentricularEscapeTables.LA,
            VentricularEscapeTables.RL, VentricularEscapeTables.LL, VentricularEscapeTables.C1,
            VentricularEscapeTables.C2, VentricularEscapeTables.C3, VentricularEscapeTables.C4,
            VentricularEscapeTables.C5, VentricularEscapeTables.C6];
        long peak = TextbookEcgTables.T.Max();
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index switch
            {
                1 => band with { TableQ32 = qrs[i] },
                2 => band with
                {
                    TableQ32 = Array.AsReadOnly(TextbookEcgTables.T.Select(value =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * VentricularEscapeTables.TAmplitudesQ32[i], peak))).ToArray())
                },
                _ => band,
            }).ToArray()),
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
