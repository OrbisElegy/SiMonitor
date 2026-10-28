// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.Intrinsics.Arm;

namespace Monitor.Infrastructure.Audio;

internal static partial class AudioKernelPolicy
{
    private static AudioCpuCapabilities DetectCapabilities() =>
        new("", 0, 0, false, false, false, AdvSimd.IsSupported);
}
