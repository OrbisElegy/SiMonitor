// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Scenarios;

namespace Monitor.Specs;

internal static class VitalChangeSchedulerSpecifications
{
    private const long Second = 1_000_000_000;

    public static Specification[] All =>
    [
        new(nameof(ChangesRampLinearlyAndPublishOncePerStep), ChangesRampLinearlyAndPublishOncePerStep),
        new(nameof(SignsRampIndependentlyAndLaterEventsTakeOver), SignsRampIndependentlyAndLaterEventsTakeOver),
        new(nameof(DelayedAndChainedEventsStartAtTheirDueTimes), DelayedAndChainedEventsStartAtTheirDueTimes),
        new(nameof(StopAndRebaseHoldValuesAndCancelUnfinishedEvents), StopAndRebaseHoldValuesAndCancelUnfinishedEvents),
        new(nameof(InvalidChangesAreRejectedWithoutChangingTheQueue), InvalidChangesAreRejectedWithoutChangingTheQueue),
    ];

    private static Dictionary<VitalSign, int> Baseline => new()
    {
        [VitalSign.HeartRateBpm] = 80,
        [VitalSign.SpO2MilliPercent] = 98_000,
        [VitalSign.AbpSystolicCentiMmHg] = 12000,
    };

    private static VitalChangeEvent Change(VitalSign sign, int target, long durationNs,
        VitalChangeTrigger trigger = VitalChangeTrigger.Manual, long delayNs = 0) =>
        new(new Dictionary<VitalSign, int> { [sign] = target }, durationNs, trigger, delayNs);

    private static VitalChangeState State(VitalChangeScheduler scheduler, int id) => scheduler.Events.Single(entry => entry.Id == id).State;

    private static void ChangesRampLinearlyAndPublishOncePerStep()
    {
        var scheduler = new VitalChangeScheduler(Baseline, 0);
        Check.That(scheduler.Advance(0) is null, "nothing is published before an event starts");
        int id = scheduler.Add(Change(VitalSign.HeartRateBpm, 120, 40 * Second));
        Check.That(scheduler.Advance(Second) is null && State(scheduler, id) == VitalChangeState.Waiting, "a manual event waits for its trigger");
        scheduler.Trigger(id);
        Check.That(scheduler.Advance(Second) is null, "starting a ramp publishes nothing while values are unchanged");
        Check.That(scheduler.Advance(2 * Second) is null && scheduler.Advance(3 * Second)![VitalSign.HeartRateBpm] == 82,
            "values are published once per two-second step");
        Check.That(scheduler.Advance(4 * Second) is null && scheduler.Advance(5 * Second)![VitalSign.HeartRateBpm] == 84,
            "the next step follows two seconds after the previous publication");
        // 80 + 40 * 19.5 / 40 = 99.5 rounds to the even 100.
        Check.That(scheduler.ValuesAt(20_500_000_000)[VitalSign.HeartRateBpm] == 100, "interpolation rounds ties to even");
        var final = scheduler.Advance(41 * Second);
        Check.That(final![VitalSign.HeartRateBpm] == 120 && final[VitalSign.SpO2MilliPercent] == 98_000 &&
            State(scheduler, id) == VitalChangeState.Done && !scheduler.IsChanging, "the exact target is published when the ramp ends");
        Check.That(scheduler.Advance(50 * Second) is null, "a finished change publishes nothing more");
    }

    private static void SignsRampIndependentlyAndLaterEventsTakeOver()
    {
        var scheduler = new VitalChangeScheduler(Baseline, 0);
        int heart = scheduler.Add(Change(VitalSign.HeartRateBpm, 120, 40 * Second));
        scheduler.Add(Change(VitalSign.SpO2MilliPercent, 88_000, 20 * Second, VitalChangeTrigger.AfterDelay, 10 * Second));
        scheduler.Trigger(heart);
        _ = scheduler.Advance(20 * Second);
        var values = scheduler.ValuesAt(20 * Second);
        Check.That(values[VitalSign.HeartRateBpm] == 100 && values[VitalSign.SpO2MilliPercent] == 93_000 &&
            values[VitalSign.AbpSystolicCentiMmHg] == 12000, "each sign follows its own ramp; unnamed signs keep their values");
        int jump = scheduler.Add(Change(VitalSign.HeartRateBpm, 60, 0));
        scheduler.Trigger(jump);
        var published = scheduler.Advance(20 * Second);
        Check.That(published![VitalSign.HeartRateBpm] == 60 && State(scheduler, jump) == VitalChangeState.Done,
            "a zero-duration event takes over its sign immediately");
        scheduler.Remove(heart);
        Check.That(State(scheduler, heart) == VitalChangeState.Cancelled && scheduler.ValuesAt(30 * Second)[VitalSign.HeartRateBpm] == 60 &&
            scheduler.ValuesAt(30 * Second)[VitalSign.SpO2MilliPercent] == 88_000,
            "cancelling a superseded event leaves the signs another event owns");
    }

    private static void DelayedAndChainedEventsStartAtTheirDueTimes()
    {
        var scheduler = new VitalChangeScheduler(Baseline, 0);
        int first = scheduler.Add(Change(VitalSign.HeartRateBpm, 100, 10 * Second, VitalChangeTrigger.AfterDelay, 5 * Second));
        int second = scheduler.Add(Change(VitalSign.HeartRateBpm, 60, 20 * Second, VitalChangeTrigger.AfterPrevious));
        _ = scheduler.Advance(7 * Second);
        Check.That(scheduler.Events.Single(entry => entry.Id == first).StartSimTimeNs == 5 * Second &&
            State(scheduler, second) == VitalChangeState.Waiting, "a delayed event starts at its due time even when advanced later");
        _ = scheduler.Advance(25 * Second);
        Check.That(State(scheduler, first) == VitalChangeState.Done &&
            scheduler.Events.Single(entry => entry.Id == second).StartSimTimeNs == 15 * Second &&
            scheduler.ValuesAt(25 * Second)[VitalSign.HeartRateBpm] == 80, "a chained event starts where its predecessor ended");
        var chain = new VitalChangeScheduler(Baseline, 0);
        int head = chain.Add(Change(VitalSign.HeartRateBpm, 100, 10 * Second));
        int tail = chain.Add(Change(VitalSign.HeartRateBpm, 70, 0, VitalChangeTrigger.AfterPrevious));
        chain.Remove(head);
        Check.That(chain.Advance(Second)![VitalSign.HeartRateBpm] == 70 && State(chain, tail) == VitalChangeState.Done,
            "removing a predecessor releases the event chained to it");
    }

    private static void StopAndRebaseHoldValuesAndCancelUnfinishedEvents()
    {
        var scheduler = new VitalChangeScheduler(Baseline, 0);
        int ramp = scheduler.Add(Change(VitalSign.HeartRateBpm, 120, 40 * Second));
        int waiting = scheduler.Add(Change(VitalSign.SpO2MilliPercent, 90_000, 0, VitalChangeTrigger.AfterDelay, 60 * Second));
        scheduler.Trigger(ramp);
        _ = scheduler.Advance(10 * Second);
        scheduler.Stop();
        Check.That(State(scheduler, ramp) == VitalChangeState.Cancelled && State(scheduler, waiting) == VitalChangeState.Cancelled &&
            !scheduler.IsChanging && scheduler.ValuesAt(30 * Second)[VitalSign.HeartRateBpm] == 90, "stop holds current values and cancels the queue");

        var restarted = new VitalChangeScheduler(Baseline, 0);
        int delayed = restarted.Add(Change(VitalSign.HeartRateBpm, 100, 0, VitalChangeTrigger.AfterDelay, 30 * Second));
        int unsupported = restarted.Add(Change(VitalSign.SpO2MilliPercent, 90_000, 0));
        _ = restarted.Advance(20 * Second);
        restarted.Rebase(new Dictionary<VitalSign, int> { [VitalSign.HeartRateBpm] = 70 }, 0);
        Check.That(State(restarted, unsupported) == VitalChangeState.Cancelled && restarted.Advance(20 * Second) is null &&
            restarted.Advance(30 * Second)![VitalSign.HeartRateBpm] == 100,
            "a restart keeps supported waiting events and counts their delay from the restart");
    }

    private static void InvalidChangesAreRejectedWithoutChangingTheQueue()
    {
        var scheduler = new VitalChangeScheduler(Baseline, 0);
        foreach (var invalid in new[]
        {
            Change(VitalSign.HeartRateBpm, 181, 0),
            Change(VitalSign.EtCo2MmHg, 40, 0),
            Change(VitalSign.HeartRateBpm, 90, -1),
            Change(VitalSign.HeartRateBpm, 90, 0, VitalChangeTrigger.Manual, Second),
            Change(VitalSign.HeartRateBpm, 90, VitalChangeScheduler.MaximumDurationNs + 1),
            new VitalChangeEvent(new Dictionary<VitalSign, int>(), 0, VitalChangeTrigger.Manual),
        })
        {
            bool rejected = false;
            try { _ = scheduler.Add(invalid); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected && scheduler.Events.Count == 0, "an invalid change is rejected and not queued");
        }
        for (int index = 0; index < VitalChangeScheduler.MaximumEventCount; index++) { _ = scheduler.Add(Change(VitalSign.HeartRateBpm, 90, 0)); }
        bool full = false;
        try { _ = scheduler.Add(Change(VitalSign.HeartRateBpm, 90, 0)); }
        catch (InvalidOperationException exception) { full = exception.Message == "VitalChange.QueueFull"; }
        Check.That(full, "the queue holds a bounded number of unfinished events");
        bool baseline = false;
        try { _ = new VitalChangeScheduler(new Dictionary<VitalSign, int> { [VitalSign.CvpCentiMmHg] = 3001 }, 0); }
        catch (ArgumentException) { baseline = true; }
        Check.That(baseline, "a baseline outside the supported range is rejected");
    }
}
