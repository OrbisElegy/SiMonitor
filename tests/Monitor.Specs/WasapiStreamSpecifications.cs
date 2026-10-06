// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using System.Runtime.InteropServices;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class WasapiStreamSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FailedShortPeriodInitializationUsesAFreshClient), FailedShortPeriodInitializationUsesAFreshClient),
        new(nameof(PeriodQueryFailureKeepsTheUnusedClient), PeriodQueryFailureKeepsTheUnusedClient),
        new(nameof(SuccessfulShortPeriodNeedsNoFallback), SuccessfulShortPeriodNeedsNoFallback),
        new(nameof(FailedReactivationDoesNotReuseTheFailedClient), FailedReactivationDoesNotReuseTheFailedClient)
    ];

    private static void FailedShortPeriodInitializationUsesAFreshClient()
    {
        var result = Negotiate(queryFails: false, shortPeriodFails: true, activationFails: false);
        Check.That(result.Opened && result.Reactivations == 1 && result.First.Initializations == 0 &&
            result.Replacement.Initializations == 1 && result.Path == WasapiStreamPath.MixFormat,
            "failed initialization is abandoned and ordinary mode initializes a fresh client");
    }

    private static void PeriodQueryFailureKeepsTheUnusedClient()
    {
        var result = Negotiate(queryFails: true, shortPeriodFails: false, activationFails: false);
        Check.That(result.Opened && result.Reactivations == 0 && result.First.Initializations == 1 &&
            result.First.ShortInitializations == 0 && result.Path == WasapiStreamPath.MixFormat,
            "a failed period query does not consume the client's initialization attempt");
    }

    private static void SuccessfulShortPeriodNeedsNoFallback()
    {
        var result = Negotiate(queryFails: false, shortPeriodFails: false, activationFails: false);
        Check.That(result.Opened && result.Reactivations == 0 && result.First.Initializations == 0 &&
            result.First.ShortInitializations == 1 && result.Path == WasapiStreamPath.ShortEnginePeriod,
            "a successful short-period stream is initialized only once");
    }

    private static void FailedReactivationDoesNotReuseTheFailedClient()
    {
        var result = Negotiate(queryFails: false, shortPeriodFails: true, activationFails: true);
        Check.That(!result.Opened && result.Reactivations == 1 && result.First.Initializations == 0 &&
            result.Replacement.Initializations == 0, "failed reactivation reports unavailable without retrying a poisoned client");
    }

    // Exercise production negotiation without COM activation or an audio device.
    // Reflection keeps this platform-independent test outside the Windows-only
    // endpoint API; all returned formats use the same unmanaged memory contract.
    private static NegotiationResult Negotiate(bool queryFails, bool shortPeriodFails, bool activationFails)
    {
        var first = new ClientProbe();
        var firstProbe = first;
        firstProbe.QueryFails = queryFails;
        firstProbe.ShortPeriodFails = shortPeriodFails;
        var replacement = new ClientProbe();
        var replacementProbe = replacement;
        var type = typeof(WasapiAudioOutput).Assembly.GetType("Monitor.Infrastructure.Audio.WasapiRenderEndpoint")!;
        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        using var endpoint = (IDisposable)type.GetConstructor(privateInstance, null, [typeof(bool)], null)!.Invoke([false]);
        var clientField = type.GetField("_client", privateInstance)!;
        clientField.SetValue(endpoint, first);
        int reactivations = 0;
        bool Reactivate()
        {
            reactivations++;
            clientField.SetValue(endpoint, activationFails ? null : replacement);
            return !activationFails;
        }
        bool opened = (bool)type.GetMethod("InitializeStream", privateInstance)!.Invoke(endpoint,
            [1920, 960, (Func<bool>)Reactivate])!;
        var path = (WasapiStreamPath)type.GetProperty("Path")!.GetValue(endpoint)!;
        return new(opened, reactivations, firstProbe, replacementProbe, path);
    }

    private sealed record NegotiationResult(bool Opened, int Reactivations, ClientProbe First,
        ClientProbe Replacement, WasapiStreamPath Path);

    private sealed class ClientProbe : IAudioClient3
    {
        private bool _initialized;
        public bool QueryFails { get; set; }
        public bool ShortPeriodFails { get; set; }
        public int Initializations { get; private set; }
        public int ShortInitializations { get; private set; }

        public int GetMixFormat(out nint format)
        {
            format = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormat>());
            Marshal.StructureToPtr(new WaveFormat
            {
                FormatTag = 3,
                Channels = 2,
                SamplesPerSecond = 48_000,
                AverageBytesPerSecond = 384_000,
                BlockAlign = 8,
                BitsPerSample = 32
            }, format, false);
            return 0;
        }

        public int GetDevicePeriod(out long defaultPeriod100Ns, out long minimumPeriod100Ns)
        {
            defaultPeriod100Ns = 200_000;
            minimumPeriod100Ns = 10_000;
            return 0;
        }

        public int GetSharedModeEnginePeriod(nint format, out uint defaultPeriodFrames, out uint fundamentalPeriodFrames,
            out uint minimumPeriodFrames, out uint maximumPeriodFrames)
        {
            defaultPeriodFrames = 960;
            fundamentalPeriodFrames = 48;
            minimumPeriodFrames = 48;
            maximumPeriodFrames = 960;
            return QueryFails ? unchecked((int)0x80004005) : 0;
        }

        public int InitializeSharedAudioStream(uint streamFlags, uint periodFrames, nint format, nint audioSessionGuid)
        {
            ShortInitializations++;
            _initialized = true;
            return ShortPeriodFails ? unchecked((int)0x80004005) : 0;
        }

        public int Initialize(int shareMode, uint streamFlags, long bufferDuration100Ns, long periodicity100Ns, nint format, nint audioSessionGuid)
        {
            Initializations++;
            if (_initialized) { return unchecked((int)0x88890002); }
            _initialized = true;
            return 0;
        }

        public int Stop() => 0;
        public int GetBufferSize(out uint bufferFrames) => throw new NotSupportedException();
        public int GetStreamLatency(out long latency100Ns) => throw new NotSupportedException();
        public int GetCurrentPadding(out uint paddingFrames) => throw new NotSupportedException();
        public int IsFormatSupported(int shareMode, nint format, out nint closestMatch) => throw new NotSupportedException();
        public int Start() => throw new NotSupportedException();
        public int Reset() => throw new NotSupportedException();
        public int SetEventHandle(nint eventHandle) => throw new NotSupportedException();
        public int GetService(in Guid interfaceId, out nint service) => throw new NotSupportedException();
        public int IsOffloadCapable(int category, out int offloadCapable) => throw new NotSupportedException();
        public int SetClientProperties(nint properties) => throw new NotSupportedException();
        public int GetBufferSizeLimits(nint format, int eventDriven, out long minimumDuration100Ns, out long maximumDuration100Ns) => throw new NotSupportedException();
        public int GetCurrentSharedModeEnginePeriod(out nint format, out uint currentPeriodFrames) => throw new NotSupportedException();
    }
}
