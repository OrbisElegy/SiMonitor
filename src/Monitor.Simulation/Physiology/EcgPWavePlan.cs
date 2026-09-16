// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed record EcgPWaveComponents(int EarlyMicrovolts, int LateMicrovolts);

// Explicit overlapping rounded lobes, not a model of separate atrial chambers.
// Null entries retain the reference electrode P; zero pairs explicitly remove it.
public sealed record EcgPWavePlan(IReadOnlyList<EcgPWaveComponents?> Electrodes)
{
    public const string EvidenceId = "PWaveComponentsIllustrationDraft@1";

    internal IReadOnlyList<EventWaveformBand>?[] CreateBands(EcgCycleTiming timing)
    {
        timing.Validate();
        if (Electrodes is null || Electrodes.Count != 10) { throw Invalid(); }
        var components = Electrodes.ToArray();
        if (components.Any(item => item is not null &&
            (item.EarlyMicrovolts is < -4000 or > 4000 || item.LateMicrovolts is < -4000 or > 4000)))
        { throw Invalid(); }
        var result = new IReadOnlyList<EventWaveformBand>?[10];
        if (components.All(item => item is null)) { return result; }
        long delay = (long)FixedPointMath.RoundDivideTiesToEven((Int128)timing.PDurationNs * 3, 8);
        long duration = timing.PDurationNs - delay;
        if (delay <= 0 || duration <= delay) { throw Invalid(); }
        long referencePeak = TextbookEcgTables.P.Max();
        IReadOnlyList<long> Table(int amplitude) => Array.AsReadOnly(TextbookEcgTables.P.Select(value =>
            checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * amplitude * FixedPointMath.Q32One, referencePeak))).ToArray());
        for (int index = 0; index < components.Length; index++)
        {
            if (components[index] is not { } item) { continue; }
            result[index] = Array.AsReadOnly(new EventWaveformBand[]
            {
                new(PhysiologyCycleEventKind.AtrialElectrical, 0, duration, Table(item.EarlyMicrovolts)),
                new(PhysiologyCycleEventKind.AtrialElectrical, delay, duration, Table(item.LateMicrovolts)),
            });
        }
        return result;
    }

    private static EventWaveformException Invalid() => new("EcgPWave.InvalidPlan", "pWave");
}
