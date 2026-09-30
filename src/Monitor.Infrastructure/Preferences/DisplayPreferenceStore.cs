// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Application.Presentation;

namespace Monitor.Infrastructure.Preferences;

public sealed record DisplayPreferences(MonitorDisplayConfiguration Display, int PaperLayout);

// Local presentation preferences only: no patient data, waveform state or audio opt-in.
public sealed class DisplayPreferenceStore(string path)
{
    private const int MaximumBytes = 32768;
    private static readonly JsonSerializerOptions Options = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8, WriteIndented = true };
    private sealed record Slot([property: JsonRequired] int Channel, [property: JsonRequired] bool Automatic,
        [property: JsonRequired] double Minimum, [property: JsonRequired] double Maximum, [property: JsonRequired] int Speed);
    private sealed class Document
    {
        public required int Version { get; init; }
        public required MonitorSkin Skin { get; init; }
        public required Slot[] Slots { get; init; }
        public required int PaperLayout { get; init; }
    }
    public DisplayPreferences Load(out bool rejected)
    {
        rejected = false;
        try
        {
            using var stream = File.OpenRead(path);
            byte[] bytes = new byte[MaximumBytes + 1];
            int count = 0, read;
            while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) != 0) { count += read; }
            if (count > MaximumBytes) { throw new ArgumentException("Preferences.TooLarge"); }
            var data = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, count), Options);
            if (data is null || data.Version != 1 || data.PaperLayout is < 0 or > 1 || data.Slots is null || data.Slots.Any(s => s is null))
            { throw new ArgumentException("Preferences.InvalidDocument"); }
            return new(new(data.Skin, data.Slots.Select(s => new MonitorDisplaySlot(s.Channel, s.Automatic,
                new(s.Minimum, s.Maximum), s.Speed)).ToArray()), data.PaperLayout);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { rejected = true; }
        return new(MonitorDisplayConfiguration.Default(), 0);
    }
    public bool Save(DisplayPreferences preferences)
    {
        if (preferences.PaperLayout is < 0 or > 1) { throw new ArgumentException("Preferences.InvalidPaperLayout"); }
        var data = new Document
        {
            Version = 1,
            Skin = preferences.Display.Skin,
            PaperLayout = preferences.PaperLayout,
            Slots = preferences.Display.Slots.Select(s => new Slot(s.Channel, s.Automatic, s.Range.Minimum, s.Range.Maximum, s.SpeedTenthsMmPerSecond)).ToArray()
        };
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, Options);
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
