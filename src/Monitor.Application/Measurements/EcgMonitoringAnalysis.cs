// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Measurements;

// Acquired single-lead evidence only. The IFU specifies event rules, not the
// proprietary classifier; the morphology and repolarization screens here are
// deliberately bounded teaching heuristics, with unavailable results explicit.
internal sealed class EcgMonitoringAnalysis(EcgMonitoringSettings settings)
{
    private const int TemplateBeatCount = 15;
    private const long StepNs = 4_000_000;
    private const long MinuteNs = 60_000_000_000;
    private int[] _samples = new int[500];
    private int _cursor, _count, _learningBeats;
    private long _timeNs;
    private long? _validFromNs, _lastPeakNs, _vfFromNs;
    private Observation[] _beats = [];
    private Shape[] _learning = [];
    private Shape? _template;
    private RecoveryBeat[] _recovery = [];
    private EcgBeatMorphology? _lastMorphology;
    private int _ventricularRun, _svtRun;
    private long _runStartNs, _svtStartNs;
    private bool _runHasLeftBoundary;
    private long? _ronTCandidateNs, _previousRonTNs;
    private long _ronTAverageNs;
    private long? _lastPulseNs, _pulseEvidenceFromNs, _averageRrNs;
    private long[] _episodeStarts = new long[25];
    private EcgMonitoringConditions _active;
    private PendingRepolarization? _pending;
    private PendingBeat? _pendingBeat;
    private int? _st, _qt, _qtc, _baseline = settings.QtcBaselineMilliseconds;
    private long? _stTimeNs, _qtTimeNs, _baselineFromNs;
    private long? _stHighFromNs, _stLowFromNs, _qtHighFromNs, _deltaHighFromNs;

    internal bool HasNarrowTemplate => _template is { Width: < 100 };

    internal EcgMonitoringAnalysis Copy() => new(settings)
    {
        _lastPulseNs = _lastPulseNs,
        _pulseEvidenceFromNs = _pulseEvidenceFromNs,
        _averageRrNs = _averageRrNs,
        _samples = (int[])_samples.Clone(),
        _cursor = _cursor,
        _count = _count,
        _learningBeats = _learningBeats,
        _timeNs = _timeNs,
        _validFromNs = _validFromNs,
        _lastPeakNs = _lastPeakNs,
        _vfFromNs = _vfFromNs,
        _beats = _beats,
        _learning = _learning,
        _template = _template,
        _recovery = _recovery,
        _lastMorphology = _lastMorphology,
        _ventricularRun = _ventricularRun,
        _svtRun = _svtRun,
        _runStartNs = _runStartNs,
        _svtStartNs = _svtStartNs,
        _runHasLeftBoundary = _runHasLeftBoundary,
        _ronTCandidateNs = _ronTCandidateNs,
        _previousRonTNs = _previousRonTNs,
        _ronTAverageNs = _ronTAverageNs,
        _episodeStarts = (long[])_episodeStarts.Clone(),
        _active = _active,
        _pending = _pending,
        _pendingBeat = _pendingBeat,
        _st = _st,
        _qt = _qt,
        _qtc = _qtc,
        _baseline = _baseline,
        _stTimeNs = _stTimeNs,
        _qtTimeNs = _qtTimeNs,
        _baselineFromNs = _baselineFromNs,
        _stHighFromNs = _stHighFromNs,
        _stLowFromNs = _stLowFromNs,
        _qtHighFromNs = _qtHighFromNs,
        _deltaHighFromNs = _deltaHighFromNs
    };

    internal void Interrupt(long timeNs, EcgRhythmInterruption reason, List<DetectedEcgMonitoringEvent> events)
    {
        foreach (var condition in Enum.GetValues<EcgMonitoringConditions>())
        {
            if (condition != EcgMonitoringConditions.None && (_active & condition) != 0)
            { events.Add(new(condition, EcgMonitoringTransition.Interrupted, _episodeStarts[Index(condition)], timeNs, reason)); }
        }
        _active = EcgMonitoringConditions.None;
        _validFromNs = _lastPeakNs = _vfFromNs = _lastPulseNs = _pulseEvidenceFromNs = _averageRrNs = null;
        _beats = [];
        _learning = [];
        _recovery = [];
        _template = null;
        _count = _learningBeats = _ventricularRun = _svtRun = 0;
        _lastMorphology = null;
        _pendingBeat = null;
        _ronTCandidateNs = _previousRonTNs = null;
        InvalidateRepolarization(timeNs, events, reason);
    }

    internal void Sample(long timeNs, int microvolts, bool usable,
        DetectedEcgBeat? beat, long qrsStartNs, long qrsEndNs, bool candidateActive, bool pacingAvailable, bool pacingPulse,
        List<DetectedEcgMonitoringEvent> events)
    {
        _timeNs = timeNs;
        if (!usable)
        {
            Interrupt(timeNs, EcgRhythmInterruption.SignalUnavailable, events);
            return;
        }
        if (pacingAvailable) { _pulseEvidenceFromNs ??= timeNs; }
        else
        {
            _pulseEvidenceFromNs = _lastPulseNs = null;
            Set(EcgMonitoringConditions.PacerNotCaptured, false, timeNs, timeNs, events, EcgRhythmInterruption.SignalUnavailable);
            Set(EcgMonitoringConditions.PacerNotPacing, false, timeNs, timeNs, events, EcgRhythmInterruption.SignalUnavailable);
        }
        if (pacingPulse) { _lastPulseNs = timeNs; }
        _validFromNs ??= timeNs;
        _samples[_cursor] = microvolts;
        _cursor = (_cursor + 1) % _samples.Length;
        _count = Math.Min(_samples.Length, _count + 1);
        if (beat is not null)
        {
            // Finish the previous beat only if its T end was already observed;
            // an overlapping QRS is never silently used as a T wave.
            FinishRepolarization(beat.PeakTimeNs - 80_000_000, events);
            _lastPeakNs = beat.PeakTimeNs;
            _pendingBeat = new(beat, qrsStartNs, qrsEndNs);
        }
        // QRS cue timing stays unchanged. Morphology waits for the complete
        // contour instead of mistaking the fast detector's steep lobe for QRS.
        if (_pendingBeat is { } candidate && timeNs >= candidate.Beat.PeakTimeNs + 160_000_000)
        {
            Beat(candidate.Beat with { ConfirmedAtNs = timeNs }, candidate.StartNs, candidate.EndNs, events);
            _pendingBeat = null;
        }
        if (_pending is { } pending && timeNs - pending.PeakNs >= 700_000_000)
        { FinishRepolarization(timeNs, events); }
        if (_stTimeNs is { } stTime && timeNs - stTime > 3_000_000_000 ||
            _qtTimeNs is { } qtTime && timeNs - qtTime > 3_000_000_000)
        { InvalidateRepolarization(timeNs, events, EcgRhythmInterruption.InsufficientRrEvidence); }

        long silenceNs = timeNs - (_lastPeakNs ?? _validFromNs.Value);
        // Frequency evidence is evaluated on complete one-second acquired windows.
        if ((timeNs - _validFromNs.Value) % 1_000_000_000 == 996_000_000 && _count >= 250)
        {
            bool fibrillatory = silenceNs >= 996_000_000 && FibrillatoryWindow();
            if (fibrillatory) { _vfFromNs ??= timeNs - 996_000_000; }
            else { _vfFromNs = null; }
        }
        bool vf = _vfFromNs is { } vfFrom && timeNs - vfFrom >= 4_000_000_000 && silenceNs >= 4_000_000_000;
        Set(EcgMonitoringConditions.SuspectedVentricularFibrillation, vf, _vfFromNs ?? timeNs, timeNs, events);
        // Wait for an in-flight QRS confirmation, but at most the detector's
        // bounded candidate duration. Flatline still alarms at the threshold.
        bool AwaitingQrs(EcgMonitoringConditions condition) => candidateActive && (_active & condition) == 0;
        bool asystole = !AwaitingQrs(EcgMonitoringConditions.Asystole) && !vf && silenceNs > settings.AsystoleMilliseconds * 1_000_000L && QuietWindow();
        Set(EcgMonitoringConditions.Asystole, asystole, timeNs - silenceNs, timeNs, events);
        bool learned = _template is not null;
        Set(EcgMonitoringConditions.Pause, learned && !AwaitingQrs(EcgMonitoringConditions.Pause) && !vf && !asystole && silenceNs > settings.PauseMilliseconds * 1_000_000L,
            timeNs - silenceNs, timeNs, events);
        long? average = _averageRrNs;
        long missedNs = average is { } rr ? (rr >= 500_000_000 ? rr * 7 / 4 : 1_000_000_000) : long.MaxValue;
        Set(EcgMonitoringConditions.MissedBeat, learned && !settings.PacedMode && !AwaitingQrs(EcgMonitoringConditions.MissedBeat) && !asystole && !vf && silenceNs > missedNs,
            timeNs - silenceNs, timeNs, events);
        bool pacingFailure = settings.PacedMode && average is { } pacingRr && _lastPeakNs is { } pacedLast &&
            _pulseEvidenceFromNs is { } pulseFrom && pulseFrom <= pacedLast && !candidateActive && !asystole && !vf && silenceNs > pacingRr * 7 / 4;
        bool pulseSinceBeat = _lastPulseNs is { } pace && pace > (_lastPeakNs ?? timeNs);
        Set(EcgMonitoringConditions.PacerNotCaptured, pacingFailure && pulseSinceBeat, timeNs - silenceNs, timeNs, events);
        Set(EcgMonitoringConditions.PacerNotPacing, pacingFailure && !pulseSinceBeat, timeNs - silenceNs, timeNs, events);
        if (_lastPeakNs is { } last && timeNs - last > 3_000_000_000)
        {
            ClearBeatConditions(timeNs, events);
        }
        Set(EcgMonitoringConditions.PvcsPerMinuteHigh, learned && PvcCount(timeNs) > settings.PvcsPerMinuteLimit,
            Math.Max(_validFromNs.Value, timeNs - MinuteNs), timeNs, events);
    }

    private void Beat(DetectedEcgBeat beat, long startNs, long endNs, List<DetectedEcgMonitoringEvent> events)
    {
        long? previous = _beats.LastOrDefault()?.PeakNs;
        long intervalNs = previous is { } p ? beat.PeakTimeNs - p : 0;
        long? average = _averageRrNs;
        var shape = ExtractShape(beat.PeakTimeNs);
        int width = shape?.Width ?? (int)((endNs - startNs) / 1_000_000);
        EcgBeatLabel label = EcgBeatLabel.Unknown;
        int difference = _template is null || shape is null ? 0 : Difference(shape, _template);
        if (_learningBeats < TemplateBeatCount)
        {
            label = EcgBeatLabel.Learning;
            if (shape is not null)
            {
                _learning = _learning.Append(shape).ToArray();
                _learningBeats++;
                if (_learningBeats == TemplateBeatCount)
                {
                    // Medoid of acquired complexes, independent of input labels.
                    _template = _learning.MinBy(s => _learning.Sum(other => Difference(s, other) + Math.Abs(s.Width - other.Width) * 10));
                    _learning = [];
                }
            }
        }
        else if (shape is not null && _template is { } template)
        {
            bool matches = difference < 450 && Math.Abs(width - template.Width) < 32;
            bool premature = average is { } avg && intervalNs > 0 && intervalNs * 5 < avg * 4;
            bool continuesSvt = _svtRun > 0 && intervalNs > 0 && 60_000_000_000L > intervalNs * settings.SvtHeartRate;
            label = matches ? (premature || continuesSvt ? EcgBeatLabel.SupraventricularPremature : EcgBeatLabel.Normal)
                : width >= 100 && (width >= template.Width + 24 || premature) ? EcgBeatLabel.Ventricular : EcgBeatLabel.Unknown;
        }
        if (settings.PacedMode && _learningBeats >= TemplateBeatCount)
        {
            if (_pulseEvidenceFromNs is null) { label = EcgBeatLabel.Unknown; }
            else if (_lastPulseNs is { } pulseNs && pulseNs <= beat.PeakTimeNs && beat.PeakTimeNs - pulseNs <= 200_000_000)
            { label = EcgBeatLabel.Paced; }
        }
        if (RecoverNormalReference(label, shape, intervalNs))
        {
            label = EcgBeatLabel.Normal;
            difference = Difference(shape!, _template!);
        }
        var morphology = new EcgBeatMorphology(label, width, difference);
        _lastMorphology = morphology;
        _lastPeakNs = beat.PeakTimeNs;
        _vfFromNs = null;
        if (_ronTCandidateNs is { } candidate && intervalNs > _ronTAverageNs * 5 / 4)
        { Occur(EcgMonitoringConditions.RonTPvc, candidate, beat.ConfirmedAtNs, events); }
        _ronTCandidateNs = null;
        if (label == EcgBeatLabel.Ventricular && average is { } mean && mean > 600_000_000 &&
            intervalNs > 0 && intervalNs < 333_000_000 && intervalNs * 3 < mean)
        {
            if (_previousRonTNs is { } ronT && beat.PeakTimeNs - ronT <= 5 * MinuteNs)
            { Occur(EcgMonitoringConditions.RonTPvc, ronT, beat.ConfirmedAtNs, events); }
            _previousRonTNs = _ronTCandidateNs = beat.PeakTimeNs;
            _ronTAverageNs = mean;
        }
        if (label == EcgBeatLabel.Ventricular)
        {
            if (_ventricularRun == 0)
            {
                _runStartNs = beat.PeakTimeNs;
                _runHasLeftBoundary = _beats.LastOrDefault()?.Label is EcgBeatLabel.Normal or EcgBeatLabel.SupraventricularPremature;
            }
            _ventricularRun++;
        }
        else
        {
            bool bounded = label is EcgBeatLabel.Normal or EcgBeatLabel.SupraventricularPremature;
            if (bounded && _runHasLeftBoundary && _ventricularRun == 2)
            { Occur(EcgMonitoringConditions.PairPvcs, _runStartNs, beat.ConfirmedAtNs, events); }
            if (bounded && _ventricularRun >= 3 && _ventricularRun < settings.VtachRunBeats && previous is { } last &&
                60_000_000_000L * (_ventricularRun - 1) > (last - _runStartNs) * settings.VtachHeartRate)
            { Occur(EcgMonitoringConditions.NonSustainedVentricularTachycardia, _runStartNs, beat.ConfirmedAtNs, events); }
            _ventricularRun = 0;
        }
        if (label == EcgBeatLabel.SupraventricularPremature)
        {
            if (_svtRun == 0) { _svtStartNs = beat.PeakTimeNs; }
            _svtRun++;
        }
        else { _svtRun = 0; }
        _beats = _beats.Append(new(beat.PeakTimeNs, label, shape)).TakeLast(300).ToArray();
        _averageRrNs = AverageRrNs() ?? _averageRrNs;
        bool fastV = _ventricularRun >= 2 && 60_000_000_000L * (_ventricularRun - 1) >
            (beat.PeakTimeNs - _runStartNs) * settings.VtachHeartRate;
        Set(EcgMonitoringConditions.VentricularTachycardia, fastV && _ventricularRun >= settings.VtachRunBeats, _runStartNs, beat.ConfirmedAtNs, events);
        Set(EcgMonitoringConditions.VentricularRhythm, !fastV && _ventricularRun > settings.VentricularRhythmRunBeats, _runStartNs, beat.ConfirmedAtNs, events);
        Set(EcgMonitoringConditions.RunPvcs, !fastV && _ventricularRun > 2 && _ventricularRun <= settings.VentricularRhythmRunBeats, _runStartNs, beat.ConfirmedAtNs, events);
        Set(EcgMonitoringConditions.SupraventricularTachycardia, _svtRun >= settings.SvtRunBeats &&
            60_000_000_000L * (_svtRun - 1) > (beat.PeakTimeNs - _svtStartNs) * settings.SvtHeartRate,
            _svtStartNs, beat.ConfirmedAtNs, events);
        bool bigeminy = Pattern([0, 1, 0, 1, 0]) || (_active & EcgMonitoringConditions.VentricularBigeminy) != 0 && Pattern([1, 0, 1, 0, 1]);
        bool trigeminy = Pattern([0, 0, 1, 0, 0, 1, 0, 0]) || (_active & EcgMonitoringConditions.VentricularTrigeminy) != 0 &&
            (Pattern([0, 1, 0, 0, 1, 0, 0, 1]) || Pattern([1, 0, 0, 1, 0, 0, 1, 0]));
        Set(EcgMonitoringConditions.VentricularBigeminy, bigeminy,
            _beats[Math.Max(0, _beats.Length - 5)].PeakNs, beat.ConfirmedAtNs, events);
        Set(EcgMonitoringConditions.VentricularTrigeminy, trigeminy,
            _beats[Math.Max(0, _beats.Length - 8)].PeakNs, beat.ConfirmedAtNs, events);
        Shape[] ventricular = _beats.TakeLast(60).Where(b => b.Label == EcgBeatLabel.Ventricular && b.Shape is not null).Select(b => b.Shape!).ToArray();
        bool multiform = ventricular.Any(a => ventricular.Any(b => Difference(a, b) >= 650 || Math.Abs(a.Width - b.Width) >= 40));
        Set(EcgMonitoringConditions.MultiformPvcs, multiform, _beats[Math.Max(0, _beats.Length - 60)].PeakNs, beat.ConfirmedAtNs, events);
        UpdateRate(beat.ConfirmedAtNs, events);
        if (label == EcgBeatLabel.Normal && width < 120 && intervalNs >= 400_000_000 && intervalNs <= 2_000_000_000)
        { _pending = new(beat.PeakTimeNs, startNs - 20_000_000, endNs - 20_000_000, intervalNs); }
        else { InvalidateRepolarization(beat.ConfirmedAtNs, events, EcgRhythmInterruption.InsufficientRrEvidence); }
    }

    // An initial dominant abnormal shape or an obsolete normal RR must not
    // permanently exclude later measurable beats. Confirm a fresh reference
    // only from consecutive, consistent narrow beats below the SVT threshold;
    // never relearn merely because ST/QT is unavailable or a timer expired.
    private bool RecoverNormalReference(EcgBeatLabel label, Shape? shape, long intervalNs)
    {
        if (settings.PacedMode || label is not (EcgBeatLabel.Unknown or EcgBeatLabel.SupraventricularPremature) ||
            shape is not { Width: < 100 } || intervalNs is < 400_000_000 or > 2_000_000_000 ||
            60_000_000_000L > intervalNs * settings.SvtHeartRate)
        {
            _recovery = [];
            return false;
        }
        if (_recovery.Any(b => Difference(shape, b.Shape) >= 250 || Math.Abs(shape.Width - b.Shape.Width) >= 20 ||
            Math.Abs(intervalNs - b.RrNs) * 10 > b.RrNs))
        { _recovery = []; }
        _recovery = _recovery.Append(new(shape, intervalNs)).ToArray();
        if (_recovery.Length < TemplateBeatCount) { return false; }
        // Candidate arrays are replaced, never mutated, so checkpoints retain
        // an independent, bounded history even halfway through confirmation.
        _template = _recovery.MinBy(b => _recovery.Sum(other => Difference(b.Shape, other.Shape) +
            Math.Abs(b.Shape.Width - other.Shape.Width) * 10))!.Shape;
        _averageRrNs = _recovery.Sum(b => b.RrNs) / _recovery.Length;
        _recovery = [];
        return true;
    }

    private void UpdateRate(long timeNs, List<DetectedEcgMonitoringEvent> events)
    {
        Observation[] window = _beats.Where(b => b.PeakNs >= timeNs - EcgHeartRateMeasurement.RateWindowNs).TakeLast(9).ToArray();
        if (window.Length < 2) { return; }
        long elapsed = window[^1].PeakNs - window[0].PeakNs;
        long numerator = 60_000_000_000L * (window.Length - 1);
        Set(EcgMonitoringConditions.ExtremeBradycardia, numerator < elapsed * settings.ExtremeLowHeartRate, window[0].PeakNs, timeNs, events);
        Set(EcgMonitoringConditions.ExtremeTachycardia, numerator > elapsed * settings.ExtremeHighHeartRate, window[0].PeakNs, timeNs, events);
        Set(EcgMonitoringConditions.HeartRateLow, numerator < elapsed * settings.LowHeartRate && numerator >= elapsed * settings.ExtremeLowHeartRate, window[0].PeakNs, timeNs, events);
        Set(EcgMonitoringConditions.HeartRateHigh, numerator > elapsed * settings.HighHeartRate && numerator <= elapsed * settings.ExtremeHighHeartRate, window[0].PeakNs, timeNs, events);
    }

    internal EcgMonitoringReading Read(WaveformMeasurementStatus status, long timeNs)
    {
        if (status is WaveformMeasurementStatus.NoData or WaveformMeasurementStatus.PoorSignal)
        { return EcgMonitoringReading.NoData with { Status = status, Repolarization = new(status, null, status, null, null, null) }; }
        bool stValid = _st is not null && _stTimeNs is { } stTime && timeNs - stTime <= 3_000_000_000;
        bool qtValid = _qt is not null && _qtTimeNs is { } qtTime && timeNs - qtTime <= 3_000_000_000;
        return new(WaveformMeasurementStatus.Valid, _template is null, _active,
            _template is null ? null : PvcCount(timeNs), _lastMorphology,
            new(stValid ? WaveformMeasurementStatus.Valid : WaveformMeasurementStatus.Uncountable, stValid ? _st : null,
                qtValid ? WaveformMeasurementStatus.Valid : WaveformMeasurementStatus.Uncountable,
                qtValid ? _qt : null, qtValid ? _qtc : null, qtValid ? _qtc - _baseline : null))
        { PacingEvidenceAvailable = _pulseEvidenceFromNs is not null };
    }

    private bool Pattern(int[] pattern)
    {
        if (_beats.Length < pattern.Length) { return false; }
        return _beats.TakeLast(pattern.Length).Select((b, i) => pattern[i] == 1
            ? b.Label == EcgBeatLabel.Ventricular : b.Label == EcgBeatLabel.Normal).All(v => v);
    }

    private long? AverageRrNs()
    {
        var normal = _beats.TakeLast(9).ToArray();
        long[] intervals = normal.Zip(normal.Skip(1), (a, b) => (a, b))
            .Where(p => p.a.Label is EcgBeatLabel.Normal or EcgBeatLabel.Learning or EcgBeatLabel.Paced &&
                p.b.Label is EcgBeatLabel.Normal or EcgBeatLabel.Learning or EcgBeatLabel.Paced)
            .Select(p => p.b.PeakNs - p.a.PeakNs).ToArray();
        return intervals.Length < 2 ? null : intervals.Sum() / intervals.Length;
    }

    private int PvcCount(long timeNs) => _beats.Count(b => b.PeakNs > timeNs - MinuteNs && b.Label == EcgBeatLabel.Ventricular);

    private Shape? ExtractShape(long peakNs)
    {
        long startNs = peakNs - 120_000_000;
        if (startNs < _timeNs - (_count - 1) * StepNs) { return null; }
        int[] values = Enumerable.Range(0, 61).Select(i => ValueAt(startNs + i * StepNs)).ToArray();
        int[] edges = values.Take(3).Concat(values.TakeLast(3)).Order().ToArray();
        int baseline = (edges[2] + edges[3]) / 2;
        values = values.Select(v => v - baseline).ToArray();
        int magnitude = values.Max(v => Math.Abs(v));
        if (magnitude < 150) { return null; }
        int threshold = Math.Max(50, magnitude / 5);
        int first = Array.FindIndex(values, v => Math.Abs(v) > threshold);
        int last = Array.FindLastIndex(values, v => Math.Abs(v) > threshold);
        int width = (last - first + 1) * 4;
        return new(width, values.Select(v => v * 1000 / magnitude).ToArray());
    }

    private static int Difference(Shape a, Shape b) => a.Values.Zip(b.Values, (x, y) => Math.Abs(x - y)).Sum() / a.Values.Length;
    private int ValueAt(long timeNs)
    {
        int age = (int)((_timeNs - timeNs) / StepNs);
        return _samples[(_cursor - 1 - age + _samples.Length * 2) % _samples.Length];
    }

    private bool QuietWindow()
    {
        if (_count < 250) { return false; }
        int minimum = int.MaxValue, maximum = int.MinValue;
        for (int i = 0; i < 250; i++)
        {
            int value = ValueAt(_timeNs - i * StepNs);
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        return maximum - minimum < 100;
    }

    private bool FibrillatoryWindow()
    {
        int[] values = Enumerable.Range(0, 250).Select(i => ValueAt(_timeNs - i * StepNs)).ToArray();
        int mean = values.Sum() / values.Length;
        int span = values.Max() - values.Min();
        int crossings = values.Zip(values.Skip(1), (a, b) => a < mean && b >= mean ? 1 : 0).Sum();
        long energy = values.Sum(v => (long)(v - mean) * (v - mean));
        long noise = values.Zip(values.Skip(1), (a, b) => (long)(a - b) * (a - b)).Sum();
        return span >= 100 && crossings is >= 2 and <= 10 && noise * 8 < energy;
    }

    private void ClearBeatConditions(long timeNs, List<DetectedEcgMonitoringEvent> events)
    {
        EcgMonitoringConditions[] conditions = [EcgMonitoringConditions.VentricularTachycardia,
            EcgMonitoringConditions.VentricularRhythm, EcgMonitoringConditions.RunPvcs,
            EcgMonitoringConditions.SupraventricularTachycardia, EcgMonitoringConditions.VentricularBigeminy,
            EcgMonitoringConditions.VentricularTrigeminy, EcgMonitoringConditions.MultiformPvcs,
            EcgMonitoringConditions.HeartRateHigh, EcgMonitoringConditions.HeartRateLow,
            EcgMonitoringConditions.ExtremeBradycardia, EcgMonitoringConditions.ExtremeTachycardia];
        foreach (var condition in conditions)
        { Set(condition, false, timeNs, timeNs, events, EcgRhythmInterruption.InsufficientRrEvidence); }
        _ventricularRun = _svtRun = 0;
    }

    private void Set(EcgMonitoringConditions condition, bool positive, long fromNs, long timeNs,
        List<DetectedEcgMonitoringEvent> events, EcgRhythmInterruption interruption = EcgRhythmInterruption.None)
    {
        bool wasActive = (_active & condition) != 0;
        if (wasActive == positive) { return; }
        if (positive)
        {
            _active |= condition;
            _episodeStarts[Index(condition)] = fromNs;
        }
        else { _active &= ~condition; }
        events.Add(new(condition, positive ? EcgMonitoringTransition.Started : interruption == EcgRhythmInterruption.None
            ? EcgMonitoringTransition.Ended : EcgMonitoringTransition.Interrupted,
            _episodeStarts[Index(condition)], timeNs, interruption));
    }

    // Completed, bounded patterns are occurrence events, never latched states.
    private static void Occur(EcgMonitoringConditions condition, long fromNs, long timeNs, List<DetectedEcgMonitoringEvent> events)
    {
        events.Add(new(condition, EcgMonitoringTransition.Occurred, fromNs, timeNs));
    }
    private static int Index(EcgMonitoringConditions condition) => System.Numerics.BitOperations.TrailingZeroCount((ulong)condition);
    private sealed record Shape(int Width, int[] Values);
    private sealed record RecoveryBeat(Shape Shape, long RrNs);
    private sealed record Observation(long PeakNs, EcgBeatLabel Label, Shape? Shape);
    private sealed record PendingBeat(DetectedEcgBeat Beat, long StartNs, long EndNs);
    private sealed record PendingRepolarization(long PeakNs, long StartNs, long EndNs, long RrNs);

    private void FinishRepolarization(long availableUntilNs, List<DetectedEcgMonitoringEvent> events)
    {
        if (_pending is not { } pending) { return; }
        _pending = null;
        long baselineStart = pending.StartNs - 60_000_000;
        long stPoint = pending.EndNs + settings.StOffsetMilliseconds * 1_000_000L;
        long end = Math.Min(availableUntilNs, Math.Min(pending.StartNs + 650_000_000, pending.StartNs + pending.RrNs - 200_000_000));
        if (baselineStart < _timeNs - (_count - 1) * StepNs || end <= stPoint + 80_000_000)
        {
            InvalidateRepolarization(_timeNs, events, EcgRhythmInterruption.InsufficientRrEvidence);
            return;
        }
        int[] baselineSamples = Enumerable.Range(0, 10).Select(i => ValueAt(baselineStart + i * StepNs)).ToArray();
        if (baselineSamples.Max() - baselineSamples.Min() > 50)
        {
            InvalidateRepolarization(_timeNs, events, EcgRhythmInterruption.InsufficientAtrialEvidence);
            return;
        }
        int baseline = baselineSamples.Sum() / baselineSamples.Length;
        _st = Enumerable.Range(-2, 5).Sum(i => ValueAt(stPoint + i * StepNs) - baseline) / 5;
        _stTimeNs = _timeNs;
        Persist(EcgMonitoringConditions.StHigh, _st > settings.StHighMicrovolts, ref _stHighFromNs, MinuteNs, events);
        Persist(EcgMonitoringConditions.StLow, _st < settings.StLowMicrovolts, ref _stLowFromNs, MinuteNs, events);
        long searchStart = pending.EndNs + 80_000_000;
        int length = (int)((end - searchStart) / StepNs) + 1;
        int[] t = Enumerable.Range(0, length).Select(i => Math.Abs(ValueAt(searchStart + i * StepNs) - baseline)).ToArray();
        int peak = Array.IndexOf(t, t.Max());
        int threshold = Math.Max(20, t[peak] / 10);
        int last = Array.FindLastIndex(t, value => value > threshold);
        bool measurable = t[peak] >= 50 && peak > 0 && last >= peak && last + 6 < t.Length &&
            !t.Skip(peak).Take(last - peak).Zip(t.Skip(peak + 1), (a, b) => b - a).Any(d => d > 80);
        if (measurable)
        {
            _qt = (int)((searchStart + (last + 1) * StepNs - pending.StartNs) / 1_000_000);
            _qtc = CorrectQt(_qt.Value, pending.RrNs / 1_000_000, settings.QtCorrection);
            _qtTimeNs = _timeNs;
            _baselineFromNs ??= _timeNs;
            if (_baseline is null && _timeNs - _baselineFromNs >= 5 * MinuteNs) { _baseline = _qtc; }
            Persist(EcgMonitoringConditions.QtcHigh, _qtc > settings.QtcHighMilliseconds, ref _qtHighFromNs, 5 * MinuteNs, events);
            Persist(EcgMonitoringConditions.DeltaQtcHigh, _baseline is not null && _qtc - _baseline > settings.DeltaQtcHighMilliseconds,
                ref _deltaHighFromNs, 5 * MinuteNs, events);
        }
        else { InvalidateQt(_timeNs, events, EcgRhythmInterruption.InsufficientRrEvidence); }
    }

    private static int CorrectQt(int qtMilliseconds, long rrMilliseconds, EcgQtCorrectionMethod method)
    {
        int power = method == EcgQtCorrectionMethod.Bazett ? 2 : 3;
        // Compare powers in integer arithmetic; midpoint implements nearest ms.
        System.Numerics.BigInteger target = System.Numerics.BigInteger.Pow(qtMilliseconds * 2, power) * 1000;
        int low = 0, high = 3000;
        while (low + 1 < high)
        {
            int middle = (low + high) / 2;
            if (System.Numerics.BigInteger.Pow(middle * 2, power) * rrMilliseconds <= target) { low = middle; }
            else { high = middle; }
        }
        return System.Numerics.BigInteger.Pow(low * 2 + 1, power) * rrMilliseconds <= target ? low + 1 : low;
    }

    private void Persist(EcgMonitoringConditions condition, bool positive, ref long? sinceNs, long delayNs,
        List<DetectedEcgMonitoringEvent> events)
    {
        if (positive) { sinceNs ??= _timeNs; }
        else { sinceNs = null; }
        Set(condition, sinceNs is { } since && _timeNs - since > delayNs, sinceNs ?? _timeNs, _timeNs, events);
    }

    private void InvalidateRepolarization(long timeNs, List<DetectedEcgMonitoringEvent> events, EcgRhythmInterruption reason)
    {
        _pending = null;
        _st = null;
        _stTimeNs = _stHighFromNs = _stLowFromNs = null;
        Set(EcgMonitoringConditions.StHigh, false, timeNs, timeNs, events, reason);
        Set(EcgMonitoringConditions.StLow, false, timeNs, timeNs, events, reason);
        InvalidateQt(timeNs, events, reason);
    }

    private void InvalidateQt(long timeNs, List<DetectedEcgMonitoringEvent> events, EcgRhythmInterruption reason)
    {
        _qt = _qtc = null;
        _qtTimeNs = _baselineFromNs = _qtHighFromNs = _deltaHighFromNs = null;
        Set(EcgMonitoringConditions.QtcHigh, false, timeNs, timeNs, events, reason);
        Set(EcgMonitoringConditions.DeltaQtcHigh, false, timeNs, timeNs, events, reason);
    }
}
