// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;

namespace Monitor.Infrastructure.Audio;

// Fixed 48 kHz mono PCM assets; preload on the producer, never in a native callback.
public static class SelectedTherapySounds
{
    private static readonly string[] Prompts = ["AED_MODE_ADULT", "AED_ANALYZING", "AED_SHOCKABLE", "AED_READY",
        "AED_SHOCK_RECORDED", "AED_RHYTHM_CHANGED_ANALYZING", "AED_AUTO_DISARM", "AED_AUTO_DISARM_ANALYZING",
        "AED_NSA_CPR", "AED_CPR_START", "AED_CPR_BREATHS", "AED_CPR_STOP"];
    private static readonly Dictionary<string, short[]> Clips = LoadAll();

    internal static ReadOnlyMemory<short> Get(string id, string locale = "en") => Clips[
        id is "Ready" or "ReadyHold" or "Cpr" or "Relay" ? id : locale + "." + id];
    public static int FrameCount(string id, string locale = "en") => Get(id, locale).Length;

    private static Dictionary<string, short[]> LoadAll()
    {
        var result = new Dictionary<string, short[]>(StringComparer.Ordinal);
        foreach (string id in new[] { "Ready", "ReadyHold", "Cpr", "Relay" }) { result.Add(id, Load("Monitor.Therapy." + id + ".wav")); }
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            foreach (string id in Prompts) { result.Add(locale + "." + id, Load($"Monitor.Therapy.{locale}.{id}.wav")); }
        }
        return result;
    }

    private static short[] Load(string name)
    {
        using var stream = typeof(SelectedTherapySounds).Assembly.GetManifestResourceStream(name) ??
            throw new InvalidOperationException("TherapySound.MissingAsset:" + name);
        if (stream.Length is < 44 or > 2_000_000) { throw new InvalidOperationException("TherapySound.InvalidAsset"); }
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (!bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        { throw new InvalidOperationException("TherapySound.InvalidWave"); }
        bool format = false;
        for (int at = 12; at + 8 <= bytes.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4, 4)));
            int start = at + 8;
            if (length > bytes.Length - start) { throw new InvalidOperationException("TherapySound.InvalidChunk"); }
            if (bytes.AsSpan(at, 4).SequenceEqual("fmt "u8))
            {
                format = length >= 16 && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start, 2)) == 1 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start + 2, 2)) == 1 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(start + 4, 4)) == 48000 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start + 14, 2)) == 16;
            }
            if (bytes.AsSpan(at, 4).SequenceEqual("data"u8) && format && length > 0 && length % 2 == 0)
            {
                short[] samples = new short[length / 2];
                for (int i = 0; i < samples.Length; i++)
                { samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(start + i * 2, 2)); }
                return samples;
            }
            at = checked(start + length + (length & 1));
        }
        throw new InvalidOperationException("TherapySound.InvalidFormat");
    }
}
