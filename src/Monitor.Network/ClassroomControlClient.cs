// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Monitor.Network;

public sealed partial class ClassroomStudentClient
{
    public static IReadOnlyList<string> RegisteredActionIds { get; } =
        Array.AsReadOnly(ClassroomControlCatalog.Catalog.Keys.Order(StringComparer.Ordinal).ToArray());

    public async Task<ClassroomActionProjection> GetActionProjectionAsync(string actionId, CancellationToken cancellationToken = default)
    {
        if (!ClassroomControlCatalog.Catalog.ContainsKey(actionId)) { throw new ArgumentException("Unknown classroom action.", nameof(actionId)); }
        using HttpResponseMessage response = await _http.GetAsync("v1/actions/projection/" + actionId, cancellationToken).ConfigureAwait(false);
        var projection = await ReadActionBody<ClassroomActionProjection>(response, cancellationToken).ConfigureAwait(false);
        ClassroomContracts.Validate("ActionProjection@1", JsonSerializer.SerializeToElement(projection, ClassroomControlWire.Json));
        if (projection.ActionId != actionId) { throw new InvalidDataException("Unexpected action projection."); }
        return projection;
    }

    public async Task<ClassroomActionResult> SubmitActionAsync(ClassroomActionIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!ClassroomControlCatalog.ValidIntent(intent)) { throw new ArgumentException("Invalid learner action intent.", nameof(intent)); }
        using HttpResponseMessage response = await _http.PostAsJsonAsync("v1/actions", intent, ClassroomControlWire.Json, cancellationToken)
            .ConfigureAwait(false);
        ClassroomActionResult receipt = await ReadActionBody<ClassroomActionResult>(response, cancellationToken).ConfigureAwait(false);
        ValidateReceipt(receipt, intent.IntentId);
        if (intent.IdempotencyKey != receipt.IdempotencyKey || intent.ActionId != receipt.ActionId)
        { throw new InvalidDataException("Unexpected action receipt."); }
        return receipt;
    }

    public async Task<ClassroomActionResult?> GetActionResultAsync(Guid intentId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync($"v1/actions/results/{intentId:D}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) { return null; }
        ClassroomActionResult result = await ReadActionBody<ClassroomActionResult>(response, cancellationToken).ConfigureAwait(false);
        ValidateReceipt(result, intentId);
        return result;
    }

    public async Task<ClassroomControlSnapshot> GetControlSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync("v1/actions/state", cancellationToken).ConfigureAwait(false);
        return await ReadActionBody<ClassroomControlSnapshot>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClassroomScheduledControl?> GetScheduledActionPolicyAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync("v1/configuration/actions", cancellationToken).ConfigureAwait(false);
        var body = await ReadActionBody<ClassroomControlScheduleReply>(response, cancellationToken).ConfigureAwait(false);
        if (body.Change is { } change) { VerifyScheduledContent(change); }
        return body.Change;
    }

    public async Task AcknowledgeActionPolicyAsync(ClassroomScheduledControl change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        VerifyScheduledContent(change);
        using HttpResponseMessage response = await _http.PostAsJsonAsync("v1/configuration/actions/prepared",
            new ClassroomControlPrepared(change.ChangeId, change.PublicDiffSha256), ClassroomControlWire.Json, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static void ValidateReceipt(ClassroomActionResult result, Guid intentId)
    {
        ClassroomContracts.Validate("LearnerActionCommitted@1", JsonSerializer.SerializeToElement(result, ClassroomControlWire.Json));
        if (result.IntentId != intentId) { throw new InvalidDataException("Unexpected action receipt."); }
    }

    private static async Task<T> ReadActionBody<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "application/json") { throw new InvalidDataException("Expected classroom JSON."); }
        return await response.Content.ReadFromJsonAsync<T>(ClassroomControlWire.Json, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing action response.");
    }
}
