// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

public sealed record PulsePressureReading(WaveformMeasurementStatus Status, int? SystolicCentiMmHg,
    int? DiastolicCentiMmHg, long? LastPeakTimeNs);

// Bounded teaching detector for pulsatile125Hz pressure, independent of ECG.
// Hysteresis rejects small notch rebounds; median-of-three rejects one-sample
// spikes. This is not WABP or a clinically qualified artifact classifier.
internal sealed class PressurePulseTracker
{
    private const long ExpiryNs = 5_000_000_000;
    private sealed record Pulse(long Time, int Peak, int Trough);
    private Pulse[] _pulses = [];
    private long? _first, _lastPeak;
    private long _troughTime, _peakTime;
    private int _previous1, _previous2, _filterCount, _trough, _peak, _amplitude;
    private bool _initialized, _active;
    internal PressurePulseTracker Copy() => (PressurePulseTracker)MemberwiseClone();
    internal void Sample(long time, int raw)
    {
        _first ??= time;
        int value = _filterCount < 2 ? raw : Math.Max(Math.Min(raw, _previous1), Math.Min(Math.Max(raw, _previous1), _previous2));
        _previous2 = _previous1; _previous1 = raw; _filterCount = Math.Min(2, _filterCount + 1);
        if (!_initialized) { _initialized = true; _trough = value; _troughTime = time; return; }
        if (_lastPeak is { } last && time - last >= ExpiryNs) { _amplitude = 0; }
        int threshold = Math.Max(100, _amplitude / 3);
        if (!_active)
        {
            if (value <= _trough) { _trough = value; _troughTime = time; }
            if (value - _trough < threshold) { return; }
            _active = true; _peak = value; _peakTime = time;
        }
        if (value > _peak) { _peak = value; _peakTime = time; }
        if (time - _troughTime > 2_000_000_000)
        { _active = false; _trough = value; _troughTime = time; return; }
        if (_peak - value < threshold) { return; }
        int trough = _trough, amplitude = _peak - trough;
        long rise = _peakTime - _troughTime;
        _active = false; _trough = value; _troughTime = time;
        if (rise < 32_000_000 || amplitude < 200 || _lastPeak is { } previous && _peakTime - previous < 200_000_000) { return; }
        // The first peak may start in the middle of a pulse: use it only to arm
        // the detector, then retain complete trough-to-peak observations.
        bool contiguous = _lastPeak is { } old && _peakTime - old < ExpiryNs;
        _pulses = contiguous ? _pulses.Append(new Pulse(_peakTime, _peak, trough)).TakeLast(8).ToArray() : [];
        _lastPeak = _peakTime; _amplitude = amplitude;
    }
    internal PulsePressureReading Read(long time, WaveformMeasurementStatus inputStatus)
    {
        var pulses = _pulses.Where(p => time - p.Time <= 20_000_000_000).ToArray();
        var status = inputStatus != WaveformMeasurementStatus.Valid ? inputStatus :
            _first is { } first && time - (_lastPeak ?? first) >= ExpiryNs ? WaveformMeasurementStatus.Stale :
            pulses.Length == 0 ? WaveformMeasurementStatus.WarmingUp : WaveformMeasurementStatus.Valid;
        int? Mean(Func<Pulse, int> selector) => status == WaveformMeasurementStatus.Valid
            ? (int)FixedPointMath.RoundDivideTiesToEven(pulses.Sum(p => (long)selector(p)), pulses.Length) : null;
        return new(status, Mean(p => p.Peak), Mean(p => p.Trough), _lastPeak);
    }
}
