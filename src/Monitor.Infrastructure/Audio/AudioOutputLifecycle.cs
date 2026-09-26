// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

public enum AudioOutputState { Stopped, Running, DeviceLost, StopFailed }
public enum AudioOutputFailure { None, Open, Start, Stop, Underrun, DeviceLost, SessionRetired }

// Adapter boundary, not an implementation of WASAPI/ASIO. Expected SDK failures
// become return values; callbacks never call the lifecycle owner directly.
public interface IAudioOutputDevice
{
    public bool Start();
    // Success means all callbacks joined AND resources released. On failure
    // keep ownership and allow retry; do not dispose a possibly active handle.
    public bool StopAndClose();
}

public interface IAudioOutputFactory
{
    // null deviceId follows system default. Return a stopped device; null means
    // failure with no resources/callbacks retained by the factory. Session is
    // 48kHz mono: adapter must convert explicitly if native format differs.
    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation);
}

// Single scheduler/control owner. Serializes producer commands with lifecycle
// changes; only the device's consumer callback may run concurrently. Public
// state is a control-thread snapshot, not a UI notification or alarm engine.
public sealed class AudioOutputLifecycle
{
    private readonly IAudioOutputFactory _factory;
    private IAudioOutputDevice? _device;
    private AudioRenderSession? _session;

    public AudioOutputLifecycle(IAudioOutputFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public AudioOutputState State { get; private set; }
    public AudioOutputFailure Failure { get; private set; }
    public long Generation { get; private set; }
    public AudioRenderSession? Session => State == AudioOutputState.Running ? _session : null;

    public bool Replace(string? deviceId, long initialFrame, int capacityMilliseconds = 40)
    {
        if (deviceId is not null && string.IsNullOrWhiteSpace(deviceId)) { throw new ArgumentException("AudioOutput.InvalidDevice", nameof(deviceId)); }
        // Validate/allocate before touching healthy output. New session owns no
        // old voices, PCM or clock mapping. Prime silence before starting I/O.
        var candidate = new AudioRenderSession(initialFrame, capacityMilliseconds);
        long nextGeneration = checked(Generation + 1);
        candidate.TryProduce(ToneVoice.SampleRate * capacityMilliseconds / 1000);
        if (!CloseCurrent()) { return false; }
        Generation = nextGeneration;
        State = AudioOutputState.DeviceLost; Failure = AudioOutputFailure.Open;
        _device = _factory.Open(deviceId, candidate, Generation);
        if (_device is null) { candidate.Retire(); return false; }
        _session = candidate;
        Failure = AudioOutputFailure.Start;
        if (!_device.Start())
        {
            CloseCurrent();
            return false;
        }
        State = AudioOutputState.Running; Failure = AudioOutputFailure.None;
        return true;
    }

    // Called by the scheduler, never inside a native callback. No automatic
    // retry loop: the owner decides when a fresh device/clock can be established.
    public bool CheckHealth()
    {
        if (State != AudioOutputState.Running) { return false; }
        if (!_session!.RequiresReplacement) { return true; }
        Failure = _session.UnderrunFrames > 0 ? AudioOutputFailure.Underrun : AudioOutputFailure.SessionRetired;
        State = AudioOutputState.DeviceLost;
        CloseCurrent();
        return false;
    }

    // Marshal native device-loss notifications to the control thread first.
    public void DeviceLost(long generation)
    {
        if (generation != Generation || _device is null) { return; }
        Failure = AudioOutputFailure.DeviceLost; State = AudioOutputState.DeviceLost;
        CloseCurrent();
    }

    public bool Stop()
    {
        if (!CloseCurrent()) { return false; }
        State = AudioOutputState.Stopped; Failure = AudioOutputFailure.None;
        return true;
    }

    private bool CloseCurrent()
    {
        _session?.Retire();
        if (_device is not null)
        {
            // Set failure state first, so even an unexpected adapter exception
            // cannot expose the retired session or permit an unjoined replacement.
            var previousFailure = Failure;
            State = AudioOutputState.StopFailed; Failure = AudioOutputFailure.Stop;
            if (!_device.StopAndClose()) { return false; }
            Failure = previousFailure;
        }
        _device = null; _session = null;
        State = AudioOutputState.DeviceLost;
        return true;
    }
}
