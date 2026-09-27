// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

public enum MonitorBeatMode { Ecg, Pleth, Auto }
public enum MonitorBeatOrigin { None, Ecg, Pleth }
public sealed record MonitorBeatSourceChange(long TimeNs, MonitorBeatMode Mode, MonitorBeatOrigin From, MonitorBeatOrigin To);

// Local source policy. A reliable status never creates a beat event.
public sealed class MonitorBeatSource
{
    private long? _ecgSince, _plethSince, _lastTime, _lastCue;
    private MonitorBeatOrigin _lastCueSource;
    private MonitorBeatMode? _mode;
    private readonly Queue<MonitorBeatSourceChange> _changes = new();
    public MonitorBeatOrigin Current { get; private set; }
    public IReadOnlyList<MonitorBeatSourceChange> Changes => _changes.ToArray();
    public void Reset()
    {
        _ecgSince = _plethSince = _lastTime = _lastCue = null;
        _mode = null;
        Current = _lastCueSource = MonitorBeatOrigin.None;
        _changes.Clear();
    }
    public bool Update(MonitorBeatMode mode, WaveformMeasurementStatus ecg, WaveformMeasurementStatus pleth, long nowNs)
    {
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(ecg) || !Enum.IsDefined(pleth) || nowNs < 0 || nowNs < _lastTime)
        { throw new ArgumentException("BeatSource.InvalidUpdate"); }
        _lastTime = nowNs;
        _ecgSince = ecg == WaveformMeasurementStatus.Valid ? _ecgSince ?? nowNs : null;
        _plethSince = pleth == WaveformMeasurementStatus.Valid ? _plethSince ?? nowNs : null;
        bool IsReady(long? since, long duration) => since is { } start && nowNs - start >= duration;
        var next = mode switch
        {
            MonitorBeatMode.Ecg => MonitorBeatOrigin.Ecg,
            MonitorBeatMode.Pleth => MonitorBeatOrigin.Pleth,
            _ when Current == MonitorBeatOrigin.Ecg && ecg == WaveformMeasurementStatus.Valid => MonitorBeatOrigin.Ecg,
            _ when IsReady(_ecgSince, Current == MonitorBeatOrigin.Pleth ? 3_000_000_000 : 1_000_000_000) => MonitorBeatOrigin.Ecg,
            _ when IsReady(_plethSince, 1_000_000_000) => MonitorBeatOrigin.Pleth,
            _ => MonitorBeatOrigin.None
        };
        if (next == Current && mode == _mode) { return false; }
        _changes.Enqueue(new(nowNs, mode, Current, next));
        if (_changes.Count > 64) { _changes.Dequeue(); }
        Current = next;
        _mode = mode;
        return true;
    }
    public bool Accept(MonitorBeatOrigin source, long confirmedAtNs)
    {
        if (source == MonitorBeatOrigin.None || source != Current || _lastTime is not { } now ||
            confirmedAtNs < 0 || confirmedAtNs > now || now - confirmedAtNs > 250_000_000 ||
            _lastCue is { } last && (confirmedAtNs <= last || source != _lastCueSource && confirmedAtNs - last < 300_000_000))
        { return false; }
        _lastCue = confirmedAtNs;
        _lastCueSource = source;
        return true;
    }
}
