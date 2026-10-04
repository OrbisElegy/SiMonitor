// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class NotificationSoundSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(NotificationSoundRendersOneExactGroup), NotificationSoundRendersOneExactGroup),
        new(nameof(NotificationSoundCoalescesWithoutTruncationOrReplay), NotificationSoundCoalescesWithoutTruncationOrReplay),
        new(nameof(NotificationSoundEscalationInterruptsAndCancellationDoesNotReplay), NotificationSoundEscalationInterruptsAndCancellationDoesNotReplay),
        new(nameof(NotificationRouterRespectsSuppressionPauseAndPriority), NotificationRouterRespectsSuppressionPauseAndPriority),
        new(nameof(NotificationRouterCoalescesHighestPriorityConditions), NotificationRouterCoalescesHighestPriorityConditions),
        new(nameof(NotificationRouterRejectsAtomicallyAndReportsMissingHistory), NotificationRouterRejectsAtomicallyAndReportsMissingHistory),
        new(nameof(NotificationRouterArbitratesContinuousSources), NotificationRouterArbitratesContinuousSources),
        new(nameof(NotificationRouterPreservesSuppressionAndValidatesMixedSources), NotificationRouterPreservesSuppressionAndValidatesMixedSources),
        new(nameof(NotificationRouterContinuousRefreshPreservesPcm), NotificationRouterContinuousRefreshPreservesPcm),
        new(nameof(NotificationRouterMixesIndependentDurations), NotificationRouterMixesIndependentDurations)
    ];

    private static float[] Render(AudioRenderSession session, MonitorAlarmSequencer sequencer,
        MonitorAlarmSoundRequest? request, int frames)
    {
        float[] data = new float[frames];
        for (int offset = 0; offset < frames; offset += 240)
        {
            int count = Math.Min(240, frames - offset);
            sequencer.Update(request);
            Check.That(session.TryProduce(count) && session.Read(data.AsSpan(offset, count)) == count, "continuous PCM production has no underrun");
        }
        sequencer.Update(request);
        return data;
    }

    private static void NotificationSoundRendersOneExactGroup()
    {
        foreach (var level in Enum.GetValues<MonitorNoticeLevel>())
        {
            var session = new AudioRenderSession();
            var sequencer = new MonitorAlarmSequencer(session);
            var request = new MonitorAlarmSoundRequest(level, 50, new(InfoTone: true)) { NotificationSequence = 1 };
            float[] actual = Render(session, sequencer, request, 6 * 48000);
            var reference = new SampleToneRenderer();
            long key = 0;
            foreach (int onset in MonitorSoundPattern.OnsetsMilliseconds(level))
            {
                long frame = 480 + onset * 48L;
                reference.Schedule(++key, SelectedMonitorTones.Alarm(level, 50), frame, frame + 2400);
            }
            float[] expected = new float[actual.Length]; reference.Render(expected);
            Check.That(actual.SequenceEqual(expected) && actual.Any(v => v != 0), "one notification renders precisely one original tone group at every severity");
            Check.That(sequencer.Dispatches.Select(d => d.Stage).SequenceEqual(new[] { AlarmSoundDispatchStage.Selected, AlarmSoundDispatchStage.RenderWindowElapsed }),
                "render feedback distinguishes selection and elapsed window without claiming physical delivery");
            Check.That(Render(session, sequencer, request, 48000).All(v => v == 0), "same request never starts a later group");
        }
        var silent = new AudioRenderSession(); var silentSequencer = new MonitorAlarmSequencer(silent);
        Check.That(Render(silent, silentSequencer, new(MonitorNoticeLevel.Info, 50, new()) { NotificationSequence = 1 }, 48000).All(v => v == 0) &&
            silentSequencer.Dispatches.Single().Stage == AlarmSoundDispatchStage.SkippedSilent, "Info remains silent without opt-in");
    }

    private static void NotificationSoundCoalescesWithoutTruncationOrReplay()
    {
        var session = new AudioRenderSession(); var sequencer = new MonitorAlarmSequencer(session);
        var first = new MonitorAlarmSoundRequest(MonitorNoticeLevel.Warning, 50, new()) { NotificationSequence = 1 };
        float[] head = Render(session, sequencer, first, 4800);
        var second = first with { NotificationSequence = 2 };
        float[] tail = Render(session, sequencer, second, 4 * 48000 - head.Length);
        var referenceSession = new AudioRenderSession(); var referenceSequencer = new MonitorAlarmSequencer(referenceSession);
        Check.That(head.Concat(tail).SequenceEqual(Render(referenceSession, referenceSequencer, first, 4 * 48000)),
            "busy same-level reminder neither truncates a Warning group nor starts a deferred replay");
        Check.That(sequencer.Dispatches.Any(d => d.NotificationSequence == 2 && d.Stage == AlarmSoundDispatchStage.CoalescedWhileBusy), "busy decision is visible");
        var third = first with { NotificationSequence = 3 };
        Check.That(Render(session, sequencer, third, 4800).Any(v => v != 0), "fresh reminder after completion starts a new group");
        var before = sequencer.Dispatches;
        bool rejected = false;
        try { sequencer.Update(third with { VolumePercent = 101, NotificationSequence = 4 }); }
        catch (ArgumentException) { rejected = true; }
        Check.That(rejected && sequencer.Dispatches.SequenceEqual(before), "invalid sound request does not interrupt the valid group or consume its sequence");
    }

    private static void NotificationSoundEscalationInterruptsAndCancellationDoesNotReplay()
    {
        var session = new AudioRenderSession(); var sequencer = new MonitorAlarmSequencer(session);
        var warning = new MonitorAlarmSoundRequest(MonitorNoticeLevel.Warning, 50, new()) { NotificationSequence = 1 };
        Render(session, sequencer, warning, 4800);
        var critical = warning with { Level = MonitorNoticeLevel.Critical, NotificationSequence = 2 };
        Check.That(Render(session, sequencer, critical, 4800).Any(v => v != 0), "higher priority starts immediately without waiting for the old group");
        Check.That(sequencer.Dispatches.Any(d => d.NotificationSequence == 1 && d.Stage == AlarmSoundDispatchStage.Interrupted) &&
            sequencer.Dispatches.Any(d => d.NotificationSequence == 2 && d.Stage == AlarmSoundDispatchStage.Selected), "interruption identifies old and new notifications");
        Render(session, sequencer, null, 4800);
        Check.That(Render(session, sequencer, critical, 48000).All(v => v == 0), "cancelled identity cannot replay on resume");
        var resumed = critical with { NotificationSequence = 5 };
        Check.That(Render(session, sequencer, resumed, 4800).Any(v => v != 0) && sequencer.MissedNotificationCount == 2,
            "fresh resume identity plays once and missing mailbox sequence values are counted");
    }

    private static void NotificationRouterArbitratesContinuousSources()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        filter.Lifecycle.ConfigureNotifications("hr-high", new(0, 200));
        var router = new AlarmNotificationSoundRouter();
        var info = new MonitorNotice("signal", MonitorNoticeLevel.Info, "Signal unavailable");
        var technical = new MonitorNotice("technical", MonitorNoticeLevel.Notice, "Technical notice");
        var test = new MonitorNotice("explicit-test", MonitorNoticeLevel.Critical, "Test notice");
        var outputFault = new MonitorNotice("audio-output", MonitorNoticeLevel.Critical, "Output unavailable") { Audible = false };
        MonitorAlarmSoundRequest? Route(params MonitorNotice[] notices) => router.Update([filter.Lifecycle], 50, new(), true, notices);
        Check.That(Route(info, outputFault) is null, "silent Info and inaudible output failures cannot request audio");
        var continuous = Route(technical);
        Check.That(continuous is { NotificationSequence: 0, Level: MonitorNoticeLevel.Notice } &&
            router.Routes.Single().ContinuousNotices.Single() == technical && router.Routes.Single().Decisions.Count == 0,
            "unregistered technical notices retain continuous sound with explicit source tracing");
        Read(filter, 0, 130000);
        var single = Route(technical, outputFault);
        Check.That(single is { NotificationSequence: 1, Level: MonitorNoticeLevel.Warning }, "higher physiology preempts continuous technical sound; silent faults do not compete");
        Read(filter, 100, 180001);
        Check.That(Route(test) is { NotificationSequence: 0, Level: MonitorNoticeLevel.Critical }, "same-level continuous test covers simultaneous physiology");
        Read(filter, 300, 180001); Route(test);
        Check.That(Route() is null, "reminder consumed under continuous sound is not replayed on withdrawal");
        Read(filter, 500, 180001);
        Check.That(Route() is { NotificationSequence: 2 }, "next fresh physiological reminder restores single-group playback");
        Read(filter, 600, 100000);
        Check.That(Route(technical) == continuous, "still-active lower continuous source resumes when physiology clears");
        Check.That(router.Update([filter.Lifecycle], 50, new(), false, [technical]) is null && Route() is null,
            "a technical condition removed while paused cannot resurrect on resume");
        Check.That(router.Update([filter.Lifecycle], 50, new(InfoTone: true), true, [info]) is { NotificationSequence: 0, Level: MonitorNoticeLevel.Info },
            "Info uses its original sparse continuous pattern only after explicit opt-in");
    }

    private static void NotificationRouterPreservesSuppressionAndValidatesMixedSources()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        filter.Lifecycle.ConfigureNotifications("hr-high", new(1000, 0));
        var router = new AlarmNotificationSoundRouter();
        var registered = new MonitorNotice("hr-high", MonitorNoticeLevel.Warning, "HR high");
        MonitorAlarmSoundRequest? Route(params MonitorNotice[] notices) => router.Update([filter.Lifecycle], 50, new(), true, notices);
        Read(filter, 0, 130000); Route(registered);
        Read(filter, 100, 100000); Route();
        Read(filter, 200, 130000);
        Check.That(Route(registered) is null, "registered banner must not reenter as continuous fallback and bypass suppression");
        var lower = new MonitorNotice("technical", MonitorNoticeLevel.Notice, "Technical");
        Check.That(Route(lower) is null, "lower continuous sound cannot override a higher active suppressed condition");
        Read(filter, 300, 180001);
        int before = router.Routes.Count;
        foreach (var invalid in new MonitorNotice[][] { [lower, lower], [null!], [lower with { Level = (MonitorNoticeLevel)99 }], [lower with { Id = "" }] })
        {
            bool rejected = false;
            try { Route(invalid); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected && router.Routes.Count == before, "invalid mixed input is rejected before consuming fresh escalation");
        }
        Check.That(Route(lower) is { NotificationSequence: 2, Level: MonitorNoticeLevel.Critical }, "valid retry retains the unconsumed escalation");
        var empty = new AlarmNotificationSoundRouter();
        empty.Update([], 50, new(), true, [lower]);
        bool changed = false;
        try { empty.Update([filter.Lifecycle], 50, new(), true); } catch (ArgumentException) { changed = true; }
        Check.That(changed, "even an initially empty registry has fixed ownership");
    }

    private static void NotificationRouterContinuousRefreshPreservesPcm()
    {
        var router = new AlarmNotificationSoundRouter();
        var session = new AudioRenderSession();
        var sequencer = new MonitorAlarmSequencer(session);
        var notice = new MonitorNotice("explicit-test", MonitorNoticeLevel.Warning, "Test");
        var expectedSession = new AudioRenderSession();
        var expectedSequencer = new MonitorAlarmSequencer(expectedSession);
        var continuous = new MonitorAlarmSoundRequest(MonitorNoticeLevel.Warning, 50, new());
        var actual = new List<float>();
        for (int i = 0; i < 70; i++)
        {
            // Identity/order/text changes are presentation changes, not a new sound group.
            var request = router.Update([], 50, new(), true, [notice with { Text = "Test " + i }]);
            actual.AddRange(Render(session, sequencer, request, 4800));
        }
        Check.That(actual.SequenceEqual(Render(expectedSession, expectedSequencer, continuous, actual.Count)),
            "continuous mixed source refresh renders the exact legacy PCM across multiple periods");
        var saved = router.Routes;
        router.Update([], 50, new(), true, [notice with { Id = "replacement" }]);
        Check.That(saved[^1].ContinuousNotices.Single().Id == "explicit-test", "continuous source records are immutable snapshots");
        router.Update([], 50, new(), true, []);
        Check.That(Render(session, sequencer, null, 4800).Skip(2400).All(v => v == 0), "withdrawing all sources cancels ongoing PCM");
    }

    private static void NotificationRouterMixesIndependentDurations()
    {
        var heart = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var saturation = new ConfirmedLimitNotice(MonitorNumeric.SpO2);
        heart.Lifecycle.ConfigureNotifications("hr-high", new() { SoundDuration = AlarmSoundDuration.Continuous });
        saturation.Lifecycle.ConfigureNotifications("spo2-low", new(1000, 200));
        var router = new AlarmNotificationSoundRouter();
        MonitorAlarmSoundRequest? Route(bool enabled = true) => router.Update([heart.Lifecycle, saturation.Lifecycle], 50, new(), enabled);
        void Sample(long time, int rate, int spo2)
        {
            Read(heart, time, rate);
            saturation.Evaluate(MeasuredLimitNotice.SpO2Descriptor.TeachingDefaults with { Enabled = true }, Empty with
            { SampleTimeNs = time * 1_000_000, SpO2 = new(WaveformMeasurementStatus.Valid, spo2, null, time * 1_000_000) });
        }
        Sample(0, 130000, 91000);
        var continuous = Route();
        Check.That(continuous is { NotificationSequence: 0, Level: MonitorNoticeLevel.Warning } &&
            router.Routes[^1].ContinuousConditions.Single().ConditionId == "hr-high", "same-level long condition carries sound with traceable physiology");
        Sample(200, 130000, 91000);
        Check.That(Route() == continuous, "short reminders do not restart a same-level long group");
        Sample(300, 130000, 84000);
        var shortCritical = Route();
        Check.That(shortCritical is { NotificationSequence: > 0, Level: MonitorNoticeLevel.Critical }, "higher short event preempts a lower long event");
        Sample(400, 130000, 98000);
        Check.That(Route() == continuous, "still-active long event resumes after higher short event recovers");
        Sample(500, 180001, 98000); Route();
        Sample(600, 130000, 98000);
        Check.That(Route() == continuous, "long event follows confirmed downgrade without needing a new notification intent");
        Check.That(Route(false) is null && Route() == continuous, "long playback respects pause and resumes only current activity");
        var episode = heart.Lifecycle.Conditions.Single(c => c.ConditionId == "hr-high").Episode;
        heart.Lifecycle.ConfigureNotifications("hr-high", new(1000, 0));
        Check.That(Route() is null, "switching long to short cannot reuse a stale policy decision");
        Sample(700, 130000, 98000);
        var shortWarning = Route();
        Check.That(shortWarning is { NotificationSequence: > 0, Level: MonitorNoticeLevel.Warning } &&
            heart.Lifecycle.Conditions.Single(c => c.ConditionId == "hr-high").Episode == episode, "fresh short decision preserves physiological identity");
        var longSession = new AudioRenderSession(); var longSequencer = new MonitorAlarmSequencer(longSession);
        var shortSession = new AudioRenderSession(); var shortSequencer = new MonitorAlarmSequencer(shortSession);
        float[] longPcm = Render(longSession, longSequencer, continuous, 7 * 48000);
        float[] shortPcm = Render(shortSession, shortSequencer, shortWarning, 7 * 48000);
        Check.That(longPcm.Skip(5 * 48000).Any(v => v != 0) && shortPcm.Skip(5 * 48000).All(v => v == 0),
            "independent duration choices reach actual repeated versus single-group PCM");
        heart.Reset();
        Check.That(Route() is null, "ending a condition cancels either duration without acknowledging or latching it");
    }

    private static readonly LiveMeasurementSnapshot Empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
    private static readonly MeasurementLimits Limits = MeasuredLimitNotice.HeartRateDescriptor.TeachingDefaults with { Enabled = true };
    private static void Read(ConfirmedLimitNotice filter, long milliseconds, int value) => filter.Evaluate(Limits,
        Empty with { SampleTimeNs = milliseconds * 1_000_000, HeartRate = new(WaveformMeasurementStatus.Valid, value, milliseconds * 1_000_000) });

    private static void NotificationRouterRespectsSuppressionPauseAndPriority()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        filter.Lifecycle.ConfigureNotifications("hr-high", new(1000, 500));
        var router = new AlarmNotificationSoundRouter();
        MonitorAlarmSoundRequest? Route(bool enabled = true) => router.Update([filter.Lifecycle], 50, new(), enabled);
        Read(filter, 0, 130000);
        var first = Route();
        Check.That(first is { Level: MonitorNoticeLevel.Warning, NotificationSequence: 1 } && Route() == first, "first intent is consumed exactly once");
        Read(filter, 100, 100000); Check.That(Route() is null, "recovery cancels the current request");
        Read(filter, 200, 130000); Check.That(Route() is null, "suppressed recurrence has no sound request although physiology is active");
        Read(filter, 300, 180001);
        var critical = Route();
        Check.That(critical is { Level: MonitorNoticeLevel.Critical, NotificationSequence: 2 }, "escalation bypasses repeat suppression in the sound bridge");
        Check.That(Route(false) is null, "pause immediately cancels playback");
        Read(filter, 600, 180001); Route(false); Read(filter, 800, 180001); Route(false);
        var resumed = Route();
        Check.That(resumed is { NotificationSequence: 3, Level: MonitorNoticeLevel.Critical } && Route() == resumed, "resume issues one fresh current request instead of replaying paused reminders");
        Read(filter, 900, 100000); Route(false);
        Check.That(Route() is null, "cleared alarm is never resurrected by resume");
        Read(filter, 1000, 180001); Check.That(Route() is null, "resume cannot bypass an actual recurrence suppression window");
        Read(filter, 1300, 180001); Check.That(Route() is null, "suppression is anchored to the latest policy intent");
        Read(filter, 1600, 180001); Route();
        Read(filter, 1800, 180001);
        Check.That(Route()?.Level == MonitorNoticeLevel.Critical, "deferred repeat becomes audible exactly at its intent deadline");
    }

    private static void NotificationRouterCoalescesHighestPriorityConditions()
    {
        var heart = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        var saturation = new ConfirmedLimitNotice(MonitorNumeric.SpO2);
        heart.Lifecycle.ConfigureNotifications("hr-high", new(0, 200));
        void ReadSaturation(long milliseconds, int value) => saturation.Evaluate(
            MeasuredLimitNotice.SpO2Descriptor.TeachingDefaults with { Enabled = true },
            Empty with { SampleTimeNs = milliseconds * 1_000_000, SpO2 = new(WaveformMeasurementStatus.Valid, value, null, milliseconds * 1_000_000) });
        var router = new AlarmNotificationSoundRouter();
        MonitorAlarmSoundRequest? Route() => router.Update([heart.Lifecycle, saturation.Lifecycle], 50, new(), true);
        Read(heart, 0, 180001); ReadSaturation(0, 84000);
        var first = Route();
        Check.That(first?.Level == MonitorNoticeLevel.Critical && router.Routes.Single().Decisions.Count == 2,
            "simultaneous highest-level intents share one group with traceable source episodes");
        Read(heart, 100, 130000); ReadSaturation(100, 84000); Route();
        Read(heart, 200, 130000); ReadSaturation(200, 84000);
        Check.That(Route() == first, "lower priority reminder cannot displace an active higher-priority condition");
        Read(heart, 300, 130000); ReadSaturation(300, 98000);
        Check.That(Route() is null, "expired lower-priority intent is not queued for later playback");
        Read(heart, 400, 130000); ReadSaturation(400, 98000);
        Check.That(Route() is { NotificationSequence: 2, Level: MonitorNoticeLevel.Warning } && router.Routes[^1].Decisions.Single().Episode.ConditionId == "hr-high",
            "next fresh lower-level reminder plays after the higher condition clears");
    }

    private static void NotificationRouterRejectsAtomicallyAndReportsMissingHistory()
    {
        var filter = new ConfirmedLimitNotice(MonitorNumeric.HeartRate);
        filter.Lifecycle.ConfigureNotifications("hr-high", new(0, 1));
        for (int time = 0; time <= 300; time++) { Read(filter, time, 180001); }
        var router = new AlarmNotificationSoundRouter();
        var request = router.Update([filter.Lifecycle], 50, new(), true);
        Check.That(request is { NotificationSequence: 1 } && router.MissedRecordCount == 45, "late consumer coalesces current history and reports evicted records");
        foreach (var sources in new IReadOnlyList<AlarmLifecycleJournal>[] { [], [filter.Lifecycle, filter.Lifecycle], [new ConfirmedLimitNotice(MonitorNumeric.HeartRate).Lifecycle, filter.Lifecycle] })
        {
            bool rejected = false;
            try { router.Update(sources, 50, new(), true); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected && router.Update([filter.Lifecycle], 50, new(), true) == request, "invalid or replaced owners cannot partially consume routing state");
        }
        Check.That(router.Update([filter.Lifecycle], 0, new(), true) is null &&
            router.Update([filter.Lifecycle], 50, new(), true)?.NotificationSequence == 2, "mute/unmute gets fresh identity rather than replaying a cancelled group");
    }
}
