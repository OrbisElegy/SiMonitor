// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Infrastructure.Identity;
using Monitor.Network;

namespace Monitor.Specs;

internal static partial class ClassroomControlSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(ActionReceiptsAreScopedDurableAndIdempotent), ActionReceiptsAreScopedDurableAndIdempotent),
        new(nameof(QueuedConfigurationsActivateOnlyAfterPreparedTeacherRelease), QueuedConfigurationsActivateOnlyAfterPreparedTeacherRelease),
        new(nameof(TeachingConfigurationChangesTheTransmittedWaveform), () => TeachingConfigurationChangesTheTransmittedWaveform().GetAwaiter().GetResult()),
        new(nameof(ScheduledOperatorPolicyNeedsMatchingPreparedAcknowledgements), ScheduledOperatorPolicyNeedsMatchingPreparedAcknowledgements),
        new(nameof(MomentaryControlCannotSurviveLossOrRecovery), MomentaryControlCannotSurviveLossOrRecovery)
    ];

    private static void ActionReceiptsAreScopedDurableAndIdempotent()
    {
        using var fixture = new ControlFixture();
        var authority = fixture.Open();
        var actor = Guid.NewGuid();
        ClassroomActionIntent intent = fixture.Intent(actor, "nibp.measurement.start", "{}", 1);
        Check.That(authority.Submit(actor, intent, 0, true, out _).Rejection?.ReasonCode == "OPERATOR_REQUIRED", "observers are read-only");
        authority.Designate(actor, 0);
        intent = intent with { ProjectionRevision = authority.Snapshot().ProjectionRevision };
        ClassroomActionResult result = authority.Submit(actor, intent, 0, true, out bool committed);
        Check.That(committed && result.Accepted && result.CommitSeq == 1, "a designated student starts a real cuff cycle");
        ClassroomContracts.Validate("LearnerActionCommitted@1", JsonSerializer.SerializeToElement(result, ClassroomControlWire.Json));
        authority = fixture.Open();
        Check.That(authority.Lookup(actor, intent.IntentId) == result && authority.Lookup(Guid.NewGuid(), intent.IntentId) is null,
            "durable receipts are visible only to their author");
        Check.That(authority.Submit(actor, intent, 5, true, out committed) == result && !committed, "lost replies do not repeat an action");
        Check.That(authority.Submit(actor, intent with { IntentId = Guid.NewGuid() }, 6, true, out _).Rejection?.ReasonCode == "IDEMPOTENCY_CONFLICT",
            "reusing a key for a different intent is rejected");
        Check.That(authority.Submit(actor, intent with { IntentId = Guid.NewGuid(), IdempotencyKey = "retry.new" }, 7, true, out _)
            .Rejection?.ReasonCode == "STALE_PROJECTION", "stale input never changes the current state");
        authority.Advance(16_000_000_000, 16_000_000_000);
        JsonElement state = authority.Snapshot().DeviceState;
        Check.That(state.GetProperty("reading").GetProperty("systolic_mm_hg").GetInt32() == 120 && !state.TryGetProperty("target", out _),
            "the cuff delivers its reading after the authoritative cycle");
        try { authority.Submit(Guid.NewGuid(), intent, 8, true, out _); }
        catch (UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("A forged actor ID must fail.");
    }

    private static void ScheduledOperatorPolicyNeedsMatchingPreparedAcknowledgements()
    {
        using var fixture = new ControlFixture();
        var authority = fixture.Open();
        var student = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var first = authority.Schedule(Guid.NewGuid(), 1, 1_000_000_000, ["nibp.measurement.start"], [student, peer], 2);
        Check.That(!authority.Prepare(student, first.ChangeId, "wrong") && !authority.Prepare(Guid.NewGuid(), first.ChangeId, first.PublicDiffSha256),
            "only the required endpoint and exact revision may prepare");
        Check.That(authority.Prepare(student, first.ChangeId, first.PublicDiffSha256) &&
            authority.Prepare(student, first.ChangeId, first.PublicDiffSha256), "repeating a prepared ACK is idempotent");
        authority.Advance(1_000_000_000, 1);
        Check.That(authority.Pending()?.State == "Rejected" && authority.Snapshot().ConfigurationRevision == 1,
            "missing Required ACKs preserve the active configuration");
        var second = authority.Schedule(Guid.NewGuid(), 1, 2_000_000_000, [], [student], 1);
        Check.That(authority.Prepare(student, second.ChangeId, second.PublicDiffSha256), "the ACK prepares the second policy");
        authority = fixture.Open();
        authority.Advance(2_000_000_000, 2);
        Check.That(authority.Pending()?.State == "Committed" && authority.Snapshot().ConfigurationRevision == 2 &&
            authority.Projection("nibp.measurement.start", fixture.Teacher).DisabledReasonCode == "ACTION_NOT_ALLOWED",
            "the stored allowlist activates atomically only at its published boundary");
    }

    private static void QueuedConfigurationsActivateOnlyAfterPreparedTeacherRelease()
    {
        using var f = new ControlFixture();
        var a = f.Open();
        var student = Guid.NewGuid();
        var content = new ClassroomConfigurationContent(["nibp.measurement.start"], 100, []);
        var first = a.ScheduleConfiguration(Guid.NewGuid(), 1, 1_000_000_000, content, [student], 1, ClassroomChangeConflict.RejectOnConflict, true);
        var second = a.ScheduleConfiguration(Guid.NewGuid(), 2, 2_000_000_000, content with { HeartRateBpm = 90 }, [], 0, ClassroomChangeConflict.Queue, false);
        var ack = new ClassroomConfigurationAck(first.ChangeId, first.PublicDiffSha256, ClassroomAckStage.Received, null, null, null);
        Check.That(a.Acknowledge(student, ack, 0, 0) && a.Acknowledge(student, ack with { Stage = ClassroomAckStage.Prepared }, 0, 0),
            "received and prepared dispositions remain distinct");
        Check.That(a.Confirm(first.ChangeId), "teacher explicitly releases its prepared revision");
        a.Advance(1_000_000_000, 1);
        a.Advance(1_200_000_000, 2);
        var applied = ack with { Stage = ClassroomAckStage.Applied, ActualSimTimeNs = 1_100_000_000, FirstBlockSequence = 5 };
        Check.That(!a.Acknowledge(student, applied with { FirstBlockSequence = 4 }, 6, 1_200_000_000) &&
            a.Acknowledge(student, applied, 6, 1_200_000_000), "applied requires an actual block at or after the configured boundary");
        a = f.Open();
        Check.That(a.Acks(first.ChangeId)[student] == (applied with { DeviationNs = 100_000_000 }), "reported presentation timing survives restart");
        a.Advance(2_000_000_000, 3);
        Check.That(a.Configuration().Active.HeartRateBpm == 90 && a.FindConfiguration(second.ChangeId)?.State == "Committed",
            "the queued second configuration uses its predecessor as baseline");
        var unconfirmed = a.ScheduleConfiguration(Guid.NewGuid(), 3, 3_000_000_000, content, [], 0, ClassroomChangeConflict.Queue, true);
        a.Advance(3_000_000_000, 4);
        Check.That(a.FindConfiguration(unconfirmed.ChangeId)?.State == "Rejected" && a.ConfigurationRevision == 3,
            "an unconfirmed teacher gate preserves the current source");
        var obsolete = a.ScheduleConfiguration(Guid.NewGuid(), 3, 4_000_000_000, content, [], 0, ClassroomChangeConflict.Queue, false);
        var replacement = a.ScheduleConfiguration(Guid.NewGuid(), 3, 5_000_000_000, content, [], 0, ClassroomChangeConflict.ReplacePending, false);
        Check.That(a.FindConfiguration(obsolete.ChangeId)?.State == "Cancelled" && a.Cancel(replacement.ChangeId),
            "explicit replacement and later cancellation preserve prior decisions");
    }

    private static void MomentaryControlCannotSurviveLossOrRecovery()
    {
        using var fixture = new ControlFixture();
        var authority = fixture.Open();
        authority.BindReducer(new HoldReducer(), 0);
        var student = Guid.NewGuid();
        authority.Designate(student, 0);
        var interaction = Guid.NewGuid();
        string parameters = JsonSerializer.Serialize(new { interaction_id = interaction });
        ClassroomActionIntent Press() => fixture.Intent(student, "therapy.defib.shock.press", parameters, authority.Snapshot().ProjectionRevision);
        Check.That(authority.Submit(student, Press(), 0, false, out _).Rejection?.ReasonCode == "CONTROL_CONNECTION_REQUIRED",
            "a momentary input needs a live connection");
        Check.That(authority.Submit(student, Press(), 0, true, out _, simRunning: false).Rejection?.ReasonCode == "CLASSROOM_PAUSED",
            "a paused classroom cannot reacquire an interaction even while the socket is healthy");
        Check.That(authority.Submit(student, Press(), 0, true, out _).Accepted && authority.HasHeldAction, "press grants a bounded lease");
        var foreign = fixture.Intent(student, "therapy.defib.shock.release",
            JsonSerializer.Serialize(new { interaction_id = Guid.NewGuid() }), authority.Snapshot().ProjectionRevision);
        Check.That(!authority.Submit(student, foreign, 5, true, out _).Accepted && authority.HasHeldAction, "late release cannot cancel a new hold");
        authority.Disconnect(student, 10);
        Check.That(!authority.HasHeldAction && !authority.Snapshot().DeviceState.GetProperty("held").GetBoolean(), "disconnect cancels the reducer state");
        Check.That(authority.Submit(student, Press(), 15, true, out _).Accepted, "a new interaction may start");
        authority.Expire(1_000_000_015);
        Check.That(!authority.HasHeldAction, "safety-clock expiry works while the simulated clock is paused");
        Check.That(authority.Submit(student, Press(), 2_000_000_000, true, out _).Accepted, "establish a hold before a simulated crash");
        authority = fixture.Open();
        authority.BindReducer(new HoldReducer(), 0);
        Check.That(!authority.HasHeldAction && !authority.Snapshot().DeviceState.GetProperty("held").GetBoolean(),
            "restoring durable device state cannot revive a held input");
    }

    private sealed class HoldReducer : IClassroomMomentaryActionReducer
    {
        public string ProfileId => "test.Momentary@1";
        public JsonElement InitialState => JsonSerializer.SerializeToElement(new { held = false });
        public bool Supports(string actionId) => actionId.StartsWith("therapy.defib.shock.", StringComparison.Ordinal);
        public JsonElement Reduce(JsonElement currentState, ClassroomActionIntent action, long simTimeNs, long safetyTimeNs) =>
            JsonSerializer.SerializeToElement(new { held = !action.ActionId.EndsWith(".release", StringComparison.Ordinal) });
        public JsonElement CancelInteraction(JsonElement currentState, Guid clientId, Guid interactionId, long safetyTimeNs) => InitialState;
    }

    private sealed class ControlFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("classroom-control-");
        private readonly AesGcmIdentityPayloadProtector _protector = new(RandomNumberGenerator.GetBytes(32));
        private readonly Guid _session = Guid.NewGuid(), _instance = Guid.NewGuid();
        internal Guid Teacher { get; } = Guid.NewGuid();

        internal ClassroomControlAuthority Open() => new(_session, _instance, Teacher, Path.Combine(_directory.FullName, "control.db"), _protector);
        internal ClassroomActionIntent Intent(Guid actor, string action, string parameters, ulong revision)
        {
            var id = Guid.NewGuid();
            using var document = JsonDocument.Parse(parameters);
            return new("LearnerActionIntent@1", id, $"action.{id:N}", _session, actor, _instance, action, revision, 1,
                "test.control", null, document.RootElement.Clone());
        }

        public void Dispose()
        {
            _protector.Dispose();
            _directory.Delete(recursive: true);
        }
    }
}
