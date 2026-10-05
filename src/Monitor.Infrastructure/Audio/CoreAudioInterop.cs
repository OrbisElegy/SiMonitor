// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Monitor.Infrastructure.Audio;

// Windows Core Audio declarations used by the managed WASAPI output. Method
// order mirrors the vtables in mmdeviceapi.h and audioclient.h; every method
// keeps its HRESULT so expected device failures stay return values.
[SupportedOSPlatform("windows")]
internal static partial class CoreAudio
{
    public const int RenderFlow = 0;
    public const int ConsoleRole = 0;
    public const uint DeviceStateActive = 0x1;
    public const int SharedMode = 0;
    public const uint StreamFlagsEventCallback = 0x0004_0000;
    public const uint StreamFlagsNoPersist = 0x0008_0000;
    public const uint StreamFlagsSrcDefaultQuality = 0x0800_0000;
    public const uint StreamFlagsAutoConvertPcm = 0x8000_0000;
    public const int SFalse = 1;
    public const int ChangedModeError = unchecked((int)0x8001_0106);
    public const ushort WaveFormatIeeeFloat = 0x0003;
    public const ushort WaveFormatExtensible = 0xFFFE;
    public const long HundredNanosecondsPerSecond = 10_000_000;

    public static readonly Guid DeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid AudioRenderClientId = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    public static readonly Guid AudioClockId = new("CD63314F-3FBA-4A1B-812C-EF96358728E7");
    public static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");

    // UniqueInstance wrappers are released deterministically with FinalRelease.
    public static readonly StrategyBasedComWrappers Wrappers = new();

    private const uint ClassContextAll = 0x17;
    private const uint MultithreadedApartment = 0x0;

    public static int CreateDeviceEnumerator(out nint enumerator) =>
        CoCreateInstance(DeviceEnumeratorClassId, 0, ClassContextAll, typeof(IMMDeviceEnumerator).GUID, out enumerator);

    public static int InitializeMultithreaded() => CoInitializeEx(0, MultithreadedApartment);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint classContext, in Guid interfaceId, out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint concurrencyModel);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("avrt.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

    [LibraryImport("avrt.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AvRevertMmThreadCharacteristics(nint handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormat
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSecond;
    public uint AverageBytesPerSecond;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatExtensible
{
    public WaveFormat Format;
    public ushort ValidBitsPerSample;
    public uint ChannelMask;
    public Guid SubFormat;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig]
    public int EnumAudioEndpoints(int dataFlow, uint stateMask, out nint devices);
    [PreserveSig]
    public int GetDefaultAudioEndpoint(int dataFlow, int role, out nint endpoint);
    [PreserveSig]
    public int GetDevice(string deviceId, out nint device);
    [PreserveSig]
    public int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig]
    public int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig]
    public int Activate(in Guid interfaceId, uint classContext, nint activationParameters, out nint instance);
    [PreserveSig]
    public int OpenPropertyStore(uint access, out nint properties);
    [PreserveSig]
    public int GetId(out nint deviceId);
    [PreserveSig]
    public int GetState(out uint state);
}

[GeneratedComInterface]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
internal partial interface IMMNotificationClient
{
    [PreserveSig]
    public int OnDeviceStateChanged(nint deviceId, uint newState);
    [PreserveSig]
    public int OnDeviceAdded(nint deviceId);
    [PreserveSig]
    public int OnDeviceRemoved(nint deviceId);
    [PreserveSig]
    public int OnDefaultDeviceChanged(int dataFlow, int role, nint defaultDeviceId);
    [PreserveSig]
    public int OnPropertyValueChanged(nint deviceId, PropertyKey key);
}

[GeneratedComInterface]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
internal partial interface IAudioClient
{
    [PreserveSig]
    public int Initialize(int shareMode, uint streamFlags, long bufferDuration100Ns, long periodicity100Ns, nint format, nint audioSessionGuid);
    [PreserveSig]
    public int GetBufferSize(out uint bufferFrames);
    [PreserveSig]
    public int GetStreamLatency(out long latency100Ns);
    [PreserveSig]
    public int GetCurrentPadding(out uint paddingFrames);
    [PreserveSig]
    public int IsFormatSupported(int shareMode, nint format, out nint closestMatch);
    [PreserveSig]
    public int GetMixFormat(out nint deviceFormat);
    [PreserveSig]
    public int GetDevicePeriod(out long defaultPeriod100Ns, out long minimumPeriod100Ns);
    [PreserveSig]
    public int Start();
    [PreserveSig]
    public int Stop();
    [PreserveSig]
    public int Reset();
    [PreserveSig]
    public int SetEventHandle(nint eventHandle);
    [PreserveSig]
    public int GetService(in Guid interfaceId, out nint service);
}

[GeneratedComInterface]
[Guid("726778CD-F60A-4EDA-82DE-E47610CD78AA")]
internal partial interface IAudioClient2 : IAudioClient
{
    [PreserveSig]
    public int IsOffloadCapable(int category, out int offloadCapable);
    [PreserveSig]
    public int SetClientProperties(nint properties);
    [PreserveSig]
    public int GetBufferSizeLimits(nint format, int eventDriven, out long minimumDuration100Ns, out long maximumDuration100Ns);
}

[GeneratedComInterface]
[Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42")]
internal partial interface IAudioClient3 : IAudioClient2
{
    [PreserveSig]
    public int GetSharedModeEnginePeriod(nint format, out uint defaultPeriodFrames, out uint fundamentalPeriodFrames,
        out uint minimumPeriodFrames, out uint maximumPeriodFrames);
    [PreserveSig]
    public int GetCurrentSharedModeEnginePeriod(out nint format, out uint currentPeriodFrames);
    [PreserveSig]
    public int InitializeSharedAudioStream(uint streamFlags, uint periodFrames, nint format, nint audioSessionGuid);
}

[GeneratedComInterface]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
internal partial interface IAudioRenderClient
{
    [PreserveSig]
    public int GetBuffer(uint framesRequested, out nint data);
    [PreserveSig]
    public int ReleaseBuffer(uint framesWritten, uint flags);
}

[GeneratedComInterface]
[Guid("CD63314F-3FBA-4A1B-812C-EF96358728E7")]
internal partial interface IAudioClock
{
    [PreserveSig]
    public int GetFrequency(out ulong frequency);
    [PreserveSig]
    public int GetPosition(out ulong position, out ulong qpcPosition100Ns);
    [PreserveSig]
    public int GetCharacteristics(out uint characteristics);
}
