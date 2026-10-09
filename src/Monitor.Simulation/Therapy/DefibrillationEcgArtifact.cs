// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Simulation.Therapy;

// An illustrative protected ECG front end, not a current-to-voltage calibration.
// The underlying discharge lasts milliseconds; the saturated front end takes longer
// to recover. Unusable samples remain visible but must not count as QRS/asystole.
public sealed class DefibrillationEcgArtifact
{
    public const uint UnusableQualityFlag = 1;
    public DefibrillationWaveform Discharge { get; }
    public long DeliveredAtSimTimeNs => Discharge.DeliveredAtSimTimeNs;
    public long RecoveryEndSimTimeNs { get; }

    public DefibrillationEcgArtifact(DefibrillationWaveformKind waveform, long deliveredAtSimTimeNs,
        int recoveryMilliseconds)
    {
        if (recoveryMilliseconds is < 100 or > 10000)
        { throw new ArgumentOutOfRangeException(nameof(recoveryMilliseconds)); }
        bool mono = waveform is DefibrillationWaveformKind.MonophasicDampedSine or DefibrillationWaveformKind.MonophasicTruncatedExponential;
        Discharge = DefibrillationWaveform.Create(new(waveform, 10_000, mono ? 10_000_000 : 8_000_000,
            mono ? 0 : 6_000_000), deliveredAtSimTimeNs);
        RecoveryEndSimTimeNs = checked(deliveredAtSimTimeNs + recoveryMilliseconds * 1_000_000L);
    }

    public bool Contains(long simTimeNs) => simTimeNs >= DeliveredAtSimTimeNs && simTimeNs < RecoveryEndSimTimeNs;

    public int EvaluateMicrovolts(long simTimeNs)
    {
        if (!Contains(simTimeNs)) { return 0; }
        if (simTimeNs < Discharge.EndSimTimeNs)
        {
            // Normalized coupling into a limited front end; no Joules -> ECG relation.
            int coupledMicrovolts = (int)((long)Discharge.EvaluateCurrentMilliamps(simTimeNs) * 12_000 / Discharge.Plan.PeakCurrentMilliamps);
            return Math.Clamp(coupledMicrovolts, -6000, 6000);
        }
        long elapsed = simTimeNs - Discharge.EndSimTimeNs;
        long settling = Math.Min(300_000_000, RecoveryEndSimTimeNs - Discharge.EndSimTimeNs);
        if (elapsed >= settling) { return 0; }
        long remaining = settling - elapsed;
        int polarity = Discharge.Plan.Kind is DefibrillationWaveformKind.MonophasicDampedSine or
            DefibrillationWaveformKind.MonophasicTruncatedExponential ? -1 : 1;
        return (int)((Int128)polarity * 6000 * remaining * remaining / settling / settling);
    }

    public WaveformPlane Apply(WaveformPlane plane, long blockStartSimTimeNs)
    {
        ArgumentNullException.ThrowIfNull(plane);
        long stepNs = 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
        if (blockStartSimTimeNs + plane.Samples.Count * stepNs <= DeliveredAtSimTimeNs || blockStartSimTimeNs >= RecoveryEndSimTimeNs)
        { return plane; }
        if (plane.ScaleNumerator != 1 || plane.ScaleDenominator != 1 || plane.OffsetNumerator != 0)
        { throw new ArgumentException("Defibrillation.EcgMicrovoltsRequired", nameof(plane)); }
        short[] samples = plane.Samples.ToArray();
        uint[] quality = new uint[samples.Length];
        foreach (var range in plane.QualityRanges)
        {
            for (uint i = range.FirstSampleOffset; i < range.FirstSampleOffset + range.Count; i++)
            { quality[i] = range.QualityFlags; }
        }
        for (int i = 0; i < samples.Length; i++)
        {
            long time = blockStartSimTimeNs + i * stepNs;
            if (!Contains(time)) { continue; }
            samples[i] = (short)EvaluateMicrovolts(time);
            quality[i] |= UnusableQualityFlag;
        }
        List<WaveformQualityRange> ranges = [];
        for (int start = 0; start < quality.Length;)
        {
            int end = start + 1;
            while (end < quality.Length && quality[end] == quality[start]) { end++; }
            if (quality[start] != 0) { ranges.Add(new((uint)start, (uint)(end - start), quality[start])); }
            start = end;
        }
        return plane with
        {
            Samples = Array.AsReadOnly(samples),
            QualityEncoding = ranges.Count == 0 ? WaveformQualityEncoding.None : WaveformQualityEncoding.Ranges,
            QualityRanges = ranges.AsReadOnly()
        };
    }
}
