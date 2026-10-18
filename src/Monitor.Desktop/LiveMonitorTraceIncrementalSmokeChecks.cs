// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using Avalonia;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;

namespace Monitor.Desktop;

internal static class LiveMonitorTraceIncrementalSmokeChecks
{
    internal static void Verify()
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(MonitorSkin.FourRows));
        var trace = new LiveMonitorTrace(session);
        MethodInfo build = typeof(LiveMonitorTrace).GetMethod("BuildCurrentContour", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var range = new MonitorAmplitudeRange(-1200, 1200);
        const long fromNs = 4_000_000_000, durationNs = 20_000_000_000;
        (long TimeNs, double Value)[] samples = Enumerable.Range(0, 96).Select(index =>
            (fromNs + index * 8_000_000L + index % 5 * 23,
                index % 19 == 0 ? 1600d : Math.Sin(index * .17) * 450 + Math.Cos(index * .07) * 40)).ToArray();
        Point[]? previous = null;
        Point[] previousSnapshot = [];
        for (int count = 0; count <= samples.Length; count++)
        {
            var currentSamples = samples.Take(count).ToArray();
            Point[] current = VerifyFresh(currentSamples);
            if (previous is not null)
            { Require(Exact(previous, previousSnapshot), "appending samples leaves a submitted frame immutable"); }
            Require(ReferenceEquals(current, Build(currentSamples.ToArray())),
                "identical raw samples reuse normalized points");
            previous = current;
            previousSnapshot = current.ToArray();
        }

        var edited = samples.ToArray();
        edited[10].Value += 117;
        Point[] revised = VerifyFresh(edited);
        Require(!ReferenceEquals(previous, revised), "a changed historical prefix invalidates the cache");
        _ = VerifyFresh(samples.Take(27).ToArray());
        _ = VerifyFresh(samples.Take(43).ToArray());
        _ = VerifyFresh(samples, channel: 2);
        _ = VerifyFresh(samples, startNs: fromNs - 1);
        _ = VerifyFresh(samples, sweepDurationNs: durationNs / 2);
        _ = VerifyFresh(samples, amplitudeRange: new(-100, 200));
        _ = VerifyFresh(samples, row: 1);

        Point[] beforeLayout = VerifyFresh(samples);
        session.UpdateDisplay(MonitorDisplayConfiguration.Default(MonitorSkin.ThreeRows));
        Require(!ReferenceEquals(beforeLayout, VerifyFresh(samples)),
            "changing the row count retires current contour snapshots");

        // Tiny, irregular intervals can yield fewer than four subdivisions.
        // They remain supported by falling back to the complete interpolation.
        (long TimeNs, double Value)[] irregular = [(0, 0), (1, 2), (3, -2), (8, 4), (9, 1), (17, -1)];
        for (int count = 3; count <= irregular.Length; count++)
        {
            var currentSamples = irregular.Take(count).ToArray();
            Point[] current = VerifyFresh(currentSamples, startNs: 0);
            Require(!ReferenceEquals(current, Build(currentSamples.ToArray(), startNs: 0)),
                "unexpected interpolation point counts use the complete path");
        }

        var zeros = samples.Take(8).ToArray();
        zeros[2].Value = 0;
        Point[] positiveZero = VerifyFresh(zeros);
        var negativeZero = zeros.ToArray();
        negativeZero[2].Value = BitConverter.Int64BitsToDouble(long.MinValue);
        Require(!ReferenceEquals(positiveZero, VerifyFresh(negativeZero)),
            "raw prefix comparisons preserve floating-point sign bits");

        var valid = samples.Take(8).ToArray();
        Point[] lastValid = VerifyFresh(valid);
        ExpectInvalid([.. valid, valid[^1]]);
        ExpectInvalid([.. valid, (valid[^1].TimeNs + 8_000_000, double.NaN)]);
        ExpectInvalid([.. valid, (valid[^1].TimeNs + 8_000_000, double.PositiveInfinity)]);
        ExpectInvalid(Enumerable.Range(0, 10_001).Select(index => (fromNs + index * 8_000_000L, 0d)).ToArray());
        Require(ReferenceEquals(lastValid, Build(valid.ToArray())),
            "invalid appended samples do not replace the last valid snapshot");

        Point[] Build((long TimeNs, double Value)[] input, int channel = 1, long startNs = fromNs,
            long sweepDurationNs = durationNs, MonitorAmplitudeRange? amplitudeRange = null, int row = 0) =>
            (Point[])build.Invoke(trace, [row, channel, startNs, sweepDurationNs, amplitudeRange ?? range, input])!;

        Point[] VerifyFresh((long TimeNs, double Value)[] input, int channel = 1, long startNs = fromNs,
            long sweepDurationNs = durationNs, MonitorAmplitudeRange? amplitudeRange = null, int row = 0)
        {
            Point[] actual = Build(input, channel, startNs, sweepDurationNs, amplitudeRange, row);
            MonitorAmplitudeRange scale = amplitudeRange ?? range;
            Point[] expected = PreviewContour.Interpolate(input).Select(sample => new Point(
                (sample.TimeNs - startNs) / (double)sweepDurationNs, 1 - scale.Normalize(sample.Value))).ToArray();
            Require(Exact(actual, expected), "incremental normalized points equal complete interpolation bit for bit");
            return actual;
        }

        void ExpectInvalid((long TimeNs, double Value)[] input)
        {
            try { _ = Build(input); }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { return; }
            throw new InvalidOperationException("Incremental contour accepted invalid samples.");
        }
    }

    private static bool Exact(Point[] actual, Point[] expected)
    {
        if (actual.Length != expected.Length) { return false; }
        for (int index = 0; index < actual.Length; index++)
        {
            if (BitConverter.DoubleToInt64Bits(actual[index].X) != BitConverter.DoubleToInt64Bits(expected[index].X) ||
                BitConverter.DoubleToInt64Bits(actual[index].Y) != BitConverter.DoubleToInt64Bits(expected[index].Y))
            { return false; }
        }
        return true;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Incremental contour: " + message); }
    }
}
