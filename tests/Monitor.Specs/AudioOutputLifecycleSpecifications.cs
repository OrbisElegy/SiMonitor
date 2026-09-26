// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class AudioOutputLifecycleSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AudioReplacementJoinsOldDeviceAndFencesLateNotifications), AudioReplacementJoinsOldDeviceAndFencesLateNotifications),
        new(nameof(AudioFailedStopPreventsOpeningAnotherDevice), AudioFailedStopPreventsOpeningAnotherDevice),
        new(nameof(AudioOpenStartAndUnderrunFailuresRemainRecoverable), AudioOpenStartAndUnderrunFailuresRemainRecoverable),
    ];

    private static void AudioReplacementJoinsOldDeviceAndFencesLateNotifications()
    {
        var factory = new ProbeFactory(); var owner = new AudioOutputLifecycle(factory);
        Check.That(owner.Replace(null, 0), "default device opens with primed silence");
        var old = owner.Session!; long generation = owner.Generation;
        old.Schedule(1, TonePreset.BeatAudition, 2000, 14000);
        Check.That(owner.Replace("usb", 48000), "explicit selection replaces default device");
        Check.That(string.Join(',', factory.Events) == "open:default,start,join,open:usb,start", "old callback is joined before next open");
        float[] output = new float[1920];
        Check.That(old.RequiresReplacement && old.Read(output) == 0 && output.All(v => v == 0), "old session is fenced");
        Check.That(owner.Session!.Read(output) == 1920 && output.All(v => v == 0), "replacement contains no old tone");
        owner.DeviceLost(generation);
        Check.That(owner.State == AudioOutputState.Running && owner.CheckHealth(), "late old-device notification cannot stop replacement");
        owner.DeviceLost(owner.Generation);
        Check.That(owner.State == AudioOutputState.DeviceLost && owner.Failure == AudioOutputFailure.DeviceLost && owner.Session is null,
            "current device loss is surfaced and output closed");
        Check.That(owner.Stop() && owner.Stop(), "explicit stop is idempotent");
    }

    private static void AudioFailedStopPreventsOpeningAnotherDevice()
    {
        var factory = new ProbeFactory(); var owner = new AudioOutputLifecycle(factory);
        owner.Replace(null, 0); factory.Last!.CanClose = false;
        var old = owner.Session!;
        Check.That(!owner.Replace("usb", 48000) && owner.State == AudioOutputState.StopFailed && owner.Failure == AudioOutputFailure.Stop &&
            factory.Opens == 1 && owner.Session is null && old.RequiresReplacement, "unjoined device retains ownership and blocks reopen");
        Check.That(!owner.Replace("usb", 48000) && factory.Opens == 1, "retry cannot bypass failed stop");
        factory.Last.CanClose = true;
        Check.That(owner.Replace("usb", 48000) && factory.Opens == 2, "successful stop retry permits fresh session");
        var active = owner.Session; bool rejected = false;
        try { owner.Replace("", 0); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected && ReferenceEquals(active, owner.Session) && owner.CheckHealth(), "invalid selection leaves healthy output intact");
    }

    private static void AudioOpenStartAndUnderrunFailuresRemainRecoverable()
    {
        var factory = new ProbeFactory { FailOpen = true }; var owner = new AudioOutputLifecycle(factory);
        Check.That(!owner.Replace(null, 0) && owner.Failure == AudioOutputFailure.Open && owner.Session is null, "open failure visible without fake running state");
        factory.FailOpen = false; factory.FailStart = true;
        Check.That(!owner.Replace(null, 0) && owner.State == AudioOutputState.DeviceLost && owner.Failure == AudioOutputFailure.Start &&
            factory.Last!.Closed, "partial start failure closes device");
        factory.FailStart = false;
        Check.That(owner.Replace(null, 0), "fresh attempt may recover");
        owner.Session!.Read(new float[2000]);
        Check.That(!owner.CheckHealth() && owner.Failure == AudioOutputFailure.Underrun && factory.Last!.Closed && owner.Session is null,
            "scheduler observes callback underrun and joins old device");
        Check.That(owner.Replace(null, 96000) && owner.Session!.RenderedThroughFrame == 97920, "recovery uses a new explicit engine origin");
        factory.Last!.CanClose = false;
        Check.That(!owner.Stop() && owner.State == AudioOutputState.StopFailed, "failed shutdown must be retried by owner");
        factory.Last.CanClose = true; Check.That(owner.Stop(), "shutdown retry joins retained handle");
    }

    private sealed class ProbeFactory : IAudioOutputFactory
    {
        public List<string> Events { get; } = [];
        public bool FailOpen { get; set; }
        public bool FailStart { get; set; }
        public int Opens { get; private set; }
        public ProbeDevice? Last { get; private set; }
        public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation)
        {
            Opens++; Events.Add("open:" + (deviceId ?? "default"));
            if (FailOpen) { return null; }
            Check.That(generation > 0 && !session.RequiresReplacement, "factory receives current generation and fresh session");
            Last = new ProbeDevice(Events, !FailStart); return Last;
        }
    }

    private sealed class ProbeDevice(List<string> events, bool canStart) : IAudioOutputDevice
    {
        public bool CanClose { get; set; } = true;
        public bool Closed { get; private set; }
        public bool Start() { events.Add("start"); return canStart; }
        public bool StopAndClose() { events.Add("join"); Closed = CanClose; return CanClose; }
    }
}
