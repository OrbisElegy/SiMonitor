// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class NativeRecoveryChecks
{
    public static IReadOnlyList<byte[]> Verify(Func<PhysiologyWaveformGroup> createGroup,
        IReadOnlyList<long> boundariesNs, int maximumSamplesPerChannel, int maximumBlocks,
        int maximumEvents, int expectedBlocks, string scenario)
    {
        var expected = createGroup().AdvanceTo(boundariesNs[^1], maximumSamplesPerChannel, maximumBlocks, maximumEvents);
        var group = createGroup();
        List<byte[]> actual = [];
        for (int index = 0; index < boundariesNs.Count; index++)
        {
            actual.AddRange(group.AdvanceTo(boundariesNs[index], maximumSamplesPerChannel, maximumBlocks, maximumEvents));
            if (index + 1 < boundariesNs.Count)
            {
                group = PhysiologyWaveformGroup.Restore(group.CaptureState());
            }
        }
        Check.That(expected.Count == expectedBlocks && actual.Count == expected.Count &&
            expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            $"{scenario}: every native block survives recovery at phase, pending-output and cycle boundaries");
        return actual;
    }
}
