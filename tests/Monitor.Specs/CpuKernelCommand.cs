// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text.Json;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class CpuKernelCommand
{
    internal static int Execute()
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CompiledArchitecture =
#if MONITOR_X64
                "x64",
#elif MONITOR_ARM64
                "arm64",
#else
                "scalar",
#endif
            AudioKernelPolicy.Capabilities,
            PcmKernel = AudioKernelPolicy.PcmKernel.ToString(),
            MixingKernel = AudioKernelPolicy.MixingKernel.ToString(),
            AvailableKernels = Enum.GetValues<AudioKernelKind>().Where(AudioKernels.IsSupported)
                .Select(kernel => kernel.ToString()).ToArray(),
        }));
        return 0;
    }
}
