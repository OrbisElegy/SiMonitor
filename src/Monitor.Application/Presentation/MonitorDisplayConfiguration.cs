// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public enum MonitorSkin { ThreeRows, FiveRows, SevenRows }
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
public sealed record MonitorDisplaySlot(int Channel, bool Automatic, MonitorAmplitudeRange Range);
public sealed class MonitorDisplayConfiguration
{
    public const double AutoOccupancy = .85;
    public const long SweepDurationNs = 10_000_000_000;
    public MonitorSkin Skin { get; }
    public IReadOnlyList<MonitorDisplaySlot> Slots { get; }
    public static int RowCount(MonitorSkin skin) => skin switch
    { MonitorSkin.ThreeRows => 3, MonitorSkin.FiveRows => 5, MonitorSkin.SevenRows => 7, _ => throw new ArgumentException("MonitorDisplay.InvalidSkin") };
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
        }
        Skin = skin; Slots = Array.AsReadOnly(owned);
    }
    public static MonitorDisplayConfiguration Default(MonitorSkin skin = MonitorSkin.FiveRows)
    {
        int[] channels = [0, 2, 3, 4, 1, 5, 6];
        return new(skin, channels.Take(RowCount(skin)).Select(c => new MonitorDisplaySlot(c, true, ReferenceRange(c))).ToArray());
    }
}

// Presentation-only ranges. Update once at a sweep boundary from the completed
// previous sweep, never from future buffered samples or current-frame extrema.
public sealed class MonitorSweepRanges(MonitorDisplayConfiguration configuration)
{
    private MonitorAmplitudeRange[] _ranges = configuration.Slots.Select(s => s.Range).ToArray();
    private bool[] _showPrevious = configuration.Slots.Select(_ => true).ToArray();
    public long Cycle { get; private set; }
    public MonitorAmplitudeRange Range(int slot) => _ranges[slot];
    public bool ShowPrevious(int slot) => _showPrevious[slot];
    public void Advance(long frontierNs, Func<int, long, long, IEnumerable<double>> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        long cycle = frontierNs / MonitorDisplayConfiguration.SweepDurationNs;
        if (frontierNs < 0 || cycle < Cycle) { throw new ArgumentException("MonitorDisplay.TimeRegression"); }
        if (cycle == Cycle) { return; }
        long end = cycle * MonitorDisplayConfiguration.SweepDurationNs;
        long start = end - MonitorDisplayConfiguration.SweepDurationNs;
        var next = _ranges.ToArray();
        for (int slot = 0; slot < next.Length; slot++)
        {
            if (!configuration.Slots[slot].Automatic) { continue; }
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
            { minimum = Math.Min(minimum, 0); maximum = Math.Max(maximum, 1000); }
            double padding = Math.Max((maximum - minimum) * (1 / MonitorDisplayConfiguration.AutoOccupancy - 1) / 2, referenceSpan * .01);
            next[slot] = new(minimum - padding, maximum + padding);
            next[slot].Validate();
        }
        _showPrevious = next.Select((r, i) => r == _ranges[i]).ToArray();
        _ranges = next; Cycle = cycle;
    }
}
