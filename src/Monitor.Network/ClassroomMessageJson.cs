// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Monitor.Network;

internal static class ClassroomMessageJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new ClassroomControlWire.U64(), new ClassroomControlWire.I64() },
        MaxDepth = 32
    };

    internal static async Task<T> ReadAsync<T>(HttpResponseMessage response, int maximumBytes, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "application/json") { throw new InvalidDataException("Expected a classroom JSON response."); }
        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(stream, maximumBytes, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        byte[] bytes = ArrayPool<byte>.Shared.Rent(maximumBytes + 1);
        try
        {
            int used = 0;
            do
            {
                int count = await stream.ReadAsync(bytes.AsMemory(used, maximumBytes + 1 - used), timeout.Token).ConfigureAwait(false);
                if (count == 0) { break; }
                used += count;
            } while (used <= maximumBytes);
            if (used > maximumBytes) { throw new InvalidDataException("Classroom message exceeds its size limit."); }
            return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, used), Options) ?? throw new JsonException("Missing classroom JSON message.");
        }
        finally { ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
    }
}

public sealed partial class ClassroomStudentClient
{
    private async Task<T> ExchangeAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) { request.Content = JsonContent.Create(body, options: ClassroomMessageJson.Options); }
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return await ClassroomMessageJson.ReadAsync<T>(response, 1024 * 1024, cancellationToken).ConfigureAwait(false);
    }
}
