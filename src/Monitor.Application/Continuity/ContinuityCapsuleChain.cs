// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Continuity;

public enum ContinuityPermissionState
{
    Allowed = 0,
    DisallowedByCourse = 1,
    DisallowedByMode = 2,
    Ineligible = 3,
}

public sealed record ContinuityVersionedReference(
    string Id,
    string Version,
    string ContentSha256);

public sealed record ContinuityStateComponent(
    string ComponentId,
    ContinuityVersionedReference StateSchemaReference,
    string StateSha256,
    byte[] StateBytes);

public sealed record ContinuityChannelCursor(
    string ChannelId,
    ulong NextSampleIndex,
    ulong NextBlockSequence,
    ulong NextNumericSequence);

public sealed record ContinuityProjectionRevisions(
    ulong DisplayRole,
    ulong AlarmEpisode,
    ulong AlarmAudioPolicy,
    ulong Therapy);

public sealed record ContinuityCapsuleBase(
    Guid CapsuleId,
    Guid SessionId,
    Guid InstanceId,
    string BranchId,
    ulong AuthorityEpoch,
    ulong TimebaseEpoch,
    ulong StreamEpoch,
    ulong CheckpointSequence,
    ulong CommitSequence,
    long SimTimeNs,
    ulong NextEventSequence,
    IReadOnlyList<ContinuityChannelCursor> ChannelCursors,
    IReadOnlyList<ContinuityStateComponent> StateComponents,
    ContinuityProjectionRevisions ProjectionRevisions,
    ContinuityVersionedReference EngineReference,
    ContinuityVersionedReference MathProfileReference,
    IReadOnlyList<ContinuityVersionedReference> ResourcePackReferences,
    ContinuityPermissionState ContinuationPermission,
    long SecretBoundarySimTimeNs,
    long ContinuationValidUntilSimTimeNs,
    string CapsuleSha256,
    ContinuitySignatureProof Signature,
    string? EncryptionEnvelopeReference);

public sealed record ContinuityCapsuleDelta(
    Guid DeltaId,
    string BaseSha256,
    string? PreviousDeltaSha256,
    ulong CheckpointSequence,
    ulong CommitSequence,
    long SimTimeNs,
    IReadOnlyList<ContinuityStateComponent> ChangedComponents,
    long SecretBoundarySimTimeNs,
    long ContinuationValidUntilSimTimeNs,
    string DeltaSha256,
    ContinuitySignatureProof Signature);

public sealed record ContinuityCapsuleFrontier(
    Guid BaseCapsuleId,
    string BaseSha256,
    Guid? LastDeltaId,
    string? LastDeltaSha256,
    ulong CheckpointSequence,
    ulong CommitSequence,
    long SimTimeNs,
    long SecretBoundarySimTimeNs,
    long ContinuationValidUntilSimTimeNs);

public sealed record ContinuityCapsuleChainState(
    ContinuitySignatureGateState SignatureGate,
    ContinuityCapsuleFrontier? Frontier);

public sealed record AcceptedContinuityCapsuleBase(
    ContinuityCapsuleBase Capsule,
    ContinuityCapsuleChainState State);

public sealed record AcceptedContinuityCapsuleDelta(
    ContinuityCapsuleDelta Delta,
    ContinuityCapsuleChainState State);

public interface IContinuityCapsulePayloadDecoder
{
    public ContinuityCapsuleBase DecodeBase(ReadOnlyMemory<byte> signedPayload);

    public ContinuityCapsuleDelta DecodeDelta(ReadOnlyMemory<byte> signedPayload);
}

public sealed class ContinuityCapsuleChainException : ArgumentException
{
    public ContinuityCapsuleChainException(
        string reasonCode,
        string parameterName,
        Exception? innerException = null)
        : base(reasonCode, parameterName, innerException)
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}

public sealed class ContinuityCapsuleChain
{
    public const int MaximumChannelCursorCount = 128;
    public const int MaximumStateComponentCount = 256;
    public const int MaximumResourcePackCount = 32;

    private readonly ContinuitySignatureGate _signatureGate;
    private readonly IContinuityCapsulePayloadDecoder _payloadDecoder;
    private ContinuityCapsuleFrontier? _frontier;

    private ContinuityCapsuleChain(
        ContinuitySignatureGate signatureGate,
        ContinuityCapsuleFrontier? frontier,
        IContinuityCapsulePayloadDecoder payloadDecoder)
    {
        ArgumentNullException.ThrowIfNull(signatureGate);
        ArgumentNullException.ThrowIfNull(payloadDecoder);
        ValidateFrontier(frontier, signatureGate.CaptureState());
        _signatureGate = signatureGate;
        _payloadDecoder = payloadDecoder;
        _frontier = frontier;
    }

    public ContinuityCapsuleFrontier? Frontier => _frontier;

    public static ContinuityCapsuleChain Start(
        Guid sessionId,
        Guid instanceId,
        ulong authorityEpoch,
        long authorityMonotonicNs,
        IEnumerable<TrustedContinuitySigningKey> trustedKeys,
        IContinuitySignatureVerifier verifier,
        IContinuityCapsulePayloadDecoder payloadDecoder) =>
        new(
            ContinuitySignatureGate.Start(
                sessionId,
                instanceId,
                authorityEpoch,
                authorityMonotonicNs,
                trustedKeys,
                verifier),
            frontier: null,
            payloadDecoder);

    public static ContinuityCapsuleChain Restore(
        ContinuityCapsuleChainState state,
        IEnumerable<TrustedContinuitySigningKey> trustedKeys,
        IContinuitySignatureVerifier verifier,
        IContinuityCapsulePayloadDecoder payloadDecoder)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            return new ContinuityCapsuleChain(
                ContinuitySignatureGate.Restore(
                    state.SignatureGate,
                    trustedKeys,
                    verifier),
                state.Frontier,
                payloadDecoder);
        }
        catch (ContinuityCapsuleChainException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "ContinuityChain.InvalidCheckpoint",
                nameof(state),
                exception);
        }
    }

    public AcceptedContinuityCapsuleBase AcceptBase(
        ContinuitySignedBody signedBody,
        ReadOnlyMemory<byte> signedPayload,
        long currentSimTimeNs,
        long authorityMonotonicNs)
    {
        ArgumentNullException.ThrowIfNull(signedBody);
        ContinuityCapsuleBase decoded = DecodeBase(signedPayload);
        ValidateBase(decoded);
        ContinuityCapsuleBase capsule = CopyBase(decoded);
        ContinuitySignatureAdmission admission = _signatureGate.Verify(
            signedBody,
            capsule.Signature,
            signedPayload,
            currentSimTimeNs,
            authorityMonotonicNs);
        ValidateBaseBinding(capsule, signedBody);
        if (capsule.ContinuationPermission != ContinuityPermissionState.Allowed)
        {
            throw Error(
                "ContinuityChain.PermissionDenied",
                nameof(capsule));
        }

        if (_frontier is not null &&
            (capsule.CheckpointSequence < _frontier.CheckpointSequence ||
                capsule.CommitSequence < _frontier.CommitSequence ||
                capsule.SimTimeNs < _frontier.SimTimeNs))
        {
            throw Error("ContinuityChain.FrontierRegression", nameof(capsule));
        }

        ContinuityCapsuleFrontier next = new(
            capsule.CapsuleId,
            capsule.CapsuleSha256,
            LastDeltaId: null,
            LastDeltaSha256: null,
            capsule.CheckpointSequence,
            capsule.CommitSequence,
            capsule.SimTimeNs,
            capsule.SecretBoundarySimTimeNs,
            capsule.ContinuationValidUntilSimTimeNs);
        _ = _signatureGate.Commit(admission);
        _frontier = next;
        return new AcceptedContinuityCapsuleBase(capsule, CaptureState());
    }

    public AcceptedContinuityCapsuleDelta AcceptDelta(
        ContinuitySignedBody signedBody,
        ReadOnlyMemory<byte> signedPayload,
        long currentSimTimeNs,
        long authorityMonotonicNs)
    {
        ArgumentNullException.ThrowIfNull(signedBody);
        ContinuityCapsuleDelta decoded = DecodeDelta(signedPayload);
        ValidateDelta(decoded);
        ContinuityCapsuleDelta delta = CopyDelta(decoded);
        ContinuitySignatureAdmission admission = _signatureGate.Verify(
            signedBody,
            delta.Signature,
            signedPayload,
            currentSimTimeNs,
            authorityMonotonicNs);
        ValidateDeltaBinding(delta, signedBody);
        ContinuityCapsuleFrontier current = _frontier ??
            throw Error("ContinuityChain.BaseRequired", nameof(delta));
        if (!string.Equals(
                delta.BaseSha256,
                current.BaseSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                delta.PreviousDeltaSha256,
                current.LastDeltaSha256,
                StringComparison.Ordinal))
        {
            throw Error("ContinuityChain.HashChainMismatch", nameof(delta));
        }

        if (delta.CheckpointSequence <= current.CheckpointSequence ||
            delta.CommitSequence < current.CommitSequence ||
            delta.SimTimeNs < current.SimTimeNs)
        {
            throw Error("ContinuityChain.FrontierRegression", nameof(delta));
        }

        ContinuityCapsuleFrontier next = current with
        {
            LastDeltaId = delta.DeltaId,
            LastDeltaSha256 = delta.DeltaSha256,
            CheckpointSequence = delta.CheckpointSequence,
            CommitSequence = delta.CommitSequence,
            SimTimeNs = delta.SimTimeNs,
            SecretBoundarySimTimeNs = delta.SecretBoundarySimTimeNs,
            ContinuationValidUntilSimTimeNs =
                delta.ContinuationValidUntilSimTimeNs,
        };
        _ = _signatureGate.Commit(admission);
        _frontier = next;
        return new AcceptedContinuityCapsuleDelta(delta, CaptureState());
    }

    public ContinuityCapsuleChainState CaptureState() => new(
        _signatureGate.CaptureState(),
        _frontier);

    private ContinuityCapsuleBase DecodeBase(ReadOnlyMemory<byte> signedPayload)
    {
        if (signedPayload.Length is < 1 or >
            ContinuitySignatureGate.MaximumBaseCapsuleBytes)
        {
            throw InvalidPayload(nameof(signedPayload));
        }

        try
        {
            return _payloadDecoder.DecodeBase(signedPayload) ??
                throw InvalidPayload(nameof(signedPayload));
        }
        catch (ContinuityCapsuleChainException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "ContinuityChain.InvalidPayload",
                nameof(signedPayload),
                exception);
        }
    }

    private ContinuityCapsuleDelta DecodeDelta(ReadOnlyMemory<byte> signedPayload)
    {
        if (signedPayload.Length is < 1 or >
            ContinuitySignatureGate.MaximumDeltaCapsuleBytes)
        {
            throw InvalidPayload(nameof(signedPayload));
        }

        try
        {
            return _payloadDecoder.DecodeDelta(signedPayload) ??
                throw InvalidPayload(nameof(signedPayload));
        }
        catch (ContinuityCapsuleChainException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "ContinuityChain.InvalidPayload",
                nameof(signedPayload),
                exception);
        }
    }

    private static ContinuityCapsuleBase CopyBase(
        ContinuityCapsuleBase capsule) => capsule with
        {
            ChannelCursors = capsule.ChannelCursors
                .Select(static cursor => cursor with { })
                .ToArray(),
            StateComponents = capsule.StateComponents
                .Select(CopyComponent)
                .ToArray(),
            ProjectionRevisions = capsule.ProjectionRevisions with { },
            EngineReference = capsule.EngineReference with { },
            MathProfileReference = capsule.MathProfileReference with { },
            ResourcePackReferences = capsule.ResourcePackReferences
                .Select(static reference => reference with { })
                .ToArray(),
            Signature = CopySignature(capsule.Signature),
        };

    private static ContinuityCapsuleDelta CopyDelta(
        ContinuityCapsuleDelta delta) => delta with
        {
            ChangedComponents = delta.ChangedComponents
                .Select(CopyComponent)
                .ToArray(),
            Signature = CopySignature(delta.Signature),
        };

    private static ContinuityStateComponent CopyComponent(
        ContinuityStateComponent component) => component with
        {
            StateSchemaReference = component.StateSchemaReference with { },
            StateBytes = component.StateBytes.ToArray(),
        };

    private static ContinuitySignatureProof CopySignature(
        ContinuitySignatureProof signature) => signature with
        {
            SignatureRs64 = signature.SignatureRs64.ToArray(),
        };

    private static void ValidateBase(ContinuityCapsuleBase capsule)
    {
        if (capsule.CapsuleId == Guid.Empty ||
            capsule.SessionId == Guid.Empty ||
            capsule.InstanceId == Guid.Empty ||
            !IsStableId(capsule.BranchId) ||
            capsule.SimTimeNs < 0 ||
            capsule.SecretBoundarySimTimeNs < 0 ||
            capsule.ContinuationValidUntilSimTimeNs < capsule.SimTimeNs ||
            capsule.ContinuationValidUntilSimTimeNs >
                capsule.SecretBoundarySimTimeNs ||
            !Enum.IsDefined(capsule.ContinuationPermission) ||
            !IsSha256(capsule.CapsuleSha256) ||
            (capsule.EncryptionEnvelopeReference is not null &&
                !IsStableId(capsule.EncryptionEnvelopeReference)) ||
            capsule.ProjectionRevisions is null ||
            !IsVersionedReference(capsule.EngineReference) ||
            !IsVersionedReference(capsule.MathProfileReference) ||
            capsule.Signature?.SignatureRs64 is null)
        {
            throw InvalidPayload(nameof(capsule));
        }

        ValidateChannelCursors(capsule.ChannelCursors);
        ValidateStateComponents(capsule.StateComponents, allowEmpty: false);
        ValidateReferences(capsule.ResourcePackReferences);
    }

    private static void ValidateDelta(ContinuityCapsuleDelta delta)
    {
        if (delta.DeltaId == Guid.Empty ||
            !IsSha256(delta.BaseSha256) ||
            (delta.PreviousDeltaSha256 is not null &&
                !IsSha256(delta.PreviousDeltaSha256)) ||
            delta.SimTimeNs < 0 ||
            delta.SecretBoundarySimTimeNs < 0 ||
            delta.ContinuationValidUntilSimTimeNs < delta.SimTimeNs ||
            delta.ContinuationValidUntilSimTimeNs >
                delta.SecretBoundarySimTimeNs ||
            !IsSha256(delta.DeltaSha256) ||
            delta.Signature?.SignatureRs64 is null)
        {
            throw InvalidPayload(nameof(delta));
        }

        ValidateStateComponents(delta.ChangedComponents, allowEmpty: false);
    }

    private static void ValidateBaseBinding(
        ContinuityCapsuleBase capsule,
        ContinuitySignedBody body)
    {
        if (body.CapsuleType != "ContinuityCapsuleBase" ||
            body.CapsuleId != capsule.CapsuleId ||
            body.SessionId != capsule.SessionId ||
            body.InstanceId != capsule.InstanceId ||
            body.AuthorityEpoch != capsule.AuthorityEpoch ||
            body.SecretBoundarySimTimeNs !=
                capsule.SecretBoundarySimTimeNs ||
            body.ContinuationValidUntilSimTimeNs !=
                capsule.ContinuationValidUntilSimTimeNs)
        {
            throw Error("ContinuityChain.SignedBodyMismatch", nameof(body));
        }
    }

    private static void ValidateDeltaBinding(
        ContinuityCapsuleDelta delta,
        ContinuitySignedBody body)
    {
        if (body.CapsuleType != "ContinuityCapsuleDelta" ||
            body.CapsuleId != delta.DeltaId ||
            body.SecretBoundarySimTimeNs != delta.SecretBoundarySimTimeNs ||
            body.ContinuationValidUntilSimTimeNs !=
                delta.ContinuationValidUntilSimTimeNs)
        {
            throw Error("ContinuityChain.SignedBodyMismatch", nameof(body));
        }
    }

    private static void ValidateChannelCursors(
        IReadOnlyList<ContinuityChannelCursor> cursors)
    {
        if (cursors is null || cursors.Count > MaximumChannelCursorCount)
        {
            throw InvalidPayload(nameof(cursors));
        }

        string? previous = null;
        foreach (ContinuityChannelCursor cursor in cursors)
        {
            if (cursor is null ||
                !IsStableId(cursor.ChannelId) ||
                (previous is not null &&
                    StringComparer.Ordinal.Compare(
                        previous,
                        cursor.ChannelId) >= 0))
            {
                throw InvalidPayload(nameof(cursors));
            }

            previous = cursor.ChannelId;
        }
    }

    private static void ValidateStateComponents(
        IReadOnlyList<ContinuityStateComponent> components,
        bool allowEmpty)
    {
        if (components is null ||
            components.Count > MaximumStateComponentCount ||
            (!allowEmpty && components.Count == 0))
        {
            throw InvalidPayload(nameof(components));
        }

        string? previous = null;
        foreach (ContinuityStateComponent component in components)
        {
            if (component is null ||
                !IsStableId(component.ComponentId) ||
                !IsVersionedReference(component.StateSchemaReference) ||
                !IsSha256(component.StateSha256) ||
                component.StateBytes is null ||
                (previous is not null &&
                    StringComparer.Ordinal.Compare(
                        previous,
                        component.ComponentId) >= 0))
            {
                throw InvalidPayload(nameof(components));
            }

            previous = component.ComponentId;
        }
    }

    private static void ValidateReferences(
        IReadOnlyList<ContinuityVersionedReference> references)
    {
        if (references is null || references.Count > MaximumResourcePackCount)
        {
            throw InvalidPayload(nameof(references));
        }

        string? previous = null;
        foreach (ContinuityVersionedReference reference in references)
        {
            if (!IsVersionedReference(reference))
            {
                throw InvalidPayload(nameof(references));
            }

            string identity = string.Concat(
                reference.Id,
                "\0",
                reference.Version,
                "\0",
                reference.ContentSha256);
            if (previous is not null &&
                StringComparer.Ordinal.Compare(previous, identity) >= 0)
            {
                throw InvalidPayload(nameof(references));
            }

            previous = identity;
        }
    }

    private static void ValidateFrontier(
        ContinuityCapsuleFrontier? frontier,
        ContinuitySignatureGateState signatureState)
    {
        if (frontier is null)
        {
            if (signatureState.AcceptedCapsuleIds.Count != 0)
            {
                throw Error(
                    "ContinuityChain.InvalidCheckpoint",
                    nameof(frontier));
            }

            return;
        }

        bool deltaPairPresent = frontier.LastDeltaId.HasValue &&
            frontier.LastDeltaSha256 is not null;
        bool deltaPairAbsent = !frontier.LastDeltaId.HasValue &&
            frontier.LastDeltaSha256 is null;
        if (frontier.BaseCapsuleId == Guid.Empty ||
            !IsSha256(frontier.BaseSha256) ||
            (!deltaPairPresent && !deltaPairAbsent) ||
            (frontier.LastDeltaId.HasValue &&
                (frontier.LastDeltaId.Value == Guid.Empty ||
                    frontier.LastDeltaId.Value == frontier.BaseCapsuleId)) ||
            (frontier.LastDeltaSha256 is not null &&
                !IsSha256(frontier.LastDeltaSha256)) ||
            frontier.SimTimeNs < 0 ||
            frontier.SecretBoundarySimTimeNs < 0 ||
            frontier.ContinuationValidUntilSimTimeNs < frontier.SimTimeNs ||
            frontier.ContinuationValidUntilSimTimeNs >
                frontier.SecretBoundarySimTimeNs ||
            !signatureState.AcceptedCapsuleIds.Contains(
                frontier.BaseCapsuleId) ||
            (frontier.LastDeltaId.HasValue &&
                signatureState.AcceptedCapsuleIds.Count < 2) ||
            (frontier.LastDeltaId.HasValue &&
                !signatureState.AcceptedCapsuleIds.Contains(
                    frontier.LastDeltaId.Value)))
        {
            throw Error(
                "ContinuityChain.InvalidCheckpoint",
                nameof(frontier));
        }
    }

    private static bool IsVersionedReference(
        ContinuityVersionedReference? reference) =>
        reference is not null &&
        IsStableId(reference.Id) &&
        IsSemanticVersion(reference.Version) &&
        IsSha256(reference.ContentSha256);

    private static bool IsSemanticVersion(string? value)
    {
        if (value is not { Length: >= 5 and <= 64 })
        {
            return false;
        }

        int suffixIndex = value.IndexOf('-', StringComparison.Ordinal);
        ReadOnlySpan<char> core = suffixIndex < 0
            ? value.AsSpan()
            : value.AsSpan(0, suffixIndex);
        if (suffixIndex == value.Length - 1 ||
            (suffixIndex >= 0 &&
                !IsSemanticVersionSuffix(value.AsSpan(suffixIndex + 1))))
        {
            return false;
        }

        int segmentCount = 0;
        foreach (Range range in core.Split('.'))
        {
            ReadOnlySpan<char> segment = core[range];
            if (segment.IsEmpty || !IsAsciiDigits(segment))
            {
                return false;
            }

            segmentCount++;
        }

        return segmentCount == 3;
    }

    private static bool IsStableId(string? value)
    {
        if (value is not { Length: >= 1 and <= 128 } ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (!IsAsciiLetter(character) &&
                character is not (>= '0' and <= '9') &&
                character is not ('.' or '_' or ':' or '@' or '/' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSemanticVersionSuffix(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (!IsAsciiLetter(character) &&
                character is not (>= '0' and <= '9') &&
                character is not ('.' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ContinuityCapsuleChainException InvalidPayload(
        string parameterName) =>
        Error("ContinuityChain.InvalidPayload", parameterName);

    private static ContinuityCapsuleChainException Error(
        string reasonCode,
        string parameterName,
        Exception? innerException = null) =>
        new(reasonCode, parameterName, innerException);
}
