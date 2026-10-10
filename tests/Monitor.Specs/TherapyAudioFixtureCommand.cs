// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class TherapyAudioFixtureCommand
{
    internal static int Execute(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length != 2)
        { error.WriteLine("Usage: --therapy-audio-fixture OUTPUT_DIRECTORY"); return 2; }
        string directory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(directory);
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            string path = Path.Combine(directory, "aed-runtime-" + locale + ".wav");
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(0);
            var renderer = new TherapySoundRenderer();
            float[] buffer = new float[480];
            ulong revision = 0;
            foreach (var (phase, seconds) in new[]
            {
                (TherapySoundPhase.Analyzing, 8), (TherapySoundPhase.Charging, 3), (TherapySoundPhase.Ready, 7),
                (TherapySoundPhase.CprShock, 120), (TherapySoundPhase.Analyzing, 9), (TherapySoundPhase.Silent, 6)
            })
            {
                revision++;
                for (int frame = 0; frame < seconds * 48000; frame += buffer.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (phase == TherapySoundPhase.CprShock && frame == 0 ||
                        phase == TherapySoundPhase.Silent && (frame == 0 || frame / 32000 != (frame - buffer.Length) / 32000))
                    { renderer.UpdateRelay(true, triggered: true); } // Delivered shock; then a separate 90 PPM pacing audition.
                    // Exercise independent UI and audio clocks, including UI
                    // stalls, instead of supplying perfectly aligned progress.
                    int block = frame / buffer.Length;
                    if (frame == 0 || block % 5 == 0 && block % 200 >= 50)
                    {
                        int elapsed = frame == 0 ? 0 : Math.Clamp(frame / 48 + (block % 15 == 0 ? 17 : -23), 0, 120000);
                        renderer.Update(new(revision, phase, locale, true,
                            phase == TherapySoundPhase.Charging ? frame * 1000 / (seconds * 48000) : 0,
                            phase == TherapySoundPhase.CprShock ? elapsed : 0)
                        { Announce = true, EnteringAed = revision == 1, AfterCpr = revision == 5 });
                    }
                    Array.Clear(buffer);
                    renderer.Mix(buffer);
                    foreach (float sample in buffer)
                    { writer.Write((short)Math.Clamp(Math.Round(sample * 32768), short.MinValue, short.MaxValue)); }
                }
            }
            long length = stream.Position;
            stream.Position = 4; writer.Write(checked((int)(length - 8)));
            stream.Position = 40; writer.Write(checked((int)(length - 44)));
            output.WriteLine(path);
        }
        WritePriorityFixture(directory, output, cancellationToken);
        return 0;
    }

    private static void WritePriorityFixture(string directory, TextWriter output, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "ready-priority.wav");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(0);
        var session = new AudioRenderSession();
        float[] buffer = new float[480];
        long key = 0;
        // 0–3 s maximum-volume critical alarm + heartbeat; 3–9 s stored
        // energy; 9–13 s disarmed. Alarm schedules continue without replay.
        for (int frame = 0; frame < 13 * 48000; frame += buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frame % (4 * 48000) == 0)
            { session.Schedule(++key, SelectedMonitorTones.Alarm(MonitorNoticeLevel.Critical, 100), frame, frame + 4800); }
            if (frame % 48000 == 0)
            { session.Schedule(++key, SelectedMonitorTones.Heartbeat(100), frame, frame + 4800); }
            if (frame == 3 * 48000)
            { session.UpdateTherapy(new(1, TherapySoundPhase.Ready, "zh-CN", false, 1000, 0) { Announce = true }); }
            if (frame == 9 * 48000)
            { session.UpdateTherapy(new(2, TherapySoundPhase.Silent, "zh-CN", false, 0, 0)); }
            if (!session.TryProduce(buffer.Length) || session.Read(buffer) != buffer.Length)
            { throw new InvalidOperationException("TherapyAudioFixture.BufferFailure"); }
            foreach (float sample in buffer)
            { writer.Write((short)Math.Clamp(Math.Round(sample * 32768), short.MinValue, short.MaxValue)); }
        }
        long length = stream.Position;
        stream.Position = 4; writer.Write(checked((int)(length - 8)));
        stream.Position = 40; writer.Write(checked((int)(length - 44)));
        output.WriteLine(path);
    }
}
