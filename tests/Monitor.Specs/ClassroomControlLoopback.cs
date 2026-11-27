// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Network;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static partial class ClassroomControlSpecifications
{
    internal static async Task VerifyLoopbackAsync(ClassroomHost host, ClassroomStudentClient first, ClassroomStudentClient second,
        ClassroomManifest firstManifest, CancellationToken ct)
    {
        const string action = "therapy.defib.energy.set";
        var secondManifest = await second.GetSessionAsync(ct).ConfigureAwait(false);
        using var parameters = JsonDocument.Parse("""{"energy":{"unit_code":"JOULE","mantissa_i64":"200","exponent10":0}}""");
        ClassroomActionIntent Intent(ClassroomManifest m, ulong revision)
        {
            var id = Guid.NewGuid();
            return new("LearnerActionIntent@1", id, $"action.{id:N}", m.SessionId, m.StudentId, m.InstanceId,
                action, revision, 5, "classroom.energy", null, parameters.RootElement);
        }

        host.DesignateOperator(firstManifest.StudentId);
        try
        {
            var denied = await second.GetActionProjectionAsync(action, ct).ConfigureAwait(false);
            var observer = await second.SubmitActionAsync(Intent(secondManifest, denied.ProjectionRevision), ct).ConfigureAwait(false);
            Check.That(denied.Availability == "Disabled" && observer.Rejection?.ReasonCode == "OPERATOR_REQUIRED",
                "an observer cannot submit a device action using its own classroom bearer");
            var projection = await first.GetActionProjectionAsync(action, ct).ConfigureAwait(false);
            Check.That(projection.Availability == "Enabled", "the designated endpoint receives an enabled action projection");
            ClassroomActionIntent intent = Intent(firstManifest, projection.ProjectionRevision);
            var committed = await first.SubmitActionAsync(intent, ct).ConfigureAwait(false);
            var replay = await first.SubmitActionAsync(intent, ct).ConfigureAwait(false);
            var outcome = await first.GetActionResultAsync(intent.IntentId, ct).ConfigureAwait(false);
            Check.That(committed.Accepted && committed == replay && replay == outcome &&
                await second.GetActionResultAsync(intent.IntentId, ct).ConfigureAwait(false) is null,
                "a lost receipt can be fetched and replayed only in its original actor scope");
            var shared = await second.GetControlSnapshotAsync(ct).ConfigureAwait(false);
            Check.That(shared.DeviceState.GetProperty("defib_energy_joules").GetInt32() == 200,
                "observers receive the resulting authority state");
            long effective = Math.Max(firstManifest.NextBlockStartSimTimeNs, secondManifest.NextBlockStartSimTimeNs) +
                100L * WaveformBlockAssembler.BlockDurationNs;
            var change = host.ScheduleActionPolicy(Guid.NewGuid(), shared.ConfigurationRevision, effective,
                [action], [firstManifest.StudentId, secondManifest.StudentId], 2);
            var staged = await first.GetScheduledActionPolicyAsync(ct).ConfigureAwait(false);
            Check.That(staged?.ChangeId == change.ChangeId, "the required client can fetch its staged allowlist");
            await first.AcknowledgeActionPolicyAsync(staged!, ct).ConfigureAwait(false);
            await second.AcknowledgeActionPolicyAsync(staged!, ct).ConfigureAwait(false);
            Check.That((await first.GetScheduledActionPolicyAsync(ct).ConfigureAwait(false))?.State == "Prepared" &&
                host.CancelActionPolicy(change.ChangeId), "both required acknowledgements prepare a still-cancellable change");
        }
        finally { host.DesignateOperator(null); }
    }
}
