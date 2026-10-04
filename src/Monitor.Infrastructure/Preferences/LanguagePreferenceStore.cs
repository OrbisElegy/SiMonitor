// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Infrastructure.Localization;

namespace Monitor.Infrastructure.Preferences;

// Separate from simulation preferences so selecting a language never saves unapplied editors.
public sealed class LanguagePreferenceStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true
    };

    private sealed record Document(int Version, string Locale);

    public string Load(out bool rejected)
    {
        rejected = false;
        try
        {
            using var stream = File.OpenRead(path);
            byte[] bytes = new byte[1025];
            int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > 1024) { throw new ArgumentException("LanguagePreferences.TooLarge"); }
            var data = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, count), Options);
            if (data is null || data.Version != 1 || !IsSupported(data.Locale))
            { throw new ArgumentException("LanguagePreferences.InvalidDocument"); }
            return data.Locale;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { rejected = true; }
        return BuiltInLocalizations.DefaultLocale;
    }

    public bool Save(string locale)
    {
        if (!IsSupported(locale)) { throw new ArgumentException("LanguagePreferences.UnsupportedLocale", nameof(locale)); }
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Document(1, locale), Options);
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

    private static bool IsSupported(string? locale) => BuiltInLocalizations.Supported.Any(item => item.Code == locale);
}
