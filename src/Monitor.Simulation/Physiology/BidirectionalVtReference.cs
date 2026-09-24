// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// One authored opposite ventricular vector pair, not anatomical activation or
// digitalis/CPVT causation. Electrical polarity does not reverse perfusion.
public static class BidirectionalVtReference
{
    public const string EvidenceId = "BidirectionalVtIllustration@1";
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        Array.AsReadOnly(VentricularTachycardiaReference.CreateElectrodes().Select(e => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical
                ? b with { VentricularCycles = new(2, 1) } : b)
                .Concat(e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).Select(b => b with
                {
                    VentricularCycles = new(2, 2),
                    TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked(-v)).ToArray())
                })).ToArray())
        }).ToArray());
}
