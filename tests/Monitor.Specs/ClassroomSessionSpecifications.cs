// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Monitor.Network;

namespace Monitor.Specs;

internal static class ClassroomSessionSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(PlaybackWaitsForAnExplicitFutureBoundary), PlaybackWaitsForAnExplicitFutureBoundary),
        new(nameof(ClosingAdmissionAndEndingTheClassHaveDistinctEffects), ClosingAdmissionAndEndingTheClassHaveDistinctEffects),
        new(nameof(FrozenClassroomEnvelopeRejectsMisboundPayloads), FrozenClassroomEnvelopeRejectsMisboundPayloads)
    ];

    private static void PlaybackWaitsForAnExplicitFutureBoundary()
    {
        var timeline = new ClassroomTimeline(0, 1);
        Check.That(timeline.Target(12_000_000_000) == 0, "a prepared class must not implicitly begin");
        var start = timeline.Schedule("Running", 12_000_000_000, 0, 1, 1, 300_000_000);
        Check.That(start.HostAnchorNs == 13_000_000_000 && timeline.Target(12_999_999_999) == 0, "future start is shared");
        Check.That(timeline.Target(13_650_000_000) == 650_000_000, "the host maps simulation time with integer anchors");
        var pause = timeline.Schedule("Paused", 13_650_000_000, 600_000_000, 1, 1, 300_000_000);
        long frozen = timeline.Target(pause.HostAnchorNs);
        Check.That(frozen % 200_000_000 == 0 && timeline.Target(pause.HostAnchorNs + 10_000_000_000) == frozen, "paused time never advances");
        var fast = timeline.Schedule("Running", pause.HostAnchorNs + 10_000_000_000, frozen, 3, 2, 300_000_000);
        Check.That(timeline.Target(fast.HostAnchorNs + 200_000_000) == frozen + 300_000_000, "rational speed resumes from the same last sample");
    }

    private static void ClosingAdmissionAndEndingTheClassHaveDistinctEffects()
    {
        using var admission = new ClassroomAdmission(Guid.NewGuid(), Guid.NewGuid());
        string unused = admission.IssueInvitation(Guid.NewGuid());
        Check.That(admission.TryRedeem(admission.IssueInvitation(Guid.NewGuid()), "local", out string? token), "redeem active student");
        admission.CloseInvitations();
        Check.That(!admission.TryRedeem(unused, "local", out _) && admission.TryAuthorize(token!, out _, out _), "closing admission preserves active access");
        admission.ReopenInvitations();
        Check.That(!admission.TryRedeem(unused, "local", out _), "rotation cannot revive outstanding codes");
        ulong epoch = admission.SessionEpoch;
        admission.EndSession();
        admission.EndSession();
        Check.That(admission.SessionEpoch == epoch + 1 && !admission.TryAuthorize(token!, out _, out _), "end revokes the whole epoch exactly once");
    }

    private static void FrozenClassroomEnvelopeRejectsMisboundPayloads()
    {
        using var malformed = JsonDocument.Parse("""{"schema_id":"NumericFrame@1","publish_seq":"18446744073709551616"}""");
        try { ClassroomContracts.Validate("NumericFrame@1", malformed.RootElement); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Incomplete and overflowing numeric fields must be rejected.");
    }

    internal static async Task VerifySessionAsync(ClassroomHost host, ClassroomStudentClient first, ClassroomStudentClient second,
        ClassroomManifest manifest, CancellationToken ct)
    {
        var snapshot = await first.GetTelemetryAsync(ct).ConfigureAwait(false);
        Check.That(first.Negotiation is { SampleFormat: "raw-int16" } && first.Negotiation.ClientId == manifest.StudentId &&
            snapshot.SessionId == manifest.SessionId && snapshot.InstanceId == manifest.InstanceId, "authenticated receiver negotiates its teaching session");
        var other = await second.GetSessionAsync(ct).ConfigureAwait(false);
        try
        {
            await first.NegotiateAsync(other, null, ct).ConfigureAwait(false);
            throw new InvalidOperationException("client cannot take a different seat");
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.UnprocessableEntity) { }
        await first.ReportPreparedAsync(true, 320_000_000, ct).ConfigureAwait(false);
        var next = host.StartClass();
        Check.That(host.StartingSet.SequenceEqual([manifest.StudentId]) && next.PlayoutDelayNs == 320_000_000,
            "only healthy prepared seats are frozen by the teacher start");
        var payload = JsonSerializer.SerializeToElement(new
        {
            schema_id = "NumericFrame@1",
            measurement_channel_ref = new { parameter_id = "HR.ECG", source_instance_id = manifest.InstanceId },
            publish_seq = "1",
            sim_time_ns = "1000000000",
            measured_at_sim_time_ns = "900000000",
            estimated_at_sim_time_ns = "990000000",
            published_at_sim_time_ns = "1000000000",
            source_kind = "DerivedEstimate",
            quantity = new { mantissa_i64 = "72000", exponent10 = -3, unit_code = "BEATS_PER_MINUTE" },
            quality = new { state = "Valid", reason_codes = Array.Empty<string>(), since_sim_time_ns = "900000000" },
            configuration_revision = "1"
        });
        var observed = host.PublishDomainEvent("monitoring.numeric.published", payload, 1_000_000_000, Guid.NewGuid(), 1);
        var events = await Task.WhenAll(first.ReadEventsAsync(manifest, snapshot.NextEventSequence, ct),
            second.ReadEventsAsync(other, snapshot.NextEventSequence, ct)).ConfigureAwait(false);
        Check.That(events.All(page => page.Events.Count == 1 && JsonElement.DeepEquals(page.Events[0], observed)) &&
            JsonElement.DeepEquals((await second.GetTelemetryAsync(ct).ConfigureAwait(false)).Numerics.Single(), payload),
            "both endpoints see the same authoritative numeric event and late-join projection");
    }
}
