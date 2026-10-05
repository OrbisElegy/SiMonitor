// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Monitor.Infrastructure.Audio;

// Event-driven WASAPI shared-mode render stream. The owner thread opens,
// writes, waits and releases it; Core Audio notification threads only latch
// retirement and wake the owner. No managed code runs on an audio engine thread.
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WasapiRenderEndpoint : IRenderEndpoint
{
    private const int SampleRate = ToneVoice.SampleRate;
    private readonly bool _ownsApartment;
    private readonly EventWaitHandle _consumed = new(false, EventResetMode.AutoReset);
    private bool _eventReferenced;
    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioClient? _client;
    private IAudioRenderClient? _render;
    private IAudioClock? _clock;
    private NotificationClient? _notifications;
    private nint _mmcssTask;
    private int _channels;
    private int _retired;

    private WasapiRenderEndpoint(bool ownsApartment) => _ownsApartment = ownsApartment;

    public int BufferFrames { get; private set; }
    public int PeriodFrames { get; private set; }
    public int Channels => _channels;
    // Mix-format float stream, Windows sample-rate conversion, or IAudioClient3 period.
    public WasapiStreamPath Path { get; private set; }
    public bool IsRetired => Volatile.Read(ref _retired) != 0;

    // Null when no device, format or stream is available; partial resources are released.
    public static WasapiRenderEndpoint? Open(string? deviceId, int bufferFrames, int queueTargetFrames)
    {
        int apartment = CoreAudio.InitializeMultithreaded();
        if (apartment < 0 && apartment != CoreAudio.ChangedModeError) { return null; }
        var endpoint = new WasapiRenderEndpoint(apartment >= 0);
        try
        {
            if (endpoint.TryOpen(deviceId, bufferFrames, queueTargetFrames)) { return endpoint; }
        }
        catch (Exception error) when (error is InvalidCastException or COMException or OverflowException)
        {
        }
        endpoint.Dispose();
        return null;
    }

    private bool TryOpen(string? deviceId, int bufferFrames, int queueTargetFrames)
    {
        if (CoreAudio.CreateDeviceEnumerator(out nint enumerator) < 0) { return false; }
        _enumerator = Wrap<IMMDeviceEnumerator>(enumerator);
        int result = deviceId is null
            ? _enumerator.GetDefaultAudioEndpoint(CoreAudio.RenderFlow, CoreAudio.ConsoleRole, out nint device)
            : _enumerator.GetDevice(deviceId, out device);
        if (result < 0) { return false; }
        _device = Wrap<IMMDevice>(device);
        if (_device.GetState(out uint state) < 0 || state != CoreAudio.DeviceStateActive) { return false; }
        if (_device.GetId(out nint idPointer) < 0) { return false; }
        string openedId = Marshal.PtrToStringUni(idPointer) ?? "";
        Marshal.FreeCoTaskMem(idPointer);
        if (_device.Activate(CoreAudio.AudioClientId, 0x17, 0, out nint client) < 0) { return false; }
        _client = Wrap<IAudioClient>(client);
        if (!InitializeStream(bufferFrames, queueTargetFrames)) { return false; }
        if (_client.GetBufferSize(out uint actualBufferFrames) < 0 || actualBufferFrames == 0) { return false; }
        BufferFrames = checked((int)actualBufferFrames);
        _consumed.SafeWaitHandle.DangerousAddRef(ref _eventReferenced);
        if (_client.SetEventHandle(_consumed.SafeWaitHandle.DangerousGetHandle()) < 0) { return false; }
        if (_client.GetService(CoreAudio.AudioRenderClientId, out nint render) < 0) { return false; }
        _render = Wrap<IAudioRenderClient>(render);
        if (_client.GetService(CoreAudio.AudioClockId, out nint clock) >= 0) { _clock = Wrap<IAudioClock>(clock); }
        _notifications = new NotificationClient(this, openedId, followsDefault: deviceId is null);
        return _enumerator.RegisterEndpointNotificationCallback(_notifications) >= 0;
    }

    // Float 48 kHz mix formats are written directly, using an IAudioClient3
    // period when the default engine period is too long for the queue target.
    // Other mix formats let Windows convert from a 48 kHz mono float stream.
    private bool InitializeStream(int bufferFrames, int queueTargetFrames)
    {
        if (_client!.GetMixFormat(out nint mixFormat) < 0) { return false; }
        try
        {
            uint flags = CoreAudio.StreamFlagsEventCallback | CoreAudio.StreamFlagsNoPersist;
            long bufferDuration = (long)bufferFrames * CoreAudio.HundredNanosecondsPerSecond / SampleRate;
            if (_client.GetDevicePeriod(out long defaultPeriod, out _) < 0) { return false; }
            int defaultPeriodFrames = (int)((defaultPeriod * SampleRate + CoreAudio.HundredNanosecondsPerSecond - 1) / CoreAudio.HundredNanosecondsPerSecond);
            if (IsFloat48k((WaveFormat*)mixFormat, out int channels))
            {
                _channels = channels;
                if (2 * defaultPeriodFrames > queueTargetFrames && _client is IAudioClient3 lowLatency &&
                    TryInitializeShortPeriod(lowLatency, mixFormat, flags, queueTargetFrames)) { return true; }
                PeriodFrames = defaultPeriodFrames;
                Path = WasapiStreamPath.MixFormat;
                return _client.Initialize(CoreAudio.SharedMode, flags, bufferDuration, 0, mixFormat, 0) >= 0;
            }
            var mono = new WaveFormat
            {
                FormatTag = CoreAudio.WaveFormatIeeeFloat,
                Channels = 1,
                SamplesPerSecond = SampleRate,
                AverageBytesPerSecond = SampleRate * sizeof(float),
                BlockAlign = sizeof(float),
                BitsPerSample = 32,
                ExtraSize = 0,
            };
            _channels = 1;
            PeriodFrames = defaultPeriodFrames;
            Path = WasapiStreamPath.WindowsConversion;
            flags |= CoreAudio.StreamFlagsAutoConvertPcm | CoreAudio.StreamFlagsSrcDefaultQuality;
            return _client.Initialize(CoreAudio.SharedMode, flags, bufferDuration, 0, (nint)(&mono), 0) >= 0;
        }
        finally { Marshal.FreeCoTaskMem(mixFormat); }
    }

    // Largest supported period that still keeps two periods within the target.
    private bool TryInitializeShortPeriod(IAudioClient3 client, nint mixFormat, uint flags, int queueTargetFrames)
    {
        if (client.GetSharedModeEnginePeriod(mixFormat, out _, out uint fundamental, out uint minimum, out uint maximum) < 0 || fundamental == 0) { return false; }
        uint limit = Math.Min(maximum, (uint)queueTargetFrames / 2);
        if (limit < minimum) { return false; }
        uint period = minimum + (limit - minimum) / fundamental * fundamental;
        if (client.InitializeSharedAudioStream(flags, period, mixFormat, 0) < 0) { return false; }
        PeriodFrames = (int)period;
        Path = WasapiStreamPath.ShortEnginePeriod;
        return true;
    }

    private static bool IsFloat48k(WaveFormat* format, out int channels)
    {
        channels = format->Channels;
        if (format->SamplesPerSecond != SampleRate || format->BitsPerSample != 32 || channels < 1 ||
            format->BlockAlign != channels * sizeof(float)) { return false; }
        if (format->FormatTag == CoreAudio.WaveFormatIeeeFloat) { return true; }
        return format->FormatTag == CoreAudio.WaveFormatExtensible && format->ExtraSize >= 22 &&
            ((WaveFormatExtensible*)format)->SubFormat == CoreAudio.IeeeFloatSubFormat;
    }

    public bool TryGetPadding(out int queuedFrames)
    {
        queuedFrames = 0;
        if (_client!.GetCurrentPadding(out uint padding) < 0)
        {
            Retire();
            return false;
        }
        queuedFrames = (int)padding;
        return true;
    }

    public bool TryWrite(ReadOnlySpan<float> monoFrames)
    {
        if (_render!.GetBuffer((uint)monoFrames.Length, out nint data) < 0) { return false; }
        RenderFrames.ExpandMono(monoFrames, new Span<float>((void*)data, monoFrames.Length * _channels), _channels);
        return _render.ReleaseBuffer((uint)monoFrames.Length, 0) >= 0;
    }

    public bool Start()
    {
        if (_client!.Start() < 0) { return false; }
        // Pro Audio scheduling for the owner thread that refills this stream.
        uint taskIndex = 0;
        _mmcssTask = CoreAudio.AvSetMmThreadCharacteristicsW("Pro Audio", ref taskIndex);
        return true;
    }

    // Shared-mode WASAPI runs no callback of ours, so releasing is always safe.
    public bool Stop()
    {
        _client?.Stop();
        if (_mmcssTask != 0)
        {
            CoreAudio.AvRevertMmThreadCharacteristics(_mmcssTask);
            _mmcssTask = 0;
        }
        return true;
    }

    public void Wait(int timeoutMilliseconds) => _consumed.WaitOne(timeoutMilliseconds);

    public NativeAudioClockSample ReadClock()
    {
        if (_clock is null || IsRetired) { return new(IsRetired ? -4 : -2, 0, 0, 0, 0); }
        int result = _clock.GetFrequency(out ulong frequency);
        if (result != 0) { return new(-2, (uint)result, 0, 0, 0); }
        result = _clock.GetPosition(out ulong position, out ulong qpc);
        if (result < 0 || frequency == 0) { return new(-2, (uint)result, 0, 0, 0); }
        // S_FALSE keeps reduced accuracy visible, matching the native clock sample.
        return new(result == 0 ? 0 : 1, (uint)result, position, frequency, qpc);
    }

    public void Dispose()
    {
        Stop();
        if (_notifications is not null) { _enumerator?.UnregisterEndpointNotificationCallback(_notifications); }
        Release(ref _clock);
        Release(ref _render);
        Release(ref _client);
        Release(ref _device);
        Release(ref _enumerator);
        if (_eventReferenced)
        {
            _consumed.SafeWaitHandle.DangerousRelease();
            _eventReferenced = false;
        }
        Interlocked.Exchange(ref _retired, 1);
        _consumed.Dispose();
        if (_ownsApartment)
        {
            CoreAudio.CoUninitialize();
        }
    }

    private void Retire()
    {
        Interlocked.Exchange(ref _retired, 1);
        // A late notification may race disposal; retirement is already latched.
        try { _consumed.Set(); } catch (ObjectDisposedException) { }
    }

    private static T Wrap<T>(nint pointer) where T : class
    {
        try { return (T)CoreAudio.Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance); }
        finally { Marshal.Release(pointer); }
    }

    private static void Release<T>(ref T? instance) where T : class
    {
        if (instance is ComObject comObject) { comObject.FinalRelease(); }
        instance = null;
    }

    // Runs on Core Audio notification threads: compare IDs and latch only.
    [GeneratedComClass]
    private sealed partial class NotificationClient(WasapiRenderEndpoint owner, string deviceId, bool followsDefault) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(nint deviceId, uint newState)
        {
            if (newState != CoreAudio.DeviceStateActive && Matches(deviceId)) { owner.Retire(); }
            return 0;
        }

        public int OnDeviceAdded(nint deviceId) => 0;

        public int OnDeviceRemoved(nint deviceId)
        {
            if (Matches(deviceId)) { owner.Retire(); }
            return 0;
        }

        public int OnDefaultDeviceChanged(int dataFlow, int role, nint defaultDeviceId)
        {
            if (followsDefault && dataFlow == CoreAudio.RenderFlow && role == CoreAudio.ConsoleRole) { owner.Retire(); }
            return 0;
        }

        public int OnPropertyValueChanged(nint deviceId, PropertyKey key) => 0;

        private bool Matches(nint changedDeviceId) => changedDeviceId != 0 &&
            string.Equals(Marshal.PtrToStringUni(changedDeviceId), deviceId, StringComparison.OrdinalIgnoreCase);
    }
}

public enum WasapiStreamPath
{
    None,
    MixFormat,
    ShortEnginePeriod,
    WindowsConversion
}
