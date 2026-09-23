// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Established regular narrow-complex SVT illustration, not a reentry mechanism.
public static class SupraventricularTachycardiaReference
{
    public const string EvidenceId = "NarrowComplexSvtIllustration@1";
    // PR40 is a component-builder placeholder, not an observed PR interval.
    public static EcgCycleTiming Timing { get; } = new(300_000_000, 40_000_000,
        40_000_000, 80_000_000, 240_000_000, 100_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, Timing.RrIntervalNs,
        0, 80_000_000, 80_000_000, 3_750_000_000, 1_875_000_000,
        ConductionPattern: AvConductionPattern.NarrowComplexSvtIllustration);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select(e => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical
                ? b with { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked((long)FixedPointMath.RoundDivideTiesToEven(-(Int128)v, 2))).ToArray()) }
                : b).ToArray())
        }).ToArray());
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var e = CreateElectrodes();
        return Array.AsReadOnly(e[(int)EcgElectrode.LL].Bands.Concat(e[(int)EcgElectrode.RA].Bands.Select(b => b with
        { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked(-v)).ToArray()) })).ToArray());
    }
}
