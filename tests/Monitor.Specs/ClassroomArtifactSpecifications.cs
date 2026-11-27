// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Monitor.Network;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class ClassroomArtifactSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ArtifactRangeResumeVerifiesTheWholeObject), ArtifactRangeResumeVerifiesTheWholeObject),
        new(nameof(StudyRecordReleaseRemovesUnassignedChannels), StudyRecordReleaseRemovesUnassignedChannels)
    ];

    internal static async Task ExerciseTransportAsync(ClassroomHost host, ClassroomStudentClient first, ClassroomStudentClient second,
        ClassroomManifest firstManifest, CancellationToken ct)
    {
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jKe0AAAAASUVORK5CYII=");
        ClassroomArtifactDescriptor image = host.PublishLearnerImage(png, firstManifest.StudentId);
        byte[] original = (byte[])png.Clone();
        png[^1] ^= 1;
        Check.That((await first.ListArtifactsAsync(ct).ConfigureAwait(false)).Contains(image) &&
            !(await second.ListArtifactsAsync(ct).ConfigureAwait(false)).Contains(image), "catalog respects individual grants");
        Check.That((await first.DownloadArtifactAsync(image, ct).ConfigureAwait(false)).SequenceEqual(original), "published image is immutable");
        await ExpectNotFoundAsync(second, image, ct).ConfigureAwait(false);
        ClassroomArtifactDescriptor study = host.PublishStudyRecord(Record(firstManifest.SessionId, firstManifest.InstanceId));
        WaveformRecordArchive received = await second.DownloadStudyRecordAsync(study, ct).ConfigureAwait(false);
        Check.That(received.BlockCount == 1 && received.RecordRef == "record:" + study.Sha256, "students can read shared captured evidence");
        Check.That(host.RemoveArtifact(study.Sha256), "teacher withdraws released copy");
        await ExpectNotFoundAsync(first, study, ct).ConfigureAwait(false);
    }

    private static async Task ExpectNotFoundAsync(ClassroomStudentClient student, ClassroomArtifactDescriptor descriptor, CancellationToken ct)
    {
        try { await student.DownloadArtifactAsync(descriptor, ct).ConfigureAwait(false); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { return; }
        throw new InvalidOperationException("An unassigned or withdrawn artifact must remain inaccessible by hash.");
    }

    private static void ArtifactRangeResumeVerifiesTheWholeObject()
    {
        byte[] data = "verified captured content"u8.ToArray();
        var descriptor = new ClassroomArtifactDescriptor(Convert.ToHexStringLower(SHA256.HashData(data)), data.Length,
            ClassroomArtifactDescriptor.ImageMediaType);
        using var handler = new DroppedTransfer(data, descriptor);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        byte[] received = ClassroomStudentClient.ReadArtifactAsync(http, descriptor, CancellationToken.None).GetAwaiter().GetResult();
        Check.That(received.SequenceEqual(data) && handler.Attempts == 2, "resume starts at the first missing byte");
        byte[] corrupt = (byte[])data.Clone();
        corrupt[0] ^= 1;
        using var corruptHandler = new DroppedTransfer(corrupt, descriptor);
        using var corruptHttp = new HttpClient(corruptHandler) { BaseAddress = new Uri("https://localhost/") };
        try { ClassroomStudentClient.ReadArtifactAsync(corruptHttp, descriptor, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (InvalidDataException)
        {
            Check.That(corruptHandler.Attempts == 2, "corrupt content is never retried as a transient disconnect");
            return;
        }
        throw new InvalidOperationException("The client must reject a mismatched content digest.");
    }

    private static void StudyRecordReleaseRemovesUnassignedChannels()
    {
        var sessionId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        WaveformRecordArchive source = Record(sessionId, instanceId);
        byte[] wire = ClassroomArtifactRecord.Encode(source);
        Check.That(wire.AsSpan().IndexOf("teacher-only"u8) < 0, "a private authoring identifier cannot be included");
        var descriptor = new ClassroomArtifactDescriptor(Convert.ToHexStringLower(SHA256.HashData(wire)), wire.Length,
            ClassroomArtifactDescriptor.StudyRecordMediaType);
        WaveformRecordArchive result = ClassroomArtifactRecord.Decode(wire, descriptor, sessionId, instanceId);
        WaveformEnvelope released = WaveformEnvelopeCodec.Decode(result.ReadBlocks()[0].RawEnvelope);
        Check.That(released.Planes.Count == 12 && result.CapturePlan().ChannelIds.SequenceEqual(source.CapturePlan().ChannelIds),
            "release retains only assigned lead samples in the original clinical order");
        try { ClassroomArtifactRecord.Decode(wire, descriptor, Guid.NewGuid(), instanceId); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Cross-classroom records cannot be consumed.");
    }

    private static WaveformRecordArchive Record(Guid sessionId, Guid instanceId)
    {
        Guid[] channels = Enumerable.Range(0, 13).Select(_ => Guid.NewGuid()).ToArray();
        WaveformPlane[] planes = channels.Select((id, index) => new WaveformPlane(id, 5, 1, 0, 1, 1, 0, 1,
            WaveformQualityEncoding.None, [(short)index], [])).ToArray();
        var envelope = new WaveformEnvelope(sessionId, instanceId, 1, 1, 0, 1, 0, WaveformBlockAssembler.BlockDurationNs, planes);
        var plan = new WaveformRecordArchivePlan("teacher-only:course", "teacher-only:answer", 2, sessionId, instanceId, 1,
            0, 1, 1, 0, WaveformBlockAssembler.BlockDurationNs, channels[..12]);
        return WaveformRecordArchive.Create(plan, [WaveformEnvelopeCodec.EncodeRaw(envelope)]);
    }

    private sealed class DroppedTransfer(byte[] data, ClassroomArtifactDescriptor descriptor) : HttpMessageHandler
    {
        internal int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool resume = Attempts++ != 0;
            if (resume)
            {
                Check.That(request.Headers.Range?.Ranges.Single().From == 7 && request.Headers.IfRange?.EntityTag?.Tag == "\"" + descriptor.Sha256 + "\"",
                    "resumed HTTP request must bind the range to the immutable digest");
            }
            var response = new HttpResponseMessage(resume ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(resume ? data[7..] : data[..7]))
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(descriptor.MediaType);
            response.Content.Headers.ContentLength = resume ? data.Length - 7 : data.Length;
            response.Headers.ETag = new EntityTagHeaderValue("\"" + descriptor.Sha256 + "\"");
            if (resume) { response.Content.Headers.ContentRange = new ContentRangeHeaderValue(7, data.Length - 1, data.Length); }
            return Task.FromResult(response);
        }
    }
}
