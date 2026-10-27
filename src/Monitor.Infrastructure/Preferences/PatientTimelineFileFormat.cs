// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Application.Presentation;

namespace Monitor.Infrastructure.Preferences;

public static class PatientTimelineFileFormat
{
    public const int MaximumBytes = 1_048_576;
    private const string Format = "simonitor.patient-timeline";
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        IgnoreReadOnlyProperties = true,
        MaxDepth = 16,
        WriteIndented = true
    };
    private sealed record Document(string Format, int Version, PatientTimelineFile Timeline);

    public static PatientTimelineFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] bytes = new byte[MaximumBytes + 1];
        int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count > MaximumBytes) { throw new ArgumentException("PatientTimeline.FileTooLarge"); }
        var document = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, count), Options);
        if (document is null || document.Format != Format || document.Version != 1 || document.Timeline is null)
        { throw new ArgumentException("PatientTimeline.InvalidFile"); }
        return document.Timeline.Snapshot();
    }

    public static byte[] Write(PatientTimelineFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(Format, 1, file.Snapshot()), Options);
        if (bytes.Length > MaximumBytes) { throw new ArgumentException("PatientTimeline.FileTooLarge"); }
        return bytes;
    }
}
