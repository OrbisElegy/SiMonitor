// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class NativeAudioCommand
{
    public static int Execute(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        bool check = args.Length > 0 && args[0] == "--audio-native-check";
        if (args.Length is < 2 or > 3 || !Path.IsPathFullyQualified(args[1]) || (check && args.Length != 2))
        { error.WriteLine("Usage: --audio-native-audition ABSOLUTE_LIBRARY [DEVICE_ID] | --audio-native-check ABSOLUTE_TEST_LIBRARY"); return 2; }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (check) { VerifyBinding(args[1]); output.WriteLine("PASS managed/native PCM, single queue, retirement and unload ownership"); return 0; }
            var factory = new NativeAudioOutputFactory(args[1]);
            var lifecycle = new AudioOutputLifecycle(factory);
            int result = 1;
            try { result = Audition(args, output, error, factory, lifecycle, cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { result = 130; }
            finally
            {
                if (lifecycle.Stop()) { factory.Dispose(); }
                else { error.WriteLine("Audio stop failed; native handle retained until process exit."); result = 1; }
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 130; }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException or ArgumentException)
        { error.WriteLine($"Native audio failed: {exception.Message}"); return 1; }
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
