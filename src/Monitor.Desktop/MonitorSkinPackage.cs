// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Monitor.Application.Therapy;

namespace Monitor.Desktop;

// A self-contained, data-only archive. The host owns all controls and commands.
internal sealed class MonitorSkinPackage
{
    internal const int MaximumBytes = 256 * 1024;
    internal const int MaximumExpandedBytes = 128 * 1024;
    internal const int MaximumManifestBytes = 16 * 1024;
    internal const int MaximumMaterialBytes = 4 * 1024;
    internal const int MaximumEntries = 33;
    private static readonly string[] DefibrillatorMaterials = ["shell", "key", "softkey", "charge", "shock", "selector"];
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        WriteIndented = true
    };
    private sealed record Document
    {
        public required int Version { get; init; }
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Layout { get; init; }
        public int? FixedWaveformRows { get; init; }
        public string? DeviceProfile { get; init; }
        public required int[] DefaultEnergyStepsJoules { get; init; }
        public required string Background { get; init; }
        public required string[] ChannelColors { get; init; }
        public required string WarningColor { get; init; }
        public required string NibpColor { get; init; }
        public required Dictionary<string, string> Materials { get; init; }
    }
    private sealed record MaterialDocument
    {
        public required int Version { get; init; }
        public required string Fill { get; init; }
        public required string PressedFill { get; init; }
        public required string Border { get; init; }
        public required string Foreground { get; init; }
        public required double CornerRadius { get; init; }
        public required double BorderThickness { get; init; }
    }
    private readonly Document _document;
    private readonly Dictionary<string, byte[]> _assets;
    private readonly Dictionary<string, MonitorSkinMaterial> _materials;
    private readonly IBrush[] _channels;
    private MonitorSkinPackage(Document document, Dictionary<string, byte[]> assets, Dictionary<string, MonitorSkinMaterial> materials)
    {
        _document = document;
        _assets = assets;
        _materials = materials;
        DeviceProfile = MonitorDeviceProfiles.Resolve(document.DeviceProfile);
        DefaultEnergyStepsJoules = (DefibrillatorConfiguration.Default with { EnergyStepsJoules = document.DefaultEnergyStepsJoules })
            .Snapshot().EnergyStepsJoules;
        Background = Brush.Parse(document.Background);
        WarningBrush = Brush.Parse(document.WarningColor);
        NibpBrush = Brush.Parse(document.NibpColor);
        _channels = document.ChannelColors.Select(Brush.Parse).ToArray();
    }
    internal string Id => _document.Id;
    internal string Name => _document.Name;
    internal string Layout => _document.Layout;
    internal bool Bedside => Layout == "bedside";
    internal bool Defibrillator => Layout == "defibrillator";
    internal int? FixedWaveformRows => _document.FixedWaveformRows;
    internal MonitorDeviceProfile? DeviceProfile { get; }
    internal IReadOnlyList<int> DefaultEnergyStepsJoules { get; }
    internal IBrush Background { get; }
    internal IBrush WarningBrush { get; }
    internal IBrush NibpBrush { get; }
    internal IBrush ChannelBrush(int channel) => _channels[channel];
    internal MonitorSkinMaterial Material(string key) => _materials[key];
    internal bool Matches(MonitorSkinPackage other) => Id == other.Id && Name == other.Name &&
        Layout == other.Layout && FixedWaveformRows == other.FixedWaveformRows && _document.DeviceProfile == other._document.DeviceProfile &&
        DefaultEnergyStepsJoules.SequenceEqual(other.DefaultEnergyStepsJoules) &&
        _document.Background == other._document.Background && _document.WarningColor == other._document.WarningColor &&
        _document.NibpColor == other._document.NibpColor && _document.ChannelColors.SequenceEqual(other._document.ChannelColors) &&
        _document.Materials.Count == other._document.Materials.Count &&
        _document.Materials.All(pair => other._document.Materials.TryGetValue(pair.Key, out string? path) && path == pair.Value) &&
        _assets.Count == other._assets.Count &&
        _assets.All(pair => other._assets.TryGetValue(pair.Key, out byte[]? bytes) && pair.Value.AsSpan().SequenceEqual(bytes));
    internal static MonitorSkinPackage Generic { get; } = BuiltIn("generic");
    internal static MonitorSkinPackage BedsideMonitor { get; } = BuiltIn("epm12");
    internal static MonitorSkinPackage DefibrillatorEnclosure { get; } = BuiltIn("d3");

    private static MonitorSkinPackage BuiltIn(string id)
    {
        var assembly = typeof(MonitorSkinPackage).Assembly;
        string prefix = "Monitor.Skins/" + id + "/";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in assembly.GetManifestResourceNames())
        {
            string normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
            string path = normalized[prefix.Length..];
            ValidatePath(path);
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Skin.MissingBuiltIn");
            files.Add(path, ReadBounded(stream, EntryLimit(path)));
        }
        return Create(files);
    }

    internal static MonitorSkinPackage Read(Stream stream)
    {
        byte[] bytes = ReadBounded(stream, MaximumBytes);
        try
        {
            using var source = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(source, ZipArchiveMode.Read);
            if (archive.Entries.Count is < 1 or > MaximumEntries) { throw new ArgumentException("Skin.InvalidEntryCount"); }
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                string path = entry.FullName;
                ValidatePath(path);
                int type = (entry.ExternalAttributes >> 16) & 0xF000;
                if (!paths.Add(path) || type is not (0 or 0x8000)) { throw new ArgumentException("Skin.InvalidEntry"); }
                int limit = EntryLimit(path);
                expandedBytes += entry.Length;
                if (entry.Length > limit || expandedBytes > MaximumExpandedBytes ||
                    (entry.Length > 1024 && (entry.CompressedLength == 0 || entry.Length > entry.CompressedLength * 100)))
                { throw new ArgumentException("Skin.TooLarge"); }
                using var content = entry.Open();
                byte[] data = ReadBounded(content, limit);
                if (data.LongLength != entry.Length) { throw new ArgumentException("Skin.InvalidEntry"); }
                files.Add(path, data);
            }
            return Create(files);
        }
        catch (InvalidDataException error) { throw new ArgumentException("Skin.InvalidArchive", error); }
    }

    private static byte[] ReadBounded(Stream stream, int limit)
    {
        byte[] buffer = new byte[limit + 1];
        int count = 0, read;
        while (count < buffer.Length && (read = stream.Read(buffer, count, buffer.Length - count)) != 0) { count += read; }
        if (count > limit) { throw new ArgumentException("Skin.TooLarge"); }
        return buffer[..count];
    }

    private static int EntryLimit(string path) => path == "manifest.json" ? MaximumManifestBytes : MaximumMaterialBytes;

    private static void ValidatePath(string path)
    {
        if (path.Length > 160) { throw new ArgumentException("Skin.InvalidPath"); }
        if (path != "manifest.json" && !Regex.IsMatch(path, "^assets/(?:[a-z][a-z0-9-]*/)*[a-z][a-z0-9-]*\\.material\\.json\\z", RegexOptions.CultureInvariant))
        { throw new ArgumentException("Skin.InvalidPath"); }
    }

    private static T ReadJson<T>(byte[] bytes) where T : class
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        static void CheckDuplicates(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) { throw new ArgumentException("Skin.DuplicateProperty"); }
                    CheckDuplicates(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) { CheckDuplicates(item); }
            }
        }
        if (json.RootElement.ValueKind != JsonValueKind.Object) { throw new ArgumentException("Skin.InvalidDocument"); }
        CheckDuplicates(json.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, Options) ?? throw new ArgumentException("Skin.InvalidDocument");
    }

    private static bool Color(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}\\z", RegexOptions.CultureInvariant);

    private static MonitorSkinPackage Create(Dictionary<string, byte[]> files)
    {
        if (files.Count > MaximumEntries || files.Values.Sum(bytes => bytes.Length) > MaximumExpandedBytes ||
            !files.Remove("manifest.json", out byte[]? manifest)) { throw new ArgumentException("Skin.InvalidDocument"); }
        var document = ReadJson<Document>(manifest);
        if (document.Version != 2 || document.Id is null ||
            !Regex.IsMatch(document.Id, "^[a-z][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant) ||
            string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 64 || document.Name.Any(char.IsControl) ||
            document.Layout is not ("generic" or "bedside" or "defibrillator") ||
            document.FixedWaveformRows is not (null or 3 or 4 or 5 or 7) ||
            (document.Layout == "defibrillator" && (document.FixedWaveformRows is null || document.DeviceProfile is null)) ||
            !Color(document.Background) || !Color(document.WarningColor) || !Color(document.NibpColor) ||
            document.ChannelColors is not { Length: 7 } || !document.ChannelColors.All(Color) ||
            document.Materials is null || document.Materials.Count >= MaximumEntries ||
            (document.Layout == "defibrillator" && !DefibrillatorMaterials.All(document.Materials.ContainsKey)))
        { throw new ArgumentException("Skin.InvalidDocument"); }
        var materials = new Dictionary<string, MonitorSkinMaterial>(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in document.Materials)
        {
            if (!Regex.IsMatch(pair.Key, "^[a-z][a-z0-9-]{0,31}\\z", RegexOptions.CultureInvariant) ||
                pair.Value is null || !files.TryGetValue(pair.Value, out byte[]? data))
            { throw new ArgumentException("Skin.InvalidMaterialReference"); }
            var material = ReadJson<MaterialDocument>(data);
            if (material.Version != 1 || !Color(material.Fill) || !Color(material.PressedFill) || !Color(material.Border) ||
                !Color(material.Foreground) || !double.IsFinite(material.CornerRadius) || material.CornerRadius is < 0 or > 80 ||
                !double.IsFinite(material.BorderThickness) || material.BorderThickness is < 0 or > 8)
            { throw new ArgumentException("Skin.InvalidMaterial"); }
            materials.Add(pair.Key, new(Brush.Parse(material.Fill), Brush.Parse(material.PressedFill), Brush.Parse(material.Border),
                Brush.Parse(material.Foreground), material.CornerRadius, material.BorderThickness));
            referenced.Add(pair.Value);
        }
        if (files.Keys.Any(path => !referenced.Contains(path))) { throw new ArgumentException("Skin.UnreferencedAsset"); }
        int canonicalManifestBytes = JsonSerializer.SerializeToUtf8Bytes(document, Options).Length;
        if (canonicalManifestBytes > MaximumManifestBytes || files.Values.Sum(bytes => bytes.Length) + canonicalManifestBytes > MaximumExpandedBytes)
        { throw new ArgumentException("Skin.TooLarge"); }
        return new(document, files, materials);
    }

    internal void Write(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        static void Add(ZipArchive archive, string path, byte[] bytes)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var output = entry.Open();
            output.Write(bytes);
        }
        Add(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(_document, Options));
        foreach (var asset in _assets.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { Add(archive, asset.Key, asset.Value); }
    }

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

internal sealed record MonitorSkinMaterial(IBrush Fill, IBrush PressedFill, IBrush Border, IBrush Foreground, double CornerRadius, double BorderThickness);
