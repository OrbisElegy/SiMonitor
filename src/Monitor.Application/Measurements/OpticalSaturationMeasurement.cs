// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

// Dark/ambient-corrected, positive transmitted-light channels in linear units.
// Both wavelengths belong to the same sensor and sampling instant. A displayed
// single-channel, AC-only Pleth trace cannot supply this contract.
public sealed record OpticalSample(long SampleTimeNs, int Red, int Infrared, uint QualityFlags = 0);
public sealed record SaturationCalibrationPoint(int RatioPpm, int SaturationMilliPercent);
public sealed record OpticalSaturationReading(WaveformMeasurementStatus Status,
    int? SaturationMilliPercent, int? RatioPpm, long? MeasuredAtNs)
{
    // Teaching PI: IR peak-to-peak / window mean *100%, not vendor calibration.
    public int? PerfusionMilliPercent { get; init; }
    // iPM-style display convention, not a claim of vendor-calibrated PI.
    public bool IsQuestionable => Status == WaveformMeasurementStatus.Valid && PerfusionMilliPercent is < 300;
}

// A bounded-window engineering estimator, not a clinically qualified oximeter.
// Calibration is mandatory and caller-owned; there is no universal/default curve.
public sealed class OpticalSaturationMeasurement
{
    public const int WindowSamples = 500;
    public const long SampleStepNs = 8_000_000;
    private readonly SaturationCalibrationPoint[] _calibration;
    public string CalibrationId { get; }

    public OpticalSaturationMeasurement(string calibrationId, IReadOnlyList<SaturationCalibrationPoint> calibration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calibrationId);
        ArgumentNullException.ThrowIfNull(calibration);
        if (calibration.Count is < 2 or > 256) { throw new ArgumentException("Optical.InvalidCalibration", nameof(calibration)); }
        _calibration = calibration.ToArray();
        for (int i = 0; i < _calibration.Length; i++)
        {
            var point = _calibration[i];
            if (point is null || point.RatioPpm is < 1 or > 10_000_000 || point.SaturationMilliPercent is < 0 or > 100_000 ||
                i > 0 && (point.RatioPpm <= _calibration[i - 1].RatioPpm ||
                point.SaturationMilliPercent >= _calibration[i - 1].SaturationMilliPercent))
            { throw new ArgumentException("Optical.InvalidCalibration", nameof(calibration)); }
        }
        CalibrationId = calibrationId;
    }

    public OpticalSaturationReading Estimate(IReadOnlyList<OpticalSample> samples, long asOfSampleTimeNs)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(asOfSampleTimeNs);
        if (samples.Count > WindowSamples) { throw new ArgumentException("Optical.WindowTooLong", nameof(samples)); }
        long? last = null;
        BigInteger red = 0, infrared = 0, redSquares = 0, infraredSquares = 0, products = 0;
        bool poor = false;
        int irMinimum = int.MaxValue, irMaximum = int.MinValue, redMinimum = int.MaxValue, redMaximum = int.MinValue;
        foreach (var sample in samples)
        {
            if (sample is null || sample.SampleTimeNs < 0 || sample.SampleTimeNs > asOfSampleTimeNs ||
                last.HasValue && sample.SampleTimeNs - last.Value != SampleStepNs)
            { throw new ArgumentException("Optical.NonContiguousPairs", nameof(samples)); }
            last = sample.SampleTimeNs;
            poor |= sample.QualityFlags != 0 || sample.Red is <= 0 or >= 1_000_000 || sample.Infrared is <= 0 or >= 1_000_000;
            red += sample.Red; infrared += sample.Infrared;
            redSquares += (BigInteger)sample.Red * sample.Red;
            infraredSquares += (BigInteger)sample.Infrared * sample.Infrared;
            products += (BigInteger)sample.Red * sample.Infrared;
            redMinimum = Math.Min(redMinimum, sample.Red); redMaximum = Math.Max(redMaximum, sample.Red);
            irMinimum = Math.Min(irMinimum, sample.Infrared); irMaximum = Math.Max(irMaximum, sample.Infrared);
        }
        if (last is null || asOfSampleTimeNs - last.Value > 500_000_000)
        { return new(WaveformMeasurementStatus.NoData, null, null, last); }
        if (poor) { return new(WaveformMeasurementStatus.PoorSignal, null, null, last); }
        if (samples.Count < WindowSamples) { return new(WaveformMeasurementStatus.WarmingUp, null, null, last); }
        BigInteger redVariance = WindowSamples * redSquares - red * red;
        BigInteger infraredVariance = WindowSamples * infraredSquares - infrared * infrared;
        BigInteger covariance = WindowSamples * products - red * infrared;
        bool coherent = covariance > 0 && covariance * covariance * 100 >= redVariance * infraredVariance * 81;
        // A correlated runoff/drift is not pulsatile AC. Require a meaningful
        // excursion in both directions on each wavelength inside the same4s
        // window. Reversal must also span10% of the observed range so tiny
        // quantization ripples on a large runoff cannot qualify as pulsation.
        // This is a bounded quality gate, not a motion classifier.
        bool pulsatile = HasReversal(samples, true, Math.Max(Math.Max(1, (redMaximum - redMinimum + 9) / 10), (int)((red + WindowSamples * 20000 - 1) / (WindowSamples * 20000)))) &&
            HasReversal(samples, false, Math.Max(Math.Max(1, (irMaximum - irMinimum + 9) / 10), (int)((infrared + WindowSamples * 20000 - 1) / (WindowSamples * 20000))));
        OpticalSaturationReading Reading(WaveformMeasurementStatus status, int? saturation, int? ratio) => new(status, saturation, ratio, last)
        {
            PerfusionMilliPercent = coherent && pulsatile || redVariance == 0 && infraredVariance == 0
                ? (int)FixedPointMath.RoundDivideTiesToEven((Int128)(irMaximum - irMinimum) * WindowSamples * 100_000, (Int128)infrared) : null
        };
        // Engineering floor: AC rms/DC >=0.00005 on both channels and IR PI>=0.05%.
        // Low but coherent pulsatility can retain a reportable, qualified value.
        // These are explicit engineering quality gates, not motion rejection.
        if (redVariance * 400_000_000 < red * red || infraredVariance * 400_000_000 < infrared * infrared ||
            (BigInteger)(irMaximum - irMinimum) * WindowSamples * 2000 < infrared ||
            !coherent || !pulsatile)
        { return Reading(WaveformMeasurementStatus.PoorSignal, null, null); }
        // R = (ACrms(red)/DC(red)) / (ACrms(IR)/DC(IR)).
        // Integer square root avoids a platform-dependent floating point path.
        BigInteger squaredPpm = redVariance * infrared * infrared * 1_000_000_000_000L /
            (infraredVariance * red * red);
        BigInteger root = SquareRoot(squaredPpm);
        if (root < _calibration[0].RatioPpm || root > _calibration[^1].RatioPpm)
        { return Reading(WaveformMeasurementStatus.PoorSignal, null, null); }
        int ratio = (int)root;
        for (int i = 1; i < _calibration.Length; i++)
        {
            var a = _calibration[i - 1]; var b = _calibration[i];
            if (ratio > b.RatioPpm) { continue; }
            int saturation = a.SaturationMilliPercent + (int)FixedPointMath.RoundDivideTiesToEven(
                (Int128)(ratio - a.RatioPpm) * (b.SaturationMilliPercent - a.SaturationMilliPercent), b.RatioPpm - a.RatioPpm);
            return Reading(WaveformMeasurementStatus.Valid, saturation, ratio);
        }
        throw new InvalidOperationException("Optical.CalibrationCoverage");
    }

    private static bool HasReversal(IReadOnlyList<OpticalSample> samples, bool red, int threshold)
    {
        int initial = red ? samples[0].Red : samples[0].Infrared;
        int extreme = initial, direction = 0;
        foreach (var sample in samples)
        {
            int value = red ? sample.Red : sample.Infrared;
            if (direction == 0)
            {
                if (Math.Abs(value - initial) < threshold) { continue; }
                direction = value > initial ? 1 : -1; extreme = value;
            }
            else if (direction > 0)
            {
                if (extreme - value >= threshold) { return true; }
                extreme = Math.Max(extreme, value);
            }
            else
            {
                if (value - extreme >= threshold) { return true; }
                extreme = Math.Min(extreme, value);
            }
        }
        return false;
    }

    private static BigInteger SquareRoot(BigInteger value)
    {
        BigInteger low = 0, high = value + 1;
        while (high - low > 1)
        {
            BigInteger middle = (low + high) / 2;
            if (middle * middle <= value) { low = middle; } else { high = middle; }
        }
        return low;
    }
}
