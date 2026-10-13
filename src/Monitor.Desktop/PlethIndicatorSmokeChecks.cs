// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PlethIndicatorSmokeChecks
{
    internal static void Verify()
    {
        var slots = MonitorDisplayConfiguration.Default(MonitorSkin.FourRows).Slots.ToArray();
        slots[3] = slots[1] with { SpeedTenthsMmPerSecond = 500 };
        var display = new MonitorDisplayConfiguration(MonitorSkin.FourRows, slots);
        LocalMonitorPreviewSession Create(bool stopped = false) => new(
            PhysiologyIllustrationConfiguration.Default with { CardiacActivity = stopped ? CardiacActivity.Absent : CardiacActivity.AtrialAndVentricular },
            display, enableMeasurements: true, opticalSaturationMilliPercent: 98_000);
        var session = Create();
        session.DiscardStartup();
        var view = new LiveMonitorView(new LiveMonitorTrace(session));
        Require(view.PulseLevels.All(level => level == 0), "no samples leave the pulse indicators empty");
        bool earlyPulse = false, movedWithinNumericBucket = false;
        long[] steps = [16_000_000, 33_000_000, 50_000_000, 17_000_000, 125_000_000];
        for (int frame = 0; frame < 400; frame++)
        {
            long oldBucket = session.SimulationTimeNs / 200_000_000;
            double oldLevel = view.PulseLevels[0];
            session.Advance(steps[frame % steps.Length]);
            view.Refresh();
            double level = view.PulseLevels[0];
            if (session.SimulationTimeNs < 2_000_000_000 && level > .5) { earlyPulse = true; }
            if (oldBucket == session.SimulationTimeNs / 200_000_000 && Math.Abs(level - oldLevel) > .05)
            { movedWithinNumericBucket = true; }
            Require(view.PulseLevels.All(value => double.IsFinite(value) && value >= 0 && value <= 1 && value == level),
                "duplicate PLETH rows agree despite different sweep speeds");
            if (session.SimulationTimeNs < 4_000_000_000) { continue; }
            long frontier = session.SimulationTimeNs;
            var visible = session.Samples(2, frontier - 4_000_000_000, frontier).ToArray();
            var range = new MonitorAmplitudeRange(visible.Min(sample => sample.Value), visible.Max(sample => sample.Value));
            Require(Math.Abs(level - range.Normalize(visible[^1].Value)) < 1e-9,
                "the bar follows the newest sample at the visible PLETH sweep frontier");
            view.Refresh();
            Require(view.PulseLevels[0] == level, "refreshing paused data never changes the pulse height");
        }
        Require(earlyPulse && movedWithinNumericBucket,
            "the pulse indicator starts with acquired waveforms and advances between numeric display refreshes");

        session.ScheduleSource(Create(stopped: true), 0);
        for (int frame = 0; frame < 24; frame++) { session.Advance(50_000_000); view.Refresh(); }
        double[] runoff = session.Samples(2, session.SimulationTimeNs - 200_000_000, session.SimulationTimeNs).Select(sample => sample.Value).ToArray();
        Require(runoff.Length > 1 && runoff.Zip(runoff.Skip(1), (previous, next) => next <= previous).All(falling => falling) &&
            view.PulseLevels.All(level => level == 0),
            "the ending pulse empties the bar as its physiological runoff declines");
        for (int frame = 0; frame < 200; frame++) { session.Advance(50_000_000); view.Refresh(); }
        Require(view.PulseLevels.All(level => level == 0), "a prolonged flat trace has no false pulse or invalid normalization");
        session.ScheduleSource(Create(), 0);
        bool beforeRateConfirmation = false;
        for (int frame = 0; frame < 30; frame++)
        {
            session.Advance(50_000_000);
            view.Refresh();
            if (view.PulseLevels[0] > .5 && session.BeatEvidence!.PlethStatus == WaveformMeasurementStatus.Stale)
            { beforeRateConfirmation = true; }
        }
        Require(beforeRateConfirmation, "the first returning pulse animates before a new pulse-rate peak is confirmed");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("PLETH indicator: " + message); }
    }
}
