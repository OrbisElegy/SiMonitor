// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;

namespace Monitor.Application.Continuity;

public enum ContinuitySigningKeyState
{
    PreActive = 0,
    Active = 1,
    Retiring = 2,
    Revoked = 3,
    Expired = 4,
}

public sealed record ContinuitySignedBody(
    string CapsuleType,
    Guid CapsuleId,
    Guid SessionId,
    Guid InstanceId,
    ulong AuthorityEpoch,
    long SecretBoundarySimTimeNs,
    long ContinuationValidUntilSimTimeNs,
    string PayloadSha256);

public sealed record ContinuitySignatureProof(
    string SignatureAlgorithmId,
    string KeyId,
    ushort SignedSchemaMajor,
    ushort SignedSchemaMinor,
    byte[] SignatureRs64);

public sealed record TrustedContinuitySigningKey(
    string KeyId,
    byte[] PublicKeySec1,
    ContinuitySigningKeyState State,
    ulong AuthorityEpoch,
    long ActivatedAtAuthorityMonotonicNs,
    long ExpiresAtAuthorityMonotonicNs);

public sealed record ContinuitySignatureGateState(
    Guid SessionId,
    Guid InstanceId,
    ulong AuthorityEpoch,
    long LastAuthorityMonotonicNs,
    IReadOnlyList<Guid> AcceptedCapsuleIds);

public interface IContinuitySignatureVerifier
{
    public string DeriveKeyId(ReadOnlyMemory<byte> publicKeySec1);

    public bool Verify(
        ContinuitySignedBody body,
        ReadOnlyMemory<byte> signatureRs64,
        ReadOnlyMemory<byte> publicKeySec1);
}

public sealed class ContinuitySignatureGateException : ArgumentException
{
    public ContinuitySignatureGateException(
        string reasonCode,
        string parameterName,
        Exception? innerException = null)
        : base(reasonCode, parameterName, innerException)
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}

public sealed class ContinuitySignatureAdmission
{
    internal ContinuitySignatureAdmission(
        ContinuitySignatureGate owner,
        Guid capsuleId,
        long authorityMonotonicNs,
        int expectedAcceptedCount,
        long expectedAuthorityMonotonicNs)
    {
        Owner = owner;
        CapsuleId = capsuleId;
        AuthorityMonotonicNs = authorityMonotonicNs;
        ExpectedAcceptedCount = expectedAcceptedCount;
        ExpectedAuthorityMonotonicNs = expectedAuthorityMonotonicNs;
    }

    internal ContinuitySignatureGate Owner { get; }

    internal Guid CapsuleId { get; }

    internal long AuthorityMonotonicNs { get; }

    internal int ExpectedAcceptedCount { get; }

    internal long ExpectedAuthorityMonotonicNs { get; }
}

public sealed class ContinuitySignatureGate
{
    public const string SignatureAlgorithmId = "SM2-SM3-RS64@1";
    public const int MaximumTrustedKeyCount = 64;
    public const int MaximumAcceptedCapsuleCount = 65_536;
    public const int MaximumSignedPayloadBytes = 1_048_576;
    public const int MaximumBaseCapsuleBytes = 65_536;
    public const int MaximumDeltaCapsuleBytes = 16_384;
    public const long MaximumOnlineKeyLifetimeNs =
        90L * 24 * 60 * 60 * 1_000_000_000;

    private readonly IContinuitySignatureVerifier _verifier;
    private readonly Dictionary<string, StoredKey> _trustedKeys;
    private readonly HashSet<Guid> _acceptedCapsuleIds;
    private long _lastAuthorityMonotonicNs;

    private ContinuitySignatureGate(
        Guid sessionId,
        Guid instanceId,
        ulong authorityEpoch,
        long lastAuthorityMonotonicNs,
        IReadOnlyList<Guid> acceptedCapsuleIds,
        IEnumerable<TrustedContinuitySigningKey> trustedKeys,
        IContinuitySignatureVerifier verifier,
        string invalidStateReason)
    {
        ArgumentNullException.ThrowIfNull(acceptedCapsuleIds);
        ArgumentNullException.ThrowIfNull(trustedKeys);
        ArgumentNullException.ThrowIfNull(verifier);

        if (sessionId == Guid.Empty ||
            instanceId == Guid.Empty ||
            lastAuthorityMonotonicNs < 0)
        {
            throw Error(invalidStateReason, nameof(sessionId));
        }

        SessionId = sessionId;
        InstanceId = instanceId;
        AuthorityEpoch = authorityEpoch;
        _lastAuthorityMonotonicNs = lastAuthorityMonotonicNs;
        _verifier = verifier;
        _trustedKeys = ValidateAndCopyKeys(
            trustedKeys,
            authorityEpoch,
            verifier,
            invalidStateReason);
        _acceptedCapsuleIds = ValidateAndCopyAcceptedIds(
            acceptedCapsuleIds,
            invalidStateReason);
    }

    public Guid SessionId { get; }

    public Guid InstanceId { get; }

    public ulong AuthorityEpoch { get; }

    public static ContinuitySignatureGate Start(
        Guid sessionId,
        Guid instanceId,
        ulong authorityEpoch,
        long authorityMonotonicNs,
        IEnumerable<TrustedContinuitySigningKey> trustedKeys,
        IContinuitySignatureVerifier verifier) =>
        new(
            sessionId,
            instanceId,
            authorityEpoch,
            authorityMonotonicNs,
            [],
            trustedKeys,
            verifier,
            "ContinuityCrypto.InvalidConfiguration");

    public static ContinuitySignatureGate Restore(
        ContinuitySignatureGateState state,
        IEnumerable<TrustedContinuitySigningKey> trustedKeys,
        IContinuitySignatureVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            return new ContinuitySignatureGate(
                state.SessionId,
                state.InstanceId,
                state.AuthorityEpoch,
                state.LastAuthorityMonotonicNs,
                state.AcceptedCapsuleIds,
                trustedKeys,
                verifier,
                "ContinuityCrypto.InvalidCheckpoint");
        }
        catch (ContinuitySignatureGateException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "ContinuityCrypto.InvalidCheckpoint",
                nameof(state),
                exception);
        }
    }

    public ContinuitySignatureGateState VerifyAndCommit(
        ContinuitySignedBody body,
        ContinuitySignatureProof proof,
        ReadOnlyMemory<byte> payload,
        long currentSimTimeNs,
        long authorityMonotonicNs) =>
        Commit(Verify(
            body,
            proof,
            payload,
            currentSimTimeNs,
            authorityMonotonicNs));

    public ContinuitySignatureAdmission Verify(
        ContinuitySignedBody body,
        ContinuitySignatureProof proof,
        ReadOnlyMemory<byte> payload,
        long currentSimTimeNs,
        long authorityMonotonicNs)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(proof);
        ValidateRequestBounds(
            body,
            proof,
            payload,
            currentSimTimeNs,
            authorityMonotonicNs);

        if (!string.Equals(
                proof.SignatureAlgorithmId,
                SignatureAlgorithmId,
                StringComparison.Ordinal) ||
            proof.SignedSchemaMajor != 1 ||
            proof.SignedSchemaMinor != 0)
        {
            throw Error(
                "ContinuityCrypto.UnsupportedProfile",
                nameof(proof));
        }

        if (!_trustedKeys.TryGetValue(proof.KeyId, out StoredKey? key))
        {
            throw Error("ContinuityCrypto.UnknownKey", nameof(proof));
        }

        ValidateKeyForUse(key, authorityMonotonicNs);
        if (!VerifySignature(body, proof.SignatureRs64, key.PublicKeySec1))
        {
            throw Error(
                "ContinuityCrypto.InvalidSignature",
                nameof(proof));
        }

        if (body.SessionId != SessionId || body.InstanceId != InstanceId)
        {
            throw Error(
                "ContinuityCrypto.IdentityMismatch",
                nameof(body));
        }

        if (body.AuthorityEpoch != AuthorityEpoch)
        {
            throw Error("ContinuityCrypto.EpochMismatch", nameof(body));
        }

        if (_acceptedCapsuleIds.Contains(body.CapsuleId))
        {
            throw Error("ContinuityCrypto.Replay", nameof(body));
        }

        if (_acceptedCapsuleIds.Count == MaximumAcceptedCapsuleCount)
        {
            throw Error(
                "ContinuityCrypto.ReplayCapacityExceeded",
                nameof(body));
        }

        if (currentSimTimeNs > body.ContinuationValidUntilSimTimeNs)
        {
            throw Error("ContinuityCrypto.Expired", nameof(body));
        }

        if (body.ContinuationValidUntilSimTimeNs >
            body.SecretBoundarySimTimeNs)
        {
            throw Error(
                "ContinuityCrypto.SecretBoundaryExceeded",
                nameof(body));
        }

        string actualPayloadSha256 = Convert.ToHexString(
            SHA256.HashData(payload.Span)).ToLowerInvariant();
        if (!string.Equals(
            body.PayloadSha256,
            actualPayloadSha256,
            StringComparison.Ordinal))
        {
            throw Error(
                "ContinuityCrypto.PayloadMismatch",
                nameof(payload));
        }

        return new ContinuitySignatureAdmission(
            this,
            body.CapsuleId,
            authorityMonotonicNs,
            _acceptedCapsuleIds.Count,
            _lastAuthorityMonotonicNs);
    }

    public ContinuitySignatureGateState Commit(
        ContinuitySignatureAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (!ReferenceEquals(admission.Owner, this) ||
            admission.ExpectedAcceptedCount != _acceptedCapsuleIds.Count ||
            admission.ExpectedAuthorityMonotonicNs !=
                _lastAuthorityMonotonicNs ||
            admission.AuthorityMonotonicNs < _lastAuthorityMonotonicNs ||
            _acceptedCapsuleIds.Contains(admission.CapsuleId) ||
            _acceptedCapsuleIds.Count == MaximumAcceptedCapsuleCount)
        {
            throw Error(
                "ContinuityCrypto.StaleAdmission",
                nameof(admission));
        }

        _ = _acceptedCapsuleIds.Add(admission.CapsuleId);
        _lastAuthorityMonotonicNs = admission.AuthorityMonotonicNs;
        return CaptureState();
    }

    public ContinuitySignatureGateState CaptureState() => new(
        SessionId,
        InstanceId,
        AuthorityEpoch,
        _lastAuthorityMonotonicNs,
        _acceptedCapsuleIds
            .OrderBy(static id => id.ToString("D"), StringComparer.Ordinal)
            .ToArray());

    private static Dictionary<string, StoredKey> ValidateAndCopyKeys(
        IEnumerable<TrustedContinuitySigningKey> trustedKeys,
        ulong authorityEpoch,
        IContinuitySignatureVerifier verifier,
        string reasonCode)
    {
        TrustedContinuitySigningKey[] keys = trustedKeys.ToArray();
        if (keys.Length is < 1 or > MaximumTrustedKeyCount)
        {
            throw Error(reasonCode, nameof(trustedKeys));
        }

        Dictionary<string, StoredKey> result =
            new(keys.Length, StringComparer.Ordinal);
        HashSet<ulong> activeEpochs = [];
        bool hasActiveCurrentKey = false;
        foreach (TrustedContinuitySigningKey key in keys)
        {
            ArgumentNullException.ThrowIfNull(key);
            byte[] publicKey = key.PublicKeySec1?.ToArray() ??
                throw Error(reasonCode, nameof(trustedKeys));
            if (!Enum.IsDefined(key.State) ||
                !IsKeyId(key.KeyId) ||
                publicKey.Length != 65 ||
                publicKey[0] != 0x04 ||
                key.ActivatedAtAuthorityMonotonicNs < 0 ||
                key.ExpiresAtAuthorityMonotonicNs <=
                    key.ActivatedAtAuthorityMonotonicNs ||
                (Int128)key.ExpiresAtAuthorityMonotonicNs -
                    key.ActivatedAtAuthorityMonotonicNs >
                    MaximumOnlineKeyLifetimeNs)
            {
                throw Error(reasonCode, nameof(trustedKeys));
            }

            string derivedKeyId;
            try
            {
                derivedKeyId = verifier.DeriveKeyId(publicKey);
            }
            catch (ArgumentException exception)
            {
                throw Error(reasonCode, nameof(trustedKeys), exception);
            }

            if (!string.Equals(key.KeyId, derivedKeyId, StringComparison.Ordinal) ||
                !result.TryAdd(
                    key.KeyId,
                    new StoredKey(
                        publicKey,
                        key.State,
                        key.AuthorityEpoch,
                        key.ActivatedAtAuthorityMonotonicNs,
                        key.ExpiresAtAuthorityMonotonicNs)))
            {
                throw Error(reasonCode, nameof(trustedKeys));
            }

            if (key.State == ContinuitySigningKeyState.Active &&
                !activeEpochs.Add(key.AuthorityEpoch))
            {
                throw Error(reasonCode, nameof(trustedKeys));
            }

            if (key.State == ContinuitySigningKeyState.Active &&
                key.AuthorityEpoch == authorityEpoch)
            {
                hasActiveCurrentKey = true;
            }
        }

        if (!hasActiveCurrentKey)
        {
            throw Error(reasonCode, nameof(trustedKeys));
        }

        return result;
    }

    private static HashSet<Guid> ValidateAndCopyAcceptedIds(
        IReadOnlyList<Guid> acceptedCapsuleIds,
        string reasonCode)
    {
        if (acceptedCapsuleIds.Count > MaximumAcceptedCapsuleCount)
        {
            throw Error(reasonCode, nameof(acceptedCapsuleIds));
        }

        HashSet<Guid> result = [];
        foreach (Guid capsuleId in acceptedCapsuleIds)
        {
            if (capsuleId == Guid.Empty || !result.Add(capsuleId))
            {
                throw Error(reasonCode, nameof(acceptedCapsuleIds));
            }
        }

        return result;
    }

    private static void ValidateRequestBounds(
        ContinuitySignedBody body,
        ContinuitySignatureProof proof,
        ReadOnlyMemory<byte> payload,
        long currentSimTimeNs,
        long authorityMonotonicNs)
    {
        if (!IsCapsuleType(body.CapsuleType) ||
            body.CapsuleId == Guid.Empty ||
            body.SessionId == Guid.Empty ||
            body.InstanceId == Guid.Empty ||
            body.SecretBoundarySimTimeNs < 0 ||
            body.ContinuationValidUntilSimTimeNs < 0 ||
            !IsSha256(body.PayloadSha256) ||
            !IsKeyId(proof.KeyId) ||
            proof.SignatureRs64 is not { Length: 64 } ||
            payload.Length < 1 ||
            payload.Length > MaximumPayloadBytesFor(body.CapsuleType) ||
            currentSimTimeNs < 0 ||
            authorityMonotonicNs < 0)
        {
            throw Error("ContinuityCrypto.InvalidRequest", nameof(body));
        }
    }

    private void ValidateKeyForUse(
        StoredKey key,
        long authorityMonotonicNs)
    {
        if (key.State != ContinuitySigningKeyState.Active)
        {
            throw Error("ContinuityCrypto.KeyInactive", nameof(key));
        }

        if (key.AuthorityEpoch != AuthorityEpoch)
        {
            throw Error(
                "ContinuityCrypto.KeyEpochMismatch",
                nameof(key));
        }

        if (authorityMonotonicNs < _lastAuthorityMonotonicNs)
        {
            throw Error(
                "ContinuityCrypto.AuthorityTimeReversed",
                nameof(authorityMonotonicNs));
        }

        if (authorityMonotonicNs < key.ActivatedAtAuthorityMonotonicNs ||
            authorityMonotonicNs >= key.ExpiresAtAuthorityMonotonicNs)
        {
            throw Error(
                "ContinuityCrypto.KeyNotCurrent",
                nameof(authorityMonotonicNs));
        }
    }

    private bool VerifySignature(
        ContinuitySignedBody body,
        ReadOnlyMemory<byte> signature,
        ReadOnlyMemory<byte> publicKey)
    {
        try
        {
            return _verifier.Verify(body, signature, publicKey);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsCapsuleType(string? value) => value is
        "ContinuityCapsuleBase" or
        "ContinuityCapsuleDelta" or
        "OfflineCapabilityLease" or
        "QuestionCompletionPermit";

    private static int MaximumPayloadBytesFor(string capsuleType) =>
        capsuleType switch
        {
            "ContinuityCapsuleBase" => MaximumBaseCapsuleBytes,
            "ContinuityCapsuleDelta" => MaximumDeltaCapsuleBytes,
            _ => MaximumSignedPayloadBytes,
        };

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsKeyId(string? value) =>
        value is { Length: 72 } &&
        value.StartsWith("sm2-sm3:", StringComparison.Ordinal) &&
        value[8..].All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ContinuitySignatureGateException Error(
        string reasonCode,
        string parameterName,
        Exception? innerException = null) =>
        new(reasonCode, parameterName, innerException);

    private sealed record StoredKey(
        byte[] PublicKeySec1,
        ContinuitySigningKeyState State,
        ulong AuthorityEpoch,
        long ActivatedAtAuthorityMonotonicNs,
        long ExpiresAtAuthorityMonotonicNs);
}
