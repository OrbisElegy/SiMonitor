// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace Monitor.Desktop;

internal sealed record StylePreviewData((long TimeNs, double Value)[] Ecg, (long TimeNs, double Value)[] Abp)
{
    internal (long TimeNs, double Value)[] Samples(int channel) => channel switch
    { 0 => Ecg, 3 => Abp, _ => throw new ArgumentOutOfRangeException(nameof(channel)) };
}

// Build-generated finite sample catalog. UI loading never invokes simulation.
internal sealed class StylePreviewCatalog
{
    internal const int CombinationCount = 864;
    internal static long DurationNs(int ecg) => ecg is >= 66 and <= 71 ? 12_000_000_000 : ecg is 48 or 49 ? 12_800_000_000 : ecg == 50 ? 8_000_000_000 : ecg is 51 or 52 ? 6_400_000_000 : ecg == 44 ? 3_200_000_000 : ecg == 45 ? 4_800_000_000 : ecg == 46 ? 8_000_000_000 : ecg == 47 ? 6_000_000_000 : ecg is 4 or 40 or 41 ? 6_200_000_000 : ecg is 5 or 42 or 43 ? 6_400_000_000 : ecg == 39 ? 3_600_000_000 : ecg is 27 or 28 or 34 or 35 ? 12_000_000_000 : ecg is >= 10 and <= 19 ? 5_000_000_000 : 3_000_000_000;
    private const string FileName = "style-previews.bin";
    private readonly Dictionary<(int, int, int), StylePreviewData> _styles = [];
    private readonly (long TimeNs, double Value)[][] _respiration = new (long, double)[4][];
    private static readonly Lazy<StylePreviewCatalog> Current = new(() => Load(Path.Combine(AppContext.BaseDirectory, FileName)));
    internal static StylePreviewData Get(int ecg, int respiration, int ejection) =>
        Current.Value._styles.TryGetValue((ecg, respiration, ejection), out var data) ? data : throw new ArgumentException("Preview.IncompatibleStyle");
    internal static (long TimeNs, double Value)[] Respiration(int index) =>
        index is >= 0 and < 4 ? Current.Value._respiration[index] : throw new ArgumentOutOfRangeException(nameof(index));
    private static StylePreviewCatalog Load(string path)
    {
        using var file = File.OpenRead(path); using var reader = new BinaryReader(file, Encoding.UTF8);
        if (reader.ReadInt32() != 0x31565053 || reader.ReadInt32() != CombinationCount) { throw new InvalidDataException("Preview.InvalidCatalog"); }
        var catalog = new StylePreviewCatalog();
        (long, double)[] ReadSamples()
        {
            int count = reader.ReadInt32();
            if (count is < 2 or > 12000) { throw new InvalidDataException("Preview.InvalidCount"); }
            var samples = new (long Time, double Value)[count];
            for (int i = 0; i < count; i++)
            {
                samples[i] = (reader.ReadInt64(), reader.ReadDouble());
                if (!double.IsFinite(samples[i].Value) || samples[i].Time < 0 || i > 0 && samples[i].Time <= samples[i - 1].Time)
                { throw new InvalidDataException("Preview.InvalidSamples"); }
            }
            return samples;
        }
        for (int i = 0; i < CombinationCount; i++)
        {
            var key = (reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            if ((key.Item1 < 0 || key.Item1 >= DesignPreviewSettings.EcgChoiceCount) || key.Item2 is < 0 or > 3 || key.Item3 is < 0 or > 3 ||
                !catalog._styles.TryAdd(key, new(ReadSamples(), ReadSamples())))
            { throw new InvalidDataException("Preview.InvalidKey"); }
        }
        for (int i = 0; i < 4; i++) { catalog._respiration[i] = ReadSamples(); }
        if (file.Position != file.Length) { throw new InvalidDataException("Preview.TrailingData"); }
        return catalog;
    }
    // Invoked by MSBuild after compiling, before packaging. No window or
    // graphics platform is started; only the same authoring sources are used.
    internal static int Generate(string destination)
    {
        var keys = new List<(int Ecg, int Resp, int Ejection)>();
        for (int ecg = 0; ecg < DesignPreviewSettings.EcgChoiceCount; ecg++)
            for (int resp = 0; resp < 4; resp++)
                for (int ejection = 0; ejection < 4; ejection++)
                {
                    try { DesignPreviewWindow.ResolveStyle(ecg, resp, ejection); keys.Add((ecg, resp, ejection)); }
                    catch (ArgumentException) { /* Intentionally unavailable style combination. */ }
                }
        var data = new StylePreviewData[keys.Count];
        Parallel.For(0, keys.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(32, Environment.ProcessorCount) }, i =>
        {
            var key = keys[i]; var source = DesignPreviewWindow.CreateStylePreview(key.Ecg, key.Resp, key.Ejection);
            data[i] = new(source.Samples(0, source.FrontierNs - DurationNs(key.Ecg), source.FrontierNs).ToArray(),
                source.Samples(3, source.FrontierNs - DurationNs(key.Ecg), source.FrontierNs).ToArray());
        });
        string temporary = destination + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
        {
            writer.Write(0x31565053); writer.Write(keys.Count);
            void WriteSamples((long TimeNs, double Value)[] samples)
            {
                writer.Write(samples.Length);
                foreach (var sample in samples) { writer.Write(sample.TimeNs); writer.Write(sample.Value); }
            }
            for (int i = 0; i < keys.Count; i++)
            {
                writer.Write(keys[i].Ecg); writer.Write(keys[i].Resp); writer.Write(keys[i].Ejection);
                WriteSamples(data[i].Ecg); WriteSamples(data[i].Abp);
            }
            for (int i = 0; i < 4; i++) { WriteSamples(DesignPreviewWindow.CreateRespirationPreview(i)); }
        }
        _ = Load(temporary); // Refuse incomplete/malformed output before publishing.
        File.Move(temporary, destination, true);
        Console.WriteLine($"Generated {keys.Count} style combinations and4 full respiration previews: {destination}");
        return 0;
    }
}
