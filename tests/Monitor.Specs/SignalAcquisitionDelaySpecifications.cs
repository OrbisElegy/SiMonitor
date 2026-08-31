// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class SignalAcquisitionDelaySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FrozenProfilesReleaseAtExactLatency), FrozenProfilesReleaseAtExactLatency),
        new(nameof(ReleaseChunkingDoesNotChangeSamples), ReleaseChunkingDoesNotChangeSamples),
        new(nameof(InputContractRejectsWithoutPartialMutation),
            InputContractRejectsWithoutPartialMutation),
        new(nameof(DelayCheckpointRestoresPendingSamples), DelayCheckpointRestoresPendingSamples),
    ];

    private static void FrozenProfilesReleaseAtExactLatency()
    {
        string[] profileIds =
        [
            "AcqECGMonitor250@1",
            "AcqResp125@1",
            "AcqPleth125@1",
            "AcqPressure125@1",
            "AcqCO2_100@1",
        ];

        foreach (string profileId in profileIds)
        {
            SignalAcquisitionProfileDescriptor profile =
                FrozenSignalAcquisitionProfiles.Get(profileId);
            var clock = SignalSampleClock.Start(profileId, 8, 20_000_000);
            SignalSampleTick[] ticks = [.. clock.DrainBefore(220_000_000)];
            var delay =
                SignalAcquisitionDelayLine.Start(profileId, 8, 20_000_000, 512);
            for (int index = 0; index < ticks.Length; index++)
            {
                delay.Enqueue(ticks[index], checked((short)(index - 100)), (uint)(index % 3));
            }

            long firstAvailable = ticks[0].SimTimeNs + profile.LatencyNs;
            Check.That(delay.DrainAvailable(firstAvailable - 1).Count == 0,
                $"a sample cannot escape the frozen acquisition delay: {profileId}");
            IReadOnlyList<DelayedSignalSample> first = delay.DrainAvailable(firstAvailable);
            Check.That(first.Count == 1 && first[0] == new DelayedSignalSample(
                profileId,
                8,
                0,
                20_000_000,
                firstAvailable,
                -100,
                0),
                $"the first sample must release at its exact availability time: {profileId}");

            long lastAvailable = ticks[^1].SimTimeNs + profile.LatencyNs;
            IReadOnlyList<DelayedSignalSample> remaining = delay.DrainAvailable(lastAvailable);
            Check.That(remaining.Count == ticks.Length - 1 && delay.PendingCount == 0,
                $"the complete input block must leave the delay line without loss: {profileId}");
        }
    }

    private static void ReleaseChunkingDoesNotChangeSamples()
    {
        SignalSampleTick[] ticks =
        [
            .. SignalSampleClock.Start("AcqECGMonitor250@1", 3, 0)
                .DrainBefore(1_000_000_000),
        ];
        SignalAcquisitionDelayLine oneDrain = Filled(ticks);
        DelayedSignalSample[] expected = [.. oneDrain.DrainAvailable(1_040_000_000)];

        SignalAcquisitionDelayLine chunked = Filled(ticks);
        List<DelayedSignalSample> actual = [];
        foreach (long boundary in new long[]
        {
            39_999_999,
            40_000_000,
            123_000_000,
            777_000_000,
            1_040_000_000,
        })
        {
            actual.AddRange(chunked.DrainAvailable(boundary));
        }

        SignalAcquisitionDelayState paused = chunked.CaptureState();
        Check.That(chunked.DrainAvailable(1_040_000_000).Count == 0 &&
            Equivalent(chunked.CaptureState(), paused),
            "an unchanged simulation time must not repeat released samples");
        Check.That(actual.SequenceEqual(expected),
            "release polling boundaries must not change delayed samples");

        static SignalAcquisitionDelayLine Filled(IEnumerable<SignalSampleTick> source)
        {
            var delay = SignalAcquisitionDelayLine.Start(
                "AcqECGMonitor250@1",
                3,
                0,
                512);
            foreach (SignalSampleTick tick in source)
            {
                delay.Enqueue(tick, checked((short)tick.SampleIndex), 0x20);
            }

            return delay;
        }
    }

    private static void InputContractRejectsWithoutPartialMutation()
    {
        var delay = SignalAcquisitionDelayLine.Start(
            "AcqResp125@1",
            9,
            0,
            1);
        SignalAcquisitionDelayState empty = delay.CaptureState();
        Check.That(Reason(() => delay.Enqueue(
            new SignalSampleTick("AcqPressure125@1", 9, 0, 0),
            1)) == "SignalAcquisitionDelay.ProfileMismatch" &&
            Equivalent(delay.CaptureState(), empty),
            "a different acquisition profile must reject without mutation");
        Check.That(Reason(() => delay.Enqueue(
            new SignalSampleTick("AcqResp125@1", 10, 0, 0),
            1)) == "SignalAcquisitionDelay.StreamEpochMismatch" &&
            Equivalent(delay.CaptureState(), empty),
            "a different stream epoch must reject without mutation");
        Check.That(Reason(() => delay.Enqueue(
            new SignalSampleTick("AcqResp125@1", 9, 1, 8_000_000),
            1)) == "SignalAcquisitionDelay.InputIndexDiscontinuous" &&
            Equivalent(delay.CaptureState(), empty),
            "an index gap must reject without mutation");
        Check.That(Reason(() => delay.Enqueue(
            new SignalSampleTick("AcqResp125@1", 9, 0, 1),
            1)) == "SignalAcquisitionDelay.InputTimeDiscontinuous" &&
            Equivalent(delay.CaptureState(), empty),
            "a tick outside the exact sample grid must reject without mutation");

        delay.Enqueue(new SignalSampleTick("AcqResp125@1", 9, 0, 0), 7);
        SignalAcquisitionDelayState full = delay.CaptureState();
        Check.That(Reason(() => delay.Enqueue(
            new SignalSampleTick("AcqResp125@1", 9, 1, 8_000_000),
            8)) == "SignalAcquisitionDelay.CapacityExceeded" &&
            Equivalent(delay.CaptureState(), full),
            "capacity exhaustion must reject without losing the queued sample");

        _ = delay.DrainAvailable(88_000_000);
        SignalAcquisitionDelayState released = delay.CaptureState();
        Check.That(Reason(() => delay.Enqueue(
            new SignalSampleTick("AcqResp125@1", 9, 1, 8_000_000),
            8)) == "SignalAcquisitionDelay.InputArrivedTooLate" &&
            Equivalent(delay.CaptureState(), released),
            "a late input cannot be inserted behind the release cursor");
        Check.That(Reason(() => delay.DrainAvailable(87_999_999)) ==
            "SignalAcquisitionDelay.TimeReversed" &&
            Equivalent(delay.CaptureState(), released),
            "the release cursor cannot move backwards");
    }

    private static void DelayCheckpointRestoresPendingSamples()
    {
        var clock = SignalSampleClock.Start("AcqPleth125@1", 17, 100_000_000);
        var original = SignalAcquisitionDelayLine.Start(
            "AcqPleth125@1",
            17,
            100_000_000,
            128);
        foreach (SignalSampleTick tick in clock.DrainBefore(500_000_000))
        {
            original.Enqueue(tick, checked((short)(tick.SampleIndex * 2)), 4);
        }

        _ = original.DrainAvailable(2_200_000_000);
        SignalAcquisitionDelayState checkpoint = original.CaptureState();
        var restored = SignalAcquisitionDelayLine.Restore(checkpoint);
        Check.That(restored.DrainAvailable(2_500_000_000).SequenceEqual(
            original.DrainAvailable(2_500_000_000)),
            "restoring a delay checkpoint must preserve every pending sample");

        DelayedSignalSample corrupt = checkpoint.PendingSamples[0] with
        {
            AvailableSimTimeNs = checkpoint.PendingSamples[0].AvailableSimTimeNs + 1,
        };
        SignalAcquisitionDelayState invalid = checkpoint with
        {
            PendingSamples = [corrupt, .. checkpoint.PendingSamples.Skip(1)],
        };
        Check.That(Reason(() => SignalAcquisitionDelayLine.Restore(invalid)) ==
            "SignalAcquisitionDelay.InvalidCheckpoint",
            "a checkpoint cannot alter a pending sample's derived availability time");
    }

    private static bool Equivalent(
        SignalAcquisitionDelayState left,
        SignalAcquisitionDelayState right) =>
        left.ProfileId == right.ProfileId &&
        left.StreamEpoch == right.StreamEpoch &&
        left.EpochAnchorSimTimeNs == right.EpochAnchorSimTimeNs &&
        left.NextInputSampleIndex == right.NextInputSampleIndex &&
        left.ReleaseCursorSimTimeNs == right.ReleaseCursorSimTimeNs &&
        left.Capacity == right.Capacity &&
        left.PendingSamples.SequenceEqual(right.PendingSamples);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (SignalAcquisitionDelayException exception)
        {
            return exception.ReasonCode;
        }
    }
}
