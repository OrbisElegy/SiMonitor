// SPDX-License-Identifier: AGPL-3.0-or-later
#if MONITOR_ARM64
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Monitor.Infrastructure.Audio;

internal static partial class AudioKernels
{
    private static bool IsArm64Supported() => AdvSimd.IsSupported;

    private static void ConvertPcmAdvSimd(ReadOnlySpan<short> source, Span<float> destination, int gainQ15)
    {
        ref short input = ref MemoryMarshal.GetReference(source);
        ref float output = ref MemoryMarshal.GetReference(destination);
        var gain = Vector128.Create((short)gainQ15);
        var scale = Vector128.Create(1 / 2147483648f);
        int i = 0;
        for (; i <= source.Length - Vector128<short>.Count; i += Vector128<short>.Count)
        {
            var samples = Vector128.LoadUnsafe(ref input, (nuint)i);
            var first = AdvSimd.ShiftLeftLogical(AdvSimd.MultiplyWideningLower(samples.GetLower(), gain.GetLower()).AsUInt32(), 2).AsInt32();
            var second = AdvSimd.ShiftLeftLogical(AdvSimd.MultiplyWideningUpper(samples, gain).AsUInt32(), 2).AsInt32();
            AdvSimd.Multiply(AdvSimd.ConvertToSingle(first), scale).StoreUnsafe(ref output, (nuint)i);
            AdvSimd.Multiply(AdvSimd.ConvertToSingle(second), scale).StoreUnsafe(ref output, (nuint)(i + Vector128<int>.Count));
        }
        ConvertPcmScalar(source[i..], destination[i..], gainQ15);
    }

    private static void AddAdvSimd(Span<float> destination, ReadOnlySpan<float> source)
    {
        ref float input = ref MemoryMarshal.GetReference(source);
        ref float output = ref MemoryMarshal.GetReference(destination);
        int i = 0;
        for (; i <= source.Length - Vector128<float>.Count; i += Vector128<float>.Count)
        {
            AdvSimd.Add(Vector128.LoadUnsafe(ref output, (nuint)i), Vector128.LoadUnsafe(ref input, (nuint)i)).StoreUnsafe(ref output, (nuint)i);
        }
        AddScalar(destination[i..], source[i..]);
    }

    private static void ClampAdvSimd(Span<float> destination)
    {
        ref float output = ref MemoryMarshal.GetReference(destination);
        var minimum = Vector128.Create(-1f);
        var maximum = Vector128.Create(1f);
        int i = 0;
        for (; i <= destination.Length - Vector128<float>.Count; i += Vector128<float>.Count)
        {
            var value = Vector128.LoadUnsafe(ref output, (nuint)i);
            value = Vector128.ConditionalSelect(AdvSimd.CompareLessThan(value, minimum), minimum, value);
            value = Vector128.ConditionalSelect(AdvSimd.CompareGreaterThan(value, maximum), maximum, value);
            value.StoreUnsafe(ref output, (nuint)i);
        }
        ClampScalar(destination[i..]);
    }
}
#endif
