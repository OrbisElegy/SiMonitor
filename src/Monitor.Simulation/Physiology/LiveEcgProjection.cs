// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Project the electrodes before quantization. Each electrode uses the same
// event-tail continuation as the monitor's II channel, without a delivery queue.
public sealed class LiveEcgProjection
{
    private PhysiologySignalGenerator[] _electrodes;

    public LiveEcgProjection(RegularPhysiologyPlan plan, IReadOnlyList<ElectrodeWaveformPlan> electrodes)
    {
        var owned = ElectrodeWaveformComposition.Restore(new(electrodes, [])).CaptureState().Electrodes;
        _electrodes = owned.Select(electrode => PhysiologySignalGenerator.Start(plan,
            "AcqECGMonitor250@1", 1, electrode.Bands)).ToArray();
    }

    private LiveEcgProjection(PhysiologySignalGenerator[] electrodes) => _electrodes = electrodes;

    public LiveEcgProjection Fork() => new(_electrodes.Select(electrode => electrode.Fork()).ToArray());

    public void ContinueWith(LiveEcgProjection definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var electrodes = _electrodes.Select((electrode, index) =>
            electrode.ContinueWith(definition._electrodes[index].CaptureState())).ToArray();
        _electrodes = electrodes;
    }

    public IReadOnlyList<ElectrodeSignalSample> GenerateBefore(long exclusiveSimTimeNs, int maximumSamples, int maximumEvents)
    {
        var trial = Fork();
        var channels = trial._electrodes.Select(electrode =>
            electrode.GenerateBefore(exclusiveSimTimeNs, maximumSamples, maximumEvents)).ToArray();
        var result = new ElectrodeSignalSample[channels[0].Count];
        for (int index = 0; index < result.Length; index++)
        {
            long Value(int electrode) => channels[electrode][index].ValueQ32;
            var leads = EcgLeadProjection.Project(new(Value(0), Value(1), Value(2), Value(3), Value(4),
                Value(5), Value(6), Value(7), Value(8), Value(9)));
            short[] values = Enum.GetValues<EcgLead>().Select(lead => checked((short)FixedPointMath.RoundDivideTiesToEven(
                leads[lead].Numerator, 6 * (Int128)FixedPointMath.Q32One))).ToArray();
            result[index] = new(channels[0][index].Tick, leads, Array.AsReadOnly(values));
        }
        _electrodes = trial._electrodes;
        return Array.AsReadOnly(result);
    }
}
