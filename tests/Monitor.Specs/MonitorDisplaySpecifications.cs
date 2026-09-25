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
        new(nameof(ContourSimplificationBoundsErrorAndPreservesTurningPoints), ContourSimplificationBoundsErrorAndPreservesTurningPoints),
    ];
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
        Check.That(calls == 3 && ranges.Range(0).Minimum < -10 && ranges.Range(0).Maximum > 100 && !ranges.ShowPrevious(0),
            "new scale includes padding and removes old-scale history from this row");
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
        Check.That(ecg.Minimum < 0 && ecg.Maximum > 1000 &&
            Math.Abs(ecg.Normalize(1000) - ecg.Normalize(0) - .85) < 1e-12,
            "ECG auto range reserves room for the true 1mV reference");
    }
    private static void ContourSimplificationBoundsErrorAndPreservesTurningPoints()
    {
        var samples = Enumerable.Range(0, 401).Select(i => ((long)i, Math.Round(i / 10d) / 2)).ToArray();
        var copy = samples.ToArray();
        var displayed = PreviewContour.Simplify(samples, .3);
        Check.That(displayed.Count < samples.Length / 4 && samples.SequenceEqual(copy), "quantized ramp simplifies without altering samples");
        int segment = 0;
        foreach (var (time, value) in samples)
        {
            while (segment + 2 < displayed.Count && time > displayed[segment + 1].TimeNs) { segment++; }
            var a = displayed[segment]; var b = displayed[segment + 1];
            double expected = a.Value + (b.Value - a.Value) * (time - a.TimeNs) / (b.TimeNs - a.TimeNs);
            Check.That(Math.Abs(expected - value) <= .3 + 1e-10, "every raw sample remains inside the declared display error");
        }
        (long, double)[] notch = [(0, 0), (1, 10), (2, 10), (3, 3), (4, 3), (5, 6), (6, 0)];
        Check.That(PreviewContour.Simplify(notch, 1).SequenceEqual(notch), "peak and notch plateaus survive");
        Check.That(PreviewContour.Simplify(samples, 0).SequenceEqual(samples), "ECG and zero-tolerance data are unchanged");
        Reject(() => PreviewContour.Simplify([(0, double.NaN)], .3));
        Reject(() => PreviewContour.Simplify([(0, 0), (0, 1)], .3));
    }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "invalid display input rejected");
    }
}
