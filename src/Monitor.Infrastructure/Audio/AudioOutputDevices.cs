// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Monitor.Infrastructure.Audio;

public sealed record AudioOutputDeviceInfo(string Id, string Name);

public static partial class AudioOutputDevices
{
    // Endpoint IDs are opaque and work with both native and managed WASAPI.
    public static IReadOnlyList<AudioOutputDeviceInfo> Enumerate() => OperatingSystem.IsWindows() ? EnumerateWindows() : [];

    [SupportedOSPlatform("windows")]
    private static List<AudioOutputDeviceInfo> EnumerateWindows()
    {
        var devices = new List<AudioOutputDeviceInfo>();
        int apartment = CoreAudio.InitializeMultithreaded();
        if (apartment < 0 && apartment != CoreAudio.ChangedModeError) { return devices; }
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            if (CoreAudio.CreateDeviceEnumerator(out nint pointer) < 0) { return devices; }
            enumerator = Wrap<IMMDeviceEnumerator>(pointer);
            if (enumerator.EnumAudioEndpoints(CoreAudio.RenderFlow, CoreAudio.DeviceStateActive, out pointer) < 0) { return devices; }
            collection = Wrap<IMMDeviceCollection>(pointer);
            if (collection.GetCount(out uint count) < 0) { return devices; }
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out pointer) < 0) { continue; }
                IMMDevice device = Wrap<IMMDevice>(pointer);
                IPropertyStore? properties = null;
                try
                {
                    if (device.GetId(out nint idPointer) < 0) { continue; }
                    string? id;
                    try { id = Marshal.PtrToStringUni(idPointer); }
                    finally { Marshal.FreeCoTaskMem(idPointer); }
                    if (string.IsNullOrEmpty(id)) { continue; }
                    string name = id;
                    if (device.OpenPropertyStore(0, out pointer) >= 0)
                    {
                        properties = Wrap<IPropertyStore>(pointer);
                        var key = new PropertyKey { FormatId = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
                        var value = new PropertyVariant();
                        try
                        {
                            if (properties.GetValue(in key, out value) >= 0 && value.Type == 31)
                            { name = Marshal.PtrToStringUni(value.Pointer) ?? id; }
                        }
                        finally { PropVariantClear(ref value); }
                    }
                    devices.Add(new(id, name));
                }
                finally { Release(properties); Release(device); }
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
            // Keep endpoints already read during a concurrent device change.
        }
        finally
        {
            Release(collection);
            Release(enumerator);
            if (apartment >= 0) { CoreAudio.CoUninitialize(); }
        }
        return devices;
    }

    [SupportedOSPlatform("windows")]
    private static T Wrap<T>(nint pointer) where T : class
    {
        try { return (T)CoreAudio.Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance); }
        finally { Marshal.Release(pointer); }
    }
    private static void Release(object? instance)
    {
        if (instance is ComObject comObject) { comObject.FinalRelease(); }
    }

    // PROPVARIANT includes a counted pointer pair in its union (24 bytes on x64).
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyVariant
    {
        public ushort Type, Reserved1, Reserved2, Reserved3;
        public nint Pointer;
        public nint UnionTail;
    }
    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropertyVariant value);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-C0A9C3A3CCB2")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig]
    public int GetCount(out uint count);
    [PreserveSig]
    public int Item(uint index, out nint device);
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    [PreserveSig]
    public int GetCount(out uint count);
    [PreserveSig]
    public int GetAt(uint index, out PropertyKey key);
    [PreserveSig]
    public int GetValue(in PropertyKey key, out AudioOutputDevices.PropertyVariant value);
    [PreserveSig]
    public int SetValue(in PropertyKey key, in AudioOutputDevices.PropertyVariant value);
    [PreserveSig]
    public int Commit();
}
