// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Monitor.Application.Continuity;
using Monitor.Infrastructure.Continuity;

namespace Monitor.Specs;

internal static class ContinuitySignatureSpecifications
{
    private const string FrozenKeyId =
        "sm2-sm3:40d87c6fb4c64aae27e3820b3d6bbe21133d89451c12eeb93fa71726a0d5e54b";
    private const string FrozenPublicKey =
        "042b3d76bbaf961f6a198bd146002e74f44c1a0e3eabbc2acbfbd4d5f96d25a5cc" +
        "791a84f42619754777353dd62c198a9a06c3a3920b6baf18c57a68b91dc51cf4";
    private const string FrozenSignature =
        "bc0bd27d87cccc9e309afc73b51177f8a007bc26eaa7f47f2151eca4a148a9a4" +
        "26319277b907e24b73672634cd570813c804f20c5f85c17bea55d050d8e17ae8";
    private const string FrozenMessage =
        "{\"authority_epoch\":\"7\",\"capsule_id\":" +
        "\"11111111-1111-4111-8111-111111111111\",\"capsule_type\":" +
        "\"ContinuityCapsuleBase\",\"continuation_valid_until\":" +
        "\"120000000000\",\"instance_id\":" +
        "\"22222222-2222-4222-8222-222222222222\",\"payload_sha256\":" +
        "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"," +
        "\"schema\":\"ContinuitySignedBody@1\",\"secret_boundary_sim_time\":" +
        "\"120000000000\",\"session_id\":" +
        "\"33333333-3333-4333-8333-333333333333\"}";

    private static readonly Guid CapsuleId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid SessionId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");

    public static Specification[] All =>
    [
        new(nameof(FrozenSm2VectorAndCanonicalBodyMatch),
            FrozenSm2VectorAndCanonicalBodyMatch),
        new(nameof(MalformedOrMutatedSignaturesFailClosed),
            MalformedOrMutatedSignaturesFailClosed),
        new(nameof(VerifiedCapsuleCommitsReplayStateAtomically),
            VerifiedCapsuleCommitsReplayStateAtomically),
        new(nameof(SignaturePrecedesIdentityAndEpochChecks),
            SignaturePrecedesIdentityAndEpochChecks),
        new(nameof(TimeBoundaryAndPayloadFailuresDoNotCommit),
            TimeBoundaryAndPayloadFailuresDoNotCommit),
        new(nameof(KeyLifecycleAndCheckpointBoundsFailClosed),
            KeyLifecycleAndCheckpointBoundsFailClosed),
    ];

    private static void FrozenSm2VectorAndCanonicalBodyMatch()
    {
        Sm2ContinuitySignatureVerifier verifier = new();
        ContinuitySignedBody body = FrozenBody();
        byte[] publicKey = Convert.FromHexString(FrozenPublicKey);
        byte[] signature = Convert.FromHexString(FrozenSignature);

        Check.That(
            Encoding.UTF8.GetString(
                Sm2ContinuitySignatureVerifier.EncodeCanonicalBody(body)) ==
                FrozenMessage &&
            verifier.DeriveKeyId(publicKey) == FrozenKeyId &&
            verifier.Verify(body, signature, publicKey),
            "the managed verifier must reproduce the frozen canonical SM2 vector");
    }

    private static void MalformedOrMutatedSignaturesFailClosed()
    {
        Sm2ContinuitySignatureVerifier verifier = new();
        byte[] publicKey = Convert.FromHexString(FrozenPublicKey);
        byte[] signature = Convert.FromHexString(FrozenSignature);
        byte[] zeroR = signature.ToArray();
        zeroR.AsSpan(0, 32).Clear();
        byte[] zeroS = signature.ToArray();
        zeroS.AsSpan(32, 32).Clear();
        byte[] malformedPublicKey = publicKey.ToArray();
        malformedPublicKey[0] = 0x02;

        Check.That(
            !verifier.Verify(
                FrozenBody() with { PayloadSha256 = Hash('b') },
                signature,
                publicKey) &&
            !verifier.Verify(FrozenBody(), zeroR, publicKey) &&
            !verifier.Verify(FrozenBody(), zeroS, publicKey) &&
            !verifier.Verify(
                FrozenBody(),
                signature.AsMemory(0, 63),
                publicKey) &&
            !verifier.Verify(FrozenBody(), signature, malformedPublicKey),
            "message, scalar, length, and public-key mutations must all reject");
    }

    private static void VerifiedCapsuleCommitsReplayStateAtomically()
    {
        FakeVerifier verifier = new(verifyResult: true);
        byte[] publicKey = [0x04, 0x01, .. new byte[63]];
        TrustedContinuitySigningKey key = FakeKey(
            FakeVerifier.ActiveKeyId,
            publicKeyMarker: 1,
            ContinuitySigningKeyState.Active) with
        {
            PublicKeySec1 = publicKey,
        };
        var gate = ContinuitySignatureGate.Start(
            SessionId,
            InstanceId,
            authorityEpoch: 7,
            authorityMonotonicNs: 10,
            [key],
            verifier);
        publicKey[1] ^= 0xff;

        ContinuitySignatureGateState committed = gate.VerifyAndCommit(
            Body(),
            FakeProof(),
            Payload(),
            currentSimTimeNs: 100_000_000_000,
            authorityMonotonicNs: 11);
        Check.That(committed.AcceptedCapsuleIds.SequenceEqual([CapsuleId]) &&
            committed.LastAuthorityMonotonicNs == 11 &&
            Reason(() => gate.VerifyAndCommit(
                Body(),
                FakeProof(),
                Payload(),
                100_000_000_000,
                12)) == "ContinuityCrypto.Replay",
            "a valid capsule must commit once despite caller key-buffer mutation");

        var restored = ContinuitySignatureGate.Restore(
            committed,
            [FakeKey(
                FakeVerifier.ActiveKeyId,
                publicKeyMarker: 1,
                ContinuitySigningKeyState.Active)],
            verifier);
        Check.That(Reason(() => restored.VerifyAndCommit(
            Body(),
            FakeProof(),
            Payload(),
            100_000_000_000,
            12)) == "ContinuityCrypto.Replay",
            "a restored gate must retain capsule replay evidence");
    }

    private static void SignaturePrecedesIdentityAndEpochChecks()
    {
        FakeVerifier verifier = new(verifyResult: false);
        ContinuitySignatureGate gate = FakeGate(verifier);
        ContinuitySignatureGateState before = gate.CaptureState();
        ContinuitySignedBody mismatched = Body() with
        {
            SessionId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            AuthorityEpoch = 6,
        };
        Check.That(Reason(() => gate.VerifyAndCommit(
                mismatched,
                FakeProof(),
                Payload(),
                100_000_000_000,
                11)) == "ContinuityCrypto.InvalidSignature" &&
            SameState(before, gate.CaptureState()),
            "an unauthenticated body must not reach identity or epoch decisions");

        verifier.VerifyResult = true;
        Check.That(Reason(() => gate.VerifyAndCommit(
                mismatched,
                FakeProof(),
                Payload(),
                100_000_000_000,
                11)) == "ContinuityCrypto.IdentityMismatch" &&
            Reason(() => gate.VerifyAndCommit(
                Body() with { AuthorityEpoch = 6 },
                FakeProof(),
                Payload(),
                100_000_000_000,
                11)) == "ContinuityCrypto.EpochMismatch" &&
            SameState(before, gate.CaptureState()),
            "verified bodies must still bind the expected session, instance, and epoch");
    }

    private static void TimeBoundaryAndPayloadFailuresDoNotCommit()
    {
        FakeVerifier verifier = new(verifyResult: true);
        ContinuitySignatureGate gate = FakeGate(verifier);
        ContinuitySignatureGateState before = gate.CaptureState();

        Check.That(Reason(() => gate.VerifyAndCommit(
                Body(),
                FakeProof(),
                Payload(),
                currentSimTimeNs: 120_000_000_001,
                authorityMonotonicNs: 11)) == "ContinuityCrypto.Expired" &&
            Reason(() => gate.VerifyAndCommit(
                Body() with
                {
                    ContinuationValidUntilSimTimeNs = 120_000_000_001,
                },
                FakeProof(),
                Payload(),
                currentSimTimeNs: 100_000_000_000,
                authorityMonotonicNs: 11)) ==
                "ContinuityCrypto.SecretBoundaryExceeded" &&
            Reason(() => gate.VerifyAndCommit(
                Body(),
                FakeProof(),
                Encoding.UTF8.GetBytes("wrong-payload"),
                currentSimTimeNs: 100_000_000_000,
                authorityMonotonicNs: 11)) ==
                "ContinuityCrypto.PayloadMismatch" &&
            Reason(() => gate.VerifyAndCommit(
                Body(),
                FakeProof(),
                Payload(),
                currentSimTimeNs: 100_000_000_000,
                authorityMonotonicNs: 9)) ==
                "ContinuityCrypto.AuthorityTimeReversed" &&
            Reason(() => gate.VerifyAndCommit(
                Body(),
                FakeProof(),
                new byte[ContinuitySignatureGate.MaximumBaseCapsuleBytes + 1],
                currentSimTimeNs: 100_000_000_000,
                authorityMonotonicNs: 11)) ==
                "ContinuityCrypto.InvalidRequest" &&
            SameState(before, gate.CaptureState()),
            "time, boundary, payload, and size failures must be non-mutating");

        ContinuitySignatureGateState accepted = gate.VerifyAndCommit(
            Body(),
            FakeProof(),
            Payload(),
            currentSimTimeNs: 120_000_000_000,
            authorityMonotonicNs: 11);
        Check.That(accepted.AcceptedCapsuleIds.Count == 1,
            "the exact continuation deadline remains an inclusive boundary");
    }

    private static void KeyLifecycleAndCheckpointBoundsFailClosed()
    {
        FakeVerifier verifier = new(verifyResult: true);
        TrustedContinuitySigningKey active = FakeKey(
            FakeVerifier.ActiveKeyId,
            publicKeyMarker: 1,
            ContinuitySigningKeyState.Active);
        TrustedContinuitySigningKey revoked = FakeKey(
            FakeVerifier.RevokedKeyId,
            publicKeyMarker: 2,
            ContinuitySigningKeyState.Revoked);
        var gate = ContinuitySignatureGate.Start(
            SessionId,
            InstanceId,
            authorityEpoch: 7,
            authorityMonotonicNs: 10,
            [active, revoked],
            verifier);
        ContinuitySignatureGateState before = gate.CaptureState();

        Check.That(Reason(() => gate.VerifyAndCommit(
                Body(),
                FakeProof() with { KeyId = FakeVerifier.UnknownKeyId },
                Payload(),
                100_000_000_000,
                11)) == "ContinuityCrypto.UnknownKey" &&
            Reason(() => gate.VerifyAndCommit(
                Body(),
                FakeProof() with { KeyId = FakeVerifier.RevokedKeyId },
                Payload(),
                100_000_000_000,
                11)) == "ContinuityCrypto.KeyInactive" &&
            Reason(() => gate.VerifyAndCommit(
                Body(),
                FakeProof(),
                Payload(),
                100_000_000_000,
                1_000_000)) == "ContinuityCrypto.KeyNotCurrent" &&
            SameState(before, gate.CaptureState()),
            "unknown, revoked, and expired online keys must not advance state");

        ContinuitySignatureGateState duplicateCheckpoint = before with
        {
            AcceptedCapsuleIds = new[] { CapsuleId, CapsuleId },
        };
        Check.That(Reason(() => ContinuitySignatureGate.Restore(
                duplicateCheckpoint,
                [active],
                verifier)) == "ContinuityCrypto.InvalidCheckpoint",
            "duplicate replay IDs must make a checkpoint invalid");

        TrustedContinuitySigningKey secondActive = revoked with
        {
            State = ContinuitySigningKeyState.Active,
        };
        Check.That(Reason(() => ContinuitySignatureGate.Start(
                SessionId,
                InstanceId,
                authorityEpoch: 7,
                authorityMonotonicNs: 10,
                [active, secondActive],
                verifier)) == "ContinuityCrypto.InvalidConfiguration",
            "an authority epoch cannot install two active signing keys");
    }

    private static ContinuitySignedBody FrozenBody() => new(
        "ContinuityCapsuleBase",
        CapsuleId,
        SessionId,
        InstanceId,
        AuthorityEpoch: 7,
        SecretBoundarySimTimeNs: 120_000_000_000,
        ContinuationValidUntilSimTimeNs: 120_000_000_000,
        PayloadSha256: Hash('a'));

    private static ContinuitySignedBody Body() =>
        FrozenBody() with { PayloadSha256 = PayloadHash() };

    private static ContinuitySignatureProof FakeProof() => new(
        ContinuitySignatureGate.SignatureAlgorithmId,
        FakeVerifier.ActiveKeyId,
        SignedSchemaMajor: 1,
        SignedSchemaMinor: 0,
        new byte[64]);

    private static ContinuitySignatureGate FakeGate(FakeVerifier verifier) =>
        ContinuitySignatureGate.Start(
            SessionId,
            InstanceId,
            authorityEpoch: 7,
            authorityMonotonicNs: 10,
            [FakeKey(
                FakeVerifier.ActiveKeyId,
                publicKeyMarker: 1,
                ContinuitySigningKeyState.Active)],
            verifier);

    private static TrustedContinuitySigningKey FakeKey(
        string keyId,
        byte publicKeyMarker,
        ContinuitySigningKeyState state) => new(
        keyId,
        [0x04, publicKeyMarker, .. new byte[63]],
        state,
        AuthorityEpoch: 7,
        ActivatedAtAuthorityMonotonicNs: 0,
        ExpiresAtAuthorityMonotonicNs: 1_000_000);

    private static bool SameState(
        ContinuitySignatureGateState expected,
        ContinuitySignatureGateState actual) =>
        expected.SessionId == actual.SessionId &&
        expected.InstanceId == actual.InstanceId &&
        expected.AuthorityEpoch == actual.AuthorityEpoch &&
        expected.LastAuthorityMonotonicNs == actual.LastAuthorityMonotonicNs &&
        expected.AcceptedCapsuleIds.SequenceEqual(actual.AcceptedCapsuleIds);

    private static string Hash(char character) => new(character, 64);

    private static byte[] Payload() =>
        Encoding.UTF8.GetBytes("continuity-capsule-payload");

    private static string PayloadHash() => Convert.ToHexString(
        SHA256.HashData(Payload())).ToLowerInvariant();

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (ContinuitySignatureGateException exception)
        {
            return exception.ReasonCode;
        }
    }

    private sealed class FakeVerifier(bool verifyResult) :
        IContinuitySignatureVerifier
    {
        public const string ActiveKeyId =
            "sm2-sm3:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string RevokedKeyId =
            "sm2-sm3:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public const string UnknownKeyId =
            "sm2-sm3:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

        public bool VerifyResult { get; set; } = verifyResult;

        public string DeriveKeyId(ReadOnlyMemory<byte> publicKeySec1) =>
            publicKeySec1.Span[1] switch
            {
                1 => ActiveKeyId,
                2 => RevokedKeyId,
                _ => UnknownKeyId,
            };

        public bool Verify(
            ContinuitySignedBody body,
            ReadOnlyMemory<byte> signatureRs64,
            ReadOnlyMemory<byte> publicKeySec1) =>
            VerifyResult && publicKeySec1.Span[1] == 1;
    }
}
