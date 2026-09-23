// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EcgProjectionChecks
{
    internal static void RequireMatchingLeadII(IReadOnlyList<ElectrodeSignalSample> projected,
        IReadOnlyList<PhysiologySignalSample> monitor, int expectedSampleCount, string scenario)
    {
        Check.That(expectedSampleCount > 0 && projected.Count == expectedSampleCount && monitor.Count == expectedSampleCount,
            $"{scenario}: both sources produce every expected sample");
        Check.That(projected.Zip(monitor).All(pair => pair.First.Tick == pair.Second.Tick &&
            Math.Abs(pair.First.MicrovoltValues[(int)EcgLead.II] - pair.Second.NormalizedValue) <= 1),
            $"{scenario}: monitor II matches the electrode projection at each sample time");
    }

    internal static void RequireMatchingSamples(IReadOnlyList<ElectrodeSignalSample> expected,
        IReadOnlyList<ElectrodeSignalSample> actual, string scenario)
    {
        Check.That(expected.Count > 0 && actual.Count == expected.Count,
            $"{scenario}: recovery produces every remaining sample");
        Check.That(expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick &&
            pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
            $"{scenario}: recovery preserves sample times and projected voltages");
    }
}
