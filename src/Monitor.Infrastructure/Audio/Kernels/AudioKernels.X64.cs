// SPDX-License-Identifier: AGPL-3.0-or-later
#if MONITOR_X64
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Monitor.Infrastructure.Audio;

internal static partial class AudioKernels
{
    private static bool IsX64Supported(AudioKernelKind kernel) => kernel switch
    {
        AudioKernelKind.Sse2 => Sse2.IsSupported,
        AudioKernelKind.Avx2 => Avx2.IsSupported,
        AudioKernelKind.Avx512 => Avx512F.IsSupported,
        _ => false,
    };

    private static void ConvertPcmSse2(ReadOnlySpan<short> source, Span<float> destination, int gainQ15)
    {
        ref short input = ref MemoryMarshal.GetReference(source);
        ref float output = ref MemoryMarshal.GetReference(destination);
        var gain = Vector128.Create((short)gainQ15);
        var scale = Vector128.Create(1 / 2147483648f);
        int i = 0;
        for (; i <= source.Length - Vector128<short>.Count; i += Vector128<short>.Count)
        {
            var samples = Vector128.LoadUnsafe(ref input, (nuint)i);
            // SSE2 has no Int32 MultiplyLow. Reassemble each signed 16x16
            // product from its two halves, then apply the exact factor of four.
            var low = Sse2.MultiplyLow(samples, gain);
            var high = Sse2.MultiplyHigh(samples, gain);
            var first = Sse2.ShiftLeftLogical(Sse2.UnpackLow(low, high).AsInt32(), 2);
            var second = Sse2.ShiftLeftLogical(Sse2.UnpackHigh(low, high).AsInt32(), 2);
            Sse.Multiply(Sse2.ConvertToVector128Single(first), scale).StoreUnsafe(ref output, (nuint)i);
            Sse.Multiply(Sse2.ConvertToVector128Single(second), scale).StoreUnsafe(ref output, (nuint)(i + Vector128<int>.Count));
        }
        ConvertPcmScalar(source[i..], destination[i..], gainQ15);
    }

    private static void ConvertPcmAvx2(ReadOnlySpan<short> source, Span<float> destination, int gainQ15)
    {
        ref short input = ref MemoryMarshal.GetReference(source);
        ref float output = ref MemoryMarshal.GetReference(destination);
        var gain = Vector256.Create(gainQ15 * 4);
        var scale = Vector256.Create(1 / 2147483648f);
        int i = 0;
        for (; i <= source.Length - Vector256<short>.Count; i += Vector256<short>.Count)
        {
            var samples = Vector256.LoadUnsafe(ref input, (nuint)i);
            var first = Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(samples.GetLower()), gain);
            var second = Avx2.MultiplyLow(Avx2.ConvertToVector256Int32(samples.GetUpper()), gain);
            Avx.Multiply(Avx.ConvertToVector256Single(first), scale).StoreUnsafe(ref output, (nuint)i);
            Avx.Multiply(Avx.ConvertToVector256Single(second), scale).StoreUnsafe(ref output, (nuint)(i + Vector256<int>.Count));
        }
        ConvertPcmScalar(source[i..], destination[i..], gainQ15);
    }

    private static void ConvertPcmAvx512(ReadOnlySpan<short> source, Span<float> destination, int gainQ15)
    {
        ref short input = ref MemoryMarshal.GetReference(source);
        ref float output = ref MemoryMarshal.GetReference(destination);
        var gain = Vector512.Create(gainQ15 * 4);
        var scale = Vector512.Create(1 / 2147483648f);
        int i = 0;
        // VPMOVSXWD takes 256 bits of PCM and produces 512 bits of Int32;
        // this path requires AVX512F only, without a hidden AVX512BW dependency.
        for (; i <= source.Length - Vector256<short>.Count; i += Vector256<short>.Count)
        {
            var samples = Vector256.LoadUnsafe(ref input, (nuint)i);
            var product = Avx512F.MultiplyLow(Avx512F.ConvertToVector512Int32(samples), gain);
            Avx512F.Multiply(Avx512F.ConvertToVector512Single(product), scale).StoreUnsafe(ref output, (nuint)i);
        }
        ConvertPcmScalar(source[i..], destination[i..], gainQ15);
    }

    private static void AddSse2(Span<float> destination, ReadOnlySpan<float> source)
    {
        ref float input = ref MemoryMarshal.GetReference(source);
        ref float output = ref MemoryMarshal.GetReference(destination);
        int i = 0;
        for (; i <= source.Length - Vector128<float>.Count; i += Vector128<float>.Count)
        {
            Sse.Add(Vector128.LoadUnsafe(ref output, (nuint)i), Vector128.LoadUnsafe(ref input, (nuint)i)).StoreUnsafe(ref output, (nuint)i);
        }
        AddScalar(destination[i..], source[i..]);
    }

    private static void AddAvx2(Span<float> destination, ReadOnlySpan<float> source)
    {
        ref float input = ref MemoryMarshal.GetReference(source);
        ref float output = ref MemoryMarshal.GetReference(destination);
        int i = 0;
        for (; i <= source.Length - Vector256<float>.Count; i += Vector256<float>.Count)
        {
            Avx.Add(Vector256.LoadUnsafe(ref output, (nuint)i), Vector256.LoadUnsafe(ref input, (nuint)i)).StoreUnsafe(ref output, (nuint)i);
        }
        AddScalar(destination[i..], source[i..]);
    }

    private static void AddAvx512(Span<float> destination, ReadOnlySpan<float> source)
    {
        ref float input = ref MemoryMarshal.GetReference(source);
        ref float output = ref MemoryMarshal.GetReference(destination);
        int i = 0;
        for (; i <= source.Length - Vector512<float>.Count; i += Vector512<float>.Count)
        {
            Avx512F.Add(Vector512.LoadUnsafe(ref output, (nuint)i), Vector512.LoadUnsafe(ref input, (nuint)i)).StoreUnsafe(ref output, (nuint)i);
        }
        AddScalar(destination[i..], source[i..]);
    }

    private static void ClampSse2(Span<float> destination)
    {
        ref float output = ref MemoryMarshal.GetReference(destination);
        var minimum = Vector128.Create(-1f);
        var maximum = Vector128.Create(1f);
        int i = 0;
        for (; i <= destination.Length - Vector128<float>.Count; i += Vector128<float>.Count)
        {
            var value = Vector128.LoadUnsafe(ref output, (nuint)i);
            // Compare/select preserves the original NaN payload and signed zero
            // just like Math.Clamp; hardware Min/Max has different NaN rules.
            value = Vector128.ConditionalSelect(Sse.CompareLessThan(value, minimum), minimum, value);
            value = Vector128.ConditionalSelect(Sse.CompareGreaterThan(value, maximum), maximum, value);
            value.StoreUnsafe(ref output, (nuint)i);
        }
        ClampScalar(destination[i..]);
    }

    private static void ClampAvx2(Span<float> destination)
    {
        ref float output = ref MemoryMarshal.GetReference(destination);
        var minimum = Vector256.Create(-1f);
        var maximum = Vector256.Create(1f);
        int i = 0;
        for (; i <= destination.Length - Vector256<float>.Count; i += Vector256<float>.Count)
        {
            var value = Vector256.LoadUnsafe(ref output, (nuint)i);
            value = Vector256.ConditionalSelect(Avx.CompareLessThan(value, minimum), minimum, value);
            value = Vector256.ConditionalSelect(Avx.CompareGreaterThan(value, maximum), maximum, value);
            value.StoreUnsafe(ref output, (nuint)i);
        }
        ClampScalar(destination[i..]);
    }

    private static void ClampAvx512(Span<float> destination)
    {
        ref float output = ref MemoryMarshal.GetReference(destination);
        var minimum = Vector512.Create(-1f);
        var maximum = Vector512.Create(1f);
        int i = 0;
        for (; i <= destination.Length - Vector512<float>.Count; i += Vector512<float>.Count)
        {
            var value = Vector512.LoadUnsafe(ref output, (nuint)i);
            value = Vector512.ConditionalSelect(Avx512F.CompareLessThan(value, minimum), minimum, value);
            value = Vector512.ConditionalSelect(Avx512F.CompareGreaterThan(value, maximum), maximum, value);
            value.StoreUnsafe(ref output, (nuint)i);
        }
        ClampScalar(destination[i..]);
    }
}
#endif
