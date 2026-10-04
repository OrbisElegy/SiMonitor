// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Measurements;

public enum EcgRhythmEventKind { IrregularRhythm, SuspectedAtrialFibrillation }
public enum EcgRhythmTransition { Started, Ended, Interrupted }
public enum EcgRhythmInterruption
{
    None,
    SignalUnavailable,
    StreamDiscontinuity,
    InsufficientAtrialEvidence,
    InsufficientRrEvidence
}

// EvidenceFromNs is the beginning of the analysis window, not a reconstructed
// physiological onset. ConfirmedAtNs is causal and belongs to the acquired clock.
public sealed record DetectedEcgRhythmEvent(EcgRhythmEventKind Kind, EcgRhythmTransition Transition,
    long EvidenceFromNs, long ConfirmedAtNs, EcgRhythmInterruption Interruption = EcgRhythmInterruption.None);
public sealed record EcgRhythmEvidence(int RrIntervalCount, int IrregularChangesPermille,
    int RrBinCount, int AtrialWindowCount, int AtrialCoherencePermille, int AtrialRmsMicrovolts);
public sealed record EcgRhythmReading(WaveformMeasurementStatus Status, bool? IrregularRhythm,
    bool? SuspectedAtrialFibrillation, EcgRhythmEvidence? Evidence);

// Conservative single-lead teaching screen. It recognizes irregular ventricular
// activation with non-coherent atrial activity, not all forms of AF. In particular
// absent P evidence alone is insufficient. No source labels enter this analyzer.
internal sealed class EcgRhythmAnalysis
{
    internal const long WindowNs = 30_000_000_000;
    private const long PersistenceNs = 5_000_000_000;
    private const long StepNs = 4_000_000;
    private readonly int[] _samples = new int[500];
    private int _cursor, _count;
    private long _sampleTimeNs;
    private Observation[] _observations = [];
    private Episode _irregular = new();
    private Episode _af = new();
    private EcgRhythmEvidence? _evidence;
    private bool _ready;
    private bool _atrialReady;

    internal EcgRhythmAnalysis Copy()
    {
        var copy = new EcgRhythmAnalysis
        {
            _cursor = _cursor,
            _count = _count,
            _sampleTimeNs = _sampleTimeNs,
            _observations = _observations,
            _irregular = _irregular with { },
            _af = _af with { },
            _evidence = _evidence,
            _ready = _ready,
            _atrialReady = _atrialReady
        };
        _samples.CopyTo(copy._samples, 0);
        return copy;
    }

    internal void Interrupt(long timeNs, EcgRhythmInterruption reason, List<DetectedEcgRhythmEvent> events)
    {
        InterruptEpisode(_irregular, EcgRhythmEventKind.IrregularRhythm, timeNs, reason, events);
        InterruptEpisode(_af, EcgRhythmEventKind.SuspectedAtrialFibrillation, timeNs, reason, events);
        _observations = [];
        _count = 0;
        _evidence = null;
        _ready = _atrialReady = false;
    }

    internal void Sample(long timeNs, int microvolts, bool usable, List<DetectedEcgRhythmEvent> events)
    {
        _sampleTimeNs = timeNs;
        if (!usable)
        {
            Interrupt(timeNs, EcgRhythmInterruption.SignalUnavailable, events);
            return;
        }
        _samples[_cursor] = microvolts;
        _cursor = (_cursor + 1) % _samples.Length;
        _count = Math.Min(_samples.Length, _count + 1);
    }

    internal void Beat(DetectedEcgBeat beat, List<DetectedEcgRhythmEvent> events)
    {
        long? previous = _observations.LastOrDefault()?.PeakTimeNs;
        // Avoid the previous QRS/T complex in short cycles. Atrial evidence is
        // collected only where there is enough diastolic signal to inspect.
        int[]? atrial = previous is { } last && beat.PeakTimeNs - last >= 650_000_000
            ? AtrialWindow(beat.PeakTimeNs) : null;
        _observations = _observations.Append(new Observation(beat.PeakTimeNs, atrial))
            .Where(o => o.PeakTimeNs >= beat.PeakTimeNs - WindowNs - 5_000_000_000).TakeLast(180).ToArray();
        int first = Array.FindLastIndex(_observations, o => o.PeakTimeNs <= beat.PeakTimeNs - WindowNs);
        if (first < 0 || _observations.Length - first < 21)
        {
            // A slower rhythm can lose window support after analysis was ready.
            // Retain observations for relearning, but never retain its verdict
            // or let a pending confirmation run across unavailable evidence.
            InterruptEpisode(_irregular, EcgRhythmEventKind.IrregularRhythm, beat.ConfirmedAtNs,
                EcgRhythmInterruption.InsufficientRrEvidence, events);
            InterruptEpisode(_af, EcgRhythmEventKind.SuspectedAtrialFibrillation, beat.ConfirmedAtNs,
                EcgRhythmInterruption.InsufficientRrEvidence, events);
            _ready = false;
            _atrialReady = false;
            _evidence = null;
            return;
        }
        Observation[] window = _observations.Skip(first).ToArray();
        long[] rr = window.Zip(window.Skip(1), (a, b) => (b.PeakTimeNs - a.PeakTimeNs) / 1_000_000).ToArray();
        long mean = rr.Sum() / rr.Length;
        int changes = rr.Zip(rr.Skip(1), (a, b) => Math.Abs(a - b) >= Math.Max(60, mean / 10) ? 1 : 0).Sum();
        int changesPermille = changes * 1000 / (rr.Length - 1);
        long variance = rr.Sum(r => (r - mean) * (r - mean)) / rr.Length;
        bool irregular = changesPermille >= 350 && variance * 10000 >= mean * mean * 64;
        // Repeated coupling/block patterns occupy few RR bins. This gate is
        // deliberately additional to waveform evidence, never an AF label alone.
        int bins = rr.Select(r => r / 40).Distinct().Count();
        int[][] atrialWindows = window.Skip(1).Where(o => o.Atrial is not null).Select(o => o.Atrial!).ToArray();
        long energy = 0, coherentEnergy = 0, differenceEnergy = 0;
        if (atrialWindows.Length >= 8)
        {
            for (int i = 0; i < 61; i++)
            {
                long sum = 0;
                foreach (int[] values in atrialWindows)
                {
                    sum += values[i];
                    energy += (long)values[i] * values[i];
                    if (i > 0) { differenceEnergy += (long)(values[i] - values[i - 1]) * (values[i] - values[i - 1]); }
                }
                coherentEnergy += sum * sum;
            }
        }
        int coherence = energy == 0 ? 0 : (int)(coherentEnergy * 1000 / (energy * atrialWindows.Length));
        int rms = energy == 0 ? 0 : IntegerSquareRoot(energy / (61 * atrialWindows.Length));
        bool measurable = atrialWindows.Length >= 8 && rms >= 2 && differenceEnergy * 5 <= energy;
        bool organized = measurable && coherence >= 500;
        bool disorganized = measurable && coherence <= 200;
        bool? af = organized ? false : irregular && bins >= 6 && disorganized ? true : null;
        _evidence = new(rr.Length, changesPermille, bins, atrialWindows.Length, coherence, rms);
        _ready = true;
        _atrialReady = af.HasValue;
        UpdateEpisode(_irregular, EcgRhythmEventKind.IrregularRhythm, irregular, window[0].PeakTimeNs, beat.ConfirmedAtNs, events);
        if (af is { } positive)
        { UpdateEpisode(_af, EcgRhythmEventKind.SuspectedAtrialFibrillation, positive, window[0].PeakTimeNs, beat.ConfirmedAtNs, events); }
        else
        {
            _af.PendingSinceNs = null;
            _af.UnknownSinceNs ??= beat.ConfirmedAtNs;
            if (beat.ConfirmedAtNs - _af.UnknownSinceNs >= WindowNs)
            { InterruptEpisode(_af, EcgRhythmEventKind.SuspectedAtrialFibrillation, beat.ConfirmedAtNs, EcgRhythmInterruption.InsufficientAtrialEvidence, events); }
        }
    }

    internal EcgRhythmReading Read(WaveformMeasurementStatus heartRateStatus)
    {
        if (heartRateStatus != WaveformMeasurementStatus.Valid)
        { return new(heartRateStatus, null, null, null); }
        return new(_ready ? WaveformMeasurementStatus.Valid : WaveformMeasurementStatus.WarmingUp,
            _ready ? _irregular.Active : null, _ready && _atrialReady ? _af.Active : null, _evidence);
    }

    private int[]? AtrialWindow(long peakTimeNs)
    {
        long from = peakTimeNs - 300_000_000;
        if (from < _sampleTimeNs - (_count - 1) * StepNs) { return null; }
        int[] values = new int[61];
        for (int i = 0; i < values.Length; i++)
        {
            int age = checked((int)((_sampleTimeNs - from - i * StepNs) / StepNs));
            values[i] = _samples[(_cursor - 1 - age + _samples.Length) % _samples.Length];
        }
        int left = values.Take(5).Sum() / 5;
        int right = values.TakeLast(5).Sum() / 5;
        for (int i = 0; i < values.Length; i++) { values[i] -= left + (right - left) * i / 60; }
        return values;
    }

    private static int IntegerSquareRoot(long value)
    {
        int low = 0, high = 40001;
        while (low + 1 < high)
        {
            int mid = (low + high) / 2;
            if ((long)mid * mid <= value) { low = mid; }
            else { high = mid; }
        }
        return low;
    }

    private static void UpdateEpisode(Episode episode, EcgRhythmEventKind kind, bool positive,
        long evidenceFromNs, long confirmedAtNs, List<DetectedEcgRhythmEvent> events)
    {
        episode.UnknownSinceNs = null;
        if (positive == episode.Active)
        {
            episode.PendingSinceNs = null;
            return;
        }
        if (episode.PendingSinceNs is null)
        {
            episode.PendingSinceNs = confirmedAtNs;
            episode.EvidenceFromNs = evidenceFromNs;
        }
        if (confirmedAtNs - episode.PendingSinceNs < PersistenceNs) { return; }
        episode.Active = positive;
        episode.PendingSinceNs = null;
        events.Add(new(kind, positive ? EcgRhythmTransition.Started : EcgRhythmTransition.Ended,
            episode.EvidenceFromNs, confirmedAtNs));
    }

    private static void InterruptEpisode(Episode episode, EcgRhythmEventKind kind, long timeNs,
        EcgRhythmInterruption reason, List<DetectedEcgRhythmEvent> events)
    {
        if (episode.Active) { events.Add(new(kind, EcgRhythmTransition.Interrupted, episode.EvidenceFromNs, timeNs, reason)); }
        episode.Active = false;
        episode.PendingSinceNs = null;
        episode.UnknownSinceNs = null;
    }

    private sealed record Observation(long PeakTimeNs, int[]? Atrial);
    private sealed record Episode
    {
        internal bool Active;
        internal long? PendingSinceNs, UnknownSinceNs;
        internal long EvidenceFromNs;
    }
}
