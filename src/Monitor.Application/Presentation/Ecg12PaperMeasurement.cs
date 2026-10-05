// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Application.Presentation;

// A caliper end snapped to an acquired sample. Paper coordinates locate its drawing.
public sealed record Ecg12PaperCursor(long TimeNs, long NumeratorMicrovolts, uint Denominator, double X, double Y)
{
    public EcgManualCursor Value => new(TimeNs, NumeratorMicrovolts, Denominator);
}

public sealed record Ecg12PaperMeasurementDisplay(string ReasonCode, Ecg12PaperRegion? Region,
    Ecg12PaperCursor? Start, Ecg12PaperCursor? End, EcgManualMeasurementResult? Result);

// Manual calipers on one frozen paper record. Both ends stay on one lead and
// snap to acquired samples; nothing is detected. Results are ordered by time
// (later end minus earlier end), whichever end the user placed first.
public sealed class Ecg12PaperMeasurement
{
    private readonly Ecg12PaperLayout _layout;
    private readonly IReadOnlyList<WaveformEnvelope> _blocks;
    private readonly Func<int, Guid> _channelOfLead;
    private readonly bool _allowAuxiliaryRate;
    private readonly Dictionary<int, Ecg12PaperCursor[]> _samples = [];
    private Ecg12PaperRegion? _region;
    private int _anchorIndex;
    private int _activeIndex;

    public Ecg12PaperMeasurement(Ecg12PaperLayout layout, IReadOnlyList<WaveformEnvelope> blocks, Func<int, Guid> channelOfLead,
        SystemViewCommandAssessmentPolicy policy, bool allowAuxiliaryRate = true)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(channelOfLead);
        _layout = layout;
        _blocks = blocks;
        _channelOfLead = channelOfLead;
        _allowAuxiliaryRate = allowAuxiliaryRate;
        UpdatePolicy(policy);
    }

    public Ecg12PaperLayout Layout => _layout;
    public SystemViewCommandAssessmentPolicy Policy { get; private set; }
    public bool CanMeasure => Policy == SystemViewCommandAssessmentPolicy.Enabled;

    // Disabling or locking measurement withdraws any visible calipers.
    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy)
    {
        if (!Enum.IsDefined(policy)) { throw new ArgumentOutOfRangeException(nameof(policy)); }
        Policy = policy;
        if (!CanMeasure) { Clear(); }
    }

    public bool Begin(double x, double y)
    {
        if (!CanMeasure || _layout.HitTest(x, y) is not { } region) { return false; }
        var samples = Samples(region);
        if (samples.Length == 0) { return false; }
        _region = region;
        _anchorIndex = Nearest(samples, region.TimeAt(x));
        _activeIndex = _anchorIndex;
        return true;
    }

    // Keeps the moving end on the starting lead, clamped to that lead's samples.
    public bool Extend(double x)
    {
        if (!CanMeasure || _region is not { } region) { return false; }
        _activeIndex = Nearest(Samples(region), region.TimeAt(Math.Clamp(x, region.Left, region.Right)));
        return true;
    }

    public bool Nudge(int samples)
    {
        if (!CanMeasure || _region is not { } region) { return false; }
        _activeIndex = Math.Clamp(_activeIndex + samples, 0, Samples(region).Length - 1);
        return true;
    }

    public void Clear() => _region = null;

    public Ecg12PaperMeasurementDisplay Display
    {
        get
        {
            string reason = Policy switch
            {
                SystemViewCommandAssessmentPolicy.Disabled => "Ecg12Measurement.Disabled",
                SystemViewCommandAssessmentPolicy.CourseLocked => "Ecg12Measurement.CourseLocked",
                _ => _region is null ? "Ecg12Measurement.Idle" : "Ecg12Measurement.Ready",
            };
            if (_region is not { } region) { return new(reason, null, null, null, null); }
            var samples = Samples(region);
            var start = samples[Math.Min(_anchorIndex, _activeIndex)];
            var end = samples[Math.Max(_anchorIndex, _activeIndex)];
            return new(reason, region, start, end, EcgManualMeasurement.Calculate(start.Value, end.Value, _allowAuxiliaryRate));
        }
    }

    // First sample at or after timeNs, or its predecessor when that is at least as close.
    private static int Nearest(Ecg12PaperCursor[] samples, long timeNs)
    {
        int low = 0;
        int high = samples.Length - 1;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (samples[middle].TimeNs < timeNs) { low = middle + 1; }
            else { high = middle; }
        }
        if (low > 0 && timeNs - samples[low - 1].TimeNs <= samples[low].TimeNs - timeNs) { return low - 1; }
        return low;
    }

    // Samples drawn inside the region, with exact physical microvolts from the plane scale and offset.
    private Ecg12PaperCursor[] Samples(Ecg12PaperRegion region)
    {
        if (_samples.TryGetValue(region.Lead, out var cached)) { return cached; }
        Guid channel = _channelOfLead(region.SourceLead);
        var samples = new List<Ecg12PaperCursor>();
        foreach (var block in _blocks)
        {
            foreach (var plane in block.Planes)
            {
                if (plane.ChannelId != channel) { continue; }
                long step = checked(1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator);
                ulong denominator = (ulong)plane.ScaleDenominator * plane.OffsetDenominator;
                if (denominator is 0 or > uint.MaxValue) { throw new ArgumentException("Ecg12Measurement.UnsupportedScale", nameof(region)); }
                for (int i = 0; i < plane.Samples.Count; i++)
                {
                    long time = block.StartSimTimeNs + i * step;
                    if (time < region.StartNs || time >= region.EndExclusiveNs) { continue; }
                    long numerator = checked((long)plane.Samples[i] * plane.ScaleNumerator * plane.OffsetDenominator +
                        (long)plane.OffsetNumerator * plane.ScaleDenominator);
                    samples.Add(new(time, numerator, (uint)denominator, region.XAt(time), region.YAt(numerator, (uint)denominator)));
                }
            }
        }
        var ordered = samples.OrderBy(sample => sample.TimeNs).ToArray();
        _samples[region.Lead] = ordered;
        return ordered;
    }
}
