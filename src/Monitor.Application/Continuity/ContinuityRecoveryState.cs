// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;

namespace Monitor.Application.Continuity;

public sealed record ContinuityStateComponentCapability(
    string ComponentId,
    ContinuityVersionedReference StateSchemaReference);

public sealed record ContinuityRuntimeCapabilities(
    ContinuityVersionedReference EngineReference,
    ContinuityVersionedReference MathProfileReference,
    IReadOnlyList<ContinuityVersionedReference> ResourcePackReferences,
    IReadOnlyList<string> ChannelIds,
    IReadOnlyList<ContinuityStateComponentCapability> StateComponents);

public sealed record ContinuityRecoveryStateImage(
    Guid BaseCapsuleId,
    string BaseSha256,
    Guid? LastDeltaId,
    string? LastDeltaSha256,
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
    long SecretBoundarySimTimeNs,
    long ContinuationValidUntilSimTimeNs);

public sealed partial class ContinuityCapsuleChain
{
    private static ContinuityRuntimeCapabilities ValidateAndCopyCapabilities(
        ContinuityRuntimeCapabilities capabilities)
    {
        if (capabilities is null ||
            !IsVersionedReference(capabilities.EngineReference) ||
            !IsVersionedReference(capabilities.MathProfileReference))
        {
            throw Error(
                "ContinuityChain.InvalidConfiguration",
                nameof(capabilities));
        }

        ValidateCapabilityReferences(capabilities.ResourcePackReferences);
        ValidateCapabilityChannels(capabilities.ChannelIds);
        ValidateCapabilityComponents(capabilities.StateComponents);
        return new ContinuityRuntimeCapabilities(
            capabilities.EngineReference with { },
            capabilities.MathProfileReference with { },
            capabilities.ResourcePackReferences
                .Select(static reference => reference with { })
                .ToArray(),
            capabilities.ChannelIds.ToArray(),
            capabilities.StateComponents
                .Select(static component => component with
                {
                    StateSchemaReference =
                        component.StateSchemaReference with { },
                })
                .ToArray());
    }

    private static ContinuityRecoveryStateImage BuildRecoveryStateImage(
        ContinuityCapsuleBase capsule,
        ContinuityCapsuleFrontier frontier,
        ContinuityRuntimeCapabilities capabilities)
    {
        ValidateCompatibility(capsule, capabilities);
        ValidateComponentHashes(capsule.StateComponents);
        return new ContinuityRecoveryStateImage(
            frontier.BaseCapsuleId,
            frontier.BaseSha256,
            frontier.LastDeltaId,
            frontier.LastDeltaSha256,
            capsule.SessionId,
            capsule.InstanceId,
            capsule.BranchId,
            capsule.AuthorityEpoch,
            capsule.TimebaseEpoch,
            capsule.StreamEpoch,
            frontier.CheckpointSequence,
            frontier.CommitSequence,
            frontier.SimTimeNs,
            capsule.NextEventSequence,
            capsule.ChannelCursors
                .Select(static cursor => cursor with { })
                .ToArray(),
            capsule.StateComponents.Select(CopyComponent).ToArray(),
            capsule.ProjectionRevisions with { },
            capsule.EngineReference with { },
            capsule.MathProfileReference with { },
            capsule.ResourcePackReferences
                .Select(static reference => reference with { })
                .ToArray(),
            frontier.SecretBoundarySimTimeNs,
            frontier.ContinuationValidUntilSimTimeNs);
    }

    private static ContinuityRecoveryStateImage ApplyDeltaToRecoveryState(
        ContinuityRecoveryStateImage current,
        ContinuityCapsuleDelta delta,
        ContinuityCapsuleFrontier frontier,
        ContinuityRuntimeCapabilities capabilities)
    {
        ValidateComponentHashes(delta.ChangedComponents);
        var replacements =
            delta.ChangedComponents.ToDictionary(
                static component => component.ComponentId,
                StringComparer.Ordinal);
        var components = new ContinuityStateComponent[
            current.StateComponents.Count];
        for (int index = 0; index < components.Length; index++)
        {
            ContinuityStateComponent existing = current.StateComponents[index];
            if (replacements.Remove(
                    existing.ComponentId,
                    out ContinuityStateComponent? changed))
            {
                ContinuityStateComponentCapability expected =
                    capabilities.StateComponents[index];
                if (!ReferenceEqualsValue(
                        changed.StateSchemaReference,
                        expected.StateSchemaReference))
                {
                    throw Error(
                        "ContinuityChain.StateSchemaMismatch",
                        nameof(delta));
                }

                components[index] = CopyComponent(changed);
            }
            else
            {
                components[index] = CopyComponent(existing);
            }
        }

        if (replacements.Count != 0)
        {
            throw Error(
                "ContinuityChain.UnknownStateComponent",
                nameof(delta));
        }

        return current with
        {
            LastDeltaId = frontier.LastDeltaId,
            LastDeltaSha256 = frontier.LastDeltaSha256,
            CheckpointSequence = frontier.CheckpointSequence,
            CommitSequence = frontier.CommitSequence,
            SimTimeNs = frontier.SimTimeNs,
            StateComponents = components,
            SecretBoundarySimTimeNs = frontier.SecretBoundarySimTimeNs,
            ContinuationValidUntilSimTimeNs =
                frontier.ContinuationValidUntilSimTimeNs,
        };
    }

    private static ContinuityRecoveryStateImage?
        ValidateAndCopyRecoveryStateImage(
            ContinuityRecoveryStateImage? image,
            ContinuityCapsuleFrontier? frontier,
            ContinuitySignatureGateState signatureState,
            ContinuityRuntimeCapabilities capabilities)
    {
        if (image is null || frontier is null)
        {
            if (image is not null || frontier is not null)
            {
                throw Error(
                    "ContinuityChain.InvalidCheckpoint",
                    nameof(image));
            }

            return null;
        }

        try
        {
            ValidateChannelCursors(image.ChannelCursors);
            ValidateStateComponents(image.StateComponents, allowEmpty: false);
            ValidateReferences(image.ResourcePackReferences);
            ValidateComponentHashes(image.StateComponents);
        }
        catch (ContinuityCapsuleChainException exception)
        {
            throw Error(
                "ContinuityChain.InvalidCheckpoint",
                nameof(image),
                exception);
        }

        if (image.BaseCapsuleId != frontier.BaseCapsuleId ||
            image.BaseSha256 != frontier.BaseSha256 ||
            image.LastDeltaId != frontier.LastDeltaId ||
            image.LastDeltaSha256 != frontier.LastDeltaSha256 ||
            image.SessionId != signatureState.SessionId ||
            image.InstanceId != signatureState.InstanceId ||
            image.AuthorityEpoch != signatureState.AuthorityEpoch ||
            image.CheckpointSequence != frontier.CheckpointSequence ||
            image.CommitSequence != frontier.CommitSequence ||
            image.SimTimeNs != frontier.SimTimeNs ||
            image.SecretBoundarySimTimeNs !=
                frontier.SecretBoundarySimTimeNs ||
            image.ContinuationValidUntilSimTimeNs !=
                frontier.ContinuationValidUntilSimTimeNs ||
            !IsStableId(image.BranchId) ||
            image.SimTimeNs < 0 ||
            image.ProjectionRevisions is null ||
            !ReferenceEqualsValue(
                image.EngineReference,
                capabilities.EngineReference) ||
            !ReferenceEqualsValue(
                image.MathProfileReference,
                capabilities.MathProfileReference) ||
            !ReferencesEqual(
                image.ResourcePackReferences,
                capabilities.ResourcePackReferences) ||
            !ChannelIdsEqual(image.ChannelCursors, capabilities.ChannelIds) ||
            !ComponentsMatchCapabilities(
                image.StateComponents,
                capabilities.StateComponents))
        {
            throw Error(
                "ContinuityChain.InvalidCheckpoint",
                nameof(image));
        }

        return CopyRecoveryStateImage(image);
    }

    private static void ValidateCompatibility(
        ContinuityCapsuleBase capsule,
        ContinuityRuntimeCapabilities capabilities)
    {
        if (!ReferenceEqualsValue(
                capsule.EngineReference,
                capabilities.EngineReference))
        {
            throw Error(
                "ContinuityChain.EngineMismatch",
                nameof(capsule));
        }

        if (!ReferenceEqualsValue(
                capsule.MathProfileReference,
                capabilities.MathProfileReference))
        {
            throw Error(
                "ContinuityChain.MathProfileMismatch",
                nameof(capsule));
        }

        if (!ReferencesEqual(
                capsule.ResourcePackReferences,
                capabilities.ResourcePackReferences))
        {
            throw Error(
                "ContinuityChain.ResourcePackMismatch",
                nameof(capsule));
        }

        if (!ChannelIdsEqual(capsule.ChannelCursors, capabilities.ChannelIds))
        {
            throw Error(
                "ContinuityChain.ChannelSetMismatch",
                nameof(capsule));
        }

        if (!ComponentsMatchCapabilities(
                capsule.StateComponents,
                capabilities.StateComponents))
        {
            throw Error(
                "ContinuityChain.StateComponentSetMismatch",
                nameof(capsule));
        }
    }

    private static void ValidateComponentHashes(
        IReadOnlyList<ContinuityStateComponent> components)
    {
        foreach (ContinuityStateComponent component in components)
        {
            byte[] expected = Convert.FromHexString(component.StateSha256);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(component.StateBytes),
                    expected))
            {
                throw Error(
                    "ContinuityChain.StateHashMismatch",
                    nameof(components));
            }
        }
    }

    private static bool ComponentsMatchCapabilities(
        IReadOnlyList<ContinuityStateComponent> components,
        IReadOnlyList<ContinuityStateComponentCapability> capabilities)
    {
        if (components.Count != capabilities.Count)
        {
            return false;
        }

        for (int index = 0; index < components.Count; index++)
        {
            if (components[index].ComponentId != capabilities[index].ComponentId ||
                !ReferenceEqualsValue(
                    components[index].StateSchemaReference,
                    capabilities[index].StateSchemaReference))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ChannelIdsEqual(
        IReadOnlyList<ContinuityChannelCursor> cursors,
        IReadOnlyList<string> channelIds) =>
        cursors.Count == channelIds.Count &&
        cursors.Select(static cursor => cursor.ChannelId)
            .SequenceEqual(channelIds, StringComparer.Ordinal);

    private static bool ReferencesEqual(
        IReadOnlyList<ContinuityVersionedReference> left,
        IReadOnlyList<ContinuityVersionedReference> right) =>
        left.Count == right.Count &&
        left.Zip(right).All(static pair =>
            ReferenceEqualsValue(pair.First, pair.Second));

    private static bool ReferenceEqualsValue(
        ContinuityVersionedReference? left,
        ContinuityVersionedReference? right) =>
        left is not null &&
        right is not null &&
        left.Id == right.Id &&
        left.Version == right.Version &&
        left.ContentSha256 == right.ContentSha256;

    private static ContinuityRecoveryStateImage CopyRecoveryStateImage(
        ContinuityRecoveryStateImage image) => image with
        {
            ChannelCursors = image.ChannelCursors
                .Select(static cursor => cursor with { })
                .ToArray(),
            StateComponents = image.StateComponents
                .Select(CopyComponent)
                .ToArray(),
            ProjectionRevisions = image.ProjectionRevisions with { },
            EngineReference = image.EngineReference with { },
            MathProfileReference = image.MathProfileReference with { },
            ResourcePackReferences = image.ResourcePackReferences
                .Select(static reference => reference with { })
                .ToArray(),
        };

    private static void ValidateCapabilityReferences(
        IReadOnlyList<ContinuityVersionedReference> references)
    {
        try
        {
            ValidateReferences(references);
        }
        catch (ContinuityCapsuleChainException exception)
        {
            throw Error(
                "ContinuityChain.InvalidConfiguration",
                nameof(references),
                exception);
        }
    }

    private static void ValidateCapabilityChannels(IReadOnlyList<string> ids)
    {
        if (ids is null || ids.Count > MaximumChannelCursorCount)
        {
            throw Error("ContinuityChain.InvalidConfiguration", nameof(ids));
        }

        string? previous = null;
        foreach (string id in ids)
        {
            if (!IsStableId(id) ||
                (previous is not null &&
                    StringComparer.Ordinal.Compare(previous, id) >= 0))
            {
                throw Error(
                    "ContinuityChain.InvalidConfiguration",
                    nameof(ids));
            }

            previous = id;
        }
    }

    private static void ValidateCapabilityComponents(
        IReadOnlyList<ContinuityStateComponentCapability> components)
    {
        if (components is null ||
            components.Count is < 1 or > MaximumStateComponentCount)
        {
            throw Error(
                "ContinuityChain.InvalidConfiguration",
                nameof(components));
        }

        string? previous = null;
        foreach (ContinuityStateComponentCapability component in components)
        {
            if (component is null ||
                !IsStableId(component.ComponentId) ||
                !IsVersionedReference(component.StateSchemaReference) ||
                (previous is not null &&
                    StringComparer.Ordinal.Compare(
                        previous,
                        component.ComponentId) >= 0))
            {
                throw Error(
                    "ContinuityChain.InvalidConfiguration",
                    nameof(components));
            }

            previous = component.ComponentId;
        }
    }
}
