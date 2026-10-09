// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Application.Presentation;

namespace Monitor.Infrastructure.Preferences;

// Output drafts only; never persists a running or armed treatment state.
public sealed class MonitorTherapyPreferenceStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true
    };

    private sealed record Document(int Version, MonitorTherapyPreferences Settings);

    public MonitorTherapyPreferences Load(out bool rejected)
    {
        rejected = false;
        try
        {
            using var stream = File.OpenRead(path);
            byte[] bytes = new byte[1025];
            int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > 1024) { throw new ArgumentException("TherapyPreferences.TooLarge"); }
            var data = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, count), Options);
            if (data is null || data.Version != 1 || data.Settings is null)
            { throw new ArgumentException("TherapyPreferences.InvalidDocument"); }
            data.Settings.Validate();
            return data.Settings;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { rejected = true; }
        return MonitorTherapyPreferences.Default;
    }

    public bool Save(MonitorTherapyPreferences settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Document(1, settings), Options);
                stream.Flush(flushToDisk: true);
            }
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
