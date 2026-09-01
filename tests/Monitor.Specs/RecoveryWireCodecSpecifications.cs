// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Monitor.Domain.Continuity;
using Monitor.Infrastructure.Continuity;

namespace Monitor.Specs;

internal static class RecoveryWireCodecSpecifications
{
    private static readonly Guid ClientId =
        Guid.Parse("11111111-1111-4111-8111-abcdefabcdef");
    private static readonly Guid SessionId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid InstanceId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid ReportId =
        Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid PlanId =
        Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid EventA =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid EventB =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    public static Specification[] All =>
    [
        new(nameof(HelloUsesExactCanonicalContractBytes),
            HelloUsesExactCanonicalContractBytes),
        new(nameof(ReportPreservesBoundedCanonicalCollections),
            ReportPreservesBoundedCanonicalCollections),
        new(nameof(PlanRoundTripsIntoTheRelockGate),
            PlanRoundTripsIntoTheRelockGate),
        new(nameof(UnknownAndMalformedShapesFailClosed),
            UnknownAndMalformedShapesFailClosed),
        new(nameof(NonCanonicalAndOversizedWireImagesAreRejected),
            NonCanonicalAndOversizedWireImagesAreRejected),
    ];

    private static void HelloUsesExactCanonicalContractBytes()
    {
        RecoveryHelloMessage hello = Hello() with
        {
            LastAppliedCommitSequence = ulong.MaxValue,
        };
        byte[] wire = RecoveryWireCodec.EncodeHello(hello);
        string expected =
            $"{{\"base_sha256\":\"{Hash('a')}\"," +
            $"\"client_id\":\"{ClientId:D}\"," +
            $"\"instance_id\":\"{InstanceId:D}\"," +
            "\"last_applied_commit_seq\":\"18446744073709551615\"," +
            "\"last_applied_event_seq\":\"90\"," +
            $"\"last_delta_sha256\":\"{Hash('b')}\"," +
            $"\"local_continuation_report_id\":\"{ReportId:D}\"," +
            "\"momentary_inputs_cleared\":true," +
            "\"observed_authority_epoch\":\"4\"," +
            "\"schema_id\":\"RecoveryHello@1\"," +
            $"\"session_id\":\"{SessionId:D}\"}}";
        RecoveryHelloMessage decoded = RecoveryWireCodec.DecodeHello(wire);

        Check.That(Encoding.UTF8.GetString(wire) == expected && decoded == hello,
            "RecoveryHello must use one exact RFC 8785-compatible wire image");

        var coordinator =
            RecoveryHandshakeCoordinator.Start(
                ClientId,
                SessionId,
                InstanceId,
                hostAuthorityEpoch: 5);
        Check.That(coordinator.AcceptHello(decoded).Phase ==
            RecoveryHandshakePhase.AwaitingReport,
            "a decoded hello must enter the existing Host intake gate unchanged");
    }

    private static void ReportPreservesBoundedCanonicalCollections()
    {
        LocalContinuationReportMessage report = Report() with
        {
            FallbackEpoch = ulong.MaxValue,
        };
        byte[] wire = RecoveryWireCodec.EncodeReport(report);
        LocalContinuationReportMessage decoded =
            RecoveryWireCodec.DecodeReport(wire);
        using var document = JsonDocument.Parse(wire);
        string[] propertyNames = document.RootElement
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();

        Check.That(propertyNames.SequenceEqual(propertyNames.Order()) &&
            Encoding.UTF8.GetString(wire).Contains(
                "\"fallback_epoch\":\"18446744073709551615\"",
                StringComparison.Ordinal) &&
            decoded.LastSampleIndexByChannel.SequenceEqual(
                report.LastSampleIndexByChannel) &&
            decoded.OfflineActionEventIds.SequenceEqual(
                report.OfflineActionEventIds) &&
            RecoveryWireCodec.EncodeReport(decoded).SequenceEqual(wire),
            "report keys, integer strings and ordered collections must round-trip");

        var coordinator =
            RecoveryHandshakeCoordinator.Start(
                ClientId,
                SessionId,
                InstanceId,
                hostAuthorityEpoch: 5);
        _ = coordinator.AcceptHello(Hello());
        Check.That(coordinator.AcceptReport(decoded).Phase ==
            RecoveryHandshakePhase.VerifyingReport,
            "a decoded report must still pass the Host semantic intake gate");

        LocalContinuationReportMessage tooManyChannels = Report() with
        {
            LastSampleIndexByChannel = Enumerable.Range(0, 129)
                .Select(index => new LocalContinuationSampleFrontier(
                    $"ECG.C{index}",
                    checked((ulong)index)))
                .ToArray(),
        };
        Check.That(Reason(() => RecoveryWireCodec.EncodeReport(
                tooManyChannels)) == "RecoveryWire.InvalidShape",
            "the schema's 128-channel bound must be enforced before writing");

        LocalContinuationReportMessage tooManyActions = Report() with
        {
            OfflineActionEventIds = Enumerable.Range(0, 10_001)
                .Select(index => new Guid(index, 0, 0, new byte[8]))
                .ToArray(),
        };
        Check.That(Reason(() => RecoveryWireCodec.EncodeReport(
                tooManyActions)) == "RecoveryWire.InvalidShape",
            "the schema's 10,000-action bound must be enforced before writing");
    }

    private static void PlanRoundTripsIntoTheRelockGate()
    {
        RecoveryResyncPlan plan = Plan();
        byte[] wire = RecoveryWireCodec.EncodePlan(plan);
        RecoveryResyncPlan decoded = RecoveryWireCodec.DecodePlan(wire);
        using var document = JsonDocument.Parse(wire);
        string[] propertyNames = document.RootElement
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();
        Check.That(decoded == plan &&
            propertyNames.SequenceEqual(propertyNames.Order()) &&
            RecoveryWireCodec.EncodePlan(decoded).SequenceEqual(wire),
            "ResyncPlan must preserve every frozen field in canonical key order");

        var client = RecoveryRelockCoordinator.Start(
            ClientId,
            SessionId,
            InstanceId,
            acceptedAuthorityEpoch: 4,
            acceptedStreamEpoch: 7,
            lastAppliedCommitSequence: 100,
            currentSimTimeNs: 0);
        Check.That(client.AcceptPlan(decoded, 0).Phase ==
            RecoveryRelockPhase.Preparing,
            "a decoded plan must enter the client relock gate without translation");
    }

    private static void UnknownAndMalformedShapesFailClosed()
    {
        string canonical = Encoding.UTF8.GetString(
            RecoveryWireCodec.EncodeHello(Hello()));
        string missing = canonical.Replace(
            $"\"base_sha256\":\"{Hash('a')}\",",
            string.Empty,
            StringComparison.Ordinal);
        string unknown = canonical.Insert(1, "\"unknown\":null,");
        string duplicate = canonical.Insert(
            1,
            $"\"base_sha256\":\"{Hash('a')}\",");
        string unsupported = canonical.Replace(
            "RecoveryHello@1",
            "RecoveryHello@2",
            StringComparison.Ordinal);
        string leadingZero = canonical.Replace(
            "\"observed_authority_epoch\":\"4\"",
            "\"observed_authority_epoch\":\"04\"",
            StringComparison.Ordinal);
        string number = canonical.Replace(
            "\"observed_authority_epoch\":\"4\"",
            "\"observed_authority_epoch\":4",
            StringComparison.Ordinal);

        Check.That(Reason(() => DecodeHello(missing)) ==
                "RecoveryWire.InvalidShape" &&
            Reason(() => DecodeHello(unknown)) ==
                "RecoveryWire.InvalidShape" &&
            Reason(() => DecodeHello(duplicate)) ==
                "RecoveryWire.InvalidShape" &&
            Reason(() => DecodeHello(unsupported)) ==
                "RecoveryWire.UnsupportedSchema" &&
            Reason(() => DecodeHello(leadingZero)) ==
                "RecoveryWire.InvalidShape" &&
            Reason(() => DecodeHello(number)) ==
                "RecoveryWire.InvalidShape",
            "unknown, missing, duplicate, unsupported and mistyped fields must fail");

        string unknownDecision = Encoding.UTF8.GetString(
            RecoveryWireCodec.EncodePlan(Plan())).Replace(
                "CommitVerified",
                "TrustClient",
                StringComparison.Ordinal);
        Check.That(Reason(() => DecodePlan(unknownDecision)) ==
            "RecoveryWire.InvalidShape",
            "unknown wire enums cannot be guessed into a local decision");
    }

    private static void NonCanonicalAndOversizedWireImagesAreRejected()
    {
        string canonical = Encoding.UTF8.GetString(
            RecoveryWireCodec.EncodeHello(Hello()));
        string whitespace = $" {canonical}";
        string uppercaseUuid = canonical.Replace(
            ClientId.ToString("D"),
            ClientId.ToString("D").ToUpperInvariant(),
            StringComparison.Ordinal);

        Check.That(Reason(() => DecodeHello(whitespace)) ==
                "RecoveryWire.NonCanonical" &&
            Reason(() => DecodeHello(uppercaseUuid)) ==
                "RecoveryWire.NonCanonical" &&
            Reason(() => RecoveryWireCodec.DecodeHello(new byte[] { (byte)'{' })) ==
                "RecoveryWire.InvalidJson" &&
            Reason(() => RecoveryWireCodec.DecodeHello(Array.Empty<byte>())) ==
                "RecoveryWire.InvalidJson" &&
            Reason(() => RecoveryWireCodec.DecodeHello(
                new byte[RecoveryWireCodec.MaximumMessageBytes + 1])) ==
                "RecoveryWire.MessageTooLarge",
            "noncanonical, malformed and oversized messages must be distinct");
    }

    private static RecoveryHelloMessage Hello() => new(
        ClientId,
        SessionId,
        InstanceId,
        ObservedAuthorityEpoch: 4,
        LastAppliedCommitSequence: 100,
        LastAppliedEventSequence: 90,
        BaseSha256: Hash('a'),
        LastDeltaSha256: Hash('b'),
        LocalContinuationReportId: ReportId,
        MomentaryInputsCleared: true);

    private static LocalContinuationReportMessage Report() => new(
        ReportId,
        SessionId,
        ClientId,
        InstanceId,
        BranchId: "shared.observer",
        BaseSha256: Hash('a'),
        LastDeltaSha256: Hash('b'),
        FallbackEpoch: 0,
        StartSimTimeNs: 10_000_000_000,
        EndSimTimeNs: 10_800_000_000,
        LastSampleIndexByChannel:
        [
            new LocalContinuationSampleFrontier("ECG.II", 2000),
            new LocalContinuationSampleFrontier("Pleth", 500),
        ],
        LastBlockSequence: 60,
        LastEventSequence: 95,
        RollingStateSha256: Hash('c'),
        OfflineActionChainSha256: Hash('d'),
        OfflineActionEventIds: new[] { EventA, EventB },
        IntegrityState: ContinuityReportIntegrityState.PendingVerification);

    private static RecoveryResyncPlan Plan() => new(
        PlanId,
        NewAuthorityEpoch: 5,
        HostCheckpointSequence: 44,
        HostStateSha256: Hash('a'),
        RelockSimTimeNs: 2_000_000_000,
        NewStreamEpoch: 8,
        PrerollFromSimTimeNs: 0,
        PrerollUntilSimTimeNs: 2_000_000_000,
        LocalPredictionDecision: LocalPredictionDecision.CommitVerified,
        ReasonCode: "HOST_RECOVERY",
        AcceptedThroughCommitSequence: 101,
        ResumeSnapshotSha256: null);

    private static void DecodeHello(string json) =>
        _ = RecoveryWireCodec.DecodeHello(Encoding.UTF8.GetBytes(json));

    private static void DecodePlan(string json) =>
        _ = RecoveryWireCodec.DecodePlan(Encoding.UTF8.GetBytes(json));

    private static string Hash(char character) => new(character, 64);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (RecoveryWireCodecException exception)
        {
            return exception.ReasonCode;
        }
    }
}
