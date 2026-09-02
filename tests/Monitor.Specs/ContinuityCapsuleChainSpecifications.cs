// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Monitor.Application.Continuity;

namespace Monitor.Specs;

internal static class ContinuityCapsuleChainSpecifications
{
    private const string KeyId =
        "sm2-sm3:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly Guid SessionId =
        Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid InstanceId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid BaseId =
        Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid DeltaOneId =
        Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private static readonly Guid DeltaTwoId =
        Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");

    public static Specification[] All =>
    [
        new(nameof(BaseAndDeltasAdvanceOneAuthenticatedChain),
            BaseAndDeltasAdvanceOneAuthenticatedChain),
        new(nameof(DeltaRequiresBaseAndExactHashLinks),
            DeltaRequiresBaseAndExactHashLinks),
        new(nameof(SignedMetadataAndPermissionFailuresDoNotCommit),
            SignedMetadataAndPermissionFailuresDoNotCommit),
        new(nameof(ShapeAndFrontierFailuresPreserveThePreviousChain),
            ShapeAndFrontierFailuresPreserveThePreviousChain),
        new(nameof(ChainCheckpointRestoresOnlyWithReplayEvidence),
            ChainCheckpointRestoresOnlyWithReplayEvidence),
        new(nameof(TwoPhaseAdmissionsCannotCommitOutOfOrder),
            TwoPhaseAdmissionsCannotCommitOutOfOrder),
        new(nameof(DecodedCapsulesAreDefensivelyCopied),
            DecodedCapsulesAreDefensivelyCopied),
    ];

    private static void BaseAndDeltasAdvanceOneAuthenticatedChain()
    {
        ContinuityCapsuleChain chain = Start();
        ContinuityCapsuleBase capsule = Base();
        byte[] basePayload = Payload("base");
        AcceptedContinuityCapsuleBase acceptedBase = chain.AcceptBase(
            SignedBody(capsule, basePayload),
            basePayload,
            currentSimTimeNs: 100,
            authorityMonotonicNs: 11);
        ContinuityCapsuleChainState baseState = acceptedBase.State;
        Check.That(baseState.Frontier is
        {
            BaseCapsuleId: var acceptedBaseId,
            BaseSha256: var acceptedBaseHash,
            LastDeltaId: null,
            CheckpointSequence: 30,
            CommitSequence: 400,
            SimTimeNs: 100,
        } &&
            acceptedBaseId == BaseId &&
            acceptedBaseHash == Hash('a') &&
            acceptedBase.Capsule.StateComponents.Count == 2 &&
            baseState.SignatureGate.AcceptedCapsuleIds.SequenceEqual([BaseId]),
            "an authenticated complete base must establish the chain frontier");

        ContinuityCapsuleDelta first = Delta(
            DeltaOneId,
            previousDeltaSha256: null,
            deltaSha256: Hash('b'),
            checkpointSequence: 31,
            simTimeNs: 101);
        byte[] firstPayload = Payload("delta-one");
        _ = chain.AcceptDelta(
            SignedBody(first, firstPayload),
            firstPayload,
            currentSimTimeNs: 101,
            authorityMonotonicNs: 12);
        ContinuityCapsuleDelta second = Delta(
            DeltaTwoId,
            previousDeltaSha256: Hash('b'),
            deltaSha256: Hash('c'),
            checkpointSequence: 32,
            simTimeNs: 102);
        byte[] secondPayload = Payload("delta-two");
        ContinuityCapsuleChainState final = chain.AcceptDelta(
            SignedBody(second, secondPayload),
            secondPayload,
            currentSimTimeNs: 102,
            authorityMonotonicNs: 13).State;

        Check.That(final.Frontier is
        {
            LastDeltaId: var lastDeltaId,
            LastDeltaSha256: var lastDeltaHash,
            CheckpointSequence: 32,
            SimTimeNs: 102,
        } &&
            lastDeltaId == DeltaTwoId &&
            lastDeltaHash == Hash('c') &&
            final.SignatureGate.AcceptedCapsuleIds.Count == 3,
            "each delta must advance the signed base and previous-delta chain");
    }

    private static void DeltaRequiresBaseAndExactHashLinks()
    {
        ContinuityCapsuleChain empty = Start();
        ContinuityCapsuleDelta first = Delta(
            DeltaOneId,
            previousDeltaSha256: null,
            deltaSha256: Hash('b'),
            checkpointSequence: 31,
            simTimeNs: 101);
        byte[] deltaPayload = Payload("delta-one");
        Check.That(Reason(() => empty.AcceptDelta(
                SignedBody(first, deltaPayload),
                deltaPayload,
                101,
                11)) == "ContinuityChain.BaseRequired" &&
            empty.CaptureState().SignatureGate.AcceptedCapsuleIds.Count == 0,
            "a standalone delta cannot consume replay state or establish a base");

        ContinuityCapsuleChain chain = WithBase();
        ContinuityCapsuleDelta wrongBase = first with
        {
            BaseSha256 = Hash('f'),
        };
        byte[] wrongBasePayload = Payload("delta-one-wrong-base");
        Check.That(Reason(() => chain.AcceptDelta(
                SignedBody(wrongBase, wrongBasePayload),
                wrongBasePayload,
                101,
                12)) == "ContinuityChain.HashChainMismatch" &&
            chain.CaptureState().SignatureGate.AcceptedCapsuleIds.Count == 1,
            "a mismatched base hash must leave the signed ID available to retry");

        ContinuityCapsuleChainState accepted = chain.AcceptDelta(
            SignedBody(first, deltaPayload),
            deltaPayload,
            101,
            12).State;
        Check.That(accepted.Frontier!.LastDeltaId == DeltaOneId &&
            accepted.SignatureGate.AcceptedCapsuleIds.Count == 2,
            "correcting a failed link may reuse the uncommitted delta ID");
    }

    private static void SignedMetadataAndPermissionFailuresDoNotCommit()
    {
        ContinuityCapsuleChain chain = Start();
        ContinuityCapsuleBase denied = Base() with
        {
            ContinuationPermission =
                ContinuityPermissionState.DisallowedByCourse,
        };
        byte[] deniedPayload = Payload("base-denied");
        ContinuitySignedBody deniedBody = SignedBody(denied, deniedPayload);
        Check.That(Reason(() => chain.AcceptBase(
                deniedBody,
                deniedPayload,
                100,
                11)) == "ContinuityChain.PermissionDenied",
            "a denied capsule must not consume its replay identity");

        ContinuityCapsuleBase allowed = Base();
        byte[] payload = Payload("base");
        ContinuitySignedBody body = SignedBody(allowed, payload);
        Check.That(
            Reason(() => chain.AcceptBase(
                body with { SecretBoundarySimTimeNs = 121 },
                payload,
                100,
                11)) == "ContinuityChain.SignedBodyMismatch" &&
            chain.Frontier is null &&
            chain.CaptureState().SignatureGate.AcceptedCapsuleIds.Count == 0,
            "permission and signed metadata mismatches must not consume the base");

        ContinuityCapsuleChainState accepted = chain.AcceptBase(
            body,
            payload,
            100,
            11).State;
        Check.That(accepted.Frontier!.BaseCapsuleId == BaseId,
            "an uncommitted base ID remains usable after semantic rejection");
    }

    private static void ShapeAndFrontierFailuresPreserveThePreviousChain()
    {
        ContinuityCapsuleChain chain = WithBase();
        ContinuityCapsuleChainState before = chain.CaptureState();
        ContinuityStateComponent later = Component("prng.rhythm", 'e');
        ContinuityStateComponent earlier = Component("filter.ecg", 'f');
        ContinuityCapsuleDelta unsorted = Delta(
            DeltaOneId,
            previousDeltaSha256: null,
            deltaSha256: Hash('b'),
            checkpointSequence: 31,
            simTimeNs: 101) with
        {
            ChangedComponents = new[] { later, earlier },
        };
        byte[] unsortedPayload = Payload("delta-unsorted");
        Check.That(Reason(() => chain.AcceptDelta(
                SignedBody(unsorted, unsortedPayload),
                unsortedPayload,
                101,
                12)) == "ContinuityChain.InvalidPayload" &&
            SameState(before, chain.CaptureState()),
            "noncanonical component order must reject before signature commit");

        ContinuityCapsuleDelta stale = Delta(
            DeltaOneId,
            previousDeltaSha256: null,
            deltaSha256: Hash('b'),
            checkpointSequence: 30,
            simTimeNs: 101);
        byte[] stalePayload = Payload("delta-stale");
        Check.That(Reason(() => chain.AcceptDelta(
                SignedBody(stale, stalePayload),
                stalePayload,
                101,
                12)) == "ContinuityChain.FrontierRegression" &&
            SameState(before, chain.CaptureState()),
            "a delta must strictly advance checkpoint sequence without regression");
    }

    private static void ChainCheckpointRestoresOnlyWithReplayEvidence()
    {
        ContinuityCapsuleChain chain = WithBaseAndFirstDelta();
        ContinuityCapsuleChainState checkpoint = chain.CaptureState();
        ContinuityCapsuleChain restored = ContinuityCapsuleChain.Restore(
            checkpoint,
            [Key()],
            new FakeVerifier(),
            new FakeDecoder());
        ContinuityCapsuleDelta second = Delta(
            DeltaTwoId,
            previousDeltaSha256: Hash('b'),
            deltaSha256: Hash('c'),
            checkpointSequence: 32,
            simTimeNs: 102);
        byte[] payload = Payload("delta-two");
        Check.That(restored.AcceptDelta(
                SignedBody(second, payload),
                payload,
                102,
                13).State.Frontier!.LastDeltaSha256 == Hash('c'),
            "a restored chain must accept only the exact next delta link");

        ContinuityCapsuleChainState forged = checkpoint with
        {
            Frontier = checkpoint.Frontier! with
            {
                LastDeltaId = DeltaTwoId,
            },
        };
        Check.That(Reason(() => ContinuityCapsuleChain.Restore(
                forged,
                [Key()],
                new FakeVerifier(),
                new FakeDecoder())) == "ContinuityChain.InvalidCheckpoint",
            "a frontier cannot name a delta absent from replay evidence");
    }

    private static void TwoPhaseAdmissionsCannotCommitOutOfOrder()
    {
        ContinuitySignatureGate gate = ContinuitySignatureGate.Start(
            SessionId,
            InstanceId,
            authorityEpoch: 4,
            authorityMonotonicNs: 10,
            [Key()],
            new FakeVerifier());
        byte[] firstPayload = Payload("first");
        byte[] secondPayload = Payload("second");
        ContinuitySignatureAdmission first = gate.Verify(
            SignedBody("ContinuityCapsuleBase", BaseId, firstPayload),
            Proof(),
            firstPayload,
            currentSimTimeNs: 100,
            authorityMonotonicNs: 11);
        ContinuitySignatureAdmission second = gate.Verify(
            SignedBody("ContinuityCapsuleBase", DeltaOneId, secondPayload),
            Proof(),
            secondPayload,
            currentSimTimeNs: 100,
            authorityMonotonicNs: 11);

        _ = gate.Commit(first);
        Check.That(Reason(() => gate.Commit(second)) ==
                "ContinuityCrypto.StaleAdmission" &&
            gate.CaptureState().AcceptedCapsuleIds.SequenceEqual([BaseId]),
            "committing one admission must invalidate every sibling trial");
    }

    private static void DecodedCapsulesAreDefensivelyCopied()
    {
        FakeDecoder decoder = new();
        ContinuityCapsuleChain chain = Start(decoder);
        byte[] payload = Payload("base");
        AcceptedContinuityCapsuleBase accepted = chain.AcceptBase(
            SignedBody(Base(), payload),
            payload,
            currentSimTimeNs: 100,
            authorityMonotonicNs: 11);
        decoder.LastDecodedBase!.StateComponents[0].StateBytes[0] = 0xff;

        Check.That(accepted.Capsule.StateComponents[0].StateBytes[0] == 0x00,
            "an adapter cannot mutate the accepted state after decoding returns");
    }

    private static ContinuityCapsuleChain WithBase()
    {
        ContinuityCapsuleChain chain = Start();
        ContinuityCapsuleBase capsule = Base();
        byte[] payload = Payload("base");
        _ = chain.AcceptBase(
            SignedBody(capsule, payload),
            payload,
            100,
            11);
        return chain;
    }

    private static ContinuityCapsuleChain WithBaseAndFirstDelta()
    {
        ContinuityCapsuleChain chain = WithBase();
        ContinuityCapsuleDelta delta = Delta(
            DeltaOneId,
            previousDeltaSha256: null,
            deltaSha256: Hash('b'),
            checkpointSequence: 31,
            simTimeNs: 101);
        byte[] payload = Payload("delta-one");
        _ = chain.AcceptDelta(
            SignedBody(delta, payload),
            payload,
            101,
            12);
        return chain;
    }

    private static ContinuityCapsuleChain Start(FakeDecoder? decoder = null) =>
        ContinuityCapsuleChain.Start(
            SessionId,
            InstanceId,
            authorityEpoch: 4,
            authorityMonotonicNs: 10,
            [Key()],
            new FakeVerifier(),
            decoder ?? new FakeDecoder());

    private static ContinuityCapsuleBase Base() => new(
        BaseId,
        SessionId,
        InstanceId,
        BranchId: "main",
        AuthorityEpoch: 4,
        TimebaseEpoch: 2,
        StreamEpoch: 9,
        CheckpointSequence: 30,
        CommitSequence: 400,
        SimTimeNs: 100,
        NextEventSequence: 401,
        ChannelCursors:
        [
            new("ECG.II", 2_000, 50, 41),
            new("Pleth", 500, 50, 41),
        ],
        StateComponents:
        [
            Component("filter.ecg", 'f'),
            Component("prng.rhythm", 'e'),
        ],
        new ContinuityProjectionRevisions(2, 9, 5, 8),
        Reference("SimulationCore", '1'),
        Reference("DeterminismProfile.FixedV1", '2'),
        ResourcePackReferences: [],
        ContinuityPermissionState.Allowed,
        SecretBoundarySimTimeNs: 120,
        ContinuationValidUntilSimTimeNs: 120,
        CapsuleSha256: Hash('a'),
        Proof(),
        EncryptionEnvelopeReference: null);

    private static ContinuityCapsuleDelta Delta(
        Guid deltaId,
        string? previousDeltaSha256,
        string deltaSha256,
        ulong checkpointSequence,
        long simTimeNs) => new(
        deltaId,
        BaseSha256: Hash('a'),
        previousDeltaSha256,
        checkpointSequence,
        CommitSequence: 400,
        simTimeNs,
        ChangedComponents: [Component("prng.rhythm", 'e')],
        SecretBoundarySimTimeNs: 120,
        ContinuationValidUntilSimTimeNs: 120,
        deltaSha256,
        Proof());

    private static ContinuityStateComponent Component(
        string componentId,
        char hashCharacter) => new(
        componentId,
        Reference("State.PRNG.Xoshiro256", hashCharacter),
        Hash(hashCharacter),
        [0x00]);

    private static ContinuityVersionedReference Reference(
        string id,
        char hashCharacter) => new(id, "1.0.0", Hash(hashCharacter));

    private static TrustedContinuitySigningKey Key() => new(
        KeyId,
        [0x04, 0x01, .. new byte[63]],
        ContinuitySigningKeyState.Active,
        AuthorityEpoch: 4,
        ActivatedAtAuthorityMonotonicNs: 0,
        ExpiresAtAuthorityMonotonicNs: 1_000_000);

    private static ContinuitySignatureProof Proof() => new(
        ContinuitySignatureGate.SignatureAlgorithmId,
        KeyId,
        SignedSchemaMajor: 1,
        SignedSchemaMinor: 0,
        new byte[64]);

    private static ContinuitySignedBody SignedBody(
        ContinuityCapsuleBase capsule,
        byte[] payload) => new(
        "ContinuityCapsuleBase",
        capsule.CapsuleId,
        capsule.SessionId,
        capsule.InstanceId,
        capsule.AuthorityEpoch,
        capsule.SecretBoundarySimTimeNs,
        capsule.ContinuationValidUntilSimTimeNs,
        PayloadHash(payload));

    private static ContinuitySignedBody SignedBody(
        ContinuityCapsuleDelta delta,
        byte[] payload) =>
        SignedBody("ContinuityCapsuleDelta", delta.DeltaId, payload) with
        {
            SecretBoundarySimTimeNs = delta.SecretBoundarySimTimeNs,
            ContinuationValidUntilSimTimeNs =
                delta.ContinuationValidUntilSimTimeNs,
        };

    private static ContinuitySignedBody SignedBody(
        string capsuleType,
        Guid capsuleId,
        byte[] payload) => new(
        capsuleType,
        capsuleId,
        SessionId,
        InstanceId,
        AuthorityEpoch: 4,
        SecretBoundarySimTimeNs: 120,
        ContinuationValidUntilSimTimeNs: 120,
        PayloadHash(payload));

    private static bool SameState(
        ContinuityCapsuleChainState expected,
        ContinuityCapsuleChainState actual) =>
        expected.Frontier == actual.Frontier &&
        expected.SignatureGate.SessionId == actual.SignatureGate.SessionId &&
        expected.SignatureGate.InstanceId == actual.SignatureGate.InstanceId &&
        expected.SignatureGate.AuthorityEpoch ==
            actual.SignatureGate.AuthorityEpoch &&
        expected.SignatureGate.LastAuthorityMonotonicNs ==
            actual.SignatureGate.LastAuthorityMonotonicNs &&
        expected.SignatureGate.AcceptedCapsuleIds.SequenceEqual(
            actual.SignatureGate.AcceptedCapsuleIds);

    private static byte[] Payload(string value) => Encoding.UTF8.GetBytes(value);

    private static string PayloadHash(byte[] payload) => Convert.ToHexString(
        SHA256.HashData(payload)).ToLowerInvariant();

    private static string Hash(char character) => new(character, 64);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (ContinuityCapsuleChainException exception)
        {
            return exception.ReasonCode;
        }
        catch (ContinuitySignatureGateException exception)
        {
            return exception.ReasonCode;
        }
    }

    private sealed class FakeDecoder : IContinuityCapsulePayloadDecoder
    {
        public ContinuityCapsuleBase? LastDecodedBase { get; private set; }

        public ContinuityCapsuleBase DecodeBase(
            ReadOnlyMemory<byte> signedPayload)
        {
            ContinuityCapsuleBase result =
                Encoding.UTF8.GetString(signedPayload.Span) switch
                {
                    "base" => Base(),
                    "base-denied" => Base() with
                    {
                        ContinuationPermission =
                            ContinuityPermissionState.DisallowedByCourse,
                    },
                    _ => throw new ArgumentException("Unknown base payload."),
                };
            LastDecodedBase = result;
            return result;
        }

        public ContinuityCapsuleDelta DecodeDelta(
            ReadOnlyMemory<byte> signedPayload) =>
            Encoding.UTF8.GetString(signedPayload.Span) switch
            {
                "delta-one" => Delta(
                    DeltaOneId,
                    previousDeltaSha256: null,
                    deltaSha256: Hash('b'),
                    checkpointSequence: 31,
                    simTimeNs: 101),
                "delta-two" => Delta(
                    DeltaTwoId,
                    previousDeltaSha256: Hash('b'),
                    deltaSha256: Hash('c'),
                    checkpointSequence: 32,
                    simTimeNs: 102),
                "delta-one-wrong-base" => Delta(
                    DeltaOneId,
                    previousDeltaSha256: null,
                    deltaSha256: Hash('b'),
                    checkpointSequence: 31,
                    simTimeNs: 101) with
                {
                    BaseSha256 = Hash('f'),
                },
                "delta-unsorted" => Delta(
                    DeltaOneId,
                    previousDeltaSha256: null,
                    deltaSha256: Hash('b'),
                    checkpointSequence: 31,
                    simTimeNs: 101) with
                {
                    ChangedComponents =
                    [
                        Component("prng.rhythm", 'e'),
                        Component("filter.ecg", 'f'),
                    ],
                },
                "delta-stale" => Delta(
                    DeltaOneId,
                    previousDeltaSha256: null,
                    deltaSha256: Hash('b'),
                    checkpointSequence: 30,
                    simTimeNs: 101),
                _ => throw new ArgumentException("Unknown delta payload."),
            };
    }

    private sealed class FakeVerifier : IContinuitySignatureVerifier
    {
        public string DeriveKeyId(ReadOnlyMemory<byte> publicKeySec1) => KeyId;

        public bool Verify(
            ContinuitySignedBody body,
            ReadOnlyMemory<byte> signatureRs64,
            ReadOnlyMemory<byte> publicKeySec1) =>
            signatureRs64.Length == 64 && publicKeySec1.Span[1] == 1;
    }
}
