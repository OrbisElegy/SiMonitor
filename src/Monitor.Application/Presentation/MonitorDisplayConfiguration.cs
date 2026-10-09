// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public enum MonitorSkin { ThreeRows, FiveRows, SevenRows, FourRows }
public sealed record MonitorAmplitudeRange(double Minimum, double Maximum)
{
    public double Normalize(double value) => Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1);
    public void Validate()
    {
        if (!double.IsFinite(Minimum) || !double.IsFinite(Maximum) || Minimum >= Maximum ||
            Minimum < -10_000_000 || Maximum > 10_000_000)
        { throw new ArgumentException("MonitorDisplay.InvalidRange"); }
    }
}
public sealed record MonitorDisplaySlot(int Channel, bool Automatic, MonitorAmplitudeRange Range, int SpeedTenthsMmPerSecond = 250)
{
    public long DurationNs => MonitorDisplayConfiguration.SweepDurationNs * 250 / SpeedTenthsMmPerSecond;
}
public sealed class MonitorDisplayConfiguration
{
    public const double AutoOccupancy = .85;
    public const long SweepDurationNs = 10_000_000_000;
    public MonitorSkin Skin { get; }
    public IReadOnlyList<MonitorDisplaySlot> Slots { get; }
    public static int RowCount(MonitorSkin skin) => skin switch
    { MonitorSkin.ThreeRows => 3, MonitorSkin.FiveRows => 5, MonitorSkin.SevenRows => 7, MonitorSkin.FourRows => 4, _ => throw new ArgumentException("MonitorDisplay.InvalidSkin") };
    public static MonitorAmplitudeRange ReferenceRange(int channel) => channel switch
    {
        0 => new(-1200, 1500),
        1 => new(-1200, 1200),
        2 => new(0, 1600),
        3 => new(0, 160),
        4 => new(0, 80),
        5 => new(0, 40),
        6 => new(-5, 15),
        _ => throw new ArgumentException("MonitorDisplay.InvalidChannel")
    };
    public MonitorDisplayConfiguration(MonitorSkin skin, IReadOnlyList<MonitorDisplaySlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count != RowCount(skin)) { throw new ArgumentException("MonitorDisplay.InvalidSlotCount"); }
        var owned = slots.ToArray();
        foreach (var slot in owned)
        {
            if (slot is null || slot.Range is null) { throw new ArgumentException("MonitorDisplay.InvalidSlot"); }
            _ = ReferenceRange(slot.Channel); slot.Range.Validate();
            if (slot.SpeedTenthsMmPerSecond is not (125 or 250 or 500)) { throw new ArgumentException("MonitorDisplay.InvalidSpeed"); }
        }
        Skin = skin; Slots = Array.AsReadOnly(owned);
    }
    public static MonitorDisplayConfiguration Default(MonitorSkin skin = MonitorSkin.FiveRows)
    {
        int[] channels = skin == MonitorSkin.FourRows ? [0, 2, 1, 4] : [0, 2, 3, 4, 1, 5, 6];
        return new(skin, channels.Take(RowCount(skin)).Select(c => new MonitorDisplaySlot(c, true, ReferenceRange(c))).ToArray());
    }
}

// Presentation-only ranges. Update once at a sweep boundary from the completed
// previous sweep, never from future buffered samples or current-frame extrema.
public sealed class MonitorSweepRanges(MonitorDisplayConfiguration configuration)
{
    private MonitorAmplitudeRange[] _ranges = configuration.Slots.Select(s => s.Range).ToArray();
    private MonitorAmplitudeRange[] _previous = configuration.Slots.Select(s => s.Range).ToArray();
    private long[] _cycles = new long[configuration.Slots.Count];
    private long[] _frontiers = new long[configuration.Slots.Count];
    public long Cycle { get; private set; }
    public MonitorAmplitudeRange Range(int slot) => _ranges[slot];
    public bool ShowPrevious(int slot) => _cycles[slot] > 0;
    public long RowCycle(int slot) => _cycles[slot];
    public MonitorAmplitudeRange PreviousRange(int slot) => _previous[slot];
    public void Advance(long frontierNs, Func<int, long, long, IEnumerable<double>> samples) => Advance(_ => frontierNs, samples);

    public void Advance(Func<int, long> channelFrontierNs, Func<int, long, long, IEnumerable<double>> samples)
    {
        ArgumentNullException.ThrowIfNull(channelFrontierNs);
        ArgumentNullException.ThrowIfNull(samples);
        long[] frontiers = configuration.Slots.Select(s => channelFrontierNs(s.Channel)).ToArray();
        if (frontiers.Where((frontier, index) => frontier < _frontiers[index]).Any())
        { throw new ArgumentException("MonitorDisplay.TimeRegression"); }
        long[] nextCycles = configuration.Slots.Select((s, index) => frontiers[index] / s.DurationNs).ToArray();
        var next = _ranges.ToArray(); var previous = _previous.ToArray();
        for (int slot = 0; slot < next.Length; slot++)
        {
            if (nextCycles[slot] == _cycles[slot]) { continue; }
            previous[slot] = _ranges[slot];
            if (!configuration.Slots[slot].Automatic) { continue; }
            long end = nextCycles[slot] * configuration.Slots[slot].DurationNs;
            long start = end - configuration.Slots[slot].DurationNs;
            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            int count = 0;
            foreach (double value in samples(configuration.Slots[slot].Channel, start, end))
            {
                if (!double.IsFinite(value) || ++count > 100_000) { throw new ArgumentException("MonitorDisplay.InvalidSamples"); }
                minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value);
            }
            if (count == 0) { continue; }
            double referenceSpan = configuration.Slots[slot].Range.Maximum - configuration.Slots[slot].Range.Minimum;
            if (configuration.Slots[slot].Channel == 0)
            { minimum = Math.Min(minimum, -500); maximum = Math.Max(maximum, 500); }
            double padding = Math.Max((maximum - minimum) * (1 / MonitorDisplayConfiguration.AutoOccupancy - 1) / 2, referenceSpan * .01);
            next[slot] = new(minimum - padding, maximum + padding);
            next[slot].Validate();
        }
        if (!nextCycles.SequenceEqual(_cycles)) { Cycle++; }
        _ranges = next; _previous = previous; _cycles = nextCycles; _frontiers = frontiers;
    }
}
