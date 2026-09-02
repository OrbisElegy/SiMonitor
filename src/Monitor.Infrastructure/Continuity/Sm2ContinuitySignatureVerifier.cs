// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Monitor.Application.Continuity;

namespace Monitor.Infrastructure.Continuity;

public sealed class Sm2ContinuitySignatureVerifier :
    IContinuitySignatureVerifier
{
    public const string UserId = "MONITOR-CONTINUITY-V1";

    private static readonly BigInteger Prime = ParseHex(
        "FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFF");
    private static readonly BigInteger CurveA = ParseHex(
        "FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFC");
    private static readonly BigInteger CurveB = ParseHex(
        "28E9FA9E9D9F5E344D5A9E4BCF6509A7F39789F515AB8F92DDBCBD414D940E93");
    private static readonly BigInteger Order = ParseHex(
        "FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFF7203DF6B21C6052B53BBF40939D54123");
    private static readonly CurvePoint Generator = new(
        ParseHex(
            "32C4AE2C1F1981195F9904466A39C9948FE30BBFF2660BE1715A4589334C74C7"),
        ParseHex(
            "BC3736A2F4F6779C59BDCEE36B692153D0A9877CC62A474002DF32E52139F0A0"));
    private static readonly byte[] UserIdBytes = Encoding.ASCII.GetBytes(UserId);

    public string DeriveKeyId(ReadOnlyMemory<byte> publicKeySec1)
    {
        _ = ParsePublicKey(publicKeySec1.Span, throwOnError: true);
        return "sm2-sm3:" + Convert.ToHexString(
            Sm3.HashData(publicKeySec1.Span)).ToLowerInvariant();
    }

    public bool Verify(
        ContinuitySignedBody body,
        ReadOnlyMemory<byte> signatureRs64,
        ReadOnlyMemory<byte> publicKeySec1)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (signatureRs64.Length != 64 ||
            !TryParsePublicKey(publicKeySec1.Span, out CurvePoint publicKey))
        {
            return false;
        }

        BigInteger r = ParseUnsigned(signatureRs64.Span[..32]);
        BigInteger s = ParseUnsigned(signatureRs64.Span[32..]);
        if (r < BigInteger.One || r >= Order ||
            s < BigInteger.One || s >= Order)
        {
            return false;
        }

        BigInteger t = Mod(r + s, Order);
        if (t.IsZero)
        {
            return false;
        }

        byte[] message = EncodeCanonicalBody(body);
        BigInteger e = ParseUnsigned(
            Sm3.HashData(Combine(ComputeZa(publicKey), message)));
        CurvePoint? result = Add(
            Multiply(s, Generator),
            Multiply(t, publicKey));
        return result is CurvePoint point && Mod(e + point.X, Order) == r;
    }

    public static byte[] EncodeCanonicalBody(ContinuitySignedBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "authority_epoch",
                body.AuthorityEpoch.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("capsule_id", body.CapsuleId.ToString("D"));
            writer.WriteString("capsule_type", body.CapsuleType);
            writer.WriteString(
                "continuation_valid_until",
                body.ContinuationValidUntilSimTimeNs.ToString(
                    CultureInfo.InvariantCulture));
            writer.WriteString("instance_id", body.InstanceId.ToString("D"));
            writer.WriteString("payload_sha256", body.PayloadSha256);
            writer.WriteString("schema", "ContinuitySignedBody@1");
            writer.WriteString(
                "secret_boundary_sim_time",
                body.SecretBoundarySimTimeNs.ToString(
                    CultureInfo.InvariantCulture));
            writer.WriteString("session_id", body.SessionId.ToString("D"));
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] ComputeZa(CurvePoint publicKey)
    {
        int entl = checked(UserIdBytes.Length * 8);
        byte[] result = new byte[2 + UserIdBytes.Length + (6 * 32)];
        BinaryPrimitives.WriteUInt16BigEndian(
            result,
            checked((ushort)entl));
        int offset = 2;
        UserIdBytes.CopyTo(result, offset);
        offset += UserIdBytes.Length;
        WriteCoordinate(CurveA, result.AsSpan(offset, 32));
        offset += 32;
        WriteCoordinate(CurveB, result.AsSpan(offset, 32));
        offset += 32;
        WriteCoordinate(Generator.X, result.AsSpan(offset, 32));
        offset += 32;
        WriteCoordinate(Generator.Y, result.AsSpan(offset, 32));
        offset += 32;
        WriteCoordinate(publicKey.X, result.AsSpan(offset, 32));
        offset += 32;
        WriteCoordinate(publicKey.Y, result.AsSpan(offset, 32));
        return Sm3.HashData(result);
    }

    private static byte[] Combine(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second)
    {
        byte[] result = new byte[first.Length + second.Length];
        first.CopyTo(result);
        second.CopyTo(result.AsSpan(first.Length));
        return result;
    }

    private static CurvePoint ParsePublicKey(
        ReadOnlySpan<byte> encoded,
        bool throwOnError)
    {
        if (TryParsePublicKey(encoded, out CurvePoint point))
        {
            return point;
        }

        if (throwOnError)
        {
            throw new ArgumentException(
                "ContinuityCrypto.InvalidPublicKey",
                nameof(encoded));
        }

        return default;
    }

    private static bool TryParsePublicKey(
        ReadOnlySpan<byte> encoded,
        out CurvePoint point)
    {
        point = default;
        if (encoded.Length != 65 || encoded[0] != 0x04)
        {
            return false;
        }

        BigInteger x = ParseUnsigned(encoded.Slice(1, 32));
        BigInteger y = ParseUnsigned(encoded.Slice(33, 32));
        if (x < BigInteger.Zero || x >= Prime ||
            y < BigInteger.Zero || y >= Prime ||
            Mod(y * y - ((x * x * x) + (CurveA * x) + CurveB), Prime) !=
                BigInteger.Zero)
        {
            return false;
        }

        CurvePoint candidate = new(x, y);
        if (Multiply(Order, candidate) is not null)
        {
            return false;
        }

        point = candidate;
        return true;
    }

    private static CurvePoint? Add(CurvePoint? left, CurvePoint? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        CurvePoint p = left.Value;
        CurvePoint q = right.Value;
        if (p.X == q.X && Mod(p.Y + q.Y, Prime).IsZero)
        {
            return null;
        }

        BigInteger slope = p == q
            ? Mod(
                (3 * p.X * p.X) + CurveA,
                Prime) * ModInverse(2 * p.Y, Prime)
            : Mod(q.Y - p.Y, Prime) * ModInverse(q.X - p.X, Prime);
        slope = Mod(slope, Prime);
        BigInteger x = Mod((slope * slope) - p.X - q.X, Prime);
        BigInteger y = Mod((slope * (p.X - x)) - p.Y, Prime);
        return new CurvePoint(x, y);
    }

    private static CurvePoint? Multiply(
        BigInteger scalar,
        CurvePoint point)
    {
        CurvePoint? result = null;
        CurvePoint? addend = point;
        while (scalar > BigInteger.Zero)
        {
            if (!scalar.IsEven)
            {
                result = Add(result, addend);
            }

            addend = Add(addend, addend);
            scalar >>= 1;
        }

        return result;
    }

    private static BigInteger ModInverse(
        BigInteger value,
        BigInteger modulus) =>
        BigInteger.ModPow(Mod(value, modulus), modulus - 2, modulus);

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        BigInteger result = value % modulus;
        return result.Sign < 0 ? result + modulus : result;
    }

    private static BigInteger ParseHex(string value) =>
        ParseUnsigned(Convert.FromHexString(value));

    private static BigInteger ParseUnsigned(ReadOnlySpan<byte> value) =>
        new(value, isUnsigned: true, isBigEndian: true);

    private static void WriteCoordinate(
        BigInteger value,
        Span<byte> destination)
    {
        if (!value.TryWriteBytes(
                destination,
                out int written,
                isUnsigned: true,
                isBigEndian: true))
        {
            throw new InvalidOperationException(
                "ContinuityCrypto.InvalidCoordinate");
        }

        if (written != destination.Length)
        {
            destination[..written].CopyTo(destination[(destination.Length - written)..]);
            destination[..(destination.Length - written)].Clear();
        }
    }

    private readonly record struct CurvePoint(BigInteger X, BigInteger Y);
}
