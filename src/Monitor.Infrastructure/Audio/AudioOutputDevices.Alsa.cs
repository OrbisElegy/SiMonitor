// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Monitor.Infrastructure.Audio;

public static partial class AudioOutputDevices
{
    [SupportedOSPlatform("linux")]
    private static List<AudioOutputDeviceInfo> EnumerateAlsa()
    {
        var devices = new List<AudioOutputDeviceInfo>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        nint hints = 0;
        try
        {
            if (snd_device_name_hint(-1, "pcm", out hints) < 0 || hints == 0) { return devices; }
            for (int index = 0; ; index++)
            {
                nint hint = Marshal.ReadIntPtr(hints, checked(index * IntPtr.Size));
                if (hint == 0) { break; }
                string? id = ReadAlsaHint(hint, "NAME");
                // The UI already has a default choice; null is a discard/test sink.
                if (string.IsNullOrWhiteSpace(id) || id is "default" or "null" ||
                    ReadAlsaHint(hint, "IOID") == "Input" || !ids.Add(id)) { continue; }
                string? description = ReadAlsaHint(hint, "DESC");
                string name = string.IsNullOrWhiteSpace(description) ? id :
                    string.Join(" · ", description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                devices.Add(new(id, name));
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            // A machine without alsa-lib still offers the UI's system-default choice.
        }
        finally
        {
            if (hints != 0) { _ = snd_device_name_free_hint(hints); }
        }
        return devices;
    }

    [SupportedOSPlatform("linux")]
    private static string? ReadAlsaHint(nint hint, string key)
    {
        nint value = snd_device_name_get_hint(hint, key);
        if (value == 0) { return null; }
        try { return Marshal.PtrToStringUTF8(value); }
        finally { FreeAlsaHintValue(value); }
    }

    [LibraryImport("libasound.so.2", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int snd_device_name_hint(int card, string iface, out nint hints);

    [LibraryImport("libasound.so.2", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint snd_device_name_get_hint(nint hint, string id);

    [LibraryImport("libasound.so.2")]
    private static partial int snd_device_name_free_hint(nint hints);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void FreeAlsaHintValue(nint value);
}
