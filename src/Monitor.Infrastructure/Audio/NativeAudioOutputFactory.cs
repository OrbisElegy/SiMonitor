// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Monitor.Infrastructure.Audio;

public readonly record struct NativeAudioStatus(uint SampleRate, uint Channels, uint PeriodFrames,
    uint BufferFrames, uint RetiredReason, uint MissingFrames, bool LowLatencyQualified);

public readonly record struct NativeAudioPeriodSnapshot(uint QueryStatus, uint DefaultFrames, uint FundamentalFrames,
    uint MinimumFrames, uint MaximumFrames, uint CurrentFrames, uint EngineSampleRate, uint HResult, uint EngineChannels);

// Explicit absolute library path; no DLL search-path fallback. All methods and
// Dispose belong to the serialized scheduler/control owner, never a callback.
public sealed class NativeAudioOutputFactory : IPumpedAudioOutput
{
    private nint _library;
    private readonly OpenCall _open;
    private readonly SubmitCall _submit;
    private readonly HandleCall _start;
    private readonly HandleCall _close;
    private readonly InfoCall _info;
    private readonly ClockCall? _clock;
    private Device? _device;

    // Production library beside the application, named for the current platform.
    public static string DefaultLibraryPath => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "sim_audio_native.dll" : OperatingSystem.IsMacOS() ? "libsim_audio_native.dylib" : "libsim_audio_native.so");

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
            if (NativeLibrary.TryGetExport(_library, "sa_clock_sample", out nint clock))
            { _clock = Marshal.GetDelegateForFunctionPointer<ClockCall>(clock); }
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

    // Optional keys return zero on older ABI1 libraries; zero is unavailable,
    // never evidence of a zero-millisecond device period.
    public NativeAudioPeriodSnapshot? PeriodSnapshot => _device is { } d ? new(
        _info(d.Handle, 10), _info(d.Handle, 11), _info(d.Handle, 12), _info(d.Handle, 13),
        _info(d.Handle, 14), _info(d.Handle, 15), _info(d.Handle, 16), _info(d.Handle, 17), _info(d.Handle, 18)) : null;

    // Bounded work: at most one queue capacity each invocation. The native
    // queue is the only latency target; managed PCM is drained immediately.
    public bool Pump() => _device?.Pump() == true;

    public NativeAudioClockSample ReadClock()
    {
        if (_device is null || _clock is null) { return new(-2, 0, 0, 0, 0); }
        int result = _clock(_device.Handle, out ulong position, out ulong frequency, out ulong qpc, out uint hr);
        return new(result, hr, position, frequency, qpc);
    }

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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ClockCall(nint handle, out ulong position, out ulong frequency, out ulong qpc100Ns, out uint hresult);
}
