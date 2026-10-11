// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
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
        new(nameof(StoredEnergyWarningDucksAndRestoresMonitor), StoredEnergyWarningDucksAndRestoresMonitor),
        new(nameof(CprSuppressesHeartbeatWithoutSuppressingAlarmsOrReplayingBeats), CprSuppressesHeartbeatWithoutSuppressingAlarmsOrReplayingBeats),
        new(nameof(CprInstructionPrecedesClicksAndCoachingAvoidsBreaths), CprInstructionPrecedesClicksAndCoachingAvoidsBreaths)
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
        foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var phase in new[] { TherapySoundPhase.CprShock, TherapySoundPhase.CprNoShock })
            {
                var renderer = new TherapySoundRenderer();
                renderer.Update(Request(phase, locale), announce: false);
                int intro = CprInstructionFrames(phase, locale);
                int compressions = 30 * 48000 * 60 / 110;
                int cycle = compressions + 5 * 48000;
                float[] samples = Render(renderer, intro + 2 * cycle);
                float[] expected = new float[samples.Length];
                float[] click = SelectedTherapySounds.Get("Cpr").ToArray().Select(s => s / 32768f).ToArray();
                float[] breath = SelectedTherapySounds.Get("AED_CPR_BREATHS", locale).ToArray().Select(s => s / 32768f).ToArray();
                for (int round = 0; round < 2; round++)
                {
                    for (int beat = 0; beat < 30; beat++)
                    {
                        // Integer frame rounding follows the renderer's 110/min clock.
                        int slot = (int)Math.Ceiling(beat * 48000d * 60 / 110);
                        int offset = slot - beat * 48000 * 60 / 110;
                        click.AsSpan(offset).CopyTo(expected.AsSpan(intro + round * cycle + slot));
                    }
                    breath.CopyTo(expected, intro + round * cycle + compressions);
                }
                Check.That(samples.SequenceEqual(expected),
                    "after the entry instruction, each cycle has exactly 30 clicks followed by two breaths in a five-second window");
                renderer.Update(Request(phase, locale) with { CprElapsedMilliseconds = 120000 });
                Check.That(Render(renderer, 48000).All(s => s == 0), "no metronome or queued breaths outlive 120 seconds");
            }
    }

    private static int CprInstructionFrames(TherapySoundPhase phase, string locale) =>
        phase == TherapySoundPhase.CprShock
            ? SelectedTherapySounds.FrameCount("AED_SHOCK_RECORDED", locale) + 7680
            : SelectedTherapySounds.FrameCount("AED_NSA_CPR", locale) + 7680 +
                SelectedTherapySounds.FrameCount("AED_CPR_START", locale) + 7680;

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
        var request = Request(TherapySoundPhase.CprShock, "en") with { CprElapsedMilliseconds = 60000 };
        var reference = new TherapySoundRenderer();
        reference.Update(request with { CprElapsedMilliseconds = 0 });
        Render(reference, 60000 * 48);
        var reconnected = new TherapySoundRenderer();
        reconnected.Update(request, announce: false);
        Check.That(Render(reconnected, 4800).SequenceEqual(Render(reference, 4800)),
            "a fresh stream anchors to the current CPR cycle without replaying its introduction");
        reconnected.Update(request with { CprElapsedMilliseconds = 60050 }, announce: false);
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

    private static void CprInstructionPrecedesClicksAndCoachingAvoidsBreaths()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var phase in new[] { TherapySoundPhase.CprShock, TherapySoundPhase.CprNoShock })
            {
                var guided = new TherapySoundRenderer();
                var rhythm = new TherapySoundRenderer();
                guided.Update(Request(phase, locale));
                rhythm.Update(Request(phase, locale), announce: false);
                const int compressions = 30 * 48000 * 60 / 110;
                const int cycle = compressions + 5 * 48000;
                int start = CprInstructionFrames(phase, locale);
                float[] actual = Render(guided, start + 2 * cycle);
                float[] clicks = Render(rhythm, actual.Length);
                string introId = phase == TherapySoundPhase.CprShock ? "AED_SHOCK_RECORDED" : "AED_NSA_CPR";
                var intro = SelectedTherapySounds.Get(introId, locale).Span;
                for (int i = 0; i < intro.Length; i++)
                {
                    Check.That(actual[i] == intro[i] / 32768f,
                        "the initial action instruction plays without compression clicks");
                }
                if (phase == TherapySoundPhase.CprNoShock)
                {
                    var instruction = SelectedTherapySounds.Get("AED_CPR_START", locale).Span;
                    int at = intro.Length + 7680;
                    for (int i = 0; i < instruction.Length; i++)
                    {
                        Check.That(actual[at + i] == instruction[i] / 32768f,
                            "no-shock advice is followed by an explicit instruction to begin chest compressions");
                    }
                }
                Check.That(clicks.Take(start).All(s => s == 0),
                    "neither compression clicks nor ventilation cues precede the completed instruction");
                Check.That(actual.AsSpan(start - 7680, 7680).ToArray().All(s => s == 0),
                    "a short gap separates the action instruction from the first compression");
                var hands = SelectedTherapySounds.Get("AED_CPR_HANDS", locale).Span;
                for (int i = 0; i < hands.Length; i++)
                {
                    Check.That(actual[start + i] == hands[i] / 32768f + clicks[start + i],
                        "coaching starts with the first compression and preserves the click volume");
                }
                foreach (string id in new[] { "AED_CPR_HANDS", "AED_CPR_DEPTH" })
                {
                    Check.That(SelectedTherapySounds.FrameCount(id, locale) < compressions,
                        "each coaching instruction fits inside a compression window");
                }
                int introFrames = intro.Length;
                Check.That(actual.Skip(introFrames).Where((s, i) => Math.Abs(s - clicks[i + introFrames]) > .001).Any(),
                    "detailed coaching plays after the immediate action instruction");
                for (int at = start + compressions; at < actual.Length; at += cycle)
                {
                    Check.That(actual.AsSpan(at, 5 * 48000).SequenceEqual(clicks.AsSpan(at, 5 * 48000)),
                        "coaching never overlaps the two-breath prompt or inserts extra ventilation delays");
                }
                var reconnected = new TherapySoundRenderer();
                reconnected.Update(Request(phase, locale) with { CprElapsedMilliseconds = 1000 }, announce: false);
                Check.That(Render(reconnected, actual.Length - 48000).AsSpan().SequenceEqual(clicks.AsSpan(48000)),
                    "reconnecting during the instruction preserves the first full 30:2 cycle without replaying speech");
                guided.Update(Request(phase, locale, 2) with { CprElapsedMilliseconds = 119990 }, announce: false);
                Check.That(Render(guided, 960).Skip(480).All(s => s == 0), "audio stops at 120 seconds even without another UI snapshot");
            }
    }

    private static void CprSuppressesHeartbeatWithoutSuppressingAlarmsOrReplayingBeats()
    {
        foreach (var phase in new[] { TherapySoundPhase.CprShock, TherapySoundPhase.CprNoShock })
        {
            var mixed = new AudioRenderSession();
            var reference = new AudioRenderSession();
            var mixedOwner = new MonitorAlarmSequencer(mixed);
            var referenceOwner = new MonitorAlarmSequencer(reference);
            float[] actual = new float[480];
            float[] expected = new float[480];
            mixedOwner.UpdateHeartbeat(true, 100);
            for (int i = 0; i < 2; i++) { mixed.TryProduce(480); mixed.Read(actual); }
            Check.That(actual.Any(s => s != 0), "a heartbeat is already sounding before CPR starts");
            for (int i = 0; i < 2; i++) { reference.TryProduce(480); reference.Read(expected); }
            var request = Request(phase) with { Announce = false };
            mixed.UpdateTherapy(request);
            reference.UpdateTherapy(request);
            for (int block = 0; block < 3000; block++)
            {
                var alarm = new MonitorAlarmSoundRequest(MonitorNoticeLevel.Critical, 100, new());
                mixedOwner.Update(alarm);
                referenceOwner.Update(alarm);
                mixedOwner.UpdateHeartbeat(true, block % 40 == 0 ? 100 : null);
                mixed.TryProduce(480); mixed.Read(actual);
                reference.TryProduce(480); reference.Read(expected);
                if (block > 0)
                { Check.That(actual.SequenceEqual(expected), "CPR cancels existing beats and drops new ones, retaining alarms through compression and ventilation"); }
            }
            mixed.UpdateTherapy(null);
            reference.UpdateTherapy(null);
            mixedOwner.Update(null);
            referenceOwner.Update(null);
            for (int block = 0; block < 50; block++)
            {
                mixedOwner.UpdateHeartbeat(true, null);
                mixed.TryProduce(480); mixed.Read(actual);
                reference.TryProduce(480); reference.Read(expected);
                Check.That(actual.SequenceEqual(expected), "CPR exit does not replay any suppressed beats");
            }
            mixedOwner.UpdateHeartbeat(true, 100);
            for (int i = 0; i < 2; i++) { mixed.TryProduce(480); mixed.Read(actual); reference.TryProduce(480); reference.Read(expected); }
            Check.That(!actual.SequenceEqual(expected), "only a new heartbeat restores the normal monitor rhythm");
        }
    }
}
