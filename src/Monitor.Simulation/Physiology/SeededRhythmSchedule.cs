// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Finite, immutable teaching sequences. Preparation consumes independent named
// streams; indexed lookup, acquisition and checkpoint recovery consume none.
public sealed record SeededRhythmSchedule
{
    public string SeedHex { get; }
    public IReadOnlyList<DeterministicStreamState> PreparedStates { get; }
    public static SeededRhythmSchedule Default { get; } = new(new string('0', 63) + "1");
    private readonly long[] _afJitter = new long[256];
    private readonly long[] _flutter = new long[240];
    private readonly long[] _arrest = new long[256];
    private readonly long[] _arrhythmia = new long[256];
    private readonly long _arrestDurationNs;
    private readonly long _flutterDurationNs;
    private readonly int[] _pausesBefore = new int[257];
    public long PauseDurationNs { get; }
    public int ConductionPercent { get; }

    public SeededRhythmSchedule(string seedHex, long pauseDurationNs = 2_000_000_000, int conductionPercent = 33)
    {
        if (pauseDurationNs is < 1_200_000_000 or > 10_000_000_000) { throw new ArgumentOutOfRangeException(nameof(pauseDurationNs)); }
        if (conductionPercent is < 10 or > 50) { throw new ArgumentOutOfRangeException(nameof(conductionPercent)); }
        PauseDurationNs = pauseDurationNs;
        ConductionPercent = conductionPercent;
        using var factory = DeterministicStreamFactory.FromLowercaseHex(seedHex);
        SeedHex = seedHex;
        var af = factory.CreateStream("physiology.rhythm.atrial-fibrillation");
        var flutter = factory.CreateStream("physiology.rhythm.variable-flutter");
        var arrest = factory.CreateStream("physiology.rhythm.sinus-arrest");
        var arrhythmia = factory.CreateStream("physiology.rhythm.sinus-arrhythmia");
        for (int i = 1; i < _afJitter.Length; i++) { _afJitter[i] = (long)af.UniformBelow(361) * 1_000_000; }
        // Fixed total atrial opportunities gives a reproducible target fraction.
        // The two-cycle minimum preserves the authored QRS support bound.
        int total = (int)Math.Round(_flutter.Length * 100m / conductionPercent);
        int[] ratios = Enumerable.Range(0, _flutter.Length).Select(i => total / _flutter.Length + (i < total % _flutter.Length ? 1 : 0)).ToArray();
        for (int i = 0; i < ratios.Length * 4; i++)
        {
            int donor = (int)flutter.UniformBelow((ulong)ratios.Length);
            int receiver = (int)flutter.UniformBelow((ulong)ratios.Length);
            if (ratios[donor] > 2 && ratios[receiver] < 12)
            {
                ratios[donor]--;
                ratios[receiver]++;
            }
        }
        for (int i = ratios.Length - 1; i > 0; i--)
        {
            int j = (int)flutter.UniformBelow((ulong)i + 1);
            (ratios[i], ratios[j]) = (ratios[j], ratios[i]);
        }
        long flutterCursorNs = 0, arrestCursorNs = 0, arrhythmiaCursorNs = 0;
        for (int i = 0; i < _flutter.Length; i++)
        {
            _flutter[i] = flutterCursorNs;
            flutterCursorNs += ratios[i] * 200_000_000L;
        }
        _flutterDurationNs = flutterCursorNs;
        for (int group = 0; group < 64; group++)
        {
            int pauseSlot = (int)arrest.UniformBelow(4);
            long pauseNs = PauseDurationNs;
            long excursionNs = (100 + (long)arrhythmia.UniformBelow(101)) * 1_000_000;
            int sign = arrhythmia.UniformBelow(2) == 0 ? -1 : 1;
            for (int slot = 0; slot < 4; slot++)
            {
                int index = group * 4 + slot;
                _arrest[index] = arrestCursorNs;
                _pausesBefore[index + 1] = _pausesBefore[index] + (slot == pauseSlot ? 1 : 0);
                arrestCursorNs += slot == pauseSlot ? pauseNs : 800_000_000;
                _arrhythmia[index] = arrhythmiaCursorNs;
                arrhythmiaCursorNs += 800_000_000 + (slot is 0 or 3 ? excursionNs : -excursionNs) * sign;
            }
        }
        _arrestDurationNs = arrestCursorNs;
        PreparedStates = Array.AsReadOnly(new[] { af.CaptureState(), flutter.CaptureState(), arrest.CaptureState(), arrhythmia.CaptureState() });
    }

    public static bool Supports(AvConductionPattern pattern) => AtrialFibrillationReference.IsPattern(pattern) || pattern is
        AvConductionPattern.VariableAtrialFlutterIllustration or AvConductionPattern.SinusArrestIllustration or AvConductionPattern.SinusArrhythmiaIllustration;

    // Map the normal PP clock while keeping every selected pause duration fixed.
    internal Int128 MapArrest(SeededCardiacRate rate, Int128 timeNs)
    {
        Int128 group = timeNs >= 0 ? timeNs / _arrestDurationNs : (timeNs + 1) / _arrestDurationNs - 1;
        long phase = (long)(timeNs - group * _arrestDurationNs);
        int index = Array.BinarySearch(_arrest, phase);
        if (index < 0) { index = ~index - 1; }
        Int128 pauses = group * 64 + _pausesBefore[index];
        Int128 normal = group * 192 + index - _pausesBefore[index];
        Int128 rateGroup = normal >= 0 ? normal / rate.Slots.Length : (normal + 1) / rate.Slots.Length - 1;
        int slot = (int)(normal - rateGroup * rate.Slots.Length);
        Int128 start = rateGroup * rate.PeriodNs * rate.Slots.Length + rate.Slots[slot] + pauses * PauseDurationNs;
        long elapsed = phase - _arrest[index];
        return start + (_pausesBefore[index + 1] > _pausesBefore[index] ? elapsed :
            (Int128)elapsed * rate.FollowingIntervalNs((ulong)slot) / 800_000_000);
    }

    internal long AfJitterNs(ulong index) => _afJitter[index % (ulong)_afJitter.Length];

    private (long[] Slots, long DurationNs) Sequence(AvConductionPattern pattern) => pattern switch
    {
        AvConductionPattern.VariableAtrialFlutterIllustration => (_flutter, _flutterDurationNs),
        AvConductionPattern.SinusArrestIllustration => (_arrest, _arrestDurationNs),
        AvConductionPattern.SinusArrhythmiaIllustration => (_arrhythmia, 204_800_000_000),
        _ => throw new ArgumentException("SeededRhythm.UnsupportedPattern", nameof(pattern))
    };

    internal Int128 CycleStartNs(AvConductionPattern pattern, ulong index)
    {
        var (slots, durationNs) = Sequence(pattern);
        return (Int128)(index / (ulong)slots.Length) * durationNs + slots[index % (ulong)slots.Length];
    }

    internal void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind, long offsetNs,
        long inclusiveSimTimeNs, Int128 exclusiveSimTimeNs, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        var (slots, durationNs) = Sequence(plan.ConductionPattern);
        IndexedCardiacSchedule.Visit(plan, kind, offsetNs, inclusiveSimTimeNs, exclusiveSimTimeNs,
            maximumEvents, visitor, durationNs, slots, cancellationToken);
    }
}
