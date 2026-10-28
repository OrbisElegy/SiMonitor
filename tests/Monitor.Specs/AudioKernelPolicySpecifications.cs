// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class AudioKernelPolicySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(DispatchRequiresExecutableFeatures), DispatchRequiresExecutableFeatures),
        new(nameof(DispatchDoesNotSpecialCaseProcessorModels), DispatchDoesNotSpecialCaseProcessorModels),
        new(nameof(CompiledAudioContainsOnlyTargetArchitectureIntrinsics), CompiledAudioContainsOnlyTargetArchitectureIntrinsics),
        new(nameof(ShortBlocksSelectUsableNarrowKernels), ShortBlocksSelectUsableNarrowKernels),
#if MONITOR_X64
        new(nameof(CpuSignaturesDecodeExtendedFamilyAndModel), CpuSignaturesDecodeExtendedFamilyAndModel),
#endif
    ];

    private static void DispatchRequiresExecutableFeatures()
    {
        var scalar = new AudioCpuCapabilities("", 0, 0, false, false, false, false);
        Check.That(AudioKernelPolicy.SelectPcm(scalar) == AudioKernelKind.Scalar &&
            AudioKernelPolicy.SelectMixing(scalar) == AudioKernelKind.Scalar, "disabled runtime features use scalar kernels");
        var sse = scalar with { Sse2 = true };
        var avx2 = sse with { Avx2 = true };
        var avx512F = avx2 with { Avx512F = true };
        Check.That(AudioKernelPolicy.SelectPcm(sse) == AudioKernelKind.Sse2 &&
            AudioKernelPolicy.SelectMixing(sse) == AudioKernelKind.Sse2, "baseline x64 requires only SSE2");
        Check.That(AudioKernelPolicy.SelectPcm(avx2) == AudioKernelKind.Avx2 &&
            AudioKernelPolicy.SelectMixing(avx2) == AudioKernelKind.Avx2, "AVX2 feature tier");
        Check.That(AudioKernelPolicy.SelectPcm(avx512F) == AudioKernelKind.Avx512 &&
            AudioKernelPolicy.SelectMixing(avx512F) == AudioKernelKind.Avx512, "AVX512F is sufficient for these kernels");
        var arm = scalar with { AdvSimd = true };
        Check.That(AudioKernelPolicy.SelectPcm(arm) == AudioKernelKind.AdvSimd &&
            AudioKernelPolicy.SelectMixing(arm) == AudioKernelKind.AdvSimd, "ARM selects its independent kernel");
    }

    private static void DispatchDoesNotSpecialCaseProcessorModels()
    {
        foreach (string vendor in new[] { "GenuineIntel", "AuthenticAMD", "unknown" })
            foreach (int family in new[] { 6, 0x19, 0x1A })
                foreach (int model in new[] { 0, 0xBE, 0x44 })
                {
                    var capabilities = new AudioCpuCapabilities(vendor, family, model, true, true, false, false);
                    Check.That(AudioKernelPolicy.SelectPcm(capabilities) == AudioKernelKind.Avx2 &&
                        AudioKernelPolicy.SelectMixing(capabilities) == AudioKernelKind.Avx2, "equal usable features select equal kernels");
                }
    }

    private static void CompiledAudioContainsOnlyTargetArchitectureIntrinsics()
    {
        using var stream = File.OpenRead(typeof(SampleToneRenderer).Assembly.Location);
        using var reader = new PEReader(stream);
        MetadataReader metadata = reader.GetMetadataReader();
        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            string ns = metadata.GetString(metadata.GetTypeReference(handle).Namespace);
#if !MONITOR_ARM64
            Check.That(ns != "System.Runtime.Intrinsics.Arm", "non-ARM build must exclude ARM code from IL");
#endif
#if !MONITOR_X64
            Check.That(ns != "System.Runtime.Intrinsics.X86", "non-x64 build must exclude x86 code from IL");
#endif
        }
    }

    private static void ShortBlocksSelectUsableNarrowKernels()
    {
        foreach (int length in Enumerable.Range(0, 34))
        {
            Check.That(AudioKernels.IsSupported(AudioKernelPolicy.PcmForLength(length)) &&
                AudioKernels.IsSupported(AudioKernelPolicy.MixingForLength(length)), "short-block selection cannot grant ISA support");
        }
        Check.That(AudioKernelPolicy.PcmForLength(7) == AudioKernelKind.Scalar &&
            AudioKernelPolicy.MixingForLength(3) == AudioKernelKind.Scalar, "sub-vector buffers use scalar directly");
        if (AudioKernelPolicy.Capabilities.Sse2)
        {
            Check.That(AudioKernelPolicy.PcmForLength(8) == AudioKernelKind.Sse2 &&
                AudioKernelPolicy.MixingForLength(4) == AudioKernelKind.Sse2, "short x64 buffers use a complete narrow vector");
        }
    }

#if MONITOR_X64
    private static void CpuSignaturesDecodeExtendedFamilyAndModel()
    {
        Check.That(AudioKernelPolicy.DecodeFamilyModel(0x000B06E0) == (6, 0xBE), "Intel extended model");
        Check.That(AudioKernelPolicy.DecodeFamilyModel(0x00A00F11) == (0x19, 1), "AMD extended family");
        Check.That(AudioKernelPolicy.DecodeFamilyModel(0x005A0520) == (5, 2), "legacy family ignores extended fields");
    }
#endif
}
