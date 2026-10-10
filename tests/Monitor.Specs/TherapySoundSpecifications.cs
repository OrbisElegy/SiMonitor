// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class TherapySoundSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(TherapySpeechPrecedesReadyAndDucksMonitor), TherapySpeechPrecedesReadyAndDucksMonitor),
        new(nameof(TherapyCancellationAndReconnectDiscardSpeech), TherapyCancellationAndReconnectDiscardSpeech),
        new(nameof(TherapyCprUsesThirtyCompressionsAndTwoBreaths), TherapyCprUsesThirtyCompressionsAndTwoBreaths),
        new(nameof(TherapyMixIsPartitionIndependentAndAllocationFree), TherapyMixIsPartitionIndependentAndAllocationFree),
        new(nameof(TherapyAudioUsesSharedGainAndBuffer), TherapyAudioUsesSharedGainAndBuffer),
        new(nameof(TherapyCprSnapshotsDoNotSeekPlayingPcm), TherapyCprSnapshotsDoNotSeekPlayingPcm),
        new(nameof(TherapyCprReconnectAnchorsOnceAndStillStops), TherapyCprReconnectAnchorsOnceAndStillStops),
        new(nameof(TherapyRelayRequiresActualEventsAndSharesMute), TherapyRelayRequiresActualEventsAndSharesMute),
        new(nameof(StoredEnergyWarningDucksAndRestoresMonitor), StoredEnergyWarningDucksAndRestoresMonitor)
    ];

    private static TherapySoundRequest Request(TherapySoundPhase phase, string locale = "zh-CN", ulong revision = 1) =>
        new(revision, phase, locale, true, 500, 0) { Announce = true };

    private static void StoredEnergyWarningDucksAndRestoresMonitor()
    {
        foreach (bool automated in new[] { false, true })
        {
            var dry = new TherapySoundRenderer();
            var mixed = new TherapySoundRenderer();
            var request = Request(TherapySoundPhase.Ready) with { Automated = automated };
            dry.Update(request);
            mixed.Update(request);
            float[] reference = new float[480];
            float[] background = Enumerable.Repeat(.5f, 480).ToArray();
            for (int block = 0; block < 800; block++)
            {
                Array.Clear(reference);
                Array.Fill(background, .5f);
                dry.Mix(reference);
                mixed.Mix(background);
                if (block >= 2)
                {
                    Check.That(background.Zip(reference, (a, b) => Math.Abs(a - b - .5f * .063095735f)).All(d => d < .000001),
                        "stored energy ducks all monitor audio by 24 dB through voice, gaps, double and hold");
                }
            }
            // Cancellation removes the warning without altering monitor schedules.
            mixed.Update(Request(TherapySoundPhase.Silent, revision: 2) with { Announce = false });
            float[] release = Enumerable.Repeat(.5f, 9600).ToArray();
            mixed.Mix(release);
            Check.That(release.Skip(4800).All(s => Math.Abs(s - .5f) < .000001),
                "discharge/disarm restores configured monitor gain within 100 ms");
            var reconnected = new TherapySoundRenderer();
            reconnected.Update(request, announce: false);
            float[] resumed = Enumerable.Repeat(.5f, 4800).ToArray();
            var resumedDry = new TherapySoundRenderer();
            resumedDry.Update(request, announce: false);
            reconnected.Mix(resumed);
            float[] hold = Render(resumedDry, resumed.Length);
            Check.That(resumed.Skip(960).Zip(hold.Skip(960), (a, b) => Math.Abs(a - b - .5f * .063095735f)).All(d => d < .000001),
                "reconnection restores stored-energy priority without replaying speech");
        }
    }
    private static float[] Render(TherapySoundRenderer renderer, int frames)
    {
        float[] samples = new float[frames];
        renderer.Mix(samples);
        return samples;
    }

    private static void TherapySpeechPrecedesReadyAndDucksMonitor()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            var renderer = new TherapySoundRenderer();
            renderer.Update(Request(TherapySoundPhase.Ready, locale));
            int speech = SelectedTherapySounds.FrameCount("AED_READY", locale);
            int ready = SelectedTherapySounds.FrameCount("Ready");
            float[] samples = Render(renderer, speech + 7680 + ready + 48000);
            Check.That(samples.Take(speech).Any(s => s != 0) && samples.Skip(speech).Take(7680).All(s => s == 0),
                "selected ready voice plays before the ready double with a clear gap");
            var expected = SelectedTherapySounds.Get("Ready").ToArray().Select(s => s / 32768f);
            Check.That(samples.Skip(speech + 7680).Take(ready).SequenceEqual(expected), "ready double plays once after speech");
            int holdAt = speech + 7680 + ready + 7680;
            float[] hold = SelectedTherapySounds.Get("ReadyHold").ToArray().Select(s => s / 32768f).ToArray();
            Check.That(samples.Skip(holdAt).Select((sample, index) => sample == hold[index % hold.Length]).All(equal => equal),
                "alternating ready hold starts after the double and loops continuously");
            var dry = new TherapySoundRenderer();
            var mixed = new TherapySoundRenderer();
            dry.Update(Request(TherapySoundPhase.Analyzing, locale));
            mixed.Update(Request(TherapySoundPhase.Analyzing, locale));
            float[] background = Enumerable.Repeat(.5f, 4800).ToArray();
            mixed.Mix(background);
            float[] voice = Render(dry, 4800);
            Check.That(background.Zip(voice, (a, b) => Math.Abs(a - b - .5f * .1259f)).All(d => d < .000001),
                "speech ducks monitor audio while leaving the selected recording unmodified");
        }
    }

    private static void TherapyCancellationAndReconnectDiscardSpeech()
    {
        var renderer = new TherapySoundRenderer();
        var request = Request(TherapySoundPhase.Charging);
        renderer.Update(request);
        Check.That(Render(renderer, 4800).Any(s => s != 0), "charging produces audio");
        renderer.Update(request with { Revision = 2, Phase = TherapySoundPhase.Silent, Announce = false });
        Check.That(Render(renderer, 48000).Skip(240).All(s => s == 0), "cancellation fades within 5 ms without old speech or charging tails");
        renderer.Update(Request(TherapySoundPhase.Ready, revision: 3), announce: false);
        float[] hold = SelectedTherapySounds.Get("ReadyHold").ToArray().Select(s => s / 32768f).ToArray();
        Check.That(Render(renderer, 48000).Select((sample, index) => sample == hold[index % hold.Length]).All(equal => equal),
            "reconnection resumes only the ready hold without replaying voice or double");
        renderer.Update(Request(TherapySoundPhase.Analyzing, revision: 4));
        Render(renderer, 5000);
        renderer.Update(Request(TherapySoundPhase.Analyzing, "en", 5) with { Announce = false });
        Check.That(Render(renderer, 48000).Skip(240).All(s => s == 0), "language change withdraws speech without replaying an old entry");
    }

    private static void TherapyCprUsesThirtyCompressionsAndTwoBreaths()
    {
        const string locale = "en";
        var renderer = new TherapySoundRenderer();
        renderer.Update(Request(TherapySoundPhase.CprNoShock, locale));
        int intro = SelectedTherapySounds.FrameCount("AED_NSA_CPR", locale) + SelectedTherapySounds.FrameCount("AED_CPR_START", locale) + 2 * 7680;
        int compressions = 30 * 48000 * 60 / 110;
        int cycle = compressions + 5 * 48000;
        float[] samples = Render(renderer, intro + cycle + 960);
        float[] click = SelectedTherapySounds.Get("Cpr").ToArray().Select(s => s / 32768f).ToArray();
        for (int beat = 0; beat < 30; beat++)
        {
            // Integer frame rounding follows the renderer's 110/min clock.
            int at = intro + (int)Math.Ceiling(beat * 48000d * 60 / 110);
            int offset = at - intro - beat * 48000 * 60 / 110;
            Check.That(samples.Skip(at).Take(click.Length - offset).SequenceEqual(click.Skip(offset)), "CPR click follows the current compression slot");
        }
        var breath = SelectedTherapySounds.Get("AED_CPR_BREATHS", locale).ToArray().Select(s => s / 32768f);
        Check.That(samples.Skip(intro + compressions).Take(breath.Count()).SequenceEqual(breath), "two-breath prompt occupies the ventilation window without clicks");
        Check.That(samples.Skip(intro + cycle).Take(click.Length).SequenceEqual(click), "compression cycle resumes after a five-second ventilation window");
        renderer.Update(Request(TherapySoundPhase.CprNoShock, locale) with { CprElapsedMilliseconds = 120000 });
        Check.That(Render(renderer, 48000).All(s => s == 0), "no metronome or queued breaths outlive 120 seconds");
    }

    private static void TherapyMixIsPartitionIndependentAndAllocationFree()
    {
        var whole = new TherapySoundRenderer();
        var split = new TherapySoundRenderer();
        whole.Update(Request(TherapySoundPhase.Charging));
        split.Update(Request(TherapySoundPhase.Charging));
        float[] expected = Render(whole, 48000);
        float[] actual = new float[48000];
        for (int at = 0; at < actual.Length; at += 137) { split.Mix(actual.AsSpan(at, Math.Min(137, actual.Length - at))); }
        Check.That(actual.SequenceEqual(expected), "charge oscillator and speech are independent of render partition");
        float[] buffer = new float[480];
        split.Mix(buffer);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { Array.Clear(buffer); split.Mix(buffer); }
        Check.That(GC.GetAllocatedBytesForCurrentThread() == before, "rendering has no allocation or asset lookups");
    }

    private static void TherapyAudioUsesSharedGainAndBuffer()
    {
        var session = new AudioRenderSession();
        session.UpdateTherapy(Request(TherapySoundPhase.Charging));
        session.Gain = 0;
        float[] output = new float[480];
        Check.That(session.TryProduce(480) && session.Read(output) == 480 && output.All(v => v == 0), "master mute applies to therapy in the shared stream");
        session.Gain = 1;
        Check.That(session.TryProduce(480) && session.Read(output) == 480 && output.Any(v => v != 0), "unmuting continues current therapy audio");
        session.UpdateTherapy(null);
        session.TryProduce(480);
        session.Read(output);
        Check.That(output.Skip(240).All(v => v == 0), "withdrawal reaches the existing native PCM buffer");
    }

    private static void TherapyCprSnapshotsDoNotSeekPlayingPcm()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var phase in new[] { TherapySoundPhase.CprShock, TherapySoundPhase.CprNoShock })
            {
                var request = Request(phase, locale);
                var reference = new TherapySoundRenderer();
                var session = new AudioRenderSession();
                reference.Update(request);
                session.UpdateTherapy(request);
                float[] expected = new float[480];
                float[] actual = new float[480];
                for (int block = 0; block < 4000; block++)
                {
                    // UI snapshots arrive every 50 ms, independently of the device's
                    // 10 ms consumption. Include queue lead, jitter and a stalled UI.
                    if (block > 0 && block % 5 == 0 && block % 200 >= 50)
                    {
                        int elapsed = block * 10 + (block % 15 == 0 ? 17 : -23);
                        session.UpdateTherapy(request with { CprElapsedMilliseconds = elapsed });
                    }
                    Array.Clear(expected);
                    reference.Mix(expected);
                    Check.That(session.TryProduce(actual.Length) && session.Read(actual) == actual.Length && session.UnderrunFrames == 0,
                        "CPR reproduction supplies every device frame without underrun");
                    Check.That(actual.SequenceEqual(expected),
                        $"{locale} {phase}: UI progress must not repeat or skip PCM at audio frame {block * 480}");
                }
            }
    }

    private static void TherapyCprReconnectAnchorsOnceAndStillStops()
    {
        var request = Request(TherapySoundPhase.CprShock, "en") with { CprElapsedMilliseconds = 30000 };
        var reference = new TherapySoundRenderer();
        reference.Update(request with { CprElapsedMilliseconds = 0 });
        Render(reference, 30000 * 48);
        var reconnected = new TherapySoundRenderer();
        reconnected.Update(request, announce: false);
        Check.That(Render(reconnected, 4800).SequenceEqual(Render(reference, 4800)),
            "a fresh stream anchors to the current CPR cycle without replaying its introduction");
        reconnected.Update(request with { CprElapsedMilliseconds = 30050 }, announce: false);
        Check.That(Render(reconnected, 4800).SequenceEqual(Render(reference, 4800)),
            "later snapshots cannot rewind the reconnected stream");
        reconnected.Update(request with { CprElapsedMilliseconds = 120000 }, announce: false);
        Check.That(Render(reconnected, 4800).All(s => s == 0), "simulation deadline ends CPR even when its audio cursor is behind");
        reconnected.Update(Request(TherapySoundPhase.Silent, "en", 2) with { Announce = false });
        Check.That(Render(reconnected, 4800).Skip(240).All(s => s == 0), "pause and cancellation still withdraw therapy sound");
    }

    private static void TherapyRelayRequiresActualEventsAndSharesMute()
    {
        var renderer = new TherapySoundRenderer();
        renderer.Update(Request(TherapySoundPhase.Silent));
        Check.That(Render(renderer, 4800).All(s => s == 0), "idle/disarm does not invent a relay event");
        renderer.UpdateRelay(true, true);
        float[] relay = SelectedTherapySounds.Get("Relay").ToArray().Select(s => s / 32768f).ToArray();
        float[] played = Render(renderer, 4800);
        Check.That(played.Take(relay.Length).SequenceEqual(relay) && played.Skip(relay.Length).All(s => s == 0),
            "each delivered shock or emitted pacing pulse plays the selected relay once");
        renderer.UpdateRelay(true);
        Check.That(Render(renderer, 4800).All(s => s == 0), "refreshing enable state does not replay an event");
        renderer.UpdateRelay(true, true);
        Render(renderer, 480);
        renderer.UpdateRelay(false);
        Check.That(Render(renderer, 4800).All(s => s == 0), "pause clears an in-flight relay");
        var fresh = new TherapySoundRenderer();
        fresh.UpdateRelay(true);
        Check.That(Render(fresh, 4800).All(s => s == 0), "reconnection cannot replay a past relay");
        var session = new AudioRenderSession { Gain = 0 };
        session.UpdateTherapyRelay(true, true);
        float[] buffer = new float[480];
        Check.That(session.TryProduce(480) && session.Read(buffer) == 480 && buffer.All(s => s == 0),
            "master mute also silences actual relay events in the shared stream");
    }
}
