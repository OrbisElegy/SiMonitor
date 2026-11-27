// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace Monitor.Network;

public sealed partial class ClassroomStudentClient
{
    public async Task<ClassroomBranchAssignment> GetBranchAssignmentAsync(CancellationToken cancellationToken = default)
    {
        if (_privateInstance is not null) { throw new InvalidOperationException("Read branch assignments using the original shared classroom connection."); }
        using HttpResponseMessage response = await _http.GetAsync("v1/branch/assignment", cancellationToken).ConfigureAwait(false);
        return await ReadControlAsync<ClassroomBranchAssignment>(response, cancellationToken).ConfigureAwait(false);
    }

    public Task<ClassroomStudentClient> ConnectAssignedBranchAsync(CancellationToken cancellationToken = default) =>
        ConnectAssignedBranchAsync(_negotiatedHello, cancellationToken);

    public async Task<ClassroomStudentClient> ConnectAssignedBranchAsync(JsonElement? presentation, CancellationToken cancellationToken = default)
    {
        ClassroomBranchAssignment assignment = await GetBranchAssignmentAsync(cancellationToken).ConfigureAwait(false);
        if (assignment.State is not ("Detached" or "ReturnRejected" or "ArchivePending" or "DiscardPending") || assignment.InstanceId == assignment.SharedInstanceId ||
            assignment.InstanceId == Guid.Empty || assignment.SessionId == Guid.Empty)
        { throw new InvalidOperationException("This student has no active private classroom instance."); }
        using HttpResponseMessage reply = await _http.PostAsync("v1/branch/enter/" + assignment.InstanceId.ToString("D"), null, cancellationToken)
            .ConfigureAwait(false);
        ClassroomBranchCredential handoff = await ReadControlAsync<ClassroomBranchCredential>(reply, cancellationToken).ConfigureAwait(false);
        if (handoff.InstanceId != assignment.InstanceId || handoff.SessionId != assignment.SessionId)
        { throw new InvalidDataException("Unexpected classroom instance in branch assignment."); }
        return await JoinCoreAsync(_endpoint, handoff.JoinCode, _trustedRoot, _pinnedLeaf, handoff.SessionId, handoff.InstanceId,
            presentation, cancellationToken).ConfigureAwait(false);
    }
}
