// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class SoundPreviewSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SoundPreviewOwnsOutputOffCallerAndScalesVolume), SoundPreviewOwnsOutputOffCallerAndScalesVolume),
        new(nameof(SoundPreviewCancellationAndFailedJoinPreventReplay), SoundPreviewCancellationAndFailedJoinPreventReplay),
        new(nameof(MonitorAlarmOutputRetainsFailedJoin), MonitorAlarmOutputRetainsFailedJoin),
        new(nameof(HeartbeatWorkerDropsExpiredAndDisabledCues), HeartbeatWorkerDropsExpiredAndDisabledCues),
        new(nameof(OutputHealthRequiresSuccessfulPump), OutputHealthRequiresSuccessfulPump),
    ];

    private static void SoundPreviewOwnsOutputOffCallerAndScalesVolume()
    {
        int caller = Environment.CurrentManagedThreadId;
        float Measure(int volume)
        {
            using var cancel = new CancellationTokenSource();
            var output = new Output(cancel);
            var playback = new SoundPreviewPlayback(() => output);
            Check.That(playback.PlayAsync(volume, cancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.Stopped,
                "explicit stop ends preview");
            Check.That(output.Threads.Count == 1 && !output.Threads.Contains(caller) && output.Closed && output.Disposed,
                "open/pump/close/dispose share background owner and join before unload");
            return output.Peak;
        }
        float full = Measure(100), half = Measure(50), zero = Measure(0);
        Check.That(full > 0.1f && half == full / 2 && zero == 0, "volume scales rendered PCM including mute");
    }

    private static void SoundPreviewCancellationAndFailedJoinPreventReplay()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        int opens = 0;
        var player = new SoundPreviewPlayback(() => { opens++; return new Output(cancel); });
        Check.That(player.PlayAsync(50, cancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.Stopped && opens == 0,
            "cancel before start opens no device");
        bool invalid = false;
        try { player.PlayAsync(101, cancel.Token).GetAwaiter().GetResult(); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check.That(invalid && opens == 0, "invalid volume has no output effects");
        using var stop = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var output = new Output(stop) { CanClose = false };
        player = new(() => { opens++; entered.Set(); release.Wait(); return output; });
        var running = player.PlayAsync(50, stop.Token);
        entered.Wait();
        bool duplicate = false;
        try { player.PlayAsync(50, stop.Token).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { duplicate = true; }
        finally { release.Set(); }
        Check.That(duplicate && running.GetAwaiter().GetResult() == SoundPreviewResult.StopFailed && !output.Disposed,
            "concurrent play rejected; unjoined device retained");
        Check.That(player.PlayAsync(50, CancellationToken.None).GetAwaiter().GetResult() == SoundPreviewResult.StopFailed && opens == 1,
            "failed join blocks second output");
        output.CanClose = true;
        Check.That(player.PlayAsync(50, cancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.Stopped && output.Disposed && opens == 1,
            "successful close retry releases retained device without stale tones");
    }

    private static void MonitorAlarmOutputRetainsFailedJoin()
    {
        using var cancel = new CancellationTokenSource();
        var output = new Output(cancel); int opens = 0;
        var player = new MonitorAlarmPlayback(() => { opens++; return output; });
        player.SetRequest(new(Monitor.Application.Presentation.MonitorNoticeLevel.Critical, 50, new()));
        Check.That(player.RunAsync(cancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.Stopped && output.Peak > 0 && output.Disposed,
            "live alarm worker emits PCM and joins before disposal");
        using var failureCancel = new CancellationTokenSource();
        output = new Output(failureCancel) { CanClose = false, FailPump = true };
        player = new(() => { opens++; return output; });
        Check.That(player.RunAsync(failureCancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.StopFailed && !output.Disposed,
            "pump failure plus failed join retains native owner");
        Check.That(player.RunAsync(failureCancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.StopFailed && opens == 2,
            "failed join prevents replacement stream");
        output.CanClose = true; failureCancel.Cancel();
        Check.That(player.RunAsync(failureCancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.Stopped && output.Disposed,
            "retry releases retained stream without new sound");
    }
    private static void HeartbeatWorkerDropsExpiredAndDisabledCues()
    {
        foreach (string mode in new[] { "fresh", "expired", "disabled" })
        {
            using var cancel = new CancellationTokenSource();
            var output = new Output(cancel); var player = new MonitorAlarmPlayback(() => output);
            player.SetHeartbeatEnabled(true);
            output.BeforePump = count =>
            {
                if (count != 0) { return; }
                player.SubmitHeartbeat(100);
                if (mode == "expired") { Thread.Sleep(300); }
                if (mode == "disabled") { player.SetHeartbeatEnabled(false); }
            };
            Check.That(player.RunAsync(cancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.Stopped && output.Disposed,
                "heartbeat-only stream closes normally");
            Check.That((output.Peak > 0) == (mode == "fresh"), "fresh beats play without alarm; expired/disabled mailbox cannot replay");
        }
    }
    private static void OutputHealthRequiresSuccessfulPump()
    {
        foreach (bool fail in new[] { false, true })
        {
            using var cancel = new CancellationTokenSource();
            var output = new Output(cancel);
            var player = new MonitorAlarmPlayback(() => output);
            bool sawActive = false;
            output.BeforePump = count =>
            {
                if (count == 0) { Check.That(!player.OutputActive, "opening alone does not prove pumping"); }
                else
                {
                    sawActive |= player.OutputActive;
                    if (fail) { output.FailPump = true; }
                }
            };
            var result = player.RunAsync(cancel.Token).GetAwaiter().GetResult();
            Check.That(sawActive && !player.OutputActive && result == (fail ? SoundPreviewResult.Interrupted : SoundPreviewResult.Stopped),
                "healthy pump is observable; both stop and interruption clear health");
        }
        var missing = new MonitorAlarmPlayback(() => throw new DllNotFoundException());
        Check.That(missing.RunAsync(CancellationToken.None).GetAwaiter().GetResult() == SoundPreviewResult.Unavailable && !missing.OutputActive,
            "missing backend never reports active output");
    }
    private sealed class Output(CancellationTokenSource cancel) : IPumpedAudioOutput, IAudioOutputDevice
    {
        private AudioRenderSession? _session;
        private readonly float[] _pcm = new float[1920];
        private int _pumps;
        public HashSet<int> Threads { get; } = [];
        public bool Closed { get; private set; }
        public bool Disposed { get; private set; }
        public bool CanClose { get; set; } = true;
        public bool FailPump { get; set; }
        public float Peak { get; private set; }
        public Action<int>? BeforePump { get; set; }
        public IAudioOutputDevice Open(string? deviceId, AudioRenderSession session, long generation)
        { Touch(); _session = session; return this; }
        public bool Start() { Touch(); return true; }
        public bool Pump()
        {
            Touch();
            BeforePump?.Invoke(_pumps);
            if (_session!.BufferedFrames == 0) { _session.TryProduce(_pcm.Length); }
            _session.Read(_pcm);
            foreach (float sample in _pcm) { Peak = Math.Max(Peak, Math.Abs(sample)); }
            if (++_pumps == 12) { cancel.Cancel(); }
            return !FailPump;
        }
        public bool StopAndClose() { Touch(); Closed = CanClose; return CanClose; }
        public void Dispose() { Touch(); Check.That(Closed, "join before dispose"); Disposed = true; }
        private void Touch() => Threads.Add(Environment.CurrentManagedThreadId);
    }
}
