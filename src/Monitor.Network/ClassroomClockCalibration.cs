// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Network;

public sealed record ClassroomClockEstimate(int SampleCount, long ClientReferenceNs, long HostOffsetNs, double DriftPpm,
    long RoundTripP95Ns, long JitterP99Ns, long UncertaintyNs, long RecommendedPlayoutDelayNs, bool RequiresResynchronization)
{
    public long HostTimeAt(long clientMonotonicNs) => checked(clientMonotonicNs + HostOffsetNs +
        (long)Math.Round(((double)clientMonotonicNs - ClientReferenceNs) * DriftPpm / 1_000_000));
}

public sealed class ClassroomClockCalibration
{
    public const int InitialSampleCount = 16;
    public const int WindowSize = 32;
    private readonly Queue<ClassroomClockSample> _samples = new();
    private readonly long _minimumDelayNs;
    private readonly long _maximumDelayNs;

    public ClassroomClockCalibration(long minimumDelayNs = 250_000_000, long maximumDelayNs = 1_500_000_000)
    {
        if (minimumDelayNs < 100_000_000 || minimumDelayNs > maximumDelayNs || maximumDelayNs > 3_000_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDelayNs));
        }
        _minimumDelayNs = minimumDelayNs;
        _maximumDelayNs = maximumDelayNs;
        Estimate = new(0, 0, 0, 0, 0, 0, 0, Math.Clamp(500_000_000L, _minimumDelayNs, _maximumDelayNs), false);
    }

    public ClassroomClockEstimate Estimate { get; private set; }

    public ClassroomClockEstimate Add(ClassroomClockSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.ClientSendNs < 0 || sample.HostReceiveNs < 0 || sample.HostSendNs < sample.HostReceiveNs ||
            sample.ClientReceiveNs < sample.ClientSendNs || sample.RoundTripNs < 0 ||
            (_samples.Count > 0 && sample.ClientSendNs < _samples.Last().ClientSendNs))
        {
            throw new ArgumentException("Invalid or unordered four-timestamp sample.", nameof(sample));
        }
        if (_samples.Count == WindowSize) { _samples.Dequeue(); }
        _samples.Enqueue(sample);

        // Fit the low-RTT half with a median of pairwise slopes; long windows distinguish drift from queue noise.
        ClassroomClockSample[] sorted = [.. _samples.OrderBy(value => value.RoundTripNs)];
        ClassroomClockSample[] selected = [.. sorted.Take(Math.Max(3, (sorted.Length + 1) / 2)).OrderBy(Midpoint)];
        long reference = Midpoint(sample);
        List<double> slopes = [];
        for (int right = 1; right < selected.Length; right++)
        {
            for (int left = 0; left < right; left++)
            {
                long span = Midpoint(selected[right]) - Midpoint(selected[left]);
                if (span >= 250_000_000)
                {
                    slopes.Add(((double)selected[right].HostOffsetNs - selected[left].HostOffsetNs) / span);
                }
            }
        }
        double fittedSlope = slopes.Count == 0 ? 0 : Median(slopes);
        double slope = Math.Clamp(fittedSlope, -0.001, 0.001);
        long offset = checked((long)Math.Round(Median(selected.Select(x => x.HostOffsetNs + slope * (reference - Midpoint(x))))));
        long rtt = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1].RoundTripNs;
        double[] residuals = [.. _samples.Select(x => Math.Abs(x.HostOffsetNs - (offset + slope * (Midpoint(x) - reference)))).Order()];
        long jitter = checked((long)Math.Ceiling(residuals[(int)Math.Ceiling(residuals.Length * 0.99) - 1]));
        double proposed = 4.0 * jitter + rtt / 2.0 + 100_000_000;
        long delay = _samples.Count < InitialSampleCount ? Math.Clamp(500_000_000, _minimumDelayNs, _maximumDelayNs)
            : (long)Math.Clamp(Math.Ceiling(proposed), _minimumDelayNs, _maximumDelayNs);
        long uncertainty = (long)Math.Min((double)long.MaxValue / 2, jitter + selected[0].RoundTripNs / 2.0);
        Estimate = new(_samples.Count, reference, offset, slope * 1_000_000, rtt, jitter, uncertainty, delay,
            proposed > _maximumDelayNs || Math.Abs(fittedSlope) > 0.001);
        return Estimate;
    }

    // The caller stores this decision in the authoritative timebase when starting the cohort.
    public static long SelectCohortDelayNs(IEnumerable<ClassroomClockEstimate> estimates, long? fixedDelayNs = null)
    {
        ArgumentNullException.ThrowIfNull(estimates);
        if (fixedDelayNs is < 100_000_000 or > 3_000_000_000) { throw new ArgumentOutOfRangeException(nameof(fixedDelayNs)); }
        return fixedDelayNs ?? estimates.Where(estimate => !estimate.RequiresResynchronization)
            .Select(estimate => Math.Clamp(estimate.RecommendedPlayoutDelayNs, 100_000_000, 3_000_000_000)).DefaultIfEmpty(500_000_000).Max();
    }

    private static long Midpoint(ClassroomClockSample sample) =>
        sample.ClientSendNs + (sample.ClientReceiveNs - sample.ClientSendNs) / 2;

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }
}
