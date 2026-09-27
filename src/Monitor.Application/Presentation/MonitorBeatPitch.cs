// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Application.Presentation;

// Local measured pitch projection, independent of beat timing and volume.
public sealed class MonitorBeatPitch
{
    private long? _lastMeasurement;
    private int? _filtered;
    public int SaturationPercent { get; private set; } = 97;
    public bool Unavailable { get; private set; }
    public void Reset() { _lastMeasurement = null; _filtered = null; SaturationPercent = 97; Unavailable = false; }
    public void Update(OpticalSaturationReading? reading, long nowNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nowNs);
        if (reading is not { Status: WaveformMeasurementStatus.Valid, SaturationMilliPercent: >= 0 and <= 100000, MeasuredAtNs: { } at } ||
            at < 0 || at > nowNs || nowNs - at > 5_000_000_000)
        { Reset(); Unavailable = true; return; }
        int value = Math.Clamp(reading.SaturationMilliPercent.Value, 70000, 97000);
        if (_lastMeasurement is { } last && at < last) { Reset(); }
        if (_lastMeasurement == at) { return; }
        long elapsed = _lastMeasurement is { } previous ? Math.Min(at - previous, 5_000_000_000) : 0;
        if (_filtered is not { } filtered || Math.Abs(value - filtered) >= 5000)
        { _filtered = value; SaturationPercent = (value + 500) / 1000; }
        else
        {
            _filtered = filtered + (int)((value - filtered) * elapsed / (500_000_000 + elapsed));
            if (Math.Abs(_filtered.Value - SaturationPercent * 1000) >= 750)
            { SaturationPercent = Math.Clamp((_filtered.Value + 500) / 1000, 70, 97); }
        }
        _lastMeasurement = at; Unavailable = false;
    }
}
