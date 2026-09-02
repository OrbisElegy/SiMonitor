// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Monitor.Domain.Continuity;

namespace Monitor.Application.Continuity;

public enum ContinuityShadowGenerationStatus
{
    Completed = 0,
    CapsuleRejected = 1,
    PerformanceInsufficient = 2,
}

public enum ContinuityShadowVerificationOutcome
{
    Verified = 0,
    Mismatch = 1,
    CapsuleInvalid = 2,
    PerformanceInsufficient = 3,
    InvalidEvidence = 4,
}

public sealed record ContinuityShadowBlockProof(
    ulong BlockSequence,
    long StartSimTimeNs,
    string WaveformContentSha256,
    string NumericFramesSha256,
    string CanonicalStateSha256);

public sealed record ContinuityShadowGenerationRequest(
    Guid BaseCapsuleId,
    string BaseSha256,
    Guid? LastDeltaId,
    string? LastDeltaSha256,
    ulong CheckpointSequence,
    ulong CommitSequence,
    ulong TimebaseEpoch,
    ulong StreamEpoch,
    ulong FirstBlockSequence,
    long FirstBlockStartSimTimeNs,
    int BlockCount);

public sealed record ContinuityShadowGenerationResult(
    ContinuityShadowGenerationStatus Status,
    IReadOnlyList<ContinuityShadowBlockProof> BlockProofs);

public sealed record ContinuityShadowVerificationResult(
    ContinuityShadowVerificationOutcome Outcome,
    ContinuityShadowGenerationRequest? Request,
    DataContinuityState DataContinuityState);

public interface IContinuityShadowGenerator
{
    public ContinuityShadowGenerationResult Generate(
        ContinuityRecoveryStateImage recoveryStateImage,
        ContinuityShadowGenerationRequest request);
}

public sealed class ContinuityShadowCoordinatorException(
    string reasonCode,
    string parameterName,
    Exception? innerException = null)
    : ArgumentException(reasonCode, parameterName, innerException)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class ContinuityShadowCoordinator
{
    public const long BlockDurationNs = 200_000_000;
    public const int MaximumOverlapBlockCount = 50;

    private readonly ContinuityCapsuleChain _capsuleChain;
    private readonly DataContinuityStateMachine _dataContinuity;
    private readonly IContinuityShadowGenerator _generator;

    public ContinuityShadowCoordinator(
        ContinuityCapsuleChain capsuleChain,
        DataContinuityStateMachine dataContinuity,
        IContinuityShadowGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(capsuleChain);
        ArgumentNullException.ThrowIfNull(dataContinuity);
        ArgumentNullException.ThrowIfNull(generator);
        _capsuleChain = capsuleChain;
        _dataContinuity = dataContinuity;
        _generator = generator;
    }

    public ContinuityShadowVerificationResult VerifyOverlap(
        IReadOnlyList<ContinuityShadowBlockProof> authoritativeOverlap,
        long authorityMonotonicNs)
    {
        DataContinuityState state = _dataContinuity.CaptureState();
        if (state.ConnectionState != ConnectionState.Disconnected ||
            state.ShadowVerification != ShadowVerificationState.Pending)
        {
            throw Error(
                "ContinuityShadow.InvalidTransition",
                nameof(authoritativeOverlap));
        }

        if (authorityMonotonicNs < state.LastAuthorityMonotonicNs)
        {
            throw Error(
                "ContinuityShadow.TimeReversed",
                nameof(authorityMonotonicNs));
        }

        ContinuityRecoveryStateImage? image =
            _capsuleChain.RecoveryStateImage;
        if (image is null)
        {
            return Fail(
                ContinuityShadowVerificationOutcome.CapsuleInvalid,
                LocalContinuationFailure.CapsuleInvalid,
                request: null,
                authorityMonotonicNs);
        }

        if (!TryCopyAuthoritativeProofs(
                authoritativeOverlap,
                out ContinuityShadowBlockProof[] expected))
        {
            return Fail(
                ContinuityShadowVerificationOutcome.InvalidEvidence,
                LocalContinuationFailure.ConsistencyMismatch,
                request: null,
                authorityMonotonicNs);
        }

        if (!OverlapFitsCapsuleWindow(expected, image))
        {
            return Fail(
                ContinuityShadowVerificationOutcome.CapsuleInvalid,
                LocalContinuationFailure.CapsuleInvalid,
                request: null,
                authorityMonotonicNs);
        }

        ContinuityShadowGenerationRequest request = CreateRequest(
            expected,
            image);

        ContinuityShadowGenerationResult generated;
        try
        {
            generated = _generator.Generate(image, request);
        }
        catch (ArgumentException)
        {
            return Fail(
                ContinuityShadowVerificationOutcome.InvalidEvidence,
                LocalContinuationFailure.ConsistencyMismatch,
                request,
                authorityMonotonicNs);
        }

        if (generated is null ||
            !Enum.IsDefined(generated.Status) ||
            generated.BlockProofs is null)
        {
            return Fail(
                ContinuityShadowVerificationOutcome.InvalidEvidence,
                LocalContinuationFailure.ConsistencyMismatch,
                request,
                authorityMonotonicNs);
        }

        if (generated.Status != ContinuityShadowGenerationStatus.Completed &&
            generated.BlockProofs.Count != 0)
        {
            return Fail(
                ContinuityShadowVerificationOutcome.InvalidEvidence,
                LocalContinuationFailure.ConsistencyMismatch,
                request,
                authorityMonotonicNs);
        }

        if (generated.Status ==
            ContinuityShadowGenerationStatus.CapsuleRejected)
        {
            return Fail(
                ContinuityShadowVerificationOutcome.CapsuleInvalid,
                LocalContinuationFailure.CapsuleInvalid,
                request,
                authorityMonotonicNs);
        }

        if (generated.Status ==
            ContinuityShadowGenerationStatus.PerformanceInsufficient)
        {
            return Fail(
                ContinuityShadowVerificationOutcome.PerformanceInsufficient,
                LocalContinuationFailure.PerformanceInsufficient,
                request,
                authorityMonotonicNs);
        }

        if (generated.BlockProofs.Count != request.BlockCount)
        {
            return Fail(
                ContinuityShadowVerificationOutcome.InvalidEvidence,
                LocalContinuationFailure.ConsistencyMismatch,
                request,
                authorityMonotonicNs);
        }

        ContinuityShadowBlockProof[] generatedProofs;
        if (!TryCopyProofs(generated.BlockProofs, out generatedProofs))
        {
            return Fail(
                ContinuityShadowVerificationOutcome.InvalidEvidence,
                LocalContinuationFailure.ConsistencyMismatch,
                request,
                authorityMonotonicNs);
        }

        if (!ProofsHaveExpectedShape(generatedProofs, request))
        {
            return Fail(
                ContinuityShadowVerificationOutcome.InvalidEvidence,
                LocalContinuationFailure.ConsistencyMismatch,
                request,
                authorityMonotonicNs);
        }

        bool matched = ProofsMatch(
            expected,
            generatedProofs);
        DataContinuityState next = _dataContinuity.RecordShadowVerification(
            matched,
            authorityMonotonicNs);
        return new ContinuityShadowVerificationResult(
            matched
                ? ContinuityShadowVerificationOutcome.Verified
                : ContinuityShadowVerificationOutcome.Mismatch,
            request,
            next);
    }

    private ContinuityShadowVerificationResult Fail(
        ContinuityShadowVerificationOutcome outcome,
        LocalContinuationFailure failure,
        ContinuityShadowGenerationRequest? request,
        long authorityMonotonicNs) => new(
        outcome,
        request,
        _dataContinuity.RecordContinuationFailure(
            failure,
            authorityMonotonicNs));

    private static ContinuityShadowGenerationRequest CreateRequest(
        ContinuityShadowBlockProof[] authoritativeOverlap,
        ContinuityRecoveryStateImage image)
    {
        ContinuityShadowBlockProof first = authoritativeOverlap[0];
        return new ContinuityShadowGenerationRequest(
            image.BaseCapsuleId,
            image.BaseSha256,
            image.LastDeltaId,
            image.LastDeltaSha256,
            image.CheckpointSequence,
            image.CommitSequence,
            image.TimebaseEpoch,
            image.StreamEpoch,
            first.BlockSequence,
            first.StartSimTimeNs,
            authoritativeOverlap.Length);
    }

    private static bool OverlapFitsCapsuleWindow(
        ContinuityShadowBlockProof[] proofs,
        ContinuityRecoveryStateImage image)
    {
        ContinuityShadowBlockProof first = proofs[0];
        ContinuityShadowBlockProof last = proofs[^1];
        return first.StartSimTimeNs >= image.SimTimeNs &&
            last.StartSimTimeNs <= long.MaxValue - BlockDurationNs &&
            last.StartSimTimeNs + BlockDurationNs <=
                image.ContinuationValidUntilSimTimeNs;
    }

    private static bool TryCopyAuthoritativeProofs(
        IReadOnlyList<ContinuityShadowBlockProof> proofs,
        out ContinuityShadowBlockProof[] copy)
    {
        copy = [];
        if (proofs is null ||
            proofs.Count is < 1 or > MaximumOverlapBlockCount)
        {
            return false;
        }

        return TryCopyProofs(proofs, out copy) &&
            ProofsHaveCanonicalShape(copy);
    }

    private static bool TryCopyProofs(
        IReadOnlyList<ContinuityShadowBlockProof> proofs,
        out ContinuityShadowBlockProof[] copy)
    {
        copy = new ContinuityShadowBlockProof[proofs.Count];
        for (int index = 0; index < proofs.Count; index++)
        {
            ContinuityShadowBlockProof? proof = proofs[index];
            if (proof is null)
            {
                copy = [];
                return false;
            }

            copy[index] = proof with { };
        }

        return true;
    }

    private static bool ProofsHaveExpectedShape(
        ContinuityShadowBlockProof[] proofs,
        ContinuityShadowGenerationRequest request) =>
        proofs.Length == request.BlockCount &&
        ProofsHaveCanonicalShape(proofs) &&
        proofs[0].BlockSequence == request.FirstBlockSequence &&
        proofs[0].StartSimTimeNs == request.FirstBlockStartSimTimeNs;

    private static bool ProofsHaveCanonicalShape(
        ContinuityShadowBlockProof[] proofs)
    {
        for (int index = 0; index < proofs.Length; index++)
        {
            ContinuityShadowBlockProof? proof = proofs[index];
            if (proof is null ||
                proof.StartSimTimeNs < 0 ||
                !IsSha256(proof.WaveformContentSha256) ||
                !IsSha256(proof.NumericFramesSha256) ||
                !IsSha256(proof.CanonicalStateSha256))
            {
                return false;
            }

            if (index == 0)
            {
                continue;
            }

            ContinuityShadowBlockProof previous = proofs[index - 1];
            if (previous.BlockSequence == ulong.MaxValue ||
                proof.BlockSequence != previous.BlockSequence + 1 ||
                previous.StartSimTimeNs > long.MaxValue - BlockDurationNs ||
                proof.StartSimTimeNs !=
                    previous.StartSimTimeNs + BlockDurationNs)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ProofsMatch(
        ContinuityShadowBlockProof[] expected,
        ContinuityShadowBlockProof[] actual)
    {
        for (int index = 0; index < expected.Length; index++)
        {
            ContinuityShadowBlockProof left = expected[index];
            ContinuityShadowBlockProof right = actual[index];
            if (left.BlockSequence != right.BlockSequence ||
                left.StartSimTimeNs != right.StartSimTimeNs ||
                !HashesEqual(
                    left.WaveformContentSha256,
                    right.WaveformContentSha256) ||
                !HashesEqual(
                    left.NumericFramesSha256,
                    right.NumericFramesSha256) ||
                !HashesEqual(
                    left.CanonicalStateSha256,
                    right.CanonicalStateSha256))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HashesEqual(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ContinuityShadowCoordinatorException Error(
        string reasonCode,
        string parameterName,
        Exception? innerException = null) =>
        new(reasonCode, parameterName, innerException);
}
