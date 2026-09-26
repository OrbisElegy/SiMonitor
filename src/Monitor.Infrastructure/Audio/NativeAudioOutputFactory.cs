// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Monitor.Infrastructure.Audio;

public readonly record struct NativeAudioStatus(uint SampleRate, uint Channels, uint PeriodFrames,
    uint BufferFrames, uint RetiredReason, uint MissingFrames, bool LowLatencyQualified);

// Explicit absolute library path; no DLL search-path fallback. All methods and
// Dispose belong to the serialized scheduler/control owner, never a callback.
public sealed class NativeAudioOutputFactory : IAudioOutputFactory, IDisposable
{
    private nint _library;
    private readonly OpenCall _open;
    private readonly SubmitCall _submit;
    private readonly HandleCall _start;
    private readonly HandleCall _close;
    private readonly InfoCall _info;
    private Device? _device;

    public NativeAudioOutputFactory(string libraryPath, bool allowTestBackend = false)
    {
        if (!Path.IsPathFullyQualified(libraryPath)) { throw new ArgumentException("AudioNative.AbsolutePathRequired", nameof(libraryPath)); }
        _library = NativeLibrary.Load(libraryPath);
        try
        {
            if (Export<VersionCall>("sa_abi_version")() != 1) { throw new InvalidOperationException("AudioNative.UnsupportedAbi"); }
            if (!allowTestBackend && NativeLibrary.TryGetExport(_library, "sa_test_render", out _))
            { throw new InvalidOperationException("AudioNative.TestBackendRejected"); }
            _open = Export<OpenCall>("sa_open"); _submit = Export<SubmitCall>("sa_submit");
            _start = Export<HandleCall>("sa_start"); _close = Export<HandleCall>("sa_close");
            _info = Export<InfoCall>("sa_info");
        }
        catch
        {
            NativeLibrary.Free(_library); _library = 0; throw;
        }
    }

    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation)
    {
        ObjectDisposedException.ThrowIf(_library == 0, this);
        ArgumentNullException.ThrowIfNull(session);
        if (_device is not null) { throw new InvalidOperationException("AudioNative.DeviceStillOwned"); }
        if (deviceId?.Contains('\0', StringComparison.Ordinal) == true) { throw new ArgumentException("AudioNative.InvalidId", nameof(deviceId)); }
        if (session.RequiresReplacement) { return null; }
        if (_open(deviceId, (uint)(session.CapacityFrames / 48), out nint handle) != 0) { return null; }
        _device = new Device(this, handle, session);
        return _device;
    }

    public NativeAudioStatus? Status => _device is { } d ? new(
        _info(d.Handle, 1), _info(d.Handle, 2), _info(d.Handle, 4), _info(d.Handle, 5),
        _info(d.Handle, 6), _info(d.Handle, 7), _info(d.Handle, 9) != 0) : null;

    // Bounded work: at most one queue capacity each invocation. The native
    // queue is the only latency target; managed PCM is drained immediately.
    public bool Pump() => _device?.Pump() == true;

    public void Dispose()
    {
        if (_device is not null) { throw new InvalidOperationException("AudioNative.CloseBeforeUnload"); }
        if (_library != 0) { NativeLibrary.Free(_library); _library = 0; }
        GC.SuppressFinalize(this);
    }

    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private sealed class Device(NativeAudioOutputFactory owner, nint handle, AudioRenderSession session) : IAudioOutputDevice
    {
        private readonly float[] _scratch = new float[session.CapacityFrames];
        public nint Handle { get; private set; } = handle;

        public bool Start() => Handle != 0 && Pump() && owner._start(Handle) == 0;

        public bool Pump()
        {
            if (Handle == 0 || session.RequiresReplacement) { return false; }
            for (int budget = session.CapacityFrames; budget > 0;)
            {
                uint reason = owner._info(Handle, 6);
                if (reason != 0)
                {
                    if (reason == 1) { session.ReportNativeUnderrun(owner._info(Handle, 7)); }
                    else { session.Retire(); }
                    return false;
                }
                int free = (int)owner._info(Handle, 8);
                if (free == 0) { return true; }
                int frames = Math.Min(free, budget);
                if (session.BufferedFrames == 0)
                {
                    frames = Math.Min(frames, 240);
                    if (!session.TryProduce(frames)) { return false; }
                }
                frames = Math.Min(frames, session.BufferedFrames);
                if (session.Read(_scratch.AsSpan(0, frames)) != frames || owner._submit(Handle, _scratch, (uint)frames) != 0)
                { session.Retire(); return false; }
                budget -= frames;
            }
            return true;
        }

        public bool StopAndClose()
        {
            if (Handle == 0) { return true; }
            session.Retire();
            if (owner._close(Handle) != 0) { return false; }
            Handle = 0; owner._device = null;
            return true;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint VersionCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenCall([MarshalAs(UnmanagedType.LPUTF8Str)] string? id, uint capacityMilliseconds, out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SubmitCall(nint handle, [In] float[] pcm, uint frames);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleCall(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint InfoCall(nint handle, uint key);
}
