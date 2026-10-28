// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Monitor.Infrastructure.Audio;

internal static partial class AudioKernelPolicy
{
    private static AudioCpuCapabilities DetectCapabilities()
    {
        string vendor = "";
        int family = 0;
        int model = 0;
        if (X86Base.IsSupported)
        {
            (int maxLeaf, int ebx, int ecx, int edx) = X86Base.CpuId(0, 0);
            Span<byte> vendorBytes = stackalloc byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(vendorBytes, ebx);
            BinaryPrimitives.WriteInt32LittleEndian(vendorBytes[4..], edx);
            BinaryPrimitives.WriteInt32LittleEndian(vendorBytes[8..], ecx);
            vendor = Encoding.ASCII.GetString(vendorBytes);
            if (maxLeaf >= 1)
                (family, model) = DecodeFamilyModel(X86Base.CpuId(1, 0).Eax);
        }

        // .NET's IsSupported includes CPUID, OS XSAVE/XCR0 state and runtime
        // disable switches. Raw CPUID AVX bits alone must never enable a kernel.
        return new(vendor, family, model, Sse2.IsSupported, Avx2.IsSupported,
            Avx512F.IsSupported, false);
    }

    internal static (int Family, int Model) DecodeFamilyModel(int signature)
    {
        int baseFamily = (signature >> 8) & 0xF;
        int family = baseFamily == 0xF ? baseFamily + ((signature >> 20) & 0xFF) : baseFamily;
        int model = (signature >> 4) & 0xF;
        if (baseFamily is 6 or 0xF) model |= (signature >> 12) & 0xF0;
        return (family, model);
    }
}
