// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Specs;

internal static class DeterminismSpecifications
{
    private const string RootSeedHex =
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string ProfileHashHex =
        "15a37ea1ad2a5227a8e735800aa3cc837f53d0ef83b065f0a8376e1bdf74bea8";

    public static Specification[] All =>
    [
        new(nameof(NamedStreamsMatchFrozenGoldenVectors), NamedStreamsMatchFrozenGoldenVectors),
        new(nameof(SamplingConsumesOnlyItsNamedStream), SamplingConsumesOnlyItsNamedStream),
        new(nameof(CheckpointRestoresEveryFutureDraw), CheckpointRestoresEveryFutureDraw),
        new(nameof(CheckpointCorruptionFailsClosed), CheckpointCorruptionFailsClosed),
        new(nameof(InvalidSeedAndStreamIdentifiersReject), InvalidSeedAndStreamIdentifiersReject),
    ];

    private static void NamedStreamsMatchFrozenGoldenVectors()
    {
        GoldenStream[] vectors =
        [
            new(
                "physiology.hr.drift",
                ["22bcd5328d39c7ad", "c5bd66310af408e7", "bccf33e99eb5411d", "010f324e7f0d4c52"],
                ["257b4f7672c8515e", "a654933d1882a90d", "f3d21609854dec8f", "2efb405ed0232db6", "a6e77200f046fb36", "6e7649792e380462", "e7a85695c2e191ca", "9d928e5801d4c7b0"]),
            new(
                "sensor.ecg.noise",
                ["519c3639bc811753", "887bb2b73a83b68c", "4b47e4fd50dd1ad9", "320d53405ce3ac7e"],
                ["df351aa4938b50fd", "187a2e62a9f00ade", "6af31f6f49737519", "4f7a136a9f501804", "76c90b00b2945658", "8604253fdac3bc5d", "235ae2bd34ab88fb", "c5616fe10bd7dbc5"]),
            new(
                "therapy.outcome",
                ["46839d634dc80d03", "fc73162cdf3f760c", "9cf9fb288a677011", "6b0e5cfe74ff86da"],
                ["1d72f19f13e0122f", "d4610fa8a8fa2657", "2d3d863cadca92a4", "359d1adc39d3389d", "b924ce94f674a4ba", "f2e5368df9ebe825", "410213ccfd7a3a46", "6fa927cc63842293"]),
        ];

        using var factory =
            DeterministicStreamFactory.FromLowercaseHex(RootSeedHex);
        foreach (GoldenStream vector in vectors)
        {
            DeterministicRandomSource source = factory.CreateStream(vector.StreamId);
            DeterministicStreamState initial = source.CaptureState();
            ulong[] actualState = [initial.S0, initial.S1, initial.S2, initial.S3];
            Check.That(actualState.SequenceEqual(vector.InitialState.Select(ParseHex)),
                $"derived state must match the frozen vector for {vector.StreamId}");
            ulong[] outputs = Enumerable.Range(0, 8).Select(_ => source.NextUInt64()).ToArray();
            Check.That(outputs.SequenceEqual(vector.FirstOutputs.Select(ParseHex)),
                $"xoshiro256** outputs must match the frozen vector for {vector.StreamId}");
            Check.That(source.DrawCount == 8, "every output must advance draw_count exactly once");
        }
    }

    private static void SamplingConsumesOnlyItsNamedStream()
    {
        using var factory =
            DeterministicStreamFactory.FromLowercaseHex(RootSeedHex);
        DeterministicRandomSource noise = factory.CreateStream("sensor.ecg.noise");
        DeterministicRandomSource drift = factory.CreateStream("physiology.hr.drift");
        DeterministicRandomSource driftProbe = factory.CreateStream("physiology.hr.drift");
        _ = noise.NextNormal12CenteredQ32();
        Check.That(noise.DrawCount == 12 && drift.DrawCount == 0,
            "Normal12U64Centered must consume exactly twelve draws from its own stream");

        const ulong bound = (1UL << 63) + 1;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong expected;
        do
        {
            expected = driftProbe.NextUInt64();
        }
        while (expected < threshold);

        Check.That(drift.UniformBelow(bound) == expected % bound,
            "UniformBelowU64 must reject the same values as the frozen algorithm");
        Check.That(drift.DrawCount == driftProbe.DrawCount && drift.DrawCount > 1,
            "every bounded rejection draw must remain visible in draw_count");
        Check.That(ConfigurationReason(() => drift.UniformBelow(0)) ==
            "UniformBelowU64.InvalidBound",
            "a zero bound must reject as a configuration error");
    }

    private static void CheckpointRestoresEveryFutureDraw()
    {
        using var factory =
            DeterministicStreamFactory.FromLowercaseHex(RootSeedHex);
        DeterministicRandomSource noise = factory.CreateStream("sensor.ecg.noise");
        DeterministicRandomSource drift = factory.CreateStream("physiology.hr.drift");
        for (int index = 0; index < 17; index++)
        {
            noise.NextUInt64();
        }

        for (int index = 0; index < 9; index++)
        {
            drift.NextUInt64();
        }

        DeterminismCheckpoint checkpoint = new(
            116_000_000,
            0x0123456789abcdefUL,
            -123456789,
            [noise.CaptureState(), drift.CaptureState()]);
        DeterminismCheckpointCodec codec = new(Convert.FromHexString(ProfileHashHex));
        byte[] encoded = codec.Serialize(checkpoint);
        DeterminismCheckpoint restored = codec.Restore(encoded);
        Check.That(restored.Streams.Select(stream => stream.StreamId).SequenceEqual(
            ["physiology.hr.drift", "sensor.ecg.noise"]),
            "checkpoint streams must use canonical ASCII ordering");

        var resumed = restored.Streams.ToDictionary(
            stream => stream.StreamId,
            DeterministicRandomSource.Restore,
            StringComparer.Ordinal);
        for (int index = 0; index < 64; index++)
        {
            Check.That(noise.NextUInt64() == resumed["sensor.ecg.noise"].NextUInt64(),
                "restored noise stream must reproduce every future draw");
            Check.That(drift.NextUInt64() == resumed["physiology.hr.drift"].NextUInt64(),
                "restored drift stream must reproduce every future draw");
        }
    }

    private static void CheckpointCorruptionFailsClosed()
    {
        DeterminismCheckpointCodec codec = new(Convert.FromHexString(ProfileHashHex));
        byte[] encoded = codec.Serialize(new DeterminismCheckpoint(0, 0, 0, []));
        byte[] badMagic = [.. encoded];
        badMagic[0] ^= 0x20;
        Check.That(Rejection(codec, badMagic) == "Checkpoint.BadMagic",
            "bad magic must have a stable rejection");
        byte[] badHash = [.. encoded];
        badHash[8] ^= 0x01;
        Check.That(Rejection(codec, badHash) == "Checkpoint.ProfileHashMismatch",
            "profile mismatch must have a stable rejection");
        byte[] trailing = [.. encoded, 0];
        Check.That(Rejection(codec, trailing) == "Checkpoint.TrailingBytes",
            "trailing bytes must have a stable rejection");
        Check.That(Rejection(codec, encoded.AsSpan(0, encoded.Length - 1)) == "Checkpoint.Truncated",
            "truncated checkpoint must fail without partial state");
    }

    private static void InvalidSeedAndStreamIdentifiersReject()
    {
        Check.That(ThrowsArgument(() =>
            DeterministicStreamFactory.FromLowercaseHex(RootSeedHex.ToUpperInvariant())),
            "uppercase seed text must reject");
        using var factory =
            DeterministicStreamFactory.FromLowercaseHex(RootSeedHex);
        foreach (string streamId in new[] { "Sensor.ecg", "sensor..ecg", "sensor.ecg.", "传感器.ecg" })
        {
            Check.That(ThrowsArgument(() => factory.CreateStream(streamId)),
                $"invalid stream ID must reject: {streamId}");
        }
    }

    private static string? Rejection(DeterminismCheckpointCodec codec, ReadOnlySpan<byte> bytes)
    {
        try
        {
            codec.Restore(bytes);
            return null;
        }
        catch (DeterminismCheckpointException exception)
        {
            return exception.ReasonCode;
        }
    }

    private static bool ThrowsArgument(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

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

    private static ulong ParseHex(string value) => Convert.ToUInt64(value, 16);

    private sealed record GoldenStream(
        string StreamId,
        string[] InitialState,
        string[] FirstOutputs);
}
