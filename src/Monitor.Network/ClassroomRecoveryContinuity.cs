// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Monitor.Application.Continuity;
using Monitor.Infrastructure.Continuity;

namespace Monitor.Network;

public sealed record ClassroomSignedContinuity(ContinuitySignedBody Body, ContinuitySignatureProof Proof,
    byte[] Payload);
public sealed record ClassroomOfflineLease(Guid LeaseId, string BranchId, string BaseSha256, IReadOnlyList<string> AllowedActions,
    long TakeoverFromSimTimeNs, ulong MaximumOfflineWindowNs, ulong MaximumOfflineOperationSequence,
    long ContinuationValidUntilSimTimeNs, long ExpiresAtSafetyNs);
public sealed record ClassroomVerifiedContinuity(ulong Sequence, ContinuityRecoveryStateImage? State, ClassroomOfflineLease? Lease);

public sealed class ClassroomContinuityReceiver
{
    private readonly object _gate = new();
    private readonly ContinuitySignatureGate _leaseGate;
    private readonly ContinuityCapsuleChain _chain;
    private readonly CapsuleDecoder _decoder;
    private readonly HashSet<Guid> _ids = [];
    private readonly Dictionary<string, long> _keyExpiries;
    private ClassroomOfflineLease? _lease;

    public ClassroomContinuityReceiver(Guid studentId, Guid sessionId, Guid instanceId, ulong authorityEpoch, string branchId,
        IReadOnlyList<TrustedContinuitySigningKey> trustedKeys, ContinuityRuntimeCapabilities capabilities,
        Func<ContinuityStateComponent, bool> learnerComponentAllowed)
    {
        if (studentId == Guid.Empty || authorityEpoch == 0 || string.IsNullOrWhiteSpace(branchId)) { throw new ArgumentException("Invalid classroom continuity identity."); }
        ArgumentNullException.ThrowIfNull(learnerComponentAllowed);
        var verifier = new Sm2ContinuitySignatureVerifier();
        StudentId = studentId;
        SessionId = sessionId;
        InstanceId = instanceId;
        AuthorityEpoch = authorityEpoch;
        BranchId = branchId;
        _leaseGate = ContinuitySignatureGate.Start(sessionId, instanceId, authorityEpoch, 0, trustedKeys, verifier);
        _decoder = new CapsuleDecoder(branchId, learnerComponentAllowed);
        _chain = ContinuityCapsuleChain.Start(sessionId, instanceId, authorityEpoch, 0, trustedKeys, verifier, capabilities, _decoder);
        _keyExpiries = trustedKeys.ToDictionary(key => key.KeyId, key => key.ExpiresAtAuthorityMonotonicNs, StringComparer.Ordinal);
    }

    public Guid StudentId { get; }
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong AuthorityEpoch { get; }
    public string BranchId { get; }

    internal bool CanDeliver(ClassroomSignedContinuity packet, long simulationNs, long safetyNs) =>
        simulationNs <= packet.Body.ContinuationValidUntilSimTimeNs && _keyExpiries.TryGetValue(packet.Proof.KeyId, out long until) && safetyNs < until;

    internal ClassroomVerifiedContinuity Accept(ulong sequence, ClassroomSignedContinuity packet, long simulationNs, long safetyNs)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(packet.Body);
        ArgumentNullException.ThrowIfNull(packet.Proof);
        ArgumentNullException.ThrowIfNull(packet.Payload);
        int maximum = packet.Body.CapsuleType == "ContinuityCapsuleBase" ? ContinuitySignatureGate.MaximumBaseCapsuleBytes :
            ContinuitySignatureGate.MaximumDeltaCapsuleBytes;
        if (packet.Payload.Length is 0 || packet.Payload.Length > maximum || packet.Proof.SignatureRs64?.Length != 64 ||
            packet.Body.SessionId != SessionId || packet.Body.InstanceId != InstanceId || packet.Body.AuthorityEpoch != AuthorityEpoch ||
            packet.Body.CapsuleType is not ("ContinuityCapsuleBase" or "ContinuityCapsuleDelta" or "OfflineCapabilityLease"))
        { throw new InvalidDataException("Invalid teaching continuity packet or binding."); }
        lock (_gate)
        {
            if (_ids.Count >= ContinuitySignatureGate.MaximumAcceptedCapsuleCount || _ids.Contains(packet.Body.CapsuleId))
            { throw new InvalidDataException("Replay or exhausted continuity epoch."); }
            if (packet.Body.CapsuleType == "OfflineCapabilityLease")
            {
                ContinuitySignatureAdmission verified = _leaseGate.Verify(packet.Body, packet.Proof, packet.Payload, simulationNs, safetyNs);
                JsonElement json = CapsuleDecoder.ReadSignedPayload(packet.Payload, packet.Body.CapsuleType, packet.Proof);
                ClassroomOfflineLease lease = ReadLease(json, packet.Body, safetyNs);
                _leaseGate.Commit(verified);
                _lease = lease;
            }
            else
            {
                _decoder.Proof = packet.Proof;
                try
                {
                    if (packet.Body.CapsuleType == "ContinuityCapsuleBase")
                    {
                        _chain.AcceptBase(packet.Body, packet.Payload, simulationNs, safetyNs);
                        _lease = null;
                    }
                    else { _chain.AcceptDelta(packet.Body, packet.Payload, simulationNs, safetyNs); }
                }
                finally { _decoder.Proof = null; }
            }
            _ids.Add(packet.Body.CapsuleId);
            if (_lease is not null && (simulationNs > _lease.ContinuationValidUntilSimTimeNs || safetyNs >= _lease.ExpiresAtSafetyNs)) { _lease = null; }
            return new ClassroomVerifiedContinuity(sequence, _chain.RecoveryStateImage, _lease);
        }
    }

    private ClassroomOfflineLease ReadLease(JsonElement json, ContinuitySignedBody body, long safetyNs)
    {
        long issued = CapsuleDecoder.I64(json, "issued_at_safety_ns");
        long expires = CapsuleDecoder.I64(json, "expires_at_safety_ns");
        string baseHash = CapsuleDecoder.Text(json, "base_capsule_sha256");
        long takeover = CapsuleDecoder.I64(json, "takeover_from_sim_time_ns");
        if (json.GetProperty("lease_id").GetGuid() != body.CapsuleId || json.GetProperty("client_id").GetGuid() != StudentId ||
            json.GetProperty("session_id").GetGuid() != SessionId || json.GetProperty("instance_id").GetGuid() != InstanceId ||
            CapsuleDecoder.U64(json, "authority_epoch") != AuthorityEpoch || CapsuleDecoder.Text(json, "branch_id") != BranchId ||
            CapsuleDecoder.I64(json, "secret_boundary_sim_time") != body.SecretBoundarySimTimeNs ||
            CapsuleDecoder.I64(json, "continuation_valid_until") != body.ContinuationValidUntilSimTimeNs ||
            issued < 0 || safetyNs < issued || safetyNs >= expires || takeover < 0 || takeover > body.ContinuationValidUntilSimTimeNs ||
            _chain.Frontier is not { } frontier || baseHash != frontier.BaseSha256 ||
            body.ContinuationValidUntilSimTimeNs > frontier.ContinuationValidUntilSimTimeNs)
        { throw new InvalidDataException("Offline lease is not current for this student, branch and accepted base."); }
        return new ClassroomOfflineLease(body.CapsuleId, BranchId, baseHash,
            json.GetProperty("allowed_action_ids").EnumerateArray().Select(x => x.GetString()!).ToArray(), takeover,
            CapsuleDecoder.U64(json, "maximum_offline_window_ns"), CapsuleDecoder.U64(json, "maximum_offline_operation_seq"),
            body.ContinuationValidUntilSimTimeNs, expires);
    }

    private sealed class CapsuleDecoder(string branchId, Func<ContinuityStateComponent, bool> learnerComponentAllowed) : IContinuityCapsulePayloadDecoder
    {
        internal ContinuitySignatureProof? Proof { get; set; }

        internal static JsonElement ReadSignedPayload(ReadOnlyMemory<byte> bytes, string type, ContinuitySignatureProof proof)
        {
            using var input = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
            if (input.RootElement.ValueKind != JsonValueKind.Object || input.RootElement.TryGetProperty("signature", out _))
            { throw new InvalidDataException("Expected a detached learner projection."); }
            using var combined = new MemoryStream();
            using (var writer = new Utf8JsonWriter(combined))
            {
                writer.WriteStartObject();
                foreach (JsonProperty field in input.RootElement.EnumerateObject()) { field.WriteTo(writer); }
                writer.WritePropertyName("signature");
                JsonSerializer.Serialize(writer, new
                {
                    signature_alg_id = proof.SignatureAlgorithmId,
                    key_id = proof.KeyId,
                    signed_schema_version = new { major = proof.SignedSchemaMajor, minor = proof.SignedSchemaMinor },
                    signature_base64 = proof.SignatureRs64
                });
                writer.WriteEndObject();
            }
            using var full = JsonDocument.Parse(combined.ToArray());
            ClassroomContracts.Validate(type + "@1", full.RootElement);
            return input.RootElement.Clone();
        }

        internal static string Text(JsonElement e, string key) => e.GetProperty(key).GetString()!;
        internal static ulong U64(JsonElement e, string key) => ulong.Parse(Text(e, key), NumberStyles.None, CultureInfo.InvariantCulture);
        internal static long I64(JsonElement e, string key) => long.Parse(Text(e, key), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        private static ContinuityVersionedReference Reference(JsonElement value) =>
            new(Text(value, "id"), Text(value, "version"), Text(value, "content_sha256"));

        private ContinuityStateComponent[] Components(JsonElement array)
        {
            return array.EnumerateArray().Select(value =>
            {
                var component = new ContinuityStateComponent(Text(value, "component_id"), Reference(value.GetProperty("state_schema_ref")),
                    Text(value, "state_sha256"), value.GetProperty("state_base64").GetBytesFromBase64());
                if (!learnerComponentAllowed(component)) { throw new InvalidDataException("Unapproved learner state component."); }
                return component;
            }).ToArray();
        }

        public ContinuityCapsuleBase DecodeBase(ReadOnlyMemory<byte> signedPayload)
        {
            JsonElement root = ReadSignedPayload(signedPayload, "ContinuityCapsuleBase", Proof!);
            JsonElement revisions = root.GetProperty("projection_revisions");
            if (Text(root, "branch_id") != branchId || root.GetProperty("encryption_envelope_ref").ValueKind != JsonValueKind.Null)
            { throw new InvalidDataException("Unsupported capsule branch or encrypted envelope."); }
            return new(root.GetProperty("capsule_id").GetGuid(), root.GetProperty("session_id").GetGuid(), root.GetProperty("instance_id").GetGuid(),
                branchId, U64(root, "authority_epoch"), U64(root, "timebase_epoch"), U64(root, "stream_epoch"), U64(root, "checkpoint_seq"),
                U64(root, "commit_seq"), I64(root, "sim_time_ns"), U64(root, "next_event_seq"),
                root.GetProperty("channel_cursors").EnumerateArray().Select(value => new ContinuityChannelCursor(Text(value, "channel_id"),
                    U64(value, "next_sample_index"), U64(value, "next_block_seq"), U64(value, "next_numeric_seq"))).ToArray(),
                Components(root.GetProperty("state_components")), new(U64(revisions, "display_role"), U64(revisions, "alarm_episode"),
                    U64(revisions, "alarm_audio_policy"), U64(revisions, "therapy")), Reference(root.GetProperty("engine_ref")),
                Reference(root.GetProperty("math_profile_ref")), root.GetProperty("resource_pack_refs").EnumerateArray().Select(Reference).ToArray(),
                Enum.Parse<ContinuityPermissionState>(Text(root, "continuation_permission")), I64(root, "secret_boundary_sim_time"),
                I64(root, "continuation_valid_until"), Text(root, "capsule_sha256"), Proof!, null);
        }

        public ContinuityCapsuleDelta DecodeDelta(ReadOnlyMemory<byte> signedPayload)
        {
            JsonElement root = ReadSignedPayload(signedPayload, "ContinuityCapsuleDelta", Proof!);
            return new(root.GetProperty("delta_id").GetGuid(), Text(root, "base_sha256"), root.GetProperty("previous_delta_sha256").GetString(),
                U64(root, "checkpoint_seq"), U64(root, "commit_seq"), I64(root, "sim_time_ns"), Components(root.GetProperty("changed_components")),
                I64(root, "secret_boundary_sim_time"), I64(root, "continuation_valid_until"), Text(root, "delta_sha256"), Proof!);
        }
    }
}
