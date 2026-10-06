// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class EndpointAudioOutputSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(QueueTargetKeepsTwoPeriodsWithinTheStreamBuffer), QueueTargetKeepsTwoPeriodsWithinTheStreamBuffer),
        new(nameof(StagedPcmDrainsThenNewPcmTopsUpToTarget), StagedPcmDrainsThenNewPcmTopsUpToTarget),
        new(nameof(SmallStreamBufferKeepsStagedPcmUntilSpaceFrees), SmallStreamBufferKeepsStagedPcmUntilSpaceFrees),
        new(nameof(StarvationLatchesUnderrunWithoutLateRefill), StarvationLatchesUnderrunWithoutLateRefill),
        new(nameof(EmptyQueueRefillsWhileTheLastPacketIsStillPlaying), EmptyQueueRefillsWhileTheLastPacketIsStillPlaying),
        new(nameof(DeviceLossRetiresWithoutReportingUnderrun), DeviceLossRetiresWithoutReportingUnderrun),
        new(nameof(LifecycleReportsEndpointUnderrunAndReleasesStream), LifecycleReportsEndpointUnderrunAndReleasesStream),
        new(nameof(FailedStopRetainsStreamUntilRetry), FailedStopRetainsStreamUntilRetry),
        new(nameof(OpenValidatesInputsAndReportsUnavailableDevices), OpenValidatesInputsAndReportsUnavailableDevices),
        new(nameof(MonoFramesFillTheFrontPairOnly), MonoFramesFillTheFrontPairOnly),
        new(nameof(WasapiOutputValidatesTargetAndIsUnavailableOffWindows), WasapiOutputValidatesTargetAndIsUnavailableOffWindows),
        new(nameof(ExplicitSelectionOverridesPlatformDefaultOutput), ExplicitSelectionOverridesPlatformDefaultOutput),
        new(nameof(ManagedAudioCommandRejectsBadArgumentsWithoutOpening), ManagedAudioCommandRejectsBadArgumentsWithoutOpening),
        new(nameof(AlsaNullDeviceDrivesTheEndpointPump), AlsaNullDeviceDrivesTheEndpointPump),
    ];

    private static void QueueTargetKeepsTwoPeriodsWithinTheStreamBuffer()
    {
        foreach ((int targetMilliseconds, int periodFrames, int bufferFrames, int expectedFrames) in new[]
        {
            (5, 480, 1920, 960),
            (20, 0, 1920, 960),
            (30, 480, 1920, 1440),
            (100, 480, 1920, 1920),
            (20, 480, 1000, 960),
            (30, 480, 1000, 1000),
            (5, 128, 1920, 256),
        })
        {
            var endpoint = new FakeEndpoint(bufferFrames, periodFrames);
            using var output = new EndpointAudioOutput((_, _) => endpoint, targetMilliseconds);
            var device = output.Open(null, new AudioRenderSession(), 1)!;
            Check.That(output.QueueTargetFrames == expectedFrames && endpoint.WakeThreshold == expectedFrames / 2,
                "target keeps two periods, the requested time and the stream buffer limit; the owner wakes at half the target");
            Check.That(device.StopAndClose() && output.QueueTargetFrames is null, "closed stream detaches its target");
        }
        Check.That(AudioQueueTarget.PeriodFrames48k(441, 44_100) == 480 && AudioQueueTarget.PeriodFrames48k(128, 44_100) == 140 &&
            AudioQueueTarget.PeriodFrames48k(480, 0) == 0, "device periods round up to 48 kHz frames; unknown rate is no period");
    }

    private static void StagedPcmDrainsThenNewPcmTopsUpToTarget()
    {
        var endpoint = new FakeEndpoint();
        using var output = new EndpointAudioOutput((_, _) => endpoint, NativeAudioOutputFactory.DefaultQueueTargetMilliseconds);
        var session = new AudioRenderSession();
        Check.That(session.TryProduce(session.CapacityFrames), "prime one queue capacity like the lifecycle");
        var device = output.Open(null, session, 1)!;
        Check.That(device.Start() && endpoint.Started && endpoint.QueuedFrames == 1920 && session.BufferedFrames == 0,
            "start drains staged PCM into the stream before playback");
        endpoint.Consume(1440);
        Check.That(output.Pump() && endpoint.QueuedFrames == 960 && session.RenderedThroughFrame == 2400,
            "new PCM tops the stream up to the 20 ms target only");
        Check.That(output.Pump() && session.RenderedThroughFrame == 2400, "stream at target leaves tone phase untouched");
        endpoint.Consume(100);
        Check.That(output.Pump() && endpoint.QueuedFrames == 960 && session.RenderedThroughFrame == 2500, "each pump replaces consumed frames");
        Check.That(device.StopAndClose() && endpoint.Disposed, "stop precedes release");
    }

    private static void SmallStreamBufferKeepsStagedPcmUntilSpaceFrees()
    {
        var endpoint = new FakeEndpoint(bufferFrames: 1000);
        using var output = new EndpointAudioOutput((_, _) => endpoint, NativeAudioOutputFactory.DefaultQueueTargetMilliseconds);
        var session = new AudioRenderSession();
        session.TryProduce(session.CapacityFrames);
        var device = output.Open(null, session, 1)!;
        Check.That(device.Start() && endpoint.QueuedFrames == 1000 && session.BufferedFrames == 920, "writes never exceed the stream buffer");
        endpoint.Consume(600);
        Check.That(output.Pump() && endpoint.QueuedFrames == 1000 && session.BufferedFrames == 320, "staged PCM drains as space frees");
        endpoint.Consume(600);
        Check.That(output.Pump() && session.BufferedFrames == 0 && endpoint.QueuedFrames == 960 && session.RenderedThroughFrame == 2160,
            "staged PCM drains first; the same pump then tops new PCM up to the target");
        Check.That(output.Pump() && session.RenderedThroughFrame == 2160, "stream at target leaves tone phase untouched");
        Check.That(device.StopAndClose(), "small stream closes");
    }

    private static void StarvationLatchesUnderrunWithoutLateRefill()
    {
        var endpoint = new FakeEndpoint();
        using var output = new EndpointAudioOutput((_, _) => endpoint, NativeAudioOutputFactory.DefaultQueueTargetMilliseconds);
        var session = new AudioRenderSession();
        session.TryProduce(session.CapacityFrames);
        var device = output.Open(null, session, 1)!;
        Check.That(device.Start(), "stream starts");
        endpoint.Consume(endpoint.QueuedFrames + 1);
        long written = endpoint.WrittenFrames;
        Check.That(!output.Pump() && session.RequiresReplacement && session.UnderrunFrames > 0, "a backend-confirmed underrun retires the stream");
        Check.That(!output.Pump() && endpoint.WrittenFrames == written, "a starved stream never receives late PCM");
        Check.That(device.StopAndClose() && endpoint.Disposed, "starved stream still closes");
    }

    private static void DeviceLossRetiresWithoutReportingUnderrun()
    {
        foreach (string failure in new[] { "retired", "padding", "write" })
        {
            var endpoint = new FakeEndpoint();
            using var output = new EndpointAudioOutput((_, _) => endpoint, NativeAudioOutputFactory.DefaultQueueTargetMilliseconds);
            var session = new AudioRenderSession();
            var device = output.Open(null, session, 1)!;
            Check.That(device.Start(), "stream starts");
            endpoint.Consume(endpoint.QueuedFrames / 2);
            endpoint.IsRetired = failure == "retired";
            endpoint.PaddingFails = failure == "padding";
            endpoint.WriteFails = failure == "write";
            Check.That(!output.Pump() && session.RequiresReplacement && session.UnderrunFrames == 0,
                "device change, invalidation and write failure retire the session as device loss");
            Check.That(device.StopAndClose(), "lost stream closes");
        }
    }

    private static void EmptyQueueRefillsWhileTheLastPacketIsStillPlaying()
    {
        var endpoint = new FakeEndpoint(bufferFrames: 960);
        using var output = new EndpointAudioOutput((_, _) => endpoint, 20);
        var lifecycle = new AudioOutputLifecycle(output);
        Check.That(lifecycle.Replace(null, 0), "stream starts with two engine periods queued");
        // The engine takes the last complete packet; it has not requested any
        // missing frames. Refilling now can still meet the next engine deadline.
        endpoint.Consume(480);
        endpoint.Consume(480);
        Check.That(endpoint.QueuedFrames == 0 && !endpoint.Underrun, "empty queue does not imply missing PCM");
        Check.That(output.Pump() && lifecycle.CheckHealth() && lifecycle.Session!.UnderrunFrames == 0 &&
            endpoint.QueuedFrames == 960, "staged PCM refills the empty queue without retiring playback");
        endpoint.Consume(960);
        Check.That(output.Pump() && lifecycle.CheckHealth() && endpoint.QueuedFrames == 960,
            "fresh PCM also refills a healthy empty queue");
        Check.That(lifecycle.Stop(), "healthy stream stops normally");
    }

    private static void LifecycleReportsEndpointUnderrunAndReleasesStream()
    {
        var endpoint = new FakeEndpoint();
        using var output = new EndpointAudioOutput((_, _) => endpoint, NativeAudioOutputFactory.DefaultQueueTargetMilliseconds);
        var lifecycle = new AudioOutputLifecycle(output);
        Check.That(lifecycle.Replace(null, 0) && lifecycle.State == AudioOutputState.Running && endpoint.QueuedFrames == 1920,
            "lifecycle primes silence and starts the endpoint stream");
        endpoint.Consume(endpoint.QueuedFrames + 1);
        Check.That(!output.Pump() && !lifecycle.CheckHealth() && lifecycle.Failure == AudioOutputFailure.Underrun && endpoint.Disposed,
            "health check maps starvation to underrun and releases the stream");
    }

    private static void FailedStopRetainsStreamUntilRetry()
    {
        var endpoint = new FakeEndpoint { StopFails = true };
        int opens = 0;
        using var output = new EndpointAudioOutput((_, _) => { opens++; return endpoint; }, NativeAudioOutputFactory.DefaultQueueTargetMilliseconds);
        var device = output.Open(null, new AudioRenderSession(), 1)!;
        Check.That(device.Start() && !device.StopAndClose() && !endpoint.Disposed && output.QueueTargetFrames is not null,
            "failed stop keeps the stream owned");
        bool rejected = false;
        try { output.Open(null, new AudioRenderSession(), 2); } catch (InvalidOperationException) { rejected = true; }
        Check.That(rejected && opens == 1, "a retained stream blocks a second stream");
        endpoint.StopFails = false;
        Check.That(device.StopAndClose() && endpoint.Disposed && output.QueueTargetFrames is null && device.StopAndClose(),
            "retry releases the stream once; later closes are idempotent");
        var replacement = new FakeEndpoint();
        endpoint = replacement;
        Check.That(output.Open(null, new AudioRenderSession(), 3) is not null && opens == 2, "a released stream allows a new stream");
    }

    private static void OpenValidatesInputsAndReportsUnavailableDevices()
    {
        foreach (int invalid in new[] { 4, 101 })
        {
            bool rejected = false;
            try { _ = new EndpointAudioOutput((_, _) => null, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check.That(rejected, "queue target outside 5-100 ms is rejected");
        }
        string? requestedDevice = "unset";
        int requestedFrames = 0;
        var output = new EndpointAudioOutput((id, frames) => { requestedDevice = id; requestedFrames = frames; return null; }, 20);
        Check.That(output.Open("{0.0.0.00000000}.{a}", new AudioRenderSession(), 1) is null &&
            requestedDevice == "{0.0.0.00000000}.{a}" && requestedFrames == 1920, "unavailable device maps to null with the requested stream size");
        bool invalidId = false;
        try { output.Open("bad\0id", new AudioRenderSession(), 1); } catch (ArgumentException) { invalidId = true; }
        Check.That(invalidId, "embedded NUL device IDs are rejected");
        var retired = new AudioRenderSession();
        retired.Retire();
        requestedFrames = 0;
        Check.That(output.Open(null, retired, 1) is null && requestedFrames == 0, "retired session never opens a stream");
        foreach (int invalid in new[] { 0, 1001 })
        {
            bool rejected = false;
            try { output.WaitForQueueSpace(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check.That(rejected, "wait timeout outside 1-1000 ms is rejected");
        }
        output.WaitForQueueSpace(1);
        Check.That(output.ReadClock().Result == -2, "no stream reports an unavailable clock");
        var endpoint = new FakeEndpoint();
        output = new EndpointAudioOutput((_, _) => endpoint, 20);
        Check.That(output.Open(null, new AudioRenderSession(), 1)!.Start(), "stream starts");
        output.WaitForQueueSpace(10);
        Check.That(endpoint.Waits is [10] && output.ReadClock() is { Result: 0, DeviceFrequency: 48_000 }, "wait and clock reach the open stream");
        output.Dispose();
        Check.That(endpoint.Disposed && !endpoint.Started, "dispose stops and releases an open stream instead of throwing");
        bool disposed = false;
        try { output.Open(null, new AudioRenderSession(), 2); } catch (ObjectDisposedException) { disposed = true; }
        Check.That(disposed, "disposed output cannot open streams");
    }

    private static void MonoFramesFillTheFrontPairOnly()
    {
        float[] mono = [0.25f, -0.5f];
        float[] single = new float[2];
        RenderFrames.ExpandMono(mono, single, 1);
        float[] stereo = new float[4];
        RenderFrames.ExpandMono(mono, stereo, 2);
        float[] surround = Enumerable.Repeat(1f, 12).ToArray();
        RenderFrames.ExpandMono(mono, surround, 6);
        Check.That(single.SequenceEqual(mono) && stereo.SequenceEqual([0.25f, 0.25f, -0.5f, -0.5f]) &&
            surround.SequenceEqual([0.25f, 0.25f, 0, 0, 0, 0, -0.5f, -0.5f, 0, 0, 0, 0]), "mono reaches the front pair; other channels are silent");
        bool mismatch = false;
        try { RenderFrames.ExpandMono(mono, new float[3], 2); } catch (ArgumentException) { mismatch = true; }
        Check.That(mismatch, "interleaved length must match frames and channels");
    }

    private static void WasapiOutputValidatesTargetAndIsUnavailableOffWindows()
    {
        bool rejected = false;
        try { using var invalid = new WasapiAudioOutput(queueTargetMilliseconds: 4); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected, "managed WASAPI output validates its queue target");
        using var output = new WasapiAudioOutput();
        Check.That(output.StreamPath == WasapiStreamPath.None && output.QueueTargetFrames is null, "no stream before open");
        // Opening on Windows would start a real device, so only other platforms check unavailability.
        if (OperatingSystem.IsWindows()) { return; }
        var lifecycle = new AudioOutputLifecycle(output);
        Check.That(!lifecycle.Replace(null, 0) && lifecycle.Failure == AudioOutputFailure.Open && output.StreamPath == WasapiStreamPath.None,
            "other platforms report an unavailable output instead of throwing");
    }

    private static void ExplicitSelectionOverridesPlatformDefaultOutput()
    {
        var platformDefault = OperatingSystem.IsLinux() ? AudioOutputBackend.Alsa : AudioOutputBackend.Native;
        Check.That(AudioOutputSelection.Resolve("wasapi") == AudioOutputBackend.Wasapi && AudioOutputSelection.Resolve(" ALSA ") == AudioOutputBackend.Alsa &&
            AudioOutputSelection.Resolve("Native") == AudioOutputBackend.Native, "explicit selections ignore case and surrounding spaces");
        Check.That(new string?[] { null, "", "wasapi2", "pulse" }.All(value => AudioOutputSelection.Resolve(value) == platformDefault),
            "unset or unknown selections use ALSA on Linux and the native library elsewhere");
        using var managed = AudioOutputSelection.Create(AudioOutputBackend.Wasapi);
        using var alsa = AudioOutputSelection.Create(AudioOutputBackend.Alsa);
        Check.That(managed is WasapiAudioOutput && alsa is AlsaAudioOutput, "managed selections create outputs without loading a library");
    }

    private static void ManagedAudioCommandRejectsBadArgumentsWithoutOpening()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Check.That(ManagedAudioCommand.Execute(["--audio-wasapi-unknown"], output, error, default) == 2 &&
            ManagedAudioCommand.Execute(["--audio-alsa-audition", "a", "b"], output, error, default) == 2,
            "unknown commands and extra arguments are usage errors");
        Check.That(ManagedAudioCommand.Execute(["--audio-wasapi-audition"], output, error, new CancellationToken(true)) == 130 && output.ToString().Length == 0,
            "cancelled audition opens no device");
        if (OperatingSystem.IsWindows()) { return; }
        Check.That(ManagedAudioCommand.Execute(["--audio-wasapi-diagnostics"], output, error, default) == 1 && error.ToString().Contains("unavailable", StringComparison.Ordinal),
            "WASAPI diagnostics are unavailable off Windows");
    }

    // ALSA's null PCM discards audio, so this exercises the real library without sound.
    private static void AlsaNullDeviceDrivesTheEndpointPump()
    {
        if (!OperatingSystem.IsLinux() || !NativeLibrary.TryLoad("libasound.so.2", out nint library)) { return; }
        NativeLibrary.Free(library);
        using var output = new AlsaAudioOutput();
        var lifecycle = new AudioOutputLifecycle(output);
        // The null PCM consumes instantly, so steady-state pacing needs a real device.
        Check.That(lifecycle.Replace("null", 0) && output.BufferFrames > 0 && output.QueueTargetFrames > 0 &&
            output.Pump() && lifecycle.CheckHealth() && lifecycle.Session!.UnderrunFrames == 0 && lifecycle.Stop(),
            "ALSA null device opens, starts, refills its immediately consumed buffer without a false xrun, and stops");
        Check.That(new AlsaAudioOutput().Open("no-such-alsa-pcm", new AudioRenderSession(), 1) is null, "unknown PCM names are unavailable");
    }

    private sealed class FakeEndpoint(int bufferFrames = 1920, int periodFrames = 480) : IRenderEndpoint
    {
        private int _queuedFrames;
        public int BufferFrames => bufferFrames;
        public int PeriodFrames => periodFrames;
        public bool IsRetired { get; set; }
        public bool PaddingFails { get; set; }
        public bool WriteFails { get; set; }
        public bool StopFails { get; set; }
        public bool Underrun { get; private set; }
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public int QueuedFrames => _queuedFrames;
        public long WrittenFrames { get; private set; }
        public List<int> Waits { get; } = [];
        public int? WakeThreshold { get; private set; }
        public void SetWakeThreshold(int queuedFrames) => WakeThreshold = queuedFrames;

        public RenderEndpointReadiness ReadPadding(out int queuedFrames)
        {
            queuedFrames = _queuedFrames;
            return PaddingFails ? RenderEndpointReadiness.Unavailable :
                Underrun ? RenderEndpointReadiness.Underrun : RenderEndpointReadiness.Ready;
        }

        public bool TryWrite(ReadOnlySpan<float> monoFrames)
        {
            Check.That(_queuedFrames + monoFrames.Length <= bufferFrames, "writes never exceed the stream buffer");
            if (WriteFails) { return false; }
            _queuedFrames += monoFrames.Length;
            WrittenFrames += monoFrames.Length;
            return true;
        }

        public void Consume(int frames)
        {
            Underrun |= frames > _queuedFrames;
            _queuedFrames -= Math.Min(frames, _queuedFrames);
        }

        public bool Start()
        {
            Started = true;
            return true;
        }

        public bool Stop()
        {
            if (StopFails) { return false; }
            Started = false;
            return true;
        }

        public void Wait(int timeoutMilliseconds) => Waits.Add(timeoutMilliseconds);
        public NativeAudioClockSample ReadClock() => new(0, 0, 4800, 48_000, 1);

        public void Dispose()
        {
            Check.That(!Started && !Disposed, "release once, after stop");
            Disposed = true;
        }
    }
}
