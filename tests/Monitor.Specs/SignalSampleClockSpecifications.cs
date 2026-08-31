// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class SignalSampleClockSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FrozenProfilesProduceExactBlockGrids), FrozenProfilesProduceExactBlockGrids),
        new(nameof(DrainChunkingDoesNotChangeSamples), DrainChunkingDoesNotChangeSamples),
        new(nameof(ProfileBoundaryCreatesANewContinuousEpoch),
            ProfileBoundaryCreatesANewContinuousEpoch),
        new(nameof(ClockCheckpointRestoresWithoutGuessing), ClockCheckpointRestoresWithoutGuessing),
    ];

    private static void FrozenProfilesProduceExactBlockGrids()
    {
        ProfileVector[] vectors =
        [
            new("AcqECGMonitor250@1", "ECG-monitor", 250, 40_000_000, 50),
            new("AcqResp125@1", "IMP-Resp", 125, 80_000_000, 25),
            new("AcqPleth125@1", "Pleth", 125, 2_000_000_000, 25),
            new("AcqPressure125@1", "Pressure", 125, 80_000_000, 25),
            new("AcqCO2_100@1", "CO2", 100, 2_000_000_000, 20),
        ];

        foreach (ProfileVector vector in vectors)
        {
            SignalAcquisitionProfileDescriptor profile =
                FrozenSignalAcquisitionProfiles.Get(vector.ProfileId);
            Check.That(profile.ChannelClass == vector.ChannelClass &&
                profile.SampleRateHz == vector.SampleRateHz &&
                profile.LatencyNs == vector.LatencyNs &&
                profile.FilterPrimitive == "Identity@1" &&
                profile.Quantization == "Int16Normalized" &&
                profile.EvidenceStatus == "ProjectTeachingProfile",
                $"frozen acquisition metadata must match: {vector.ProfileId}");

            var clock = SignalSampleClock.Start(vector.ProfileId, 7, 0);
            IReadOnlyList<SignalSampleTick> samples = clock.DrainBefore(200_000_000);
            Check.That(samples.Count == vector.SamplesPerBlock,
                $"a 200 ms block must contain the frozen sample count: {vector.ProfileId}");
            Check.That(samples[0] == new SignalSampleTick(vector.ProfileId, 7, 0, 0) &&
                samples[^1].SampleIndex == (ulong)(vector.SamplesPerBlock - 1),
                "the first block must start at index zero and the epoch anchor");
            Check.That(samples.Zip(samples.Skip(1)).All(pair =>
                pair.Second.SimTimeNs - pair.First.SimTimeNs == profile.SamplePeriodNs),
                "every sample interval must exactly match the frozen profile");
        }
    }

    private static void DrainChunkingDoesNotChangeSamples()
    {
        var oneBlock = SignalSampleClock.Start("AcqECGMonitor250@1", 1, 0);
        SignalSampleTick[] expected = [.. oneBlock.DrainBefore(2_000_000_000)];

        var chunked = SignalSampleClock.Start("AcqECGMonitor250@1", 1, 0);
        List<SignalSampleTick> actual = [];
        foreach (long boundary in new long[]
        {
            200_000_000,
            333_000_000,
            777_000_000,
            1_000_000_000,
            2_000_000_000,
        })
        {
            actual.AddRange(chunked.DrainBefore(boundary));
        }

        SignalSampleClockState paused = chunked.CaptureState();
        Check.That(chunked.DrainBefore(2_000_000_000).Count == 0 &&
            chunked.CaptureState() == paused,
            "an unchanged simulation time must not emit catch-up samples");
        Check.That(actual.SequenceEqual(expected),
            "publish or processing chunk boundaries must not change the sample timeline");
    }

    private static void ProfileBoundaryCreatesANewContinuousEpoch()
    {
        var clock = SignalSampleClock.Start("AcqECGMonitor250@1", 4, 0);
        SignalSampleTick[] previous = [.. clock.DrainBefore(200_000_000)];
        SignalSampleClockState beforeRejectedChange = clock.CaptureState();
        Check.That(Reason(() => clock.ApplyProfileBoundary(
            "AcqCO2_100@1",
            5,
            200_000_000,
            FilterStateTransitionPolicy.Clear,
            true)) == "SignalSampleClock.StandardRecordActive",
            "an active standard ECG record must block a profile change");
        Check.That(clock.CaptureState() == beforeRejectedChange,
            "a rejected profile boundary must leave the clock unchanged");

        SignalProfileBoundary boundary = clock.ApplyProfileBoundary(
            "AcqCO2_100@1",
            5,
            200_000_000,
            FilterStateTransitionPolicy.Clear,
            false);
        SignalSampleTick[] next = [.. clock.DrainBefore(400_000_000)];
        Check.That(boundary == new SignalProfileBoundary(
            "AcqECGMonitor250@1",
            "AcqCO2_100@1",
            4,
            5,
            200_000_000,
            FilterStateTransitionPolicy.Clear),
            "the boundary must record old/new profile, epoch, time and filter policy");
        Check.That(next.Length == 20 &&
            next[0] == new SignalSampleTick("AcqCO2_100@1", 5, 0, 200_000_000),
            "the new epoch must start at the boundary with a reset sample index");
        Check.That(previous[^1].SimTimeNs < next[0].SimTimeNs,
            "profile changes must preserve monotonic simulation time");
    }

    private static void ClockCheckpointRestoresWithoutGuessing()
    {
        var original = SignalSampleClock.Start("AcqResp125@1", 12, 20_000_000);
        _ = original.DrainBefore(123_000_000);
        SignalSampleClockState checkpoint = original.CaptureState();
        var restored = SignalSampleClock.Restore(checkpoint);
        Check.That(restored.DrainBefore(500_000_000).SequenceEqual(
            original.DrainBefore(500_000_000)),
            "restored sampling must reproduce every future timestamp and index");

        SignalSampleClockState misaligned = checkpoint with
        {
            NextSampleSimTimeNs = checkpoint.NextSampleSimTimeNs + 1,
        };
        Check.That(Reason(() => SignalSampleClock.Restore(misaligned)) ==
            "SignalSampleClock.InvalidCheckpoint",
            "a checkpoint whose index and next time disagree must reject");
        SignalSampleClockState skippedHistory = checkpoint with
        {
            CursorSimTimeNs = checkpoint.EpochAnchorSimTimeNs,
        };
        Check.That(Reason(() => SignalSampleClock.Restore(skippedHistory)) ==
            "SignalSampleClock.InvalidCheckpoint",
            "a checkpoint cannot skip indexed samples ahead of its cursor");
        Check.That(Reason(() => SignalSampleClock.Start("AcqUnknown@1", 1, 0)) ==
            "SignalSampleClock.UnknownProfile",
            "an acquisition profile outside the frozen registry must reject");
    }

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (SignalSampleClockException exception)
        {
            return exception.ReasonCode;
        }
    }

    private sealed record ProfileVector(
        string ProfileId,
        string ChannelClass,
        uint SampleRateHz,
        long LatencyNs,
        int SamplesPerBlock);
}
