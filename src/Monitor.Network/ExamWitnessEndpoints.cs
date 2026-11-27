// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;

namespace Monitor.Network;

internal static class ExamWitnessEndpoints
{
    internal const string LeasePath = "/v1/exam/witness";

    internal static void Map(IEndpointRouteBuilder routes, ExamWitnessStore store, Func<HttpContext, Guid?> authenticatedHost)
    {
        routes.MapGet(LeasePath, (HttpContext context) =>
        {
            if (authenticatedHost(context) is not { } owner || owner == Guid.Empty) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            try { return Results.Json(store.Read(), ExamClusterConfiguration.Json); }
            catch (Exception error) when (error is SqliteException or IOException or InvalidDataException)
            { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });
        routes.MapPost(LeasePath + "/acquire", (Func<HttpContext, Task<IResult>>)(context => ChangeAsync(context, true)));
        routes.MapPost(LeasePath + "/renew", (Func<HttpContext, Task<IResult>>)(context => ChangeAsync(context, false)));

        async Task<IResult> ChangeAsync(HttpContext context, bool acquire)
        {
            if (authenticatedHost(context) is not { } owner || owner == Guid.Empty)
            { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            if (!context.Request.HasJsonContentType()) { return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType); }
            const int maximumRequestBytes = 1024;
            if (context.Request.ContentLength > maximumRequestBytes) { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }
            var size = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (size is { IsReadOnly: false }) { size.MaxRequestBodySize = maximumRequestBytes; }

            ExamWitnessRequest? request;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                request = await JsonSerializer.DeserializeAsync<ExamWitnessRequest>(context.Request.Body,
                    ExamClusterConfiguration.Json, deadline.Token).ConfigureAwait(false);
            }
            catch (JsonException) { return Results.BadRequest(); }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            { return Results.StatusCode(StatusCodes.Status408RequestTimeout); }
            if (request is null) { return Results.BadRequest(); }
            try
            {
                bool accepted = acquire ? store.TryAcquire(owner, request, out var lease) : store.TryRenew(owner, request, out lease);
                return Results.Json(lease, ExamClusterConfiguration.Json,
                    statusCode: accepted ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
            }
            catch (Exception error) when (error is ArgumentException or OverflowException) { return Results.BadRequest(); }
            catch (Exception error) when (error is SqliteException or IOException or InvalidDataException)
            { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        }
    }
}
