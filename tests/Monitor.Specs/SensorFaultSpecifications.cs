// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class SensorFaultSpecifications
{
    private static readonly string[] AllCategories = ["Adult", "Pediatric", "Neonatal"];
    private static readonly string[] ExpectedActiveSpO2Faults = ["Motion", "SensorOff"];

    public static Specification[] All =>
    [
        new(nameof(FrozenSensorProfilesMatchTheClosedPackage),
            FrozenSensorProfilesMatchTheClosedPackage),
        new(nameof(FaultAdmissionIsClosedAndOriginAware), FaultAdmissionIsClosedAndOriginAware),
        new(nameof(FaultTransitionsAreAtomicAndCanonical), FaultTransitionsAreAtomicAndCanonical),
        new(nameof(FaultCheckpointRestoresWithoutGuessing), FaultCheckpointRestoresWithoutGuessing),
    ];

    private static void FrozenSensorProfilesMatchTheClosedPackage()
    {
        SensorProfileVector[] vectors =
        [
            new("SensorECGGeneric@1", "ECGElectrodes",
                ["LeadOff", "Noise", "Disconnected"]),
            new("SensorSpO2Generic@1", "OpticalOximetry",
                ["LowPerfusion", "Motion", "SensorOff", "Disconnected"]),
            new("SensorNIBPCuffGeneric@1", "NIBPCuff",
                ["LooseCuff", "Overpressure", "Timeout", "Disconnected"]),
            new("SensorPressureGeneric@1", "PressureTransducer",
                ["NotZeroed", "Overdamped", "Underdamped", "Disconnected"]),
            new("SensorCO2Generic@1", "SidestreamCO2",
                ["Occluded", "SampleLineOff", "NoBreath", "Disconnected"]),
            new("SensorTempGeneric@1", "TemperatureProbe",
                ["ProbeOff", "OutOfRange", "Disconnected"]),
        ];

        foreach (SensorProfileVector vector in vectors)
        {
            SensorProfileDescriptor profile = FrozenSensorProfiles.Get(vector.ProfileId);
            Check.That(profile.SensorClass == vector.SensorClass &&
                profile.CompatibleCategories.SequenceEqual(AllCategories) &&
                profile.FailureStates.SequenceEqual(vector.FailureStates),
                $"sensor profile metadata must match the closed package: {vector.ProfileId}");
        }

        Check.That(Reason(() => FrozenSensorProfiles.Get("SensorUnknown@1")) ==
            "SensorFault.UnknownProfile",
            "a sensor profile outside the frozen package must reject");
    }

    private static void FaultAdmissionIsClosedAndOriginAware()
    {
        var ecg = SensorFaultStateMachine.Start(
            "SensorECGGeneric@1",
            "ecg-main",
            0);
        SensorFaultTransition leadOff = ecg.Apply(
            SensorFaultOperation.Start,
            "LeadOff",
            SensorFaultOrigin.ScenarioOverride,
            10,
            0);
        Check.That(leadOff == new SensorFaultTransition(
            "SensorECGGeneric@1",
            "ecg-main",
            SensorFaultOperation.Start,
            "LeadOff",
            SensorFaultOrigin.ScenarioOverride,
            10,
            0,
            1),
            "a registered scenario fault must produce an explicit transition");

        SensorFaultState beforeUnsupported = ecg.CaptureState();
        Check.That(Reason(() => ecg.Apply(
            SensorFaultOperation.Start,
            "Motion",
            SensorFaultOrigin.ScenarioOverride,
            11,
            1)) == "SensorFault.UnsupportedFailureState" &&
            Equivalent(ecg.CaptureState(), beforeUnsupported),
            "a failure from another sensor profile must reject without mutation");

        var spo2 = SensorFaultStateMachine.Start(
            "SensorSpO2Generic@1",
            "spo2-main",
            0);
        SensorFaultState beforeDirectLowPerfusion = spo2.CaptureState();
        Check.That(Reason(() => spo2.Apply(
            SensorFaultOperation.Start,
            "LowPerfusion",
            SensorFaultOrigin.ScenarioOverride,
            1,
            0)) == "SensorFault.OriginNotAllowed" &&
            Equivalent(spo2.CaptureState(), beforeDirectLowPerfusion),
            "local low perfusion cannot be injected as an arbitrary final fault override");
        _ = spo2.Apply(
            SensorFaultOperation.Start,
            "LowPerfusion",
            SensorFaultOrigin.LatentPhysiology,
            1,
            0);
        Check.That(spo2.ActiveFaults.Single().Origin == SensorFaultOrigin.LatentPhysiology,
            "low perfusion must retain its physiological provenance");
    }

    private static void FaultTransitionsAreAtomicAndCanonical()
    {
        var faults = SensorFaultStateMachine.Start(
            "SensorSpO2Generic@1",
            "left-finger",
            100);
        _ = faults.Apply(
            SensorFaultOperation.Start,
            "SensorOff",
            SensorFaultOrigin.ScenarioOverride,
            110,
            0);
        _ = faults.Apply(
            SensorFaultOperation.Start,
            "Motion",
            SensorFaultOrigin.SensorModel,
            110,
            1);
        Check.That(faults.ActiveFaults.Select(fault => fault.FailureState).SequenceEqual(
            ExpectedActiveSpO2Faults),
            "active faults must use the profile's canonical order, not arrival order");

        SensorFaultState stable = faults.CaptureState();
        Check.That(Reason(() => faults.Apply(
            SensorFaultOperation.Start,
            "Motion",
            SensorFaultOrigin.SensorModel,
            111,
            2)) == "SensorFault.AlreadyActive" &&
            Equivalent(faults.CaptureState(), stable),
            "starting an active fault must reject without mutation");
        Check.That(Reason(() => faults.Apply(
            SensorFaultOperation.Stop,
            "Motion",
            SensorFaultOrigin.ScenarioOverride,
            111,
            2)) == "SensorFault.OriginMismatch" &&
            Equivalent(faults.CaptureState(), stable),
            "one source cannot silently clear another source's fault");
        Check.That(Reason(() => faults.Apply(
            SensorFaultOperation.Stop,
            "Motion",
            SensorFaultOrigin.SensorModel,
            111,
            1)) == "SensorFault.RevisionConflict" &&
            Equivalent(faults.CaptureState(), stable),
            "a stale effect revision must reject without mutation");
        Check.That(Reason(() => faults.Apply(
            SensorFaultOperation.Stop,
            "Motion",
            SensorFaultOrigin.SensorModel,
            99,
            2)) == "SensorFault.TimeReversed" &&
            Equivalent(faults.CaptureState(), stable),
            "fault time cannot move backwards");

        SensorFaultTransition stopped = faults.Apply(
            SensorFaultOperation.Stop,
            "Motion",
            SensorFaultOrigin.SensorModel,
            111,
            2);
        Check.That(stopped.PreviousRevision == 2 && stopped.Revision == 3 &&
            faults.ActiveFaults.Single().FailureState == "SensorOff",
            "a valid stop must advance exactly one revision and retain unrelated faults");
    }

    private static void FaultCheckpointRestoresWithoutGuessing()
    {
        var original = SensorFaultStateMachine.Start(
            "SensorPressureGeneric@1",
            "art-main",
            50);
        _ = original.Apply(
            SensorFaultOperation.Start,
            "Underdamped",
            SensorFaultOrigin.ScenarioOverride,
            60,
            0);
        _ = original.Apply(
            SensorFaultOperation.Start,
            "NotZeroed",
            SensorFaultOrigin.SensorModel,
            70,
            1);
        SensorFaultState checkpoint = original.CaptureState();
        var restored = SensorFaultStateMachine.Restore(checkpoint);
        SensorFaultTransition expected = original.Apply(
            SensorFaultOperation.Stop,
            "Underdamped",
            SensorFaultOrigin.ScenarioOverride,
            80,
            2);
        SensorFaultTransition actual = restored.Apply(
            SensorFaultOperation.Stop,
            "Underdamped",
            SensorFaultOrigin.ScenarioOverride,
            80,
            2);
        Check.That(actual == expected &&
            Equivalent(restored.CaptureState(), original.CaptureState()),
            "restored faults must produce the same future transition and state");

        SensorFaultState reordered = checkpoint with
        {
            ActiveFaults = checkpoint.ActiveFaults.Reverse().ToArray(),
        };
        Check.That(Reason(() => SensorFaultStateMachine.Restore(reordered)) ==
            "SensorFault.InvalidCheckpoint",
            "a checkpoint cannot restore non-canonical active fault ordering");
        SensorFaultState invalidOrigin = checkpoint with
        {
            ActiveFaults =
            [
                checkpoint.ActiveFaults[0] with
                {
                    Origin = SensorFaultOrigin.LatentPhysiology,
                },
                .. checkpoint.ActiveFaults.Skip(1),
            ],
        };
        Check.That(Reason(() => SensorFaultStateMachine.Restore(invalidOrigin)) ==
            "SensorFault.InvalidCheckpoint",
            "a checkpoint cannot bypass the frozen fault-origin policy");
    }

    private static bool Equivalent(SensorFaultState left, SensorFaultState right) =>
        left.ProfileId == right.ProfileId &&
        left.SensorInstanceId == right.SensorInstanceId &&
        left.Revision == right.Revision &&
        left.CursorSimTimeNs == right.CursorSimTimeNs &&
        left.ActiveFaults.SequenceEqual(right.ActiveFaults);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (SensorFaultException exception)
        {
            return exception.ReasonCode;
        }
    }

    private sealed record SensorProfileVector(
        string ProfileId,
        string SensorClass,
        string[] FailureStates);
}
