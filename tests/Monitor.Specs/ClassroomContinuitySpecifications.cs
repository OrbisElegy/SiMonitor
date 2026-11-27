// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Monitor.Application.Continuity;
using Monitor.Infrastructure.Continuity;
using Monitor.Network;

namespace Monitor.Specs;

internal static class ClassroomContinuitySpecifications
{
    // These public vectors were signed offline with SM2/SM3. No signing key is part of the fixture.
    internal static readonly Guid SessionId = new("00000001-1111-4111-8111-111111111111");
    internal static readonly Guid InstanceId = new("00000002-1111-4111-8111-111111111111");
    internal static readonly Guid StudentId = new("00000003-1111-4111-8111-111111111111");
    private const string PublicKey = "04c7d80cd94122e5bf747c1678d80b2ec720fda766ca1fec07745befeea9eed3276ff867e7c51c1589e869da6359dc0f9ebdf47e97e2c73556bb04bea608f4c350";
    private static ClassroomSignedContinuity Base() => Packet("ContinuityCapsuleBase", "00000004-1111-4111-8111-111111111111",
        "60f0b9b10d714e6b71719175b1cd387c132afc59ca5a9c452d977eb59a6d31ac8e340a55bdd8199146452268ef74d486c151b05be0f339bfd01e44128996ad46",
        """{"authority_epoch":"1","branch_id":"class.practice","capsule_id":"00000004-1111-4111-8111-111111111111","capsule_sha256":"75fb403a15d68891bee3dc81feaa3db8812d704df1659d03782e0652046776b1","channel_cursors":[{"channel_id":"ecg","next_block_seq":"0","next_numeric_seq":"0","next_sample_index":"0"}],"checkpoint_seq":"1","commit_seq":"1","continuation_permission":"Allowed","continuation_valid_until":"120000000000","encryption_envelope_ref":null,"engine_ref":{"content_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","id":"Engine","version":"1.0.0"},"instance_id":"00000002-1111-4111-8111-111111111111","math_profile_ref":{"content_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","id":"Math","version":"1.0.0"},"next_event_seq":"1","projection_revisions":{"alarm_audio_policy":"1","alarm_episode":"1","display_role":"1","therapy":"1"},"resource_pack_refs":[],"schema_id":"ContinuityCapsuleBase@1","secret_boundary_sim_time":"120000000000","session_id":"00000001-1111-4111-8111-111111111111","sim_time_ns":"0","state_components":[{"component_id":"wave.phase","state_base64":"QQ==","state_schema_ref":{"content_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","id":"WaveState","version":"1.0.0"},"state_sha256":"559aead08264d5795d3909718cdd05abd49572e84fe55590eef31a88a08fdffd"}],"stream_epoch":"2","timebase_epoch":"1"}"""u8);

    private static ClassroomSignedContinuity Delta() => Packet("ContinuityCapsuleDelta", "00000005-1111-4111-8111-111111111111",
        "218e80df9c60c8de23c4cc8a041ce255eb1b8586e699fc1ebb4b333117220eb01486addd1d4090bee7164474820d993a085be98e3aca12cb5ccc4805cb838b86",
        """{"base_sha256":"75fb403a15d68891bee3dc81feaa3db8812d704df1659d03782e0652046776b1","changed_components":[{"component_id":"wave.phase","state_base64":"Qg==","state_schema_ref":{"content_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","id":"WaveState","version":"1.0.0"},"state_sha256":"df7e70e5021544f4834bbee64a9e3789febc4be81470df629cad6ddb03320a5c"}],"checkpoint_seq":"2","commit_seq":"2","continuation_valid_until":"120000000000","delta_id":"00000005-1111-4111-8111-111111111111","delta_sha256":"a5cfc3b462eb547483fd954ba0f446fe15d8913bb13fa6a2649fa1ad63669524","previous_delta_sha256":null,"schema_id":"ContinuityCapsuleDelta@1","secret_boundary_sim_time":"120000000000","sim_time_ns":"200000000"}"""u8);

    private static ClassroomSignedContinuity Lease() => Packet("OfflineCapabilityLease", "00000006-1111-4111-8111-111111111111",
        "c1c315c39ba60235bc4cc6a6b24b654617ff627d272c3f64f8e5526d4c2bd9f3c9cd171e16500bface113f7f73794aef0086c3b9eb7e277fc2d1cbcc9d178ee2",
        """{"allowed_action_ids":["nibp.measurement.start"],"authority_epoch":"1","base_capsule_sha256":"75fb403a15d68891bee3dc81feaa3db8812d704df1659d03782e0652046776b1","branch_id":"class.practice","client_id":"00000003-1111-4111-8111-111111111111","continuation_valid_until":"120000000000","expires_at_safety_ns":"120000000000","instance_id":"00000002-1111-4111-8111-111111111111","issued_at_safety_ns":"0","lease_id":"00000006-1111-4111-8111-111111111111","maximum_offline_operation_seq":"3","maximum_offline_window_ns":"120000000000","schema_id":"OfflineCapabilityLease@1","secret_boundary_sim_time":"120000000000","session_id":"00000001-1111-4111-8111-111111111111","takeover_from_sim_time_ns":"0"}"""u8);

    private static ClassroomSignedContinuity Packet(string kind, string id, string signature, ReadOnlySpan<byte> payload)
    {
        var verifier = new Sm2ContinuitySignatureVerifier();
        return new(new ContinuitySignedBody(kind, Guid.Parse(id), SessionId, InstanceId, 1, 120_000_000_000, 120_000_000_000,
            Convert.ToHexStringLower(SHA256.HashData(payload))),
            new ContinuitySignatureProof(ContinuitySignatureGate.SignatureAlgorithmId, verifier.DeriveKeyId(Convert.FromHexString(PublicKey)),
                1, 0, Convert.FromHexString(signature)), payload.ToArray());
    }

    private static ClassroomContinuityReceiver Receiver()
    {
        byte[] key = Convert.FromHexString(PublicKey);
        var verifier = new Sm2ContinuitySignatureVerifier();
        var trusted = new TrustedContinuitySigningKey(verifier.DeriveKeyId(key), key, ContinuitySigningKeyState.Active, 1, 0, 150_000_000_000);
        ContinuityVersionedReference Ref(string id) => new(id, "1.0.0", new string('a', 64));
        var capabilities = new ContinuityRuntimeCapabilities(Ref("Engine"), Ref("Math"), [], ["ecg"], [new("wave.phase", Ref("WaveState"))]);
        return new ClassroomContinuityReceiver(StudentId, SessionId, InstanceId, 1, "class.practice", [trusted], capabilities,
            component => component.StateBytes.Length == 1 && component.StateBytes[0] is (byte)'A' or (byte)'B');
    }

    internal static async Task VerifyLoopbackAsync(ClassroomHost host, ClassroomStudentClient first, ClassroomStudentClient second,
        CancellationToken ct)
    {
        var receive = Receiver();
        host.ConfigureContinuity(StudentId, Receiver());
        host.PublishContinuity(StudentId, Base());
        var corrupt = Delta();
        corrupt.Proof.SignatureRs64[0] ^= 1;
        try { host.PublishContinuity(StudentId, corrupt); throw new InvalidOperationException("Accepted corrupted continuity signature."); }
        catch (ContinuitySignatureGateException) { }
        host.PublishContinuity(StudentId, Delta());
        host.PublishContinuity(StudentId, Lease());
        static (long, long) Time() => (70_000_000_000, 60_000_000_000);
        Check.That(await second.ReadContinuityAsync(Receiver(), 0, Time, ct).ConfigureAwait(false) is null,
            "a different authenticated student cannot retrieve another student's offline capsules");
        ClassroomVerifiedContinuity? firstDelivery = await first.ReadContinuityAsync(receive, 0, Time, ct).ConfigureAwait(false);
        Check.That(firstDelivery?.State?.StateComponents[0].StateBytes[0] == 'A', "signed base restores a permitted state component");
        ClassroomVerifiedContinuity? delta = await first.ReadContinuityAsync(receive, firstDelivery!.Sequence, Time, ct).ConfigureAwait(false);
        Check.That(delta?.State?.StateComponents[0].StateBytes[0] == 'B', "signed delta is applied only to its verified base");
        ClassroomVerifiedContinuity? lease = await first.ReadContinuityAsync(receive, delta!.Sequence, Time, ct).ConfigureAwait(false);
        Check.That(lease?.Lease?.AllowedActions.Single() == "nibp.measurement.start", "an eligible student obtains the explicitly signed offline operation");
        try { await first.ReadContinuityAsync(Receiver(), 0, () => (121_000_000_000, 60_000_000_000), ct).ConfigureAwait(false); throw new InvalidOperationException("Accepted expired capsule."); }
        catch (ContinuitySignatureGateException) { }
        host.ConfigureContinuity(StudentId, null);
    }
}
