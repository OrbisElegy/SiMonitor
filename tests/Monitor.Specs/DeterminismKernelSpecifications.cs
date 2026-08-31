// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Monitor.Simulation.Determinism;

namespace Monitor.Specs;

internal static class DeterminismKernelSpecifications
{
    private const string RootSeedHex =
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string ProfileHashHex =
        "15a37ea1ad2a5227a8e735800aa3cc837f53d0ef83b065f0a8376e1bdf74bea8";

    public static Specification[] All =>
    [
        new(nameof(UninterruptedKernelMatchesFrozenRecordHash),
            UninterruptedKernelMatchesFrozenRecordHash),
        new(nameof(CheckpointResumeMatchesFrozenHashes), CheckpointResumeMatchesFrozenHashes),
        new(nameof(IncompatibleKernelCheckpointRejects), IncompatibleKernelCheckpointRejects),
        new(nameof(FailedStepDoesNotPartiallyAdvance), FailedStepDoesNotPartiallyAdvance),
    ];

    private static void UninterruptedKernelMatchesFrozenRecordHash()
    {
        var kernel =
            DeterminismConformanceKernel.CreateFromLowercaseHex(RootSeedHex);
        byte[] records = kernel.Run(64);
        Check.That(Hash(records) ==
            "c7b3185448bc74499f5e5449326d1e753c2cc88f9c4d7b838dedc41d1db44cc1",
            "64 uninterrupted records must match the frozen simulation hash");
        Check.That(kernel.SimTimeNs == 256_000_000 && kernel.StepIndex == 64,
            "the conformance kernel must advance one exact 4 ms step per record");
    }

    private static void CheckpointResumeMatchesFrozenHashes()
    {
        DeterminismCheckpointCodec codec = new(Convert.FromHexString(ProfileHashHex));
        var split =
            DeterminismConformanceKernel.CreateFromLowercaseHex(RootSeedHex);
        byte[] before = split.Run(29);
        byte[] checkpointBytes = codec.Serialize(split.CaptureCheckpoint());
        Check.That(Hash(checkpointBytes) ==
            "4bff9a4b8ae48019b2529241ab78d41c8bdd630f81a524c63d93469ab3e1f199",
            "step 29 checkpoint bytes must match the frozen hash");

        var resumed =
            DeterminismConformanceKernel.Restore(codec.Restore(checkpointBytes));
        byte[] after = resumed.Run(35);
        byte[] records = [.. before, .. after];
        Check.That(Hash(records) ==
            "c7b3185448bc74499f5e5449326d1e753c2cc88f9c4d7b838dedc41d1db44cc1",
            "checkpoint resume must reproduce the uninterrupted record bytes");
        Check.That(Hash(codec.Serialize(resumed.CaptureCheckpoint())) ==
            "0c65950486074204f3b16c30cd20fd82fdc6b91332f4815b87cb8cc12272d865",
            "resumed final state must match the frozen final checkpoint hash");
    }

    private static void IncompatibleKernelCheckpointRejects()
    {
        var kernel =
            DeterminismConformanceKernel.CreateFromLowercaseHex(RootSeedHex);
        DeterminismCheckpoint valid = kernel.CaptureCheckpoint();
        DeterminismCheckpoint missingStream = valid with
        {
            Streams = valid.Streams.Skip(1).ToArray(),
        };
        Check.That(ConfigurationReason(() =>
            DeterminismConformanceKernel.Restore(missingStream)) ==
            "DeterminismKernel.StreamSetMismatch",
            "a missing named stream must reject rather than derive replacement state");
        DeterminismCheckpoint unalignedTime = valid with { SimTimeNs = 1 };
        Check.That(ConfigurationReason(() =>
            DeterminismConformanceKernel.Restore(unalignedTime)) ==
            "DeterminismKernel.InvalidSimTime",
            "an unaligned time must reject because the minimal checkpoint cannot recover its step index");
    }

    private static void FailedStepDoesNotPartiallyAdvance()
    {
        var initial =
            DeterminismConformanceKernel.CreateFromLowercaseHex(RootSeedHex);
        DeterminismCheckpoint checkpoint = initial.CaptureCheckpoint();
        DeterministicStreamState[] exhaustedStreams =
        [
            .. checkpoint.Streams.Select(stream => stream.StreamId == "sensor.ecg.noise"
                ? stream with { DrawCount = ulong.MaxValue - 5 }
                : stream),
        ];
        var kernel = DeterminismConformanceKernel.Restore(
            checkpoint with { Streams = exhaustedStreams });
        DeterminismCheckpoint before = kernel.CaptureCheckpoint();
        Check.That(ThrowsInvalidOperation(() => _ = kernel.Step()),
            "draw-count exhaustion must fail the step");
        DeterminismCheckpoint after = kernel.CaptureCheckpoint();
        Check.That(after.SimTimeNs == before.SimTimeNs &&
            after.PhaseU64 == before.PhaseU64 &&
            after.FilterYQ32 == before.FilterYQ32 &&
            after.Streams.SequenceEqual(before.Streams),
            "a failed step must not advance time, numeric state, or any named stream");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string? ConfigurationReason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (DeterminismConfigurationException exception)
        {
            return exception.ReasonCode;
        }
    }

    private static bool ThrowsInvalidOperation(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
