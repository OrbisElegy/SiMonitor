// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// Capabilities mean executable in this process, after OS and runtime checks.
// CPU identity is diagnostic only; it never grants instruction support.
internal readonly record struct AudioCpuCapabilities(
    string Vendor, int Family, int Model, bool Sse2, bool Avx2,
    bool Avx512F, bool AdvSimd);

internal static partial class AudioKernelPolicy
{
    internal static AudioCpuCapabilities Capabilities { get; } = DetectCapabilities();
    internal static AudioKernelKind PcmKernel { get; } = SelectPcm(Capabilities);
    internal static AudioKernelKind MixingKernel { get; } = SelectMixing(Capabilities);

    internal static AudioKernelKind PcmForLength(int sampleCount) => sampleCount switch
    {
        < 8 => AudioKernelKind.Scalar,
        < 16 when Capabilities.Sse2 => AudioKernelKind.Sse2,
        _ => PcmKernel,
    };

    internal static AudioKernelKind MixingForLength(int sampleCount) => sampleCount switch
    {
        < 4 => AudioKernelKind.Scalar,
        < 8 when Capabilities.Sse2 => AudioKernelKind.Sse2,
        < 16 when Capabilities.Avx2 => AudioKernelKind.Avx2,
        _ => MixingKernel,
    };

    internal static AudioKernelKind SelectPcm(AudioCpuCapabilities capabilities)
    {
        if (capabilities.Avx512F) return AudioKernelKind.Avx512;
        if (capabilities.Avx2) return AudioKernelKind.Avx2;
        if (capabilities.Sse2) return AudioKernelKind.Sse2;
        return capabilities.AdvSimd ? AudioKernelKind.AdvSimd : AudioKernelKind.Scalar;
    }

    internal static AudioKernelKind SelectMixing(AudioCpuCapabilities capabilities)
    {
        if (capabilities.Avx512F) return AudioKernelKind.Avx512;
        if (capabilities.Avx2) return AudioKernelKind.Avx2;
        if (capabilities.Sse2) return AudioKernelKind.Sse2;
        return capabilities.AdvSimd ? AudioKernelKind.AdvSimd : AudioKernelKind.Scalar;
    }

#if !MONITOR_X64 && !MONITOR_ARM64
    private static AudioCpuCapabilities DetectCapabilities() => new("", 0, 0, false, false, false, false);
#endif
}
