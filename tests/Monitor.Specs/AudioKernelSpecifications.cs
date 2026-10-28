// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class AudioKernelSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(PcmKernelsPreserveIntegerGainRoundingAcrossPcm16Range), PcmKernelsPreserveIntegerGainRoundingAcrossPcm16Range),
        new(nameof(AudioKernelsRespectUnalignedSlicesAndVectorTails), AudioKernelsRespectUnalignedSlicesAndVectorTails),
        new(nameof(AudioMixingPreservesVoiceOrderAndClampsOnce), AudioMixingPreservesVoiceOrderAndClampsOnce),
        new(nameof(AudioClampPreservesNanPayloadAndSignedZero), AudioClampPreservesNanPayloadAndSignedZero),
        new(nameof(SelectedToneRenderingPreservesGainCancellationAndContinuation), SelectedToneRenderingPreservesGainCancellationAndContinuation),
        new(nameof(SynthesizedTonePhaseAdvancementMatchesAbsoluteFrameReference), SynthesizedTonePhaseAdvancementMatchesAbsoluteFrameReference),
    ];

    private static IEnumerable<AudioKernelKind> SupportedKernels => Enum.GetValues<AudioKernelKind>().Where(AudioKernels.IsSupported);

    private static void PcmKernelsPreserveIntegerGainRoundingAcrossPcm16Range()
    {
        short[] source = Enumerable.Range(short.MinValue, 65536).Select(value => (short)value).ToArray();
        float[] expected = new float[source.Length];
        float[] actual = new float[source.Length];
        foreach (int gain in new[] { 0, 1, 31, 127, 1023, 8191, 8192, 10001, 16383, 16384 })
        {
            for (int i = 0; i < source.Length; i++) { expected[i] = ReferencePcm(source[i], gain); }
            foreach (AudioKernelKind kernel in SupportedKernels)
            {
                AudioKernels.ConvertPcm(source, actual, gain, kernel);
                Check.That(SameBits(expected, actual), $"{kernel} preserves exact PCM16 gain conversion, including extrema and zero gain ({gain})");
            }
        }
    }

    private static void AudioKernelsRespectUnalignedSlicesAndVectorTails()
    {
        short[] source = Enumerable.Range(0, 1100).Select(i => (short)(i * 911)).ToArray();
        int[] lengths = [.. Enumerable.Range(0, 65), 127, 128, 129, 255, 256, 257, 1023, 1057];
        foreach (AudioKernelKind kernel in SupportedKernels)
        {
            foreach (int length in lengths)
            {
                float[] pcm = Enumerable.Repeat(123f, length + 6).ToArray();
                float[] expectedPcm = (float[])pcm.Clone();
                for (int i = 0; i < length; i++) { expectedPcm[i + 3] = ReferencePcm(source[i + 1], 10001); }
                AudioKernels.ConvertPcm(source.AsSpan(1, length), pcm.AsSpan(3, length), 10001, kernel);
                Check.That(SameBits(expectedPcm, pcm), $"{kernel} PCM writes only the requested unaligned slice of length {length}");

                float[] contribution = Enumerable.Range(0, length + 2).Select(i => (i - 37) / 17f).ToArray();
                float[] expectedMix = (float[])pcm.Clone();
                for (int i = 0; i < length; i++) { expectedMix[i + 3] += contribution[i + 1]; }
                AudioKernels.AddInPlace(pcm.AsSpan(3, length), contribution.AsSpan(1, length), kernel);
                Check.That(SameBits(expectedMix, pcm), $"{kernel} addition preserves scalar order and bounded vector tails of length {length}");
                for (int i = 0; i < length; i++) { expectedMix[i + 3] = Math.Clamp(expectedMix[i + 3], -1, 1); }
                AudioKernels.Clamp(pcm.AsSpan(3, length), kernel);
                Check.That(SameBits(expectedMix, pcm), $"{kernel} clamp writes only the requested slice of length {length}");
            }
        }
    }

    private static void AudioMixingPreservesVoiceOrderAndClampsOnce()
    {
        foreach (AudioKernelKind kernel in SupportedKernels)
        {
            float[] destination = new float[257];
            float[] first = Enumerable.Repeat(16777216f, destination.Length).ToArray();
            float[] second = Enumerable.Repeat(1f, destination.Length).ToArray();
            float[] third = Enumerable.Repeat(-16777216f, destination.Length).ToArray();
            AudioKernels.AddInPlace(destination, first, kernel);
            AudioKernels.AddInPlace(destination, second, kernel);
            AudioKernels.AddInPlace(destination, third, kernel);
            AudioKernels.Clamp(destination, kernel);
            Check.That(destination.All(value => value == 0), $"{kernel} preserves floating-point voice order instead of reassociating sums");

            Array.Fill(first, 2f);
            Array.Fill(second, -1.5f);
            AudioKernels.AddInPlace(destination, first, kernel);
            AudioKernels.AddInPlace(destination, second, kernel);
            AudioKernels.Clamp(destination, kernel);
            Check.That(destination.All(value => value == 0.5f), $"{kernel} accumulates all voices before saturation");
        }
    }

    private static void AudioClampPreservesNanPayloadAndSignedZero()
    {
        float[] cases =
        [
            float.NegativeInfinity, -float.MaxValue, -1.00001f, -1f, -float.Epsilon,
            -0f, 0f, float.Epsilon, 1f, 1.00001f, float.MaxValue, float.PositiveInfinity,
            BitConverter.UInt32BitsToSingle(0x7fc12345), BitConverter.UInt32BitsToSingle(0xffc12345),
            BitConverter.UInt32BitsToSingle(0x7fa12345), BitConverter.UInt32BitsToSingle(0xffa12345),
        ];
        foreach (AudioKernelKind kernel in SupportedKernels)
        {
            for (int offset = 0; offset < cases.Length; offset++)
            {
                float[] actual = Enumerable.Range(0, 65).Select(i => cases[(i + offset) % cases.Length]).ToArray();
                float[] expected = actual.Select(value => Math.Clamp(value, -1, 1)).ToArray();
                AudioKernels.Clamp(actual, kernel);
                Check.That(SameBits(expected, actual), $"{kernel} clamp preserves NaN payloads and signed zero in vectors and tails");
            }
        }
    }

    private static void SelectedToneRenderingPreservesGainCancellationAndContinuation()
    {
        foreach (MonitorToneSample sample in Enum.GetValues<MonitorToneSample>().Where(value => value != MonitorToneSample.None))
        {
            foreach (int gain in new[] { 0, 1, 10001, 16384 })
            {
                var preset = TonePreset.BeatAudition with { Sample = sample, GainQ15 = gain, BeatPitchPercent = 83 };
                ReadOnlySpan<short> source = SelectedMonitorTones.Get(sample, preset.BeatPitchPercent);
                var voice = new ToneVoice(preset);
                float[] actual = new float[source.Length + 17];
                float[] expected = new float[actual.Length];
                for (int i = 0; i < source.Length; i++) { expected[i] = ReferencePcm(source[i], gain); }
                int rendered = voice.Render(actual);
                Check.That(rendered == source.Length && voice.Finished && SameBits(expected, actual), "selected PCM renders exact gain and clears the tail");

                foreach (int stop in new[] { 0, 1, 137, source.Length - 1 })
                {
                    voice = new ToneVoice(preset);
                    voice.Render(new float[stop]);
                    voice.Cancel();
                    voice = ToneVoice.Restore(voice.CaptureState());
                    int end = stop == 0 ? 0 : Math.Min(source.Length, stop + 240);
                    float[] cancellation = new float[257];
                    float[] cancellationExpected = new float[cancellation.Length];
                    for (int i = 0; i < end - stop; i++)
                    {
                        long selected = (long)source[stop + i] * 4 * gain;
                        long faded = ReferenceRound(selected * (end - 1 - stop - i), end - stop);
                        cancellationExpected[i] = faded / 2147483648f;
                    }
                    for (int offset = 0; offset < cancellation.Length; offset += 17)
                    {
                        voice.Render(cancellation.AsSpan(offset, Math.Min(17, cancellation.Length - offset)));
                        voice = ToneVoice.Restore(voice.CaptureState());
                    }
                    Check.That(voice.Finished && SameBits(cancellationExpected, cancellation), "selected PCM cancellation preserves integer ties-to-even across restoration and short chunks");
                }
            }
        }
    }

    private static void SynthesizedTonePhaseAdvancementMatchesAbsoluteFrameReference()
    {
        foreach (int frequency in new[] { 20000, 795001, 1000000, 19999999, 20000000 })
        {
            var preset = new TonePreset("PhaseReference@1", frequency, 17, 251, 29, 10001);
            float[] expected = new float[preset.TotalFrames];
            for (int frame = 0; frame < expected.Length; frame++)
            {
                const long denominator = 48000000;
                long phase = (long)frame * frequency % denominator * 1024;
                int index = (int)(phase / denominator);
                long first = ReferenceSine(index);
                long sine = first + ReferenceRound((ReferenceSine((index + 1) & 1023) - first) * (phase % denominator), denominator);
                long envelopeNumerator = frame < preset.AttackFrames ? frame : frame >= preset.AttackFrames + preset.HoldFrames ? preset.TotalFrames - 1 - frame : 1;
                long envelopeDenominator = frame < preset.AttackFrames ? preset.AttackFrames : frame >= preset.AttackFrames + preset.HoldFrames ? preset.ReleaseFrames : 1;
                long q31 = ReferenceRound(sine * preset.GainQ15 * envelopeNumerator, 32768 * envelopeDenominator);
                expected[frame] = q31 / 2147483648f;
            }
            var voice = new ToneVoice(preset);
            float[] actual = new float[expected.Length];
            for (int offset = 0; offset < actual.Length; offset += 13)
            {
                voice.Render(actual.AsSpan(offset, Math.Min(13, actual.Length - offset)));
                voice = ToneVoice.Restore(voice.CaptureState());
            }
            Check.That(SameBits(expected, actual), "phase recurrence matches independent absolute-frame synthesis across wraparound and restoration");
        }
    }

    private static float ReferencePcm(short sample, int gainQ15) => (long)sample * 65536 * gainQ15 / 16384 / 2147483648f;

    private static long ReferenceRound(long numerator, long denominator)
    {
        long magnitude = Math.Abs(numerator);
        long quotient = magnitude / denominator;
        long remainder = magnitude % denominator;
        if (remainder * 2 > denominator || remainder * 2 == denominator && (quotient & 1) != 0) { quotient++; }
        return numerator < 0 ? -quotient : quotient;
    }

    private static long ReferenceSine(int index)
    {
        int offset = index & 255;
        return (index >> 8) switch
        {
            0 => SineTable.Quarter[offset],
            1 => SineTable.Quarter[256 - offset],
            2 => -(long)SineTable.Quarter[offset],
            _ => -(long)SineTable.Quarter[256 - offset],
        };
    }

    private static bool SameBits(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual) =>
        MemoryMarshal.AsBytes(expected).SequenceEqual(MemoryMarshal.AsBytes(actual));
}
