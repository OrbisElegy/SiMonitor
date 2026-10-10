// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class AudioRecoverySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PlaybackRecoversAndKeepsDeviceSelection), PlaybackRecoversAndKeepsDeviceSelection),
        new(nameof(MasterGainControlsBufferedPcmWithoutLosingVolume), MasterGainControlsBufferedPcmWithoutLosingVolume),
        new(nameof(ReconnectionDoesNotReplayRetiredNotifications), ReconnectionDoesNotReplayRetiredNotifications),
        new(nameof(HeartbeatCancellationSurvivesImmediateReenable), HeartbeatCancellationSurvivesImmediateReenable),
    ];

    private static void PlaybackRecoversAndKeepsDeviceSelection()
    {
        VerifyRecovery(false);
        VerifyRecovery(true);
    }

    private static void VerifyRecovery(bool muted)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var outputs = new List<RecoveryOutput>();
        MonitorAlarmPlayback? player = null;
        int ownerThread = 0;
        player = new(() =>
        {
            Check.That(outputs.Count == 0 || outputs[^1].Disposed, "join and dispose old output before opening replacement");
            ownerThread = ownerThread == 0 ? Environment.CurrentManagedThreadId : ownerThread;
            Check.That(ownerThread == Environment.CurrentManagedThreadId, "all generations share one owner thread");
            var output = new RecoveryOutput();
            int generation = outputs.Count;
            outputs.Add(output);
            output.OnPump = () =>
            {
                if (output.Pumps < 6) { return true; }
                if (generation == 0) { return false; } // Default device changed / retired.
                if (generation == 1) { player!.SetOutputDevice("speakers-b"); return true; }
                if (generation == 2) { return false; } // Pinned device temporarily removed.
                Check.That(player!.OutputActive, "reopened selected endpoint becomes healthy after pumping");
                cancel.Cancel();
                return true;
            };
            return output;
        });
        player.SetVolume(37, muted);
        player.SetRequest(new(MonitorNoticeLevel.Critical, 100, new()));
        Check.That(player.RunAsync(cancel.Token).GetAwaiter().GetResult() == SoundPreviewResult.Stopped, "cancellation joins recovered output");
        Check.That(outputs.Count == 4 && outputs.Select(o => o.DeviceId).SequenceEqual(new string?[] { null, null, "speakers-b", "speakers-b" }),
            "default recovery re-resolves default; explicit selection stays pinned across loss");
        Check.That(outputs.All(o => o.Disposed && o.Gain == (muted ? 0 : 0.37f)), "volume and mute survive every replacement");
        Check.That(outputs.All(o => (o.Peak > 0) == !muted), "current continuous alarm resumes on fresh sessions");
    }

    private static void MasterGainControlsBufferedPcmWithoutLosingVolume()
    {
        static float[] Render(float gain)
        {
            var session = new AudioRenderSession();
            session.Schedule(1, TonePreset.BeatAudition, 0, 12000);
            session.TryProduce(1920);
            session.Gain = gain; // Apply after rendering, before the device consumes PCM.
            float[] pcm = new float[1920];
            session.Read(pcm);
            return pcm;
        }
        float[] full = Render(1);
        float[] quarter = Render(0.25f);
        Check.That(full.Any(v => v != 0) && quarter.SequenceEqual(full.Select(v => v * 0.25f)) && Render(0).All(v => v == 0),
            "master gain scales already buffered voices and mute silences them");
    }

    private static void ReconnectionDoesNotReplayRetiredNotifications()
    {
        var request = new MonitorAlarmSoundRequest(MonitorNoticeLevel.Critical, 100, new()) { NotificationSequence = 8 };
        var session = new AudioRenderSession();
        var sequencer = new MonitorAlarmSequencer(session, retiredNotificationSequence: 8);
        sequencer.Update(request);
        Check.That(sequencer.Dispatches.Count == 0 && sequencer.RetiredNotificationSequence == 8, "completed group stays retired across endpoint replacement");
        sequencer.Update(request with { NotificationSequence = 9 });
        Check.That(sequencer.Dispatches.Single().Stage == AlarmSoundDispatchStage.Selected, "current newer group is eligible after reconnection");
    }

    private static void HeartbeatCancellationSurvivesImmediateReenable()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var output = new RecoveryOutput();
        var player = new MonitorAlarmPlayback(() => output);
        player.SetHeartbeatEnabled(true);
        output.OnPump = () =>
        {
            if (output.Pumps == 1) { player.SubmitHeartbeat(100); }
            if (output.Pumps == 3)
            {
                Check.That(output.LastPeak > 0, "the old QRS tone is active before discharge");
                player.CancelHeartbeat();
                player.SetHeartbeatEnabled(false);
                player.SetHeartbeatEnabled(true); // Same UI frame; worker never observes false.
            }
            if (output.Pumps == 5)
            { Check.That(output.TailPeak == 0, "cancellation fences active cues after queued PCM and a short fade drain"); }
            if (output.Pumps == 7) { player.SubmitHeartbeat(100); }
            if (output.Pumps == 9)
            {
                Check.That(output.LastPeak > 0, "a newly confirmed QRS may sound after cancellation");
                cancellation.Cancel();
            }
            return true;
        };
        Check.That(player.RunAsync(cancellation.Token).GetAwaiter().GetResult() == SoundPreviewResult.Stopped && output.Pumps == 9,
            "the worker completes the deterministic cancel/re-enable interleaving");
    }

    private sealed class RecoveryOutput : IPumpedAudioOutput, IAudioOutputDevice
    {
        private AudioRenderSession? _session;
        public Func<bool> OnPump { get; set; } = () => true;
        public string? DeviceId { get; private set; }
        public bool Disposed { get; private set; }
        public int Pumps { get; private set; }
        public float Gain => _session!.Gain;
        public float Peak { get; private set; }
        public float LastPeak { get; private set; }
        public float TailPeak { get; private set; }
        public IAudioOutputDevice Open(string? deviceId, AudioRenderSession session, long generation)
        { DeviceId = deviceId; _session = session; return this; }
        public bool Start() => true;
        public bool StopAndClose() => true;
        public bool Pump()
        {
            Pumps++;
            float[] pcm = new float[1920];
            _session!.Read(pcm);
            _session.TryProduce(1920);
            LastPeak = pcm.Max(Math.Abs);
            TailPeak = pcm.Skip(240).Max(Math.Abs);
            Peak = Math.Max(Peak, pcm.Max(Math.Abs));
            return OnPump();
        }
        public void WaitForQueueSpace(int timeoutMilliseconds) { }
        public void Dispose() => Disposed = true;
    }
}
