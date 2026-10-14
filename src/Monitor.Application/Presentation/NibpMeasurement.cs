// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public enum NibpPhase { Disabled, Idle, Inflating, Deflating }
public enum NibpEventKind { Started, Completed, Cancelled }
public sealed record NibpMeasurementEvent(long TimeNs, NibpEventKind Kind, ManualNibp? Reading);

// Local teaching cuff cycle, driven only by the session clock. The configured
// pressure is a target for the next measurement, never an instantaneous reading.
public sealed class NibpMeasurement
{
    public const long InflationDurationNs = 6_000_000_000;
    public const long MeasurementDurationNs = 15_000_000_000;
    public const long ReadingLifetimeNs = 900_000_000_000;
    public static IReadOnlyList<int> AutoIntervalsMinutes { get; } = Array.AsReadOnly(new[] { 1, 2, 3, 5, 10, 15 });
    private ManualNibp? _target, _measuringTarget, _reading;
    private long _timeNs;
    private long? _startedAtNs;
    public bool Enabled => _target is not null;
    public bool Measuring => _startedAtNs.HasValue;
    public bool AutoEnabled { get; private set; }
    public int AutoIntervalMinutes { get; private set; } = 5;
    public long? NextMeasurementAtNs { get; private set; }
    public long? MeasuredAtNs { get; private set; }
    public ManualNibp? Reading => Enabled && !Measuring && MeasuredAtNs is { } measured &&
        _timeNs - measured < ReadingLifetimeNs ? _reading : null;
    public NibpPhase Phase => !Enabled ? NibpPhase.Disabled : _startedAtNs is not { } start ? NibpPhase.Idle :
        _timeNs - start < InflationDurationNs ? NibpPhase.Inflating : NibpPhase.Deflating;
    public int PhaseRemainingMilliseconds => _startedAtNs is { } start
        ? (int)Math.Max(0, (start + (Phase == NibpPhase.Inflating ? InflationDurationNs : MeasurementDurationNs) - _timeNs + 999_999) / 1_000_000)
        : 0;
    public event Action<NibpMeasurementEvent>? Changed;

    public void Advance(long simTimeNs, ManualNibp? target)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(simTimeNs, _timeNs);
        _timeNs = simTimeNs;
        _target = target;
        if (!Enabled)
        {
            Stop();
            AutoEnabled = false;
            NextMeasurementAtNs = null;
            MeasuredAtNs = null;
            _reading = null;
            return;
        }
        if (_startedAtNs is { } start && simTimeNs >= start + MeasurementDurationNs)
        {
            _reading = _measuringTarget;
            MeasuredAtNs = start + MeasurementDurationNs;
            _startedAtNs = null;
            _measuringTarget = null;
            Changed?.Invoke(new(MeasuredAtNs.Value, NibpEventKind.Completed, _reading));
        }
        // Never replay missed cycles after a large advance; take one new measurement now.
        if (AutoEnabled && !Measuring && NextMeasurementAtNs is { } next && simTimeNs >= next) { Start(); }
    }

    public bool Start()
    {
        if (!Enabled || Measuring) { return false; }
        long? next = AutoEnabled ? checked(_timeNs + AutoIntervalMinutes * 60_000_000_000L) : null;
        _ = checked(_timeNs + MeasurementDurationNs);
        _measuringTarget = _target;
        _startedAtNs = _timeNs;
        NextMeasurementAtNs = next;
        Changed?.Invoke(new(_timeNs, NibpEventKind.Started, null));
        return true;
    }

    public void Stop()
    {
        if (!Measuring) { return; }
        _startedAtNs = null;
        _measuringTarget = null;
        Changed?.Invoke(new(_timeNs, NibpEventKind.Cancelled, null));
    }

    public void ConfigureAuto(bool enabled, int intervalMinutes)
    {
        if (!AutoIntervalsMinutes.Contains(intervalMinutes)) { throw new ArgumentOutOfRangeException(nameof(intervalMinutes)); }
        bool starting = enabled && Enabled && !AutoEnabled;
        bool changed = AutoIntervalMinutes != intervalMinutes;
        long? next = enabled && Enabled && (starting || changed) ? checked(_timeNs + intervalMinutes * 60_000_000_000L) : NextMeasurementAtNs;
        AutoIntervalMinutes = intervalMinutes;
        AutoEnabled = enabled && Enabled;
        if (!AutoEnabled) { NextMeasurementAtNs = null; }
        else { NextMeasurementAtNs = next; }
        if (starting && !Measuring) { Start(); }
    }
}
