// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Simulation.Authoring;

// An owned electrode definition shared by monitoring, detection and twelve-lead
// output. Rhythm and respiratory/mechanical settings remain configuration-owned.
public sealed class AuthoredEcgDefinition
{
    public long AtrialPeriodNs { get; }
    public long VentricularElectricalOffsetNs { get; }
    public IReadOnlyList<ElectrodeWaveformPlan> Electrodes { get; }

    public AuthoredEcgDefinition(long atrialPeriodNs, long ventricularElectricalOffsetNs,
        IReadOnlyList<ElectrodeWaveformPlan> electrodes, EcgLimbPlacement placement)
    {
        if (atrialPeriodNs <= 0 || ventricularElectricalOffsetNs < 0)
        { throw new ArgumentException("EcgDefinition.InvalidTiming"); }
        var owned = ElectrodeWaveformComposition.Restore(new(electrodes, [], placement)).CaptureState().Electrodes;
        int[] wiring = placement switch
        {
            EcgLimbPlacement.Standard => [0, 1, 2, 3],
            EcgLimbPlacement.SwapRaLa => [1, 0, 2, 3],
            EcgLimbPlacement.SwapRaLl => [3, 1, 2, 0],
            EcgLimbPlacement.SwapLaLl => [0, 3, 2, 1],
            _ => throw new ArgumentException("EcgPlacement.InvalidWiring", nameof(placement))
        };
        Electrodes = Array.AsReadOnly(owned.Select((electrode, index) => electrode with
        { Bands = owned[index < 4 ? wiring[index] : index].Bands }).ToArray());
        AtrialPeriodNs = atrialPeriodNs;
        VentricularElectricalOffsetNs = ventricularElectricalOffsetNs;
    }

    public IReadOnlyList<EventWaveformBand> CreateLeadIIBands() => Array.AsReadOnly(
        Electrodes[(int)EcgElectrode.LL].Bands.Concat(Electrodes[(int)EcgElectrode.RA].Bands.Select(band => band with
        { TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value => checked(-value)).ToArray()) })).ToArray());
}
