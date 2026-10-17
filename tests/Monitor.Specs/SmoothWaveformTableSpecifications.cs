// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SmoothWaveformTableSpecifications
{
    private const long Q = FixedPointMath.Q32One;

    public static Specification[] All =>
    [
        new(nameof(AuthoredLandmarksRetainBoundsAndFlatSegments), AuthoredLandmarksRetainBoundsAndFlatSegments),
        new(nameof(PhysicalTimeSlopesRemainSmoothAcrossPhaseBoundaries), PhysicalTimeSlopesRemainSmoothAcrossPhaseBoundaries),
        new(nameof(SmoothSupportMeetsZeroWithVanishingSlope), SmoothSupportMeetsZeroWithVanishingSlope),
        new(nameof(ConstructionIsDeterministicAcrossExtremeValidTiming), ConstructionIsDeterministicAcrossExtremeValidTiming),
        new(nameof(InvalidAuthoredInputsAreRejected), InvalidAuthoredInputsAreRejected),
    ];

    private static void AuthoredLandmarksRetainBoundsAndFlatSegments()
    {
        (int Index, long ValueQ32)[] landmarks =
        [(0, 0), (256, 100 * Q), (512, 100 * Q), (768, 20 * Q), (1024, 80 * Q), (1536, -20 * Q), (2048, 0)];
        long[] values = SmoothWaveformTable.Create(2048, landmarks);
        Check.That(values.Length == 2048, "table contains exactly the half-open support");
        for (int segment = 0; segment < landmarks.Length - 1; segment++)
        {
            var left = landmarks[segment];
            var right = landmarks[segment + 1];
            Check.That(values[left.Index] == left.ValueQ32, "every authored landmark is exact");
            for (int index = left.Index; index < right.Index; index++)
            {
                Check.That(values[index] >= Math.Min(left.ValueQ32, right.ValueQ32) &&
                    values[index] <= Math.Max(left.ValueQ32, right.ValueQ32), "each segment remains within its authored amplitude bounds");
                long next = index + 1 == values.Length ? 0 : values[index + 1];
                Check.That(right.ValueQ32 >= left.ValueQ32 ? next >= values[index] : next <= values[index],
                    "monotone intervals never reverse or overshoot, including flat segments");
            }
        }
    }

    private static void PhysicalTimeSlopesRemainSmoothAcrossPhaseBoundaries()
    {
        (int Index, long ValueQ32)[] landmarks =
        [(0, 0), (1024, 100 * Q), (2048, 160 * Q), (3072, 200 * Q), (4096, 0)];
        EventWaveformPhasePoint[] phases =
        [new(0, 0), new(1_024_000, 1024), new(9_216_000, 3072), new(10_240_000, 4096)];
        long[] values = SmoothWaveformTable.Create(4096, landmarks, phases);
        decimal incoming = (decimal)(values[1024] - values[1023]) / 1000;
        decimal outgoing = (decimal)(values[1025] - values[1024]) / 4000;
        Check.That(Math.Abs(incoming - outgoing) < incoming / 50,
            "equal physical-time slopes cross a fourfold table-speed change without a corner");
        // This phase boundary is deliberately not an authored landmark.
        EventWaveformPhasePoint[] splitPhases =
        [new(0, 0), new(1_536_000, 1536), new(11_776_000, 4096)];
        long[] splitValues = SmoothWaveformTable.Create(4096, landmarks, splitPhases);
        incoming = (decimal)(splitValues[1536] - splitValues[1535]) / 1000;
        outgoing = (decimal)(splitValues[1537] - splitValues[1536]) / 4000;
        Check.That(Math.Abs(incoming - outgoing) < incoming / 50,
            "a speed change inside a cubic segment also retains physical-time continuity");
    }

    private static void SmoothSupportMeetsZeroWithVanishingSlope()
    {
        long[] values = SmoothWaveformTable.Create(2048, [(0, 0), (1024, 100 * Q), (2048, 0)]);
        Check.That(values[0] == 0 && values[1024] == 100 * Q && values[^1] > 0 &&
            values[1] < Q / 1000 && values[^1] < Q / 1000,
            "both support endpoints approach baseline with zero derivative");
        Check.That(values[2] > 3 * values[1] && values[^2] > 3 * values[^1] &&
            100 * Q - values[1023] < Q / 1000 && 100 * Q - values[1025] < Q / 1000,
            "support boundaries and the local maximum have curved, vanishing slopes");
    }

    private static void ConstructionIsDeterministicAcrossExtremeValidTiming()
    {
        (int Index, long ValueQ32)[] landmarks =
        [(0, 0), (1024, short.MaxValue * Q), (2048, 1), (3072, short.MinValue * Q), (4096, 0)];
        EventWaveformPhasePoint[] phases =
        [new(0, 0), new(1, 1024), new(long.MaxValue - 1, 3072), new(long.MaxValue, 4096)];
        long[] first = SmoothWaveformTable.Create(4096, landmarks, phases);
        long[] second = SmoothWaveformTable.Create(4096, landmarks, phases);
        Check.That(first.SequenceEqual(second) && first.All(value => value >= short.MinValue * Q && value <= short.MaxValue * Q),
            "one-nanosecond phases next to maximum durations remain bounded and deterministic");
        long[] small = SmoothWaveformTable.Create(8, [(0, 0), (4, 1), (8, 0)]);
        Check.That(small[2] == 0 && small[6] == 0, "half-Q32-unit samples round ties to even");
    }

    private static void InvalidAuthoredInputsAreRejected()
    {
        Reject(() => SmoothWaveformTable.Create(12, [(0, 0), (12, 0)]), "sampleCount");
        Reject(() => SmoothWaveformTable.Create(16, [(0, 1), (16, 0)]), "landmarks");
        Reject(() => SmoothWaveformTable.Create(16, [(0, 0), (8, Q), (8, Q), (16, 0)]), "landmarks");
        Reject(() => SmoothWaveformTable.Create(16, [(0, 0), (8, long.MaxValue), (16, 0)]), "landmarks");
        Reject(() => SmoothWaveformTable.Create(16, [(0, 0), (16, 0)], [new(0, 0), new(0, 16)]), "phases");
        Reject(() => SmoothWaveformTable.Create(16, [(0, 0), (16, 0)], [new(0, 0), new(10, 8), new(20, 8), new(30, 16)]), "phases");
    }

    private static void Reject(Action action, string parameter)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentException exception) { rejected = exception.ParamName == parameter; }
        Check.That(rejected, $"invalid {parameter} is rejected before a table is returned");
    }
}
