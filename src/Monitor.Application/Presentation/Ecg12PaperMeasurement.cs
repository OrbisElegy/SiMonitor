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
    Ecg12PaperCursor? Start, Ecg12PaperCursor? End, EcgManualMeasurementResult? Result)
{
    // The sample under the pointer, where the next point would be placed.
    public Ecg12PaperCursor? Hover { get; init; }
    // While the second point is being placed: the result if it were placed at Hover.
    public EcgManualMeasurementResult? HoverResult { get; init; }
}

// Manual point measurement on one frozen paper record. A hover point follows the
// pointer along the trace; the first placed point fixes the lead and the second
// completes the measurement, and a further placement starts a new one. Points
// snap to acquired samples; nothing is detected. Results are ordered by time
// (later point minus earlier point), whichever point was placed first.
public sealed class Ecg12PaperMeasurement
{
    private readonly Ecg12PaperLayout _layout;
    private readonly IReadOnlyList<WaveformEnvelope> _blocks;
    private readonly Func<int, Guid> _channelOfLead;
    private readonly bool _allowAuxiliaryRate;
    private readonly Dictionary<int, Ecg12PaperCursor[]> _samples = [];
    private Ecg12PaperRegion? _region;
    private int _anchorIndex;
    private int? _endIndex;
    private Ecg12PaperRegion? _hoverRegion;
    private int _hoverIndex;

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
    private bool Placing => _region is not null && _endIndex is null;

    // Disabling or locking measurement withdraws any visible points.
    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy)
    {
        if (!Enum.IsDefined(policy)) { throw new ArgumentOutOfRangeException(nameof(policy)); }
        Policy = policy;
        if (!CanMeasure) { Clear(); }
    }

    // While placing the second point the hover stays on the first point's lead,
    // clamped to its samples; otherwise it follows whichever lead is under the pointer.
    public bool Hover(double x, double y)
    {
        if (!CanMeasure) { return false; }
        if (Placing)
        {
            var region = _region!;
            _hoverRegion = region;
            _hoverIndex = Nearest(Samples(region), region.TimeAt(Math.Clamp(x, region.Left, region.Right)));
            return true;
        }
        if (_layout.HitTest(x, y) is not { } hit || Samples(hit) is not { Length: > 0 } samples)
        {
            bool had = _hoverRegion is not null;
            _hoverRegion = null;
            return had;
        }
        _hoverRegion = hit;
        _hoverIndex = Nearest(samples, hit.TimeAt(x));
        return true;
    }

    public void EndHover() => _hoverRegion = null;

    public bool Place(double x, double y)
    {
        if (!CanMeasure || !Hover(x, y) || _hoverRegion is not { } region) { return false; }
        if (Placing) { _endIndex = _hoverIndex; }
        else
        {
            _region = region;
            _anchorIndex = _hoverIndex;
            _endIndex = null;
        }
        return true;
    }

    // Moves the latest placed point by whole samples within its lead.
    public bool Nudge(int samples)
    {
        if (!CanMeasure || _region is not { } region) { return false; }
        int last = Samples(region).Length - 1;
        if (_endIndex is { } end) { _endIndex = Math.Clamp(end + samples, 0, last); }
        else { _anchorIndex = Math.Clamp(_anchorIndex + samples, 0, last); }
        return true;
    }

    public void Clear()
    {
        _region = null;
        _endIndex = null;
        _hoverRegion = null;
    }

    public Ecg12PaperMeasurementDisplay Display
    {
        get
        {
            string reason = Policy switch
            {
                SystemViewCommandAssessmentPolicy.Disabled => "Ecg12Measurement.Disabled",
                SystemViewCommandAssessmentPolicy.CourseLocked => "Ecg12Measurement.CourseLocked",
                _ => _region is null ? "Ecg12Measurement.Idle" : _endIndex is null ? "Ecg12Measurement.Placing" : "Ecg12Measurement.Ready",
            };
            var hover = _hoverRegion is { } hoverRegion ? Samples(hoverRegion)[_hoverIndex] : null;
            if (_region is not { } region) { return new(reason, null, null, null, null) { Hover = hover }; }
            var samples = Samples(region);
            if (_endIndex is not { } endIndex)
            {
                var anchor = samples[_anchorIndex];
                var preview = hover is null ? null : anchor.TimeNs <= hover.TimeNs
                    ? EcgManualMeasurement.Calculate(anchor.Value, hover.Value, _allowAuxiliaryRate)
                    : EcgManualMeasurement.Calculate(hover.Value, anchor.Value, _allowAuxiliaryRate);
                return new(reason, region, anchor, null, null) { Hover = hover, HoverResult = preview };
            }
            var start = samples[Math.Min(_anchorIndex, endIndex)];
            var end = samples[Math.Max(_anchorIndex, endIndex)];
            return new(reason, region, start, end, EcgManualMeasurement.Calculate(start.Value, end.Value, _allowAuxiliaryRate)) { Hover = hover };
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
