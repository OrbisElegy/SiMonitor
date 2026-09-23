// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Established monomorphic VT with independent slower atria. Authored contour,
// not an anatomical focus, onset mechanism or capture-beat model.
public static class VentricularTachycardiaReference
{
    public const string EvidenceId = "MonomorphicVtIllustration@1";
    // PR100 is only a component-builder placeholder: there is no fixed PR.
    public static EcgCycleTiming Timing { get; } = new(375_000_000, 100_000_000,
        100_000_000, 160_000_000, 275_000_000, 100_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, 800_000_000,
        120_000_000, 80_000_000, 200_000_000, 3_750_000_000, 1_875_000_000,
        IndependentVentricularPeriodNs: 375_000_000,
        ConductionPattern: AvConductionPattern.MonomorphicVtIllustration);

    // One fusion slot per12s: ventricular index13 begins at4995ms,195ms
    // after the independent P at4800ms. The32-beat group repeats alongside
    // fifteen atrial cycles. This is an authored example, not an AV node model.
    public const string FusionEvidenceId = "VtFusionIllustration@1";
    public const int FusionCycleLength = 32;
    public const int FusionCycleSlot = 13;
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool fusion = false)
    {
        var ventricular = CompleteAvBlockVentricularReference.CreateElectrodes(Timing);
        if (!fusion) { return ventricular; }
        var conducted = TextbookElectrodeReference.CreateElectrodes(timing: Timing with { QrsDurationNs = 80_000_000 });
        const ulong fusionMask = 1UL << FusionCycleSlot;
        const ulong ordinaryMask = ((1UL << FusionCycleLength) - 1) ^ fusionMask;
        return Array.AsReadOnly(ventricular.Select((e, index) => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical
                ? b with { VentricularCycles = new(FusionCycleLength, ordinaryMask) } : b)
                .Concat(e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).Select(HalfFusion))
                .Concat(conducted[index].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).Select(HalfFusion)).ToArray())
        }).ToArray());

        // Preserve each component's own time support, not just a mean table.
        // The50/50 electrode mixture also keeps the limb projection identities.
        static EventWaveformBand HalfFusion(EventWaveformBand b) => b with
        {
            VentricularCycles = new(FusionCycleLength, fusionMask),
            TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked((long)FixedPointMath.RoundDivideTiesToEven(v, 2))).ToArray())
        };
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool fusion = false)
    {
        var electrodes = CreateElectrodes(fusion);
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Concat(
            electrodes[(int)EcgElectrode.RA].Bands.Select(b => b with
            { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked(-v)).ToArray()) })).ToArray());
    }
}
