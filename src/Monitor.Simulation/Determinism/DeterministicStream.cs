// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Monitor.Simulation.Determinism;

public readonly record struct DeterministicStreamState(
    string StreamId,
    ulong S0,
    ulong S1,
    ulong S2,
    ulong S3,
    ulong DrawCount);

public sealed class DeterministicRandomSource
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    internal DeterministicRandomSource(DeterministicStreamState state)
    {
        DeterministicStreamFactory.ValidateStreamId(state.StreamId);
        StreamId = state.StreamId;
        _s0 = state.S0;
        _s1 = state.S1;
        _s2 = state.S2;
        _s3 = state.S3;
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = 0x9e3779b97f4a7c15UL;
        }

        DrawCount = state.DrawCount;
    }

    public string StreamId { get; }

    public ulong DrawCount { get; private set; }

    public static DeterministicRandomSource Restore(DeterministicStreamState state) => new(state);

    public ulong NextUInt64()
    {
        if (DrawCount == ulong.MaxValue)
        {
            throw new InvalidOperationException("The deterministic stream draw count is exhausted.");
        }

        ulong result = unchecked(BitOperations.RotateLeft(_s1 * 5, 7) * 9);
        ulong shifted = unchecked(_s1 << 17);
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= shifted;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        DrawCount++;
        return result;
    }

    public ulong UniformBelow(ulong bound)
    {
        if (bound == 0)
        {
            throw new DeterminismConfigurationException("UniformBelowU64.InvalidBound", nameof(bound));
        }

        ulong threshold = unchecked(0UL - bound) % bound;
        while (true)
        {
            ulong value = NextUInt64();
            if (value >= threshold)
            {
                return value % bound;
            }
        }
    }

    public long NextNormal12CenteredQ32()
    {
        long sum = 0;
        for (int index = 0; index < 12; index++)
        {
            sum += (long)(NextUInt64() >> 32);
        }

        return sum - (12L << 31);
    }

    public DeterministicStreamState CaptureState() =>
        new(StreamId, _s0, _s1, _s2, _s3, DrawCount);
}

public sealed class DeterministicStreamFactory : IDisposable
{
    public const int RootSeedSize = 32;

    private static ReadOnlySpan<byte> DerivationDomain => "MONDET1\0"u8;

    private byte[]? _rootSeed;

    public DeterministicStreamFactory(ReadOnlySpan<byte> rootSeed)
    {
        if (rootSeed.Length != RootSeedSize)
        {
            throw new ArgumentException("The deterministic root seed must be exactly 32 bytes.", nameof(rootSeed));
        }

        _rootSeed = rootSeed.ToArray();
    }

    public static DeterministicStreamFactory FromLowercaseHex(string rootSeedHex)
    {
        ArgumentNullException.ThrowIfNull(rootSeedHex);
        if (rootSeedHex.Length != RootSeedSize * 2 ||
            rootSeedHex.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "The deterministic root seed must be 64 lowercase hexadecimal characters.",
                nameof(rootSeedHex));
        }

        return new DeterministicStreamFactory(Convert.FromHexString(rootSeedHex));
    }

    public DeterministicRandomSource CreateStream(string streamId)
    {
        byte[] streamIdBytes = ValidateStreamId(streamId);
        byte[] message = new byte[DerivationDomain.Length + sizeof(ushort) + streamIdBytes.Length];
        DerivationDomain.CopyTo(message);
        BinaryPrimitives.WriteUInt16LittleEndian(
            message.AsSpan(DerivationDomain.Length),
            checked((ushort)streamIdBytes.Length));
        streamIdBytes.CopyTo(message.AsSpan(DerivationDomain.Length + sizeof(ushort)));
        byte[] digest = HMACSHA256.HashData(
            _rootSeed ?? throw new ObjectDisposedException(nameof(DeterministicStreamFactory)),
            message);
        try
        {
            return new DeterministicRandomSource(new DeterministicStreamState(
                streamId,
                BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(0, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(8, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(16, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(digest.AsSpan(24, 8)),
                0));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    public void Dispose()
    {
        byte[]? rootSeed = Interlocked.Exchange(ref _rootSeed, null);
        if (rootSeed is not null)
        {
            CryptographicOperations.ZeroMemory(rootSeed);
        }
    }

    internal static byte[] ValidateStreamId(string streamId)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        if (streamId.Length is < 1 or > 128 || streamId[0] is not (>= 'a' and <= 'z'))
        {
            throw new ArgumentException("The deterministic stream ID is invalid.", nameof(streamId));
        }

        bool previousWasSeparator = false;
        foreach (char character in streamId)
        {
            bool alphaNumeric = character is >= 'a' and <= 'z' or >= '0' and <= '9';
            bool separator = character is '.' or '_' or '-';
            if ((!alphaNumeric && !separator) || (separator && previousWasSeparator))
            {
                throw new ArgumentException("The deterministic stream ID is invalid.", nameof(streamId));
            }

            previousWasSeparator = separator;
        }

        if (previousWasSeparator)
        {
            throw new ArgumentException("The deterministic stream ID is invalid.", nameof(streamId));
        }

        return Encoding.ASCII.GetBytes(streamId);
    }
}
