// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class NibpSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(NibpRequiresMeasurementAndExpiresAtBoundary), NibpRequiresMeasurementAndExpiresAtBoundary),
        new(nameof(NibpAutoUsesStartIntervalsAndNeverOverlaps), NibpAutoUsesStartIntervalsAndNeverOverlaps),
        new(nameof(NibpDisableCancelsAndSessionRecordsEvents), NibpDisableCancelsAndSessionRecordsEvents),
        new(nameof(NibpSoundIsFiniteDistinctAndPartitionIndependent), NibpSoundIsFiniteDistinctAndPartitionIndependent),
        new(nameof(NibpDeflationStartsIntermittentAndFinishesSoftContinuous), NibpDeflationStartsIntermittentAndFinishesSoftContinuous),
        new(nameof(NibpAudioSharesOutputGainAndCancellation), NibpAudioSharesOutputGainAndCancellation)
    ];
    private static readonly ManualNibp Target = new(120, 80, 93);

    private static void NibpRequiresMeasurementAndExpiresAtBoundary()
    {
        var cuff = new NibpMeasurement();
        Check.That(!cuff.Start() && cuff.Phase == NibpPhase.Disabled, "disabled NIBP cannot measure");
        cuff.Advance(0, Target);
        Check.That(cuff.Enabled && cuff.Reading is null && cuff.Start() && !cuff.Start(), "enabled is not measured and starts cannot overlap");
        cuff.Advance(5_999_999_999, Target);
        Check.That(cuff.Phase == NibpPhase.Inflating && cuff.Reading is null, "inflation does not reveal target readings");
        cuff.Advance(6_000_000_000, new(140, 90, 105));
        Check.That(cuff.Phase == NibpPhase.Deflating && cuff.Reading is null, "deflation continues with a fixed measurement target");
        cuff.Advance(14_999_999_999, Target);
        Check.That(cuff.Reading is null, "the result is not published early");
        cuff.Advance(15_000_000_000, Target);
        Check.That(cuff.Reading == Target && cuff.MeasuredAtNs == 15_000_000_000, "the completed measurement owns its acquisition time");
        cuff.Advance(15_000_000_000 + NibpMeasurement.ReadingLifetimeNs - 1, Target);
        Check.That(cuff.Reading == Target, "reading remains available until expiry");
        cuff.Advance(15_000_000_000 + NibpMeasurement.ReadingLifetimeNs, Target);
        Check.That(cuff.Reading is null, "expiry clears the displayed reading exactly at fifteen minutes");
        cuff.Start();
        cuff.Stop();
        cuff.Advance(950_000_000_000, Target);
        Check.That(cuff.Reading is null, "stopping a cycle cannot publish its target later");
    }

    private static void NibpAutoUsesStartIntervalsAndNeverOverlaps()
    {
        var cuff = new NibpMeasurement();
        var events = new List<NibpMeasurementEvent>();
        cuff.Changed += events.Add;
        cuff.Advance(0, Target);
        cuff.ConfigureAuto(true, 1);
        Check.That(cuff.Measuring && cuff.NextMeasurementAtNs == 60_000_000_000, "enabling automatic mode starts immediately");
        cuff.ConfigureAuto(true, 1);
        cuff.Advance(15_000_000_000, Target);
        cuff.Advance(59_999_999_999, Target);
        Check.That(!cuff.Measuring && events.Count == 2, "unchanged automatic settings do not restart or duplicate cycles");
        cuff.Advance(60_000_000_000, Target);
        Check.That(cuff.Measuring && cuff.Reading is null && cuff.NextMeasurementAtNs == 120_000_000_000, "automatic measurements use start-to-start intervals");
        cuff.Stop();
        cuff.ConfigureAuto(false, 1);
        cuff.Advance(300_000_000_000, Target);
        Check.That(!cuff.Measuring && cuff.NextMeasurementAtNs is null, "turning off automatic mode prevents the next cycle");
        cuff.ConfigureAuto(true, 5);
        cuff.Advance(10_000_000_000_000, Target);
        Check.That(cuff.Measuring && events.Count(item => item.Kind == NibpEventKind.Started) == 4,
            "large clock advances do not replay missed automatic cycles");
        bool rejected = false;
        try { cuff.ConfigureAuto(true, 4); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected, "unsupported intervals are rejected");
        Check.That(cuff.AutoIntervalMinutes == 5, "invalid intervals do not publish partial settings");
    }

    private static void NibpDisableCancelsAndSessionRecordsEvents()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), manualVitals: new(Target));
        session.Nibp.ConfigureAuto(true, 5);
        for (int index = 0; index < 60; index++) { session.Advance(250_000_000); }
        Check.That(session.Nibp.Reading == Target, "the session clock completes the measurement");
        session.Nibp.Start();
        var disabled = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default());
        session.ScheduleSource(disabled, 0);
        session.Advance(50_000_000);
        var events = session.CapturePrintSnapshot().Events;
        Check.That(!session.Nibp.Enabled && !session.Nibp.Measuring && !session.Nibp.AutoEnabled && session.Nibp.Reading is null,
            "disabling NIBP cancels the cycle, automatic schedule and old result");
        Check.That(events.Count(item => item.Kind == PreviewRecordEventKind.NibpStarted) == 2 &&
            events.Any(item => item.Kind == PreviewRecordEventKind.NibpCompleted && item.TimeNs == 15_000_000_000) &&
            events.Any(item => item.Kind == PreviewRecordEventKind.NibpCancelled), "print history retains NIBP measurement events");
        session.Nibp.Advance(session.SimulationTimeNs, Target);
        Check.That(session.Nibp.Reading is null && !session.Nibp.AutoEnabled, "re-enabling never revives an old reading or schedule");
    }

    private static void NibpSoundIsFiniteDistinctAndPartitionIndependent()
    {
        float[][] phases = new float[2][];
        int phaseIndex = 0;
        foreach (var phase in new[] { NibpPhase.Inflating, NibpPhase.Deflating })
        {
            var whole = new NibpSoundRenderer();
            var chunked = new NibpSoundRenderer();
            var request = new NibpSoundRequest(phase, 1000);
            whole.Update(request);
            chunked.Update(request);
            float[] expected = new float[50000];
            float[] actual = new float[50000];
            whole.Mix(expected);
            for (int offset = 0; offset < actual.Length; offset += 100)
            {
                chunked.Update(request);
                chunked.Mix(actual.AsSpan(offset, 100));
            }
            Check.That(expected.SequenceEqual(actual), "repeated phase snapshots cannot restart the pump or valve sound");
            Check.That(expected.Take(48000).Any(sample => Math.Abs(sample) > .01f) && expected.Skip(49000).All(sample => sample == 0),
                "both cuff phases are audible and expire if control updates stop");
            Check.That(expected.All(sample => float.IsFinite(sample) && Math.Abs(sample) <= 1), "cuff PCM remains finite and bounded");
            phases[phaseIndex++] = expected;
        }
        Check.That(!phases[0].SequenceEqual(phases[1]), "inflation motor and deflation air sounds differ");
        WriteCuffFixture();
    }

    private static void NibpDeflationStartsIntermittentAndFinishesSoftContinuous()
    {
        var renderer = new NibpSoundRenderer();
        renderer.Update(new(NibpPhase.Deflating, 9000));
        float[] samples = new float[9 * 48000];
        renderer.Mix(samples);
        for (int pulse = 0; pulse < 8; pulse++)
        {
            int start = pulse * 36000;
            Check.That(Rms(samples.AsSpan(start + 4800, 4800)) > .003,
                "initial valve openings produce gentle audible releases");
            Check.That(samples.AsSpan(start + 14400, 21600).IndexOfAnyExcept(0f) == -1,
                "initial valve releases have real silent intervals");
        }
        for (int frame = 6 * 48000 + 4800; frame < samples.Length - 4800; frame += 4800)
        {
            Check.That(Rms(samples.AsSpan(frame, 4800)) > .003,
                "the final release stays continuous instead of pulsing");
        }
        Check.That(Rms(samples.AsSpan(6 * 48000 + 4800, 4800)) > Rms(samples.AsSpan(8 * 48000, 4800)),
            "continuous airflow softens as cuff pressure falls");
        Check.That(samples.Max(Math.Abs) < .04 && Rms(samples) < .009,
            "deflation stays soft without exaggerated hiss peaks or sustained loudness");

        var updated = new NibpSoundRenderer();
        float[] actual = new float[samples.Length];
        for (int offset = 0; offset < actual.Length; offset += 4800)
        {
            updated.Update(new(NibpPhase.Deflating, 9000 - offset / 48));
            updated.Mix(actual.AsSpan(offset, 4800));
        }
        Check.That(samples.SequenceEqual(actual), "progress updates preserve the intermittent-to-continuous sequence");
        updated.Update(null);
        updated.Mix(new float[960]);
        updated.Update(new(NibpPhase.Deflating, 8500));
        float[] resumed = new float[4800];
        updated.Mix(resumed);
        Check.That(resumed.All(sample => sample == 0), "resuming during a closed valve interval preserves its silence");
        updated.Update(null);
        updated.Mix(new float[960]);
        updated.Update(new(NibpPhase.Deflating, 2000));
        updated.Mix(resumed);
        Check.That(Rms(resumed) > .003, "resuming the final release does not restart intermittent deflation");
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double energy = 0;
        foreach (float sample in samples) { energy += sample * sample; }
        return Math.Sqrt(energy / samples.Length);
    }

    private static void WriteCuffFixture()
    {
        float[] samples = new float[15 * 48000];
        var renderer = new NibpSoundRenderer();
        renderer.Update(new(NibpPhase.Inflating, 6000));
        renderer.Mix(samples.AsSpan(0, 6 * 48000));
        renderer.Update(new(NibpPhase.Deflating, 9000));
        renderer.Mix(samples.AsSpan(6 * 48000));
        Directory.CreateDirectory("artifacts");
        using var writer = new BinaryWriter(File.Create("artifacts/nibp-cuff.wav"));
        writer.Write("RIFF"u8);
        writer.Write(samples.Length * 2 + 36);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(48000);
        writer.Write(96000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (float sample in samples) { writer.Write((short)MathF.Round(sample * short.MaxValue)); }
    }

    private static void NibpAudioSharesOutputGainAndCancellation()
    {
        var audio = new AudioRenderSession();
        audio.UpdateNibp(new(NibpPhase.Inflating, 6000));
        float[] samples = new float[960];
        audio.Gain = 0;
        Check.That(audio.TryProduce(samples.Length) && audio.Read(samples) == samples.Length && samples.All(sample => sample == 0),
            "master mute applies to cuff audio on the shared output");
        audio.Gain = 1;
        Check.That(audio.TryProduce(samples.Length) && audio.Read(samples) == samples.Length && samples.Any(sample => sample != 0),
            "unmuting reveals the current cuff phase");
        audio.UpdateNibp(null);
        audio.TryProduce(samples.Length);
        audio.Read(samples);
        Check.That(samples.Skip(480).All(sample => sample == 0), "stop or pause retires cuff sound within a short fade");
    }
}
