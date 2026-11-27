// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed partial class ClassroomStudentClient
{
    public async Task<IReadOnlyList<ClassroomArtifactDescriptor>> ListArtifactsAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using HttpResponseMessage response = await _http.GetAsync("v1/artifacts", HttpCompletionOption.ResponseHeadersRead,
            deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(32 * 1024, deadline.Token).ConfigureAwait(false);
        ClassroomArtifactDescriptor[] descriptors = await ReadControlAsync<ClassroomArtifactDescriptor[]>(response, deadline.Token)
            .ConfigureAwait(false);
        if (descriptors.Length > ClassroomArtifactDescriptor.MaximumEntries || descriptors.Any(item => item is null || !item.IsValid) ||
            descriptors.DistinctBy(item => item.Sha256, StringComparer.Ordinal).Count() != descriptors.Length)
        {
            throw new InvalidDataException("Invalid classroom artifact catalog.");
        }
        return descriptors;
    }

    public Task<byte[]> DownloadArtifactAsync(ClassroomArtifactDescriptor descriptor, CancellationToken cancellationToken = default) =>
        ReadArtifactAsync(_http, descriptor, cancellationToken);

    public async Task<WaveformRecordArchive> DownloadStudyRecordAsync(ClassroomArtifactDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.MediaType != ClassroomArtifactDescriptor.StudyRecordMediaType)
        {
            throw new ArgumentException("Expected a captured waveform record.", nameof(descriptor));
        }
        ClassroomManifest manifest = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        byte[] bytes = await DownloadArtifactAsync(descriptor, cancellationToken).ConfigureAwait(false);
        return ClassroomArtifactRecord.Decode(bytes, descriptor, manifest.SessionId, manifest.InstanceId);
    }

    internal static async Task<byte[]> ReadArtifactAsync(HttpClient http, ClassroomArtifactDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.IsValid) { throw new ArgumentException("Invalid artifact reference.", nameof(descriptor)); }
        byte[] bytes = new byte[descriptor.LengthBytes];
        int received = 0;
        var expectedTag = new EntityTagHeaderValue("\"" + descriptor.Sha256 + "\"");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        for (int attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "v1/artifacts/" + descriptor.Sha256);
            if (received > 0)
            {
                request.Headers.Range = new RangeHeaderValue(received, null);
                request.Headers.IfRange = new RangeConditionHeaderValue(expectedTag);
            }
            try
            {
                using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                HttpContentHeaders headers = response.Content.Headers;
                ContentRangeHeaderValue? range = headers.ContentRange;
                bool matchesRange = received == 0 ? response.StatusCode == HttpStatusCode.OK && range is null :
                    response.StatusCode == HttpStatusCode.PartialContent && range is { Unit: "bytes" } && range.From == received &&
                    range.To == bytes.Length - 1 && range.Length == bytes.Length;
                if (!matchesRange || headers.ContentLength != bytes.Length - received || !expectedTag.Equals(response.Headers.ETag) ||
                    headers.ContentType?.MediaType != descriptor.MediaType || headers.ContentEncoding.Count != 0)
                {
                    throw new InvalidDataException("Classroom artifact metadata changed during transfer.");
                }
                using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                while (received < bytes.Length)
                {
                    int count = await stream.ReadAsync(bytes.AsMemory(received), deadline.Token).ConfigureAwait(false);
                    if (count == 0) { throw new EndOfStreamException("Incomplete classroom artifact."); }
                    received += count;
                }
                break;
            }
            catch (Exception error) when (attempt < 2 && !deadline.IsCancellationRequested &&
                (error is IOException and not System.Net.Http.HttpIOException ||
                 error is HttpIOException { HttpRequestError: HttpRequestError.ResponseEnded or HttpRequestError.ConnectionError } ||
                 error is HttpRequestException { StatusCode: null, HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded } ||
                 error is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable }))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), deadline.Token).ConfigureAwait(false);
            }
        }
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != descriptor.Sha256)
        {
            throw new InvalidDataException("Classroom artifact SHA-256 verification failed.");
        }
        return bytes;
    }
}
