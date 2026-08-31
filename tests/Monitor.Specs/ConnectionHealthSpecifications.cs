// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Specs;

internal static class ConnectionHealthSpecifications
{
    private static readonly Guid PlaneA =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PlaneB =
        Guid.Parse("22222222-2222-4222-8222-222222222222");

    public static Specification[] All =>
    [
        new(nameof(HeartbeatThresholdsUseAuthorityClock),
            HeartbeatThresholdsUseAuthorityClock),
        new(nameof(TransportFailuresDisconnectImmediately),
            TransportFailuresDisconnectImmediately),
        new(nameof(PlaneProgressRemainsIndependentFromControlHealth),
            PlaneProgressRemainsIndependentFromControlHealth),
        new(nameof(PausedSimulationFreezesOnlyPlaneProgressClock),
            PausedSimulationFreezesOnlyPlaneProgressClock),
        new(nameof(CheckpointAndInvalidTransitionsFailClosed),
            CheckpointAndInvalidTransitionsFailClosed),
    ];

    private static void HeartbeatThresholdsUseAuthorityClock()
    {
        ConnectionHealthStateMachine machine = Start(sessionRunning: false);
        Check.That(machine.Advance(2_999_999_999, false).ConnectionState ==
                ConnectionState.Connected &&
            machine.Advance(3_000_000_000, false).ConnectionState ==
                ConnectionState.Suspect,
            "three seconds without valid activity must enter Suspect exactly");

        Check.That(machine.RecordServerActivity(4_000_000_000).ConnectionState ==
                ConnectionState.Connected &&
            machine.Advance(8_999_999_999, false).ConnectionState ==
                ConnectionState.Suspect &&
            machine.Advance(9_000_000_000, false).ConnectionState ==
                ConnectionState.Disconnected,
            "activity must reset silence while five seconds must latch Disconnected");
        Check.That(machine.RecordServerActivity(10_000_000_000).ConnectionState ==
            ConnectionState.Disconnected,
            "late activity cannot bypass application-level relocking");

        ConnectionHealthStateMachine exactDeadline = Start(sessionRunning: false);
        Check.That(exactDeadline.RecordServerActivity(5_000_000_000).ConnectionState ==
            ConnectionState.Disconnected,
            "the disconnect deadline must win over same-time receive callback ordering");

        _ = machine.BeginRelocking(10_000_000_000);
        Check.That(machine.CompleteRelocking(11_000_000_000).ConnectionState ==
            ConnectionState.Connected,
            "an explicit relocking transition may establish a new connected anchor");
    }

    private static void TransportFailuresDisconnectImmediately()
    {
        foreach (ImmediateTransportFailure failure in
            Enum.GetValues<ImmediateTransportFailure>())
        {
            ConnectionHealthStateMachine machine = Start(sessionRunning: false);
            ConnectionHealthState state = machine.RecordImmediateFailure(failure, 1);
            Check.That(state.ConnectionState == ConnectionState.Disconnected &&
                state.LastImmediateFailure == failure,
                "Close, I/O and TLS failures must each disconnect immediately");
        }
    }

    private static void PlaneProgressRemainsIndependentFromControlHealth()
    {
        ConnectionHealthStateMachine machine = Start(sessionRunning: true);
        Check.That(machine.RecordPlaneProgress(PlaneA, 4, 10, 0) ==
                PlaneProgressUpdateStatus.Accepted &&
            machine.RecordPlaneProgress(PlaneB, 4, 10, 0) ==
                PlaneProgressUpdateStatus.Accepted,
            "the first validated block must establish each required plane cursor");
        _ = machine.RecordServerActivity(900_000_000);

        ConnectionHealthState stalled = machine.Advance(1_000_000_000, true);
        Check.That(stalled.ConnectionState == ConnectionState.Connected &&
            stalled.RequiredPlanes.All(plane =>
                plane.Health == SamplePlaneProgressHealth.SamplePlaneStalled),
            "healthy control activity cannot conceal a one-second plane stall");
        Check.That(machine.RecordPlaneProgress(PlaneA, 4, 10, 1_000_000_000) ==
                PlaneProgressUpdateStatus.IgnoredDuplicateOrStale &&
            machine.RecordPlaneProgress(PlaneA, 4, 12, 1_200_000_000) ==
                PlaneProgressUpdateStatus.IgnoredDiscontinuous,
            "duplicate and skipped sequences cannot reset the progress deadline");
        Check.That(machine.RecordPlaneProgress(PlaneA, 4, 11, 1_500_000_000) ==
            PlaneProgressUpdateStatus.Accepted,
            "the exact next block may recover a stalled plane before disconnection");

        ConnectionHealthState disconnected = machine.Advance(2_000_000_000, true);
        RequiredPlaneProgressState planeA = disconnected.RequiredPlanes.Single(
            plane => plane.PlaneId == PlaneA);
        RequiredPlaneProgressState planeB = disconnected.RequiredPlanes.Single(
            plane => plane.PlaneId == PlaneB);
        Check.That(planeA.Health == SamplePlaneProgressHealth.Healthy &&
            planeB.Health == SamplePlaneProgressHealth.Disconnected &&
            machine.RecordPlaneProgress(PlaneB, 4, 11, 2_000_000_000) ==
                PlaneProgressUpdateStatus.RequiresResync,
            "two seconds without progress must latch only the affected plane");
        Check.That(machine.ResetPlaneAfterResync(PlaneB, 5, 20, 2_100_000_000)
            .RequiredPlanes.Single(plane => plane.PlaneId == PlaneB).Health ==
                SamplePlaneProgressHealth.Healthy,
            "a disconnected plane requires an explicit resynchronization cursor");
    }

    private static void PausedSimulationFreezesOnlyPlaneProgressClock()
    {
        ConnectionHealthStateMachine machine = Start(sessionRunning: true);
        _ = machine.RecordPlaneProgress(PlaneA, 4, 10, 0);
        _ = machine.RecordPlaneProgress(PlaneB, 4, 10, 0);
        ConnectionHealthState paused = machine.Advance(500_000_000, false);
        ConnectionHealthState afterPause = machine.Advance(30_500_000_000, false);
        Check.That(afterPause.RunningTimeNs == paused.RunningTimeNs &&
            afterPause.RequiredPlanes.All(plane =>
                plane.Health == SamplePlaneProgressHealth.Healthy) &&
            afterPause.ConnectionState == ConnectionState.Disconnected,
            "pause must freeze plane deadlines but not authority-clock heartbeat expiry");

        _ = machine.Advance(30_500_000_000, true);
        ConnectionHealthState resumed = machine.Advance(31_000_000_000, true);
        Check.That(resumed.RequiredPlanes.All(plane =>
            plane.Health == SamplePlaneProgressHealth.SamplePlaneStalled),
            "resume must continue the pre-pause running-time age without catch-up");
    }

    private static void CheckpointAndInvalidTransitionsFailClosed()
    {
        ConnectionHealthStateMachine original = Start(sessionRunning: false);
        ConnectionHealthState checkpoint = original.Advance(3_000_000_000, false);
        var restored = ConnectionHealthStateMachine.Restore(
            checkpoint);
        Check.That(Equivalent(restored.CaptureState(), checkpoint) &&
            Equivalent(
                restored.RecordServerActivity(4_000_000_000),
                original.RecordServerActivity(4_000_000_000)),
            "restoring a Suspect connection must preserve its exact future transition");

        ConnectionHealthState corrupt = checkpoint with
        {
            RequiredPlanes =
            [
                checkpoint.RequiredPlanes[0] with
                {
                    Health = SamplePlaneProgressHealth.Disconnected,
                },
                .. checkpoint.RequiredPlanes.Skip(1),
            ],
        };
        Check.That(Reason(() => ConnectionHealthStateMachine.Restore(corrupt)) ==
            "ConnectionHealth.InvalidCheckpoint",
            "checkpoint plane health must be derivable from the running-time clock");

        RealtimeConnectionHealthProfile invalidProfile =
            RealtimeConnectionHealthProfile.Default with
            {
                HeartbeatIntervalNs = 3_000_000_000,
            };
        Check.That(Reason(() => ConnectionHealthStateMachine.Start(
                invalidProfile,
                0,
                false,
                new[] { PlaneA })) == "ConnectionHealth.InvalidProfile",
            "disconnect timeout must remain at least two heartbeat intervals");

        ConnectionHealthStateMachine connected = Start(sessionRunning: false);
        ConnectionHealthState unchanged = connected.CaptureState();
        Check.That(Reason(() => connected.BeginRelocking(1)) ==
                "ConnectionHealth.InvalidTransition" &&
            Equivalent(connected.CaptureState(), unchanged),
            "an invalid relocking transition must reject without advancing state");
    }

    private static ConnectionHealthStateMachine Start(bool sessionRunning) =>
        ConnectionHealthStateMachine.Start(
            RealtimeConnectionHealthProfile.Default,
            0,
            sessionRunning,
            new[] { PlaneB, PlaneA });

    private static bool Equivalent(ConnectionHealthState left, ConnectionHealthState right) =>
        left.Profile == right.Profile &&
        left.LastAuthorityMonotonicNs == right.LastAuthorityMonotonicNs &&
        left.LastServerActivityAtNs == right.LastServerActivityAtNs &&
        left.RunningTimeNs == right.RunningTimeNs &&
        left.SessionRunning == right.SessionRunning &&
        left.ConnectionState == right.ConnectionState &&
        left.LastImmediateFailure == right.LastImmediateFailure &&
        left.RequiredPlanes.SequenceEqual(right.RequiredPlanes);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (ConnectionHealthException exception)
        {
            return exception.ReasonCode;
        }
    }
}
