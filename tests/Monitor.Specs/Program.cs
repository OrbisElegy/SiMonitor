// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Specs;

internal static class Program
{
    public static int Main()
    {
        Specification[] specifications =
        [
            .. TherapySpecifications.All,
            .. AssessmentSpecifications.All,
            .. IdentitySpecifications.All,
            .. AuthenticationSpecifications.All,
            .. PersistenceSpecifications.All,
            .. KeyProtectionSpecifications.All,
            .. BackupSpecifications.All,
            .. DeterminismSpecifications.All,
            .. NumericDeterminismSpecifications.All,
            .. AuthorityOrderingSpecifications.All,
            .. DeterminismKernelSpecifications.All,
            .. SignalSampleClockSpecifications.All,
            .. SignalAcquisitionDelaySpecifications.All,
            .. SensorFaultSpecifications.All,
            .. WaveformEnvelopeCodecSpecifications.All,
            .. WaveformBlockAssemblerSpecifications.All,
            .. WaveformBlockRingSpecifications.All,
            .. WaveformRecordArchiveSpecifications.All,
            .. WaveformRecoveryPlannerSpecifications.All,
            .. ConnectionHealthSpecifications.All,
            .. DataContinuitySpecifications.All,
            .. NoDataPresentationSpecifications.All,
            .. NoDataSweepSpecifications.All,
            .. SweepStateProjectionSpecifications.All,
            .. FillOnceThenHoldSpecifications.All,
            .. ContinuitySignatureSpecifications.All,
            .. ContinuityCapsuleChainSpecifications.All,
            .. ContinuityShadowSpecifications.All,
            .. ClientRecoverySessionSpecifications.All,
            .. RecoveryHandshakeSpecifications.All,
            .. RecoveryResyncPlanFactorySpecifications.All,
            .. RecoveryRelockSpecifications.All,
            .. RecoveryWireCodecSpecifications.All,
            .. WaveformSubscriberQueueSpecifications.All,
        ];

        for (int index = 0; index < specifications.Length; index++)
        {
            specifications[index].Body();
            Console.WriteLine($"ok {index + 1} - {specifications[index].Name}");
        }

        Console.WriteLine($"ok: {specifications.Length} executable specifications passed");
        return 0;
    }
}

internal sealed record Specification(string Name, Action Body);

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
