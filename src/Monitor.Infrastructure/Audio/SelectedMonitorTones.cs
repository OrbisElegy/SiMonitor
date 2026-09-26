// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using Monitor.Application.Presentation;

namespace Monitor.Infrastructure.Audio;

public enum MonitorToneSample { None, Info, Notice, Warning, Critical, Heartbeat }

// Original user-selected A/A4 samples, authored offline. Loaded before Render;
// no floating-point synthesis, file reads or allocation on the render path.
public static class SelectedMonitorTones
{
    private static readonly short[][] Samples = [[], Load("Info", 8640), Load("Notice", 7680),
        Load("Warning", 6240), Load("Critical", 168000), Load("Heartbeat", 4800)];
    internal static ReadOnlySpan<short> Get(MonitorToneSample sample) => Samples[(int)sample];
    public static TonePreset Alarm(MonitorNoticeLevel level, int volumePercent) =>
        Create((MonitorToneSample)((int)level + 1), volumePercent);
    public static TonePreset Heartbeat(int volumePercent)
    {
        var tone = Create(MonitorToneSample.Heartbeat, volumePercent);
        // Keep alarm headroom/timbre intact; routine beats are12dB quieter
        // than the selected audition level, independently of master volume.
        return tone with { GainQ15 = tone.GainQ15 / 4 };
    }
    private static TonePreset Create(MonitorToneSample sample, int volume)
    {
        if (sample is < MonitorToneSample.Info or > MonitorToneSample.Heartbeat || volume is < 0 or > 100)
        { throw new ArgumentOutOfRangeException(nameof(volume)); }
        return new("SelectedMonitorA4@1", 795000, 1, 0, 1, 16384 * volume / 100) { Sample = sample };
    }
    private static short[] Load(string name, int frames)
    {
        using var stream = typeof(SelectedMonitorTones).Assembly.GetManifestResourceStream(
            $"Monitor.Infrastructure.Audio.SelectedTones.{name}.pcm") ?? throw new InvalidOperationException("AudioTone.MissingAsset");
        if (stream.Length != frames * 2) { throw new InvalidOperationException("AudioTone.InvalidAsset"); }
        byte[] bytes = new byte[frames * 2]; stream.ReadExactly(bytes);
        short[] samples = new short[frames];
        for (int i = 0; i < frames; i++) { samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)); }
        return samples;
    }
}
