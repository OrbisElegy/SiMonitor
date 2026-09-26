// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class NativeAudioCommand
{
    public static int Execute(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        bool check = args.Length > 0 && args[0] == "--audio-native-check";
        if (args.Length is < 2 or > 3 || args[0] is not ("--audio-native-audition" or "--audio-native-diagnostics" or "--audio-native-clock-probe" or "--audio-native-check") ||
            !Path.IsPathFullyQualified(args[1]) || (check && args.Length != 2))
        { error.WriteLine("Usage: --audio-native-audition|--audio-native-diagnostics|--audio-native-clock-probe ABSOLUTE_LIBRARY [DEVICE_ID] | --audio-native-check ABSOLUTE_TEST_LIBRARY"); return 2; }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (check) { VerifyBinding(args[1]); output.WriteLine("PASS managed/native PCM, single queue, retirement and unload ownership"); return 0; }
            var factory = new NativeAudioOutputFactory(args[1]);
            if (args[0] == "--audio-native-diagnostics")
            { return Diagnose(args, factory, output, error); }
            var lifecycle = new AudioOutputLifecycle(factory);
            int result = 1;
            try
            {
                result = args[0] == "--audio-native-clock-probe"
                    ? ClockProbe(args, output, error, factory, lifecycle, cancellation)
                    : Audition(args, output, error, factory, lifecycle, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { result = 130; }
            finally
            {
                if (lifecycle.Stop()) { factory.Dispose(); }
                else { error.WriteLine("Audio stop failed; native handle retained until process exit."); result = 1; }
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 130; }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        { error.WriteLine($"Native audio failed: {exception.Message}"); return 1; }
    }

    private readonly record struct ClockObservation(long RawStopwatchBefore, long RawStopwatchAfter,
        long RenderedThroughFrame, NativeAudioClockSample Clock);

    private static int ClockProbe(string[] args, TextWriter output, TextWriter error,
        NativeAudioOutputFactory factory, AudioOutputLifecycle lifecycle, CancellationToken cancellation)
    {
        var samples = new ClockObservation[20];
        if (!lifecycle.Replace(args.Length == 3 ? args[2] : null, 0))
        { error.WriteLine($"Clock probe unavailable: {lifecycle.Failure}"); return 1; }
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < samples.Length; i++)
        {
            do
            {
                cancellation.ThrowIfCancellationRequested();
                if (!factory.Pump() || !lifecycle.CheckHealth())
                { error.WriteLine($"Clock probe stopped: {factory.Status}"); return 1; }
                if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= i * 50) { break; }
                Thread.Sleep(1);
            } while (true);
            long before = Stopwatch.GetTimestamp();
            var clock = factory.ReadClock();
            long after = Stopwatch.GetTimestamp();
            samples[i] = new(before, after, lifecycle.Session!.RenderedThroughFrame, clock);
        }
        var native = factory.Status; var periods = factory.PeriodSnapshot;
        if (!lifecycle.Stop()) { error.WriteLine("Clock probe stop failed."); return 1; }
        // Serialize only after stop so console/file I/O cannot starve playback.
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = "Monitor.AudioClockProbe@1",
            LibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))),
            PlaybackStarted = true,
            GeneratedTone = false,
            EngineQueueMilliseconds = 40,
            RawStopwatchFrequency = Stopwatch.Frequency,
            DeviceQpcUnitsPerSecond = 10_000_000,
            Native = native,
            PeriodSnapshot = periods,
            Samples = samples,
            PhysicalLatencyAssessment = "NotAssessed",
        }));
        return 0;
    }

    private static int Diagnose(string[] args, NativeAudioOutputFactory factory, TextWriter output, TextWriter error)
    {
        IAudioOutputDevice? device = null;
        int result = 1;
        try
        {
            device = factory.Open(args.Length == 3 ? args[2] : null, new AudioRenderSession(), 1);
            if (device is null) { error.WriteLine("Audio diagnostics: device open failed."); }
            else
            {
                // Deliberately never Start/Pump: capture open-time diagnostics
                // without playing or producing PCM. I/O is outside callbacks.
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    Schema = "Monitor.AudioOpenDiagnostics@1",
                    CapturedUtc = DateTimeOffset.UtcNow,
                    LibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))),
                    RequestedDevice = args.Length == 3 ? args[2] : "system-default",
                    PlaybackStarted = false,
                    EngineQueueMilliseconds = 40,
                    Native = factory.Status,
                    PeriodSnapshot = factory.PeriodSnapshot,
                    PhysicalLatencyAssessment = "NotAssessed",
                }));
                result = 0;
            }
        }
        finally
        {
            if (device is null || device.StopAndClose()) { factory.Dispose(); }
            else { error.WriteLine("Diagnostics close failed; native handle retained until process exit."); result = 1; }
        }
        return result;
    }

    private static int Audition(string[] args, TextWriter output, TextWriter error,
        NativeAudioOutputFactory factory, AudioOutputLifecycle lifecycle, CancellationToken cancellation)
    {
        if (!lifecycle.Replace(args.Length == 3 ? args[2] : null, 0))
        { error.WriteLine($"Audio unavailable: {lifecycle.Failure}"); return 1; }
        var session = lifecycle.Session!;
        // Explicit engineering audition only; these are not detected QRS.
        for (int i = 0; i < 5; i++)
        {
            long target = 4800 + i * 38400;
            if (session.Schedule(i, TonePreset.BeatAudition, target, target + 12000) != ToneScheduleResult.Accepted)
            { error.WriteLine("Could not schedule audition."); return 1; }
        }
        output.WriteLine($"Engineering audition: five 75bpm tones. {factory.Status}. Physical latency NOT qualified.");
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(4))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!factory.Pump() || !lifecycle.CheckHealth())
            { error.WriteLine($"Audio stopped; native status: {factory.Status}"); return 1; }
            // Diagnostic producer pacing only; onset is sample-indexed.
            Thread.Sleep(1);
        }
        return 0;
    }

    private static void VerifyBinding(string path)
    {
        bool rejected = false;
        try { using var forbidden = new NativeAudioOutputFactory(path); }
        catch (InvalidOperationException) { rejected = true; }
        Check.That(rejected, "production loading rejects the null test library");
        using var diagnostics = new StringWriter(); using var diagnosticsError = new StringWriter();
        Check.That(Diagnose(["--audio-native-diagnostics", path], new NativeAudioOutputFactory(path, allowTestBackend: true),
            diagnostics, diagnosticsError) == 0, "quiet diagnostics open and close test device");
        using (var report = JsonDocument.Parse(diagnostics.ToString()))
        {
            Check.That(!report.RootElement.GetProperty("PlaybackStarted").GetBoolean() &&
                report.RootElement.GetProperty("PeriodSnapshot").GetProperty("QueryStatus").GetUInt32() == 0 &&
                report.RootElement.GetProperty("PhysicalLatencyAssessment").GetString() == "NotAssessed",
                "unavailable test periods cannot imply zero latency or hardware qualification");
        }
        nint library = NativeLibrary.Load(path);
        try
        {
            // Capture opaque handle without exposing miniaudio structs.
            var open = Marshal.GetDelegateForFunctionPointer<TestOpen>(NativeLibrary.GetExport(library, "sa_open"));
            var close = Marshal.GetDelegateForFunctionPointer<TestClose>(NativeLibrary.GetExport(library, "sa_close"));
            Check.That(open(0, 40, out nint probe) == 0 && close(probe) == 0, "cdecl opaque-handle ABI opens and closes");
            using var factory = new NativeAudioOutputFactory(path, allowTestBackend: true);
            var session = new AudioRenderSession();
            session.Schedule(1, TonePreset.BeatAudition, 0, 12000);
            var device = factory.Open(null, session, 1)!;
            try
            {
                Check.That(factory.ReadClock() is { Result: -2, DevicePosition: 0, DeviceFrequency: 0 } &&
                    factory.ReadClock().Nominal48kElapsedFrames is null, "null backend does not invent hardware clock readings");
                Check.That(factory.Pump() && session.RenderedThroughFrame == 1920 && session.BufferedFrames == 0,
                    "single40ms native queue; managed staging is drained");
                Check.That(factory.Pump() && session.RenderedThroughFrame == 1920, "native full leaves tone phase untouched");
                rejected = false;
                try { factory.Dispose(); } catch (InvalidOperationException) { rejected = true; }
                Check.That(rejected, "live handle prevents library unload");
                // No callback pointer or handle is exposed by the managed factory.
                // Exercise its real null worker, then observe underrun propagation.
                Check.That(device.Start(), "native start consumes primed PCM");
                long start = Stopwatch.GetTimestamp();
                while (factory.Status!.Value.RetiredReason == 0 && Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(3)) { Thread.Sleep(5); }
                Check.That(!factory.Pump() && session.RequiresReplacement && session.UnderrunFrames > 0,
                    "native underrun retires managed session without late refill");
            }
            finally { Check.That(device.StopAndClose(), "native worker joins before unload"); }
            Check.That(factory.Status is null && device.StopAndClose(), "closed device is idempotent and detached");
        }
        finally { NativeLibrary.Free(library); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TestOpen(nint id, uint capacity, out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TestClose(nint handle);
}
