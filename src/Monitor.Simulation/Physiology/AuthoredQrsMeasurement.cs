// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum AuthoredQrsKind { Flat, RFirst, QThenR, QS }

public sealed record AuthoredQrsMeasurement(AuthoredQrsKind Kind, long QDurationNs,
    long QDepthQ32, long RPeakQ32, bool MeetsQIllustrationCriteria);

// A zero-baseline, complete, isolated QRS authoring window, not a detector
// for noisy acquired ECGs. Zero crossings use linear interpolation in ns.
public static class AuthoredQrsMeasurements
{
    public static AuthoredQrsMeasurement Measure(IReadOnlyList<long> samplesQ32, long stepNs)
    {
        if (samplesQ32 is null || samplesQ32.Count is < 3 or > 4097 || stepNs <= 0 ||
            (Int128)stepNs * (samplesQ32.Count - 1) > 2_000_000_000 ||
            samplesQ32[0] != 0 || samplesQ32[^1] != 0 ||
            samplesQ32.Any(v => v < -32767 * FixedPointMath.Q32One || v > 32767 * FixedPointMath.Q32One))
        { throw new EventWaveformException("EcgQrs.InvalidMeasurementWindow", "samplesQ32"); }
        long r = samplesQ32.Max();
        int first = 1;
        while (first < samplesQ32.Count && samplesQ32[first] == 0) { first++; }
        if (first == samplesQ32.Count) { return new(AuthoredQrsKind.Flat, 0, 0, 0, false); }
        if (samplesQ32[first] > 0) { return new(AuthoredQrsKind.RFirst, 0, 0, r, false); }
        long depth = -samplesQ32[first];
        int end = first + 1;
        while (samplesQ32[end] < 0) { depth = Math.Max(depth, -samplesQ32[end]); end++; }
        long crossing = (end - 1) * stepNs + checked((long)FixedPointMath.RoundDivideTiesToEven(
            -(Int128)samplesQ32[end - 1] * stepNs, (Int128)samplesQ32[end] - samplesQ32[end - 1]));
        long duration = crossing - (first - 1) * stepNs;
        if (r == 0) { return new(AuthoredQrsKind.QS, 0, -samplesQ32.Min(), 0, false); }
        return new(AuthoredQrsKind.QThenR, duration, depth, r,
            duration >= 30_000_000 && (Int128)depth * 4 >= r);
    }

    // The current source architecture anchors QRS bands at the ventricular
    // event (delay zero); ST/fusion, T, P and U are intentionally excluded.
    public static IReadOnlyList<AuthoredQrsMeasurement> Project(IReadOnlyList<ElectrodeWaveformPlan> electrodes,
        EcgLimbPlacement placement = EcgLimbPlacement.Standard)
    {
        var owned = ElectrodeWaveformComposition.Restore(new(electrodes, [], placement)).CaptureState().Electrodes;
        var qrs = owned.Select(e => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Where(b =>
            b.Trigger == PhysiologyCycleEventKind.VentricularElectrical && b.DelayNs == 0).ToArray())
        }).ToArray();
        if (qrs.Any(e => e.Bands.Count == 0))
        { throw new EventWaveformException("EcgQrs.MissingQrsBands", "electrodes"); }
        long duration = qrs.SelectMany(e => e.Bands).Max(b => b.DurationNs);
        const long step = 1_000_000;
        if (qrs.SelectMany(e => e.Bands).Any(b => b.DurationNs < 2 * step))
        { throw new EventWaveformException("EcgQrs.InsufficientResolution", "electrodes"); }
        if (duration > 2_000_000_000)
        { throw new EventWaveformException("EcgQrs.InvalidMeasurementWindow", "electrodes"); }
        int count = checked((int)((duration + step - 1) / step) + 1);
        long[][] samples = Enumerable.Range(0, 12).Select(_ => new long[count]).ToArray();
        var source = ElectrodeWaveformComposition.Restore(new(qrs,
            [new(0, PhysiologyCycleEventKind.VentricularElectrical, 0)], placement));
        for (int index = 0; index < count; index++)
        {
            var leads = source.EvaluateAt(index * step).Leads;
            for (int lead = 0; lead < 12; lead++) { samples[lead][index] = leads[(EcgLead)lead].ToQ32(); }
        }
        return Array.AsReadOnly(samples.Select(s => Measure(s, step)).ToArray());
    }
}
