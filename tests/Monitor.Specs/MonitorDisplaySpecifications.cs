// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class MonitorDisplaySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SkinsOwnFixedValidatedSlotsAndClampBothEdges), SkinsOwnFixedValidatedSlotsAndClampBothEdges),
        new(nameof(AutoRangeChangesOnlyAtSweepStartFromPreviousSamples), AutoRangeChangesOnlyAtSweepStartFromPreviousSamples),
        new(nameof(ManualAndFailedAutoRangesPreserveTheirState), ManualAndFailedAutoRangesPreserveTheirState),
        new(nameof(LiveSessionKeepsBoundedHistoryAndProgressiveFrontier), LiveSessionKeepsBoundedHistoryAndProgressiveFrontier),
        new(nameof(AutomaticRangeReservesFifteenPercentAndCalibration), AutomaticRangeReservesFifteenPercentAndCalibration),
        new(nameof(DenseContoursPreserveKnotsAndMonotonicity), DenseContoursPreserveKnotsAndMonotonicity),
        new(nameof(DenseContourPrefixesRemainLocal), DenseContourPrefixesRemainLocal),
        new(nameof(IndependentSpeedsRetainPreviousScaleUntilOverwritten), IndependentSpeedsRetainPreviousScaleUntilOverwritten),
        new(nameof(FrozenTraceOwnsSamplesAcrossLiveEviction), FrozenTraceOwnsSamplesAcrossLiveEviction),
    ];
    private static void FrozenTraceOwnsSamplesAcrossLiveEviction()
    {
        var display = MonitorDisplayConfiguration.Default(MonitorSkin.SevenRows);
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            new(display.Skin, display.Slots.Select((s, i) => s with { SpeedTenthsMmPerSecond = i % 2 == 0 ? 125 : 500 }).ToArray()), true);
        for (int i = 0; i < 280; i++) { session.Advance(50_000_000); }
        var frozen = FrozenMonitorTrace.Capture(session);
        var owned = frozen.Rows.Select(r => r.Samples.ToArray()).ToArray();
        long time = frozen.FrontierNs;
        bool beats = false;
        for (int i = 0; i < 1000; i++) { session.Advance(50_000_000); beats |= session.DetectedBeats.Count > 0; }
        Check.That(session.Blocks[0].StartSimTimeNs > time && session.Measurements!.SampleTimeNs > time && beats,
            "background ring evicts frozen time while measurement and fresh heartbeat events continue");
        for (int row = 0; row < frozen.Rows.Count; row++)
        {
            Check.That(frozen.Rows[row].Samples.SequenceEqual(owned[row]) && owned[row].All(p => p.TimeNs < time),
                "capture retains original samples and never includes buffered future data");
            Check.That(frozen.Rows[row].Samples.Count <= 10000, "two sweep cycles bound per-row storage at all supported speeds");
        }
        Check.That(frozen.FrontierNs == time && FrozenMonitorTrace.Capture(session).FrontierNs > time, "new capture cannot mutate existing snapshot");
        bool rejected = false;
        try { ((IList<(long TimeNs, double Value)>)frozen.Rows[0].Samples)[0] = (0, 0); }
        catch (NotSupportedException) { rejected = true; }
        Check.That(rejected, "callers cannot edit captured samples");
    }
    private static void SkinsOwnFixedValidatedSlotsAndClampBothEdges()
    {
        foreach (var skin in Enum.GetValues<MonitorSkin>())
        {
            var plan = MonitorDisplayConfiguration.Default(skin);
            Check.That(plan.Slots.Count == MonitorDisplayConfiguration.RowCount(skin), "skin fixes capacity independently of viewport");
        }
        var slots = MonitorDisplayConfiguration.Default(MonitorSkin.ThreeRows).Slots.ToArray();
        var configuration = new MonitorDisplayConfiguration(MonitorSkin.ThreeRows, slots);
        slots[0] = new(6, false, new(0, 1));
        Check.That(configuration.Slots[0].Channel == 0, "caller mutations cannot change accepted slots");
        Check.That(new MonitorAmplitudeRange(-5, 15).Normalize(-100) == 0 &&
            new MonitorAmplitudeRange(-5, 15).Normalize(100) == 1, "out-of-range samples flatten to the row edges");
        Reject(() => { _ = new MonitorDisplayConfiguration(MonitorSkin.FiveRows, slots); });
        Reject(() => new MonitorAmplitudeRange(1, 1).Validate());
        Reject(() => new MonitorAmplitudeRange(double.NaN, 1).Validate());
        Reject(() => new MonitorAmplitudeRange(0, double.PositiveInfinity).Validate());
    }
    private static void AutoRangeChangesOnlyAtSweepStartFromPreviousSamples()
    {
        var plan = MonitorDisplayConfiguration.Default(MonitorSkin.ThreeRows);
        var ranges = new MonitorSweepRanges(plan);
        int calls = 0;
        IEnumerable<double> Values(int channel, long start, long end)
        {
            calls++;
            Check.That(start == 0 && end == MonitorDisplayConfiguration.SweepDurationNs, "only the completed sweep is consulted");
            return [-10, 50, 100];
        }
        ranges.Advance(MonitorDisplayConfiguration.SweepDurationNs - 1, Values);
        Check.That(calls == 0 && ranges.Range(0) == plan.Slots[0].Range, "no mid-sweep scale changes");
        ranges.Advance(MonitorDisplayConfiguration.SweepDurationNs, Values);
        Check.That(calls == 3 && ranges.Range(0).Minimum < -10 && ranges.Range(0).Maximum > 100 && ranges.ShowPrevious(0) && ranges.PreviousRange(0) == plan.Slots[0].Range,
            "new scale includes padding and preserves the old range for old history");
        var accepted = ranges.Range(0);
        ranges.Advance(MonitorDisplayConfiguration.SweepDurationNs + 100, (_, _, _) => throw new InvalidOperationException());
        Check.That(ranges.Range(0) == accepted, "new samples cannot resize the current sweep");
        ranges.Advance(2 * MonitorDisplayConfiguration.SweepDurationNs, (_, _, _) => Array.Empty<double>());
        Check.That(ranges.Range(0) == accepted, "no-data sweep keeps the previous range");
    }
    private static void ManualAndFailedAutoRangesPreserveTheirState()
    {
        var slots = MonitorDisplayConfiguration.Default(MonitorSkin.ThreeRows).Slots.ToArray();
        slots[0] = slots[0] with { Automatic = false, Range = new(-1, 1) };
        var ranges = new MonitorSweepRanges(new(MonitorSkin.ThreeRows, slots));
        ranges.Advance(MonitorDisplayConfiguration.SweepDurationNs, (_, _, _) => [5, 5]);
        Check.That(ranges.Range(0) == new MonitorAmplitudeRange(-1, 1) && ranges.ShowPrevious(0), "manual range persists");
        Check.That(ranges.Range(1).Minimum < 5 && ranges.Range(1).Maximum > 5, "flat signals still have a finite nonzero range");
        var previous = ranges.Range(1);
        Reject(() => ranges.Advance(2 * MonitorDisplayConfiguration.SweepDurationNs, (channel, _, _) => channel == slots[1].Channel ? [20] : [double.NaN]));
        Check.That(ranges.Cycle == 1 && ranges.Range(1) == previous, "late invalid samples cannot partially update scales");
    }
    private static void LiveSessionKeepsBoundedHistoryAndProgressiveFrontier()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default());
        for (int i = 0; i < 125; i++) { session.Advance(20_000_000); }
        long before = session.FrontierNs;
        session.Advance(16_000_000);
        Check.That(session.FrontierNs - before == 16_000_000, "display progresses between 200ms block arrivals");
        while (session.SimulationTimeNs < 32_000_000_000) { session.Advance(50_000_000); }
        Check.That(session.Blocks.Count <= LocalMonitorPreviewSession.RetainedBlockCount &&
            session.FrontierNs == session.SimulationTimeNs - LocalMonitorPreviewSession.PresentationLatencyNs,
            "bounded retained history and exact acquisition/presentation lag");
        long time = session.SimulationTimeNs;
        Reject(() => session.Advance(0));
        Check.That(session.SimulationTimeNs == time, "invalid advancement does not mutate clocks");
    }
    private static void AutomaticRangeReservesFifteenPercentAndCalibration()
    {
        var ranges = new MonitorSweepRanges(MonitorDisplayConfiguration.Default());
        ranges.Advance(MonitorDisplayConfiguration.SweepDurationNs, (_, _, _) => [0, 40]);
        var gas = ranges.Range(3);
        Check.That(Math.Abs((gas.Normalize(40) - gas.Normalize(0)) - .85) < 1e-12,
            "nonflat content occupies 85 percent with symmetric headroom");
        var ecg = ranges.Range(0);
        Check.That(ecg.Minimum < -500 && ecg.Maximum > 500 &&
            Math.Abs(ecg.Normalize(500) - ecg.Normalize(-500) - .85) < 1e-12,
            "ECG auto range reserves room for the true 1mV reference");
    }
    private static void DenseContoursPreserveKnotsAndMonotonicity()
    {
        (long TimeNs, double Value)[] raw = [(0, 0), (100, 10), (200, 10), (300, 3), (400, 3), (500, 6), (600, 0)];
        var points = PreviewContour.Interpolate(raw);
        Check.That(points.Count == 25 && raw.All(points.Contains), "more display points retain every original knot and extremum");
        for (int i = 0; i < raw.Length - 1; i++)
        {
            var part = points.Where(p => p.TimeNs >= raw[i].TimeNs && p.TimeNs <= raw[i + 1].TimeNs).ToArray();
            Check.That(part.All(p => p.Value >= Math.Min(raw[i].Value, raw[i + 1].Value) && p.Value <= Math.Max(raw[i].Value, raw[i + 1].Value)), "no invented overshoot or waves on plateaus");
            for (int j = 1; j < part.Length; j++)
                Check.That((part[j].Value - part[j - 1].Value) * (raw[i + 1].Value - raw[i].Value) >= 0, "each interpolated interval is monotone");
        }
        Reject(() => PreviewContour.Interpolate([(0, double.NaN)]));
        Reject(() => PreviewContour.Interpolate([(0, 1), (0, 2)]));
        Reject(() => PreviewContour.Interpolate(raw, 0));
        Check.That(PreviewContour.Interpolate([]).Count == 0, "empty input remains empty");
    }
    private static void DenseContourPrefixesRemainLocal()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default());
        while (session.SimulationTimeNs < 8_000_000_000) { session.Advance(50_000_000); }
        for (int channel = 1; channel < 7; channel++)
        {
            var raw = session.Samples(channel, 0, 6_000_000_000).ToArray();
            var complete = PreviewContour.Interpolate(raw);
            for (int count = 20; count < raw.Length; count += 20)
            {
                var prefix = PreviewContour.Interpolate(raw.Take(count).ToArray());
                long stableEnd = raw[count - 2].TimeNs;
                Check.That(prefix.Where(p => p.TimeNs < stableEnd).SequenceEqual(complete.Where(p => p.TimeNs < stableEnd)), "all six non-ECG channels use local interpolation, not whole-curve refitting");
            }
        }
    }
    private static void IndependentSpeedsRetainPreviousScaleUntilOverwritten()
    {
        var slots = MonitorDisplayConfiguration.Default(MonitorSkin.ThreeRows).Slots.ToArray();
        slots[0] = slots[0] with { SpeedTenthsMmPerSecond = 125 };
        slots[2] = slots[2] with { SpeedTenthsMmPerSecond = 500 };
        var ranges = new MonitorSweepRanges(new(MonitorSkin.ThreeRows, slots));
        ranges.Advance(5_000_000_000, (_, from, to) => { Check.That(from == 0 && to == 5_000_000_000, "fast row owns its completed window"); return [10, 40]; });
        Check.That(ranges.RowCycle(0) == 0 && ranges.RowCycle(1) == 0 && ranges.RowCycle(2) == 1, "only fast row wraps");
        Check.That(ranges.PreviousRange(2) == slots[2].Range && ranges.ShowPrevious(2), "old trace keeps old gain when new gain is installed");
        var oldFast = ranges.Range(2);
        ranges.Advance(10_000_000_000, (_, _, _) => [20, 80]);
        Check.That(ranges.RowCycle(0) == 0 && ranges.RowCycle(1) == 1 && ranges.RowCycle(2) == 2 && ranges.PreviousRange(2) == oldFast,
            "each row keeps exactly its previous sweep scale");
        slots[0] = slots[0] with { SpeedTenthsMmPerSecond = 0 };
        Reject(() => { _ = new MonitorDisplayConfiguration(MonitorSkin.ThreeRows, slots); });
    }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "invalid display input rejected");
    }
}
