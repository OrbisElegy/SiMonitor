// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Established ectopic atrial rhythm with1:1 conduction, not an automaticity model.
public static class AcceleratedAtrialReference
{
    public const string EvidenceId = "AcceleratedAtrialIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(600_000_000, 80_000_000,
        160_000_000, 80_000_000, 400_000_000, 180_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, 600_000_000,
        160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() => CreateElectrodes(Timing);

    internal static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(EcgCycleTiming timing)
    {
        var ectopic = PrematureAtrialReference.CreateElectrodes();
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: timing).Select((electrode, index) => electrode with
        {
            // Replace the sinus P on every atrial event. Do not append an
            // extra P-prime or reuse the PAC coupling/compensatory schedule.
            Bands = Array.AsReadOnly(electrode.Bands.Select(band => band.Trigger == PhysiologyCycleEventKind.AtrialElectrical
                ? band with { TableQ32 = ectopic[index].Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.PrematureAtrialElectrical).TableQ32 }
                : band).ToArray())
        }).ToArray());
    }
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Zip(electrodes[(int)EcgElectrode.RA].Bands)
            .Select(pair => pair.First with
            {
                TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                    .Select(v => checked(v.First - v.Second)).ToArray())
            }).ToArray());
    }
}
