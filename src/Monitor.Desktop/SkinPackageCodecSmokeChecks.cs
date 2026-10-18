// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monitor.Application.Therapy;
using Monitor.Simulation.Therapy;

namespace Monitor.Desktop;

internal static class SkinPackageCodecSmokeChecks
{
    private static readonly int[] ExpectedEnergyStepsJoules = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 30, 50, 70, 100, 150, 170, 200, 300, 360];
    private static readonly int[] ExpectedAedEnergySequenceJoules = [200, 300, 360];
    private static readonly int[] CustomDefaultEnergyStepsJoules = [3, 17, 42, 1000];

    internal static void Verify()
    {
        foreach (var package in new[] { MonitorSkinPackage.Generic, MonitorSkinPackage.BedsideMonitor, MonitorSkinPackage.DefibrillatorEnclosure })
        {
            byte[] original = Export(package);
            using var input = new MemoryStream(original);
            var restored = MonitorSkinPackage.Read(input);
            Require(restored.Matches(package), "archive roundtrip retains metadata, layout and every material byte");
            Require(restored.DeviceProfile == package.DeviceProfile, "archive roundtrip retains the trusted device profile selection");
            Require(restored.SupportedManualVitals == package.SupportedManualVitals, "archive roundtrip retains manual vital capabilities");
            Require(restored.DefaultEnergyStepsJoules.SequenceEqual(package.DefaultEnergyStepsJoules), "archive roundtrip retains every default energy step");
            Require(original.AsSpan().SequenceEqual(Export(restored)), "canonical export is reproducible after import");
            Require(Enumerable.Range(0, 7).All(channel => restored.ChannelBrush(channel).ToString() == package.ChannelBrush(channel).ToString()),
                "archive roundtrip retains all seven channel brushes");
        }
        Require(MonitorSkinPackage.DefibrillatorEnclosure is { Layout: "defibrillator", Defibrillator: true, FixedWaveformRows: 3 },
            "the defibrillator enclosure declares its shell layout and locked three-row screen");
        var profile = MonitorDeviceProfiles.Resolve("biphasic-360");
        Require(MonitorSkinPackage.DefibrillatorEnclosure.DeviceProfile == profile &&
            profile is { DefaultManualEnergyJoules: 200, ExitSynchronizationAfterShock: true } &&
            profile.ManualConfiguration is
            {
                Waveform: DefibrillationWaveformKind.BiphasicTruncatedExponential,
                ChargeDurationMilliseconds: 7000, AutoDisarmSeconds: 60, EcgRecoveryMilliseconds: 2500
            } &&
            profile.AedConfiguration is
            {
                Waveform: DefibrillationWaveformKind.BiphasicTruncatedExponential,
                ChargeDurationMilliseconds: 7000, AutoDisarmSeconds: 30, EcgRecoveryMilliseconds: 2500
            } &&
            profile.ManualConfiguration.EnergyStepsJoules.SequenceEqual(ExpectedEnergyStepsJoules) &&
            profile.AedConfiguration.EnergyStepsJoules.SequenceEqual(ExpectedEnergyStepsJoules) &&
            profile.AedEnergySequenceJoules.SequenceEqual(ExpectedAedEnergySequenceJoules),
            "a declared device profile resolves to the trusted manual, AED and mode-transition parameters");
        Require(MonitorSkinPackage.Generic.DeviceProfile is null && MonitorSkinPackage.BedsideMonitor.DeviceProfile is null,
            "monitor-only built-ins do not impose a device profile");
        Require(MonitorSkinPackage.Generic.DefaultEnergyStepsJoules.SequenceEqual(DefibrillatorConfiguration.Default.EnergyStepsJoules) &&
            MonitorSkinPackage.BedsideMonitor.DefaultEnergyStepsJoules.SequenceEqual(DefibrillatorConfiguration.Default.EnergyStepsJoules) &&
            MonitorSkinPackage.DefibrillatorEnclosure.DefaultEnergyStepsJoules.SequenceEqual(ExpectedEnergyStepsJoules),
            "each built-in carries its complete default energy table in the package");
        Require(MonitorSkinPackage.DefibrillatorEnclosure.DefaultEnergyStepsJoules is IList<int> { IsReadOnly: true },
            "package energy tables expose an owned read-only snapshot");
        var source = Entries(Export(MonitorSkinPackage.DefibrillatorEnclosure));
        var generic = Entries(Export(MonitorSkinPackage.Generic));
        foreach (var package in new[] { MonitorSkinPackage.Generic, MonitorSkinPackage.BedsideMonitor, MonitorSkinPackage.DefibrillatorEnclosure })
        {
            var expected = package == MonitorSkinPackage.Generic ? MonitorManualVitals.All : MonitorManualVitals.Nibp;
            Require(package.SupportedManualVitals == expected && WithManualVitals(package, null).Matches(package),
                "built-ins and older packages inherit the supported manual displays of their layout");
        }
        var subset = WithManualVitals(MonitorSkinPackage.Generic, ["custom2", "temperature"]);
        Require(subset.SupportedManualVitals == (MonitorManualVitals.Custom2 | MonitorManualVitals.Temperature) &&
            !subset.Matches(MonitorSkinPackage.Generic) && subset.Matches(WithManualVitals(subset, ["temperature", "custom2"])),
            "capabilities are an order-independent subset included in package identity");
        using (var input = new MemoryStream(Export(subset)))
        { Require(MonitorSkinPackage.Read(input).Matches(subset), "custom capability subsets survive export"); }
        Require(WithManualVitals(MonitorSkinPackage.Generic, []).SupportedManualVitals == MonitorManualVitals.None,
            "an empty capability list disables every manual display");
        foreach (var invalid in new JsonNode?[]
        {
            JsonValue.Create("nibp"), JsonValue.Create(8), new JsonArray("unknown"),
            new JsonArray("Nibp"), new JsonArray("nibp", "nibp"), new JsonArray((JsonNode?)null), new JsonArray(1)
        })
        { Reject(Archive(Rewrite(generic, "manifest.json", json => json["supportedManualVitals"] = invalid)), "invalid manual display capabilities"); }
        using (var input = new MemoryStream(Archive(Rewrite(source, "manifest.json", json =>
            json["defaultEnergyStepsJoules"] = JsonSerializer.SerializeToNode(CustomDefaultEnergyStepsJoules)))))
        {
            var changed = MonitorSkinPackage.Read(input);
            Require(changed.DefaultEnergyStepsJoules.SequenceEqual(CustomDefaultEnergyStepsJoules) &&
                changed.DeviceProfile == MonitorSkinPackage.DefibrillatorEnclosure.DeviceProfile && !changed.Matches(MonitorSkinPackage.DefibrillatorEnclosure),
                "manifest energy steps override profile defaults and participate in package identity");
            using var exported = new MemoryStream(Export(changed));
            Require(MonitorSkinPackage.Read(exported).DefaultEnergyStepsJoules.SequenceEqual(CustomDefaultEnergyStepsJoules),
                "imported custom default tables survive export without being replaced by registry defaults");
        }
        var changedMaterial = Rewrite(source, "assets/shock.material.json", json => json["fill"] = "#123456");
        using (var input = new MemoryStream(Archive(changedMaterial)))
        {
            var changed = MonitorSkinPackage.Read(input);
            Require(!changed.Matches(MonitorSkinPackage.DefibrillatorEnclosure) && changed.Material("shock").Fill.ToString() == "#ff123456",
                "same-ID imports retain changed button material and do not match the built-in package");
        }
        using (var input = new MemoryStream(Archive(Rewrite(generic, "manifest.json", json => json["fixedWaveformRows"] = 3))))
        {
            Require(!MonitorSkinPackage.Read(input).Matches(MonitorSkinPackage.Generic), "layout locks participate in package identity");
        }
        using (var input = new MemoryStream(Archive(Rewrite(source, "manifest.json", json => json["fixedWaveformRows"] = 4))))
        {
            var changed = MonitorSkinPackage.Read(input);
            Require(changed.FixedWaveformRows == 4 && changed.DeviceProfile == MonitorSkinPackage.DefibrillatorEnclosure.DeviceProfile &&
                !changed.Matches(MonitorSkinPackage.DefibrillatorEnclosure), "fixed row counts remain a layout declaration independent of device profiles");
        }
        using (var input = new MemoryStream(Archive(Rewrite(generic, "manifest.json", json => json["deviceProfile"] = "biphasic-360"))))
        {
            var changed = MonitorSkinPackage.Read(input);
            Require(changed.DeviceProfile == MonitorSkinPackage.DefibrillatorEnclosure.DeviceProfile && !changed.Matches(MonitorSkinPackage.Generic),
                "device profile selection participates in package identity independently of its layout");
        }
        using (var input = new MemoryStream(Archive(Rewrite(generic, "manifest.json", json => json.Remove("deviceProfile")))))
        {
            Require(MonitorSkinPackage.Read(input).DeviceProfile is null, "an omitted optional device profile preserves ordinary monitor packages");
        }
        using (var input = new MemoryStream(Archive(Rewrite(source, "manifest.json", json =>
            ((JsonObject)json["materials"]!)["key"] = "assets/softkey.material.json")
            .Where(entry => entry.Path != "assets/key.material.json").ToList())))
        {
            Require(!MonitorSkinPackage.Read(input).Matches(MonitorSkinPackage.DefibrillatorEnclosure), "material references participate in package identity");
        }

        foreach (string text in new[] { "null", "{}", "[]", "{\"version\":1}" })
        { Reject(Encoding.UTF8.GetBytes(text), "v1 text and malformed archives"); }
        Reject(new byte[MonitorSkinPackage.MaximumBytes + 1], "compressed input limit");
        Reject(Archive([]), "missing manifest");
        Reject(Archive(source.Concat([source[0]]).ToList()), "duplicate archive path");
        Reject(Archive(source.Where(entry => entry.Path != "assets/shock.material.json").ToList()), "missing referenced asset");
        Reject(Archive(source.Append(("assets/unused.material.json", source[1].Bytes)).ToList()), "unreferenced asset");
        foreach (string path in new[]
        {
            "../manifest.json", "/manifest.json", "C:/manifest.json", "assets/../shock.material.json", "assets\\shock.material.json",
            "assets//shock.material.json", "assets/shock.material.json\n", "assets/Shock.material.json", "assets/run.dll", "assets/icon.svg"
        })
        { Reject(Archive(source.Append((path, source[1].Bytes)).ToList()), "unsafe or unsupported archive path"); }
        Reject(Archive(source, symbolicLink: "assets/shock.material.json"), "symbolic-link asset");
        Reject(Archive(source.Append(("assets/large.material.json", new byte[MonitorSkinPackage.MaximumMaterialBytes + 1])).ToList()),
            "individual asset size limit");
        Reject(Archive([("manifest.json", new byte[MonitorSkinPackage.MaximumManifestBytes + 1])]), "manifest size limit");
        Reject(Archive(Enumerable.Range(0, MonitorSkinPackage.MaximumEntries + 1)
            .Select(index => ($"assets/item-{index}.material.json", source[1].Bytes)).ToList()), "entry count limit");

        foreach (var change in new Action<JsonObject>[]
        {
            json => json["version"] = 1,
            json => json["layout"] = "external-code",
            json => json["script"] = "anything",
            json => json["background"] = null,
            json => json["background"] = "file:///tmp/skin",
            json => json["id"] = "d3-inspired\n",
            json => json["fixedWaveformRows"] = 6,
            json => json.Remove("fixedWaveformRows"),
            json => json["deviceProfile"] = "unknown-device",
            json => json["deviceProfile"] = "file:///tmp/device.dll",
            json => json["deviceProfile"] = null,
            json => json.Remove("deviceProfile"),
            json => json.Remove("defaultEnergyStepsJoules"),
            json => json["defaultEnergyStepsJoules"] = null,
            json => json["materials"] = null,
            json => ((JsonObject)json["materials"]!).Remove("shock"),
            json => ((JsonObject)json["materials"]!)["shock"] = "https://example.org/shock.material.json"
        })
        { Reject(Archive(Rewrite(source, "manifest.json", change)), "invalid manifest contract"); }
        foreach (int[] steps in new int[][] { [], [0, 1], [1, 1001], [10, 5], [1, 1], Enumerable.Range(1, 65).ToArray() })
        {
            Reject(Archive(Rewrite(source, "manifest.json", json => json["defaultEnergyStepsJoules"] = JsonSerializer.SerializeToNode(steps))),
                "invalid default energy table");
        }
        foreach (int[] steps in new int[][] { [1], [1000], Enumerable.Range(1, 64).ToArray() })
        {
            using var input = new MemoryStream(Archive(Rewrite(generic, "manifest.json", json =>
                json["defaultEnergyStepsJoules"] = JsonSerializer.SerializeToNode(steps))));
            Require(MonitorSkinPackage.Read(input).DefaultEnergyStepsJoules.SequenceEqual(steps), "valid energy table boundary values remain supported");
        }
        foreach (var change in new Action<JsonObject>[]
        {
            json => json["version"] = 2,
            json => json["fill"] = "#123456\n",
            json => json["fill"] = null,
            json => json["pressedFill"] = "url(file:///tmp/color)",
            json => json["cornerRadius"] = 81,
            json => json["borderThickness"] = -1,
            json => json["onClick"] = "anything",
            json => json.Remove("foreground")
        })
        { Reject(Archive(Rewrite(source, "assets/shock.material.json", change)), "invalid material contract"); }
        var duplicateManifest = source.Select(entry => entry.Path == "manifest.json"
            ? (entry.Path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(entry.Bytes).Replace("\"version\": 2", "\"version\": 2, \"version\": 2", StringComparison.Ordinal))) : entry).ToList();
        Reject(Archive(duplicateManifest), "duplicate manifest property");
        var duplicateReference = source.Select(entry => entry.Path == "manifest.json"
            ? (entry.Path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(entry.Bytes).Replace("\"materials\": {", "\"materials\": {\"shock\":\"assets/shock.material.json\",", StringComparison.Ordinal))) : entry).ToList();
        Reject(Archive(duplicateReference), "duplicate nested material reference");
        var duplicateMaterial = source.Select(entry => entry.Path == "assets/shock.material.json"
            ? (entry.Path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(entry.Bytes).Replace("\"version\": 1", "\"version\": 1, \"version\": 1", StringComparison.Ordinal))) : entry).ToList();
        Reject(Archive(duplicateMaterial), "duplicate material property");
        var compressedBomb = new List<(string Path, byte[] Bytes)> { ("manifest.json", Encoding.UTF8.GetBytes("{}" + new string(' ', 16000))) };
        Reject(Archive(compressedBomb, CompressionLevel.SmallestSize), "excessive expansion ratio", "Skin.TooLarge");
        var totalLimit = new List<(string Path, byte[] Bytes)>();
        var manifest = JsonNode.Parse(generic[0].Bytes)!.AsObject();
        var references = new JsonObject();
        for (int index = 0; index < 32; index++)
        {
            string path = $"assets/item-{index}.material.json";
            references[$"item-{index}"] = path;
            string material = Encoding.UTF8.GetString(source[1].Bytes).PadRight(MonitorSkinPackage.MaximumMaterialBytes);
            totalLimit.Add((path, Encoding.UTF8.GetBytes(material)));
        }
        manifest["materials"] = references;
        totalLimit.Insert(0, ("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString())));
        Reject(Archive(totalLimit), "aggregate expanded size limit");
    }

    internal static MonitorSkinPackage WithManualVitals(MonitorSkinPackage package, string[]? names)
    {
        using var input = new MemoryStream(Archive(Rewrite(Entries(Export(package)), "manifest.json", json =>
        {
            if (names is null) { json.Remove("supportedManualVitals"); }
            else { json["supportedManualVitals"] = JsonSerializer.SerializeToNode(names); }
        })));
        return MonitorSkinPackage.Read(input);
    }

    private static byte[] Export(MonitorSkinPackage package)
    {
        using var output = new MemoryStream();
        package.Write(output);
        return output.ToArray();
    }

    private static List<(string Path, byte[] Bytes)> Entries(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        return archive.Entries.Select(entry =>
        {
            using var content = entry.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            return (entry.FullName, buffer.ToArray());
        }).ToList();
    }

    private static List<(string Path, byte[] Bytes)> Rewrite(List<(string Path, byte[] Bytes)> entries, string path, Action<JsonObject> change) =>
        entries.Select(entry =>
        {
            if (entry.Path != path) { return entry; }
            var json = JsonNode.Parse(entry.Bytes)!.AsObject();
            change(json);
            return (entry.Path, Encoding.UTF8.GetBytes(json.ToJsonString()));
        }).ToList();

    private static byte[] Archive(List<(string Path, byte[] Bytes)> entries, CompressionLevel compression = CompressionLevel.NoCompression, string? symbolicLink = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var source in entries)
            {
                var entry = archive.CreateEntry(source.Path, compression);
                if (source.Path == symbolicLink) { entry.ExternalAttributes = 0xA1FF << 16; }
                using var stream = entry.Open();
                stream.Write(source.Bytes);
            }
        }
        return output.ToArray();
    }

    private static void Reject(byte[] bytes, string reason, string? code = null)
    {
        bool rejected = false;
        try { using var input = new MemoryStream(bytes); _ = MonitorSkinPackage.Read(input); }
        catch (Exception error) when (error is ArgumentException or JsonException) { rejected = code is null || error.Message == code; }
        Require(rejected, reason + " must be rejected before use");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Skin archive: " + message); }
    }
}
