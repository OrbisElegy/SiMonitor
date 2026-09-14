// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Optional shared U timing and explicit electrode amplitudes, in canonical
// EcgElectrode order. U is outside QT; no inferred heart-rate or disease rule.
public sealed record EcgUWavePlan(long DelayAfterTNs, long DurationNs,
    IReadOnlyList<long> ElectrodeAmplitudesMicrovolts)
{
    public void Validate(EcgCycleTiming timing)
    {
        timing.Validate();
        if (DelayAfterTNs < 0 || DurationNs <= 0 ||
            (Int128)timing.PrIntervalNs + timing.QtIntervalNs + DelayAfterTNs + DurationNs > timing.RrIntervalNs ||
            ElectrodeAmplitudesMicrovolts is null || ElectrodeAmplitudesMicrovolts.Count != 10 ||
            ElectrodeAmplitudesMicrovolts.Any(value => value < short.MinValue || value > short.MaxValue))
        { throw new EventWaveformException("EcgUWave.InvalidPlan", "uWave"); }
    }
}
