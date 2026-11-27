// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Network;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class ClassroomSynchronizationSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(ClockCalibrationRejectsQueueNoiseAndFreezesCohortDelay), ClockCalibrationRejectsQueueNoiseAndFreezesCohortDelay)
    ];

    private static void ClockCalibrationRejectsQueueNoiseAndFreezesCohortDelay()
    {
        var calibration = new ClassroomClockCalibration();
        Check.That(calibration.Estimate.RecommendedPlayoutDelayNs == 500_000_000, "an uncalibrated classroom uses the fallback delay");
        for (int i = 0; i < 48; i++)
        {
            long t1 = 10_000_000_000 + i * 1_000_000_000L;
            long offset = -3_000_000_000 + i * 25_000L;
            long outward = i % 5 == 0 ? 41_000_000 : 1_000_000;
            calibration.Add(new ClassroomClockSample(t1, t1 + outward + offset, t1 + outward + offset + 100, t1 + outward + 1_000_000 + 100));
        }
        ClassroomClockEstimate estimate = calibration.Estimate;
        Check.That(estimate.SampleCount == 32 && Math.Abs(estimate.DriftPpm - 25) < 0.01 &&
            Math.Abs(estimate.HostOffsetNs - (-3_000_000_000L + 47 * 25_000L)) < 200 &&
            estimate.RecommendedPlayoutDelayNs == 250_000_000 && !estimate.RequiresResynchronization,
            "the recent low-RTT regression follows the known drift despite occasional asymmetric queueing");
        var slow = estimate with { RecommendedPlayoutDelayNs = 950_000_000 };
        var pathological = estimate with { RecommendedPlayoutDelayNs = 1_500_000_000, RequiresResynchronization = true };
        Check.That(ClassroomClockCalibration.SelectCohortDelayNs([estimate, slow, pathological]) == 950_000_000 &&
            ClassroomClockCalibration.SelectCohortDelayNs([pathological]) == 500_000_000 &&
            ClassroomClockCalibration.SelectCohortDelayNs([], 300_000_000) == 300_000_000,
            "a shared cohort can freeze one appropriate delay or use an explicit fixed value");
    }

    internal static async Task VerifyHealthAsync(ClassroomStudentClient client, WaveformEnvelope accepted, CancellationToken ct)
    {
        Guid plane = accepted.Planes[0].ChannelId;
        var live = await ClassroomSynchronization.StartAsync(client, [plane], false, ct).ConfigureAwait(false);
        Check.That(live.Estimate.SampleCount == 16 && live.PresentationHostTimeNs() >= 0,
            "the authenticated student calibrates against sixteen four-timestamp host replies");
        var time = new MonotonicTestClock();
        var session = new ClassroomSynchronization(client, new ClassroomClockCalibration(), time, [plane], false);
        session.RecordBlock(accepted);
        for (int i = 0; i < 4; i++) { time.Advance(); session.RecordHeartbeat(false); }
        Check.That(session.Health.RequiredPlanes.Single().Health == SamplePlaneProgressHealth.Healthy,
            "pausing the simulation suspends required-plane stall time");
        session.RecordHeartbeat(true);
        time.Advance();
        session.RecordHeartbeat(true);
        Check.That(session.Health.ConnectionState == ConnectionState.Connected &&
            session.Health.RequiredPlanes.Single().Health == SamplePlaneProgressHealth.SamplePlaneStalled,
            "a responsive control channel cannot hide a stalled required sample plane");
        time.Advance();
        session.RecordHeartbeat(true);
        Check.That(session.Health.RequiredPlanes.Single().Health == SamplePlaneProgressHealth.Disconnected,
            "stalled sample data expires independently of the transport heartbeat");
        session.RecordFailure(ImmediateTransportFailure.WebSocketClose);
        session.BeginRelocking();
        session.CompleteRelocking(accepted);
        Check.That(session.Health.ConnectionState == ConnectionState.Connected &&
            session.Health.RequiredPlanes.Single().Health == SamplePlaneProgressHealth.Healthy &&
            session.HealthChanges.TryRead(out var latest) && latest.ConnectionState == ConnectionState.Connected,
            "verified relock resets both connectivity and the cursor without waiting for a status consumer");
    }

    private sealed class MonotonicTestClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1_000_000_000;
        public override long GetTimestamp() => _timestamp;
        internal void Advance() => _timestamp += TimestampFrequency;
    }

}
