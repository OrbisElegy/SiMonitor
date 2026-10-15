// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace Monitor.Desktop;

// A self-contained, data-only skin. The host owns all controls and commands.
internal sealed class MonitorSkinPackage
{
    internal const int MaximumBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
        WriteIndented = true
    };
    private sealed record Document
    {
        public required int Version { get; init; }
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Layout { get; init; }
        public required string Background { get; init; }
        public required string[] ChannelColors { get; init; }
        public required string WarningColor { get; init; }
        public required string NibpColor { get; init; }
    }
    private readonly Document _document;
    private readonly IBrush[] _channels;
    private MonitorSkinPackage(Document document)
    {
        _document = document;
        Background = Brush.Parse(document.Background);
        WarningBrush = Brush.Parse(document.WarningColor);
        NibpBrush = Brush.Parse(document.NibpColor);
        _channels = document.ChannelColors.Select(Brush.Parse).ToArray();
    }
    internal string Id => _document.Id;
    internal string Name => _document.Name;
    internal bool Bedside => _document.Layout == "bedside";
    internal IBrush Background { get; }
    internal IBrush WarningBrush { get; }
    internal IBrush NibpBrush { get; }
    internal IBrush ChannelBrush(int channel) => _channels[channel];
    internal bool Matches(MonitorSkinPackage other) => Id == other.Id && Name == other.Name &&
        _document.Layout == other._document.Layout && _document.Background == other._document.Background &&
        _document.WarningColor == other._document.WarningColor && _document.NibpColor == other._document.NibpColor &&
        _document.ChannelColors.SequenceEqual(other._document.ChannelColors);
    internal static MonitorSkinPackage Generic { get; } = BuiltIn("generic");
    internal static MonitorSkinPackage BedsideMonitor { get; } = BuiltIn("epm12");

    private static MonitorSkinPackage BuiltIn(string id)
    {
        using var stream = typeof(MonitorSkinPackage).Assembly.GetManifestResourceStream("Monitor.Skins." + id + ".simskin")
            ?? throw new InvalidOperationException("Skin.MissingBuiltIn");
        return Read(stream);
    }

    internal static MonitorSkinPackage Read(Stream stream)
    {
        byte[] buffer = new byte[MaximumBytes + 1];
        int count = 0, read;
        while (count < buffer.Length && (read = stream.Read(buffer, count, buffer.Length - count)) != 0) { count += read; }
        if (count > MaximumBytes) { throw new ArgumentException("Skin.TooLarge"); }
        var bytes = buffer.AsMemory(0, count);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            json.RootElement.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
        { throw new ArgumentException("Skin.InvalidDocument"); }
        var document = JsonSerializer.Deserialize<Document>(bytes.Span, Options);
        static bool Color(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);
        if (document is null || document.Version != 1 || document.Id is null ||
            !Regex.IsMatch(document.Id, "^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant) ||
            string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 64 || document.Name.Any(char.IsControl) ||
            document.Layout is not ("generic" or "bedside") || !Color(document.Background) ||
            !Color(document.WarningColor) || !Color(document.NibpColor) ||
            document.ChannelColors is not { Length: 7 } || !document.ChannelColors.All(Color))
        { throw new ArgumentException("Skin.InvalidDocument"); }
        return new(document);
    }

    internal void Write(Stream stream) => JsonSerializer.Serialize(stream, _document, Options);

    internal bool Save(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Write(stream);
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
