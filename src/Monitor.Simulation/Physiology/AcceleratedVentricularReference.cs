// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Established AIVR contour example, not automaticity or AV competition.
public static class AcceleratedVentricularReference
{
    public const string EvidenceId = "AcceleratedVentricularIllustration@1";
    // PR100 is only the electrode builder's placeholder, not a measured PR.
    public static EcgCycleTiming Timing { get; } = new(750_000_000, 100_000_000,
        100_000_000, 160_000_000, 400_000_000, 180_000_000);

    public static RegularPhysiologyPlan CreatePlan() => new(0, 800_000_000,
        120_000_000, 80_000_000, 200_000_000, 3_750_000_000, 1_875_000_000,
        IndependentVentricularPeriodNs: 750_000_000,
        ConductionPattern: AvConductionPattern.AcceleratedVentricularIllustration);

    public const string CaptureEvidenceId = "AcceleratedVentricularCoincidentCaptureIllustration@1";
    public const string FusionEvidenceId = "AcceleratedVentricularFusionIllustration@1";
    // Slot0 of16 repeats every12s, aligned with every15th sinus P.
    // P at0/12000ms precedes fusion or coincident capture at120/12120ms.
    // Authored PR120ms; capture keeps the original grid, no focus reset.
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool fusion = false, bool capture = false)
    {
        if (fusion && capture) { throw new EventWaveformException("Aivr.ConflictingModes", "configuration"); }
        var ventricular = CompleteAvBlockVentricularReference.CreateElectrodes(Timing);
        if (!fusion && !capture) { return ventricular; }
        var conducted = TextbookElectrodeReference.CreateElectrodes(timing: Timing with { QrsDurationNs = 80_000_000 });
        return Array.AsReadOnly(ventricular.Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical
                ? b with { VentricularCycles = new(16, 65534) } : b)
                .Concat(fusion ? e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).Select(Half) : [])
                .Concat(conducted[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).Select(b => capture ? b with { VentricularCycles = new(16, 1) } : Half(b))).ToArray())
        }).ToArray());
        EventWaveformBand Half(EventWaveformBand b) => b with
        {
            VentricularCycles = new(16, 1),
            TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked((long)FixedPointMath.RoundDivideTiesToEven(v, 2))).ToArray())
        };
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool fusion = false, bool capture = false)
    {
        var electrodes = CreateElectrodes(fusion, capture);
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Zip(electrodes[(int)EcgElectrode.RA].Bands)
            .Select(pair => pair.First with
            {
                TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                    .Select(v => checked(v.First - v.Second)).ToArray())
            }).ToArray());
    }
}
