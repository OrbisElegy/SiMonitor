// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Fixed teaching combination, not serum potassium or disease progression.
public static class HyperkalemiaConductionReference
{
    public const string EvidenceId = "HyperkalemiaConductionIllustration@1";
    public const string QrsVoltageEvidenceId = "HyperkalemiaQrsVoltageIllustration@1";
    public const string AbsentPEvidenceId = "HyperkalemiaAbsentPIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(1_000_000_000, 140_000_000,
        240_000_000, 140_000_000, 440_000_000, 160_000_000);
    public static RegularPhysiologyPlan CreatePlan(bool absentP = false) => new(0, Timing.RrIntervalNs,
        Timing.PrIntervalNs, 80_000_000, Timing.PrIntervalNs + 80_000_000, 3_750_000_000, 1_875_000_000,
        CardiacActivity: absentP ? CardiacActivity.VentricularOnly : CardiacActivity.AtrialAndVentricular);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool absentP = false)
    {
        int[] st = [40, 0, 0, -40, -50, -80, -100, -100, -80, -60];
        var source = TextbookElectrodeReference.CreateElectrodes(timing: Timing, stSegment: new(st, st));
        var peaked = HyperkalemiaRepolarizationReference.CreateElectrodes();
        IReadOnlyList<long>[] qrs = [HyperkalemiaQrsTables.RA, HyperkalemiaQrsTables.LA,
            HyperkalemiaQrsTables.RL, HyperkalemiaQrsTables.LL, HyperkalemiaQrsTables.C1,
            HyperkalemiaQrsTables.C2, HyperkalemiaQrsTables.C3, HyperkalemiaQrsTables.C4,
            HyperkalemiaQrsTables.C5, HyperkalemiaQrsTables.C6];
        var output = Array.AsReadOnly(source.Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index switch
            {
                0 => band with
                {
                    TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value =>
                    (long)FixedPointMath.RoundDivideTiesToEven(value, 3)).ToArray())
                },
                1 => band with { TableQ32 = qrs[i] },
                2 => band with { TableQ32 = peaked[i].Bands[2].TableQ32, PhasePoints = null },
                _ => band,
            }).ToArray()),
        }).ToArray());
        if (absentP)
        {
            // This authored example suppresses effective atrial excitation,
            // not the ventricular timing grid; it is not a sinus-node model.
            output = Array.AsReadOnly(output.Select(electrode => electrode with
            {
                Bands = Array.AsReadOnly(electrode.Bands.Where(band =>
                    band.Trigger != PhysiologyCycleEventKind.AtrialElectrical).ToArray()),
            }).ToArray());
        }
        return ElectrodeWaveformComposition.Restore(new(output, [])).CaptureState().Electrodes;
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool absentP = false)
    {
        var electrodes = CreateElectrodes(absentP);
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Concat(
            electrodes[(int)EcgElectrode.RA].Bands.Select(band => band with
            { TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value => checked(-value)).ToArray()) })).ToArray());
    }
}
