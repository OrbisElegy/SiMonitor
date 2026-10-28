// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

internal enum AudioKernelKind
{
    Scalar,
    Sse2,
    Avx2,
    Avx512,
    AdvSimd,
}

// Platform files are excluded at build time. Policy selects once, outside the
// render loop; explicit kernel overloads also allow differential verification.
internal static partial class AudioKernels
{
    internal static void Prepare()
    {
        _ = AudioKernelPolicy.PcmKernel;
        _ = AudioKernelPolicy.MixingKernel;
    }

    internal static bool IsSupported(AudioKernelKind kernel) => kernel switch
    {
        AudioKernelKind.Scalar => true,
#if MONITOR_X64
        AudioKernelKind.Sse2 or AudioKernelKind.Avx2 or AudioKernelKind.Avx512 => IsX64Supported(kernel),
#elif MONITOR_ARM64
        AudioKernelKind.AdvSimd => IsArm64Supported(),
#endif
        _ => false,
    };

    internal static void ConvertPcm(ReadOnlySpan<short> source, Span<float> destination, int gainQ15) =>
        ConvertPcm(source, destination, gainQ15, AudioKernelPolicy.PcmForLength(source.Length));

    // Caller supplies a validated Q15 gain in [0, 16384]. The complete integer
    // product fits Int32, including -32768 * 65536 == Int32.MinValue.
    internal static void ConvertPcm(ReadOnlySpan<short> source, Span<float> destination, int gainQ15, AudioKernelKind kernel)
    {
        destination = destination[..source.Length];
        switch (kernel)
        {
            case AudioKernelKind.Scalar:
                ConvertPcmScalar(source, destination, gainQ15);
                break;
#if MONITOR_X64
            case AudioKernelKind.Sse2:
                ConvertPcmSse2(source, destination, gainQ15);
                break;
            case AudioKernelKind.Avx2:
                ConvertPcmAvx2(source, destination, gainQ15);
                break;
            case AudioKernelKind.Avx512:
                ConvertPcmAvx512(source, destination, gainQ15);
                break;
#elif MONITOR_ARM64
            case AudioKernelKind.AdvSimd:
                ConvertPcmAdvSimd(source, destination, gainQ15);
                break;
#endif
            default:
                throw new PlatformNotSupportedException("AudioKernel.Unavailable");
        }
    }

    internal static void AddInPlace(Span<float> destination, ReadOnlySpan<float> source) =>
        AddInPlace(destination, source, AudioKernelPolicy.MixingForLength(source.Length));

    internal static void AddInPlace(Span<float> destination, ReadOnlySpan<float> source, AudioKernelKind kernel)
    {
        destination = destination[..source.Length];
        switch (kernel)
        {
            case AudioKernelKind.Scalar:
                AddScalar(destination, source);
                break;
#if MONITOR_X64
            case AudioKernelKind.Sse2:
                AddSse2(destination, source);
                break;
            case AudioKernelKind.Avx2:
                AddAvx2(destination, source);
                break;
            case AudioKernelKind.Avx512:
                AddAvx512(destination, source);
                break;
#elif MONITOR_ARM64
            case AudioKernelKind.AdvSimd:
                AddAdvSimd(destination, source);
                break;
#endif
            default:
                throw new PlatformNotSupportedException("AudioKernel.Unavailable");
        }
    }

    internal static void Clamp(Span<float> destination) => Clamp(destination, AudioKernelPolicy.MixingForLength(destination.Length));

    internal static void Clamp(Span<float> destination, AudioKernelKind kernel)
    {
        switch (kernel)
        {
            case AudioKernelKind.Scalar:
                ClampScalar(destination);
                break;
#if MONITOR_X64
            case AudioKernelKind.Sse2:
                ClampSse2(destination);
                break;
            case AudioKernelKind.Avx2:
                ClampAvx2(destination);
                break;
            case AudioKernelKind.Avx512:
                ClampAvx512(destination);
                break;
#elif MONITOR_ARM64
            case AudioKernelKind.AdvSimd:
                ClampAdvSimd(destination);
                break;
#endif
            default:
                throw new PlatformNotSupportedException("AudioKernel.Unavailable");
        }
    }

    private static void ConvertPcmScalar(ReadOnlySpan<short> source, Span<float> destination, int gainQ15)
    {
        int gain = 4 * gainQ15;
        for (int i = 0; i < source.Length; i++) { destination[i] = source[i] * gain / 2147483648f; }
    }

    private static void AddScalar(Span<float> destination, ReadOnlySpan<float> source)
    {
        for (int i = 0; i < source.Length; i++) { destination[i] += source[i]; }
    }

    private static void ClampScalar(Span<float> destination)
    {
        for (int i = 0; i < destination.Length; i++) { destination[i] = Math.Clamp(destination[i], -1, 1); }
    }
}
