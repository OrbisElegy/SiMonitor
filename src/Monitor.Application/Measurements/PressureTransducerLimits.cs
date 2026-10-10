// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Measurements;

// Numerical reporting limits, independent of patient pressure/alarm thresholds.
public sealed record PressureTransducerLimits(int AbpMinimumCentiMmHg = -5000,
    int PaMinimumCentiMmHg = -5000, int CvpMinimumCentiMmHg = -5000,
    int AbpMinimumPulseCentiMmHg = 300, int PaMinimumPulseCentiMmHg = 300)
{
    public void Validate()
    {
        if (AbpMinimumCentiMmHg is < -10000 or > 40000 || PaMinimumCentiMmHg is < -10000 or > 40000 ||
            CvpMinimumCentiMmHg is < -10000 or > 40000 ||
            AbpMinimumPulseCentiMmHg is < 200 or > 10000 || PaMinimumPulseCentiMmHg is < 200 or > 10000)
        { throw new ArgumentException("PressureTransducer.InvalidLimits"); }
    }

    public LiveMeasurementSnapshot Apply(LiveMeasurementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate();
        return snapshot with
        {
            AbpMean = Apply(snapshot.AbpMean, AbpMinimumCentiMmHg, AbpMinimumPulseCentiMmHg),
            PaMean = Apply(snapshot.PaMean, PaMinimumCentiMmHg, PaMinimumPulseCentiMmHg),
            CvpMean = Apply(snapshot.CvpMean, CvpMinimumCentiMmHg, null)
        };
    }

    private static MeanPressureReading Apply(MeanPressureReading reading, int minimum, int? minimumPulse)
    {
        var pulse = reading.Pulse;
        if (pulse is { Status: WaveformMeasurementStatus.Valid } &&
            (pulse.SystolicCentiMmHg < minimum || pulse.DiastolicCentiMmHg < minimum))
        { pulse = pulse with { Status = WaveformMeasurementStatus.OutOfRange, SystolicCentiMmHg = null, DiastolicCentiMmHg = null }; }
        // Use the latest complete measured pulse, not the eight-pulse average:
        // historical strong beats must not conceal newly weak pulsation.
        if (reading.Status == WaveformMeasurementStatus.Valid &&
            pulse is { Status: WaveformMeasurementStatus.Valid, LatestAmplitudeCentiMmHg: { } amplitude } &&
            amplitude < minimumPulse)
        { pulse = pulse with { Status = WaveformMeasurementStatus.PoorSignal, SystolicCentiMmHg = null, DiastolicCentiMmHg = null }; }
        return reading.Status == WaveformMeasurementStatus.Valid && reading.MeanCentiMmHg < minimum
            ? reading with { Status = WaveformMeasurementStatus.OutOfRange, MeanCentiMmHg = null, BelowRangeUpperBoundCentiMmHg = minimum, Pulse = pulse }
            : reading with { Pulse = pulse };
    }
}
