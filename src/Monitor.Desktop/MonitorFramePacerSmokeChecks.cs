// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

internal static class MonitorFramePacerSmokeChecks
{
    internal static void Verify()
    {
        var pacer = new MonitorFramePacer(1_000_000_000);
        Require(pacer.Start(0).TotalMilliseconds == 17, "first deadline rounds up to a whole millisecond");
        Require(pacer.CompleteFrame(19_000_000).TotalMilliseconds == 15,
            "frame work is subtracted from the next deadline");
        Require(pacer.CompleteFrame(36_000_000).TotalMilliseconds == 14,
            "rounding does not accumulate into the following frame");

        long timestamp = 0;
        TimeSpan delay = pacer.Start(timestamp);
        for (int frame = 0; frame < 600; frame++)
        {
            timestamp += delay.Ticks * 100;
            delay = pacer.CompleteFrame(timestamp);
        }
        Require(timestamp == 10_000_000_000, "sixty deadlines per second retain fractional timestamp ticks");

        timestamp = 0;
        delay = pacer.Start(timestamp);
        for (int frame = 0; frame < 600; frame++)
        {
            timestamp += delay.Ticks * 100 + 2_000_000;
            delay = pacer.CompleteFrame(timestamp);
        }
        Require(timestamp == 10_002_000_000, "two milliseconds of frame work do not accumulate in the cadence");

        pacer.Start(0);
        Require(pacer.CompleteFrame(37_000_000).TotalMilliseconds == 1,
            "an over-budget frame schedules the next opportunity without a full-period wait");
        Require(pacer.CompleteFrame(58_000_000).TotalMilliseconds == 1,
            "repeated twenty-millisecond work does not fall onto thirty-hertz deadlines");
        Require(pacer.CompleteFrame(61_000_000).TotalMilliseconds == 15,
            "ordinary pacing resumes from the rebased deadline after overload");

        Require(pacer.CompleteFrame(2_000_000_000).TotalMilliseconds == 1,
            "a long suspension drops the missed frame backlog");
        Require(pacer.Start(5_000_000_000).TotalMilliseconds == 17,
            "resume resets the cadence independently of the old deadline");
        Require(pacer.CompleteFrame(5_019_000_000).TotalMilliseconds == 15,
            "resumed work uses the new start timestamp");

        foreach (int framesPerSecond in new[] { 30, 45 })
        {
            var selected = new MonitorFramePacer(1_000_000_000, framesPerSecond);
            timestamp = 0;
            delay = selected.Start(timestamp);
            for (int frame = 0; frame < framesPerSecond * 10; frame++)
            {
                timestamp += delay.Ticks * 100 + 2_000_000;
                delay = selected.CompleteFrame(timestamp);
            }
            Require(timestamp == 10_002_000_000, "the selected cadence compensates frame work without drift");
        }
        foreach (int invalid in new[] { 0, 121 })
        {
            try
            {
                _ = new MonitorFramePacer(1_000_000_000, invalid);
                throw new InvalidOperationException("Invalid frame rate was accepted.");
            }
            catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "framesPerSecond") { }
        }
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition) { throw new InvalidOperationException($"Monitor frame pacing failed: {contract}."); }
    }
}
