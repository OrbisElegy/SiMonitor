// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Monitor.Application.Continuity;
using Monitor.Domain.Continuity;

namespace Monitor.Specs;

internal static class ContinuityShadowSpecifications
{
    private const string KeyId =
        "sm2-sm3:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly Guid SessionId =
        Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid InstanceId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid BaseId =
        Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    public static Specification[] All =>
    [
        new(nameof(CompleteOverlapEnablesProvisionalContinuation),
            CompleteOverlapEnablesProvisionalContinuation),
        new(nameof(EveryProofDigestMustMatch), EveryProofDigestMustMatch),
        new(nameof(MissingOrMalformedHostEvidenceFailsClosed),
            MissingOrMalformedHostEvidenceFailsClosed),
        new(nameof(ARecoveryImageIsRequiredBeforeGeneration),
            ARecoveryImageIsRequiredBeforeGeneration),
        new(nameof(OverlapMustRemainInsideTheCapsuleWindow),
            OverlapMustRemainInsideTheCapsuleWindow),
        new(nameof(GeneratorFailuresMapToExplicitStopReasons),
            GeneratorFailuresMapToExplicitStopReasons),
        new(nameof(InvalidTransitionsDoNotInvokeTheGenerator),
            InvalidTransitionsDoNotInvokeTheGenerator),
    ];

    private static void CompleteOverlapEnablesProvisionalContinuation()
    {
        ContinuityCapsuleChain chain = WithBase();
        DataContinuityStateMachine data = Disconnected(hasBuffer: true);
        FakeGenerator generator = new(request => new(
            ContinuityShadowGenerationStatus.Completed,
            Proofs()));
        ContinuityShadowCoordinator coordinator = new(chain, data, generator);

        ContinuityShadowVerificationResult result = coordinator.VerifyOverlap(
            Proofs(),
            authorityMonotonicNs: 2);
        Check.That(result.Outcome ==
                ContinuityShadowVerificationOutcome.Verified &&
            result.Request is
            {
                BaseCapsuleId: var baseId,
                CheckpointSequence: 30,
                TimebaseEpoch: 2,
                StreamEpoch: 9,
                FirstBlockSequence: 7,
                FirstBlockStartSimTimeNs: 1_000_000_000,
                BlockCount: 2,
            } &&
            baseId == BaseId &&
            result.DataContinuityState.DataAvailability ==
                DataAvailability.Buffered &&
            result.DataContinuityState.ShadowVerification ==
                ShadowVerificationState.Verified &&
            generator.CallCount == 1,
            "all three overlap digests must verify one isolated shadow request");

        generator.LastImage!.StateComponents[0].StateBytes[0] = 0xff;
        Check.That(chain.RecoveryStateImage!.StateComponents[0].StateBytes[0] ==
                0x42 &&
            data.ExhaustAuthoritativeBuffer(3).DataAvailability ==
                DataAvailability.LocalContinuation,
            "the generator must receive a copy and only a match may cross the tail");
    }

    private static void EveryProofDigestMustMatch()
    {
        for (int changedDigest = 0; changedDigest < 3; changedDigest++)
        {
            ContinuityCapsuleChain chain = WithBase();
            DataContinuityStateMachine data = Disconnected(hasBuffer: true);
            ContinuityShadowBlockProof[] generated = Proofs();
            generated[1] = changedDigest switch
            {
                0 => generated[1] with
                {
                    WaveformContentSha256 = Hash('1'),
                },
                1 => generated[1] with
                {
                    NumericFramesSha256 = Hash('2'),
                },
                _ => generated[1] with
                {
                    CanonicalStateSha256 = Hash('3'),
                },
            };
            ContinuityShadowCoordinator coordinator = new(
                chain,
                data,
                new FakeGenerator(_ => new(
                    ContinuityShadowGenerationStatus.Completed,
                    generated)));

            ContinuityShadowVerificationResult result =
                coordinator.VerifyOverlap(Proofs(), 2);
            DataContinuityState noData = data.ExhaustAuthoritativeBuffer(3);
            Check.That(result.Outcome ==
                    ContinuityShadowVerificationOutcome.Mismatch &&
                result.DataContinuityState.DataAvailability ==
                    DataAvailability.Buffered &&
                result.DataContinuityState.ShadowVerification ==
                    ShadowVerificationState.Rejected &&
                noData.DataAvailability == DataAvailability.NoData &&
                noData.ContinuationStopReason ==
                    ContinuationStopReason.ShadowMismatch,
                "waveform, numeric and canonical state hashes must all match");
        }
    }

    private static void MissingOrMalformedHostEvidenceFailsClosed()
    {
        foreach (ContinuityShadowBlockProof[] evidence in new[]
        {
            Array.Empty<ContinuityShadowBlockProof>(),
            new[] { Proofs()[0], Proofs()[1] with { BlockSequence = 9 } },
            new[]
            {
                Proofs()[0] with { NumericFramesSha256 = Hash('A') },
            },
        })
        {
            ContinuityCapsuleChain chain = WithBase();
            DataContinuityStateMachine data = Disconnected(hasBuffer: false);
            FakeGenerator generator = new(_ => throw new InvalidOperationException());
            ContinuityShadowVerificationResult result =
                new ContinuityShadowCoordinator(chain, data, generator)
                    .VerifyOverlap(evidence, 2);

            Check.That(result.Outcome ==
                    ContinuityShadowVerificationOutcome.InvalidEvidence &&
                result.Request is null &&
                result.DataContinuityState.DataAvailability ==
                    DataAvailability.NoData &&
                result.DataContinuityState.ContinuationStopReason ==
                    ContinuationStopReason.ConsistencyMismatch &&
                generator.CallCount == 0,
                "missing, discontinuous or noncanonical evidence must fail closed");
        }
    }

    private static void ARecoveryImageIsRequiredBeforeGeneration()
    {
        ContinuityCapsuleChain chain = StartChain();
        DataContinuityStateMachine data = Disconnected(hasBuffer: false);
        FakeGenerator generator = new(_ => throw new InvalidOperationException());

        ContinuityShadowVerificationResult result =
            new ContinuityShadowCoordinator(chain, data, generator)
                .VerifyOverlap(Proofs(), 2);
        Check.That(result.Outcome ==
                ContinuityShadowVerificationOutcome.CapsuleInvalid &&
            result.Request is null &&
            result.DataContinuityState.ContinuationStopReason ==
                ContinuationStopReason.CapsuleInvalid &&
            generator.CallCount == 0,
            "a seed or Host overlap cannot replace an authenticated state image");
    }

    private static void OverlapMustRemainInsideTheCapsuleWindow()
    {
        foreach (ContinuityShadowBlockProof[] outside in new[]
        {
            new[]
            {
                Proofs()[0] with { StartSimTimeNs = 0 },
            },
            new[]
            {
                Proofs()[0] with { StartSimTimeNs = 2_000_000_000 },
            },
        })
        {
            DataContinuityStateMachine data = Disconnected(hasBuffer: false);
            FakeGenerator generator = new(_ => throw new InvalidOperationException());
            ContinuityShadowVerificationResult result =
                new ContinuityShadowCoordinator(WithBase(), data, generator)
                    .VerifyOverlap(outside, 2);
            Check.That(result.Outcome ==
                    ContinuityShadowVerificationOutcome.CapsuleInvalid &&
                result.Request is null &&
                result.DataContinuityState.ContinuationStopReason ==
                    ContinuationStopReason.CapsuleInvalid &&
                generator.CallCount == 0,
                "shadow output cannot precede or outlive its capsule permission");
        }
    }

    private static void GeneratorFailuresMapToExplicitStopReasons()
    {
        foreach ((ContinuityShadowGenerationResult generation,
            ContinuityShadowVerificationOutcome outcome,
            ContinuationStopReason reason) in new[]
        {
            (new ContinuityShadowGenerationResult(
                    ContinuityShadowGenerationStatus.CapsuleRejected,
                    []),
                ContinuityShadowVerificationOutcome.CapsuleInvalid,
                ContinuationStopReason.CapsuleInvalid),
            (new ContinuityShadowGenerationResult(
                    ContinuityShadowGenerationStatus.PerformanceInsufficient,
                    []),
                ContinuityShadowVerificationOutcome.PerformanceInsufficient,
                ContinuationStopReason.PerformanceInsufficient),
            (new ContinuityShadowGenerationResult(
                    ContinuityShadowGenerationStatus.Completed,
                    [Proofs()[0]]),
                ContinuityShadowVerificationOutcome.InvalidEvidence,
                ContinuationStopReason.ConsistencyMismatch),
        })
        {
            DataContinuityStateMachine data = Disconnected(hasBuffer: false);
            ContinuityShadowVerificationResult result =
                new ContinuityShadowCoordinator(
                    WithBase(),
                    data,
                    new FakeGenerator(_ => generation))
                .VerifyOverlap(Proofs(), 2);
            Check.That(result.Outcome == outcome &&
                result.Request is not null &&
                result.DataContinuityState.ContinuationStopReason == reason,
                "generator rejection, timing failure and malformed output differ");
        }

        DataContinuityStateMachine throwingData = Disconnected(hasBuffer: false);
        ContinuityShadowVerificationResult throwing =
            new ContinuityShadowCoordinator(
                WithBase(),
                throwingData,
                new FakeGenerator(_ => throw new ArgumentException(
                    "state rejected",
                    "request")))
            .VerifyOverlap(Proofs(), 2);
        Check.That(throwing.Outcome ==
                ContinuityShadowVerificationOutcome.InvalidEvidence &&
            throwing.DataContinuityState.ContinuationStopReason ==
                ContinuationStopReason.ConsistencyMismatch,
            "a bounded generator input rejection must not leave verification pending");
    }

    private static void InvalidTransitionsDoNotInvokeTheGenerator()
    {
        ContinuityCapsuleChain chain = WithBase();
        FakeGenerator generator = new(_ => new(
            ContinuityShadowGenerationStatus.Completed,
            Proofs()));
        var connected = DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration,
            0);
        ContinuityShadowCoordinator wrongPhase = new(
            chain,
            connected,
            generator);
        Check.That(Reason(() => wrongPhase.VerifyOverlap(Proofs(), 1)) ==
                "ContinuityShadow.InvalidTransition" &&
            generator.CallCount == 0 &&
            connected.CaptureState().ConnectionState == ConnectionState.Connected,
            "verification cannot run outside a pending disconnected episode");

        DataContinuityStateMachine disconnected = Disconnected(hasBuffer: true);
        DataContinuityState before = disconnected.CaptureState();
        ContinuityShadowCoordinator reversed = new(chain, disconnected, generator);
        Check.That(Reason(() => reversed.VerifyOverlap(Proofs(), 0)) ==
                "ContinuityShadow.TimeReversed" &&
            generator.CallCount == 0 &&
            disconnected.CaptureState() == before,
            "a reversed authority clock must reject before generation or mutation");
    }

    private static ContinuityShadowBlockProof[] Proofs() =>
    [
        new(7, 1_000_000_000, Hash('a'), Hash('b'), Hash('c')),
        new(8, 1_200_000_000, Hash('d'), Hash('e'), Hash('f')),
    ];

    private static DataContinuityStateMachine Disconnected(bool hasBuffer)
    {
        var data = DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration,
            0);
        _ = data.Disconnect(hasBuffer, 1);
        return data;
    }

    private static ContinuityCapsuleChain WithBase()
    {
        ContinuityCapsuleChain chain = StartChain();
        byte[] payload = Encoding.UTF8.GetBytes("base");
        ContinuityCapsuleBase capsule = Base();
        _ = chain.AcceptBase(
            new ContinuitySignedBody(
                "ContinuityCapsuleBase",
                capsule.CapsuleId,
                capsule.SessionId,
                capsule.InstanceId,
                capsule.AuthorityEpoch,
                capsule.SecretBoundarySimTimeNs,
                capsule.ContinuationValidUntilSimTimeNs,
                Sha256(payload)),
            payload,
            currentSimTimeNs: 100,
            authorityMonotonicNs: 11);
        return chain;
    }

    private static ContinuityCapsuleChain StartChain() =>
        ContinuityCapsuleChain.Start(
            SessionId,
            InstanceId,
            authorityEpoch: 4,
            authorityMonotonicNs: 10,
            [new TrustedContinuitySigningKey(
                KeyId,
                [0x04, 0x01, .. new byte[63]],
                ContinuitySigningKeyState.Active,
                AuthorityEpoch: 4,
                ActivatedAtAuthorityMonotonicNs: 0,
                ExpiresAtAuthorityMonotonicNs: 1_000_000)],
            new FakeVerifier(),
            new ContinuityRuntimeCapabilities(
                Reference("SimulationCore", '1'),
                Reference("DeterminismProfile.FixedV1", '2'),
                ResourcePackReferences: [],
                ChannelIds: ["ECG.II"],
                StateComponents:
                [
                    new("kernel", Reference("State.Kernel", '3')),
                ]),
            new FakeDecoder());

    private static ContinuityCapsuleBase Base()
    {
        byte[] state = [0x42];
        return new ContinuityCapsuleBase(
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
            ChannelCursors: [new("ECG.II", 2_000, 7, 41)],
            StateComponents:
            [
                new(
                    "kernel",
                    Reference("State.Kernel", '3'),
                    Sha256(state),
                    state),
            ],
            new ContinuityProjectionRevisions(2, 9, 5, 8),
            Reference("SimulationCore", '1'),
            Reference("DeterminismProfile.FixedV1", '2'),
            ResourcePackReferences: [],
            ContinuityPermissionState.Allowed,
            SecretBoundarySimTimeNs: 2_000_000_000,
            ContinuationValidUntilSimTimeNs: 2_000_000_000,
            CapsuleSha256: Hash('9'),
            new ContinuitySignatureProof(
                ContinuitySignatureGate.SignatureAlgorithmId,
                KeyId,
                SignedSchemaMajor: 1,
                SignedSchemaMinor: 0,
                new byte[64]),
            EncryptionEnvelopeReference: null);
    }

    private static ContinuityVersionedReference Reference(
        string id,
        char hashCharacter) => new(id, "1.0.0", Hash(hashCharacter));

    private static string Hash(char character) => new(character, 64);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (ContinuityShadowCoordinatorException exception)
        {
            return exception.ReasonCode;
        }
    }

    private sealed class FakeGenerator(
        Func<ContinuityShadowGenerationRequest,
            ContinuityShadowGenerationResult> generate)
        : IContinuityShadowGenerator
    {
        public int CallCount { get; private set; }

        public ContinuityRecoveryStateImage? LastImage { get; private set; }

        public ContinuityShadowGenerationResult Generate(
            ContinuityRecoveryStateImage recoveryStateImage,
            ContinuityShadowGenerationRequest request)
        {
            CallCount++;
            LastImage = recoveryStateImage;
            return generate(request);
        }
    }

    private sealed class FakeDecoder : IContinuityCapsulePayloadDecoder
    {
        public ContinuityCapsuleBase DecodeBase(
            ReadOnlyMemory<byte> signedPayload) =>
            Encoding.UTF8.GetString(signedPayload.Span) == "base"
                ? Base()
                : throw new ArgumentException(
                    "unknown payload",
                    nameof(signedPayload));

        public ContinuityCapsuleDelta DecodeDelta(
            ReadOnlyMemory<byte> signedPayload) =>
            throw new ArgumentException(
                "delta unsupported",
                nameof(signedPayload));
    }

    private sealed class FakeVerifier : IContinuitySignatureVerifier
    {
        public string DeriveKeyId(ReadOnlyMemory<byte> publicKeySec1) => KeyId;

        public bool Verify(
            ContinuitySignedBody body,
            ReadOnlyMemory<byte> signatureRs64,
            ReadOnlyMemory<byte> publicKeySec1) => true;
    }
}
