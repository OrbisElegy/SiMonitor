// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Application.Classroom;

namespace Monitor.Infrastructure.Classroom;

// The teacher's saved exams in one local JSON file, replaced atomically.
public sealed class ExamLibraryStore(string path)
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public const int MaximumExams = 200;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    private sealed record Document(int Version, IReadOnlyList<ExamDefinition> Exams);

    // A missing file is an empty library; an unreadable or invalid one is reported.
    public IReadOnlyList<ExamDefinition> Load(out bool rejected)
    {
        rejected = false;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { return []; }
            if (info.Length > MaximumBytes) { throw new ArgumentException("ExamLibrary.TooLarge"); }
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(path), Options);
            if (document is not { Version: 1, Exams: { } exams } || exams.Count > MaximumExams) { throw new ArgumentException("ExamLibrary.Invalid"); }
            foreach (var exam in exams) { exam.Validate(); }
            return exams;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            rejected = true;
            return [];
        }
    }

    public bool Save(IReadOnlyList<ExamDefinition> exams)
    {
        ArgumentNullException.ThrowIfNull(exams);
        if (exams.Count > MaximumExams) { throw new ArgumentException("ExamLibrary.TooMany", nameof(exams)); }
        foreach (var exam in exams) { exam.Validate(); }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, exams), Options);
        if (bytes.Length > MaximumBytes) { throw new ArgumentException("ExamLibrary.TooLarge", nameof(exams)); }
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, fullPath, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
